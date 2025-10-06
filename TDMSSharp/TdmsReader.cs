// TdmsReader.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TdmsSharp
{
    public class TdmsReader : IDisposable
    {
        private readonly Stream _stream;
        private bool _isBigEndian;

        public TdmsReader(Stream stream)
        {
            _stream = stream;
        }

        public TdmsFileHolder ReadFile()
        {
            var file = new TdmsFileHolder();
            ParseSegments(file);
            ParseMetadata(file);
            return file;
        }

        private void ParseSegments(TdmsFileHolder file)
        {
            _stream.Seek(0, SeekOrigin.Begin);
            while (_stream.Position < _stream.Length)
            {
                long segmentStartOffset = _stream.Position;
                if (_stream.Length - segmentStartOffset < 28) break;

                var tag = ReadString(4);
                if (tag != "TDSm" && tag != "TDSh") throw new InvalidDataException("Invalid TDMS tag.");

                var tocBytes = ReadBytes(4);
                var toc = (TocFlags)BitConverter.ToUInt32(tocBytes, 0);
                _isBigEndian = (toc & TocFlags.BigEndian) != 0;

                int version = ReadInt32();
                long nextSegmentOffset = ReadInt64();
                long rawDataOffset = ReadInt64();

                var segment = new TdmsSegment(segmentStartOffset, toc, version, nextSegmentOffset, rawDataOffset);
                file.Segments.Add(segment);

                if (unchecked((ulong)nextSegmentOffset) == 0xFFFFFFFFFFFFFFFF) break;

                long nextPos = segmentStartOffset + 28 + nextSegmentOffset;
                if (nextPos > _stream.Length || nextPos <= segmentStartOffset) break;
                _stream.Seek(nextPos, SeekOrigin.Begin);
            }
        }

        private void ParseMetadata(TdmsFileHolder file)
        {
            var activeObjects = new List<TdmsChannelReader>();
            var channelCacheByPath = new Dictionary<string, TdmsChannelReader>();

            foreach (var segment in file.Segments)
            {
                var segmentObjects = new List<TdmsChannelReader>();
                if (!segment.IsNewObjectList)
                {
                    segmentObjects.AddRange(activeObjects);
                }

                if (segment.ContainsMetadata)
                {
                    _stream.Seek(segment.AbsoluteOffset + 28, SeekOrigin.Begin);
                    _isBigEndian = segment.IsBigEndian;

                    uint objectCount = ReadUInt32();
                    for (int i = 0; i < objectCount; i++)
                    {
                        string path = ReadLengthPrefixedString();
                        ParseObject(path, file, segment, segmentObjects, channelCacheByPath);
                    }
                }
                activeObjects = segmentObjects;

                if (segment.ContainsRawData)
                {
                    segment.ActiveChannels.AddRange(activeObjects);
                    CalculateSegmentChunking(segment);
                }
            }
        }

        private void CalculateSegmentChunking(TdmsSegment segment)
        {
            if (!segment.ActiveChannels.Any()) return;

            long metadataChunkSize = segment.ActiveChannels.Sum(ch => (long)GetChunkDataSize(ch, segment));

            if (metadataChunkSize > 0)
            {
                long totalRawDataSize = segment.NextSegmentOffset - segment.RawDataOffset;
                if (totalRawDataSize > 0 && totalRawDataSize % metadataChunkSize == 0)
                {
                    segment.ChunkCount = (ulong)(totalRawDataSize / metadataChunkSize);
                }
            }
        }

        private void ParseObject(string path, TdmsFileHolder file, TdmsSegment segment, List<TdmsChannelReader> segmentObjects, Dictionary<string, TdmsChannelReader> channelCache)
        {
            var (groupName, channelName) = ParsePath(path);
            if (channelName == null)
            {
                uint groupRawDataIndexLength = ReadUInt32();
                if (groupRawDataIndexLength != 0xFFFFFFFF && groupRawDataIndexLength != 0x0)
                {
                    _stream.Seek(groupRawDataIndexLength, SeekOrigin.Current);
                }
                uint propCount = ReadUInt32();
                return;
            }

            channelCache.TryGetValue(path, out var channel);

            uint rawDataIndexLength = ReadUInt32();
            RawDataIndexInfo? indexInfo = null;

            if (rawDataIndexLength == 0xFFFFFFFF) { /* No index */ }
            else if (rawDataIndexLength == 0x00000000)
            {
                if (channel == null || !channel.DataIndices.Any()) throw new InvalidDataException($"Path '{path}' has 'matches previous' index but no previous index was found.");
                var lastIndex = channel.DataIndices.Last();
                indexInfo = new RawDataIndexInfo(segment, lastIndex.DataType, lastIndex.NumberOfValues, lastIndex.TotalSizeInBytes);
            }
            else
            {
                var dataType = (TdmsDataType)ReadUInt32();
                uint dimension = ReadUInt32();
                if (dimension != 1) throw new NotSupportedException("Only 1D arrays are supported.");
                ulong numValues = ReadUInt64();
                ulong totalSize = (dataType == TdmsDataType.String) ? ReadUInt64() : 0;
                indexInfo = new RawDataIndexInfo(segment, dataType, numValues, totalSize);
            }

            if (channel == null)
            {
                var group = file.GetGroup(groupName) ?? new TdmsGroupHolder(groupName);
                if (!file.Groups.Contains(group)) file.Groups.Add(group);

                var newChannelDataType = indexInfo?.DataType ?? TdmsDataType.Void;
                channel = new TdmsChannelReader(channelName, newChannelDataType, this);
                group.Channels.Add(channel);
                channelCache[path] = channel;
            }

            if (indexInfo != null)
            {
                channel.DataType = indexInfo.DataType;
                channel.DataIndices.Add(indexInfo);
            }

            var existingInSegment = segmentObjects.FirstOrDefault(c => c == channel);
            if (existingInSegment == null) segmentObjects.Add(channel);

            uint propertyCount = ReadUInt32();
        }

        internal T[] ReadChannelData<T>(TdmsChannelReader channel) where T : unmanaged
        {
            var allValues = new List<T>();
            foreach (var indexInfo in channel.DataIndices)
            {
                if (indexInfo.NumberOfValues == 0) continue;
                var segment = indexInfo.Segment;
                if (segment.IsInterleaved) throw new NotSupportedException("Interleaved data reading is not yet supported.");

                long singleChunkTotalSize = segment.ActiveChannels.Sum(ch => (long)GetChunkDataSize(ch, segment));
                long startOffsetInChunk = GetChannelOffsetInChunk(channel, segment);
                long firstChunkStart = segment.AbsoluteOffset + 28 + segment.RawDataOffset;
                _isBigEndian = segment.IsBigEndian;

                for (ulong i = 0; i < segment.ChunkCount; i++)
                {
                    long currentChunkStart = firstChunkStart + ((long)i * singleChunkTotalSize);
                    long channelDataAbsolutePos = currentChunkStart + startOffsetInChunk;

                    _stream.Seek(channelDataAbsolutePos, SeekOrigin.Begin);
                    allValues.AddRange(TdmsDataTypeReader.ReadArray<T>(_stream, (int)indexInfo.NumberOfValues, _isBigEndian));
                }
            }
            return allValues.ToArray();
        }

        internal string[] ReadStringChannelData(TdmsChannelReader channel)
        {
            var allValues = new List<string>();
            foreach (var indexInfo in channel.DataIndices)
            {
                if (indexInfo.NumberOfValues == 0) continue;
                var segment = indexInfo.Segment;
                if (segment.IsInterleaved) throw new NotSupportedException("Interleaved string data is not supported.");

                long singleChunkTotalSize = segment.ActiveChannels.Sum(ch => (long)GetChunkDataSize(ch, segment));
                long startOffsetInChunk = GetChannelOffsetInChunk(channel, segment);
                long firstChunkStart = segment.AbsoluteOffset + 28 + segment.RawDataOffset;
                _isBigEndian = segment.IsBigEndian;

                for (ulong i = 0; i < segment.ChunkCount; i++)
                {
                    long currentChunkStart = firstChunkStart + ((long)i * singleChunkTotalSize);
                    long channelDataAbsolutePos = currentChunkStart + startOffsetInChunk;

                    _stream.Seek(channelDataAbsolutePos, SeekOrigin.Begin);
                    allValues.AddRange(TdmsDataTypeReader.ReadStringArray(_stream, indexInfo.NumberOfValues, _isBigEndian));
                }
            }
            return allValues.ToArray();
        }

        private ulong GetChunkDataSize(TdmsChannelReader channel, TdmsSegment segment)
        {
            var chIndex = channel.DataIndices.LastOrDefault(i => i.Segment == segment);
            if (chIndex == null) return 0;
            if (chIndex.DataType == TdmsDataType.String) return chIndex.TotalSizeInBytes;
            return chIndex.NumberOfValues * (ulong)TdmsDataTypeSizeHelper.GetSize(chIndex.DataType);
        }

        private long GetChannelOffsetInChunk(TdmsChannelReader channel, TdmsSegment segment)
        {
            long offset = 0;
            foreach (var ch in segment.ActiveChannels)
            {
                if (ch == channel) break;
                offset += (long)GetChunkDataSize(ch, segment);
            }
            return offset;
        }

        private (string group, string? channel) ParsePath(string path)
        {
            if (path == "/") return ("/", null);
            var parts = path.Split(new[] { "'/'" }, StringSplitOptions.None);
            var groupName = parts[0].Trim('\'', '/').Replace("''", "'");
            if (parts.Length == 1) return (groupName, null);
            if (parts.Length == 2)
            {
                var channelName = parts[1].Trim('\'').Replace("''", "'");
                return (groupName, channelName);
            }
            throw new InvalidDataException($"Invalid channel path format: {path}");
        }

        #region Stream Readers
        private byte[] ReadBytes(int count)
        {
            var buffer = new byte[count];
            int read = _stream.Read(buffer, 0, count);
            if (read < count) throw new EndOfStreamException();
            return buffer;
        }

        private string ReadString(int count) => Encoding.ASCII.GetString(ReadBytes(count));

        private string ReadLengthPrefixedString()
        {
            uint length = ReadUInt32();
            if (length == 0) return string.Empty;
            var bytes = ReadBytes((int)length);
            var encoding = (Encoding)Encoding.UTF8.Clone();
            encoding.DecoderFallback = new DecoderReplacementFallback("�");
            return encoding.GetString(bytes);
        }

        private int ReadInt32()
        {
            var bytes = ReadBytes(4);
            if (_isBigEndian) Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }

        private uint ReadUInt32()
        {
            var bytes = ReadBytes(4);
            if (_isBigEndian) Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        private long ReadInt64()
        {
            var bytes = ReadBytes(8);
            if (_isBigEndian) Array.Reverse(bytes);
            return BitConverter.ToInt64(bytes, 0);
        }

        private ulong ReadUInt64()
        {
            var bytes = ReadBytes(8);
            if (_isBigEndian) Array.Reverse(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }
        #endregion

        public void Dispose()
        {
            _stream.Dispose();
        }
    }
}