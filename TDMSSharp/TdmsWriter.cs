using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TDMSSharp
{
    /// <summary>
    /// High-performance TDMS file writer with full optimization support
    /// </summary>
    public sealed class TdmsWriter : IDisposable, IAsyncDisposable
    {
        private readonly Stream _dataStream;
        private readonly Stream? _indexStream;
        private readonly bool _ownsStreams;
        private readonly int _version;
        private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

        private bool _rootWritten;
        private readonly HashSet<string> _writtenGroups = new();
        private readonly Dictionary<string, RawDataIndex> _previousIndexes = new();
        private readonly List<TdmsObject> _pendingObjects = new();
        private readonly MemoryStream _metadataBuffer = new();
        private readonly MemoryStream _rawDataBuffer = new();

        private long _currentSegmentStart;
        private bool _disposed;

        public TdmsWriter(string filePath, TdmsWriterOptions? options = null)
            : this(CreateFileStream(filePath), CreateIndexStream(filePath, options), true, options)
        {
        }

        public TdmsWriter(Stream dataStream, Stream? indexStream = null, bool ownsStreams = false, TdmsWriterOptions? options = null)
        {
            _dataStream = dataStream ?? throw new ArgumentNullException(nameof(dataStream));
            _indexStream = indexStream;
            _ownsStreams = ownsStreams;
            _version = options?.Version ?? 4713;

            if (_version != 4712 && _version != 4713)
                throw new ArgumentException($"Invalid TDMS version: {_version}. Must be 4712 or 4713.");
        }

        private static FileStream CreateFileStream(string filePath)
        {
            return new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        }

        private static FileStream? CreateIndexStream(string filePath, TdmsWriterOptions? options)
        {
            if (options?.CreateIndexFile != true)
                return null;

            var indexPath = filePath + "_index";
            return new FileStream(indexPath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        }

        /// <summary>
        /// Begin a new segment. Call this before writing objects to optimize segment structure.
        /// </summary>
        public void BeginSegment()
        {
            if (_pendingObjects.Count > 0)
                throw new InvalidOperationException("Cannot begin new segment with pending objects. Call WriteSegment first.");

            _currentSegmentStart = _dataStream.Position;
        }

        /// <summary>
        /// Write root object properties
        /// </summary>
        public void WriteRoot(IDictionary<string, object>? properties = null)
        {
            _pendingObjects.Add(new TdmsRoot(properties));
            _rootWritten = true;
        }

        /// <summary>
        /// Write a group object
        /// </summary>
        public void WriteGroup(string groupName, IDictionary<string, object>? properties = null)
        {
            if (string.IsNullOrEmpty(groupName))
                throw new ArgumentException("Group name cannot be null or empty", nameof(groupName));

            _pendingObjects.Add(new TdmsGroup(groupName, properties));
            _writtenGroups.Add(groupName);
        }

        /// <summary>
        /// Write channel data with optimal performance
        /// </summary>
        public void WriteChannel<T>(string groupName, string channelName, ReadOnlySpan<T> data, IDictionary<string, object>? properties = null)
            where T : unmanaged
        {
            if (string.IsNullOrEmpty(groupName))
                throw new ArgumentException("Group name cannot be null or empty", nameof(groupName));
            if (string.IsNullOrEmpty(channelName))
                throw new ArgumentException("Channel name cannot be null or empty", nameof(channelName));

            // Ensure group exists
            if (!_writtenGroups.Contains(groupName))
            {
                _pendingObjects.Add(new TdmsGroup(groupName, null));
                _writtenGroups.Add(groupName);
            }

            var channel = new TdmsChannel<T>(groupName, channelName, data.ToArray(), properties);
            _pendingObjects.Add(channel);
        }

        /// <summary>
        /// Write channel data for strings
        /// </summary>
        public void WriteStringChannel(string groupName, string channelName, ReadOnlySpan<string> data, IDictionary<string, object>? properties = null)
        {
            if (string.IsNullOrEmpty(groupName))
                throw new ArgumentException("Group name cannot be null or empty", nameof(groupName));
            if (string.IsNullOrEmpty(channelName))
                throw new ArgumentException("Channel name cannot be null or empty", nameof(channelName));

            // Ensure group exists
            if (!_writtenGroups.Contains(groupName))
            {
                _pendingObjects.Add(new TdmsGroup(groupName, null));
                _writtenGroups.Add(groupName);
            }

            var channel = new TdmsStringChannel(groupName, channelName, data.ToArray(), properties);
            _pendingObjects.Add(channel);
        }

        /// <summary>
        /// Flush pending objects as a segment
        /// </summary>
        public void WriteSegment()
        {
            if (_pendingObjects.Count == 0)
                return;

            // Ensure root object exists
            if (!_rootWritten && !_pendingObjects.Any(o => o is TdmsRoot))
            {
                _pendingObjects.Insert(0, new TdmsRoot(null));
                _rootWritten = true;
            }

            // Sort objects: root first, then groups, then channels
            _pendingObjects.Sort((a, b) => GetObjectOrder(a).CompareTo(GetObjectOrder(b)));

            WriteSegmentInternal(_dataStream, false);

            if (_indexStream != null)
            {
                WriteSegmentInternal(_indexStream, true);
            }

            _pendingObjects.Clear();
        }

        private void WriteSegmentInternal(Stream stream, bool isIndex)
        {
            _metadataBuffer.SetLength(0);
            _rawDataBuffer.SetLength(0);

            // Write metadata
            WriteMetadata(_metadataBuffer);

            // Write raw data (only for data file, not index)
            long rawDataSize = 0;
            if (!isIndex)
            {
                rawDataSize = WriteRawData(_rawDataBuffer);
            }

            // Write lead-in
            var toc = TocFlags.MetaData | TocFlags.NewObjList;
            if (rawDataSize > 0)
                toc |= TocFlags.RawData;

            WriteLeadIn(stream, toc, _metadataBuffer.Length, rawDataSize, isIndex);

            // Write metadata
            _metadataBuffer.Position = 0;
            _metadataBuffer.CopyTo(stream);

            // Write raw data
            if (!isIndex && rawDataSize > 0)
            {
                _rawDataBuffer.Position = 0;
                _rawDataBuffer.CopyTo(stream);
            }

            stream.Flush();
        }

        private void WriteLeadIn(Stream stream, TocFlags toc, long metadataSize, long rawDataSize, bool isIndex)
        {
            Span<byte> buffer = stackalloc byte[28];

            // TDSm or TDSh tag
            if (isIndex)
            {
                buffer[0] = (byte)'T';
                buffer[1] = (byte)'D';
                buffer[2] = (byte)'S';
                buffer[3] = (byte)'h';
            }
            else
            {
                buffer[0] = (byte)'T';
                buffer[1] = (byte)'D';
                buffer[2] = (byte)'S';
                buffer[3] = (byte)'m';
            }

            // ToC mask
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4), (int)toc);

            // Version
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(8), _version);

            // Next segment offset
            var nextSegmentOffset = metadataSize + rawDataSize;
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(12), (ulong)nextSegmentOffset);

            // Raw data offset
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(20), (ulong)metadataSize);

            stream.Write(buffer);
        }

        private void WriteMetadata(Stream stream)
        {
            // Number of objects
            WriteInt32(stream, _pendingObjects.Count);

            foreach (var obj in _pendingObjects)
            {
                // Object path
                WriteString(stream, obj.Path);

                // Raw data index
                WriteRawDataIndex(stream, obj);

                // Properties
                WriteProperties(stream, obj.Properties);
            }
        }

        private void WriteRawDataIndex(Stream stream, TdmsObject obj)
        {
            if (!obj.HasData)
            {
                // No raw data
                WriteInt32(stream, -1);
                return;
            }

            var currentIndex = obj.GetRawDataIndex();
            var path = obj.Path;

            // Check if index matches previous segment
            if (_previousIndexes.TryGetValue(path, out var prevIndex) && prevIndex.Equals(currentIndex))
            {
                // Same index as previous segment
                WriteInt32(stream, 0);
            }
            else
            {
                // New index
                WriteInt32(stream, 20); // Index length
                WriteInt32(stream, (int)currentIndex.DataType);
                WriteUInt32(stream, 1); // Array dimension
                WriteUInt64(stream, currentIndex.NumberOfValues);

                // For strings, also write total size
                if (currentIndex.DataType == TdsDataType.String)
                {
                    WriteUInt64(stream, currentIndex.TotalSize);
                }

                _previousIndexes[path] = currentIndex;
            }
        }

        private long WriteRawData(Stream stream)
        {
            long totalSize = 0;

            foreach (var obj in _pendingObjects)
            {
                if (obj.HasData)
                {
                    totalSize += obj.WriteData(stream);
                }
            }

            return totalSize;
        }

        private void WriteProperties(Stream stream, IDictionary<string, object>? properties)
        {
            if (properties == null || properties.Count == 0)
            {
                WriteInt32(stream, 0);
                return;
            }

            WriteInt32(stream, properties.Count);

            foreach (var kvp in properties)
            {
                WriteString(stream, kvp.Key);
                WritePropertyValue(stream, kvp.Value);
            }
        }

        private void WritePropertyValue(Stream stream, object value)
        {
            var (dataType, bytes) = TdmsValue.ToBytes(value);
            WriteInt32(stream, (int)dataType);
            stream.Write(bytes);
        }

        private static int GetObjectOrder(TdmsObject obj) => obj switch
        {
            TdmsRoot => 0,
            TdmsGroup => 1,
            _ => 2
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteInt32(Stream stream, int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            stream.Write(buffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteUInt32(Stream stream, uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            stream.Write(buffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteUInt64(Stream stream, ulong value)
        {
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
            stream.Write(buffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteString(Stream stream, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            WriteInt32(stream, bytes.Length);
            stream.Write(bytes);
        }

        public void Dispose()
        {
            if (_disposed) return;

            WriteSegment(); // Flush any pending data

            if (_ownsStreams)
            {
                _dataStream?.Dispose();
                _indexStream?.Dispose();
            }

            _metadataBuffer?.Dispose();
            _rawDataBuffer?.Dispose();

            _disposed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;

            WriteSegment(); // Flush any pending data

            if (_ownsStreams)
            {
                if (_dataStream != null)
                    await _dataStream.DisposeAsync();
                if (_indexStream != null)
                    await _indexStream.DisposeAsync();
            }

            _metadataBuffer?.Dispose();
            _rawDataBuffer?.Dispose();

            _disposed = true;
        }
    }

    /// <summary>
    /// Writer options
    /// </summary>
    public class TdmsWriterOptions
    {
        public int Version { get; set; } = 4713;
        public bool CreateIndexFile { get; set; } = false;
        public int BufferSize { get; set; } = 65536;
    }

    /// <summary>
    /// Base class for TDMS objects
    /// </summary>
    internal abstract class TdmsObject
    {
        public abstract string Path { get; }
        public IDictionary<string, object>? Properties { get; protected set; }
        public virtual bool HasData => false;

        public virtual RawDataIndex GetRawDataIndex() => throw new NotSupportedException();
        public virtual long WriteData(Stream stream) => 0;
    }

    /// <summary>
    /// Root TDMS object
    /// </summary>
    internal class TdmsRoot : TdmsObject
    {
        public override string Path => "/";

        public TdmsRoot(IDictionary<string, object>? properties)
        {
            Properties = properties;
        }
    }

    /// <summary>
    /// Group TDMS object
    /// </summary>
    internal class TdmsGroup : TdmsObject
    {
        private readonly string _name;
        public override string Path => $"/'{_name.Replace("'", "''")}'";

        public TdmsGroup(string name, IDictionary<string, object>? properties)
        {
            _name = name;
            Properties = properties;
        }
    }

    /// <summary>
    /// Channel TDMS object
    /// </summary>
    internal class TdmsChannel<T> : TdmsObject where T : unmanaged
    {
        private readonly string _group;
        private readonly string _channel;
        private readonly T[] _data;

        public override string Path => $"/'{_group.Replace("'", "''")}'/'{_channel.Replace("'", "''")}'";
        public override bool HasData => true;

        public TdmsChannel(string group, string channel, T[] data, IDictionary<string, object>? properties)
        {
            _group = group;
            _channel = channel;
            _data = data;
            Properties = properties;
        }

        public override RawDataIndex GetRawDataIndex()
        {
            var dataType = GetDataType();
            return new RawDataIndex
            {
                DataType = dataType,
                NumberOfValues = (ulong)_data.Length,
                TotalSize = (ulong)(_data.Length * Unsafe.SizeOf<T>())
            };
        }

        public override long WriteData(Stream stream)
        {
            var bytes = MemoryMarshal.AsBytes(_data.AsSpan());
            stream.Write(bytes);
            return bytes.Length;
        }

        private TdsDataType GetDataType()
        {
            return typeof(T) switch
            {
                Type t when t == typeof(sbyte) => TdsDataType.I8,
                Type t when t == typeof(short) => TdsDataType.I16,
                Type t when t == typeof(int) => TdsDataType.I32,
                Type t when t == typeof(long) => TdsDataType.I64,
                Type t when t == typeof(byte) => TdsDataType.U8,
                Type t when t == typeof(ushort) => TdsDataType.U16,
                Type t when t == typeof(uint) => TdsDataType.U32,
                Type t when t == typeof(ulong) => TdsDataType.U64,
                Type t when t == typeof(float) => TdsDataType.SingleFloat,
                Type t when t == typeof(double) => TdsDataType.DoubleFloat,
                Type t when t == typeof(bool) => TdsDataType.Boolean,
                _ => throw new NotSupportedException($"Type {typeof(T)} is not supported")
            };
        }
    }

    /// <summary>
    /// String channel TDMS object
    /// </summary>
    internal class TdmsStringChannel : TdmsObject
    {
        private readonly string _group;
        private readonly string _channel;
        private readonly string[] _data;

        public override string Path => $"/'{_group.Replace("'", "''")}'/'{_channel.Replace("'", "''")}'";
        public override bool HasData => true;

        public TdmsStringChannel(string group, string channel, string[] data, IDictionary<string, object>? properties)
        {
            _group = group;
            _channel = channel;
            _data = data;
            Properties = properties;
        }

        public override RawDataIndex GetRawDataIndex()
        {
            ulong totalSize = 0;
            foreach (var str in _data)
            {
                totalSize += 4 + (ulong)Encoding.UTF8.GetByteCount(str);
            }

            return new RawDataIndex
            {
                DataType = TdsDataType.String,
                NumberOfValues = (ulong)_data.Length,
                TotalSize = totalSize
            };
        }

        public override long WriteData(Stream stream)
        {
            long totalWritten = 0;
            var offsets = new uint[_data.Length];
            var encodedStrings = new List<byte[]>(_data.Length); // Store encoded strings
            uint currentOffset = 0;

            // 1. Single pass to encode strings and calculate offsets
            for (int i = 0; i < _data.Length; i++)
            {
                var bytes = Encoding.UTF8.GetBytes(_data[i]);
                encodedStrings.Add(bytes);
                currentOffset += (uint)bytes.Length;
                offsets[i] = currentOffset;
            }

            // 2. Write offsets (with buffer allocated ONCE)
            Span<byte> offsetBuffer = stackalloc byte[4];
            foreach (var offset in offsets)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(offsetBuffer, offset);
                stream.Write(offsetBuffer);
            }
            totalWritten += (long)offsets.Length * 4;

            // 3. Write the pre-encoded strings
            foreach (var bytes in encodedStrings)
            {
                stream.Write(bytes);
            }
            totalWritten += currentOffset; // currentOffset is the total length of all strings

            return totalWritten;
        }
    }

    /// <summary>
    /// Raw data index information
    /// </summary>
    internal struct RawDataIndex : IEquatable<RawDataIndex>
    {
        public TdsDataType DataType { get; set; }
        public ulong NumberOfValues { get; set; }
        public ulong TotalSize { get; set; }

        public bool Equals(RawDataIndex other)
        {
            return DataType == other.DataType &&
                   NumberOfValues == other.NumberOfValues &&
                   TotalSize == other.TotalSize;
        }

        public override bool Equals(object? obj) => obj is RawDataIndex other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(DataType, NumberOfValues, TotalSize);
    }

    /// <summary>
    /// TDMS value conversion utilities
    /// </summary>
    internal static class TdmsValue
    {
        public static (TdsDataType type, byte[] bytes) ToBytes(object value)
        {
            return value switch
            {
                bool b => (TdsDataType.Boolean, new[] { b ? (byte)1 : (byte)0 }),
                sbyte i8 => (TdsDataType.I8, new[] { (byte)i8 }),
                short i16 => (TdsDataType.I16, BitConverter.GetBytes(i16)),
                int i32 => (TdsDataType.I32, BitConverter.GetBytes(i32)),
                long i64 => (TdsDataType.I64, BitConverter.GetBytes(i64)),
                byte u8 => (TdsDataType.U8, new[] { u8 }),
                ushort u16 => (TdsDataType.U16, BitConverter.GetBytes(u16)),
                uint u32 => (TdsDataType.U32, BitConverter.GetBytes(u32)),
                ulong u64 => (TdsDataType.U64, BitConverter.GetBytes(u64)),
                float f => (TdsDataType.SingleFloat, BitConverter.GetBytes(f)),
                double d => (TdsDataType.DoubleFloat, BitConverter.GetBytes(d)),
                string s => StringToBytes(s),
                DateTime dt => DateTimeToBytes(dt),
                _ => throw new NotSupportedException($"Property type {value.GetType()} is not supported")
            };
        }

        private static (TdsDataType, byte[] bytes) StringToBytes(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var result = new byte[4 + bytes.Length];
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0, 4), bytes.Length);
            bytes.CopyTo(result, 4);
            return (TdsDataType.String, result);
        }

        private static (TdsDataType, byte[] bytes) DateTimeToBytes(DateTime value)
        {
            // TDMS timestamp: seconds since 1904-01-01 00:00:00 UTC + fractions
            var epoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var diff = value.ToUniversalTime() - epoch;
            var seconds = (long)diff.TotalSeconds;
            var fractions = (ulong)((diff.TotalSeconds - seconds) * Math.Pow(2, 64));

            var bytes = new byte[16];
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(0, 8), seconds);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8, 8), fractions);
            return (TdsDataType.TimeStamp, bytes);
        }
    }

    /// <summary>
    /// Example usage and additional helper methods
    /// </summary>
    public static class TdmsWriterExtensions
    {
        /// <summary>
        /// Write waveform data with standard waveform properties
        /// </summary>
        public static void WriteWaveform<T>(this TdmsWriter writer, string groupName, string channelName,
            ReadOnlySpan<T> data, DateTime startTime, double increment, IDictionary<string, object>? additionalProperties = null)
            where T : unmanaged
        {
            var properties = new Dictionary<string, object>
            {
                ["wf_start_time"] = startTime,
                ["wf_increment"] = increment,
                ["wf_samples"] = data.Length
            };

            if (additionalProperties != null)
            {
                foreach (var kvp in additionalProperties)
                    properties[kvp.Key] = kvp.Value;
            }

            writer.WriteChannel(groupName, channelName, data, properties);
        }

        /// <summary>
        /// Defragment an existing TDMS file for optimal read performance
        /// </summary>
        public static async Task DefragmentAsync(string sourcePath, string destinationPath, TdmsWriterOptions? options = null)
        {
            // This would require a TDMS reader implementation
            // For now, this is a placeholder
            await Task.CompletedTask;
            throw new NotImplementedException("Defragmentation requires TDMS reader implementation");
        }
    }
}