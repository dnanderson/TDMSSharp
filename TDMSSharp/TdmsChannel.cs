using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace TdmsSharp
{
    /// <summary>
    /// Represents a TDMS channel object
    /// </summary>
    public class TdmsChannel : TdmsObject
    {
        private readonly List<byte> _dataBuffer = new();
        private readonly TdmsDataType _dataType;
        private readonly RawDataIndex _rawDataIndex;
        private bool _hasDataToWrite = false;
        private ulong _totalValuesWritten = 0;

        public string Name { get; }
        public string GroupName { get; }
        public TdmsDataType DataType => _dataType;
        public bool HasDataToWrite => _hasDataToWrite;

        internal RawDataIndex RawDataIndex => _rawDataIndex;
        internal byte[] DataBuffer => _dataBuffer.ToArray();

        public TdmsChannel(string groupName, string name, TdmsDataType dataType)
        {
            if (string.IsNullOrEmpty(groupName))
                throw new ArgumentException("Group name cannot be null or empty", nameof(groupName));
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Channel name cannot be null or empty", nameof(name));

            GroupName = groupName;
            Name = name;
            _dataType = dataType;

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
            _dataBuffer.AddRange(bytes.ToArray());
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

            // Calculate cumulative offsets (end positions) and concatenate strings
            var offsets = new List<uint>();
            var concatenated = new List<byte>();
            uint cumulativeOffset = 0;

            foreach (var str in values)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(str ?? string.Empty);
                concatenated.AddRange(bytes);
                cumulativeOffset += (uint)bytes.Length;
                offsets.Add(cumulativeOffset);  // FIXED: Add AFTER processing, showing end position
            }

            // Write offsets first (each offset points to the END of its corresponding string)
            foreach (var offset in offsets)
            {
                _dataBuffer.AddRange(BitConverter.GetBytes(offset));
            }

            // Write concatenated strings
            _dataBuffer.AddRange(concatenated);

            _rawDataIndex.NumberOfValues += (ulong)values.Length;
            
            // FIXED: Total size = (number of offsets × 4 bytes) + total string bytes
            _rawDataIndex.TotalSizeInBytes = (ulong)(values.Length * 4 + concatenated.Count);
            _hasDataToWrite = true;
        }

        internal void ClearDataBuffer()
        {
            _dataBuffer.Clear();
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
                _rawDataIndex.HasChanged = true;
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
            byte[] bytes = _dataType switch
            {
                TdmsDataType.I8 => new[] { (byte)(sbyte)(object)value },
                TdmsDataType.I16 => BitConverter.GetBytes((short)(object)value),
                TdmsDataType.I32 => BitConverter.GetBytes((int)(object)value),
                TdmsDataType.I64 => BitConverter.GetBytes((long)(object)value),
                TdmsDataType.U8 => new[] { (byte)(object)value },
                TdmsDataType.U16 => BitConverter.GetBytes((ushort)(object)value),
                TdmsDataType.U32 => BitConverter.GetBytes((uint)(object)value),
                TdmsDataType.U64 => BitConverter.GetBytes((ulong)(object)value),
                TdmsDataType.SingleFloat => BitConverter.GetBytes((float)(object)value),
                TdmsDataType.DoubleFloat => BitConverter.GetBytes((double)(object)value),
                TdmsDataType.Boolean => new[] { (byte)((bool)(object)value ? 1 : 0) },
                TdmsDataType.TimeStamp => GetTimestampBytes((TdmsTimestamp)(object)value),
                _ => throw new NotSupportedException($"Data type {_dataType} is not supported")
            };

            _dataBuffer.AddRange(bytes);
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
    }
}