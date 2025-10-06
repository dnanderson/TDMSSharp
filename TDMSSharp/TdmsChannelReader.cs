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

        public bool[] ReadBoolData()
        {
            if (DataType != TdmsDataType.Boolean)
                throw new InvalidOperationException($"Cannot read boolean data from a channel of type {DataType}.");
            var bytes = _fileReader.ReadChannelData<byte>(this);
            return bytes.Select(b => b != 0).ToArray();
        }

        public string[] ReadStringData()
        {
            if (DataType != TdmsDataType.String)
                throw new InvalidOperationException($"Cannot read string data from a channel of type {DataType}.");
            return _fileReader.ReadStringChannelData(this);
        }
    }
}