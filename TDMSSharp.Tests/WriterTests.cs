using System.IO;
using Xunit;
using System;
using System.Linq;

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

        [Fact]
        public void WriteAndRead_Streaming_StringChannel_ShouldMatch()
        {
            // Arrange
            var file = new TdmsFile();
            var group = file.GetOrAddChannelGroup("Test Group");
            var channel = group.AddChannel<string>("String Channel");
            var data = new string[] { "a", "b", "c" };

            using var stream = new MemoryStream();
            using (var writer = new StreamingTdmsWriter(stream, file))
            {
                writer.WriteFileHeader();
                writer.WriteSegment(new[] { channel }, new object[] { data });
            }

            stream.Position = 0;

            // Act
            var readFile = TdmsFile.Open(stream);
            var readChannel = readFile.GetChannel("/'Test Group'/'String Channel'");
            var readData = readChannel.GetData<string>();

            // Assert
            Assert.Equal(data, readData);
        }

        [Fact]
        public void WriteAndRead_Streaming_MultipleChannels_ShouldMatch()
        {
            // Arrange
            var file = new TdmsFile();
            var group = file.GetOrAddChannelGroup("Test Group");
            var intChannel = group.AddChannel<int>("Int Channel");
            var doubleChannel = group.AddChannel<double>("Double Channel");
            var intData = new int[] { 1, 2, 3 };
            var doubleData = new double[] { 1.0, 2.0, 3.0 };

            using var stream = new MemoryStream();
            using (var writer = new StreamingTdmsWriter(stream, file))
            {
                writer.WriteFileHeader();
                writer.WriteSegment(new TdmsChannel[] { intChannel, doubleChannel }, new object[] { intData, doubleData });
            }

            stream.Position = 0;

            // Act
            var readFile = TdmsFile.Open(stream);
            var readIntChannel = readFile.GetChannel("/'Test Group'/'Int Channel'");
            var readIntData = readIntChannel.GetData<int>();
            var readDoubleChannel = readFile.GetChannel("/'Test Group'/'Double Channel'");
            var readDoubleData = readDoubleChannel.GetData<double>();

            // Assert
            Assert.Equal(intData, readIntData);
            Assert.Equal(doubleData, readDoubleData);
        }

        [Fact]
        public void WriteAndRead_Streaming_MultipleSegments_ShouldMatch()
        {
            // Arrange
            var file = new TdmsFile();
            var group = file.GetOrAddChannelGroup("Test Group");
            var channel = group.AddChannel<int>("Test Channel");
            var data1 = new int[] { 1, 2, 3 };
            var data2 = new int[] { 4, 5, 6 };

            using var stream = new MemoryStream();
            using (var writer = new StreamingTdmsWriter(stream, file))
            {
                writer.WriteFileHeader();
                writer.WriteSegment(new[] { channel }, new object[] { data1 });
                writer.WriteSegment(new[] { channel }, new object[] { data2 });
            }

            stream.Position = 0;

            // Act
            var readFile = TdmsFile.Open(stream);
            var readChannel = readFile.GetChannel("/'Test Group'/'Test Channel'");
            var readData = readChannel.GetData<int>();

            // Assert
            Assert.Equal(data1.Concat(data2), readData);
        }

        [Theory]
        [InlineData(new byte[] { 1, 2, 3 })]
        [InlineData(new short[] { 1, 2, 3 })]
        [InlineData(new int[] { 1, 2, 3 })]
        [InlineData(new long[] { 1, 2, 3 })]
        [InlineData(new ushort[] { 1, 2, 3 })]
        [InlineData(new uint[] { 1, 2, 3 })]
        [InlineData(new ulong[] { 1, 2, 3 })]
        [InlineData(new float[] { 1.0f, 2.0f, 3.0f })]
        [InlineData(new double[] { 1.0, 2.0, 3.0 })]
        public void WriteAndRead_Streaming_NumericTypes_ShouldMatch<T>(T[] data)
        {
            // Arrange
            var file = new TdmsFile();
            var group = file.GetOrAddChannelGroup("Test Group");
            var channel = group.AddChannel<T>("Test Channel");

            using var stream = new MemoryStream();
            using (var writer = new StreamingTdmsWriter(stream, file))
            {
                writer.WriteFileHeader();
                writer.WriteSegment(new[] { channel }, new object[] { data });
            }

            stream.Position = 0;

            // Act
            var readFile = TdmsFile.Open(stream);
            var readChannel = readFile.GetChannel("/'Test Group'/'Test Channel'");
            var readData = readChannel.GetData<T>();

            // Assert
            Assert.Equal(data, readData);
        }

        [Fact]
        public void WriteAndRead_Streaming_BoolChannel_ShouldMatch()
        {
            // Arrange
            var file = new TdmsFile();
            var group = file.GetOrAddChannelGroup("Test Group");
            var channel = group.AddChannel<bool>("Bool Channel");
            var data = new bool[] { true, false, true };

            using var stream = new MemoryStream();
            using (var writer = new StreamingTdmsWriter(stream, file))
            {
                writer.WriteFileHeader();
                writer.WriteSegment(new[] { channel }, new object[] { data });
            }

            stream.Position = 0;

            // Act
            var readFile = TdmsFile.Open(stream);
            var readChannel = readFile.GetChannel("/'Test Group'/'Bool Channel'");
            var readData = readChannel.GetData<bool>();

            // Assert
            Assert.Equal(data, readData);
        }

        [Fact]
        public void WriteAndRead_Streaming_DateTimeChannel_ShouldMatch()
        {
            // Arrange
            var file = new TdmsFile();
            var group = file.GetOrAddChannelGroup("Test Group");
            var channel = group.AddChannel<DateTime>("DateTime Channel");
            var data = new DateTime[] { DateTime.Now, DateTime.UtcNow, new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc) };

            using var stream = new MemoryStream();
            using (var writer = new StreamingTdmsWriter(stream, file))
            {
                writer.WriteFileHeader();
                writer.WriteSegment(new[] { channel }, new object[] { data });
            }

            stream.Position = 0;

            // Act
            var readFile = TdmsFile.Open(stream);
            var readChannel = readFile.GetChannel("/'Test Group'/'DateTime Channel'");
            var readData = readChannel.GetData<DateTime>();

            // Assert
            for (int i = 0; i < data.Length; i++)
            {
                Assert.Equal(RoundToMicrosecond(data[i]), RoundToMicrosecond(readData[i]));
            }
        }

        private static DateTime RoundToMicrosecond(DateTime dt)
        {
            return new DateTime((long)Math.Round(dt.Ticks / 10.0) * 10, dt.Kind);
        }
    }
}
