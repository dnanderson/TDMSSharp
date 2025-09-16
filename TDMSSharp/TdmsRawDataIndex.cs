namespace TDMSSharp
{
    public class TdmsRawDataIndex
    {
        public TdsDataType DataType { get; }
        public ulong NumberOfValues { get; }
        public ulong TotalSizeInBytes { get; } // Only for string data

        public TdmsRawDataIndex(TdsDataType dataType, ulong numberOfValues, ulong totalSizeInBytes = 0)
        {
            DataType = dataType;
            NumberOfValues = numberOfValues;
            TotalSizeInBytes = totalSizeInBytes;
        }
    }
}
