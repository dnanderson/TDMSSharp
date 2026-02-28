// TdmsSegment.cs
using System.Collections.Generic;

namespace TdmsSharp
{
    /// <summary>
    /// Represents a single segment within a TDMS file.
    /// </summary>
    public class TdmsSegment
    {
        /// <summary>
        /// Gets the absolute byte offset of this segment in the file.
        /// </summary>
        public long AbsoluteOffset { get; }
        /// <summary>
        /// Gets segment ToC flags.
        /// </summary>
        public TocFlags TableOfContents { get; }
        /// <summary>
        /// Gets TDMS version declared in segment lead-in.
        /// </summary>
        public int Version { get; }
        /// <summary>
        /// Gets byte size from end of lead-in to next segment.
        /// </summary>
        public long NextSegmentOffset { get; }
        /// <summary>
        /// Gets byte offset from segment start to raw data.
        /// </summary>
        public long RawDataOffset { get; }
        /// <summary>
        /// Gets active channels used to interpret this segment raw payload.
        /// </summary>
        public List<TdmsChannelReader> ActiveChannels { get; } = new List<TdmsChannelReader>();
        /// <summary>
        /// Gets or sets inferred chunk count in this segment.
        /// </summary>
        public ulong ChunkCount { get; internal set; } = 1;

        /// <summary>
        /// Initializes a TDMS segment model.
        /// </summary>
        /// <param name="absoluteOffset">Segment start offset in file.</param>
        /// <param name="toc">ToC flags.</param>
        /// <param name="version">TDMS format version.</param>
        /// <param name="nextSegmentOffset">Offset from lead-in end to next segment.</param>
        /// <param name="rawDataOffset">Offset from segment start to raw data.</param>
        public TdmsSegment(long absoluteOffset, TocFlags toc, int version, long nextSegmentOffset, long rawDataOffset)
        {
            AbsoluteOffset = absoluteOffset;
            TableOfContents = toc;
            Version = version;
            NextSegmentOffset = nextSegmentOffset;
            RawDataOffset = rawDataOffset;
        }

        /// <summary>
        /// Gets whether the segment declares a metadata section.
        /// </summary>
        public bool ContainsMetadata => (TableOfContents & TocFlags.MetaData) != 0;
        /// <summary>
        /// Gets whether the segment declares raw data.
        /// </summary>
        public bool ContainsRawData => (TableOfContents & TocFlags.RawData) != 0;
        /// <summary>
        /// Gets whether this segment resets the object list.
        /// </summary>
        public bool IsNewObjectList => (TableOfContents & TocFlags.NewObjList) != 0;
        /// <summary>
        /// Gets whether numeric values in this segment are big-endian.
        /// </summary>
        public bool IsBigEndian => (TableOfContents & TocFlags.BigEndian) != 0;
        /// <summary>
        /// Gets whether raw channel values are interleaved.
        /// </summary>
        public bool IsInterleaved => (TableOfContents & TocFlags.InterleavedData) != 0;
    }
}
