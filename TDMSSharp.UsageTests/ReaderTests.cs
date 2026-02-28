// ReaderTests.cs
using System.IO;
using TdmsSharp;
using Xunit;

public class ReaderTests
{
    private void RunTest(System.Action<TdmsFileWriter> writeAction, System.Action<TdmsFileHolder> readAssertAction)
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var writer = new TdmsFileWriter(path))
            {
                writeAction(writer);
            }

            using (var reader = new TdmsReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                var file = reader.ReadFile();
                readAssertAction(file);
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + "_index")) File.Delete(path + "_index");
        }
    }

    [Fact]
    public void TestSimpleWriteAndRead()
    {
        RunTest(
            writer => {
                var channel = writer.CreateChannel("Group", "Channel1", TdmsDataType.I32);
                var data = new[] { 1, 2, 3, 4, 5 };
                channel.WriteValues(data);
                writer.WriteSegment();
            },
            file => {
                Assert.Single(file.Groups);
                var group = file.GetGroup("Group");
                Assert.NotNull(group);
                Assert.Single(group.Channels);

                var channelReader = group.GetChannel("Channel1");
                Assert.NotNull(channelReader);
                Assert.Equal(TdmsDataType.I32, channelReader.DataType);

                var readData = channelReader.ReadData<int>();
                Assert.Equal(new[] { 1, 2, 3, 4, 5 }, readData);
            }
        );
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
    public void TestReaderSkipsPropertiesAndReadsRawDataCorrectly()
    {
        RunTest(
            writer =>
            {
                writer.SetFileProperty("author", "reader-test");

                var group = writer.CreateGroup("WithProps");
                group.SetProperty("group_prop", 42);

                var channel = writer.CreateChannel("WithProps", "Signal", TdmsDataType.I32);
                channel.SetProperty("unit", "V");
                channel.WriteValues(new[] { 10, 20, 30, 40 });
                writer.WriteSegment();
            },
            file =>
            {
                var group = file.GetGroup("WithProps");
                Assert.NotNull(group);

                var channel = group.GetChannel("Signal");
                Assert.NotNull(channel);
                Assert.Equal(new[] { 10, 20, 30, 40 }, channel.ReadData<int>());
            }
        );
    }
}
