using System;
using System.Collections.Generic;
using System.IO;
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

        private void ReadSegment(TdmsFile file)
        {
            long segmentStartPosition = _reader.BaseStream.Position;

            var leadIn = ReadLeadIn();
            if (leadIn == null)
            {
                // Could not read a lead-in. Assume end of file or corruption.
                // To prevent infinite loop, advance position to the end.
                _reader.BaseStream.Position = _reader.BaseStream.Length;
                return;
            }

            if ((leadIn.Value.tocMask & (1 << 1)) != 0) // kTocMetaData
            {
                ReadMetaData(file);
            }

            // Advance stream to the start of the next segment.
            // The lead-in is 28 bytes long. nextSegmentOffset is the length of the rest of the segment.
            long nextSegmentPosition = segmentStartPosition + 28 + (long)leadIn.Value.nextSegmentOffset;
            if (nextSegmentPosition > _reader.BaseStream.Length)
            {
                // If the offset points past the end of the file, assume it's the last segment.
                _reader.BaseStream.Position = _reader.BaseStream.Length;
            }
            else
            {
                _reader.BaseStream.Position = nextSegmentPosition;
            }
        }

        private (uint tocMask, uint version, ulong nextSegmentOffset, ulong rawDataOffset)? ReadLeadIn()
        {
            if (_reader.BaseStream.Position + 28 > _reader.BaseStream.Length)
                return null;

            var tag = _reader.ReadBytes(4);
            if (Encoding.ASCII.GetString(tag) != "TDSm")
            {
                // If we are not at the end of the stream but can't find the tag,
                // it's possible the file is corrupt or we are out of sync.
                // We will return null and let the caller handle it.
                return null;
            }

            var tocMask = _reader.ReadUInt32();
            var version = _reader.ReadUInt32();
            var nextSegmentOffset = _reader.ReadUInt64();
            var rawDataOffset = _reader.ReadUInt64();

            return (tocMask, version, nextSegmentOffset, rawDataOffset);
        }

        private void ReadMetaData(TdmsFile file)
        {
            var objectCount = _reader.ReadUInt32();
            for (int i = 0; i < objectCount; i++)
            {
                var path = ReadString();
                var pathParts = ParsePath(path);

                object tdmsObject;

                if (pathParts.Count == 0)
                {
                    tdmsObject = file;
                }
                else if (pathParts.Count == 1)
                {
                    tdmsObject = file.GetOrAddChannelGroup(path);
                }
                else if (pathParts.Count == 2)
                {
                    var groupPath = $"/'{pathParts[0]}'";
                    var channelGroup = file.GetOrAddChannelGroup(groupPath);
                    tdmsObject = channelGroup.GetOrAddChannel(path);
                }
                else
                {
                    throw new NotSupportedException($"Paths with more than 2 levels are not supported: {path}");
                }

                var rawDataIndexLength = _reader.ReadUInt32();
                if (rawDataIndexLength > 0 && rawDataIndexLength != 0xFFFFFFFF)
                {
                    var dataType = (TdsDataType)_reader.ReadUInt32();
                    var dimension = _reader.ReadUInt32();
                    if (dimension != 1)
                        throw new NotSupportedException("Only 1-dimensional arrays are supported.");

                    var numberOfValues = _reader.ReadUInt64();

                    if (tdmsObject is TdmsChannel channel)
                    {
                        channel.DataType = dataType;
                        channel.NumberOfValues += numberOfValues;
                    }

                    if (dataType == TdsDataType.String)
                    {
                        var totalSizeInBytes = _reader.ReadUInt64();
                    }
                }

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

        private List<string> ParsePath(string path)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(path) || path == "/") return parts;

            var pathWithoutRoot = path.Substring(1);
            var stringParts = pathWithoutRoot.Split(new[] { "'/'" }, StringSplitOptions.None);
            foreach (var part in stringParts)
            {
                parts.Add(part.Trim('\'').Replace("''", "'"));
            }
            return parts;
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
            if (length == 0) return string.Empty;
            var bytes = _reader.ReadBytes((int)length);
            return Encoding.UTF8.GetString(bytes);
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
                    var fractions = _reader.ReadUInt64();
                    var seconds = _reader.ReadInt64();
                    var ticks = (long)(new BigInteger(fractions) * 10_000_000 / (BigInteger.One << 64));
                    return TdmsEpoch.AddSeconds(seconds).AddTicks(ticks);
                default:
                    throw new NotSupportedException($"Data type {dataType} is not supported for properties.");
            }
        }
    }
}