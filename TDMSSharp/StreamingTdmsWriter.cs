using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TDMSSharp
{
    public class StreamingTdmsWriter : IDisposable
    {
        private readonly BinaryWriter _writer;
        private readonly TdmsFile _file;
        private long _previousSegmentLeadInStart = -1;
        private readonly HashSet<string> _writtenObjects = new HashSet<string>();
        private readonly Dictionary<string, TdmsRawDataIndex> _channelIndices = new Dictionary<string, TdmsRawDataIndex>();

        public StreamingTdmsWriter(Stream stream, TdmsFile file)
        {
            _writer = new BinaryWriter(stream, Encoding.UTF8, true);
            _file = file;
        }

        public void WriteSegment(TdmsChannel[] channels, object[] dataArrays)
        {
            long currentSegmentLeadInStart = _writer.BaseStream.Position;

            var valueCounts = new Dictionary<TdmsChannel, ulong>();
            for (int i = 0; i < channels.Length; i++)
            {
                var data = (Array)dataArrays[i];
                valueCounts[channels[i]] = (ulong)data.Length;
            }

            // Reserve space for lead-in (28 bytes)
            long leadInPosition = _writer.BaseStream.Position;
            _writer.Write(new byte[28]);

            long metaDataStart = _writer.BaseStream.Position;
            var newObjects = WriteMetaData(_writer, channels, valueCounts);
            long metaDataLength = _writer.BaseStream.Position - metaDataStart;

            // Write raw data directly
            long rawDataStart = _writer.BaseStream.Position;
            for (int i = 0; i < channels.Length; i++)
            {
                var channel = channels[i];
                var data = (Array)dataArrays[i];
                channel.NumberOfValues += (ulong)data.Length;
                TdmsWriter.WriteRawData(_writer, data);
            }
            long rawDataLength = _writer.BaseStream.Position - rawDataStart;

            // Update previous segment's next offset
            if (_previousSegmentLeadInStart != -1)
            {
                long currentPos = _writer.BaseStream.Position;
                _writer.BaseStream.Seek(_previousSegmentLeadInStart + 12, SeekOrigin.Begin);

                long previousSegmentEnd = _previousSegmentLeadInStart + 28;
                long offsetToNextSegment = currentSegmentLeadInStart - previousSegmentEnd;
                _writer.Write((ulong)offsetToNextSegment);

                _writer.BaseStream.Seek(currentPos, SeekOrigin.Begin);
            }

            // Write lead-in at reserved position
            long endPosition = _writer.BaseStream.Position;
            _writer.BaseStream.Seek(leadInPosition, SeekOrigin.Begin);

            uint tocMask = (1 << 1); // Meta data
            if (newObjects) tocMask |= (1 << 2); // New object list
            if (rawDataLength > 0) tocMask |= (1 << 3); // Raw data

            _writer.Write(Encoding.ASCII.GetBytes("TDSm"));
            _writer.Write(tocMask);
            _writer.Write((uint)4713);
            _writer.Write((ulong)(metaDataLength + rawDataLength));
            _writer.Write((ulong)metaDataLength);

            _writer.BaseStream.Seek(endPosition, SeekOrigin.Begin);
            _writer.Flush();

            _previousSegmentLeadInStart = currentSegmentLeadInStart;
        }

        private bool WriteMetaData(BinaryWriter writer, TdmsChannel[] channels, Dictionary<TdmsChannel, ulong> valueCounts)
        {
            var objectsToWrite = new List<object>();
            bool newObjects = false;
            
            if (_writtenObjects.Count == 0)
            {
                objectsToWrite.Add(_file);
                _writtenObjects.Add("/");
            }

            foreach (var channel in channels)
            {
                var groupName = GetGroupName(channel.Path);
                var groupPath = $"/'{groupName}'";

                if (!_writtenObjects.Contains(groupPath))
                {
                    var group = _file.GetOrAddChannelGroup(groupName);
                    objectsToWrite.Add(group);
                    _writtenObjects.Add(groupPath);
                    newObjects = true;
                }
                if (!_writtenObjects.Contains(channel.Path))
                {
                    newObjects = true;
                    _writtenObjects.Add(channel.Path);
                }
                objectsToWrite.Add(channel);
            }

            writer.Write((uint)objectsToWrite.Count);

            foreach (var obj in objectsToWrite)
            {
                if (obj is TdmsFile file)
                {
                    TdmsWriter.WriteObjectMetaData(writer, "/", file.Properties);
                }
                else if (obj is TdmsChannelGroup group)
                {
                    TdmsWriter.WriteObjectMetaData(writer, group.Path, group.Properties);
                }
                else if (obj is TdmsChannel channel)
                {
                    TdmsWriter.WriteString(writer, channel.Path);

                    var currentIndex = new TdmsRawDataIndex(channel.DataType, valueCounts[channel]);

                    // Check if index matches previous segment
                    if (_channelIndices.TryGetValue(channel.Path, out var previousIndex) &&
                        previousIndex.DataType == currentIndex.DataType &&
                        previousIndex.NumberOfValues == currentIndex.NumberOfValues)
                    {
                        writer.Write((uint)0x00000000);
                    }
                    else
                    {
                        WriteRawDataIndex(writer, channel, valueCounts[channel]);
                        _channelIndices[channel.Path] = currentIndex;
                    }

                    // Write properties
                    writer.Write((uint)channel.Properties.Count);
                    foreach (var prop in channel.Properties)
                    {
                        TdmsWriter.WriteString(writer, prop.Name);
                        writer.Write((uint)prop.DataType);
                        if (prop.Value != null)
                            TdmsWriter.WriteValue(writer, prop.Value, prop.DataType);
                    }
                }
            }

            return newObjects;
        }

        private void WriteRawDataIndex(BinaryWriter writer, TdmsChannel channel, ulong valueCount)
        {
            // Use stack allocation for small buffers
            Span<byte> buffer = stackalloc byte[32];
            int offset = 0;

            BitConverter.TryWriteBytes(buffer.Slice(offset, 4), (uint)channel.DataType);
            offset += 4;
            BitConverter.TryWriteBytes(buffer.Slice(offset, 4), (uint)1); // Dimension
            offset += 4;
            BitConverter.TryWriteBytes(buffer.Slice(offset, 8), valueCount);
            offset += 8;

            if (channel.DataType == TdsDataType.String && channel.Data != null)
            {
                var totalBytes = 0UL;
                foreach (var s in (string[])channel.Data)
                    totalBytes += (ulong)Encoding.UTF8.GetByteCount(s);
                BitConverter.TryWriteBytes(buffer.Slice(offset, 8), totalBytes);
                offset += 8;
            }

            writer.Write((uint)offset);
            writer.Write(buffer.Slice(0, offset));
        }

        private string GetGroupName(string channelPath)
        {
            var parts = channelPath.Split(new[] { "'/'" }, StringSplitOptions.None);
            if (parts.Length < 2) throw new ArgumentException("Invalid channel path", nameof(channelPath));
            return parts[0].TrimStart('/').Trim('\'');
        }

        public void Dispose()
        {
            if (_previousSegmentLeadInStart != -1)
            {
                long totalLength = _writer.BaseStream.Position;
                long lastSegmentDataLength = totalLength - (_previousSegmentLeadInStart + 28);
                _writer.BaseStream.Seek(_previousSegmentLeadInStart + 12, SeekOrigin.Begin);
                _writer.Write((ulong)lastSegmentDataLength);
            }
            _writer?.Dispose();
        }
    }
}