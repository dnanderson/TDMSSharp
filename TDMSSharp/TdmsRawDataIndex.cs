namespace TDMSSharp
{
    /// <summary>
    /// Represents an index for a chunk of raw data in a TDMS file.
    /// </summary>
    public struct TdmsRawDataIndex : IEquatable<TdmsRawDataIndex>
    {
            public TdsDataType DataType { get; set; }
            public ulong NumberOfValues { get; set; }
            public ulong TotalSize { get; set; }
            
            public bool Equals(TdmsRawDataIndex other)
            {
                return DataType == other.DataType && 
                       NumberOfValues == other.NumberOfValues &&
                       TotalSize == other.TotalSize;
            }
            
            public override bool Equals(object? obj) => obj is TdmsRawDataIndex other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(DataType, NumberOfValues, TotalSize);
    }
}
