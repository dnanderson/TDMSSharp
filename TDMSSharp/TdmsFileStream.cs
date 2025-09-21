using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TDMSSharp
{
    public class TdmsFileStream : IDisposable
    {
        private readonly TdmsFile _file;
        private readonly StreamingTdmsWriter _writer;
        private readonly FileStream _stream;
        private readonly Dictionary<string, TdmsChannel> _channelCache;
        private const int BufferSize = 65536; // 64KB buffer

        public TdmsFileStream(string path)
        {
            _stream = new FileStream(path, FileMode.Create, FileAccess.Write, 
                                    FileShare.None, BufferSize, FileOptions.SequentialScan);
            _file = new TdmsFile();
            _writer = new StreamingTdmsWriter(_stream, _file);
            _channelCache = new Dictionary<string, TdmsChannel>();
        }

        public void AppendData<T>(string groupName, string channelName, T[] data)
        {
            if (data == null || data.Length == 0)
                return;

            var group = _file.GetOrAddChannelGroup(groupName);
            var channelPath = $"{group.Path}/'{channelName.Replace("'", "''")}'";
            
            TdmsChannel channel;
            
            // Use cached channel for better performance
            if (!_channelCache.TryGetValue(channelPath, out channel))
            {
                channel = group.Channels.FirstOrDefault(c => c.Path == channelPath);
                
                if (channel == null)
                {
                    channel = group.AddChannel<T>(channelName);
                }
                else
                {
                    var expectedDataType = TdsDataTypeProvider.GetDataType<T>();
                    if (channel.DataType != expectedDataType)
                    {
                        throw new InvalidOperationException(
                            $"Data type mismatch for channel '{channelName}'. " +
                            $"Expected {channel.DataType}, got {expectedDataType}");
                    }
                }
                
                _channelCache[channelPath] = channel;
            }
            
            _writer.WriteSegment(new[] { channel }, new object[] { data });
        }

        public void AppendValue<T>(string groupName, string channelName, T value)
        {
            AppendData(groupName, channelName, new T[] { value });
        }

        /// <summary>
        /// Batch append multiple channels at once for better performance
        /// </summary>
        public void AppendBatch(params (string GroupName, string ChannelName, Array Data)[] channelData)
        {
            if (channelData == null || channelData.Length == 0)
                return;

            var channels = new TdmsChannel[channelData.Length];
            var dataArrays = new object[channelData.Length];

            for (int i = 0; i < channelData.Length; i++)
            {
                var (groupName, channelName, data) = channelData[i];
                
                var group = _file.GetOrAddChannelGroup(groupName);
                var channelPath = $"{group.Path}/'{channelName.Replace("'", "''")}'";
                
                TdmsChannel channel;
                if (!_channelCache.TryGetValue(channelPath, out channel))
                {
                    channel = group.Channels.FirstOrDefault(c => c.Path == channelPath);
                    
                    if (channel == null)
                    {
                        var elementType = data.GetType().GetElementType();
                        var addChannelMethod = typeof(TdmsChannelGroup)
                            .GetMethod(nameof(TdmsChannelGroup.AddChannel))
                            .MakeGenericMethod(elementType);
                        channel = (TdmsChannel)addChannelMethod.Invoke(group, new object[] { channelName });
                    }
                    
                    _channelCache[channelPath] = channel;
                }
                
                channels[i] = channel;
                dataArrays[i] = data;
            }

            _writer.WriteSegment(channels, dataArrays);
        }

        /// <summary>
        /// Add file-level property
        /// </summary>
        public void AddFileProperty<T>(string name, T value)
        {
            _file.AddProperty(name, value);
        }

        /// <summary>
        /// Add group-level property
        /// </summary>
        public void AddGroupProperty<T>(string groupName, string propertyName, T value)
        {
            var group = _file.GetOrAddChannelGroup(groupName);
            group.AddProperty(propertyName, value);
        }

        /// <summary>
        /// Add channel-level property
        /// </summary>
        public void AddChannelProperty<T>(string groupName, string channelName, string propertyName, T value)
        {
            var group = _file.GetOrAddChannelGroup(groupName);
            var channelPath = $"{group.Path}/'{channelName.Replace("'", "''")}'";
            var channel = group.Channels.FirstOrDefault(c => c.Path == channelPath);
            
            if (channel == null)
            {
                throw new InvalidOperationException(
                    $"Channel '{channelName}' does not exist in group '{groupName}'. " +
                    "Add data to the channel before setting properties.");
            }
            
            channel.AddProperty(propertyName, value);
        }

        /// <summary>
        /// Flush any buffered data to disk
        /// </summary>
        public void Flush()
        {
            _stream.Flush(flushToDisk: true);
        }

        public void Dispose()
        {
            _writer?.Dispose();
            _stream?.Dispose();
            _channelCache?.Clear();
        }
    }
}