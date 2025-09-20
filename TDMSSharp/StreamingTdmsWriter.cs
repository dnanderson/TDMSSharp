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
        private long _nextSegmentOffset = 0;
        private long _currentSegmentOffset = 0;
        private readonly HashSet<string> _writtenObjects = new HashSet<string>();

        public StreamingTdmsWriter(Stream stream, TdmsFile file)
        {
            _writer = new BinaryWriter(stream, Encoding.UTF8, false);
            _file = file;
        }

        public void WriteHeader()
        {
            _writer.Write(Encoding.ASCII.GetBytes("TDSm"));
            _writer.Write((uint)0); // ToC mask, will be updated
            _writer.Write((uint)4713); // Version
            _writer.Write((ulong)0); // Next segment offset, will be updated
            _writer.Write((ulong)0); // Raw data offset, will be updated
        }

        public void WriteSegment<T>(TdmsChannel channel, T[] data)
        {
            var channels = new[] { channel };
            var dataArrays = new object[] { data };
            WriteSegment(channels, dataArrays);
        }

        public void WriteSegment(TdmsChannel[] channels, object[] dataArrays)
        {
            _currentSegmentOffset = _writer.BaseStream.Position;
            if (_currentSegmentOffset > 28)
            {
                var previousSegmentLeadIn = _currentSegmentOffset - _nextSegmentOffset;
                _writer.BaseStream.Seek(_currentSegmentOffset - previousSegmentLeadIn + 12, SeekOrigin.Begin);
                _writer.Write((ulong)_nextSegmentOffset);
                _writer.BaseStream.Seek(_currentSegmentOffset, SeekOrigin.Begin);
            }

            _writer.BaseStream.Seek(28, SeekOrigin.Current);

            long metaDataStart = _writer.BaseStream.Position;
            WriteMetaData(channels);
            long metaDataEnd = _writer.BaseStream.Position;
            long metaDataLength = metaDataEnd - metaDataStart;

            long rawDataStart = _writer.BaseStream.Position;
            for (int i = 0; i < channels.Length; i++)
            {
                var channel = channels[i];
                var data = (Array)dataArrays[i];
                channel.NumberOfValues += (ulong)data.Length;
                TdmsWriter.WriteRawData(_writer, data);
            }
            long rawDataEnd = _writer.BaseStream.Position;
            long rawDataLength = rawDataEnd - rawDataStart;

            _nextSegmentOffset = metaDataLength + rawDataLength;

            _writer.BaseStream.Seek(_currentSegmentOffset, SeekOrigin.Begin);

            uint tocMask = 0;
            tocMask |= (1 << 1); // kTocMetaData
            if (rawDataLength > 0) tocMask |= (1 << 3); // kTocRawData
            if (metaDataLength > 4) tocMask |= (1 << 2); // kTocNewObjList

            _writer.Write(Encoding.ASCII.GetBytes("TDSm"));
            _writer.Write(tocMask);
            _writer.Write((uint)4713); // Version
            _writer.Write((ulong)_nextSegmentOffset);
            _writer.Write((ulong)metaDataLength); // Raw data offset

            _writer.BaseStream.Seek(0, SeekOrigin.End);
        }

        private void WriteMetaData(TdmsChannel[] channels)
        {
            var objects = new List<object>();
            if (_writtenObjects.Count == 0)
            {
                objects.Add(_file);
                _writtenObjects.Add("/");
            }

            foreach (var channel in channels)
            {
                var groupPath = Path.GetDirectoryName(channel.Path);
                if (groupPath == null) continue;
                groupPath = groupPath.Replace("\\", "/");
                if (!_writtenObjects.Contains(groupPath))
                {
                    var group = _file.GetOrAddChannelGroup(groupPath);
                    objects.Add(group);
                    _writtenObjects.Add(groupPath);
                }
                if (!_writtenObjects.Contains(channel.Path))
                {
                    objects.Add(channel);
                    _writtenObjects.Add(channel.Path);
                }
            }

            _writer.Write((uint)objects.Count);

            foreach (var obj in objects)
            {
                if (obj is TdmsFile file) TdmsWriter.WriteObjectMetaData(_writer, "/", file.Properties);
                else if (obj is TdmsChannelGroup group) TdmsWriter.WriteObjectMetaData(_writer, group.Path, group.Properties);
                else if (obj is TdmsChannel channel) TdmsWriter.WriteObjectMetaData(_writer, channel.Path, channel.Properties, channel);
            }
        }

        public void Dispose()
        {
            _writer?.Dispose();
        }
    }
}
