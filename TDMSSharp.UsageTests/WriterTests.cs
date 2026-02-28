// WriterTests.cs
using System.IO;
using TdmsSharp;
using Xunit;
using System.Linq;
using System;

public class WriterTests
{
    private string? _tempPath;

    private TdmsFileWriter CreateWriter(out string path)
    {
        _tempPath = Path.GetTempFileName();
        path = _tempPath;
        return new TdmsFileWriter(path);
    }

    private void Cleanup()
    {
        if (_tempPath != null)
        {
            if (File.Exists(_tempPath)) File.Delete(_tempPath);
            var indexPath = _tempPath + "_index";
            if (File.Exists(indexPath)) File.Delete(indexPath);
            _tempPath = null;
        }
    }

    private void RunTest(System.Action<TdmsFileWriter> writeAction, System.Action<TdmsFileHolder> readAssertAction)
    {
        string path;
        using (var writer = CreateWriter(out path))
        {
            writeAction(writer);
        }

        using (var reader = new TdmsReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)))
        {
            var file = reader.ReadFile();
            readAssertAction(file);
        }
        Cleanup();
    }

    [Fact(Skip = "Reader functionality for properties is not yet implemented")]
    public void TestFileProperties()
    {
        string path;
        using (var writer = CreateWriter(out path))
        {
            writer.SetFileProperty("Author", "Jules");
            writer.SetFileProperty("Date", new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        }

        using (var reader = new TdmsReader(new FileStream(path, FileMode.Open)))
        {
            // This is a limitation of the current reader, it doesn't read file properties.
            // When/if it does, this test should be updated to assert the properties.
        }
        Cleanup();
    }

    [Fact(Skip = "Reader functionality for properties is not yet implemented")]
    public void TestGroupAndChannelProperties()
    {
        string path;
        using (var writer = CreateWriter(out path))
        {
            var group = writer.CreateGroup("TestGroup");
            group.SetProperty("Description", "A group for testing.");

            var channel = writer.CreateChannel("TestGroup", "Channel1", TdmsDataType.I32);
            channel.SetProperty("Unit", "Volts");
        }

        using (var reader = new TdmsReader(new FileStream(path, FileMode.Open)))
        {
            // The current reader also doesn't expose properties on groups/channels.
            // This test serves as a placeholder for when that functionality is added.
        }
        Cleanup();
    }

    [Fact]
    public void TestWriteEmptyFile()
    {
        string path;
        using (var writer = CreateWriter(out path))
        {
            // No data written
        }

        Assert.True(File.Exists(path));
        Assert.True(File.Exists(path + "_index"));

        using (var reader = new TdmsReader(new FileStream(path, FileMode.Open)))
        {
            var file = reader.ReadFile();
            Assert.Empty(file.Groups);
            Assert.Single(file.Segments); // The writer always creates one segment for file metadata.
        }
        Cleanup();
    }

    [Fact]
    public void TestWriteFileWithMetadataOnly()
    {
        string path;
        using (var writer = CreateWriter(out path))
        {
            writer.CreateChannel("Group", "Channel", TdmsDataType.I32);
            // No data is written to the channel
        }

        using (var reader = new TdmsReader(new FileStream(path, FileMode.Open)))
        {
            var file = reader.ReadFile();
            var group = Assert.Single(file.Groups);
            var channel = Assert.Single(group.Channels);
            Assert.Equal("Group", group.Name);
            Assert.Equal("Channel", channel.Name);
            Assert.Empty(channel.ReadData<int>());
        }
        Cleanup();
    }

    [Fact]
    public void TestChannelCreationThrowsOnTypeMismatch()
    {
        string path;
        using (var writer = CreateWriter(out path))
        {
            writer.CreateChannel("Group", "Channel", TdmsDataType.I32);
            Assert.Throws<InvalidOperationException>(() =>
            {
                writer.CreateChannel("Group", "Channel", TdmsDataType.DoubleFloat);
            });
        }
        Cleanup();
    }

    [Fact]
    public void TestAllNumericDataTypes()
    {
        RunTest(
            writer =>
            {
                writer.CreateChannel("Numeric", "I8", TdmsDataType.I8).WriteValues(new sbyte[] { -10, 10 });
                writer.CreateChannel("Numeric", "I16", TdmsDataType.I16).WriteValues(new short[] { -1000, 1000 });
                writer.CreateChannel("Numeric", "I32", TdmsDataType.I32).WriteValues(new int[] { -100000, 100000 });
                writer.CreateChannel("Numeric", "I64", TdmsDataType.I64).WriteValues(new long[] { -1000000000, 1000000000 });
                writer.CreateChannel("Numeric", "U8", TdmsDataType.U8).WriteValues(new byte[] { 10, 20 });
                writer.CreateChannel("Numeric", "U16", TdmsDataType.U16).WriteValues(new ushort[] { 1000, 2000 });
                writer.CreateChannel("Numeric", "U32", TdmsDataType.U32).WriteValues(new uint[] { 100000, 200000 });
                writer.CreateChannel("Numeric", "U64", TdmsDataType.U64).WriteValues(new ulong[] { 1000000000, 2000000000 });
                writer.CreateChannel("Numeric", "Float", TdmsDataType.SingleFloat).WriteValues(new float[] { 1.23f, -4.56f });
                writer.CreateChannel("Numeric", "Double", TdmsDataType.DoubleFloat).WriteValues(new double[] { 1.23456, -7.891011 });
                writer.CreateChannel("Numeric", "Bool", TdmsDataType.Boolean).WriteValues(new bool[] { true, false, true });
                writer.WriteSegment();
            },
            file =>
            {
                var group = file.GetGroup("Numeric");
                Assert.NotNull(group);

                Assert.Equal(new sbyte[] { -10, 10 }, group.GetChannel("I8")?.ReadData<sbyte>());
                Assert.Equal(new short[] { -1000, 1000 }, group.GetChannel("I16")?.ReadData<short>());
                Assert.Equal(new int[] { -100000, 100000 }, group.GetChannel("I32")?.ReadData<int>());
                Assert.Equal(new long[] { -1000000000, 1000000000 }, group.GetChannel("I64")?.ReadData<long>());
                Assert.Equal(new byte[] { 10, 20 }, group.GetChannel("U8")?.ReadData<byte>());
                Assert.Equal(new ushort[] { 1000, 2000 }, group.GetChannel("U16")?.ReadData<ushort>());
                Assert.Equal(new uint[] { 100000, 200000 }, group.GetChannel("U32")?.ReadData<uint>());
                Assert.Equal(new ulong[] { 1000000000, 2000000000 }, group.GetChannel("U64")?.ReadData<ulong>());
                Assert.Equal(new float[] { 1.23f, -4.56f }, group.GetChannel("Float")?.ReadData<float>());
                Assert.Equal(new double[] { 1.23456, -7.891011 }, group.GetChannel("Double")?.ReadData<double>());
                Assert.Equal(new bool[] { true, false, true }, group.GetChannel("Bool")?.ReadBoolData());
            }
        );
    }

    [Fact]
    public void TestStringData()
    {
        RunTest(
            writer =>
            {
                var channel = writer.CreateChannel("Strings", "StringChannel", TdmsDataType.String);
                var data = new[] { "Hello", "World", "TDMS", "", "Last one" };
                channel.WriteStrings(data);
                writer.WriteSegment();
            },
            file =>
            {
                var group = file.GetGroup("Strings");
                Assert.NotNull(group);
                var channelReader = group.GetChannel("StringChannel");
                Assert.NotNull(channelReader);

                var readData = channelReader.ReadStringData();
                Assert.Equal(new[] { "Hello", "World", "TDMS", "", "Last one" }, readData);
            }
        );
    }

    [Fact]
    public void TestMultipleSegments()
    {
        RunTest(
            writer =>
            {
                var channel = writer.CreateChannel("MultiSegment", "Data", TdmsDataType.I16);

                channel.WriteValues(new short[] { 1, 2, 3 });
                writer.WriteSegment(); // Segment 1

                channel.WriteValues(new short[] { 4, 5, 6 });
                writer.WriteSegment(); // Segment 2
            },
            file =>
            {
                var group = file.GetGroup("MultiSegment");
                Assert.NotNull(group);
                var channelReader = group.GetChannel("Data");
                Assert.NotNull(channelReader);

                var readData = channelReader.ReadData<short>();
                Assert.Equal(new short[] { 1, 2, 3, 4, 5, 6 }, readData);
            }
        );
    }

    [Fact]
    public void TestStringChannelMultipleWritesBeforeSegmentAreValid()
    {
        RunTest(
            writer =>
            {
                var channel = writer.CreateChannel("Strings", "S", TdmsDataType.String);
                channel.WriteStrings(new[] { "a", "bb" });
                channel.WriteStrings(new[] { "ccc", "dddd" });
                writer.WriteSegment();
            },
            file =>
            {
                var group = file.GetGroup("Strings");
                Assert.NotNull(group);
                var channel = group.GetChannel("S");
                Assert.NotNull(channel);
                Assert.Equal(new[] { "a", "bb", "ccc", "dddd" }, channel.ReadStringData());
            }
        );
    }

    [Fact]
    public void TestChangingActiveChannelsCreatesNewObjectListSegment()
    {
        string path;
        using (var writer = CreateWriter(out path))
        {
            var c1 = writer.CreateChannel("G", "C1", TdmsDataType.I32);
            var c2 = writer.CreateChannel("G", "C2", TdmsDataType.I32);

            c1.WriteValues(new[] { 1, 2, 3 });
            c2.WriteValues(new[] { 10, 20, 30 });
            writer.WriteSegment();

            c1.WriteValues(new[] { 4, 5, 6 });
            writer.WriteSegment();
        }

        using (var reader = new TdmsReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)))
        {
            var file = reader.ReadFile();
            Assert.Equal(2, file.Segments.Count);
            Assert.True(file.Segments[1].IsNewObjectList);
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, file.GetGroup("G")?.GetChannel("C1")?.ReadData<int>());
            Assert.Equal(new[] { 10, 20, 30 }, file.GetGroup("G")?.GetChannel("C2")?.ReadData<int>());
        }

        Cleanup();
    }

    [Fact]
    public void TestStringWritesForceNewSegmentInsteadOfRawAppend()
    {
        string path;
        using (var writer = CreateWriter(out path))
        {
            var channel = writer.CreateChannel("Strings", "S", TdmsDataType.String);
            channel.WriteStrings(new[] { "alpha", "beta" });
            writer.WriteSegment();

            channel.WriteStrings(new[] { "gamma", "delta" });
            writer.WriteSegment();
        }

        using (var reader = new TdmsReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)))
        {
            var file = reader.ReadFile();
            Assert.Equal(2, file.Segments.Count);
            Assert.True(file.Segments[1].ContainsMetadata);
            Assert.Equal(new[] { "alpha", "beta", "gamma", "delta" }, file.GetGroup("Strings")?.GetChannel("S")?.ReadStringData());
        }

        Cleanup();
    }
}
