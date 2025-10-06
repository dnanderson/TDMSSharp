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

        // Original method - reads all data at once
        internal T[] ReadChannelData<T>(TdmsChannelReader channel) where T : unmanaged
        {
            long totalValues = (long)channel.DataIndices.Sum(idx => (decimal)idx.NumberOfValues);
            if (totalValues > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Channel has {totalValues} values which exceeds array size limit. Use ReadChannelDataChunk or StreamData instead.");
            }
            return ReadChannelDataChunk<T>(channel, 0, (int)totalValues);
        }

        // New method - reads a chunk of data
        internal T[] ReadChannelDataChunk<T>(TdmsChannelReader channel, long startIndex, int count) where T : unmanaged
        {
            if (count <= 0) return Array.Empty<T>();

            var result = new T[count];
            int resultOffset = 0;
            long currentIndex = 0;

            foreach (var indexInfo in channel.DataIndices)
            {
                if (indexInfo.NumberOfValues == 0) continue;

                long indexStart = currentIndex;
                long indexEnd = currentIndex + (long)indexInfo.NumberOfValues;

                // Check if this index overlaps with our requested range
                if (indexEnd <= startIndex)
                {
                    currentIndex = indexEnd;
                    continue; // This index is entirely before our range
                }

                if (indexStart >= startIndex + count)
                {
                    break; // We've read everything we need
                }

                // Calculate the range to read from this index
                long readStart = Math.Max(0, startIndex - indexStart);
                long readEnd = Math.Min((long)indexInfo.NumberOfValues, startIndex + count - indexStart);
                int readCount = (int)(readEnd - readStart);

                var segment = indexInfo.Segment;
                if (segment.IsInterleaved) throw new NotSupportedException("Interleaved data reading is not yet supported.");

                _isBigEndian = segment.IsBigEndian;

                // Read the data from this index
                var chunkData = ReadDataFromSegment<T>(channel, segment, indexInfo, (int)readStart, readCount);
                Array.Copy(chunkData, 0, result, resultOffset, chunkData.Length);
                resultOffset += chunkData.Length;

                currentIndex = indexEnd;
            }

            return result;
        }

        private T[] ReadDataFromSegment<T>(TdmsChannelReader channel, TdmsSegment segment, RawDataIndexInfo indexInfo, int skipValues, int readCount) where T : unmanaged
        {
            var values = new List<T>();
            long singleChunkTotalSize = segment.ActiveChannels.Sum(ch => (long)GetChunkDataSize(ch, segment));
            long startOffsetInChunk = GetChannelOffsetInChunk(channel, segment);
            long firstChunkStart = segment.AbsoluteOffset + 28 + segment.RawDataOffset;

            int valuesPerChunk = (int)indexInfo.NumberOfValues;
            int typeSize = TdmsDataTypeSizeHelper.GetSize(indexInfo.DataType);

            int remainingSkip = skipValues;
            int remainingRead = readCount;

            for (ulong i = 0; i < segment.ChunkCount && remainingRead > 0; i++)
            {
                long currentChunkStart = firstChunkStart + ((long)i * singleChunkTotalSize);
                long channelDataAbsolutePos = currentChunkStart + startOffsetInChunk;

                // Determine how many values to skip and read in this chunk
                if (remainingSkip >= valuesPerChunk)
                {
                    remainingSkip -= valuesPerChunk;
                    continue;
                }

                int skipInChunk = remainingSkip;
                int readInChunk = Math.Min(valuesPerChunk - skipInChunk, remainingRead);

                _stream.Seek(channelDataAbsolutePos + (skipInChunk * typeSize), SeekOrigin.Begin);
                var chunkValues = TdmsDataTypeReader.ReadArray<T>(_stream, readInChunk, _isBigEndian);
                values.AddRange(chunkValues);

                remainingSkip = 0;
                remainingRead -= readInChunk;
            }

            return values.ToArray();
        }

        // Original method - reads all strings at once
        internal string[] ReadStringChannelData(TdmsChannelReader channel)
        {
            long totalValues = (long)channel.DataIndices.Sum(idx => (decimal)idx.NumberOfValues);
            if (totalValues > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Channel has {totalValues} values which exceeds array size limit. Use ReadStringChannelDataChunk or StreamStringData instead.");
            }
            return ReadStringChannelDataChunk(channel, 0, (int)totalValues);
        }

        // New method - reads a chunk of strings
        internal string[] ReadStringChannelDataChunk(TdmsChannelReader channel, long startIndex, int count)
        {
            if (count <= 0) return Array.Empty<string>();

            var result = new string[count];
            int resultOffset = 0;
            long currentIndex = 0;

            foreach (var indexInfo in channel.DataIndices)
            {
                if (indexInfo.NumberOfValues == 0) continue;

                long indexStart = currentIndex;
                long indexEnd = currentIndex + (long)indexInfo.NumberOfValues;

                if (indexEnd <= startIndex)
                {
                    currentIndex = indexEnd;
                    continue;
                }

                if (indexStart >= startIndex + count)
                {
                    break;
                }

                long readStart = Math.Max(0, startIndex - indexStart);
                long readEnd = Math.Min((long)indexInfo.NumberOfValues, startIndex + count - indexStart);
                int readCount = (int)(readEnd - readStart);

                var segment = indexInfo.Segment;
                if (segment.IsInterleaved) throw new NotSupportedException("Interleaved string data is not supported.");

                _isBigEndian = segment.IsBigEndian;

                var chunkData = ReadStringDataFromSegment(channel, segment, indexInfo, (int)readStart, readCount);
                Array.Copy(chunkData, 0, result, resultOffset, chunkData.Length);
                resultOffset += chunkData.Length;

                currentIndex = indexEnd;
            }

            return result;
        }

        private string[] ReadStringDataFromSegment(TdmsChannelReader channel, TdmsSegment segment, RawDataIndexInfo indexInfo, int skipValues, int readCount)
        {
            var values = new List<string>();
            long singleChunkTotalSize = segment.ActiveChannels.Sum(ch => (long)GetChunkDataSize(ch, segment));
            long startOffsetInChunk = GetChannelOffsetInChunk(channel, segment);
            long firstChunkStart = segment.AbsoluteOffset + 28 + segment.RawDataOffset;

            int valuesPerChunk = (int)indexInfo.NumberOfValues;
            int remainingSkip = skipValues;
            int remainingRead = readCount;

            for (ulong i = 0; i < segment.ChunkCount && remainingRead > 0; i++)
            {
                long currentChunkStart = firstChunkStart + ((long)i * singleChunkTotalSize);
                long channelDataAbsolutePos = currentChunkStart + startOffsetInChunk;

                if (remainingSkip >= valuesPerChunk)
                {
                    remainingSkip -= valuesPerChunk;
                    continue;
                }

                // For strings, we need to read all strings in the chunk and then slice
                _stream.Seek(channelDataAbsolutePos, SeekOrigin.Begin);
                var allStringsInChunk = TdmsDataTypeReader.ReadStringArray(_stream, indexInfo.NumberOfValues, _isBigEndian);

                int skipInChunk = remainingSkip;
                int readInChunk = Math.Min(valuesPerChunk - skipInChunk, remainingRead);

                for (int j = 0; j < readInChunk; j++)
                {
                    values.Add(allStringsInChunk[skipInChunk + j]);
                }

                remainingSkip = 0;
                remainingRead -= readInChunk;
            }

            return values.ToArray();
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