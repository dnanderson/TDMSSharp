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
    /// <summary>
    /// Provides a low-level reader for TDMS files.
    /// </summary>
    public class TdmsReader2
    {
        private readonly BinaryReader _reader;
        private static readonly DateTime TdmsEpoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly byte[] _readBuffer = new byte[8192]; // Reusable buffer
        private readonly Dictionary<string, TdmsRawDataIndex> _previousIndices = new Dictionary<string, TdmsRawDataIndex>();

        /// <summary>
        /// Initializes a new instance of the <see cref="TdmsReader2"/> class.
        /// </summary>
        /// <param name="stream">The stream to read the TDMS file from.</param>
        public TdmsReader2(Stream stream)
        {
            _reader = new BinaryReader(stream, Encoding.UTF8, true);
        }

        /// <summary>
        /// Reads the TDMS file from the stream and returns a <see cref="TdmsFile"/> object.
        /// </summary>
        /// <returns>A <see cref="TdmsFile"/> object representing the contents of the TDMS file.</returns>
        public TdmsFile ReadFile()
        {
            var file = new TdmsFile();
            while (_reader.BaseStream.Position < _reader.BaseStream.Length)
            {
                try
                {
                    ReadSegment(file);
                }
                catch (EndOfStreamException)
                {
                    // Reached end of file unexpectedly, stop reading.
                    break;
                }
            }
            return file;
        }

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
                uint objectCount;
                try { objectCount = _reader.ReadUInt32(); }
                catch (EndOfStreamException) { objectCount = 0; }

                for (int i = 0; i < objectCount; i++)
                {
                    try
                    {
                        var path = ReadStringOptimized();
                        if (string.IsNullOrEmpty(path) && _reader.BaseStream.Position >= _reader.BaseStream.Length)
                            break;

                        var pathParts = ParsePathOptimized(path);

                        object? tdmsObject = null;
                        TdmsChannel? channel = null;

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
                                    ulong totalSize = 0;
                                    if (dataType == TdsDataType.String)
                                        totalSize = _reader.ReadUInt64();

                                    var rawDataIndex = new TdmsRawDataIndex(dataType, segmentValueCount, totalSize);
                                    _previousIndices[path] = rawDataIndex;

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
                                }
                                break;
                        }

                        tdmsObject = tdmsObject ?? channel;
                        if (tdmsObject == null) continue;

                        ReadProperties(tdmsObject);
                    }
                    catch (EndOfStreamException)
                    {
                        // Metadata for this object is truncated. Stop reading metadata for this segment.
                        break;
                    }
                }
            }

            long rawDataPosition = segmentStartPosition + 28 + (long)leadIn.Value.rawDataOffset;
            if (rawDataPosition >= _reader.BaseStream.Length)
            {
                _reader.BaseStream.Position = _reader.BaseStream.Length;
                return;
            }
            _reader.BaseStream.Seek(rawDataPosition, SeekOrigin.Begin);

            bool isInterleaved = (leadIn.Value.tocMask & (1 << 5)) != 0;
            if (isInterleaved)
            {
                ReadInterleavedData(channelsInSegment, valueCounts, daqmxIndices);
            }
            else
            {
                ReadContiguousData(channelsInSegment, valueCounts, daqmxIndices);
            }

            long nextSegmentPosition = segmentStartPosition + 28 + (long)leadIn.Value.nextSegmentOffset;
            if (leadIn.Value.nextSegmentOffset == 0xFFFFFFFFFFFFFFFF || nextSegmentPosition >= _reader.BaseStream.Length)
                _reader.BaseStream.Position = _reader.BaseStream.Length;
            else if (nextSegmentPosition <= segmentStartPosition)
                _reader.BaseStream.Position = _reader.BaseStream.Length;
            else
                _reader.BaseStream.Position = nextSegmentPosition;
        }

        private void ReadContiguousData(List<TdmsChannel> channelsInSegment, Dictionary<TdmsChannel, ulong> valueCounts, Dictionary<TdmsChannel, TdmsDAQmxRawDataIndex> daqmxIndices)
        {
            foreach (var channel in channelsInSegment)
            {
                try
                {
                    if (daqmxIndices.TryGetValue(channel, out var daqmxIndex))
                    {
                        TdmsDAQmxReader.ReadDAQmxData(_reader, channel, daqmxIndex);
                    }
                    else
                    {
                        ReadChannelData(channel, valueCounts[channel]);
                    }
                }
                catch (EndOfStreamException)
                {
                    // Data for this channel is truncated. Continue to next channel.
                }
            }
        }

        private void ReadInterleavedData(List<TdmsChannel> channelsInSegment, Dictionary<TdmsChannel, ulong> valueCounts, Dictionary<TdmsChannel, TdmsDAQmxRawDataIndex> daqmxIndices)
        {
            if (daqmxIndices.Any())
                throw new NotSupportedException("Interleaved DAQmx data is not supported in this version.");
            if (!channelsInSegment.Any()) return;
            if (channelsInSegment.Any(c => c.DataType == TdsDataType.String))
                throw new NotSupportedException("Interleaved string data is not supported.");

            var numValues = valueCounts[channelsInSegment[0]];
            if (valueCounts.Values.Any(v => v != numValues))
                throw new NotSupportedException("Interleaving is only supported for channels with the same number of values in a segment.");
            if (numValues == 0) return;

            var channelOffsets = new List<int>();
            int stride = 0;
            foreach (var channel in channelsInSegment)
            {
                channelOffsets.Add(stride);
                stride += GetElementSize(channel.DataType);
            }
            long totalBytesToRead = (long)numValues * stride;
            if (_reader.BaseStream.Position + totalBytesToRead > _reader.BaseStream.Length)
                totalBytesToRead = _reader.BaseStream.Length - _reader.BaseStream.Position;

            byte[] interleavedData = new byte[totalBytesToRead];
            _reader.BaseStream.Read(interleavedData, 0, (int)totalBytesToRead);

            if (stride > 0)
                numValues = (ulong)interleavedData.Length / (ulong)stride;
            else
                numValues = 0;
            if (numValues == 0) return;

            var channelByteBuffers = channelsInSegment.ToDictionary(
                c => c,
                c => new byte[(long)numValues * GetElementSize(c.DataType)]
            );

            for (ulong i = 0; i < numValues; i++)
            {
                long sourceOffset = (long)i * stride;
                for (int j = 0; j < channelsInSegment.Count; j++)
                {
                    var channel = channelsInSegment[j];
                    int elementSize = GetElementSize(channel.DataType);
                    long destOffset = (long)i * elementSize;
                    int channelOffsetInStride = channelOffsets[j];
                    if (sourceOffset + channelOffsetInStride + elementSize <= interleavedData.Length)
                        Buffer.BlockCopy(interleavedData, (int)(sourceOffset + channelOffsetInStride), channelByteBuffers[channel], (int)destOffset, elementSize);
                }
            }

            foreach (var channel in channelsInSegment)
            {
                var channelType = TdsDataTypeProvider.GetType(channel.DataType);
                var typedArray = Array.CreateInstance(channelType, (int)numValues);
                var byteBuffer = channelByteBuffers[channel];
                Buffer.BlockCopy(byteBuffer, 0, typedArray, 0, byteBuffer.Length);

                var addDataChunkMethod = channel.GetType().GetMethod("AddDataChunk");
                if (addDataChunkMethod == null) throw new InvalidOperationException("Could not find AddDataChunk method.");
                addDataChunkMethod.Invoke(channel, new object[] { typedArray });
            }
        }

        private TdmsDAQmxRawDataIndex ReadDAQmxRawDataIndex(bool isDigitalLineScaler)
        {
            _reader.ReadUInt32(); // Array dimension (always 1)
            var numberOfValues = _reader.ReadUInt64();

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

            var widthCount = _reader.ReadUInt32();
            var rawDataWidths = new uint[widthCount];
            for (int i = 0; i < widthCount; i++)
            {
                rawDataWidths[i] = _reader.ReadUInt32();
            }

            return new TdmsDAQmxRawDataIndex(numberOfValues, scalers, rawDataWidths, isDigitalLineScaler);
        }

        private int GetElementSize(TdsDataType dataType)
        {
            return dataType switch
            {
                TdsDataType.I8 or TdsDataType.U8 or TdsDataType.Boolean => 1,
                TdsDataType.I16 or TdsDataType.U16 => 2,
                TdsDataType.I32 or TdsDataType.U32 or TdsDataType.SingleFloat => 4,
                TdsDataType.I64 or TdsDataType.U64 or TdsDataType.DoubleFloat => 8,
                TdsDataType.TimeStamp => 16,
                _ => 0 // For unsupported types like string in interleaved data
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TdmsChannel CreateTypedChannel(string path, TdsDataType dataType)
        {
            var channelType = TdsDataTypeProvider.GetType(dataType);
            var genericChannelType = typeof(TdmsChannel<>).MakeGenericType(channelType);
            if (Activator.CreateInstance(genericChannelType, path) is not TdmsChannel channel)
                throw new InvalidOperationException($"Could not create channel for data type {dataType}");
            return channel;
        }

        private void ReadProperties(object tdmsObject)
        {
            var numProperties = _reader.ReadUInt32();
            var properties = GetPropertiesList(tdmsObject);

            for (int j = 0; j < numProperties; j++)
            {
                try
                {
                    var propName = ReadStringOptimized();
                    if (string.IsNullOrEmpty(propName) && _reader.BaseStream.Position >= _reader.BaseStream.Length) break;
                    var propDataType = (TdsDataType)_reader.ReadUInt32();
                    var propValue = ReadValue(propDataType);
                    var existingProp = properties.FirstOrDefault(p => p.Name == propName);
                    if (existingProp != null)
                    {
                        properties.Remove(existingProp);
                    }
                    properties.Add(new TdmsProperty(propName, propDataType, propValue));
                }
                catch (EndOfStreamException)
                {
                    // Truncated properties, stop reading them for this object.
                    break;
                }
            }
        }

        private (uint tocMask, uint version, ulong nextSegmentOffset, ulong rawDataOffset)? ReadLeadIn()
        {
            if (_reader.BaseStream.Position + 28 > _reader.BaseStream.Length) return null;

            var tag = _reader.ReadBytes(4);
            if (tag.Length < 4 || Encoding.ASCII.GetString(tag) != "TDSm") return null;

            return (_reader.ReadUInt32(), _reader.ReadUInt32(), _reader.ReadUInt64(), _reader.ReadUInt64());
        }

        private void ReadChannelData(TdmsChannel channel, ulong count)
        {
            if (count == 0) return;

            switch (channel.DataType)
            {
                case TdsDataType.I32: ReadPrimitiveArrayOptimized<int>(channel, count); break;
                case TdsDataType.DoubleFloat: ReadPrimitiveArrayOptimized<double>(channel, count); break;
                case TdsDataType.SingleFloat: ReadPrimitiveArrayOptimized<float>(channel, count); break;
                case TdsDataType.I64: ReadPrimitiveArrayOptimized<long>(channel, count); break;
                case TdsDataType.U64: ReadPrimitiveArrayOptimized<ulong>(channel, count); break;
                case TdsDataType.I16: ReadPrimitiveArrayOptimized<short>(channel, count); break;
                case TdsDataType.U16: ReadPrimitiveArrayOptimized<ushort>(channel, count); break;
                case TdsDataType.I8: ReadPrimitiveArrayOptimized<sbyte>(channel, count); break;
                case TdsDataType.U8: ReadPrimitiveArrayOptimized<byte>(channel, count); break;
                default: ReadChannelDataGeneric(channel, count); break;
            }
        }

        private void ReadPrimitiveArrayOptimized<T>(TdmsChannel channel, ulong count) where T : struct
        {
            var typedChannel = (TdmsChannel<T>)channel;
            long bytesToRead = (long)count * Marshal.SizeOf(typeof(T));
            if (_reader.BaseStream.Position + bytesToRead > _reader.BaseStream.Length)
            {
                bytesToRead = _reader.BaseStream.Length - _reader.BaseStream.Position;
            }
            if (bytesToRead <= 0) return;

            var numElements = bytesToRead / Marshal.SizeOf(typeof(T));
            var chunk = new T[numElements];
            var byteSpan = MemoryMarshal.AsBytes(chunk.AsSpan());

            int totalBytesRead = 0;
            while (totalBytesRead < bytesToRead)
            {
                int read = _reader.BaseStream.Read(byteSpan.Slice(totalBytesRead));
                if (read == 0) break;
                totalBytesRead += read;
            }

            typedChannel.AddDataChunk(chunk);
        }

        private void ReadChannelDataGeneric(TdmsChannel channel, ulong count)
        {
            var channelType = TdsDataTypeProvider.GetType(channel.DataType);
            var readMethod = typeof(TdmsReader2).GetMethod(nameof(ReadData),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (readMethod == null) throw new InvalidOperationException("Could not find ReadData method.");
            var genericReadMethod = readMethod.MakeGenericMethod(channelType);

            var data = (Array)genericReadMethod.Invoke(this, new object[] { count })!;

            var addDataChunkMethod = typeof(TdmsChannel<>).MakeGenericType(channelType)
                .GetMethod("AddDataChunk");
            if (addDataChunkMethod == null) throw new InvalidOperationException("Could not find AddDataChunk method.");
            addDataChunkMethod.Invoke(channel, new object[] { data });
        }

        private T[] ReadData<T>(ulong count)
        {
            if (typeof(T) == typeof(string))
            {
                if (count == 0) return (T[])(object)Array.Empty<string>();

                var end_offsets = new uint[count];
                var buffer = new byte[count * 4];
                _reader.Read(buffer, 0, buffer.Length);
                Buffer.BlockCopy(buffer, 0, end_offsets, 0, buffer.Length);

                var totalStringSize = end_offsets[count - 1];
                var stringDataBytes = _reader.ReadBytes((int)totalStringSize);

                var strings = new string[count];
                uint startOffset = 0;
                for (ulong i = 0; i < count; i++)
                {
                    var endOffset = end_offsets[i];
                    var stringLength = endOffset - startOffset;
                    strings[i] = Encoding.UTF8.GetString(stringDataBytes, (int)startOffset, (int)stringLength);
                    startOffset = endOffset;
                }
                return (T[])(object)strings;
            }

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

            if (path.StartsWith("/"))
                path = path.Substring(1);

            return path.Split(new[] { "'/'" }, StringSplitOptions.None)
                       .Select(p => p.Trim('\'').Replace("''", "'"))
                       .ToList();
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
            uint length;
            try { length = _reader.ReadUInt32(); }
            catch (EndOfStreamException) { return string.Empty; }

            if (length == 0) return string.Empty;
            if (length > _reader.BaseStream.Length - _reader.BaseStream.Position)
                length = (uint)(_reader.BaseStream.Length - _reader.BaseStream.Position);

            var buffer = ArrayPool<byte>.Shared.Rent((int)length);
            try
            {
                int bytesRead = _reader.BaseStream.Read(buffer, 0, (int)length);
                return Encoding.UTF8.GetString(buffer, 0, bytesRead);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
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