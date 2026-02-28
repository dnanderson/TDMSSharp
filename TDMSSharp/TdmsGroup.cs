// TdmsGroup.cs
using System.Collections.Generic;

namespace TdmsSharp
{
    /// <summary>
    /// Holds the metadata for a group of channels read from a TDMS file.
    /// </summary>
    public class TdmsGroupHolder
    {
        /// <summary>
        /// Gets the group name.
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// Gets channels defined in this group.
        /// </summary>
        public List<TdmsChannelReader> Channels { get; } = new List<TdmsChannelReader>();

        /// <summary>
        /// Initializes a group holder.
        /// </summary>
        /// <param name="name">Group name.</param>
        public TdmsGroupHolder(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Finds a channel by name.
        /// </summary>
        /// <param name="name">Channel name.</param>
        /// <returns>The channel, or <c>null</c> when not found.</returns>
        public TdmsChannelReader? GetChannel(string name) => Channels.Find(c => c.Name == name);
    }
}
