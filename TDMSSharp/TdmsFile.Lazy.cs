using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TDMSSharp
{
    /// <summary>
    /// Options for controlling TDMS file reading behavior
    /// </summary>
    public class TdmsReadOptions
    {
        /// <summary>
        /// If true, channel data is not loaded into memory until explicitly requested
        /// </summary>
        public bool LazyLoad { get; set; } = false;

        /// <summary>
        /// Maximum amount of data (in bytes) to keep in memory at once
        /// </summary>
        public long MaxMemoryBytes { get; set; } = 1_073_741_824; // 1GB default

        /// <summary>
        /// If true, only loads metadata without any raw data
        /// </summary>
        public bool MetadataOnly { get; set; } = false;

        /// <summary>
        /// Specific channels to load (null = all channels)
        /// </summary>
        public HashSet<string>? ChannelFilter { get; set; }

        /// <summary>
        /// Time range filter for channels with wf_start_time property
        /// </summary>
        public (DateTime Start, DateTime End)? TimeRange { get; set; }

        /// <summary>
        /// Sample range to load (e.g., samples 1000 to 2000)
        /// </summary>
        public (long Start, long Count)? SampleRange { get; set; }
    }

    public partial class TdmsFile
    {
        private Stream? _sourceStream;
        private string? _sourcePath;
        private bool _ownsStream;
        private TdmsReadOptions? _readOptions;
        private readonly Dictionary<string, ChannelDataInfo> _channelDataInfo = new();

        /// <summary>
        /// Information about where channel data is located in the file
        /// </summary>
        public class ChannelDataInfo
        {
            public List<(long Position, long Count, TdsDataType DataType)> Segments { get; } = new();
            public long TotalSamples { get; set; }
            public bool IsLoaded { get; set; }
        }

        /// <summary>
        /// Open a TDMS file with specific read options
        /// </summary>
        public static TdmsFile Open(string path, TdmsReadOptions? options = null)
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, 
                                      FileShare.Read, 4096, FileOptions.RandomAccess);
            var file = Open(stream, options);
            file._sourcePath = path;
            file._ownsStream = true;
            return file;
        }

        /// <summary>
        /// Open a TDMS file from a stream with specific read options
        /// </summary>
        public static TdmsFile Open(Stream stream, TdmsReadOptions? options = null)
        {
            options ??= new TdmsReadOptions();
            
            if (options.LazyLoad && !stream.CanSeek)
            {
                throw new ArgumentException("Lazy loading requires a seekable stream");
            }

            var reader = new TdmsReader(stream, options);
            var file = reader.ReadFile();
            
            if (options.LazyLoad)
            {
                file._sourceStream = stream;
                file._ownsStream = false;
                file._readOptions = options;
            }
            
            return file;
        }

        /// <summary>
        /// Get an enumerator for efficient iteration over channel data
        /// </summary>
        public IChannelDataEnumerator GetChannelEnumerator(string channelPath)
        {
            var channel = FindChannel(channelPath);
            if (channel == null)
                throw new ArgumentException($"Channel '{channelPath}' not found");

            if (_sourceStream != null && _channelDataInfo.TryGetValue(channelPath, out var info))
            {
                return new LazyChannelEnumerator(_sourceStream, channel, info);
            }
            
            return new EagerChannelEnumerator(channel);
        }

        /// <summary>
        /// Load specific channels into memory
        /// </summary>
        public async Task LoadChannelsAsync(params string[] channelPaths)
        {
            if (_sourceStream == null)
                throw new InvalidOperationException("File was not opened with lazy loading");

            var tasks = channelPaths.Select(path => LoadChannelDataAsync(path));
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Load a specific range of samples from a channel
        /// </summary>
        public async Task<Array> LoadChannelRangeAsync(string channelPath, long startSample, long count)
        {
            var channel = FindChannel(channelPath);
            if (channel == null)
                throw new ArgumentException($"Channel '{channelPath}' not found");

            if (!_channelDataInfo.TryGetValue(channelPath, out var info))
                throw new InvalidOperationException($"No data info for channel '{channelPath}'");

            return await Task.Run(() => LoadChannelRange(channel, info, startSample, count));
        }

        private Array LoadChannelRange(TdmsChannel channel, ChannelDataInfo info, long startSample, long count)
        {
            var elementType = TdsDataTypeProvider.GetType(channel.DataType);
            var result = Array.CreateInstance(elementType, count);
            long currentSample = 0;
            long resultIndex = 0;

            foreach (var segment in info.Segments)
            {
                if (currentSample + segment.Count <= startSample)
                {
                    currentSample += segment.Count;
                    continue;
                }

                long segmentStart = Math.Max(0, startSample - currentSample);
                long segmentEnd = Math.Min(segment.Count, startSample + count - currentSample);
                long samplesToRead = segmentEnd - segmentStart;

                if (samplesToRead > 0)
                {
                    lock (_sourceStream!)
                    {
                        _sourceStream.Seek(segment.Position + segmentStart * GetElementSize(segment.DataType), SeekOrigin.Begin);
                        using var reader = new BinaryReader(_sourceStream, System.Text.Encoding.UTF8, true);
                        
                        var segmentData = ReadRawData(reader, segment.DataType, samplesToRead);
                        Array.Copy(segmentData, 0, result, resultIndex, samplesToRead);
                    }
                    
                    resultIndex += samplesToRead;
                }

                currentSample += segment.Count;
                if (resultIndex >= count) break;
            }

            return result;
        }

        private async Task LoadChannelDataAsync(string channelPath)
        {
            var channel = FindChannel(channelPath);
            if (channel == null) return;

            if (!_channelDataInfo.TryGetValue(channelPath, out var info)) return;
            if (info.IsLoaded) return;

            await Task.Run(() =>
            {
                var data = LoadChannelRange(channel, info, 0, info.TotalSamples);
                channel.Data = data;
                info.IsLoaded = true;
            });
        }

        private TdmsChannel? FindChannel(string channelPath)
        {
            foreach (var group in ChannelGroups)
            {
                var channel = group.Channels.FirstOrDefault(c => c.Path == channelPath);
                if (channel != null) return channel;
            }
            return null;
        }

        private Array ReadRawData(BinaryReader reader, TdsDataType dataType, long count)
        {
            // Simplified version - you'd implement the full logic here
            switch (dataType)
            {
                case TdsDataType.DoubleFloat:
                    var doubles = new double[count];
                    for (long i = 0; i < count; i++)
                        doubles[i] = reader.ReadDouble();
                    return doubles;
                case TdsDataType.SingleFloat:
                    var floats = new float[count];
                    for (long i = 0; i < count; i++)
                        floats[i] = reader.ReadSingle();
                    return floats;
                case TdsDataType.I32:
                    var ints = new int[count];
                    for (long i = 0; i < count; i++)
                        ints[i] = reader.ReadInt32();
                    return ints;
                // Add other types...
                default:
                    throw new NotSupportedException($"Data type {dataType} not supported");
            }
        }

        private int GetElementSize(TdsDataType dataType)
        {
            return dataType switch
            {
                TdsDataType.I8 or TdsDataType.U8 => 1,
                TdsDataType.I16 or TdsDataType.U16 => 2,
                TdsDataType.I32 or TdsDataType.U32 or TdsDataType.SingleFloat => 4,
                TdsDataType.I64 or TdsDataType.U64 or TdsDataType.DoubleFloat => 8,
                _ => throw new NotSupportedException($"Data type {dataType} not supported")
            };
        }

        /// <summary>
        /// Store channel data location info during reading
        /// </summary>
        internal void AddChannelDataInfo(string channelPath, long position, long count, TdsDataType dataType)
        {
            if (!_channelDataInfo.TryGetValue(channelPath, out var info))
            {
                info = new ChannelDataInfo();
                _channelDataInfo[channelPath] = info;
            }
            
            info.Segments.Add((position, count, dataType));
            info.TotalSamples += count;
        }

        /// <summary>
        /// Dispose of resources
        /// </summary>
        public void Dispose()
        {
            if (_ownsStream && _sourceStream != null)
            {
                _sourceStream.Dispose();
                _sourceStream = null;
            }
        }
    }

    /// <summary>
    /// Interface for channel data enumeration
    /// </summary>
    public interface IChannelDataEnumerator : IDisposable
    {
        bool HasMore { get; }
        Array GetNext(int maxSamples);
        Task<Array> GetNextAsync(int maxSamples);
        void Reset();
    }

    /// <summary>
    /// Enumerator for channels already loaded in memory
    /// </summary>
    internal class EagerChannelEnumerator : IChannelDataEnumerator
    {
        private readonly Array _data;
        private int _position;

        public EagerChannelEnumerator(TdmsChannel channel)
        {
            _data = channel.GetDataAsArray() ?? Array.CreateInstance(typeof(object), 0);
            _position = 0;
        }

        public bool HasMore => _position < _data.Length;

        public Array GetNext(int maxSamples)
        {
            if (!HasMore) return Array.CreateInstance(_data.GetType().GetElementType()!, 0);

            int count = Math.Min(maxSamples, _data.Length - _position);
            var result = Array.CreateInstance(_data.GetType().GetElementType()!, count);
            Array.Copy(_data, _position, result, 0, count);
            _position += count;
            return result;
        }

        public Task<Array> GetNextAsync(int maxSamples)
        {
            return Task.FromResult(GetNext(maxSamples));
        }

        public void Reset() => _position = 0;
        public void Dispose() { }
    }

    /// <summary>
    /// Lazy enumerator that reads from file as needed
    /// </summary>
    internal class LazyChannelEnumerator : IChannelDataEnumerator
    {
        private readonly Stream _stream;
        private readonly TdmsChannel _channel;
        private readonly TdmsFile.ChannelDataInfo _info;
        private int _segmentIndex;
        private long _segmentPosition;

        public LazyChannelEnumerator(Stream stream, TdmsChannel channel, TdmsFile.ChannelDataInfo info)
        {
            _stream = stream;
            _channel = channel;
            _info = info;
            Reset();
        }

        public bool HasMore => _segmentIndex < _info.Segments.Count;

        public Array GetNext(int maxSamples)
        {
            if (!HasMore)
                return Array.CreateInstance(TdsDataTypeProvider.GetType(_channel.DataType), 0);

            var segment = _info.Segments[_segmentIndex];
            long remaining = segment.Count - _segmentPosition;
            long toRead = Math.Min(maxSamples, remaining);

            lock (_stream)
            {
                _stream.Seek(segment.Position + _segmentPosition * GetElementSize(segment.DataType), SeekOrigin.Begin);
                using var reader = new BinaryReader(_stream, System.Text.Encoding.UTF8, true);
                
                var data = ReadData(reader, segment.DataType, toRead);
                
                _segmentPosition += toRead;
                if (_segmentPosition >= segment.Count)
                {
                    _segmentIndex++;
                    _segmentPosition = 0;
                }
                
                return data;
            }
        }

        public async Task<Array> GetNextAsync(int maxSamples)
        {
            return await Task.Run(() => GetNext(maxSamples));
        }

        public void Reset()
        {
            _segmentIndex = 0;
            _segmentPosition = 0;
        }

        private Array ReadData(BinaryReader reader, TdsDataType dataType, long count)
        {
            // Implement efficient reading based on data type
            // This is simplified - you'd use the optimized reading from TdmsReader
            switch (dataType)
            {
                case TdsDataType.DoubleFloat:
                    var doubles = new double[count];
                    for (long i = 0; i < count; i++)
                        doubles[i] = reader.ReadDouble();
                    return doubles;
                // Add other types...
                default:
                    throw new NotSupportedException();
            }
        }

        private int GetElementSize(TdsDataType dataType)
        {
            return dataType switch
            {
                TdsDataType.I8 or TdsDataType.U8 => 1,
                TdsDataType.I16 or TdsDataType.U16 => 2,
                TdsDataType.I32 or TdsDataType.U32 or TdsDataType.SingleFloat => 4,
                TdsDataType.I64 or TdsDataType.U64 or TdsDataType.DoubleFloat => 8,
                _ => throw new NotSupportedException()
            };
        }

        public void Dispose() { }
    }
}