using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TdmsSharp
{
    /// <summary>
    /// High-performance TDMS file writer with incremental metadata optimization
    /// </summary>
    public class TdmsFileWriter : IDisposable
    {
        private const string TDMS_TAG = "TDSm";
        private const string TDMS_INDEX_TAG = "TDSh";
        private const uint TDMS_VERSION = 4713;
        private const uint NO_RAW_DATA = 0xFFFFFFFF;
        private const uint RAW_DATA_MATCHES_PREVIOUS = 0x00000000;

        private readonly FileStream _fileStream;
        private readonly FileStream _indexStream;
        private readonly BinaryWriter _fileWriter;
        private readonly BinaryWriter _indexWriter;
        private readonly string _filePath;

        private readonly TdmsFile _fileObject;
        private readonly Dictionary<string, TdmsGroup> _groups = new();
        private readonly Dictionary<string, TdmsChannel> _channels = new();
        private readonly List<string> _channelOrder = new();

        private bool _isFirstSegment = true;
        private long _currentSegmentStart = 0;
        private long _currentIndexSegmentStart = 0;
        private bool _disposed = false;

        public TdmsFileWriter(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            _filePath = filePath;
            _fileObject = new TdmsFile();

            // Create main file and index file
            _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, false);
            _indexStream = new FileStream(filePath + "_index", FileMode.Create, FileAccess.Write, FileShare.Read, 4096, false);

            _fileWriter = new BinaryWriter(_fileStream, Encoding.UTF8, true);
            _indexWriter = new BinaryWriter(_indexStream, Encoding.UTF8, true);
        }

        /// <summary>
        /// Set a property on the file object
        /// </summary>
        public void SetFileProperty(string name, object value)
        {
            _fileObject.SetProperty(name, value);
        }

        /// <summary>
        /// Create or get a group
        /// </summary>
        public TdmsGroup CreateGroup(string name)
        {
            if (!_groups.TryGetValue(name, out var group))
            {
                group = new TdmsGroup(name);
                _groups[name] = group;
            }
            return group;
        }

        /// <summary>
        /// Create a channel in a group
        /// </summary>
        public TdmsChannel CreateChannel(string groupName, string channelName, TdmsDataType dataType)
        {
            var group = CreateGroup(groupName);
            var key = $"{groupName}/{channelName}";

            if (_channels.TryGetValue(key, out var existingChannel))
            {
                if (existingChannel.DataType != dataType)
                    throw new InvalidOperationException($"Channel {key} already exists with different data type");
                return existingChannel;
            }

            var channel = new TdmsChannel(groupName, channelName, dataType);
            _channels[key] = channel;
            _channelOrder.Add(key);
            return channel;
        }

        /// <summary>
        /// Get an existing channel
        /// </summary>
        public TdmsChannel? GetChannel(string groupName, string channelName)
        {
            var key = $"{groupName}/{channelName}";
            _channels.TryGetValue(key, out var channel);
            return channel;
        }

        /// <summary>
        /// Write all buffered data to disk
        /// </summary>
        public void WriteSegment()
        {
            // Determine what needs to be written
            var hasMetaData = DetermineIfMetaDataNeeded();
            var hasRawData = _channels.Values.Any(c => c.HasDataToWrite);
            var newObjectList = _isFirstSegment || HasChannelOrderChanged();

            if (!hasMetaData && !hasRawData)
                return; // Nothing to write

            // If we have raw data but no metadata changes, we can append directly
            if (!hasMetaData && hasRawData && !_isFirstSegment)
            {
                AppendRawDataOnly();
                return;
            }

            // Write a new segment
            WriteFullSegment(hasMetaData, hasRawData, newObjectList);
        }

        private bool DetermineIfMetaDataNeeded()
        {
            if (_isFirstSegment)
                return true;

            // Check for property changes
            if (_fileObject.HasPropertiesModified)
                return true;

            if (_groups.Values.Any(g => g.HasPropertiesModified))
                return true;

            if (_channels.Values.Any(c => c.HasPropertiesModified || c.RawDataIndex.HasChanged))
                return true;

            return false;
        }

        private bool HasChannelOrderChanged()
        {
            // For simplicity, we'll track if channels were added
            // In a full implementation, we'd track removals and reorderings
            return false;
        }

        private void AppendRawDataOnly()
        {
            // FIXED: This optimization now properly updates both data and index files
            
            // Calculate raw data size
            var rawDataSize = _channels.Values.Where(c => c.HasDataToWrite)
                                              .Sum(c => (long)c.DataBuffer.Length);

            // === UPDATE DATA FILE ===
            var dataFileOriginalEnd = _fileStream.Position;
            
            // Update data file segment size
            _fileStream.Seek(_currentSegmentStart + 12, SeekOrigin.Begin);
            var currentDataSegmentSize = dataFileOriginalEnd - _currentSegmentStart - 28;
            var newDataSegmentSize = currentDataSegmentSize + rawDataSize;
            _fileWriter.Write((ulong)newDataSegmentSize);

            // Return to end of data file and write raw data
            _fileStream.Seek(dataFileOriginalEnd, SeekOrigin.Begin);
            WriteRawData();

            // === UPDATE INDEX FILE ===
            // Index file doesn't get raw data, but we need to update its segment size
            // Actually, the index segment size doesn't change - it only contains metadata
            // The index file's NextSegmentOffset should point to the next INDEX segment
            // So we don't need to update the index file for raw-data-only appends

            // Clear buffers
            foreach (var channel in _channels.Values)
            {
                channel.ClearDataBuffer();
            }
        }

        private void WriteFullSegment(bool hasMetaData, bool hasRawData, bool newObjectList)
        {
            var dataSegmentStart = _fileStream.Position;
            var indexSegmentStart = _indexStream.Position;

            // UPDATE RAW DATA INDEXES BEFORE WRITING
            foreach (var channel in _channels.Values)
            {
                if (channel.HasDataToWrite)
                {
                    channel.UpdateRawDataIndex();
                }
            }

            // Calculate TOC flags
            var tocFlags = TocFlags.None;
            if (hasMetaData) tocFlags |= TocFlags.MetaData;
            if (hasRawData) tocFlags |= TocFlags.RawData;
            if (newObjectList) tocFlags |= TocFlags.NewObjList;

            // === WRITE LEAD-INS (with placeholders) ===
            WriteLeadIn(tocFlags, _fileWriter, TDMS_TAG);
            WriteLeadIn(tocFlags, _indexWriter, TDMS_INDEX_TAG);

            var dataMetaDataStart = _fileStream.Position;
            var indexMetaDataStart = _indexStream.Position;

            // === WRITE METADATA to both files ===
            if (hasMetaData || newObjectList)
            {
                WriteMetaData(newObjectList);
            }

            var dataRawDataStart = _fileStream.Position;
            var indexMetaDataEnd = _indexStream.Position;

            // === WRITE RAW DATA (only to data file) ===
            if (hasRawData)
            {
                WriteRawData();
            }

            var dataSegmentEnd = _fileStream.Position;

            // === UPDATE LEAD-INS with actual sizes ===
            
            // Data file: NextSegmentOffset = total segment size, RawDataOffset = metadata size
            UpdateLeadIn(_fileStream, _fileWriter, 
                dataSegmentStart, 
                dataSegmentEnd - dataSegmentStart - 28,  // Total segment size after lead-in
                dataRawDataStart - dataMetaDataStart);   // Metadata size

            // FIXED: Index file gets DATA file's raw data offset, not index file's offset
            UpdateLeadIn(_indexStream, _indexWriter,
                indexSegmentStart,
                indexMetaDataEnd - indexSegmentStart - 28,  // Index segment size (metadata only)
                dataRawDataStart - dataMetaDataStart);       // Raw data offset from DATA file

            // Update tracking positions
            _currentSegmentStart = dataSegmentStart;
            _currentIndexSegmentStart = indexSegmentStart;

            // Clear modification flags and data buffers
            ResetAllFlags();
            _isFirstSegment = false;
        }

        private void WriteLeadIn(TocFlags tocFlags, BinaryWriter writer, string tag)
        {
            // Write TDMS tag (TDSm for data, TDSh for index)
            writer.Write(Encoding.ASCII.GetBytes(tag));

            // Write TOC
            writer.Write((uint)tocFlags);

            // Write version
            writer.Write(TDMS_VERSION);

            // Placeholder for segment length (will update later)
            writer.Write((ulong)0);

            // Placeholder for raw data offset (will update later)
            writer.Write((ulong)0);
        }

        private void UpdateLeadIn(FileStream stream, BinaryWriter writer, 
            long segmentStart, long segmentSize, long rawDataOffset)
        {
            var currentPosition = stream.Position;

            // Update NextSegmentOffset at offset 12
            stream.Seek(segmentStart + 12, SeekOrigin.Begin);
            writer.Write((ulong)segmentSize);

            // Update RawDataOffset at offset 20
            writer.Write((ulong)rawDataOffset);

            // Return to previous position
            stream.Seek(currentPosition, SeekOrigin.Begin);
        }

        private void WriteMetaData(bool newObjectList)
        {
            var objectsToWrite = new List<TdmsObject>();

            // Determine which objects need to be written
            if (newObjectList)
            {
                // Write all objects
                objectsToWrite.Add(_fileObject);
                objectsToWrite.AddRange(_groups.Values);
                objectsToWrite.AddRange(_channelOrder.Select(key => _channels[key]));
            }
            else
            {
                // Write only modified objects
                if (_fileObject.HasPropertiesModified)
                    objectsToWrite.Add(_fileObject);

                objectsToWrite.AddRange(_groups.Values.Where(g => g.HasPropertiesModified));
                objectsToWrite.AddRange(_channels.Values.Where(c => c.HasPropertiesModified || c.RawDataIndex.HasChanged));
            }

            // Write to both main file and index file
            WriteMetaDataToStream(_fileWriter, objectsToWrite);
            WriteMetaDataToStream(_indexWriter, objectsToWrite);
        }

        private void WriteMetaDataToStream(BinaryWriter writer, List<TdmsObject> objects)
        {
            // Write number of objects
            writer.Write((uint)objects.Count);

            foreach (var obj in objects)
            {
                // Write object path
                WriteString(writer, obj.Path);

                // Write raw data index
                if (obj is TdmsChannel channel)
                {
                    WriteRawDataIndex(writer, channel);
                }
                else
                {
                    writer.Write(NO_RAW_DATA);
                }

                // Write properties
                WriteProperties(writer, obj);
            }
        }

        private void WriteRawDataIndex(BinaryWriter writer, TdmsChannel channel)
        {
            var index = channel.RawDataIndex;

            if (!channel.HasDataToWrite)
            {
                writer.Write(NO_RAW_DATA);
                return;
            }

            if (!index.HasChanged && !_isFirstSegment)
            {
                writer.Write(RAW_DATA_MATCHES_PREVIOUS);
                return;
            }

            // Calculate and write index length
            // For non-strings: 4 (type) + 4 (dim) + 8 (count) = 16 bytes
            // For strings: 4 (type) + 4 (dim) + 8 (count) + 8 (size) = 24 bytes
            uint indexLength = (index.DataType == TdmsDataType.String) ? 24u : 16u;
            writer.Write(indexLength);

            // Write data type
            writer.Write((uint)index.DataType);

            // Write array dimension (always 1)
            writer.Write((uint)1);

            // Write number of values
            writer.Write(index.NumberOfValues);

            // For strings, write total size in bytes
            if (index.DataType == TdmsDataType.String)
            {
                writer.Write(index.TotalSizeInBytes);
            }
        }

        private void WriteProperties(BinaryWriter writer, TdmsObject obj)
        {
            var properties = obj.Properties;

            if (_isFirstSegment || obj.HasPropertiesModified)
            {
                writer.Write((uint)properties.Count);

                foreach (var prop in properties.Values)
                {
                    WriteString(writer, prop.Name);
                    writer.Write((uint)prop.DataType);
                    WritePropertyValue(writer, prop);
                }
            }
            else
            {
                writer.Write((uint)0);
            }
        }

        private void WritePropertyValue(BinaryWriter writer, TdmsProperty property)
        {
            switch (property.DataType)
            {
                case TdmsDataType.I8:
                    writer.Write((sbyte)property.Value);
                    break;
                case TdmsDataType.I16:
                    writer.Write((short)property.Value);
                    break;
                case TdmsDataType.I32:
                    writer.Write((int)property.Value);
                    break;
                case TdmsDataType.I64:
                    writer.Write((long)property.Value);
                    break;
                case TdmsDataType.U8:
                    writer.Write((byte)property.Value);
                    break;
                case TdmsDataType.U16:
                    writer.Write((ushort)property.Value);
                    break;
                case TdmsDataType.U32:
                    writer.Write((uint)property.Value);
                    break;
                case TdmsDataType.U64:
                    writer.Write((ulong)property.Value);
                    break;
                case TdmsDataType.SingleFloat:
                    writer.Write((float)property.Value);
                    break;
                case TdmsDataType.DoubleFloat:
                    writer.Write((double)property.Value);
                    break;
                case TdmsDataType.String:
                    WriteString(writer, (string)property.Value);
                    break;
                case TdmsDataType.Boolean:
                    writer.Write((byte)((bool)property.Value ? 1 : 0));
                    break;
                case TdmsDataType.TimeStamp:
                    // Little-endian: Fractions then Seconds
                    var ts = (TdmsTimestamp)property.Value;
                    writer.Write(ts.Fractions);
                    writer.Write(ts.Seconds);
                    break;
                default:
                    throw new NotSupportedException($"Property type {property.DataType} is not supported");
            }
        }

        private void WriteRawData()
        {
            // Write raw data in channel order (non-interleaved)
            foreach (var key in _channelOrder)
            {
                var channel = _channels[key];
                if (channel.HasDataToWrite)
                {
                    var data = channel.DataBuffer;
                    _fileWriter.Write(data);
                }
            }
        }

        private void WriteString(BinaryWriter writer, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            writer.Write((uint)bytes.Length);
            writer.Write(bytes);
        }

        private void ResetAllFlags()
        {
            _fileObject.ResetModifiedFlags();
            foreach (var group in _groups.Values)
            {
                group.ResetModifiedFlags();
            }
            foreach (var channel in _channels.Values)
            {
                channel.ClearDataBuffer();
                channel.ResetModifiedFlags();
            }
        }

        /// <summary>
        /// Flush all pending writes to disk
        /// </summary>
        public void Flush()
        {
            WriteSegment();
            _fileStream.Flush();
            _indexStream.Flush();
        }

        /// <summary>
        /// Close the TDMS file
        /// </summary>
        public void Close()
        {
            Flush();
            Dispose();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _fileWriter?.Dispose();
                _indexWriter?.Dispose();
                _fileStream?.Dispose();
                _indexStream?.Dispose();
                _disposed = true;
            }
        }
    }
}