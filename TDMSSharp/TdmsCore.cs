using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TdmsSharp
{
    /// <summary>
    /// TDMS data types enumeration
    /// </summary>
    public enum TdmsDataType : uint
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
        String = 0x20,
        Boolean = 0x21,
        TimeStamp = 0x44,
        ComplexSingleFloat = 0x08000c,
        ComplexDoubleFloat = 0x10000d,
        DAQmxRawData = 0xFFFFFFFF
    }

    /// <summary>
    /// Table of Contents flags for TDMS segments
    /// </summary>
    [Flags]
    public enum TocFlags : uint
    {
        None = 0,
        MetaData = 1 << 1,
        NewObjList = 1 << 2,
        RawData = 1 << 3,
        InterleavedData = 1 << 5,
        BigEndian = 1 << 6,
        DAQmxRawData = 1 << 7
    }

    /// <summary>
    /// TDMS timestamp structure
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TdmsTimestamp
    {
        public long Seconds;        // Seconds since 01/01/1904 00:00:00.00 UTC
        public ulong Fractions;     // Positive fractions of a second (2^-64)

        public TdmsTimestamp(DateTime dateTime)
        {
            var epoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var timeSpan = dateTime.ToUniversalTime() - epoch;
            Seconds = (long)timeSpan.TotalSeconds;
            Fractions = (ulong)((timeSpan.TotalSeconds - Seconds) * Math.Pow(2, 64));
        }

        public static TdmsTimestamp Now => new TdmsTimestamp(DateTime.UtcNow);
    }

    /// <summary>
    /// Represents a TDMS property value
    /// </summary>
    public class TdmsProperty
    {
        public string Name { get; }
        public TdmsDataType DataType { get; }
        public object Value { get; }

        public TdmsProperty(string name, object value)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Value = value ?? throw new ArgumentNullException(nameof(value));
            DataType = GetDataType(value);
        }

        private static TdmsDataType GetDataType(object value)
        {
            return value switch
            {
                sbyte => TdmsDataType.I8,
                short => TdmsDataType.I16,
                int => TdmsDataType.I32,
                long => TdmsDataType.I64,
                byte => TdmsDataType.U8,
                ushort => TdmsDataType.U16,
                uint => TdmsDataType.U32,
                ulong => TdmsDataType.U64,
                float => TdmsDataType.SingleFloat,
                double => TdmsDataType.DoubleFloat,
                string => TdmsDataType.String,
                bool => TdmsDataType.Boolean,
                TdmsTimestamp => TdmsDataType.TimeStamp,
                _ => throw new NotSupportedException($"Data type {value.GetType()} is not supported")
            };
        }
    }

    /// <summary>
    /// Base class for TDMS objects
    /// </summary>
    public abstract class TdmsObject
    {
        protected readonly Dictionary<string, TdmsProperty> _properties = new();
        protected bool _propertiesModified = false;

        public string Path { get; protected set; } = null!;
        public IReadOnlyDictionary<string, TdmsProperty> Properties => _properties;
        public bool HasPropertiesModified => _propertiesModified;

        public void SetProperty(string name, object value)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Property name cannot be null or empty", nameof(name));

            var property = new TdmsProperty(name, value);
            
            if (!_properties.TryGetValue(name, out var existing) || 
                !Equals(existing.Value, value))
            {
                _properties[name] = property;
                _propertiesModified = true;
            }
        }

        public void RemoveProperty(string name)
        {
            if (_properties.Remove(name))
                _propertiesModified = true;
        }

        public virtual void ResetModifiedFlags()
        {
            _propertiesModified = false;
        }
    }

    /// <summary>
    /// Represents a TDMS file object
    /// </summary>
    public class TdmsFile : TdmsObject
    {
        public TdmsFile()
        {
            Path = "/";
        }
    }

    /// <summary>
    /// Represents a TDMS group object
    /// </summary>
    public class TdmsGroup : TdmsObject
    {
        public string Name { get; }

        public TdmsGroup(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Group name cannot be null or empty", nameof(name));
            
            Name = name;
            Path = $"/'{name.Replace("'", "''")}'";
        }
    }

    /// <summary>
    /// Raw data index information
    /// </summary>
    internal class RawDataIndex
    {
        public TdmsDataType DataType { get; set; }
        public uint ArrayDimension { get; set; } = 1;
        public ulong NumberOfValues { get; set; }
        public ulong TotalSizeInBytes { get; set; }
        public bool HasChanged { get; set; } = true;
        public bool MatchesPrevious { get; set; } = false;

        public int GetElementSize()
        {
            return DataType switch
            {
                TdmsDataType.I8 or TdmsDataType.U8 or TdmsDataType.Boolean => 1,
                TdmsDataType.I16 or TdmsDataType.U16 => 2,
                TdmsDataType.I32 or TdmsDataType.U32 or TdmsDataType.SingleFloat => 4,
                TdmsDataType.I64 or TdmsDataType.U64 or TdmsDataType.DoubleFloat => 8,
                TdmsDataType.TimeStamp => 16,
                TdmsDataType.String => -1, // Variable length
                _ => throw new NotSupportedException($"Data type {DataType} is not supported")
            };
        }
    }
}