using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace TDMSSharp
{
    public class TdmsChannel<T> : TdmsChannel
    {
        private List<T[]> _dataChunks = new List<T[]>();
        private T[]? _combinedData;

        public new T[]? Data
        {
            get
            {
                if (_combinedData != null) return _combinedData;
                if (_dataChunks.Count == 0) return null;
                if (_dataChunks.Count == 1) return _dataChunks[0];

                _combinedData = new T[NumberOfValues];
                long offset = 0;
                foreach (var chunk in _dataChunks)
                {
                    Array.Copy(chunk, 0, _combinedData, offset, chunk.Length);
                    offset += chunk.Length;
                }
                _dataChunks.Clear(); // Free up memory
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
                base.Data = value;
            }
        }

        public TdmsChannel(string path) : base(path)
        {
            DataType = TdsDataTypeProvider.GetDataType<T>();
        }

        public void AddDataChunk(T[] chunk)
        {
            if (chunk == null || chunk.Length == 0) return;
            
            _dataChunks.Add(chunk);
            _combinedData = null; // Invalidate combined data
            base.Data = null;
        }

        public void AppendData(T[] dataToAppend)
        {
            if (dataToAppend == null || dataToAppend.Length == 0) return;

            // Ensure data is combined before appending
            if (_combinedData == null)
            {
                _ = Data; // This triggers the combination
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