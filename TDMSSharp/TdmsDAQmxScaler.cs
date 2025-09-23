using System;

namespace TDMSSharp
{
    /// <summary>
    /// Represents a DAQmx Format Changing Scaler or Digital Line Scaler
    /// </summary>
    public class TdmsDAQmxScaler
    {
        /// <summary>
        /// Gets or sets the data type of the scaled data.
        /// </summary>
        public TdsDataType DataType { get; set; }

        /// <summary>
        /// Gets or sets the index of the raw buffer this scaler applies to.
        /// </summary>
        public uint RawBufferIndex { get; set; }

        /// <summary>
        /// Gets or sets the offset in bytes within the stride where this scaler's data begins.
        /// </summary>
        public uint RawByteOffsetWithinStride { get; set; }

        /// <summary>
        /// Gets or sets a bitmap that describes the sample format.
        /// </summary>
        public uint SampleFormatBitmap { get; set; }

        /// <summary>
        /// Gets or sets the ID of the scale to use.
        /// </summary>
        public uint ScaleId { get; set; }

        /// <summary>
        /// Gets the size in bytes of the data type.
        /// </summary>
        public int ByteSize => GetByteSize(DataType);
        
        private static int GetByteSize(TdsDataType dataType)
        {
            return dataType switch
            {
                TdsDataType.I8 or TdsDataType.U8 or TdsDataType.Boolean => 1,
                TdsDataType.I16 or TdsDataType.U16 => 2,
                TdsDataType.I32 or TdsDataType.U32 or TdsDataType.SingleFloat => 4,
                TdsDataType.I64 or TdsDataType.U64 or TdsDataType.DoubleFloat or TdsDataType.TimeStamp => 8,
                _ => throw new NotSupportedException($"Unsupported DAQmx data type: {dataType}")
            };
        }
    }

    /// <summary>
    /// Represents the raw data index for DAQmx data
    /// </summary>
    public class TdmsDAQmxRawDataIndex : TdmsRawDataIndex
    {
        /// <summary>
        /// Gets or sets a value indicating whether this is a digital line scaler.
        /// </summary>
        public bool IsDigitalLineScaler { get; set; }

        /// <summary>
        /// Gets or sets the array of scalers for this raw data index.
        /// </summary>
        public TdmsDAQmxScaler[] Scalers { get; set; }

        /// <summary>
        /// Gets or sets the array of raw data widths for this raw data index.
        /// </summary>
        public uint[] RawDataWidths { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TdmsDAQmxRawDataIndex"/> class.
        /// </summary>
        /// <param name="numberOfValues">The number of values in the raw data.</param>
        /// <param name="scalers">The array of scalers.</param>
        /// <param name="rawDataWidths">The array of raw data widths.</param>
        /// <param name="isDigitalLineScaler">A value indicating whether this is a digital line scaler.</param>
        public TdmsDAQmxRawDataIndex(ulong numberOfValues, TdmsDAQmxScaler[] scalers, uint[] rawDataWidths, bool isDigitalLineScaler = false)
            : base(TdsDataType.DAQmxRawData, numberOfValues)
        {
            Scalers = scalers ?? Array.Empty<TdmsDAQmxScaler>();
            RawDataWidths = rawDataWidths ?? Array.Empty<uint>();
            IsDigitalLineScaler = isDigitalLineScaler;
        }

        /// <summary>
        /// Calculate total stride size in bytes for interleaved DAQmx data
        /// </summary>
        public int GetStrideSize()
        {
            if (RawDataWidths == null || RawDataWidths.Length == 0)
                return 0;
            
            int totalStride = 0;
            foreach (var width in RawDataWidths)
            {
                totalStride += (int)width;
            }
            return totalStride;
        }

        /// <summary>
        /// Get the primary data type for this DAQmx channel (from first scaler)
        /// </summary>
        public TdsDataType GetPrimaryDataType()
        {
            if (Scalers == null || Scalers.Length == 0)
                throw new InvalidOperationException("No scalers defined for DAQmx channel");
            
            return Scalers[0].DataType;
        }
    }
}