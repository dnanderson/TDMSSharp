using System;

namespace TDMSSharp
{
    /// <summary>
    /// Represents a DAQmx Format Changing Scaler or Digital Line Scaler
    /// </summary>
    public class TdmsDAQmxScaler
    {
        public TdsDataType DataType { get; set; }
        public uint RawBufferIndex { get; set; }
        public uint RawByteOffsetWithinStride { get; set; }
        public uint SampleFormatBitmap { get; set; }
        public uint ScaleId { get; set; }

        // Derived properties for easier processing
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
        public bool IsDigitalLineScaler { get; set; }
        public TdmsDAQmxScaler[] Scalers { get; set; }
        public uint[] RawDataWidths { get; set; }

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