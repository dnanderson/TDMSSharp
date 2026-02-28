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
        /// <summary>No value/type.</summary>
        Void = 0,
        /// <summary>Signed 8-bit integer.</summary>
        I8 = 1,
        /// <summary>Signed 16-bit integer.</summary>
        I16 = 2,
        /// <summary>Signed 32-bit integer.</summary>
        I32 = 3,
        /// <summary>Signed 64-bit integer.</summary>
        I64 = 4,
        /// <summary>Unsigned 8-bit integer.</summary>
        U8 = 5,
        /// <summary>Unsigned 16-bit integer.</summary>
        U16 = 6,
        /// <summary>Unsigned 32-bit integer.</summary>
        U32 = 7,
        /// <summary>Unsigned 64-bit integer.</summary>
        U64 = 8,
        /// <summary>Single-precision floating point.</summary>
        SingleFloat = 9,
        /// <summary>Double-precision floating point.</summary>
        DoubleFloat = 10,
        /// <summary>UTF-8 string value.</summary>
        String = 0x20,
        /// <summary>Boolean value (0 or 1 byte representation).</summary>
        Boolean = 0x21,
        /// <summary>LabVIEW/TDMS timestamp (fractions + seconds).</summary>
        TimeStamp = 0x44,
        /// <summary>Complex single-precision value.</summary>
        ComplexSingleFloat = 0x08000c,
        /// <summary>Complex double-precision value.</summary>
        ComplexDoubleFloat = 0x10000d,
        /// <summary>DAQmx raw data sentinel type.</summary>
        DAQmxRawData = 0xFFFFFFFF
    }

    /// <summary>
    /// Table of Contents flags for TDMS segments
    /// </summary>
    [Flags]
    public enum TocFlags : uint
    {
        /// <summary>No flags set.</summary>
        None = 0,
        /// <summary>Segment contains metadata.</summary>
        MetaData = 1 << 1,
        /// <summary>Segment contains a new object list.</summary>
        NewObjList = 1 << 2,
        /// <summary>Segment contains raw data.</summary>
        RawData = 1 << 3,
        /// <summary>Raw data is interleaved across channels.</summary>
        InterleavedData = 1 << 5,
        /// <summary>Numeric values in the segment are big-endian.</summary>
        BigEndian = 1 << 6,
        /// <summary>Segment includes DAQmx raw data layout.</summary>
        DAQmxRawData = 1 << 7
    }

    /// <summary>
    /// TDMS timestamp structure
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TdmsTimestamp
    {
        /// <summary>
        /// Whole seconds since 1904-01-01T00:00:00Z.
        /// </summary>
        public long Seconds;        // Seconds since 01/01/1904 00:00:00.00 UTC
        /// <summary>
        /// Fractional part of the second represented in units of 2^-64.
        /// </summary>
        public ulong Fractions;     // Positive fractions of a second (2^-64)

        /// <summary>
        /// Creates a TDMS timestamp from a <see cref="DateTime"/> value (converted to UTC).
        /// </summary>
        /// <param name="dateTime">Source date/time.</param>
        public TdmsTimestamp(DateTime dateTime)
        {
            var epoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var timeSpan = dateTime.ToUniversalTime() - epoch;
            Seconds = (long)timeSpan.TotalSeconds;
            Fractions = (ulong)((timeSpan.TotalSeconds - Seconds) * Math.Pow(2, 64));
        }

        /// <summary>
        /// Gets the current UTC timestamp encoded in TDMS format.
        /// </summary>
        public static TdmsTimestamp Now => new TdmsTimestamp(DateTime.UtcNow);
    }

    /// <summary>
    /// Base class for a TDMS property, enabling type-agnostic storage
    /// </summary>
    public abstract class TdmsProperty
    {
        /// <summary>
        /// Gets the property name.
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// Gets the TDMS data type of this property.
        /// </summary>
        public TdmsDataType DataType { get; }

        protected TdmsProperty(string name, TdmsDataType dataType)
        {
            Name = name;
            DataType = dataType;
        }

        /// <summary>
        /// Writes the property value payload to a binary stream.
        /// </summary>
        /// <param name="writer">Destination writer.</param>
        public abstract void WriteValue(BinaryWriter writer);
        /// <summary>
        /// Returns the boxed value for this property.
        /// </summary>
        /// <returns>The property value.</returns>
        public abstract object GetValue();
    }

    /// <summary>
    /// Represents a strongly-typed TDMS property to avoid boxing
    /// </summary>
    public class TdmsProperty<T> : TdmsProperty where T : notnull
    {
        /// <summary>
        /// Gets the strongly-typed property value.
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// Initializes a typed TDMS property.
        /// </summary>
        /// <param name="name">Property name.</param>
        /// <param name="value">Property value.</param>
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

        /// <summary>
        /// Gets the TDMS object path.
        /// </summary>
        public string Path { get; protected set; } = null!;
        /// <summary>
        /// Gets all currently assigned properties.
        /// </summary>
        public IReadOnlyDictionary<string, TdmsProperty> Properties => _properties;
        /// <summary>
        /// Gets whether properties changed since the last segment write.
        /// </summary>
        public bool HasPropertiesModified => _propertiesModified;

        /// <summary>
        /// Sets or updates a TDMS property.
        /// </summary>
        /// <typeparam name="T">Property value type.</typeparam>
        /// <param name="name">Property name.</param>
        /// <param name="value">Property value.</param>
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

        /// <summary>
        /// Removes a property by name.
        /// </summary>
        /// <param name="name">Property name.</param>
        public void RemoveProperty(string name)
        {
            if (_properties.Remove(name))
            {
                _propertiesModified = true;
            }
        }

        /// <summary>
        /// Clears change-tracking flags after a segment write.
        /// </summary>
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
        /// <summary>
        /// Initializes the TDMS file object at root path <c>/</c>.
        /// </summary>
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
        /// <summary>
        /// Gets the unescaped group name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Initializes a TDMS group object.
        /// </summary>
        /// <param name="name">Group name.</param>
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

        /// <summary>
        /// Gets the destination channel for this batch.
        /// </summary>
        public override TdmsChannel Channel => _channel;

        /// <summary>
        /// Initializes channel batch data for one segment write.
        /// </summary>
        /// <param name="channel">Destination channel.</param>
        /// <param name="data">Values to write.</param>
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

        /// <summary>
        /// Gets the destination string channel for this batch.
        /// </summary>
        public override TdmsChannel Channel => _channel;

        /// <summary>
        /// Initializes string channel batch data for one segment write.
        /// </summary>
        /// <param name="channel">Destination string channel.</param>
        /// <param name="data">String values to write.</param>
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
