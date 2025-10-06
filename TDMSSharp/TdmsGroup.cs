// TdmsGroup.cs
using System.Collections.Generic;

namespace TdmsSharp
{
    /// <summary>
    /// Holds the metadata for a group of channels read from a TDMS file.
    /// </summary>
    public class TdmsGroupHolder
    {
        public string Name { get; }
        public List<TdmsChannelReader> Channels { get; } = new List<TdmsChannelReader>();

        public TdmsGroupHolder(string name)
        {
            Name = name;
        }

        public TdmsChannelReader? GetChannel(string name) => Channels.Find(c => c.Name == name);
    }
}