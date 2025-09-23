namespace TDMSSharp
{
    /// <summary>
    /// Specifies the data types used in a TDMS file.
    /// </summary>
    public enum TdsDataType : uint
    {
        /// <summary>
        /// No data type.
        /// </summary>
        Void = 0,
        /// <summary>
        /// 8-bit signed integer.
        /// </summary>
        I8 = 1,
        /// <summary>
        /// 16-bit signed integer.
        /// </summary>
        I16 = 2,
        /// <summary>
        /// 32-bit signed integer.
        /// </summary>
        I32 = 3,
        /// <summary>
        /// 64-bit signed integer.
        /// </summary>
        I64 = 4,
        /// <summary>
        /// 8-bit unsigned integer.
        /// </summary>
        U8 = 5,
        /// <summary>
        /// 16-bit unsigned integer.
        /// </summary>
        U16 = 6,
        /// <summary>
        /// 32-bit unsigned integer.
        /// </summary>
        U32 = 7,
        /// <summary>
        /// 64-bit unsigned integer.
        /// </summary>
        U64 = 8,
        /// <summary>
        /// Single-precision floating-point number.
        /// </summary>
        SingleFloat = 9,
        /// <summary>
        /// Double-precision floating-point number.
        /// </summary>
        DoubleFloat = 10,
        /// <summary>
        /// Extended-precision floating-point number.
        /// </summary>
        ExtendedFloat = 11,
        /// <summary>
        /// Single-precision floating-point number with unit.
        /// </summary>
        SingleFloatWithUnit = 0x19,
        /// <summary>
        /// Double-precision floating-point number with unit.
        /// </summary>
        DoubleFloatWithUnit = 0x1A,
        /// <summary>
        /// Extended-precision floating-point number with unit.
        /// </summary>
        ExtendedFloatWithUnit = 0x1B,
        /// <summary>
        /// String.
        /// </summary>
        String = 0x20,
        /// <summary>
        /// Boolean.
        /// </summary>
        Boolean = 0x21,
        /// <summary>
        /// Timestamp.
        /// </summary>
        TimeStamp = 0x44,
        /// <summary>
        /// Fixed-point number.
        /// </summary>
        FixedPoint = 0x4F,
        /// <summary>
        /// Complex single-precision floating-point number.
        /// </summary>
        ComplexSingleFloat = 0x08000c,
        /// <summary>
        /// Complex double-precision floating-point number.
        /// </summary>
        ComplexDoubleFloat = 0x10000d,
        /// <summary>
        /// DAQmx raw data.
        /// </summary>
        DAQmxRawData = 0xFFFFFFFF
    }
}
