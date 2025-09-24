using System;
using System.Collections.Generic;
using System.Linq;

namespace TDMSSharp
{
    /// <summary>
    /// Represents a group of channels in a TDMS file.
    /// </summary>
    public class TdmsChannelGroup
    {
        /// <summary>
        /// Gets the path of the channel group.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the name of the channel group.
        /// </summary>
        public string Name => Path.TrimStart('/').Trim('\'');

        /// <summary>
        /// Gets the list of properties for this channel group.
        /// </summary>
        public IList<TdmsProperty> Properties { get; } = new List<TdmsProperty>();

        /// <summary>
        /// Gets the list of channels in this group.
        /// </summary>
        public IList<TdmsChannel> Channels { get; } = new List<TdmsChannel>();

        /// <summary>
        /// Initializes a new instance of the <see cref="TdmsChannelGroup"/> class.
        /// </summary>
        /// <param name="path">The path of the channel group.</param>
        public TdmsChannelGroup(string path)
        {
            Path = path;
        }

        /// <summary>
        /// Adds a new channel to the group with the specified name and data type.
        /// </summary>
        /// <typeparam name="T">The data type of the channel.</typeparam>
        /// <param name="name">The name of the channel.</param>
        /// <returns>The newly created <see cref="TdmsChannel{T}"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when a channel with the same name already exists in this group.</exception>
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

        /// <summary>
        /// Adds a property to the channel group.
        /// </summary>
        /// <typeparam name="T">The type of the property value.</typeparam>
        /// <param name="name">The name of the property.</param>
        /// <param name="value">The value of the property.</param>
        public void AddProperty<T>(string name, T value)
        {
            if (value == null) return;
            var dataType = TdsDataTypeProvider.GetDataType<T>();
            Properties.Add(new TdmsProperty(name, dataType, value));
        }

        /// <summary>
        /// Creates a deep clone of the channel group, including its properties and channels (but not the channel data).
        /// </summary>
        /// <returns>A new <see cref="TdmsChannelGroup"/> instance with the same properties and channels.</returns>
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
