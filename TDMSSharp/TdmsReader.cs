using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace TDMSSharp
{
    public class TdmsReader
    {
        private readonly BinaryReader _reader;
        private static readonly DateTime TdmsEpoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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
            if (leadIn == null) { _reader.BaseStream.Position = _reader.BaseStream.Length; return; }

            var channelsInSegment = new List<TdmsChannel>();
            var valueCounts = new Dictionary<TdmsChannel, ulong>();

            if ((leadIn.Value.tocMask & (1 << 1)) != 0) // kTocMetaData
            {
                var objectCount = _reader.ReadUInt32();
                for (int i = 0; i < objectCount; i++)
                {
                    var path = ReadString();
                    var pathParts = ParsePath(path);

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

                    var rawDataIndexLength = _reader.ReadUInt32();

                    if (rawDataIndexLength == 0xFFFFFFFF)
                    {
                        // No raw data for this object
                    }
                    else if (rawDataIndexLength == 0x00000000)
                    {
                        // Raw data index matches previous segment - reuse it
                        if (_previousIndices.TryGetValue(path, out var previousIndex))
                        {
                            if (channel == null)
                            {
                                var channelType = TdsDataTypeProvider.GetType(previousIndex.DataType);
                                var genericChannelType = typeof(TdmsChannel<>).MakeGenericType(channelType);
                                channel = (TdmsChannel)Activator.CreateInstance(genericChannelType, path);
                                file.GetOrAddChannelGroup(pathParts[0]).Channels.Add(channel);
                            }

                            channel.DataType = previousIndex.DataType;
                            channel.NumberOfValues += previousIndex.NumberOfValues;

                            if (previousIndex.NumberOfValues > 0)
                            {
                                channelsInSegment.Add(channel);
                                valueCounts[channel] = previousIndex.NumberOfValues;
                            }
                        }
                    }
                    else if (rawDataIndexLength > 0)
                    {
                        var dataType = (TdsDataType)_reader.ReadUInt32();
                        _reader.ReadUInt32(); // Dimension
                        var segmentValueCount = _reader.ReadUInt64();

                        // Store this index for future segments
                        _previousIndices[path] = new TdmsRawDataIndex(dataType, segmentValueCount);

                        if (channel == null)
                        {
                            var channelType = TdsDataTypeProvider.GetType(dataType);
                            var genericChannelType = typeof(TdmsChannel<>).MakeGenericType(channelType);
                            channel = (TdmsChannel)Activator.CreateInstance(genericChannelType, path);
                            file.GetOrAddChannelGroup(pathParts[0]).Channels.Add(channel);
                        }

                        channel.DataType = dataType;
                        channel.NumberOfValues += segmentValueCount;
                        if (segmentValueCount > 0)
                        {
                            channelsInSegment.Add(channel);
                            valueCounts[channel] = segmentValueCount;
                        }
                        if (dataType == TdsDataType.String) _reader.ReadUInt64();
                    }

                    tdmsObject = tdmsObject ?? channel;
                    if (tdmsObject == null) continue;

                    var numProperties = _reader.ReadUInt32();
                    var properties = GetPropertiesList(tdmsObject);
                    for (int j = 0; j < numProperties; j++)
                    {
                        var propName = ReadString();
                        var propDataType = (TdsDataType)_reader.ReadUInt32();
                        var propValue = ReadValue(propDataType);
                        properties.Add(new TdmsProperty(propName, propDataType, propValue));
                    }
                }
            }

            long rawDataPosition = segmentStartPosition + 28 + (long)leadIn.Value.rawDataOffset;
            _reader.BaseStream.Seek(rawDataPosition, SeekOrigin.Begin);
            foreach (var channel in channelsInSegment)
            {
                ReadChannelData(channel, valueCounts[channel]);
            }

            long nextSegmentPosition = segmentStartPosition + 28 + (long)leadIn.Value.nextSegmentOffset;
            if (nextSegmentPosition <= segmentStartPosition) _reader.BaseStream.Position = _reader.BaseStream.Length;
            else _reader.BaseStream.Position = nextSegmentPosition;
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
            var channelType = TdsDataTypeProvider.GetType(channel.DataType);
            var readMethod = typeof(TdmsReader).GetMethod(nameof(ReadData), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                             .MakeGenericMethod(channelType);
            var data = (Array)readMethod.Invoke(this, new object[] { count });
            typeof(TdmsChannel<>).MakeGenericType(channelType)
                                 .GetMethod("AppendData")
                                 .Invoke(channel, new object[] { data });
        }

        private T[] ReadData<T>(ulong count)
        {
            var data = new T[count];
            var type = TdsDataTypeProvider.GetDataType<T>();
            for (ulong i = 0; i < count; i++) data[i] = (T)ReadValue(type);
            return data;
        }

        private List<string> ParsePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/") return new List<string>();
            return path.Substring(1).Split(new[] { "'/'" }, StringSplitOptions.None)
                       .Select(p => p.Trim('\'').Replace("''", "'")).ToList();
        }

        private IList<TdmsProperty> GetPropertiesList(object tdmsObject)
        {
            if (tdmsObject is TdmsFile f) return f.Properties;
            if (tdmsObject is TdmsChannelGroup g) return g.Properties;
            if (tdmsObject is TdmsChannel c) return c.Properties;
            throw new ArgumentException("Unknown TDMS object type.");
        }

        private string ReadString()
        {
            var length = _reader.ReadUInt32();
            return length > 0 ? Encoding.UTF8.GetString(_reader.ReadBytes((int)length)) : string.Empty;
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
                case TdsDataType.String: return ReadString();
                case TdsDataType.Boolean: return _reader.ReadBoolean();
                case TdsDataType.TimeStamp:
                    var seconds = _reader.ReadInt64();
                    var fractions = _reader.ReadUInt64();
                    var ticks = (long)(new BigInteger(fractions) * 10_000_000 / (BigInteger.One << 64));
                    return TdmsEpoch.AddSeconds(seconds).AddTicks(ticks);
                default: throw new NotSupportedException($"Data type {dataType} not implemented in this snippet.");
            }
        }
    }
}