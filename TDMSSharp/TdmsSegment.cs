using System.Collections.Generic;

namespace TDMSSharp
{
    public class TdmsSegment
    {
        public long NextSegmentOffset { get; set; }
        public long RawDataOffset { get; set; }
        public IList<TdmsChannelGroup> ChannelGroups { get; } = new List<TdmsChannelGroup>();
    }
}
