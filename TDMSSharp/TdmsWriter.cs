using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TDMSSharp
{
    public class TdmsWriter
    {
        private readonly BinaryWriter _writer;
        private static readonly DateTime TdmsEpoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly byte[] _writeBuffer = new byte[81920]; // 80KB buffer for better I/O

        public TdmsWriter(Stream stream)
        {
            _writer = new BinaryWriter(stream, Encoding.UTF8, true);
        }

        public void WriteFile(TdmsFile file)
        {
            // Reserve space for lead-in
            _writer.BaseStream.Seek(28, SeekOrigin.Begin);

            long metaDataStart = _writer.BaseStream.Position;
            WriteMetaData(file);
            long metaDataLength = _writer.BaseStream.Position - metaDataStart;

            long rawDataStart = _writer.BaseStream.Position;
            foreach (var group in file.ChannelGroups)
            {
                foreach (var channel in group.Channels)
                {
                    if (channel.Data != null)
                    {
                        WriteRawDataOptimized(_writer, channel.Data);
                    }
                }
            }
            long rawDataLength = _writer.BaseStream.Position - rawDataStart;

            long nextSegmentOffset = metaDataLength + rawDataLength;

            // Write lead-in
            _writer.BaseStream.Seek(0, SeekOrigin.Begin);

            uint tocMask = (1 << 1) | (1 << 2); // MetaData + NewObjList
            if (rawDataLength > 0) tocMask |= (1 << 3);

            _writer.Write(Encoding.ASCII.GetBytes("TDSm"));
            _writer.Write(tocMask);
            _writer.Write((uint)4713);
            _writer.Write((ulong)nextSegmentOffset);
            _writer.Write((ulong)metaDataLength);

            _writer.BaseStream.Seek(0, SeekOrigin.End);
        }

        private void WriteMetaData(TdmsFile file)
        {
            uint objectCount = 1 + (uint)file.ChannelGroups.Count;
            foreach (var group in file.ChannelGroups)
                objectCount += (uint)group.Channels.Count;
            
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
                // Use stack allocation for index
                Span<byte> indexBuffer = stackalloc byte[32];
                int indexLength = 0;

                BitConverter.TryWriteBytes(indexBuffer.Slice(indexLength, 4), (uint)channel.DataType);
                indexLength += 4;
                BitConverter.TryWriteBytes(indexBuffer.Slice(indexLength, 4), (uint)1);
                indexLength += 4;
                BitConverter.TryWriteBytes(indexBuffer.Slice(indexLength, 8), numValues);
                indexLength += 8;

                if (channel.DataType == TdsDataType.String && channel.Data != null)
                {
                    var totalBytes = 0UL;
                    foreach (var s in (string[])channel.Data) 
                        totalBytes += (ulong)Encoding.UTF8.GetByteCount(s);
                    BitConverter.TryWriteBytes(indexBuffer.Slice(indexLength, 8), totalBytes);
                    indexLength += 8;
                }

                writer.Write((uint)indexLength);
                writer.Write(indexBuffer.Slice(0, indexLength));
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
                if (prop.Value != null) 
                    WriteValue(writer, prop.Value, prop.DataType);
            }
        }

        internal static void WriteRawDataOptimized(BinaryWriter writer, object data)
        {
            var array = (Array)data;
            if (array.Length == 0) return;

            var elementType = array.GetType().GetElementType();
            if (elementType == null) return;

            if (elementType.IsPrimitive && elementType != typeof(bool))
            {
                // Fast path for primitive types using Buffer.BlockCopy
                var elementSize = Marshal.SizeOf(elementType);
                var totalBytes = array.Length * elementSize;
                
                // Use pooled buffer for large arrays
                if (totalBytes > 81920)
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(totalBytes);
                    try
                    {
                        Buffer.BlockCopy(array, 0, buffer, 0, totalBytes);
                        writer.Write(buffer, 0, totalBytes);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
                else
                {
                    var buffer = new byte[totalBytes];
                    Buffer.BlockCopy(array, 0, buffer, 0, totalBytes);
                    writer.Write(buffer);
                }
            }
            else if (elementType == typeof(string))
            {
                WriteStringArray(writer, (string[])data);
            }
            else if (elementType == typeof(bool))
            {
                foreach (var b in (bool[])data) 
                    writer.Write((byte)(b ? 1 : 0));
            }
            else if (elementType == typeof(DateTime))
            {
                foreach (var dt in (DateTime[])data)
                {
                    WriteTimestamp(writer, dt);
                }
            }
            else
            {
                throw new NotSupportedException($"Writing {elementType} is not supported.");
            }
        }

        private static void WriteStringArray(BinaryWriter writer, string[] strings)
        {
            // Pre-calculate total size
            var totalStringBytes = 0;
            foreach (var s in strings)
                totalStringBytes += Encoding.UTF8.GetByteCount(s);

            // Use pooled buffer for offsets and string data
            var offsetBuffer = ArrayPool<byte>.Shared.Rent(strings.Length * 4);
            var stringBuffer = ArrayPool<byte>.Shared.Rent(totalStringBytes);
            
            try
            {
                var currentOffset = (uint)(strings.Length * 4);
                var stringBufferPos = 0;

                for (int i = 0; i < strings.Length; i++)
                {
                    BitConverter.TryWriteBytes(offsetBuffer.AsSpan(i * 4, 4), currentOffset);
                    var byteCount = Encoding.UTF8.GetBytes(strings[i], 0, strings[i].Length, 
                                                          stringBuffer, stringBufferPos);
                    currentOffset += (uint)byteCount;
                    stringBufferPos += byteCount;
                }

                writer.Write(offsetBuffer, 0, strings.Length * 4);
                writer.Write(stringBuffer, 0, stringBufferPos);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(offsetBuffer);
                ArrayPool<byte>.Shared.Return(stringBuffer);
            }
        }

        internal static void WriteRawData(BinaryWriter writer, object data)
        {
            WriteRawDataOptimized(writer, data);
        }

        internal static void WriteString(BinaryWriter writer, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            writer.Write((uint)bytes.Length);
            writer.Write(bytes);
        }

        private static void WriteTimestamp(BinaryWriter writer, DateTime dt)
        {
            var timestamp = dt.ToUniversalTime();
            var timespan = timestamp - TdmsEpoch;
            long seconds = (long)timespan.TotalSeconds;
            var fractions = (ulong)((timespan.Ticks % TimeSpan.TicksPerSecond) * 
                                   (1.0 / TimeSpan.TicksPerSecond * Math.Pow(2, 64)));
            writer.Write(seconds);
            writer.Write(fractions);
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
                    var fractions = (ulong)((timespan.Ticks % TimeSpan.TicksPerSecond) * 
                                           (1.0 / TimeSpan.TicksPerSecond * Math.Pow(2, 64)));
                    writer.Write(fractions);
                    writer.Write(seconds);
                    break;
                default:
                    throw new NotSupportedException($"Data type {dataType} not supported.");
            }
        }
    }
}