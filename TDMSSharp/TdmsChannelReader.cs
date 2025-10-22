// TdmsChannelReader.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace TdmsSharp
{
    /// <summary>
    /// Represents a channel within a TDMS file, providing access to its metadata and raw data.
    /// </summary>
    public class TdmsChannelReader
    {
        private readonly TdmsReader _fileReader;

        public string Name { get; }
        public TdmsDataType DataType { get; internal set; }
        public List<RawDataIndexInfo> DataIndices { get; } = new List<RawDataIndexInfo>();

        internal TdmsChannelReader(string name, TdmsDataType dataType, TdmsReader fileReader)
        {
            Name = name;
            DataType = dataType;
            _fileReader = fileReader;
        }

        /// <summary>
        /// Gets the total number of values in this channel across all segments.
        /// </summary>
        public long GetTotalValueCount()
        {
            return (long)DataIndices.Sum(idx => (decimal)idx.NumberOfValues * idx.Segment.ChunkCount);
        }

        /// <summary>
        /// Reads ALL data from the channel into memory. For large files, consider using StreamData or ReadDataChunk instead.
        /// </summary>
        public T[] ReadData<T>() where T : unmanaged
        {
            if (DataType == TdmsDataType.Void)
                return Array.Empty<T>();
            if (DataType == TdmsDataType.Boolean)
                throw new InvalidOperationException("Use ReadBoolData() for boolean channels as bool is not 'unmanaged' in all contexts.");
            if (TdmsDataTypeSizeHelper.GetSize(DataType) == 0)
                throw new InvalidOperationException($"Use a specific read method (e.g., ReadStringData) for variable-size data type {DataType}.");

            return _fileReader.ReadChannelData<T>(this);
        }

        /// <summary>
        /// Reads a chunk of data from the channel starting at the specified index.
        /// </summary>
        public T[] ReadDataChunk<T>(long startIndex, int count) where T : unmanaged
        {
            if (DataType == TdmsDataType.Void)
                return Array.Empty<T>();
            if (DataType == TdmsDataType.Boolean)
                throw new InvalidOperationException("Use ReadBoolDataChunk() for boolean channels.");
            if (TdmsDataTypeSizeHelper.GetSize(DataType) == 0)
                throw new InvalidOperationException($"Use ReadStringDataChunk for variable-size data type {DataType}.");

            return _fileReader.ReadChannelDataChunk<T>(this, startIndex, count);
        }

        /// <summary>
        /// Streams data from the channel in chunks, avoiding loading everything into memory at once.
        /// </summary>
        public IEnumerable<T[]> StreamData<T>(int chunkSize = 10000) where T : unmanaged
        {
            long totalValues = GetTotalValueCount();
            long position = 0;

            while (position < totalValues)
            {
                int readCount = (int)Math.Min(chunkSize, totalValues - position);
                yield return ReadDataChunk<T>(position, readCount);
                position += readCount;
            }
        }

        /// <summary>
        /// Reads ALL boolean data from the channel into memory. For large files, consider using StreamBoolData instead.
        /// </summary>
        public bool[] ReadBoolData()
        {
            if (DataType != TdmsDataType.Boolean)
                throw new InvalidOperationException($"Cannot read boolean data from a channel of type {DataType}.");
            var bytes = _fileReader.ReadChannelData<byte>(this);
            return bytes.Select(b => b != 0).ToArray();
        }

        /// <summary>
        /// Reads a chunk of boolean data from the channel starting at the specified index.
        /// </summary>
        public bool[] ReadBoolDataChunk(long startIndex, int count)
        {
            if (DataType != TdmsDataType.Boolean)
                throw new InvalidOperationException($"Cannot read boolean data from a channel of type {DataType}.");
            var bytes = _fileReader.ReadChannelDataChunk<byte>(this, startIndex, count);
            return bytes.Select(b => b != 0).ToArray();
        }

        /// <summary>
        /// Streams boolean data from the channel in chunks.
        /// </summary>
        public IEnumerable<bool[]> StreamBoolData(int chunkSize = 10000)
        {
            long totalValues = GetTotalValueCount();
            long position = 0;

            while (position < totalValues)
            {
                int readCount = (int)Math.Min(chunkSize, totalValues - position);
                yield return ReadBoolDataChunk(position, readCount);
                position += readCount;
            }
        }

        /// <summary>
        /// Reads ALL string data from the channel into memory. For large files, consider using StreamStringData instead.
        /// </summary>
        public string[] ReadStringData()
        {
            if (DataType != TdmsDataType.String)
                throw new InvalidOperationException($"Cannot read string data from a channel of type {DataType}.");
            return _fileReader.ReadStringChannelData(this);
        }

        /// <summary>
        /// Reads a chunk of string data from the channel starting at the specified index.
        /// </summary>
        public string[] ReadStringDataChunk(long startIndex, int count)
        {
            if (DataType != TdmsDataType.String)
                throw new InvalidOperationException($"Cannot read string data from a channel of type {DataType}.");
            return _fileReader.ReadStringChannelDataChunk(this, startIndex, count);
        }

        /// <summary>
        /// Streams string data from the channel in chunks.
        /// </summary>
        public IEnumerable<string[]> StreamStringData(int chunkSize = 1000)
        {
            long totalValues = GetTotalValueCount();
            long position = 0;

            while (position < totalValues)
            {
                int readCount = (int)Math.Min(chunkSize, totalValues - position);
                yield return ReadStringDataChunk(position, readCount);
                position += readCount;
            }
        }
    }
}