using System.Collections.Generic;

namespace TDMSSharp
{
    /// <summary>
    /// Represents a segment in a TDMS file, which contains a block of data.
    /// </summary>
    public class TdmsSegment
    {
        /// <summary>
        /// Gets or sets the offset of the next segment in the file.
        /// </summary>
        public long NextSegmentOffset { get; set; }

        /// <summary>
        /// Gets or sets the offset of the raw data within this segment.
        /// </summary>
        public long RawDataOffset { get; set; }

        /// <summary>
        /// Gets the list of channel groups in this segment.
        /// </summary>
        public IList<TdmsChannelGroup> ChannelGroups { get; } = new List<TdmsChannelGroup>();
    }
}
