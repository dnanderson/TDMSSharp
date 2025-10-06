// ReaderTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using TdmsSharp;

[TestClass]
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

    [TestMethod]
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
                Assert.AreEqual(1, file.Groups.Count);
                var group = file.GetGroup("Group");
                Assert.IsNotNull(group);
                Assert.AreEqual(1, group.Channels.Count);

                var channelReader = group.GetChannel("Channel1");
                Assert.IsNotNull(channelReader);
                Assert.AreEqual(TdmsDataType.I32, channelReader.DataType);

                var readData = channelReader.ReadData<int>();
                CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, readData);
            }
        );
    }

    [TestMethod]
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
                Assert.IsNotNull(group);

                CollectionAssert.AreEqual(new sbyte[] { -10, 10 }, group.GetChannel("I8")?.ReadData<sbyte>());
                CollectionAssert.AreEqual(new short[] { -1000, 1000 }, group.GetChannel("I16")?.ReadData<short>());
                CollectionAssert.AreEqual(new int[] { -100000, 100000 }, group.GetChannel("I32")?.ReadData<int>());
                CollectionAssert.AreEqual(new long[] { -1000000000, 1000000000 }, group.GetChannel("I64")?.ReadData<long>());
                CollectionAssert.AreEqual(new byte[] { 10, 20 }, group.GetChannel("U8")?.ReadData<byte>());
                CollectionAssert.AreEqual(new ushort[] { 1000, 2000 }, group.GetChannel("U16")?.ReadData<ushort>());
                CollectionAssert.AreEqual(new uint[] { 100000, 200000 }, group.GetChannel("U32")?.ReadData<uint>());
                CollectionAssert.AreEqual(new ulong[] { 1000000000, 2000000000 }, group.GetChannel("U64")?.ReadData<ulong>());
                CollectionAssert.AreEqual(new float[] { 1.23f, -4.56f }, group.GetChannel("Float")?.ReadData<float>());
                CollectionAssert.AreEqual(new double[] { 1.23456, -7.891011 }, group.GetChannel("Double")?.ReadData<double>());
                CollectionAssert.AreEqual(new bool[] { true, false, true }, group.GetChannel("Bool")?.ReadBoolData());
            }
        );
    }

    [TestMethod]
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
                Assert.IsNotNull(group);
                var channelReader = group.GetChannel("StringChannel");
                Assert.IsNotNull(channelReader);

                var readData = channelReader.ReadStringData();
                CollectionAssert.AreEqual(new[] { "Hello", "World", "TDMS", "", "Last one" }, readData);
            }
        );
    }

    [TestMethod]
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
                Assert.IsNotNull(group);
                var channelReader = group.GetChannel("Data");
                Assert.IsNotNull(channelReader);

                var readData = channelReader.ReadData<short>();
                CollectionAssert.AreEqual(new short[] { 1, 2, 3, 4, 5, 6 }, readData);
            }
        );
    }
}