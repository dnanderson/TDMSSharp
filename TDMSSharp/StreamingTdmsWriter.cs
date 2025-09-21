using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TDMSSharp
{
    public class StreamingTdmsWriter : IDisposable
    {
        private readonly BinaryWriter _writer;
        private readonly TdmsFile _file;
        private long _previousSegmentLeadInStart = -1;
        private readonly HashSet<string> _writtenObjects = new HashSet<string>();

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

            // Write metadata and raw data to a temporary stream to calculate their size
            using var segmentStream = new MemoryStream();
            using var segmentWriter = new BinaryWriter(segmentStream);

            var newObjects = WriteMetaData(segmentWriter, channels, valueCounts);
            long metaDataLength = segmentStream.Position;

            for (int i = 0; i < channels.Length; i++)
            {
                var channel = channels[i];
                var data = (Array)dataArrays[i];
                channel.NumberOfValues += (ulong)data.Length;
                TdmsWriter.WriteRawData(segmentWriter, data);
            }
            long rawDataLength = segmentStream.Position - metaDataLength;

            // Now that we have the lengths, update the previous segment to point to this one
            if (_previousSegmentLeadInStart != -1)
            {
                long returnPos = _writer.BaseStream.Position;
                _writer.BaseStream.Seek(_previousSegmentLeadInStart + 12, SeekOrigin.Begin);

                // Calculate offset from end of previous segment's lead-in to start of current segment
                long previousSegmentEnd = _previousSegmentLeadInStart + 28;
                long offsetToNextSegment = currentSegmentLeadInStart - previousSegmentEnd;
                _writer.Write((ulong)offsetToNextSegment);

                _writer.BaseStream.Seek(returnPos, SeekOrigin.Begin);
            }

            // Write the new segment's lead-in
            uint tocMask = (1 << 1); // Meta data is always written
            if (newObjects) tocMask |= (1 << 2); // New object list
            if (rawDataLength > 0) tocMask |= (1 << 3); // Raw data

            _writer.Write(Encoding.ASCII.GetBytes("TDSm"));
            _writer.Write(tocMask);
            _writer.Write((uint)4713); // Version
            _writer.Write((ulong)(metaDataLength + rawDataLength)); // Next segment offset (fixed)
            _writer.Write((ulong)metaDataLength); // Raw data offset

            // Write the actual segment data
            _writer.Write(segmentStream.ToArray());
            _writer.Flush();

            _previousSegmentLeadInStart = currentSegmentLeadInStart;
        }

        private readonly Dictionary<string, TdmsRawDataIndex> _channelIndices = new Dictionary<string, TdmsRawDataIndex>();

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
                        // Write 0x00000000 to indicate unchanged index
                        writer.Write((uint)0x00000000);
                    }
                    else
                    {
                        // Write full index
                        using (var ms = new MemoryStream())
                        using (var tempWriter = new BinaryWriter(ms))
                        {
                            tempWriter.Write((uint)channel.DataType);
                            tempWriter.Write((uint)1); // Dimension
                            tempWriter.Write(valueCounts[channel]);
                            if (channel.DataType == TdsDataType.String && channel.Data != null)
                            {
                                var totalBytes = 0UL;
                                foreach (var s in (string[])channel.Data)
                                    totalBytes += (ulong)Encoding.UTF8.GetByteCount(s);
                                tempWriter.Write(totalBytes);
                            }
                            writer.Write((uint)ms.Length);
                            writer.Write(ms.ToArray());
                        }

                        // Update tracking
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

        private string GetGroupName(string channelPath)
        {
            // Path is /'group'/'channel'
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
                _writer.Write(lastSegmentDataLength);
            }
            _writer?.Dispose();
        }
    }
}
