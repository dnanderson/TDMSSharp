namespace TDMSSharp
{
    public enum TdsDataType : uint
    {
        Void = 0,
        I8 = 1,
        I16 = 2,
        I32 = 3,
        I64 = 4,
        U8 = 5,
        U16 = 6,
        U32 = 7,
        U64 = 8,
        SingleFloat = 9,
        DoubleFloat = 10,
        ExtendedFloat = 11,
        SingleFloatWithUnit = 0x19,
        DoubleFloatWithUnit = 0x1A,
        ExtendedFloatWithUnit = 0x1B,
        String = 0x20,
        Boolean = 0x21,
        TimeStamp = 0x44,
        FixedPoint = 0x4F,
        ComplexSingleFloat = 0x08000c,
        ComplexDoubleFloat = 0x10000d,
        DAQmxRawData = 0xFFFFFFFF
    }
}
