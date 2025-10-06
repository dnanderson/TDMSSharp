// TdmsSegment.cs
using System.Collections.Generic;

namespace TdmsSharp
{
    /// <summary>
    /// Represents a single segment within a TDMS file.
    /// </summary>
    public class TdmsSegment
    {
        public long AbsoluteOffset { get; }
        public TocFlags TableOfContents { get; }
        public int Version { get; }
        public long NextSegmentOffset { get; }
        public long RawDataOffset { get; }
        public List<TdmsChannelReader> ActiveChannels { get; } = new List<TdmsChannelReader>();
        public ulong ChunkCount { get; internal set; } = 1;

        public TdmsSegment(long absoluteOffset, TocFlags toc, int version, long nextSegmentOffset, long rawDataOffset)
        {
            AbsoluteOffset = absoluteOffset;
            TableOfContents = toc;
            Version = version;
            NextSegmentOffset = nextSegmentOffset;
            RawDataOffset = rawDataOffset;
        }

        public bool ContainsMetadata => (TableOfContents & TocFlags.MetaData) != 0;
        public bool ContainsRawData => (TableOfContents & TocFlags.RawData) != 0;
        public bool IsNewObjectList => (TableOfContents & TocFlags.NewObjList) != 0;
        public bool IsBigEndian => (TableOfContents & TocFlags.BigEndian) != 0;
        public bool IsInterleaved => (TableOfContents & TocFlags.InterleavedData) != 0;
    }
}