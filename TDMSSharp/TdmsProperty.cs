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

        public override bool Equals(object obj)
        {
            if (obj is TdmsProperty other)
            {
                return Name == other.Name && DataType == other.DataType && Value.Equals(other.Value);
            }
            return false;
        }

        public override int GetHashCode() => (Name, DataType, Value).GetHashCode();
    }
}
