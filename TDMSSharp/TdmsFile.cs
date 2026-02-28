// TdmsFile.cs
using System.Collections.Generic;

namespace TdmsSharp
{
    /// <summary>
    /// Represents the entire structure of a read TDMS file.
    /// </summary>
    public class TdmsFileHolder
    {
        /// <summary>
        /// Gets all parsed segments in file order.
        /// </summary>
        public List<TdmsSegment> Segments { get; } = new List<TdmsSegment>();
        /// <summary>
        /// Gets top-level groups discovered in the file.
        /// </summary>
        public List<TdmsGroupHolder> Groups { get; } = new List<TdmsGroupHolder>();

        /// <summary>
        /// Finds a group by name.
        /// </summary>
        /// <param name="name">Group name.</param>
        /// <returns>The group, or <c>null</c> when not found.</returns>
        public TdmsGroupHolder? GetGroup(string name) => Groups.Find(g => g.Name == name);
    }
}
