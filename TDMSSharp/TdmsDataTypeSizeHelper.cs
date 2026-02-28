// TdmsDataTypeSizeHelper.cs
namespace TdmsSharp
{
    /// <summary>
    /// Returns fixed element widths for TDMS data types.
    /// </summary>
    public static class TdmsDataTypeSizeHelper
    {
        /// <summary>
        /// Gets the fixed element width in bytes.
        /// </summary>
        /// <param name="dataType">TDMS data type.</param>
        /// <returns>Element width in bytes, or 0 for variable-length types.</returns>
        public static int GetSize(TdmsDataType dataType)
        {
            switch (dataType)
            {
                case TdmsDataType.I8:
                case TdmsDataType.U8:
                case TdmsDataType.Boolean:
                    return 1;
                case TdmsDataType.I16:
                case TdmsDataType.U16:
                    return 2;
                case TdmsDataType.I32:
                case TdmsDataType.U32:
                case TdmsDataType.SingleFloat:
                    return 4;
                case TdmsDataType.I64:
                case TdmsDataType.U64:
                case TdmsDataType.DoubleFloat:
                case TdmsDataType.ComplexSingleFloat:
                    return 8;
                case TdmsDataType.TimeStamp:
                case TdmsDataType.ComplexDoubleFloat:
                    return 16;
                case TdmsDataType.String:
                case TdmsDataType.Void:
                case TdmsDataType.DAQmxRawData:
                default:
                    return 0; // Indicates variable size
            }
        }
    }
}
