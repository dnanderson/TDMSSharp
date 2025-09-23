using System.Collections.Generic;

namespace TDMSSharp
{
    /// <summary>
    /// Represents a channel in a TDMS file, containing data and properties.
    /// </summary>
    public class TdmsChannel
    {
        /// <summary>
        /// Gets the path of the channel.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the list of properties for this channel.
        /// </summary>
        public IList<TdmsProperty> Properties { get; } = new List<TdmsProperty>();

        /// <summary>
        /// Gets or sets the data type of the channel.
        /// </summary>
        public TdsDataType DataType { get; set; }

        /// <summary>
        /// Gets or sets the number of values in the channel.
        /// </summary>
        public ulong NumberOfValues { get; set; }

        /// <summary>
        /// Gets or sets the data of the channel as a non-generic object.
        /// </summary>
        public object? Data { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TdmsChannel"/> class.
        /// </summary>
        /// <param name="path">The path of the channel.</param>
        public TdmsChannel(string path)
        {
            Path = path;
        }

        /// <summary>
        /// Adds a property to the channel.
        /// </summary>
        /// <typeparam name="T">The type of the property value.</typeparam>
        /// <param name="name">The name of the property.</param>
        /// <param name="value">The value of the property.</param>
        public void AddProperty<T>(string name, T value)
        {
            if (value == null) return;
            var dataType = TdsDataTypeProvider.GetDataType<T>();
            Properties.Add(new TdmsProperty(name, dataType, value));
        }

        /// <summary>
        /// Gets the channel's data as a strongly-typed array.
        /// </summary>
        /// <typeparam name="T">The type of the data to retrieve.</typeparam>
        /// <returns>The data as an array of type <typeparamref name="T"/>, or <c>null</c> if the data is not of this type.</returns>
        public T[]? GetData<T>()
        {
            if (this is TdmsChannel<T> typedChannel)
            {
                return typedChannel.Data;
            }
            return null;
        }

        /// <summary>
        /// Gets the channel's data as a non-generic <see cref="Array"/>.
        /// </summary>
        /// <returns>The data as an <see cref="Array"/>, or <c>null</c> if there is no data.</returns>
        public Array? GetDataAsArray()
        {
            return Data as Array;
        }

        /// <summary>
        /// Creates a deep clone of the channel, including its properties, but not its data.
        /// </summary>
        /// <returns>A new <see cref="TdmsChannel"/> instance with the same properties.</returns>
        public TdmsChannel DeepClone()
        {
            var clone = new TdmsChannel(Path)
            {
                DataType = DataType,
                NumberOfValues = NumberOfValues
            };
            foreach (var prop in Properties)
            {
                clone.Properties.Add(new TdmsProperty(prop.Name, prop.DataType, prop.Value));
            }
            return clone;
        }
    }
}