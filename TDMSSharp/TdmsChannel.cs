using System.Collections.Generic;

namespace TDMSSharp
{
    public class TdmsChannel
    {
        public string Path { get; }
        public IList<TdmsProperty> Properties { get; } = new List<TdmsProperty>();
        public TdsDataType DataType { get; set; }
        public ulong NumberOfValues { get; set; }
        public object? Data { get; set; }

        public TdmsChannel(string path)
        {
            Path = path;
        }

        public void AddProperty<T>(string name, T value)
        {
            if (value == null) return;
            var dataType = TdsDataTypeProvider.GetDataType<T>();
            Properties.Add(new TdmsProperty(name, dataType, value));
        }
    }
}
