using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TDMSSharp
{
    public class TdmsWriter
    {
        private readonly BinaryWriter _writer;
        private static readonly DateTime TdmsEpoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public TdmsWriter(Stream stream)
        {
            _writer = new BinaryWriter(stream, Encoding.UTF8, true);
        }

        public void WriteFile(TdmsFile file)
        {
            _writer.BaseStream.Seek(28, SeekOrigin.Begin);

            long metaDataStart = _writer.BaseStream.Position;
            WriteMetaData(file);
            long metaDataEnd = _writer.BaseStream.Position;
            long metaDataLength = metaDataEnd - metaDataStart;

            long rawDataStart = _writer.BaseStream.Position;
            foreach (var group in file.ChannelGroups)
            {
                foreach (var channel in group.Channels)
                {
                    if (channel.Data != null)
                    {
                        WriteRawData(_writer, channel.Data);
                    }
                }
            }
            long rawDataEnd = _writer.BaseStream.Position;
            long rawDataLength = rawDataEnd - rawDataStart;

            long nextSegmentOffset = metaDataLength + rawDataLength;

            _writer.BaseStream.Seek(0, SeekOrigin.Begin);

            uint tocMask = 0;
            tocMask |= (1 << 1); // kTocMetaData
            if (rawDataLength > 0) tocMask |= (1 << 3); // kTocRawData
            tocMask |= (1 << 2); // kTocNewObjList

            _writer.Write(Encoding.ASCII.GetBytes("TDSm"));
            _writer.Write(tocMask);
            _writer.Write((uint)4713); // Version
            _writer.Write((ulong)nextSegmentOffset);
            _writer.Write((ulong)metaDataLength); // Raw data offset

            _writer.BaseStream.Seek(0, SeekOrigin.End);
        }

        private void WriteMetaData(TdmsFile file)
        {
            uint objectCount = 1; // file
            objectCount += (uint)file.ChannelGroups.Count;
            foreach (var group in file.ChannelGroups)
            {
                objectCount += (uint)group.Channels.Count;
            }
            _writer.Write(objectCount);

            WriteObjectMetaData(_writer, "/", file.Properties);

            foreach (var group in file.ChannelGroups)
            {
                WriteObjectMetaData(_writer, group.Path, group.Properties);
                foreach (var channel in group.Channels)
                {
                    WriteObjectMetaData(_writer, channel.Path, channel.Properties, channel);
                }
            }
        }

        internal static void WriteObjectMetaData(BinaryWriter writer, string path, IList<TdmsProperty> properties, TdmsChannel? channel = null, ulong? valuesCount = null)
        {
            WriteString(writer, path);

            var numValues = valuesCount ?? channel?.NumberOfValues ?? 0;

            if (channel != null && numValues > 0)
            {
                using (var ms = new MemoryStream())
                using (var tempWriter = new BinaryWriter(ms))
                {
                    tempWriter.Write((uint)channel.DataType);
                    tempWriter.Write((uint)1); // Dimension
                    tempWriter.Write(numValues);
                    if (channel.DataType == TdsDataType.String && channel.Data != null)
                    {
                        var totalBytes = 0UL;
                        foreach (var s in (string[])channel.Data) totalBytes += (ulong)Encoding.UTF8.GetByteCount(s);
                        tempWriter.Write(totalBytes);
                    }
                    writer.Write((uint)ms.Length);
                    writer.Write(ms.ToArray());
                }
            }
            else
            {
                writer.Write((uint)0xFFFFFFFF);
            }

            writer.Write((uint)properties.Count);
            foreach (var prop in properties)
            {
                WriteString(writer, prop.Name);
                writer.Write((uint)prop.DataType);
                if (prop.Value != null) WriteValue(writer, prop.Value, prop.DataType);
            }
        }

        internal static void WriteRawData(BinaryWriter writer, object data)
        {
            var array = (Array)data;
            if (array.Length == 0) return;

            var elementType = array.GetType().GetElementType();
            if (elementType == null) return;
            if (elementType.IsPrimitive)
            {
                var elementSize = System.Runtime.InteropServices.Marshal.SizeOf(elementType);
                var byteBuffer = new byte[array.Length * elementSize];
                Buffer.BlockCopy(array, 0, byteBuffer, 0, byteBuffer.Length);
                writer.Write(byteBuffer);
            }
            else if (elementType == typeof(string))
            {
                var strings = (string[])data;
                var offsets = new uint[strings.Length];
                using (var ms = new MemoryStream())
                {
                    var currentOffset = (uint)(strings.Length * 4);
                    for (int i = 0; i < strings.Length; i++)
                    {
                        offsets[i] = currentOffset;
                        var bytes = Encoding.UTF8.GetBytes(strings[i]);
                        ms.Write(bytes, 0, bytes.Length);
                        currentOffset += (uint)bytes.Length;
                    }

                    foreach (var offset in offsets) writer.Write(offset);
                    writer.Write(ms.ToArray());
                }
            }
            else if (elementType == typeof(bool))
            {
                foreach(var b in (bool[])data) writer.Write((byte)(b ? 1 : 0));
            }
            else if (elementType == typeof(DateTime))
            {
                foreach (var dt in (DateTime[])data)
                {
                    var timestamp = (DateTime)dt;
                    var timespan = timestamp.ToUniversalTime() - TdmsEpoch;
                    long seconds = (long)timespan.TotalSeconds;
                    var fractions = (ulong)((timespan.Ticks % TimeSpan.TicksPerSecond) * (1.0 / TimeSpan.TicksPerSecond * Math.Pow(2, 64)));
                    writer.Write(seconds);
                    writer.Write(fractions);
                }
            }
            else
            {
                throw new NotSupportedException($"Writing raw data for {elementType} is not supported.");
            }
        }

        internal static void WriteString(BinaryWriter writer, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            writer.Write((uint)bytes.Length);
            writer.Write(bytes);
        }

        internal static void WriteValue(BinaryWriter writer, object value, TdsDataType dataType)
        {
            switch (dataType)
            {
                case TdsDataType.I8: writer.Write((sbyte)value); break;
                case TdsDataType.I16: writer.Write((short)value); break;
                case TdsDataType.I32: writer.Write((int)value); break;
                case TdsDataType.I64: writer.Write((long)value); break;
                case TdsDataType.U8: writer.Write((byte)value); break;
                case TdsDataType.U16: writer.Write((ushort)value); break;
                case TdsDataType.U32: writer.Write((uint)value); break;
                case TdsDataType.U64: writer.Write((ulong)value); break;
                case TdsDataType.SingleFloat: writer.Write((float)value); break;
                case TdsDataType.DoubleFloat: writer.Write((double)value); break;
                case TdsDataType.String: WriteString(writer, (string)value); break;
                case TdsDataType.Boolean: writer.Write((bool)value); break;
                case TdsDataType.TimeStamp:
                    var timestamp = (DateTime)value;
                    var timespan = timestamp.ToUniversalTime() - TdmsEpoch;
                    long seconds = (long)timespan.TotalSeconds;
                    var fractions = (ulong)((timespan.Ticks % TimeSpan.TicksPerSecond) * (1.0 / TimeSpan.TicksPerSecond * Math.Pow(2, 64)));
                    writer.Write(seconds);
                    writer.Write(fractions);
                    break;
                default:
                    throw new NotSupportedException($"Data type {dataType} is not supported for properties.");
            }
        }
    }
}
