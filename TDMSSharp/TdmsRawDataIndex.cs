namespace TDMSSharp
{
    /// <summary>
    /// Represents an index for a chunk of raw data in a TDMS file.
    /// </summary>
    public class TdmsRawDataIndex
    {
        /// <summary>
        /// Gets the data type of the raw data.
        /// </summary>
        public TdsDataType DataType { get; }

        /// <summary>
        /// Gets the number of values in the raw data.
        /// </summary>
        public ulong NumberOfValues { get; }

        /// <summary>
        /// Gets the total size in bytes of the raw data. This is only used for string data.
        /// </summary>
        public ulong TotalSizeInBytes { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TdmsRawDataIndex"/> class.
        /// </summary>
        /// <param name="dataType">The data type of the raw data.</param>
        /// <param name="numberOfValues">The number of values in the raw data.</param>
        /// <param name="totalSizeInBytes">The total size in bytes of the raw data (only for string data).</param>
        public TdmsRawDataIndex(TdsDataType dataType, ulong numberOfValues, ulong totalSizeInBytes = 0)
        {
            DataType = dataType;
            NumberOfValues = numberOfValues;
            TotalSizeInBytes = totalSizeInBytes;
        }
    }
}
