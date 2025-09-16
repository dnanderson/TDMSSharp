using System;

namespace TDMSSharp
{
    public static class TdsDataTypeProvider
    {
        public static TdsDataType GetDataType<T>()
        {
            var type = typeof(T);
            if (type == typeof(sbyte)) return TdsDataType.I8;
            if (type == typeof(short)) return TdsDataType.I16;
            if (type == typeof(int)) return TdsDataType.I32;
            if (type == typeof(long)) return TdsDataType.I64;
            if (type == typeof(byte)) return TdsDataType.U8;
            if (type == typeof(ushort)) return TdsDataType.U16;
            if (type == typeof(uint)) return TdsDataType.U32;
            if (type == typeof(ulong)) return TdsDataType.U64;
            if (type == typeof(float)) return TdsDataType.SingleFloat;
            if (type == typeof(double)) return TdsDataType.DoubleFloat;
            if (type == typeof(string)) return TdsDataType.String;
            if (type == typeof(bool)) return TdsDataType.Boolean;
            if (type == typeof(DateTime)) return TdsDataType.TimeStamp;
            throw new NotSupportedException($"The type {type.Name} is not a supported TDMS data type.");
        }
    }
}
