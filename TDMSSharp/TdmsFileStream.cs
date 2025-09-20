using System;
using System.IO;

namespace TDMSSharp
{
    public class TdmsFileStream : IDisposable
    {
        private readonly TdmsFile _file;
        private readonly StreamingTdmsWriter _writer;
        private readonly FileStream _stream;

        public TdmsFileStream(string path)
        {
            _stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            _file = new TdmsFile();
            _writer = new StreamingTdmsWriter(_stream, _file);
            _writer.WriteHeader();
        }

        public void AppendData<T>(string groupName, string channelName, T[] data)
        {
            var group = _file.GetOrAddChannelGroup(groupName);
            var channel = group.GetOrAddChannel(channelName);
            if (channel.DataType == TdsDataType.Void)
            {
                channel.DataType = TdsDataTypeProvider.GetDataType<T>();
            }
            else if (channel.DataType != TdsDataTypeProvider.GetDataType<T>())
            {
                throw new InvalidOperationException("Data type of channel cannot be changed.");
            }
            _writer.WriteSegment(new[] { channel }, new object[] { data });
        }

        public void AppendValue<T>(string groupName, string channelName, T value)
        {
            AppendData(groupName, channelName, new T[] { value });
        }

        public void Dispose()
        {
            _writer.Dispose();
            _stream.Dispose();
        }
    }
}
