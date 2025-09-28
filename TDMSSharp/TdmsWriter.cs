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
        public TdmsChannel GetChannel(string groupName, string channelName)
        {
            var key = $"{groupName}/{channelName}";
            return _channels.TryGetValue(key, out var channel) ? channel : null;
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
            // This optimization allows appending raw data without writing new segment headers
            // when metadata hasn't changed
            
            // Save current position
            var originalPosition = _fileStream.Position;
            
            // Go back to update the segment size in the lead-in
            _fileStream.Seek(_currentSegmentStart + 12, SeekOrigin.Begin);
            
            // Calculate new segment size
            var rawDataSize = _channels.Values.Where(c => c.HasDataToWrite)
                                              .Sum(c => (long)c.DataBuffer.Length);
            var currentSegmentSize = _fileStream.Length - _currentSegmentStart - 28;
            var newSegmentSize = currentSegmentSize + rawDataSize;
            
            _fileWriter.Write(newSegmentSize);
            
            // Return to end and write raw data
            _fileStream.Seek(0, SeekOrigin.End);
            WriteRawData();
            
            // Clear buffers
            foreach (var channel in _channels.Values)
            {
                channel.ClearDataBuffer();
            }
        }

        private void WriteFullSegment(bool hasMetaData, bool hasRawData, bool newObjectList)
        {
            _currentSegmentStart = _fileStream.Position;

            // Calculate TOC flags
            var tocFlags = TocFlags.None;
            if (hasMetaData) tocFlags |= TocFlags.MetaData;
            if (hasRawData) tocFlags |= TocFlags.RawData;
            if (newObjectList) tocFlags |= TocFlags.NewObjList;

            // Write lead-in
            var metaDataSize = WriteLeadIn(tocFlags, true);
            var indexMetaDataSize = WriteLeadIn(tocFlags, false);

            // Write metadata
            if (hasMetaData || newObjectList)
            {
                WriteMetaData(newObjectList);
            }

            // Write raw data
            if (hasRawData)
            {
                WriteRawData();
            }

            // Update segment size in lead-in
            UpdateSegmentSize(_fileStream, _fileWriter, _currentSegmentStart, metaDataSize);
            UpdateSegmentSize(_indexStream, _indexWriter, _currentSegmentStart, indexMetaDataSize);

            // Clear modification flags and data buffers
            ResetAllFlags();
            _isFirstSegment = false;
        }

        private long WriteLeadIn(TocFlags tocFlags, bool toMainFile)
        {
            var writer = toMainFile ? _fileWriter : _indexWriter;
            var tag = toMainFile ? TDMS_TAG : TDMS_INDEX_TAG;
            var stream = toMainFile ? _fileStream : _indexStream;

            // Write TDMS tag
            writer.Write(Encoding.ASCII.GetBytes(tag));

            // Write TOC
            writer.Write((uint)tocFlags);

            // Write version
            writer.Write(TDMS_VERSION);

            // Placeholder for segment length (will update later)
            var segmentLengthPosition = stream.Position;
            writer.Write((ulong)0);

            // Placeholder for raw data offset
            var rawDataOffsetPosition = stream.Position;
            writer.Write((ulong)0);

            return rawDataOffsetPosition + 8; // Return position after lead-in
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
            WriteMetaDataToStream(_fileWriter, objectsToWrite, true);
            WriteMetaDataToStream(_indexWriter, objectsToWrite, false);
        }

        private void WriteMetaDataToStream(BinaryWriter writer, List<TdmsObject> objects, bool includeRawDataIndex)
        {
            // Write number of objects
            writer.Write((uint)objects.Count);

            foreach (var obj in objects)
            {
                // Write object path
                WriteString(writer, obj.Path);

                // Write raw data index
                if (obj is TdmsChannel channel && includeRawDataIndex)
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

            // Write index length
            writer.Write((uint)20); // Fixed size for non-string types

            // Write data type
            writer.Write((uint)index.DataType);

            // Write array dimension (always 1)
            writer.Write((uint)1);

            // Write number of values
            writer.Write(index.NumberOfValues);

            // For strings, write total size
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
                    var ts = (TdmsTimestamp)property.Value;
                    writer.Write(ts.Seconds);
                    writer.Write(ts.Fractions);
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

        private void UpdateSegmentSize(FileStream stream, BinaryWriter writer, long segmentStart, long metaDataEnd)
        {
            var currentPosition = stream.Position;
            var segmentSize = currentPosition - segmentStart - 28; // 28 = lead-in size
            var rawDataOffset = metaDataEnd - segmentStart - 28;

            // Update segment length
            stream.Seek(segmentStart + 12, SeekOrigin.Begin);
            writer.Write((ulong)segmentSize);

            // Update raw data offset
            writer.Write((ulong)rawDataOffset);

            // Return to end
            stream.Seek(currentPosition, SeekOrigin.Begin);
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