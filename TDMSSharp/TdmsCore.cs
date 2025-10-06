using System;
using System.Collections.Generic;
using System.IO;
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
    /// Base class for a TDMS property, enabling type-agnostic storage
    /// </summary>
    public abstract class TdmsProperty
    {
        public string Name { get; }
        public TdmsDataType DataType { get; }

        protected TdmsProperty(string name, TdmsDataType dataType)
        {
            Name = name;
            DataType = dataType;
        }

        public abstract void WriteValue(BinaryWriter writer);
        public abstract object GetValue();
    }

    /// <summary>
    /// Represents a strongly-typed TDMS property to avoid boxing
    /// </summary>
    public class TdmsProperty<T> : TdmsProperty where T : notnull
    {
        public T Value { get; }

        public TdmsProperty(string name, T value)
            : base(name, TdmsDataTypeHelper.GetDataType(typeof(T)))
        {
            Value = value;
        }

        public override object GetValue() => Value;

        public override void WriteValue(BinaryWriter writer)
        {
            switch (Value)
            {
                case sbyte v: writer.Write(v); break;
                case short v: writer.Write(v); break;
                case int v: writer.Write(v); break;
                case long v: writer.Write(v); break;
                case byte v: writer.Write(v); break;
                case ushort v: writer.Write(v); break;
                case uint v: writer.Write(v); break;
                case ulong v: writer.Write(v); break;
                case float v: writer.Write(v); break;
                case double v: writer.Write(v); break;
                case bool v: writer.Write((byte)(v ? 1 : 0)); break;
                case DateTime v:
                    var timestamp = new TdmsTimestamp(v);
                    writer.Write(timestamp.Fractions);
                    writer.Write(timestamp.Seconds);
                    break;
                case TdmsTimestamp v:
                    writer.Write(v.Fractions);
                    writer.Write(v.Seconds);
                    break;
                case string v:
                    var bytes = System.Text.Encoding.UTF8.GetBytes(v);
                    writer.Write((uint)bytes.Length);
                    writer.Write(bytes);
                    break;
                default:
                    throw new NotSupportedException($"Writing property of type {typeof(T)} is not supported.");
            }
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

        public void SetProperty<T>(string name, T value) where T : notnull
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Property name cannot be null or empty", nameof(name));
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            if (_properties.TryGetValue(name, out var existing) && existing is TdmsProperty<T> existingTyped)
            {
                if (EqualityComparer<T>.Default.Equals(existingTyped.Value, value))
                {
                    return; // Value is unchanged
                }
            }

            _properties[name] = new TdmsProperty<T>(name, value);
            _propertiesModified = true;
        }

        public void RemoveProperty(string name)
        {
            if (_properties.Remove(name))
            {
                _propertiesModified = true;
            }
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

        public int GetElementSize() => TdmsDataTypeHelper.GetSize(DataType);
    }

    /// <summary>
    /// Abstract base class to hold data for a channel before writing a segment.
    /// </summary>
    public abstract class ChannelData
    {
        /// <summary>
        /// The channel that this data belongs to.
        /// </summary>
        public abstract TdmsChannel Channel { get; }

        /// <summary>
        /// Writes the contained data to the channel's internal buffer.
        /// </summary>
        internal abstract void WriteToChannelBuffer();
    }

    /// <summary>
    /// Holds data for a channel of an unmanaged type (e.g., int, double, float).
    /// </summary>
    /// <typeparam name="T">The unmanaged data type.</typeparam>
    public class ChannelData<T> : ChannelData where T : unmanaged
    {
        private readonly TdmsChannel _channel;
        private readonly ReadOnlyMemory<T> _data;

        public override TdmsChannel Channel => _channel;

        public ChannelData(TdmsChannel channel, ReadOnlyMemory<T> data)
        {
            if (channel.DataType != TdmsDataTypeHelper.GetDataType(typeof(T)))
                throw new ArgumentException($"Channel data type '{channel.DataType}' does not match provided data type '{typeof(T)}'.");
            _channel = channel;
            _data = data;
        }

        internal override void WriteToChannelBuffer() => _channel.WriteValues(_data.Span);
    }

    /// <summary>
    /// Holds data for a string channel.
    /// </summary>
    public class StringChannelData : ChannelData
    {
        private readonly TdmsChannel _channel;
        private readonly string[] _data;

        public override TdmsChannel Channel => _channel;

        public StringChannelData(TdmsChannel channel, string[] data)
        {
            if (channel.DataType != TdmsDataType.String)
                throw new ArgumentException("Channel data type must be String for StringChannelData.");
            _channel = channel;
            _data = data;
        }

        internal override void WriteToChannelBuffer() => _channel.WriteStrings(_data);
    }
}