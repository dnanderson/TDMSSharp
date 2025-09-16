using System.IO;
using Xunit;

namespace TDMSSharp.Tests
{
    public class WriterTests
    {
        [Fact]
        public void WriteAndRead_SimpleFile_MetadataShouldMatch()
        {
            // Arrange
            var file = new TdmsFile();
            file.AddProperty("Author", "Jules");
            var group = file.GetOrAddChannelGroup("Test Group");
            var channel = group.AddChannel<int>("Test Channel");
            channel.AddProperty("Unit", "m/s");
            var data = new int[] { 1, 2, 3, 4, 5 };
            channel.AppendData(data);

            // Act
            using var stream = new MemoryStream();
            file.Save(stream); // Using the public API Save method which now needs to support streams

            stream.Position = 0;

            var readFile = TdmsFile.Open(stream); // Using the public API Open method

            // Assert
            Assert.NotNull(readFile);
            Assert.Single(readFile.Properties);
            Assert.Equal("Jules", (string)readFile.Properties[0].Value);

            Assert.Single(readFile.ChannelGroups);
            var readGroup = readFile.ChannelGroups[0];
            Assert.Equal("/'Test Group'", readGroup.Path);

            Assert.Single(readGroup.Channels);
            var readChannel = readGroup.Channels[0];
            Assert.Equal("/'Test Group'/'Test Channel'", readChannel.Path);
            Assert.Equal(TdsDataType.I32, readChannel.DataType);
            Assert.Equal((ulong)data.Length, readChannel.NumberOfValues);

            Assert.Single(readChannel.Properties);
            Assert.Equal("Unit", readChannel.Properties[0].Name);
            Assert.Equal("m/s", (string)readChannel.Properties[0].Value);
        }
    }
}
