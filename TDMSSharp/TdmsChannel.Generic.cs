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

        public TdmsChannel(string path) : base(path)
        {
            DataType = TdsDataTypeProvider.GetDataType<T>();
        }

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