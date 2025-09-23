using System.IO;
using Xunit;

namespace TDMSSharp.Tests
{
    public class IncrementalWriterTests
    {
        [Fact]
        public void WriteAndRead_IncrementalFile_ShouldMatch()
        {
            var path = Path.GetTempFileName();
            // var path = "test_incremental.tdms";
            using (var fileStream = new TdmsFileStream(path))
            {
                fileStream.AppendData("group1", "channel1", new[] { 1, 2, 3 });
                fileStream.AppendData("group1", "channel1", new[] { 4, 5, 6 });
                fileStream.AppendValue("group2", "channel2", 3.14);
            }

            var file = TdmsFile.Open(path);

            Assert.Equal(2, file.ChannelGroups.Count);
            var group1 = file.ChannelGroups[0];
            var group2 = file.ChannelGroups[1];
            Assert.Equal("/'group1'", group1.Path);
            Assert.Equal("/'group2'", group2.Path);

            Assert.Single(group1.Channels);
            var channel1 = group1.Channels[0];
            Assert.Equal("/'group1'/'channel1'", channel1.Path);
            Assert.Equal(TdsDataType.I32, channel1.DataType);
            Assert.Equal(6UL, channel1.NumberOfValues);
            var data1 = ((TdmsChannel<int>)channel1).Data;
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, data1);

            Assert.Single(group2.Channels);
            var channel2 = group2.Channels[0];
            Assert.Equal("/'group2'/'channel2'", channel2.Path);
            Assert.Equal(TdsDataType.DoubleFloat, channel2.DataType);
            Assert.Equal(1UL, channel2.NumberOfValues);
            var data2 = ((TdmsChannel<double>)channel2).Data;
            Assert.Equal(new[] { 3.14 }, data2);
        }

        [Fact]
        public void WriteAndRead_IncrementalFile_WithProperties_ShouldMatch()
        {
            var path = Path.GetTempFileName();
            using (var fileStream = new TdmsFileStream(path))
            {
                fileStream.AddFileProperty("File_Property", "File_Value");
                fileStream.AppendData("group1", "channel1", new[] { 1, 2, 3 });
                fileStream.AddGroupProperty("group1", "Group_Property", "Group_Value");
                fileStream.AddChannelProperty("group1", "channel1", "Channel_Property", "Channel_Value");
                fileStream.AppendData("group1", "channel1", new[] { 4, 5, 6 });
            }

            var file = TdmsFile.Open(path);

            Assert.Single(file.Properties);
            Assert.Equal("File_Property", file.Properties[0].Name);
            Assert.Equal("File_Value", file.Properties[0].Value);

            Assert.Single(file.ChannelGroups);
            var group1 = file.ChannelGroups[0];
            Assert.Single(group1.Properties);
            Assert.Equal("Group_Property", group1.Properties[0].Name);
            Assert.Equal("Group_Value", group1.Properties[0].Value);

            Assert.Single(group1.Channels);
            var channel1 = group1.Channels[0];
            Assert.Single(channel1.Properties);
            Assert.Equal("Channel_Property", channel1.Properties[0].Name);
            Assert.Equal("Channel_Value", channel1.Properties[0].Value);
        }
    }
}
