using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TDMSSharp
{
    /// <summary>
    /// Represents a TDMS file, containing properties and channel groups.
    /// </summary>
    public partial class TdmsFile
    {
        /// <summary>
        /// Gets the list of properties for this file.
        /// </summary>
        public IList<TdmsProperty> Properties { get; } = new List<TdmsProperty>();

        /// <summary>
        /// Gets the list of channel groups in this file.
        /// </summary>
        public IList<TdmsChannelGroup> ChannelGroups { get; } = new List<TdmsChannelGroup>();

        /// <summary>
        /// Initializes a new instance of the <see cref="TdmsFile"/> class.
        /// </summary>
        public TdmsFile()
        {
        }

        /// <summary>
        /// Gets the channel group with the specified name, or creates it if it doesn't exist.
        /// </summary>
        /// <param name="name">The name of the channel group.</param>
        /// <returns>The existing or newly created <see cref="TdmsChannelGroup"/>.</returns>
        public TdmsChannelGroup GetOrAddChannelGroup(string name)
        {
            var path = $"/'{name.Replace("'", "''")}'";
            var group = ChannelGroups.FirstOrDefault(g => g.Path == path);
            if (group == null)
            {
                group = new TdmsChannelGroup(path);
                ChannelGroups.Add(group);
            }
            return group;
        }

        /// <summary>
        /// Adds a property to the file.
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
        /// Saves the TDMS file to the specified path.
        /// </summary>
        /// <param name="path">The path to save the file to.</param>
        public void Save(string path)
        {
            using (var stream = File.Create(path))
            {
                Save(stream);
            }
        }

        /// <summary>
        /// Saves the TDMS file to the specified stream.
        /// </summary>
        /// <param name="stream">The stream to save the file to.</param>
        public void Save(Stream stream)
        {
            var writer = new TdmsWriter(stream);
            writer.WriteFile(this);
        }

        /// <summary>
        /// Opens a TDMS file from the specified path.
        /// </summary>
        /// <param name="path">The path to the TDMS file.</param>
        /// <returns>A <see cref="TdmsFile"/> object.</returns>
        public static TdmsFile Open(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                return Open(stream);
            }
        }

        /// <summary>
        /// Opens a TDMS file from the specified stream.
        /// </summary>
        /// <param name="stream">The stream to read the TDMS file from.</param>
        /// <returns>A <see cref="TdmsFile"/> object.</returns>
        public static TdmsFile Open(Stream stream)
        {
            var reader = new TdmsReader(stream);
            return reader.ReadFile();
        }

        /// <summary>
        /// Creates a deep clone of the TDMS file, including its properties and channel groups (but not the channel data).
        /// </summary>
        /// <returns>A new <see cref="TdmsFile"/> instance with the same properties and channel groups.</returns>
        public TdmsFile DeepClone()
        {
            var clone = new TdmsFile();
            foreach (var prop in Properties)
            {
                clone.Properties.Add(new TdmsProperty(prop.Name, prop.DataType, prop.Value));
            }
            foreach (var group in ChannelGroups)
            {
                clone.ChannelGroups.Add(group.DeepClone());
            }
            return clone;
        }

        /// <summary>
        /// Gets the channel with the specified path.
        /// </summary>
        /// <param name="channelPath">The path of the channel.</param>
        /// <returns>The <see cref="TdmsChannel"/> if found; otherwise, <c>null</c>.</returns>
        public TdmsChannel? GetChannel(string channelPath)
        {
            var pathParts = channelPath.Split('/');
            if (pathParts.Length != 3) return null; // e.g., "", "'group'", "'channel'"
            var groupName = pathParts[1].Trim('\'');
            var channelName = pathParts[2].Trim('\'');
            var group = ChannelGroups.FirstOrDefault(g => g.Name == groupName);
            return group?.Channels.FirstOrDefault(c => c.Name == channelName);
        }
    }
}
