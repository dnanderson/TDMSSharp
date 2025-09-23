using System;
using System.Collections.Generic;
using System.Linq;

namespace TDMSSharp
{
    public class TdmsChannelGroup
    {
        public string Path { get; }
        public IList<TdmsProperty> Properties { get; } = new List<TdmsProperty>();
        public IList<TdmsChannel> Channels { get; } = new List<TdmsChannel>();

        public TdmsChannelGroup(string path)
        {
            Path = path;
        }

        public TdmsChannel<T> AddChannel<T>(string name)
        {
            var channelName = name.Replace("'", "''");
            var channelPath = $"{Path}/'{channelName}'";
            if (Channels.Any(c => c.Path == channelPath))
            {
                throw new InvalidOperationException($"A channel with the name '{name}' already exists in this group.");
            }

            var channel = new TdmsChannel<T>(channelPath);
            Channels.Add(channel);
            return channel;
        }

        public void AddProperty<T>(string name, T value)
        {
            if (value == null) return;
            var dataType = TdsDataTypeProvider.GetDataType<T>();
            Properties.Add(new TdmsProperty(name, dataType, value));
        }

        public TdmsChannelGroup DeepClone()
        {
            var clone = new TdmsChannelGroup(Path);
            foreach (var prop in Properties)
            {
                clone.Properties.Add(new TdmsProperty(prop.Name, prop.DataType, prop.Value));
            }
            foreach (var channel in Channels)
            {
                clone.Channels.Add(channel.DeepClone());
            }
            return clone;
        }
    }
}
