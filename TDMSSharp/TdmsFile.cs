// TdmsFile.cs
using System.Collections.Generic;

namespace TdmsSharp
{
    /// <summary>
    /// Represents the entire structure of a read TDMS file.
    /// </summary>
    public class TdmsFileHolder
    {
        public List<TdmsSegment> Segments { get; } = new List<TdmsSegment>();
        public List<TdmsGroupHolder> Groups { get; } = new List<TdmsGroupHolder>();

        public TdmsGroupHolder? GetGroup(string name) => Groups.Find(g => g.Name == name);
    }
}