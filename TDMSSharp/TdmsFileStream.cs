using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TDMSSharp
{
    /// <summary>
    /// Provides a high-level API for writing data to a TDMS file in a streaming fashion.
    /// </summary>
    public class TdmsFileStream : IDisposable
    {
        private readonly TdmsFile _file;
        private readonly StreamingTdmsWriter _writer;
        private readonly FileStream _stream;
        private readonly Dictionary<string, TdmsChannel> _channelCache;
        private const int BufferSize = 65536; // 64KB buffer

        /// <summary>
        /// Initializes a new instance of the <see cref="TdmsFileStream"/> class.
        /// </summary>
        /// <param name="path">The path of the TDMS file to create.</param>
        public TdmsFileStream(string path)
        {
            _stream = new FileStream(path, FileMode.Create, FileAccess.Write, 
                                    FileShare.None, BufferSize, FileOptions.SequentialScan);
            _file = new TdmsFile();
            _writer = new StreamingTdmsWriter(_stream, _file);
            _channelCache = new Dictionary<string, TdmsChannel>();
        }

        /// <summary>
        /// Appends an array of data to a channel. If the channel or group does not exist, it will be created.
        /// </summary>
        /// <typeparam name="T">The data type of the channel.</typeparam>
        /// <param name="groupName">The name of the channel group.</param>
        /// <param name="channelName">The name of the channel.</param>
        /// <param name="data">The data to append.</param>
        public void AppendData<T>(string groupName, string channelName, T[] data)
        {
            if (data == null || data.Length == 0)
                return;

            var group = _file.GetOrAddChannelGroup(groupName);
            var channelPath = $"{group.Path}/'{channelName.Replace("'", "''")}'";
            
            TdmsChannel? channel;
            
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

        /// <summary>
        /// Appends a single value to a channel.
        /// </summary>
        /// <typeparam name="T">The data type of the channel.</typeparam>
        /// <param name="groupName">The name of the channel group.</param>
        /// <param name="channelName">The name of the channel.</param>
        /// <param name="value">The value to append.</param>
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
                
                TdmsChannel? channel;
                if (!_channelCache.TryGetValue(channelPath, out channel))
                {
                    channel = group.Channels.FirstOrDefault(c => c.Path == channelPath);
                    
                    if (channel == null)
                    {
                        var elementType = data.GetType().GetElementType();
                        if (elementType == null)
                            throw new InvalidOperationException("Could not determine element type of data array.");
                        var addChannelMethod = typeof(TdmsChannelGroup)
                            .GetMethod(nameof(TdmsChannelGroup.AddChannel));
                        if (addChannelMethod == null)
                            throw new InvalidOperationException("Could not find AddChannel method.");
                        var genericAddChannelMethod = addChannelMethod.MakeGenericMethod(elementType);
                        channel = (TdmsChannel)genericAddChannelMethod.Invoke(group, new object[] { channelName })!;
                    }
                    
                    _channelCache[channelPath] = channel;
                }
                
                channels[i] = channel;
                dataArrays[i] = data;
            }

            _writer.WriteSegment(channels, dataArrays);
        }

        /// <summary>
        /// Creates a new channel without writing data.
        /// </summary>
        /// <typeparam name="T">The data type of the channel.</typeparam>
        /// <param name="groupName">The name of the channel group.</param>
        /// <param name="channelName">The name of the channel.</param>
        public void CreateChannel<T>(string groupName, string channelName)
        {
            var group = _file.GetOrAddChannelGroup(groupName);
            var channelPath = $"{group.Path}/'{channelName.Replace("'", "''")}'";
            if (!_channelCache.ContainsKey(channelPath))
            {
                var channel = group.AddChannel<T>(channelName);
                _channelCache[channelPath] = channel;
                _writer.MetadataDirty = true;
            }
        }

        /// <summary>
        /// Adds a property to the file.
        /// </summary>
        /// <typeparam name="T">The type of the property value.</typeparam>
        /// <param name="name">The name of the property.</param>
        /// <param name="value">The value of the property.</param>
        public void AddFileProperty<T>(string name, T value)
        {
            _file.AddProperty(name, value);
            _writer.MetadataDirty = true;
        }

        /// <summary>
        /// Adds a property to a channel group.
        /// </summary>
        /// <typeparam name="T">The type of the property value.</typeparam>
        /// <param name="groupName">The name of the channel group.</param>
        /// <param name="propertyName">The name of the property.</param>
        /// <param name="value">The value of the property.</param>
        public void AddGroupProperty<T>(string groupName, string propertyName, T value)
        {
            var group = _file.GetOrAddChannelGroup(groupName);
            group.AddProperty(propertyName, value);
            _writer.MetadataDirty = true;
        }

        /// <summary>
        /// Adds a property to a channel.
        /// </summary>
        /// <typeparam name="T">The type of the property value.</typeparam>
        /// <param name="groupName">The name of the channel group.</param>
        /// <param name="channelName">The name of the channel.</param>
        /// <param name="propertyName">The name of the property.</param>
        /// <param name="value">The value of the property.</param>
        /// <exception cref="InvalidOperationException">Thrown when the specified channel does not exist.</exception>
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
            _writer.MetadataDirty = true;
        }

        /// <summary>
        /// Flushes any buffered data to the underlying file.
        /// </summary>
        public void Flush()
        {
            _stream.Flush(flushToDisk: true);
        }

        /// <summary>
        /// Releases the resources used by the <see cref="TdmsFileStream"/> object.
        /// </summary>
        public void Dispose()
        {
            _writer?.Dispose();
            _stream?.Dispose();
            _channelCache?.Clear();
        }
    }
}