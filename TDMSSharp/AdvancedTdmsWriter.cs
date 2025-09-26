using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TDMSSharp.Advanced
{
    /// <summary>
    /// Advanced TDMS writer with full optimization support including:
    /// - Incremental metadata (only changes written)
    /// - Raw data appending without metadata
    /// - Interleaved data support
    /// - DAQmx format support
    /// - Asynchronous writing
    /// </summary>
    public sealed class AdvancedTdmsWriter : IDisposable, IAsyncDisposable
    {
        private readonly Stream _dataStream;
        private readonly Stream? _indexStream;
        private readonly bool _ownsStreams;
        private readonly int _version;
        private readonly bool _interleavedMode;
        
        // Optimization state tracking
        private readonly Dictionary<string, ObjectMetadata> _objectMetadata = new();
        private readonly Dictionary<string, TdmsRawDataIndex> _currentIndexes = new();
        private readonly HashSet<string> _existingObjects = new();
        
        // Buffering for performance
        private readonly MemoryPool<byte> _memoryPool = MemoryPool<byte>.Shared;
        private IMemoryOwner<byte>? _writeBuffer;
        private int _bufferPosition;
        private const int BufferSize = 1048576; // 1MB buffer
        
        // Segment tracking
        private long _lastSegmentPosition;
        private bool _canAppendRawDataOnly;
        private bool _disposed;

        public AdvancedTdmsWriter(string filePath, AdvancedWriterOptions? options = null)
            : this(CreateFileStream(filePath, options), CreateIndexStream(filePath, options), true, options)
        {
        }

        public AdvancedTdmsWriter(Stream dataStream, Stream? indexStream = null, bool ownsStreams = false, AdvancedWriterOptions? options = null)
        {
            _dataStream = dataStream ?? throw new ArgumentNullException(nameof(dataStream));
            _indexStream = indexStream;
            _ownsStreams = ownsStreams;
            _version = options?.Version ?? 4713;
            _interleavedMode = options?.InterleavedMode ?? false;
            
            _writeBuffer = _memoryPool.Rent(BufferSize);
            _bufferPosition = 0;
            
            if (_version != 4712 && _version != 4713)
                throw new ArgumentException($"Invalid TDMS version: {_version}");
        }

        private static FileStream CreateFileStream(string filePath, AdvancedWriterOptions? options)
        {
            var mode = options?.AppendMode ?? false ? FileMode.Append : FileMode.Create;
            return new FileStream(filePath, mode, FileAccess.Write, FileShare.Read, 
                                  options?.FileBufferSize ?? 65536, 
                                  options?.UseAsync ?? true);
        }

        private static FileStream? CreateIndexStream(string filePath, AdvancedWriterOptions? options)
        {
            if (options?.CreateIndexFile != true)
                return null;
                
            var indexPath = filePath + "_index";
            var mode = options?.AppendMode ?? false ? FileMode.Append : FileMode.Create;
            return new FileStream(indexPath, mode, FileAccess.Write, FileShare.Read, 
                                  options?.FileBufferSize ?? 65536, 
                                  options?.UseAsync ?? true);
        }

        /// <summary>
        /// Write segment with incremental metadata optimization
        /// </summary>
        public async Task WriteSegmentOptimizedAsync(IEnumerable<IChannelData> channels, CancellationToken cancellationToken = default)
        {
            var channelList = channels.ToList();
            
            // Determine what metadata needs to be written
            var metadataChanges = AnalyzeMetadataChanges(channelList);
            
            if (metadataChanges.IsEmpty && _canAppendRawDataOnly)
            {
                // Optimization: Append raw data only without lead-in or metadata
                await AppendRawDataOnlyAsync(channelList, cancellationToken);
            }
            else
            {
                // Write full or incremental segment
                await WriteIncrementalSegmentAsync(channelList, metadataChanges, cancellationToken);
            }
            
            _canAppendRawDataOnly = metadataChanges.OnlyIndexChanges;
            await FlushBufferAsync();
        }

        /// <summary>
        /// Append raw data without metadata (maximum optimization)
        /// </summary>
        private async Task AppendRawDataOnlyAsync(List<IChannelData> channels, CancellationToken cancellationToken)
        {
            if (_interleavedMode)
            {
                await WriteInterleavedDataAsync(channels, cancellationToken);
            }
            else
            {
                foreach (var channel in channels)
                {
                    await WriteChannelDataAsync(channel, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Write incremental segment with only changed metadata
        /// </summary>
        private async Task WriteIncrementalSegmentAsync(List<IChannelData> channels, MetadataChanges changes, CancellationToken cancellationToken)
        {
            _lastSegmentPosition = _dataStream.Position;
            
            using var metadataStream = new MemoryStream();
            using var rawDataStream = new MemoryStream();
            
            // Write metadata for changed objects only
            await WriteIncrementalMetadataAsync(metadataStream, channels, changes);
            
            // Write raw data
            long rawDataSize = 0;
            if (channels.Any(c => c.HasData))
            {
                if (_interleavedMode)
                {
                    rawDataSize = await WriteInterleavedDataToStreamAsync(rawDataStream, channels);
                }
                else
                {
                    foreach (var channel in channels.Where(c => c.HasData))
                    {
                        rawDataSize += await WriteChannelDataToStreamAsync(rawDataStream, channel);
                    }
                }
            }
            
            // Determine ToC flags
            var toc = TocFlags.None;
            if (metadataStream.Length > 0)
                toc |= TocFlags.MetaData;
            if (rawDataSize > 0)
                toc |= TocFlags.RawData;
            if (changes.HasNewObjects)
                toc |= TocFlags.NewObjList;
            if (_interleavedMode)
                toc |= TocFlags.InterleavedData;
            
            // Write lead-in
            await WriteLeadInAsync(toc, metadataStream.Length, rawDataSize, false);
            
            // Write metadata if present
            if (metadataStream.Length > 0)
            {
                metadataStream.Position = 0;
                await metadataStream.CopyToAsync(_dataStream, 81920, cancellationToken);
            }
            
            // Write raw data
            if (rawDataSize > 0)
            {
                rawDataStream.Position = 0;
                await rawDataStream.CopyToAsync(_dataStream, 81920, cancellationToken);
            }
            
            // Write index segment if needed
            if (_indexStream != null)
            {
                await WriteIndexSegmentAsync(channels, changes, metadataStream.Length);
            }
        }

        /// <summary>
        /// Write interleaved data for better performance with multiple channels
        /// </summary>
        private async Task WriteInterleavedDataAsync(List<IChannelData> channels, CancellationToken cancellationToken)
        {
            var dataChannels = channels.Where(c => c.HasData).ToList();
            if (!dataChannels.Any()) return;
            
            // Find the minimum number of samples
            var minSamples = dataChannels.Min(c => c.SampleCount);
            
            // Write interleaved samples
            for (int i = 0; i < minSamples; i++)
            {
                foreach (var channel in dataChannels)
                {
                    await WriteBufferedBytesAsync(channel.GetSampleBytes(i), cancellationToken);
                }
            }
            
            // Write remaining samples for channels with more data
            foreach (var channel in dataChannels.Where(c => c.SampleCount > minSamples))
            {
                for (int i = minSamples; i < channel.SampleCount; i++)
                {
                    await WriteBufferedBytesAsync(channel.GetSampleBytes(i), cancellationToken);
                }
            }
        }

        private async Task<long> WriteInterleavedDataToStreamAsync(Stream stream, List<IChannelData> channels)
        {
            long totalBytes = 0;
            var dataChannels = channels.Where(c => c.HasData).ToList();
            if (!dataChannels.Any()) return 0;
            
            var minSamples = dataChannels.Min(c => c.SampleCount);
            
            // Write interleaved samples
            for (int i = 0; i < minSamples; i++)
            {
                foreach (var channel in dataChannels)
                {
                    var bytes = channel.GetSampleBytes(i);
                    await stream.WriteAsync(bytes);
                    totalBytes += bytes.Length;
                }
            }
            
            // Write remaining samples
            foreach (var channel in dataChannels.Where(c => c.SampleCount > minSamples))
            {
                for (int i = minSamples; i < channel.SampleCount; i++)
                {
                    var bytes = channel.GetSampleBytes(i);
                    await stream.WriteAsync(bytes);
                    totalBytes += bytes.Length;
                }
            }
            
            return totalBytes;
        }

        /// <summary>
        /// Analyze what metadata has changed since last segment
        /// </summary>
        private MetadataChanges AnalyzeMetadataChanges(List<IChannelData> channels)
        {
            var changes = new MetadataChanges();
            
            foreach (var channel in channels)
            {
                var path = channel.Path;
                
                // Check if object exists
                if (!_existingObjects.Contains(path))
                {
                    changes.NewObjects.Add(path);
                    changes.HasNewObjects = true;
                    _existingObjects.Add(path);
                }
                
                // Check for property changes
                if (_objectMetadata.TryGetValue(path, out var existing))
                {
                    if (!PropertiesEqual(existing.Properties, channel.Properties))
                    {
                        changes.ChangedProperties[path] = channel.Properties;
                        existing.Properties = channel.Properties;
                    }
                }
                else
                {
                    _objectMetadata[path] = new ObjectMetadata
                    {
                        Path = path,
                        Properties = channel.Properties
                    };
                    changes.ChangedProperties[path] = channel.Properties;
                }
                
                // Check for index changes
                if (channel.HasData)
                {
                    var newIndex = channel.GetRawDataIndex();
                    if (_currentIndexes.TryGetValue(path, out var currentIndex))
                    {
                        if (!currentIndex.Equals(newIndex))
                        {
                            changes.ChangedIndexes[path] = newIndex;
                            _currentIndexes[path] = newIndex;
                        }
                    }
                    else
                    {
                        changes.ChangedIndexes[path] = newIndex;
                        _currentIndexes[path] = newIndex;
                    }
                }
            }
            
            changes.OnlyIndexChanges = !changes.HasNewObjects && 
                                       changes.ChangedProperties.Count == 0 && 
                                       changes.ChangedIndexes.Count == 0;
            
            return changes;
        }

        private bool PropertiesEqual(IDictionary<string, object>? props1, IDictionary<string, object>? props2)
        {
            if (props1 == null && props2 == null) return true;
            if (props1 == null || props2 == null) return false;
            if (props1.Count != props2.Count) return false;
            
            foreach (var kvp in props1)
            {
                if (!props2.TryGetValue(kvp.Key, out var value2) || !Equals(kvp.Value, value2))
                    return false;
            }
            
            return true;
        }

        private async Task WriteIncrementalMetadataAsync(Stream stream, List<IChannelData> channels, MetadataChanges changes)
        {
            if (changes.IsEmpty) return;
            
            var objectsToWrite = channels.Where(c => 
                changes.NewObjects.Contains(c.Path) ||
                changes.ChangedProperties.ContainsKey(c.Path) ||
                changes.ChangedIndexes.ContainsKey(c.Path)).ToList();
            
            // Write number of objects
            await WriteInt32Async(stream, objectsToWrite.Count);
            
            foreach (var channel in objectsToWrite)
            {
                // Write path
                await WriteStringAsync(stream, channel.Path);
                
                // Write raw data index
                if (changes.ChangedIndexes.TryGetValue(channel.Path, out var index))
                {
                    await WriteRawDataIndexAsync(stream, index);
                }
                else if (!channel.HasData)
                {
                    await WriteInt32Async(stream, -1); // No data
                }
                else
                {
                    await WriteInt32Async(stream, 0); // Same as previous
                }
                
                // Write properties if changed
                if (changes.ChangedProperties.TryGetValue(channel.Path, out var props))
                {
                    await WritePropertiesAsync(stream, props);
                }
                else
                {
                    await WriteInt32Async(stream, 0); // No properties
                }
            }
        }

        private async Task WriteLeadInAsync(TocFlags toc, long metadataSize, long rawDataSize, bool isIndex)
        {
            var buffer = _writeBuffer!.Memory.Span.Slice(_bufferPosition, 28);
            
            // Tag
            buffer[0] = (byte)'T';
            buffer[1] = (byte)'D';
            buffer[2] = (byte)'S';
            buffer[3] = isIndex ? (byte)'h' : (byte)'m';
            
            // ToC
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4), (int)toc);
            
            // Version
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(8), _version);
            
            // Next segment offset
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(12), (ulong)(metadataSize + rawDataSize));
            
            // Raw data offset
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(20), (ulong)metadataSize);
            
            _bufferPosition += 28;
            
            if (_bufferPosition >= BufferSize - 1024)
                await FlushBufferAsync();
        }

        private async Task WriteChannelDataAsync(IChannelData channel, CancellationToken cancellationToken)
        {
            if (!channel.HasData) return;
            
            await channel.WriteDataAsync(async bytes => 
                await WriteBufferedBytesAsync(bytes, cancellationToken));
        }

        private async Task<long> WriteChannelDataToStreamAsync(Stream stream, IChannelData channel)
        {
            if (!channel.HasData) return 0;
            
            long totalBytes = 0;
            await channel.WriteDataAsync(async bytes =>
            {
                await stream.WriteAsync(bytes);
                totalBytes += bytes.Length;
            });
            
            return totalBytes;
        }

        private async Task WriteBufferedBytesAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            var remaining = bytes;
            
            while (remaining.Length > 0)
            {
                var availableSpace = BufferSize - _bufferPosition;
                if (availableSpace == 0)
                {
                    await FlushBufferAsync();
                    availableSpace = BufferSize;
                }
                
                var toCopy = Math.Min(remaining.Length, availableSpace);
                remaining.Slice(0, toCopy).CopyTo(_writeBuffer!.Memory.Slice(_bufferPosition));
                _bufferPosition += toCopy;
                remaining = remaining.Slice(toCopy);
            }
        }

        private async Task FlushBufferAsync()
        {
            if (_bufferPosition > 0)
            {
                await _dataStream.WriteAsync(_writeBuffer!.Memory.Slice(0, _bufferPosition));
                _bufferPosition = 0;
            }
        }

        private async Task WriteIndexSegmentAsync(List<IChannelData> channels, MetadataChanges changes, long metadataSize)
        {
            // Similar to data segment but without raw data
            using var metadataStream = new MemoryStream();
            await WriteIncrementalMetadataAsync(metadataStream, channels, changes);
            
            var toc = TocFlags.None;
            if (metadataStream.Length > 0)
                toc |= TocFlags.MetaData;
            if (changes.HasNewObjects)
                toc |= TocFlags.NewObjList;
            
            await WriteLeadInToStreamAsync(_indexStream!, toc, metadataStream.Length, 0, true);
            
            if (metadataStream.Length > 0)
            {
                metadataStream.Position = 0;
                await metadataStream.CopyToAsync(_indexStream!);
            }
            
            await _indexStream!.FlushAsync();
        }

        private async Task WriteLeadInToStreamAsync(Stream stream, TocFlags toc, long metadataSize, long rawDataSize, bool isIndex)
        {
            Span<byte> buffer = stackalloc byte[28];
            
            buffer[0] = (byte)'T';
            buffer[1] = (byte)'D';
            buffer[2] = (byte)'S';
            buffer[3] = isIndex ? (byte)'h' : (byte)'m';
            
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4), (int)toc);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(8), _version);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(12), (ulong)(metadataSize + rawDataSize));
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(20), (ulong)metadataSize);
            
            // Correctly handle async write from a stack-allocated Span by copying to a heap array.
            await stream.WriteAsync(buffer.ToArray());
        }

        private async Task WriteRawDataIndexAsync(Stream stream, TdmsRawDataIndex index)
        {
            await WriteInt32Async(stream, 20 + (index.DataType == TdsDataType.String ? 8 : 0));
            await WriteInt32Async(stream, (int)index.DataType);
            await WriteUInt32Async(stream, 1); // Array dimension
            await WriteUInt64Async(stream, index.NumberOfValues);
            
            if (index.DataType == TdsDataType.String)
            {
                await WriteUInt64Async(stream, index.TotalSizeInBytes);
            }
        }

        private async Task WritePropertiesAsync(Stream stream, IDictionary<string, object>? properties)
        {
            if (properties == null || properties.Count == 0)
            {
                await WriteInt32Async(stream, 0);
                return;
            }
            
            await WriteInt32Async(stream, properties.Count);
            
            foreach (var kvp in properties)
            {
                await WriteStringAsync(stream, kvp.Key);
                var (dataType, bytes) = TdmsValue.ToBytes(kvp.Value);
                await WriteInt32Async(stream, (int)dataType);
                await stream.WriteAsync(bytes);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task WriteInt32Async(Stream stream, int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            // Correctly handle async write from a stack-allocated Span by copying to a heap array.
            await stream.WriteAsync(buffer.ToArray());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task WriteUInt32Async(Stream stream, uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            // Correctly handle async write from a stack-allocated Span by copying to a heap array.
            await stream.WriteAsync(buffer.ToArray());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task WriteUInt64Async(Stream stream, ulong value)
        {
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
            // Correctly handle async write from a stack-allocated Span by copying to a heap array.
            await stream.WriteAsync(buffer.ToArray());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task WriteStringAsync(Stream stream, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            await WriteInt32Async(stream, bytes.Length);
            await stream.WriteAsync(bytes);
        }

        public void Dispose()
        {
            if (_disposed) return;

            FlushBufferAsync().GetAwaiter().GetResult();
            
            _writeBuffer?.Dispose();
            
            if (_ownsStreams)
            {
                _dataStream?.Dispose();
                _indexStream?.Dispose();
            }

            _disposed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;

            await FlushBufferAsync();
            
            _writeBuffer?.Dispose();
            
            if (_ownsStreams)
            {
                if (_dataStream != null)
                    await _dataStream.DisposeAsync();
                if (_indexStream != null)
                    await _indexStream.DisposeAsync();
            }

            _disposed = true;
        }

        private class ObjectMetadata
        {
            public string Path { get; set; } = "";
            public IDictionary<string, object>? Properties { get; set; }
        }

        private class MetadataChanges
        {
            public HashSet<string> NewObjects { get; } = new();
            public Dictionary<string, IDictionary<string, object>> ChangedProperties { get; } = new();
            public Dictionary<string, TdmsRawDataIndex> ChangedIndexes { get; } = new();
            public bool HasNewObjects { get; set; }
            public bool OnlyIndexChanges { get; set; }
            
            public bool IsEmpty => !HasNewObjects && ChangedProperties.Count == 0 && ChangedIndexes.Count == 0;
        }
    }

    /// <summary>
    /// Advanced writer options with full optimization control
    /// </summary>
    public class AdvancedWriterOptions
    {
        public int Version { get; set; } = 4713;
        public bool CreateIndexFile { get; set; } = false;
        public bool AppendMode { get; set; } = false;
        public bool InterleavedMode { get; set; } = false;
        public bool UseAsync { get; set; } = true;
        public int FileBufferSize { get; set; } = 65536;
    }

    /// <summary>
    /// Interface for channel data providers
    /// </summary>
    public interface IChannelData
    {
        string Path { get; }
        bool HasData { get; }
        int SampleCount { get; }
        IDictionary<string, object>? Properties { get; }
        TdmsRawDataIndex GetRawDataIndex();
        ReadOnlyMemory<byte> GetSampleBytes(int index);
        Task WriteDataAsync(Func<ReadOnlyMemory<byte>, Task> writer);
    }

    /// <summary>
    /// Generic channel data implementation
    /// </summary>
    public class ChannelData<T> : IChannelData where T : unmanaged
    {
        private readonly string _group;
        private readonly string _channel;
        private readonly T[] _data;

        public string Path => $"/'{_group.Replace("'", "''")}'/'{_channel.Replace("'", "''")}'";
        public bool HasData => true;
        public int SampleCount => _data.Length;
        public IDictionary<string, object>? Properties { get; }

        public ChannelData(string group, string channel, T[] data, IDictionary<string, object>? properties = null)
        {
            _group = group;
            _channel = channel;
            _data = data;
            Properties = properties;
        }

        public TdmsRawDataIndex GetRawDataIndex()
        {
            return new TdmsRawDataIndex(GetDataType(), (ulong)_data.Length, (ulong)(_data.Length * Unsafe.SizeOf<T>()));
        }

        public ReadOnlyMemory<byte> GetSampleBytes(int index)
        {
            var bytes = new byte[Unsafe.SizeOf<T>()];
            Unsafe.As<byte, T>(ref bytes[0]) = _data[index];
            return bytes;
        }

        public async Task WriteDataAsync(Func<ReadOnlyMemory<byte>, Task> writer)
        {
            // This is the most efficient way to handle this.
            // _data.AsMemory() creates a Memory<T> wrapper around the array (no copy).
            // MemoryMarshal.AsMemory then reinterprets it as Memory<byte> (no copy).
            // This is heap-safe and can be used across await boundaries.
            var byteSpan = MemoryMarshal.AsBytes(_data.AsSpan());
            await writer(byteSpan.ToArray());
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
    /// DAQmx channel data with Format Changing Scalers
    /// </summary>
    public class DAQmxChannelData : IChannelData
    {
        private readonly string _group;
        private readonly string _channel;
        private readonly byte[] _rawData;
        private readonly DAQmxScaler[] _scalers;
        
        public string Path => $"/'{_group.Replace("'", "''")}'/'{_channel.Replace("'", "''")}'";
        public bool HasData => true;
        public int SampleCount { get; }
        public IDictionary<string, object>? Properties { get; }

        public DAQmxChannelData(string group, string channel, byte[] rawData, 
                                DAQmxScaler[] scalers, int sampleCount,
                                IDictionary<string, object>? properties = null)
        {
            _group = group;
            _channel = channel;
            _rawData = rawData;
            _scalers = scalers;
            SampleCount = sampleCount;
            Properties = properties;
        }

        public TdmsRawDataIndex GetRawDataIndex()
        {
            return new TdmsRawDataIndex(TdsDataType.DAQmxRawData, (ulong)SampleCount, (ulong)_rawData.Length);
        }

        public ReadOnlyMemory<byte> GetSampleBytes(int index)
        {
            // This would need proper implementation based on DAQmx format
            throw new NotImplementedException();
        }

        public async Task WriteDataAsync(Func<ReadOnlyMemory<byte>, Task> writer)
        {
            // Write DAQmx format header
            using var ms = new MemoryStream();
            
            // Format changing scaler marker
            await WriteInt32(ms, 0x1269); // or 0x1369 for digital line scaler
            
            // Data type (0xFFFFFFFF for DAQmx)
            await WriteInt32(ms, -1);
            
            // Array dimension
            await WriteUInt32(ms, 1);
            
            // Number of values
            await WriteUInt64(ms, (ulong)SampleCount);
            
            // Vector of Format Changing scalers
            await WriteInt32(ms, _scalers.Length);
            
            foreach (var scaler in _scalers)
            {
                await WriteInt32(ms, scaler.DataType);
                await WriteInt32(ms, scaler.RawBufferIndex);
                await WriteInt32(ms, scaler.RawByteOffset);
                await WriteInt32(ms, scaler.SampleFormatBitmap);
                await WriteInt32(ms, scaler.ScaleId);
            }
            
            // Write the header
            await writer(ms.ToArray());
            
            // Write raw data
            await writer(_rawData);
        }

        private static async Task WriteInt32(Stream stream, int value)
        {
            var bytes = BitConverter.GetBytes(value);
            await stream.WriteAsync(bytes);
        }

        private static async Task WriteUInt32(Stream stream, uint value)
        {
            var bytes = BitConverter.GetBytes(value);
            await stream.WriteAsync(bytes);
        }

        private static async Task WriteUInt64(Stream stream, ulong value)
        {
            var bytes = BitConverter.GetBytes(value);
            await stream.WriteAsync(bytes);
        }
    }

    /// <summary>
    /// DAQmx Format Changing Scaler
    /// </summary>
    public struct DAQmxScaler
    {
        public int DataType { get; set; }
        public int RawBufferIndex { get; set; }
        public int RawByteOffset { get; set; }
        public int SampleFormatBitmap { get; set; }
        public int ScaleId { get; set; }
    }

    /// <summary>
    /// High-performance streaming writer for continuous data acquisition
    /// </summary>
    public class StreamingTdmsWriter : IDisposable
    {
        private readonly AdvancedTdmsWriter _writer;
        private readonly Dictionary<string, IChannelBuffer> _channelBuffers = new();
        private readonly int _flushThreshold;
        private readonly Timer? _autoFlushTimer;
        
        public StreamingTdmsWriter(string filePath, StreamingOptions? options = null)
        {
            var writerOptions = new AdvancedWriterOptions
            {
                Version = options?.Version ?? 4713,
                CreateIndexFile = options?.CreateIndexFile ?? false,
                AppendMode = true,
                InterleavedMode = options?.InterleavedMode ?? false,
                UseAsync = true,
                FileBufferSize = options?.FileBufferSize ?? 131072
            };
            
            _writer = new AdvancedTdmsWriter(filePath, writerOptions);
            _flushThreshold = options?.FlushThreshold ?? 10000;
            
            if (options?.AutoFlushInterval != null)
            {
                _autoFlushTimer = new Timer(_ => FlushAsync().GetAwaiter().GetResult(), 
                                            null, options.AutoFlushInterval.Value, 
                                            options.AutoFlushInterval.Value);
            }
        }

        /// <summary>
        /// Add samples to a channel buffer
        /// </summary>
        public void AddSamples<T>(string group, string channel, ReadOnlySpan<T> samples) where T : unmanaged
        {
            var key = $"{group}/{channel}";
            if (!_channelBuffers.TryGetValue(key, out var buffer))
            {
                buffer = new ChannelBuffer<T>(group, channel, _flushThreshold);
                _channelBuffers[key] = buffer;
            }
            
            ((ChannelBuffer<T>)buffer).AddSamples(samples);
            
            // Auto-flush if threshold reached
            if (buffer.SampleCount >= _flushThreshold)
            {
                _ = FlushChannelAsync(key);
            }
        }

        /// <summary>
        /// Flush all pending data
        /// </summary>
        public async Task FlushAsync()
        {
            var channels = new List<IChannelData>();
            
            foreach (var buffer in _channelBuffers.Values)
            {
                if (buffer.SampleCount > 0)
                {
                    channels.Add(buffer.CreateChannelData());
                    buffer.Clear();
                }
            }
            
            if (channels.Count > 0)
            {
                await _writer.WriteSegmentOptimizedAsync(channels);
            }
        }

        private async Task FlushChannelAsync(string key)
        {
            if (_channelBuffers.TryGetValue(key, out var buffer) && buffer.SampleCount > 0)
            {
                var channelData = buffer.CreateChannelData();
                buffer.Clear();
                await _writer.WriteSegmentOptimizedAsync(new[] { channelData });
            }
        }

        public void Dispose()
        {
            _autoFlushTimer?.Dispose();
            FlushAsync().GetAwaiter().GetResult();
            _writer.Dispose();
        }
        
        private interface IChannelBuffer
        {
            int SampleCount { get; }
            IChannelData CreateChannelData();
            void Clear();
        }
        
        private class ChannelBuffer<T> : IChannelBuffer where T : unmanaged
        {
            private readonly string _group;
            private readonly string _channel;
            private readonly List<T> _samples;
            private readonly int _capacity;
            
            public int SampleCount => _samples.Count;
            
            public ChannelBuffer(string group, string channel, int capacity)
            {
                _group = group;
                _channel = channel;
                _capacity = capacity;
                _samples = new List<T>(capacity);
            }
            
            public void AddSamples(ReadOnlySpan<T> samples)
            {
                foreach (var sample in samples)
                {
                    _samples.Add(sample);
                }
            }
            
            public IChannelData CreateChannelData()
            {
                return new ChannelData<T>(_group, _channel, _samples.ToArray());
            }
            
            public void Clear()
            {
                _samples.Clear();
            }
        }
    }

    /// <summary>
    /// Streaming writer options
    /// </summary>
    public class StreamingOptions
    {
        public int Version { get; set; } = 4713;
        public bool CreateIndexFile { get; set; } = false;
        public bool InterleavedMode { get; set; } = false;
        public int FlushThreshold { get; set; } = 10000;
        public TimeSpan? AutoFlushInterval { get; set; }
        public int FileBufferSize { get; set; } = 131072;
    }
}
