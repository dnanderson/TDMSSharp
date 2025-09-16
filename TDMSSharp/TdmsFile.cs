using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TDMSSharp
{
    public class TdmsFile
    {
        public IList<TdmsProperty> Properties { get; } = new List<TdmsProperty>();
        public IList<TdmsChannelGroup> ChannelGroups { get; } = new List<TdmsChannelGroup>();

        public TdmsFile()
        {
        }

        public TdmsChannelGroup GetOrAddChannelGroup(string name)
        {
            var path = $"/'{name.Replace("'", "''")}'";
            var group = ChannelGroups.FirstOrDefault(g => g.Path == path);
            if (group == null)
            {
                group = new TdmsChannelGroup(path);
                ChannelGroups.Add(group);
            }
            return group;
        }

        public void AddProperty<T>(string name, T value)
        {
            var dataType = TdsDataTypeProvider.GetDataType<T>();
            Properties.Add(new TdmsProperty(name, dataType, value));
        }

        public void Save(string path)
        {
            using (var stream = File.Create(path))
            {
                Save(stream);
            }
        }

        public void Save(Stream stream)
        {
            var writer = new TdmsWriter(stream);
            writer.WriteFile(this);
        }

        public static TdmsFile Open(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                return Open(stream);
            }
        }

        public static TdmsFile Open(Stream stream)
        {
            var reader = new TdmsReader(stream);
            return reader.ReadFile();
        }
    }
}
