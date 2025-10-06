using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.IO;


namespace TdmsSharp
{
    /// <summary>
    /// Represents a TDMS channel object
    /// </summary>
    public class TdmsChannel : TdmsObject, IDisposable
    {
        // Use RecyclableMemoryStream instead of MemoryStream
        private RecyclableMemoryStream _dataBuffer;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private bool _disposed = false;

        private readonly TdmsDataType _dataType;
        private readonly RawDataIndex _rawDataIndex;
        private bool _hasDataToWrite = false;
        private ulong _totalValuesWritten = 0;
        private ulong _lastWrittenNumberOfValues = ulong.MaxValue;

        public string Name { get; }
        public string GroupName { get; }
        public TdmsDataType DataType => _dataType;
        public bool HasDataToWrite => _hasDataToWrite;

        internal RawDataIndex RawDataIndex => _rawDataIndex;

        internal ReadOnlyMemory<byte> GetDataBuffer()
        {
            return _dataBuffer.GetBuffer().AsMemory(0, (int)_dataBuffer.Length);
        }

        public TdmsChannel(string groupName, string name, TdmsDataType dataType, RecyclableMemoryStreamManager memoryStreamManager)
        {
            if (string.IsNullOrEmpty(groupName))
                throw new ArgumentException("Group name cannot be null or empty", nameof(groupName));
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Channel name cannot be null or empty", nameof(name));

            GroupName = groupName;
            Name = name;
            _dataType = dataType;
            _memoryStreamManager = memoryStreamManager;
            // Get a stream from the manager
            _dataBuffer = (RecyclableMemoryStream)_memoryStreamManager.GetStream();

            var escapedGroup = groupName.Replace("'", "''");
            var escapedName = name.Replace("'", "''");
            Path = $"/'{escapedGroup}'/'{escapedName}'";

            _rawDataIndex = new RawDataIndex
            {
                DataType = dataType,
                ArrayDimension = 1,
                NumberOfValues = 0,
                TotalSizeInBytes = 0
            };
        }

        /// <summary>
        /// Write a single value to the channel
        /// </summary>
        public void WriteValue<T>(T value) where T : notnull
        {
            ValidateDataType<T>();
            WriteValueInternal(value);
            _hasDataToWrite = true;
        }

        /// <summary>
        /// Write multiple values to the channel
        /// </summary>
        public void WriteValues<T>(T[] values) where T : notnull
        {
            if (values == null || values.Length == 0)
                return;

            ValidateDataType<T>();
            foreach (var value in values)
            {
                WriteValueInternal(value);
            }
            _hasDataToWrite = true;
        }

        /// <summary>
        /// Write multiple values to the channel (span version for performance)
        /// </summary>
        public void WriteValues<T>(ReadOnlySpan<T> values) where T : unmanaged
        {
            if (values.Length == 0)
                return;

            ValidateDataType<T>();

            // For unmanaged types, we can do a fast bulk copy
            var bytes = MemoryMarshal.AsBytes(values);
            _dataBuffer.Write(bytes);
            _rawDataIndex.NumberOfValues += (ulong)values.Length;
            _rawDataIndex.TotalSizeInBytes += (ulong)bytes.Length;
            _hasDataToWrite = true;
        }

        /// <summary>
        /// Write string values (special handling required)
        /// FIXED: Offsets are now cumulative END positions
        /// </summary>
        public void WriteStrings(string[] values)
        {
            if (_dataType != TdmsDataType.String)
                throw new InvalidOperationException("Channel data type must be String");

            if (values == null || values.Length == 0)
                return;

            // Use a temporary RecyclableMemoryStream for string data
            using (var stringStream = (RecyclableMemoryStream)_memoryStreamManager.GetStream())
            {
                var offsets = new List<uint>();
                uint cumulativeOffset = 0;

                foreach (var str in values)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(str ?? string.Empty);
                    stringStream.Write(bytes);
                    cumulativeOffset += (uint)bytes.Length;
                    offsets.Add(cumulativeOffset);
                }

                foreach (var offset in offsets)
                {
                    _dataBuffer.Write(BitConverter.GetBytes(offset));
                }

                stringStream.Position = 0;
                stringStream.CopyTo(_dataBuffer);

                _rawDataIndex.NumberOfValues += (ulong)values.Length;
                _rawDataIndex.TotalSizeInBytes = (ulong)(values.Length * 4 + stringStream.Length);
                _hasDataToWrite = true;
            }
        }

        internal void ClearDataBuffer()
        {
            if (_hasDataToWrite)
            {
                _lastWrittenNumberOfValues = _rawDataIndex.NumberOfValues;
            }
            // Dispose the stream to return it to the pool
            _dataBuffer.Dispose();
            // Get a new stream for the next set of data
            _dataBuffer = (RecyclableMemoryStream)_memoryStreamManager.GetStream();

            _totalValuesWritten += _rawDataIndex.NumberOfValues;
            _rawDataIndex.NumberOfValues = 0;
            _rawDataIndex.TotalSizeInBytes = 0;
            _hasDataToWrite = false;
            _rawDataIndex.HasChanged = false;
        }

        internal void UpdateRawDataIndex()
        {
            if (_hasDataToWrite)
            {
                // The index has changed if the number of values is different from the last write.
                // It's always considered changed for the first segment a channel is written to.
                if (_rawDataIndex.NumberOfValues != _lastWrittenNumberOfValues)
                {
                    _rawDataIndex.HasChanged = true;
                }
                else
                {
                    _rawDataIndex.HasChanged = false;
                }
            }
        }

        public override void ResetModifiedFlags()
        {
            base.ResetModifiedFlags();
            _rawDataIndex.HasChanged = false;
            _rawDataIndex.MatchesPrevious = false;
        }

        private void ValidateDataType<T>()
        {
            var expectedType = typeof(T) switch
            {
                Type t when t == typeof(sbyte) => TdmsDataType.I8,
                Type t when t == typeof(short) => TdmsDataType.I16,
                Type t when t == typeof(int) => TdmsDataType.I32,
                Type t when t == typeof(long) => TdmsDataType.I64,
                Type t when t == typeof(byte) => TdmsDataType.U8,
                Type t when t == typeof(ushort) => TdmsDataType.U16,
                Type t when t == typeof(uint) => TdmsDataType.U32,
                Type t when t == typeof(ulong) => TdmsDataType.U64,
                Type t when t == typeof(float) => TdmsDataType.SingleFloat,
                Type t when t == typeof(double) => TdmsDataType.DoubleFloat,
                Type t when t == typeof(bool) => TdmsDataType.Boolean,
                Type t when t == typeof(TdmsTimestamp) => TdmsDataType.TimeStamp,
                Type t when t == typeof(string) => TdmsDataType.String,
                _ => throw new NotSupportedException($"Type {typeof(T)} is not supported")
            };

            if (expectedType != _dataType)
                throw new InvalidOperationException($"Expected data type {_dataType} but got {expectedType}");
        }

        private void WriteValueInternal<T>(T value)
        {
            // This method is now less efficient due to boxing and allocation.
            // Keeping for single value writes, but bulk writes are preferred.
            byte[] bytes;
            switch (_dataType)
            {
                case TdmsDataType.I8: bytes = new[] { (byte)(sbyte)(object)value }; break;
                case TdmsDataType.I16: bytes = BitConverter.GetBytes((short)(object)value); break;
                case TdmsDataType.I32: bytes = BitConverter.GetBytes((int)(object)value); break;
                case TdmsDataType.I64: bytes = BitConverter.GetBytes((long)(object)value); break;
                case TdmsDataType.U8: bytes = new[] { (byte)(object)value }; break;
                case TdmsDataType.U16: bytes = BitConverter.GetBytes((ushort)(object)value); break;
                case TdmsDataType.U32: bytes = BitConverter.GetBytes((uint)(object)value); break;
                case TdmsDataType.U64: bytes = BitConverter.GetBytes((ulong)(object)value); break;
                case TdmsDataType.SingleFloat: bytes = BitConverter.GetBytes((float)(object)value); break;
                case TdmsDataType.DoubleFloat: bytes = BitConverter.GetBytes((double)(object)value); break;
                case TdmsDataType.Boolean: bytes = new[] { (byte)((bool)(object)value ? 1 : 0) }; break;
                case TdmsDataType.TimeStamp: bytes = GetTimestampBytes((TdmsTimestamp)(object)value); break;
                default: throw new NotSupportedException($"Data type {_dataType} is not supported");
            }
            _dataBuffer.Write(bytes);
            _rawDataIndex.NumberOfValues++;
            _rawDataIndex.TotalSizeInBytes += (ulong)bytes.Length;
        }

        private byte[] GetTimestampBytes(TdmsTimestamp timestamp)
        {
            // Little-endian: Fractions (u64) then Seconds (i64)
            var bytes = new byte[16];
            BitConverter.GetBytes(timestamp.Fractions).CopyTo(bytes, 0);
            BitConverter.GetBytes(timestamp.Seconds).CopyTo(bytes, 8);
            return bytes;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _dataBuffer?.Dispose();
            }

            _disposed = true;
        }
    }
}