using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace TDMSSharp
{
    /// <summary>
    /// Represents a TDMS channel with a specific data type.
    /// </summary>
    /// <typeparam name="T">The data type of the channel.</typeparam>
    public class TdmsChannel<T> : TdmsChannel
    {
        private List<T[]> _dataChunks = new List<T[]>();
        private T[]? _combinedData;

        /// <summary>
        /// Gets or sets the data for this channel.
        /// </summary>
        public new T[]? Data
        {
            get
            {
                if (_combinedData == null && _dataChunks.Count > 0)
                {
                    CombineChunks();
                }
                return _combinedData;
            }
            set
            {
                _combinedData = value;
                _dataChunks.Clear();
                if (value != null)
                {
                    _dataChunks.Add(value);
                    NumberOfValues = (ulong)value.Length;
                }
                else
                {
                    NumberOfValues = 0;
                }
                base.Data = value; // Update base class
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TdmsChannel{T}"/> class.
        /// </summary>
        /// <param name="path">The path of the channel.</param>
        public TdmsChannel(string path) : base(path)
        {
            DataType = TdsDataTypeProvider.GetDataType<T>();
        }

        /// <summary>
        /// Adds a chunk of data to the channel. This is efficient for large datasets that are read in parts.
        /// </summary>
        /// <param name="chunk">The data chunk to add.</param>
        public void AddDataChunk(T[] chunk)
        {
            if (chunk == null || chunk.Length == 0) return;

            _dataChunks.Add(chunk);
            _combinedData = null; // Mark as needing combination
        }

        private void CombineChunks()
        {
            if (_dataChunks.Count == 0)
            {
                _combinedData = null;
                base.Data = null;
                return;
            }
            
            if (_dataChunks.Count == 1)
            {
                _combinedData = _dataChunks[0];
                base.Data = _combinedData;
                return;
            }

            long totalLength = 0;
            foreach (var chunk in _dataChunks)
            {
                totalLength += chunk.Length;
            }
            
            _combinedData = new T[totalLength];
            long offset = 0;
            foreach (var chunk in _dataChunks)
            {
                Array.Copy(chunk, 0, _combinedData, offset, chunk.Length);
                offset += chunk.Length;
            }
            _dataChunks.Clear();
            _dataChunks.Add(_combinedData);
            base.Data = _combinedData;
        }

        /// <summary>
        /// Appends data to the channel. Note that this can be inefficient if used repeatedly on large datasets.
        /// </summary>
        /// <param name="dataToAppend">The data to append.</param>
        public void AppendData(T[] dataToAppend)
        {
            if (dataToAppend == null || dataToAppend.Length == 0) return;

            if (_combinedData == null && _dataChunks.Count > 0)
            {
                CombineChunks();
            }

            if (_combinedData == null)
            {
                Data = dataToAppend;
            }
            else
            {
                var newArray = new T[_combinedData.Length + dataToAppend.Length];
                Array.Copy(_combinedData, newArray, _combinedData.Length);
                Array.Copy(dataToAppend, 0, newArray, _combinedData.Length, dataToAppend.Length);
                Data = newArray;
            }
        }
    }
}