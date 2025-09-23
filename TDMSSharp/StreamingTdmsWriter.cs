using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TDMSSharp
{
    /// <summary>
    /// Provides a mechanism for writing TDMS data in a streaming fashion, which is suitable for large datasets.
    /// </summary>
    public class StreamingTdmsWriter : IDisposable
    {
        private readonly BinaryWriter _writer;
        private readonly TdmsFile _file;
        private long _previousSegmentLeadInStart = -1;
        private readonly HashSet<string> _writtenObjects = new HashSet<string>();
        private readonly Dictionary<string, TdmsRawDataIndex> _channelIndices = new Dictionary<string, TdmsRawDataIndex>();

        /// <summary>
        /// Initializes a new instance of the <see cref="StreamingTdmsWriter"/> class.
        /// </summary>
        /// <param name="stream">The stream to write TDMS data to.</param>
        /// <param name="file">The TDMS file object containing metadata.</param>
        public StreamingTdmsWriter(Stream stream, TdmsFile file)
        {
            _writer = new BinaryWriter(stream, Encoding.UTF8, true);
            _file = file;
        }

        /// <summary>
        /// Writes the file header and initial metadata segment. This should be called before writing data segments.
        /// </summary>
        public void WriteFileHeader()
        {
            if (_writer.BaseStream.Position == 0 && _file.Properties.Count == 0 && _file.ChannelGroups.Count == 0) return;

            long currentSegmentLeadInStart = _writer.BaseStream.Position;

            // Reserve space for lead-in
            long leadInPosition = _writer.BaseStream.Position;
            _writer.Write(new byte[28]);

            long metaDataStart = _writer.BaseStream.Position;
            WriteAllMetaData(_writer);
            long metaDataLength = _writer.BaseStream.Position - metaDataStart;

            // Update previous segment's next offset
            if (_previousSegmentLeadInStart != -1)
            {
                long currentPos = _writer.BaseStream.Position;
                _writer.BaseStream.Seek(_previousSegmentLeadInStart + 12, SeekOrigin.Begin);
                _writer.Write((ulong)(currentSegmentLeadInStart - (_previousSegmentLeadInStart + 28)));
                _writer.BaseStream.Seek(currentPos, SeekOrigin.Begin);
            }

            // Write lead-in at reserved position
            long endPosition = _writer.BaseStream.Position;
            _writer.BaseStream.Seek(leadInPosition, SeekOrigin.Begin);

            uint tocMask = (1 << 1) | (1 << 2); // Meta data and New object list

            _writer.Write(Encoding.ASCII.GetBytes("TDSm"));
            _writer.Write(tocMask);
            _writer.Write((uint)4713);
            _writer.Write((ulong)metaDataLength); // Segment length
            _writer.Write((ulong)metaDataLength); // Meta data length

            _writer.BaseStream.Seek(endPosition, SeekOrigin.Begin);
            _writer.Flush();

            _previousSegmentLeadInStart = currentSegmentLeadInStart;
        }

        /// <summary>
        /// Gets or sets a value indicating whether metadata has been modified and needs to be written to the stream.
        /// </summary>
        public bool MetadataDirty { get; set; }

        /// <summary>
        /// Writes a segment of data to the TDMS file.
        /// </summary>
        /// <param name="channels">The channels to write data for.</param>
        /// <param name="dataArrays">The corresponding data arrays for each channel.</param>
        public void WriteSegment(TdmsChannel[] channels, object[] dataArrays)
        {
            if (MetadataDirty) WriteMetadataSegment();

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
                _writer.Write((ulong)(currentSegmentLeadInStart - (_previousSegmentLeadInStart + 28)));
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

        private void WriteAllMetaData(BinaryWriter writer)
        {
            var objects = new List<object> { _file };
            objects.AddRange(_file.ChannelGroups);
            foreach (var group in _file.ChannelGroups)
                objects.AddRange(group.Channels);

            writer.Write((uint)objects.Count);

            foreach (var obj in objects)
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
                    TdmsWriter.WriteObjectMetaData(writer, channel.Path, channel.Properties, channel, 0);
                }
            }
            _writtenObjects.Clear();
            _writtenObjects.Add("/");
            foreach (var group in _file.ChannelGroups) _writtenObjects.Add(group.Path);
            foreach (var group in _file.ChannelGroups)
                foreach (var channel in group.Channels) _writtenObjects.Add(channel.Path);
        }

        private TdmsFile _lastWrittenState = new TdmsFile();

        private void WriteMetadataSegment()
        {
            long currentSegmentLeadInStart = _writer.BaseStream.Position;

            // Reserve space for lead-in
            long leadInPosition = _writer.BaseStream.Position;
            _writer.Write(new byte[28]);

            long metaDataStart = _writer.BaseStream.Position;

            var changedObjects = new List<object>();

            var lastWrittenFileProps = _lastWrittenState.Properties.ToDictionary(p => p.Name);
            var currentFileProps = _file.Properties.Where(p => !lastWrittenFileProps.ContainsKey(p.Name) || !p.Equals(lastWrittenFileProps[p.Name])).ToList();
            if (currentFileProps.Any())
            {
                var file = new TdmsFile();
                foreach(var p in currentFileProps) file.Properties.Add(p);
                changedObjects.Add(file);
            }

            var lastWrittenGroups = _lastWrittenState.ChannelGroups.ToDictionary(g => g.Path);
            foreach (var group in _file.ChannelGroups)
            {
                if (!lastWrittenGroups.TryGetValue(group.Path, out var lastWrittenGroup))
                {
                    changedObjects.Add(group);
                }
                else
                {
                    var lastWrittenGroupProps = lastWrittenGroup.Properties.ToDictionary(p => p.Name);
                    var currentGroupProps = group.Properties.Where(p => !lastWrittenGroupProps.ContainsKey(p.Name) || !p.Equals(lastWrittenGroupProps[p.Name])).ToList();
                    if (currentGroupProps.Any())
                    {
                        var newGroup = new TdmsChannelGroup(group.Path);
                        foreach(var p in currentGroupProps) newGroup.Properties.Add(p);
                        changedObjects.Add(newGroup);
                    }

                    var lastWrittenChannels = lastWrittenGroup.Channels.ToDictionary(c => c.Path);
                    foreach (var channel in group.Channels)
                    {
                        if (!lastWrittenChannels.TryGetValue(channel.Path, out var lastWrittenChannel))
                        {
                            changedObjects.Add(channel);
                        }
                        else
                        {
                            var lastWrittenChannelProps = lastWrittenChannel.Properties.ToDictionary(p => p.Name);
                            var currentChannelProps = channel.Properties.Where(p => !lastWrittenChannelProps.ContainsKey(p.Name) || !p.Equals(lastWrittenChannelProps[p.Name])).ToList();
                            if (currentChannelProps.Any())
                            {
                                var newChannel = new TdmsChannel(channel.Path) { DataType = channel.DataType };
                                foreach(var p in currentChannelProps) newChannel.Properties.Add(p);
                                changedObjects.Add(newChannel);
                            }
                        }
                    }
                }
            }

            _writer.Write((uint)changedObjects.Count);
            foreach(var o in changedObjects)
            {
                if (o is TdmsFile f) TdmsWriter.WriteObjectMetaData(_writer, "/", f.Properties);
                else if (o is TdmsChannelGroup g) TdmsWriter.WriteObjectMetaData(_writer, g.Path, g.Properties);
                else if (o is TdmsChannel c) TdmsWriter.WriteObjectMetaData(_writer, c.Path, c.Properties, c, 0);
            }

            _lastWrittenState = _file.DeepClone();

            long metaDataLength = _writer.BaseStream.Position - metaDataStart;

            // Update previous segment's next offset
            if (_previousSegmentLeadInStart != -1)
            {
                long currentPos = _writer.BaseStream.Position;
                _writer.BaseStream.Seek(_previousSegmentLeadInStart + 12, SeekOrigin.Begin);
                _writer.Write((ulong)(currentSegmentLeadInStart - (_previousSegmentLeadInStart + 28)));
                _writer.BaseStream.Seek(currentPos, SeekOrigin.Begin);
            }

            // Write lead-in at reserved position
            long endPosition = _writer.BaseStream.Position;
            _writer.BaseStream.Seek(leadInPosition, SeekOrigin.Begin);

            uint tocMask = (1 << 1) | (1 << 2); // Meta data and New object list

            _writer.Write(Encoding.ASCII.GetBytes("TDSm"));
            _writer.Write(tocMask);
            _writer.Write((uint)4713);
            _writer.Write((ulong)metaDataLength); // Segment length
            _writer.Write((ulong)metaDataLength); // Meta data length

            _writer.BaseStream.Seek(endPosition, SeekOrigin.Begin);
            _writer.Flush();

            _previousSegmentLeadInStart = currentSegmentLeadInStart;
            MetadataDirty = false;
            _writtenObjects.Clear();
            _writtenObjects.Add("/");
            foreach(var g in _file.ChannelGroups) _writtenObjects.Add(g.Path);
            foreach(var g in _file.ChannelGroups)
                foreach(var c in g.Channels) _writtenObjects.Add(c.Path);
        }

        private bool WriteMetaData(BinaryWriter writer, TdmsChannel[] channels, Dictionary<TdmsChannel, ulong> valueCounts)
        {
            var objectsToWrite = new List<object>();
            bool newObjects = false;

            if (!_writtenObjects.Contains("/"))
            {
                objectsToWrite.Add(_file);
                newObjects = true;
            }

            foreach (var channel in channels)
            {
                var groupName = GetGroupName(channel.Path);
                var groupPath = $"/'{groupName}'";

                if (!_writtenObjects.Contains(groupPath))
                {
                    var group = _file.GetOrAddChannelGroup(groupName);
                    objectsToWrite.Add(group);
                    newObjects = true;
                }

                objectsToWrite.Add(channel);
                if (!_writtenObjects.Contains(channel.Path))
                {
                    newObjects = true;
                }
            }

            writer.Write((uint)objectsToWrite.Count);

            foreach (var obj in objectsToWrite)
            {
                if (obj is TdmsFile file)
                {
                    TdmsWriter.WriteObjectMetaData(writer, "/", file.Properties);
                    _writtenObjects.Add("/");
                }
                else if (obj is TdmsChannelGroup group)
                {
                    TdmsWriter.WriteObjectMetaData(writer, group.Path, group.Properties);
                    _writtenObjects.Add(group.Path);
                }
                else if (obj is TdmsChannel channel)
                {
                    TdmsWriter.WriteObjectMetaData(writer, channel.Path, channel.Properties, channel, valueCounts[channel]);
                    _writtenObjects.Add(channel.Path);
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

        /// <summary>
        /// Finalizes the TDMS file by updating segment information and disposes the writer.
        /// </summary>
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