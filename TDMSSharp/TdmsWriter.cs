using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.IO;


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
        private const ulong INCOMPLETE_SEGMENT = 0xFFFFFFFFFFFFFFFF;

        private readonly FileStream _fileStream;
        private readonly FileStream _indexStream;
        private readonly BinaryWriter _fileWriter;
        private readonly BinaryWriter _indexWriter;

        private readonly TdmsFile _fileObject;
        private readonly Dictionary<string, TdmsGroup> _groups = new();
        private readonly Dictionary<string, TdmsChannel> _channels = new();
        private readonly List<string> _channelOrder = new();
        private List<string> _lastSegmentActiveChannelOrder = new();

        // Add a RecyclableMemoryStreamManager

        private readonly RecyclableMemoryStreamManager _memoryStreamManager;


        private bool _isFirstSegment = true;
        private long _currentDataSegmentStart = 0;
        private long _currentIndexSegmentStart = 0; // <-- FIX: Track index segment start
        private bool _disposed = false;

        public TdmsFileWriter(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            _fileObject = new TdmsFile();

            // 1. Define the buffer and pool size settings.
            // BlockSize: This is the size of the small memory blocks that the manager pools.
            // When a stream needs memory, it gets it in chunks of this size. A good starting point
            // is a value slightly larger than your most common small write operations. 
            // 1024 bytes (1 KB) is a common default.
            int blockSize = 1024; // 1 KB blocks
            // LargeBufferMultiple: This controls the size of buffers in the large pool.
            // Each buffer will be a multiple of this value. This should be set to a value 
            // that makes sense for your larger data chunks. For example, if you often deal 
            // with data in megabyte chunks, setting this to 1024 * 1024 (1 MB) is a good idea.
            int largeBufferMultiple = 1024 * 1024; // 1 MB increments
            // MaximumBufferSize: This sets an upper limit on the size of a single buffer that 
            // will be pooled. Any request for a buffer larger than this will be allocated directly 
            // from the system and will not be pooled. This prevents the pool from holding onto 
            // excessively large arrays for rare, outlier events.
            int maxBufferSize = 1024 * 1024 * 16; // 16 MB max buffer size

            // MaximumFreeSmallPoolBytes: This is the total amount of memory (in bytes) that the manager will keep in the small block pool.
            long maxSmallPoolBytes = 1024 * 1024 * 20;  // 20 MB total for small pool
            // MaximumFreeLargePoolBytes: This is the total amount of memory (in bytes) that the manager will keep in the large buffer pool.
            long maxLargePoolBytes = 1024 * 1024 * 100; // 100 MB total for large pool

            // 2. Create the Options object with all parameters.
            var streamManagerOptions = new RecyclableMemoryStreamManager.Options()
            {
                BlockSize = blockSize,
                LargeBufferMultiple = largeBufferMultiple,
                MaximumBufferSize = maxBufferSize,
                MaximumSmallPoolFreeBytes = maxSmallPoolBytes,
                MaximumLargePoolFreeBytes = maxLargePoolBytes
            };

            // 3. Create the manager with the configured Options object.
            _memoryStreamManager = new RecyclableMemoryStreamManager(streamManagerOptions);

            _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, false);
            _indexStream = new FileStream(filePath + "_index", FileMode.Create, FileAccess.Write, FileShare.Read, 4096, false);

            _fileWriter = new BinaryWriter(_fileStream, Encoding.UTF8, true);
            _indexWriter = new BinaryWriter(_indexStream, Encoding.UTF8, true);
        }

        /// <summary>
        /// Set a property on the file object
        /// </summary>
        public void SetFileProperty<T>(string name, T value) where T : notnull
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

            var channel = new TdmsChannel(groupName, channelName, dataType, _memoryStreamManager);

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
        /// Writes a segment of data for multiple channels in a single, optimized operation.
        /// This is the recommended method for high-performance writing.
        /// </summary>
        /// <param name="channelData">An enumerable of ChannelData objects, each containing the data for a specific channel for this segment.</param>
        public void WriteSegment(IEnumerable<ChannelData> channelData)
        {
            // 1. Write all provided data to the respective channel buffers.
            foreach (var data in channelData)
            {
                data.WriteToChannelBuffer();
            }

            // 2. Call the original WriteSegment to write all buffered data to disk.
            WriteSegment();
        }

        /// <summary>
        /// Write all buffered data to disk
        /// </summary>
        public void WriteSegment()
        {
            // Update per-channel raw data index change flags before deciding whether
            // metadata can be omitted for this segment.
            foreach (var channel in _channels.Values)
            {
                if (channel.HasDataToWrite)
                {
                    channel.UpdateRawDataIndex();
                }
            }

            var hasRawData = _channels.Values.Any(c => c.HasDataToWrite);
            var activeChannelOrder = GetActiveChannelOrder();
            var newObjectList = _isFirstSegment || (hasRawData && HasChannelOrderChanged(activeChannelOrder));
            var hasStringChannelData = _channels.Values.Any(c => c.HasDataToWrite && c.DataType == TdmsDataType.String);
            var hasMetaData = DetermineIfMetaDataNeeded() || newObjectList || hasStringChannelData;

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

        private bool HasChannelOrderChanged(IReadOnlyList<string> activeChannelOrder)
        {
            if (!_lastSegmentActiveChannelOrder.Any())
                return activeChannelOrder.Any();

            if (_lastSegmentActiveChannelOrder.Count != activeChannelOrder.Count)
                return true;

            for (int i = 0; i < activeChannelOrder.Count; i++)
            {
                if (!string.Equals(_lastSegmentActiveChannelOrder[i], activeChannelOrder[i], StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private void AppendRawDataOnly()
        {
            // This function appends raw data to the CURRENT data segment
            // No new metadata segment is created, but the lead-in of the existing
            // segment must be updated in both files.

            // Calculate raw data size
            var rawDataSize = _channels.Values.Where(c => c.HasDataToWrite)
                                              .Sum(c => (long)c.GetDataBuffer().Length);

            // === UPDATE DATA FILE ===
            var dataFileOriginalEnd = _fileStream.Position;
            var currentSegmentSize = dataFileOriginalEnd - _currentDataSegmentStart - 28;
            var newSegmentSize = (ulong)(currentSegmentSize + rawDataSize);

            // Update data file segment's NextSegmentOffset
            _fileStream.Seek(_currentDataSegmentStart + 12, SeekOrigin.Begin);
            _fileWriter.Write(newSegmentSize);

            // Return to end and write raw data
            _fileStream.Seek(dataFileOriginalEnd, SeekOrigin.Begin);
            WriteRawData();

            // === UPDATE INDEX FILE ===
            // The index file's NextSegmentOffset must be updated to match the data file's
            var indexFileOriginalEnd = _indexStream.Position;
            _indexStream.Seek(_currentIndexSegmentStart + 12, SeekOrigin.Begin);
            _indexWriter.Write(newSegmentSize); // Write the same new size
            _indexStream.Seek(indexFileOriginalEnd, SeekOrigin.Begin);

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

            // Calculate TOC flags (identical for both data and index files)
            var tocFlags = TocFlags.None;
            if (hasMetaData) tocFlags |= TocFlags.MetaData;
            if (hasRawData) tocFlags |= TocFlags.RawData;
            if (newObjectList) tocFlags |= TocFlags.NewObjList;

            // === WRITE LEAD-INS (with INCOMPLETE_SEGMENT placeholders for crash protection) ===
            WriteLeadIn(tocFlags, _fileWriter, TDMS_TAG);
            WriteLeadIn(tocFlags, _indexWriter, TDMS_INDEX_TAG);

            var dataMetaDataStart = _fileStream.Position;
            var indexMetaDataStart = _indexStream.Position;

            // === WRITE METADATA (identical to both files) ===
            if (hasMetaData || newObjectList)
            {
                WriteMetaData(newObjectList);
            }

            var dataRawDataStart = _fileStream.Position;
            var indexMetaDataEnd = _indexStream.Position;

            // Calculate metadata size (must be identical in both files)
            var metadataSize = dataRawDataStart - dataMetaDataStart;
            var indexMetadataSize = indexMetaDataEnd - indexMetaDataStart;

            if (metadataSize != indexMetadataSize)
            {
                throw new InvalidOperationException(
                    $"Metadata size mismatch: data={metadataSize}, index={indexMetadataSize}");
            }

            // === WRITE RAW DATA (only to data file) ===
            var rawDataSize = 0L;
            if (hasRawData)
            {
                var beforeRawData = _fileStream.Position;
                WriteRawData();
                rawDataSize = _fileStream.Position - beforeRawData;
            }

            // Calculate NextSegmentOffset values
            // This is the size of remaining segment data (excluding the 28-byte lead-in)
            // For data file: metadata_size + raw_data_size
            var dataNextSegmentOffset = (ulong)(metadataSize + rawDataSize);

            // === UPDATE LEAD-INS with actual sizes ===
            // Data file lead-in update
            UpdateLeadIn(_fileStream, _fileWriter,
                dataSegmentStart,
                dataNextSegmentOffset,
                (ulong)metadataSize);

            // Index file lead-in update - MUST BE IDENTICAL
            UpdateLeadIn(_indexStream, _indexWriter,
                indexSegmentStart,
                dataNextSegmentOffset, // MUST match data file
                (ulong)metadataSize);

            // Update tracking positions
            _currentDataSegmentStart = dataSegmentStart;
            _currentIndexSegmentStart = indexSegmentStart; // <-- FIX: Update index start position
            if (hasRawData)
            {
                _lastSegmentActiveChannelOrder = GetActiveChannelOrder();
            }

            // Clear modification flags and data buffers
            ResetAllFlags();
            _isFirstSegment = false;
        }

        private void WriteLeadIn(TocFlags tocFlags, BinaryWriter writer, string tag)
        {
            // Write TDMS tag (TDSm for data, TDSh for index)
            writer.Write(Encoding.ASCII.GetBytes(tag));

            // Write TOC (must be identical in both files)
            writer.Write((uint)tocFlags);

            // Write version (must be identical in both files)
            writer.Write(TDMS_VERSION);

            // Write INCOMPLETE_SEGMENT marker as placeholder for crash protection
            writer.Write(INCOMPLETE_SEGMENT);

            // Placeholder for RawDataOffset (will update later)
            writer.Write((ulong)0);
        }

        private void UpdateLeadIn(FileStream stream, BinaryWriter writer,
            long segmentStart, ulong nextSegmentOffset, ulong rawDataOffset)
        {
            var currentPosition = stream.Position;

            // Update NextSegmentOffset at offset 12
            stream.Seek(segmentStart + 12, SeekOrigin.Begin);
            writer.Write(nextSegmentOffset);

            // Update RawDataOffset at offset 20
            writer.Write(rawDataOffset);

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

            // Write IDENTICAL metadata to both main file and index file
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

            uint indexLength = (index.DataType == TdmsDataType.String) ? 24u : 16u;
            writer.Write(indexLength);
            writer.Write((uint)index.DataType);
            writer.Write((uint)1);
            writer.Write(index.NumberOfValues);

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
                    prop.WriteValue(writer);
                }
            }
            else
            {
                writer.Write((uint)0);
            }
        }

        private void WriteRawData()
        {
            // Write raw data in channel order (non-interleaved) - ONLY to data file
            foreach (var key in _channelOrder)
            {
                var channel = _channels[key];
                if (channel.HasDataToWrite)
                {
                    var data = channel.GetDataBuffer();
                    if (!data.IsEmpty)
                    {
                        _fileWriter.Write(data.Span);
                    }
                }
            }
        }

        private List<string> GetActiveChannelOrder()
        {
            return _channelOrder.Where(key => _channels[key].HasDataToWrite).ToList();
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

        public void Flush()
        {
            WriteSegment();
            _fileStream.Flush();
            _indexStream.Flush();
        }

        public void Close()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    Flush();
                }
                finally
                {
                    // Dispose all channels to return their streams to the pool
                    foreach (var channel in _channels.Values)
                    {
                        channel?.Dispose();
                    }
                    _channels.Clear();

                    _fileWriter?.Dispose();
                    _indexWriter?.Dispose();
                    _fileStream?.Dispose();
                    _indexStream?.Dispose();
                    _disposed = true;
                }
            }
        }
    }
}
