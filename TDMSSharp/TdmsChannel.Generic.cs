using System;
using System.Buffers;

namespace TDMSSharp
{
    public class TdmsChannel<T> : TdmsChannel
    {
        private T[]? _data;
        private const int GrowthFactor = 2;
        private const int MinimumGrowth = 4;

        public new T[]? Data
        {
            get => _data;
            set
            {
                _data = value;
                base.Data = value;
            }
        }

        public TdmsChannel(string path) : base(path)
        {
            DataType = TdsDataTypeProvider.GetDataType<T>();
        }

        public void AppendData(T[] dataToAppend)
        {
            if (dataToAppend == null || dataToAppend.Length == 0)
                return;

            if (_data == null)
            {
                _data = dataToAppend;
                base.Data = _data;
            }
            else
            {
                // Use optimized array growth strategy
                var currentLength = _data.Length;
                var newLength = currentLength + dataToAppend.Length;
                
                // Rent from ArrayPool if beneficial for large arrays
                if (newLength > 1024 && typeof(T).IsPrimitive)
                {
                    var pooledArray = ArrayPool<T>.Shared.Rent(newLength);
                    Array.Copy(_data, 0, pooledArray, 0, currentLength);
                    Array.Copy(dataToAppend, 0, pooledArray, currentLength, dataToAppend.Length);
                    
                    // Return old array to pool if it was rented
                    if (currentLength > 1024)
                    {
                        ArrayPool<T>.Shared.Return(_data, clearArray: false);
                    }
                    
                    _data = new T[newLength];
                    Array.Copy(pooledArray, 0, _data, 0, newLength);
                    ArrayPool<T>.Shared.Return(pooledArray, clearArray: false);
                }
                else
                {
                    var newData = new T[newLength];
                    Array.Copy(_data, 0, newData, 0, currentLength);
                    Array.Copy(dataToAppend, 0, newData, currentLength, dataToAppend.Length);
                    _data = newData;
                }
                
                base.Data = _data;
            }
            
            NumberOfValues = (ulong)_data.Length;
        }

        /// <summary>
        /// Optimized append for single values
        /// </summary>
        public void AppendValue(T value)
        {
            if (_data == null)
            {
                _data = new T[1] { value };
                base.Data = _data;
            }
            else
            {
                var currentLength = _data.Length;
                var newData = new T[currentLength + 1];
                Array.Copy(_data, 0, newData, 0, currentLength);
                newData[currentLength] = value;
                _data = newData;
                base.Data = _data;
            }
            
            NumberOfValues = (ulong)_data.Length;
        }

        /// <summary>
        /// Pre-allocate capacity for better performance when size is known
        /// </summary>
        public void SetCapacity(int capacity)
        {
            if (_data == null)
            {
                _data = new T[capacity];
                base.Data = _data;
            }
            else if (_data.Length < capacity)
            {
                var newData = new T[capacity];
                Array.Copy(_data, newData, _data.Length);
                _data = newData;
                base.Data = _data;
            }
        }
    }
}