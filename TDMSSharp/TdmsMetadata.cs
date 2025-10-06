// TdmsMetadata.cs
namespace TdmsSharp
{
    /// <summary>
    /// Holds the raw data index information for a channel within a specific segment.
    /// </summary>
    public class RawDataIndexInfo
    {
        public TdmsSegment Segment { get; }
        public TdmsDataType DataType { get; }
        public ulong NumberOfValues { get; }
        public ulong TotalSizeInBytes { get; } // Only for variable-length types like strings

        public RawDataIndexInfo(TdmsSegment segment, TdmsDataType dataType, ulong numValues, ulong totalSize)
        {
            Segment = segment;
            DataType = dataType;
            NumberOfValues = numValues;
            TotalSizeInBytes = totalSize;
        }
    }
}