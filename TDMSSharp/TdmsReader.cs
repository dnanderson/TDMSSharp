using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace TDMSSharp
{
    public class TdmsReader
    {
        private readonly BinaryReader _reader;
        private static readonly DateTime TdmsEpoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly byte[] _readBuffer = new byte[8192]; // Reusable buffer

        public TdmsReader(Stream stream)
        {
            _reader = new BinaryReader(stream, Encoding.UTF8, true);
        }

        public TdmsFile ReadFile()
        {
            var file = new TdmsFile();
            while (_reader.BaseStream.Position < _reader.BaseStream.Length)
            {
                ReadSegment(file);
            }
            return file;
        }

        private readonly Dictionary<string, TdmsRawDataIndex> _previousIndices = new Dictionary<string, TdmsRawDataIndex>();

        private void ReadSegment(TdmsFile file)
        {
            long segmentStartPosition = _reader.BaseStream.Position;

            var leadIn = ReadLeadIn();
            if (leadIn == null) 
            { 
                _reader.BaseStream.Position = _reader.BaseStream.Length; 
                return; 
            }

            var channelsInSegment = new List<TdmsChannel>();
            var valueCounts = new Dictionary<TdmsChannel, ulong>();
            var daqmxIndices = new Dictionary<TdmsChannel, TdmsDAQmxRawDataIndex>();

            if ((leadIn.Value.tocMask & (1 << 1)) != 0) // kTocMetaData
            {
                var objectCount = _reader.ReadUInt32();
                for (int i = 0; i < objectCount; i++)
                {
                    var path = ReadStringOptimized();
                    var pathParts = ParsePathOptimized(path);

                    object tdmsObject = null;
                    TdmsChannel channel = null;

                    if (pathParts.Count == 0)
                    {
                        tdmsObject = file;
                    }
                    else if (pathParts.Count == 1)
                    {
                        tdmsObject = file.GetOrAddChannelGroup(pathParts[0]);
                    }
                    else // Channel
                    {
                        var group = file.GetOrAddChannelGroup(pathParts[0]);
                        channel = group.Channels.FirstOrDefault(c => c.Path == path);
                    }

                    var indexIdentifier = _reader.ReadUInt32();

                    switch (indexIdentifier)
                    {
                        case 0xFFFFFFFF: // No raw data
                            break;

                        case 0x00000000: // Reuse previous index
                            if (_previousIndices.TryGetValue(path, out var previousIndex))
                            {
                                if (channel == null)
                                {
                                    var dataType = previousIndex is TdmsDAQmxRawDataIndex daqmxPrev
                                        ? daqmxPrev.GetPrimaryDataType()
                                        : previousIndex.DataType;
                                    channel = CreateTypedChannel(path, dataType);
                                    file.GetOrAddChannelGroup(pathParts[0]).Channels.Add(channel);
                                }

                                channel.DataType = previousIndex.DataType;
                                channel.NumberOfValues += previousIndex.NumberOfValues;

                                if (previousIndex.NumberOfValues > 0)
                                {
                                    channelsInSegment.Add(channel);
                                    valueCounts[channel] = previousIndex.NumberOfValues;

                                    if (previousIndex is TdmsDAQmxRawDataIndex daqmxIdx)
                                    {
                                        daqmxIndices[channel] = daqmxIdx;
                                    }
                                }
                            }
                            break;

                        case 0x69120000: // DAQmx Format Changing Scaler
                        case 0x69130000: // DAQmx Digital Line Scaler
                            {
                                bool isDigitalLineScaler = (indexIdentifier == 0x69130000);
                                var daqmxIndex = ReadDAQmxRawDataIndex(isDigitalLineScaler);

                                _previousIndices[path] = daqmxIndex;

                                if (channel == null)
                                {
                                    channel = CreateTypedChannel(path, daqmxIndex.GetPrimaryDataType());
                                    file.GetOrAddChannelGroup(pathParts[0]).Channels.Add(channel);
                                }

                                channel.DataType = TdsDataType.DAQmxRawData;
                                channel.NumberOfValues += daqmxIndex.NumberOfValues;

                                if (daqmxIndex.NumberOfValues > 0)
                                {
                                    channelsInSegment.Add(channel);
                                    valueCounts[channel] = daqmxIndex.NumberOfValues;
                                    daqmxIndices[channel] = daqmxIndex;
                                }
                            }
                            break;

                        default: // Standard raw data index (indexIdentifier is the length)
                            {
                                var dataType = (TdsDataType)_reader.ReadUInt32();
                                _reader.ReadUInt32(); // Dimension
                                var segmentValueCount = _reader.ReadUInt64();

                                _previousIndices[path] = new TdmsRawDataIndex(dataType, segmentValueCount);

                                if (channel == null)
                                {
                                    channel = CreateTypedChannel(path, dataType);
                                    file.GetOrAddChannelGroup(pathParts[0]).Channels.Add(channel);
                                }

                                channel.DataType = dataType;
                                channel.NumberOfValues += segmentValueCount;
                                if (segmentValueCount > 0)
                                {
                                    channelsInSegment.Add(channel);
                                    valueCounts[channel] = segmentValueCount;
                                }
                                if (dataType == TdsDataType.String)
                                    _reader.ReadUInt64();
                            }
                            break;
                    }

                    tdmsObject = tdmsObject ?? channel;
                    if (tdmsObject == null) continue;

                    ReadProperties(tdmsObject);
                }
            }

            long rawDataPosition = segmentStartPosition + 28 + (long)leadIn.Value.rawDataOffset;
            _reader.BaseStream.Seek(rawDataPosition, SeekOrigin.Begin);
            
            // Read raw data for each channel
            foreach (var channel in channelsInSegment)
            {
                if (daqmxIndices.TryGetValue(channel, out var daqmxIndex))
                {
                    // Use DAQmx reader for DAQmx data
                    TdmsDAQmxReader.ReadDAQmxData(_reader, channel, daqmxIndex);
                }
                else
                {
                    // Standard data reading
                    ReadChannelDataOptimized(channel, valueCounts[channel]);
                }
            }

            long nextSegmentPosition = segmentStartPosition + 28 + (long)leadIn.Value.nextSegmentOffset;
            if (nextSegmentPosition <= segmentStartPosition) 
                _reader.BaseStream.Position = _reader.BaseStream.Length;
            else 
                _reader.BaseStream.Position = nextSegmentPosition;
        }

        /// <summary>
        /// Read DAQmx raw data index structure
        /// </summary>
        private TdmsDAQmxRawDataIndex ReadDAQmxRawDataIndex(bool isDigitalLineScaler)
        {
            _reader.ReadUInt32(); // Array dimension (always 1)
            var numberOfValues = _reader.ReadUInt64();
            
            // Read Format Changing scalers vector
            var scalerCount = _reader.ReadUInt32();
            var scalers = new TdmsDAQmxScaler[scalerCount];
            
            for (int i = 0; i < scalerCount; i++)
            {
                scalers[i] = new TdmsDAQmxScaler
                {
                    DataType = (TdsDataType)_reader.ReadUInt32(),
                    RawBufferIndex = _reader.ReadUInt32(),
                    RawByteOffsetWithinStride = _reader.ReadUInt32(),
                    SampleFormatBitmap = _reader.ReadUInt32(),
                    ScaleId = _reader.ReadUInt32()
                };
            }
            
            // Read raw data width vector
            var widthCount = _reader.ReadUInt32();
            var rawDataWidths = new uint[widthCount];
            for (int i = 0; i < widthCount; i++)
            {
                rawDataWidths[i] = _reader.ReadUInt32();
            }
            
            return new TdmsDAQmxRawDataIndex(numberOfValues, scalers, rawDataWidths, isDigitalLineScaler);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TdmsChannel CreateTypedChannel(string path, TdsDataType dataType)
        {
            var channelType = TdsDataTypeProvider.GetType(dataType);
            var genericChannelType = typeof(TdmsChannel<>).MakeGenericType(channelType);
            return (TdmsChannel)Activator.CreateInstance(genericChannelType, path);
        }

        private void ReadProperties(object tdmsObject)
        {
            var numProperties = _reader.ReadUInt32();
            var properties = GetPropertiesList(tdmsObject);
            
            for (int j = 0; j < numProperties; j++)
            {
                var propName = ReadStringOptimized();
                var propDataType = (TdsDataType)_reader.ReadUInt32();
                var propValue = ReadValue(propDataType);
                properties.Add(new TdmsProperty(propName, propDataType, propValue));
            }
        }

        private (uint tocMask, uint version, ulong nextSegmentOffset, ulong rawDataOffset)? ReadLeadIn()
        {
            if (_reader.BaseStream.Position + 28 > _reader.BaseStream.Length) return null;
            
            var tag = _reader.ReadBytes(4);
            if (tag.Length < 4 || Encoding.ASCII.GetString(tag) != "TDSm") return null;
            
            return (_reader.ReadUInt32(), _reader.ReadUInt32(), _reader.ReadUInt64(), _reader.ReadUInt64());
        }

        private void ReadChannelDataOptimized(TdmsChannel channel, ulong count)
        {
            if (count == 0) return;

            switch (channel.DataType)
            {
                case TdsDataType.I32:
                    ReadPrimitiveArrayOptimized<int>(channel, count);
                    break;
                case TdsDataType.DoubleFloat:
                    ReadPrimitiveArrayOptimized<double>(channel, count);
                    break;
                case TdsDataType.SingleFloat:
                    ReadPrimitiveArrayOptimized<float>(channel, count);
                    break;
                case TdsDataType.I64:
                    ReadPrimitiveArrayOptimized<long>(channel, count);
                    break;
                case TdsDataType.U64:
                    ReadPrimitiveArrayOptimized<ulong>(channel, count);
                    break;
                case TdsDataType.I16:
                    ReadPrimitiveArrayOptimized<short>(channel, count);
                    break;
                case TdsDataType.U16:
                    ReadPrimitiveArrayOptimized<ushort>(channel, count);
                    break;
                case TdsDataType.I8:
                    ReadPrimitiveArrayOptimized<sbyte>(channel, count);
                    break;
                case TdsDataType.U8:
                    ReadPrimitiveArrayOptimized<byte>(channel, count);
                    break;
                default:
                    ReadChannelDataGeneric(channel, count);
                    break;
            }
        }

        private void ReadPrimitiveArrayOptimized<T>(TdmsChannel channel, ulong count) where T : struct
        {
            var typedChannel = (TdmsChannel<T>)channel;
            var chunk = new T[(int)count];
            var byteSpan = MemoryMarshal.AsBytes(chunk.AsSpan());

            int bytesRead = 0;
            while (bytesRead < byteSpan.Length)
            {
                int toRead = Math.Min(byteSpan.Length - bytesRead, _readBuffer.Length);
                int read = _reader.BaseStream.Read(_readBuffer, 0, toRead);
                if (read == 0) break;
                _readBuffer.AsSpan(0, read).CopyTo(byteSpan.Slice(bytesRead));
                bytesRead += read;
            }

            typedChannel.AddDataChunk(chunk);
        }

        private void ReadChannelDataGeneric(TdmsChannel channel, ulong count)
        {
            var channelType = TdsDataTypeProvider.GetType(channel.DataType);
            var readMethod = typeof(TdmsReader).GetMethod(nameof(ReadData),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .MakeGenericMethod(channelType);

            var data = (Array)readMethod.Invoke(this, new object[] { count });

            typeof(TdmsChannel<>).MakeGenericType(channelType)
                .GetMethod("AddDataChunk")
                .Invoke(channel, new object[] { data });
        }

        private T[] ReadData<T>(ulong count)
        {
            var data = new T[count];
            var type = TdsDataTypeProvider.GetDataType<T>();
            for (ulong i = 0; i < count; i++) 
                data[i] = (T)ReadValue(type);
            return data;
        }

        private List<string> ParsePathOptimized(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/") 
                return new List<string>();
            
            var result = new List<string>(2); // Pre-allocate for typical case
            var span = path.AsSpan(1); // Skip leading /
            
            int start = 0;
            for (int i = 0; i < span.Length; i++)
            {
                if (i + 2 < span.Length && span[i] == '\'' && span[i + 1] == '/' && span[i + 2] == '\'')
                {
                    var part = span.Slice(start, i - start).ToString().Trim('\'').Replace("''", "'");
                    result.Add(part);
                    start = i + 3;
                    i += 2;
                }
            }
            
            if (start < span.Length)
            {
                var part = span.Slice(start).ToString().Trim('\'').Replace("''", "'");
                result.Add(part);
            }
            
            return result;
        }

        private IList<TdmsProperty> GetPropertiesList(object tdmsObject)
        {
            return tdmsObject switch
            {
                TdmsFile f => f.Properties,
                TdmsChannelGroup g => g.Properties,
                TdmsChannel c => c.Properties,
                _ => throw new ArgumentException("Unknown TDMS object type.")
            };
        }

        private string ReadStringOptimized()
        {
            var length = _reader.ReadUInt32();
            if (length == 0) return string.Empty;
            
            // Use ArrayPool for large strings
            if (length > 1024)
            {
                var buffer = ArrayPool<byte>.Shared.Rent((int)length);
                try
                {
                    _reader.Read(buffer, 0, (int)length);
                    return Encoding.UTF8.GetString(buffer, 0, (int)length);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            else
            {
                return Encoding.UTF8.GetString(_reader.ReadBytes((int)length));
            }
        }

        private object ReadValue(TdsDataType dataType)
        {
            switch (dataType)
            {
                case TdsDataType.I8: return _reader.ReadSByte();
                case TdsDataType.I16: return _reader.ReadInt16();
                case TdsDataType.I32: return _reader.ReadInt32();
                case TdsDataType.I64: return _reader.ReadInt64();
                case TdsDataType.U8: return _reader.ReadByte();
                case TdsDataType.U16: return _reader.ReadUInt16();
                case TdsDataType.U32: return _reader.ReadUInt32();
                case TdsDataType.U64: return _reader.ReadUInt64();
                case TdsDataType.SingleFloat: return _reader.ReadSingle();
                case TdsDataType.DoubleFloat: return _reader.ReadDouble();
                case TdsDataType.String: return ReadStringOptimized();
                case TdsDataType.Boolean: return _reader.ReadBoolean();
                case TdsDataType.TimeStamp:
                    var fractions = _reader.ReadUInt64();
                    var seconds = _reader.ReadInt64();
                    var ticks = (long)(new BigInteger(fractions) * 10_000_000 / (BigInteger.One << 64));
                    return TdmsEpoch.AddSeconds(seconds).AddTicks(ticks);
                default: 
                    throw new NotSupportedException($"Data type {dataType} not supported.");
            }
        }
    }
}