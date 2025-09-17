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
            _writer.Seek(28, SeekOrigin.Begin);

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
                        WriteRawData(channel.Data);
                    }
                }
            }
            long rawDataEnd = _writer.BaseStream.Position;
            long rawDataLength = rawDataEnd - rawDataStart;

            long nextSegmentOffset = metaDataLength + rawDataLength;

            _writer.Seek(0, SeekOrigin.Begin);

            uint tocMask = 0;
            tocMask |= (1 << 1); // kTocMetaData
            if (rawDataLength > 0) tocMask |= (1 << 3); // kTocRawData
            tocMask |= (1 << 2); // kTocNewObjList

            _writer.Write(Encoding.ASCII.GetBytes("TDSm"));
            _writer.Write(tocMask);
            _writer.Write((uint)4713); // Version
            _writer.Write((ulong)nextSegmentOffset);
            _writer.Write((ulong)metaDataLength); // Raw data offset

            _writer.Seek(0, SeekOrigin.End);
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

            WriteObjectMetaData("/", file.Properties);

            foreach (var group in file.ChannelGroups)
            {
                WriteObjectMetaData(group.Path, group.Properties);
                foreach (var channel in group.Channels)
                {
                    WriteObjectMetaData(channel.Path, channel.Properties, channel);
                }
            }
        }

        private void WriteObjectMetaData(string path, IList<TdmsProperty> properties, TdmsChannel channel = null)
        {
            WriteString(path);

            if (channel != null && channel.NumberOfValues > 0)
            {
                using (var ms = new MemoryStream())
                using (var tempWriter = new BinaryWriter(ms))
                {
                    tempWriter.Write((uint)channel.DataType);
                    tempWriter.Write((uint)1); // Dimension
                    tempWriter.Write(channel.NumberOfValues);
                    if (channel.DataType == TdsDataType.String)
                    {
                        tempWriter.Write((ulong)0); // Placeholder
                    }
                    _writer.Write((uint)ms.Length);
                    _writer.Write(ms.ToArray());
                }
            }
            else
            {
                _writer.Write((uint)0xFFFFFFFF);
            }

            _writer.Write((uint)properties.Count);
            foreach (var prop in properties)
            {
                WriteString(prop.Name);
                _writer.Write((uint)prop.DataType);
                WriteValue(prop.Value, prop.DataType);
            }
        }

        private void WriteRawData(object data)
        {
            var array = (Array)data;
            if (array.Length == 0) return;

            var elementType = array.GetType().GetElementType();
            if (elementType.IsPrimitive)
            {
                var elementSize = System.Runtime.InteropServices.Marshal.SizeOf(elementType);
                var byteBuffer = new byte[array.Length * elementSize];
                Buffer.BlockCopy(array, 0, byteBuffer, 0, byteBuffer.Length);
                _writer.Write(byteBuffer);
            }
            else
            {
                throw new NotSupportedException("Writing raw data for non-primitive types is not supported.");
            }
        }

        private void WriteString(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            _writer.Write((uint)bytes.Length);
            _writer.Write(bytes);
        }

        private void WriteValue(object value, TdsDataType dataType)
        {
            switch (dataType)
            {
                case TdsDataType.I8: _writer.Write((sbyte)value); break;
                case TdsDataType.I16: _writer.Write((short)value); break;
                case TdsDataType.I32: _writer.Write((int)value); break;
                case TdsDataType.I64: _writer.Write((long)value); break;
                case TdsDataType.U8: _writer.Write((byte)value); break;
                case TdsDataType.U16: _writer.Write((ushort)value); break;
                case TdsDataType.U32: _writer.Write((uint)value); break;
                case TdsDataType.U64: _writer.Write((ulong)value); break;
                case TdsDataType.SingleFloat: _writer.Write((float)value); break;
                case TdsDataType.DoubleFloat: _writer.Write((double)value); break;
                case TdsDataType.String: WriteString((string)value); break;
                case TdsDataType.Boolean: _writer.Write((bool)value); break;
                case TdsDataType.TimeStamp:
                    var timestamp = (DateTime)value;
                    var timespan = timestamp - TdmsEpoch;
                    long seconds = (long)timespan.TotalSeconds;
                    var fractions = (ulong)((timespan.Ticks % TimeSpan.TicksPerSecond) * (1.0 / TimeSpan.TicksPerSecond * (1UL << 64)));
                    _writer.Write(fractions);
                    _writer.Write(seconds);
                    break;
                default:
                    throw new NotSupportedException($"Data type {dataType} is not supported for properties.");
            }
        }
    }
}
