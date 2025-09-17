namespace TDMSSharp
{
    public class TdmsChannel<T> : TdmsChannel
    {
        public new T[] Data
        {
            get => (T[])base.Data;
            set => base.Data = value;
        }

        public TdmsChannel(string path) : base(path)
        {
            DataType = TdsDataTypeProvider.GetDataType<T>();
        }

        public void AppendData(T[] dataToAppend)
        {
            if (Data == null)
            {
                Data = dataToAppend;
            }
            else
            {
                var newData = new T[Data.Length + dataToAppend.Length];
                Data.CopyTo(newData, 0);
                dataToAppend.CopyTo(newData, Data.Length);
                Data = newData;
            }
            NumberOfValues = (ulong)Data.Length;
        }
    }
}
