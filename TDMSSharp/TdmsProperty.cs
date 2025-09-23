namespace TDMSSharp
{
    /// <summary>
    /// Represents a property in a TDMS file, which is a key-value pair with a specific data type.
    /// </summary>
    public class TdmsProperty
    {
        /// <summary>
        /// Gets the name of the property.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the data type of the property's value.
        /// </summary>
        public TdsDataType DataType { get; }

        /// <summary>
        /// Gets the value of the property.
        /// </summary>
        public object Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TdmsProperty"/> class.
        /// </summary>
        /// <param name="name">The name of the property.</param>
        /// <param name="dataType">The data type of the property's value.</param>
        /// <param name="value">The value of the property.</param>
        public TdmsProperty(string name, TdsDataType dataType, object value)
        {
            Name = name;
            DataType = dataType;
            Value = value;
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns><c>true</c> if the specified object is equal to the current object; otherwise, <c>false</c>.</returns>
        public override bool Equals(object obj)
        {
            if (obj is TdmsProperty other)
            {
                return Name == other.Name && DataType == other.DataType && Value.Equals(other.Value);
            }
            return false;
        }

        /// <summary>
        /// Serves as the default hash function.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode() => (Name, DataType, Value).GetHashCode();
    }
}
