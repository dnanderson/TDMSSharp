using System;

namespace TdmsSharp
{
    /// <summary>
    /// Helper class for TDMS data type operations
    /// </summary>
    public static class TdmsDataTypeHelper
    {
        public static TdmsDataType GetDataType(Type type)
        {
            if (type == typeof(sbyte)) return TdmsDataType.I8;
            if (type == typeof(short)) return TdmsDataType.I16;
            if (type == typeof(int)) return TdmsDataType.I32;
            if (type == typeof(long)) return TdmsDataType.I64;
            if (type == typeof(byte)) return TdmsDataType.U8;
            if (type == typeof(ushort)) return TdmsDataType.U16;
            if (type == typeof(uint)) return TdmsDataType.U32;
            if (type == typeof(ulong)) return TdmsDataType.U64;
            if (type == typeof(float)) return TdmsDataType.SingleFloat;
            if (type == typeof(double)) return TdmsDataType.DoubleFloat;
            if (type == typeof(string)) return TdmsDataType.String;
            if (type == typeof(bool)) return TdmsDataType.Boolean;
            if (type == typeof(DateTime)) return TdmsDataType.TimeStamp;
            if (type == typeof(TdmsTimestamp)) return TdmsDataType.TimeStamp;

            throw new NotSupportedException($"Data type {type} is not supported");
        }

        public static int GetSize(TdmsDataType dataType)
        {
            return dataType switch
            {
                TdmsDataType.I8 or TdmsDataType.U8 or TdmsDataType.Boolean => 1,
                TdmsDataType.I16 or TdmsDataType.U16 => 2,
                TdmsDataType.I32 or TdmsDataType.U32 or TdmsDataType.SingleFloat => 4,
                TdmsDataType.I64 or TdmsDataType.U64 or TdmsDataType.DoubleFloat => 8,
                TdmsDataType.TimeStamp => 16,
                TdmsDataType.String => -1, // Variable size
                _ => throw new NotSupportedException($"Data type {dataType} is not supported for size calculation")
            };
        }
    }
}