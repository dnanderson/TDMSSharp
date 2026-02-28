// TdmsMetadata.cs
namespace TdmsSharp
{
    /// <summary>
    /// Holds the raw data index information for a channel within a specific segment.
    /// </summary>
    public class RawDataIndexInfo
    {
        /// <summary>
        /// Gets the segment where this index declaration appears.
        /// </summary>
        public TdmsSegment Segment { get; }
        /// <summary>
        /// Gets the TDMS data type for this channel declaration.
        /// </summary>
        public TdmsDataType DataType { get; }
        /// <summary>
        /// Gets value count declared per chunk.
        /// </summary>
        public ulong NumberOfValues { get; }
        /// <summary>
        /// Gets total byte size for variable-width payloads (for example strings).
        /// </summary>
        public ulong TotalSizeInBytes { get; } // Only for variable-length types like strings

        /// <summary>
        /// Initializes raw-data index information for a channel declaration.
        /// </summary>
        /// <param name="segment">Owning segment.</param>
        /// <param name="dataType">Channel data type.</param>
        /// <param name="numValues">Values per chunk.</param>
        /// <param name="totalSize">Variable-width payload byte size.</param>
        public RawDataIndexInfo(TdmsSegment segment, TdmsDataType dataType, ulong numValues, ulong totalSize)
        {
            Segment = segment;
            DataType = dataType;
            NumberOfValues = numValues;
            TotalSizeInBytes = totalSize;
        }
    }
}
