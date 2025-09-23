using System;

namespace TDMSSharp
{
    /// <summary>
    /// Provides methods for converting between .NET types and <see cref="TdsDataType"/> enum values.
    /// </summary>
    public static class TdsDataTypeProvider
    {
        /// <summary>
        /// Gets the <see cref="TdsDataType"/> that corresponds to the specified .NET type.
        /// </summary>
        /// <typeparam name="T">The .NET type to get the <see cref="TdsDataType"/> for.</typeparam>
        /// <returns>The corresponding <see cref="TdsDataType"/>.</returns>
        /// <exception cref="NotSupportedException">Thrown when the specified type is not a supported TDMS data type.</exception>
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

        /// <summary>
        /// Gets the .NET type that corresponds to the specified <see cref="TdsDataType"/>.
        /// </summary>
        /// <param name="dataType">The <see cref="TdsDataType"/> to get the .NET type for.</param>
        /// <returns>The corresponding .NET type.</returns>
        /// <exception cref="NotSupportedException">Thrown when the specified data type is not a supported TDMS data type.</exception>
        public static Type GetType(TdsDataType dataType)
        {
            switch (dataType)
            {
                case TdsDataType.I8: return typeof(sbyte);
                case TdsDataType.I16: return typeof(short);
                case TdsDataType.I32: return typeof(int);
                case TdsDataType.I64: return typeof(long);
                case TdsDataType.U8: return typeof(byte);
                case TdsDataType.U16: return typeof(ushort);
                case TdsDataType.U32: return typeof(uint);
                case TdsDataType.U64: return typeof(ulong);
                case TdsDataType.SingleFloat: return typeof(float);
                case TdsDataType.DoubleFloat: return typeof(double);
                case TdsDataType.String: return typeof(string);
                case TdsDataType.Boolean: return typeof(bool);
                case TdsDataType.TimeStamp: return typeof(DateTime);
                default:
                    throw new NotSupportedException($"The data type {dataType} is not a supported TDMS data type.");
            }
        }
    }
}
