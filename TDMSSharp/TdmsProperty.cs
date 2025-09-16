namespace TDMSSharp
{
    public class TdmsProperty
    {
        public string Name { get; }
        public TdsDataType DataType { get; }
        public object Value { get; }

        public TdmsProperty(string name, TdsDataType dataType, object value)
        {
            Name = name;
            DataType = dataType;
            Value = value;
        }
    }
}
