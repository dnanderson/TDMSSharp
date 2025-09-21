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
                _writer.Write((ulong)(currentSegmentLeadInStart - (_previousSegmentLeadInStart + 28)));
                _writer.BaseStream.Seek(returnPos, SeekOrigin.Begin);
            }

            // Write the new segment's lead-in
            uint tocMask = (1 << 1); // Meta data is always written
            if (newObjects) tocMask |= (1 << 2); // New object list
            if (rawDataLength > 0) tocMask |= (1 << 3); // Raw data

            _writer.Write(Encoding.ASCII.GetBytes("TDSm"));
            _writer.Write(tocMask);
            _writer.Write((uint)4713); // Version
            _writer.Write((ulong)0); // Next segment offset (will be updated by next segment or on close)
            _writer.Write((ulong)metaDataLength); // Raw data offset is the length of metadata

            // Write the actual segment data
            _writer.Write(segmentStream.ToArray());
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
                if (obj is TdmsFile file) TdmsWriter.WriteObjectMetaData(writer, "/", file.Properties);
                else if (obj is TdmsChannelGroup group) TdmsWriter.WriteObjectMetaData(writer, group.Path, group.Properties);
                else if (obj is TdmsChannel channel) TdmsWriter.WriteObjectMetaData(writer, channel.Path, channel.Properties, channel, valueCounts[channel]);
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
