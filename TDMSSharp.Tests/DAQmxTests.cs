using System;
using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace TDMSSharp.Tests
{
    public class DAQmxTests
    {
        private readonly ITestOutputHelper _output;

        public DAQmxTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void ReadDAQmxFormatChangingScaler_SimpleCase()
        {
            // Create a synthetic TDMS file with DAQmx format changing scaler
            var path = Path.GetTempFileName();
            
            try
            {
                using (var stream = new FileStream(path, FileMode.Create))
                using (var writer = new BinaryWriter(stream))
                {
                    // Write lead-in
                    writer.Write(Encoding.ASCII.GetBytes("TDSm")); // Tag
                    writer.Write((uint)0x0E); // ToC: metadata + new obj list + raw data
                    writer.Write((uint)4713); // Version
                    writer.Write((ulong)0); // Next segment offset (to be updated)
                    writer.Write((ulong)0); // Raw data offset (to be updated)

                    long metaStart = stream.Position;

                    // Write metadata: 1 file object, 1 group, 1 channel
                    writer.Write((uint)3);

                    // File object
                    WriteString(writer, "/");
                    writer.Write((uint)0xFFFFFFFF); // No raw data
                    writer.Write((uint)0); // No properties

                    // Group object
                    WriteString(writer, "/'DAQmx Group'");
                    writer.Write((uint)0xFFFFFFFF); // No raw data
                    writer.Write((uint)0); // No properties

                    // Channel with DAQmx raw data
                    WriteString(writer, "/'DAQmx Group'/'DAQmx Channel'");
                    
                    // DAQmx raw data index
                    writer.Write((uint)0x69120000); // Format changing scaler indicator
                    writer.Write((uint)1); // Dimension
                    writer.Write((ulong)5); // Number of values
                    
                    // Scalers vector (1 scaler)
                    writer.Write((uint)1);
                    writer.Write((uint)TdsDataType.DoubleFloat); // Data type
                    writer.Write((uint)0); // Raw buffer index
                    writer.Write((uint)0); // Byte offset within stride
                    writer.Write((uint)0); // Sample format bitmap
                    writer.Write((uint)0); // Scale ID
                    
                    // Raw data widths (1 width = 8 bytes for double)
                    writer.Write((uint)1);
                    writer.Write((uint)8);
                    
                    writer.Write((uint)0); // No properties

                    long metaEnd = stream.Position;
                    long metaLength = metaEnd - metaStart;

                    // Write raw data (5 double values)
                    long rawDataStart = stream.Position;
                    writer.Write(1.1);
                    writer.Write(2.2);
                    writer.Write(3.3);
                    writer.Write(4.4);
                    writer.Write(5.5);
                    long rawDataEnd = stream.Position;
                    long rawDataLength = rawDataEnd - rawDataStart;

                    // Update lead-in
                    stream.Seek(12, SeekOrigin.Begin);
                    writer.Write((ulong)(metaLength + rawDataLength)); // Next segment offset
                    writer.Write((ulong)metaLength); // Raw data offset
                }

                // Now read the file
                var file = TdmsFile.Open(path);

                Assert.Single(file.ChannelGroups);
                var group = file.ChannelGroups[0];
                Assert.Equal("/'DAQmx Group'", group.Path);

                Assert.Single(group.Channels);
                var channel = group.Channels[0];
                Assert.Equal("/'DAQmx Group'/'DAQmx Channel'", channel.Path);
                Assert.Equal(5UL, channel.NumberOfValues);

                var typedChannel = channel as TdmsChannel<double>;
                Assert.NotNull(typedChannel);
                Assert.Equal(new[] { 1.1, 2.2, 3.3, 4.4, 5.5 }, typedChannel.Data);

                _output.WriteLine($"Successfully read DAQmx channel with {channel.NumberOfValues} values");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void ReadDAQmxDigitalLineScaler()
        {
            var path = Path.GetTempFileName();
            
            try
            {
                using (var stream = new FileStream(path, FileMode.Create))
                using (var writer = new BinaryWriter(stream))
                {
                    // Write lead-in
                    writer.Write(Encoding.ASCII.GetBytes("TDSm"));
                    writer.Write((uint)0x0E);
                    writer.Write((uint)4713);
                    writer.Write((ulong)0);
                    writer.Write((ulong)0);

                    long metaStart = stream.Position;

                    writer.Write((uint)3);

                    WriteString(writer, "/");
                    writer.Write((uint)0xFFFFFFFF);
                    writer.Write((uint)0);

                    WriteString(writer, "/'Digital Group'");
                    writer.Write((uint)0xFFFFFFFF);
                    writer.Write((uint)0);

                    WriteString(writer, "/'Digital Group'/'Digital Channel'");
                    
                    writer.Write((uint)0x69130000); // Digital line scaler
                    writer.Write((uint)1);
                    writer.Write((ulong)3);
                    
                    writer.Write((uint)1); // 1 scaler
                    writer.Write((uint)TdsDataType.U8);
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    
                    writer.Write((uint)1); // 1 width
                    writer.Write((uint)1); // 1 byte
                    
                    writer.Write((uint)0);

                    long metaEnd = stream.Position;
                    long metaLength = metaEnd - metaStart;

                    long rawDataStart = stream.Position;
                    writer.Write((byte)0x01);
                    writer.Write((byte)0x00);
                    writer.Write((byte)0x01);
                    long rawDataEnd = stream.Position;
                    long rawDataLength = rawDataEnd - rawDataStart;

                    stream.Seek(12, SeekOrigin.Begin);
                    writer.Write((ulong)(metaLength + rawDataLength));
                    writer.Write((ulong)metaLength);
                }

                var file = TdmsFile.Open(path);

                var channel = file.ChannelGroups[0].Channels[0];
                Assert.Equal(3UL, channel.NumberOfValues);

                var typedChannel = channel as TdmsChannel<byte>;
                Assert.NotNull(typedChannel);
                Assert.Equal(new byte[] { 0x01, 0x00, 0x01 }, typedChannel.Data);

                _output.WriteLine("Successfully read DAQmx digital line scaler");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void ReadDAQmxInterleavedData()
        {
            var path = Path.GetTempFileName();
            
            try
            {
                using (var stream = new FileStream(path, FileMode.Create))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(Encoding.ASCII.GetBytes("TDSm"));
                    writer.Write((uint)0x0E);
                    writer.Write((uint)4713);
                    writer.Write((ulong)0);
                    writer.Write((ulong)0);

                    long metaStart = stream.Position;
                    writer.Write((uint)3);

                    WriteString(writer, "/");
                    writer.Write((uint)0xFFFFFFFF);
                    writer.Write((uint)0);

                    WriteString(writer, "/'Interleaved Group'");
                    writer.Write((uint)0xFFFFFFFF);
                    writer.Write((uint)0);

                    WriteString(writer, "/'Interleaved Group'/'Channel'");
                    
                    writer.Write((uint)0x69120000);
                    writer.Write((uint)1);
                    writer.Write((ulong)3); // 3 samples
                    
                    // 2 scalers interleaved
                    writer.Write((uint)2);
                    // First scaler - float at offset 0
                    writer.Write((uint)TdsDataType.SingleFloat);
                    writer.Write((uint)0);
                    writer.Write((uint)0); // Offset 0
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    // Second scaler - int at offset 4
                    writer.Write((uint)TdsDataType.I32);
                    writer.Write((uint)0);
                    writer.Write((uint)4); // Offset 4
                    writer.Write((uint)0);
                    writer.Write((uint)1);
                    
                    // Stride is 8 bytes (4 for float + 4 for int)
                    writer.Write((uint)1);
                    writer.Write((uint)8);
                    
                    writer.Write((uint)0);

                    long metaEnd = stream.Position;
                    long metaLength = metaEnd - metaStart;

                    long rawDataStart = stream.Position;
                    // Write interleaved data: [float, int, float, int, float, int]
                    writer.Write(1.5f);
                    writer.Write((int)100);
                    writer.Write(2.5f);
                    writer.Write((int)200);
                    writer.Write(3.5f);
                    writer.Write((int)300);
                    long rawDataEnd = stream.Position;
                    long rawDataLength = rawDataEnd - rawDataStart;

                    stream.Seek(12, SeekOrigin.Begin);
                    writer.Write((ulong)(metaLength + rawDataLength));
                    writer.Write((ulong)metaLength);
                }

                var file = TdmsFile.Open(path);

                var channel = file.ChannelGroups[0].Channels[0];
                Assert.Equal(3UL, channel.NumberOfValues);

                // Should extract the first scaler (float values)
                var typedChannel = channel as TdmsChannel<float>;
                Assert.NotNull(typedChannel);
                Assert.Equal(new[] { 1.5f, 2.5f, 3.5f }, typedChannel.Data);

                _output.WriteLine("Successfully read interleaved DAQmx data");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void ReadDAQmxMultipleSegments()
        {
            var path = Path.GetTempFileName();
            
            try
            {
                using (var stream = new FileStream(path, FileMode.Create))
                using (var writer = new BinaryWriter(stream))
                {
                    // First segment
                    long seg1Start = stream.Position;
                    writer.Write(Encoding.ASCII.GetBytes("TDSm"));
                    writer.Write((uint)0x0E);
                    writer.Write((uint)4713);
                    writer.Write((ulong)0); // To be updated
                    writer.Write((ulong)0); // To be updated

                    long meta1Start = stream.Position;
                    writer.Write((uint)3);

                    WriteString(writer, "/");
                    writer.Write((uint)0xFFFFFFFF);
                    writer.Write((uint)0);

                    WriteString(writer, "/'Group'");
                    writer.Write((uint)0xFFFFFFFF);
                    writer.Write((uint)0);

                    WriteString(writer, "/'Group'/'Channel'");
                    writer.Write((uint)0x69120000);
                    writer.Write((uint)1);
                    writer.Write((ulong)2);
                    writer.Write((uint)1);
                    writer.Write((uint)TdsDataType.I32);
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    writer.Write((uint)1);
                    writer.Write((uint)4);
                    writer.Write((uint)0);

                    long meta1End = stream.Position;
                    long raw1Start = stream.Position;
                    writer.Write((int)10);
                    writer.Write((int)20);
                    long raw1End = stream.Position;

                    // Second segment - reuses index
                    long seg2Start = stream.Position;
                    writer.Write(Encoding.ASCII.GetBytes("TDSm"));
                    writer.Write((uint)0x0E);
                    writer.Write((uint)4713);
                    writer.Write((ulong)0);
                    writer.Write((ulong)0);

                    long meta2Start = stream.Position;
                    writer.Write((uint)1);

                    WriteString(writer, "/'Group'/'Channel'");
                    writer.Write((uint)0x00000000); // Reuse previous index
                    writer.Write((uint)0);

                    long meta2End = stream.Position;
                    long raw2Start = stream.Position;
                    writer.Write((int)30);
                    writer.Write((int)40);
                    long raw2End = stream.Position;

                    // Update segment 1 pointers
                    stream.Seek(seg1Start + 12, SeekOrigin.Begin);
                    writer.Write((ulong)(seg2Start - seg1Start - 28));
                    writer.Write((ulong)(meta1End - meta1Start));

                    // Update segment 2 pointers
                    stream.Seek(seg2Start + 12, SeekOrigin.Begin);
                    writer.Write((ulong)(raw2End - seg2Start - 28));
                    writer.Write((ulong)(meta2End - meta2Start));
                }

                var file = TdmsFile.Open(path);

                var channel = file.ChannelGroups[0].Channels[0];
                Assert.Equal(4UL, channel.NumberOfValues); // 2 from each segment

                var typedChannel = channel as TdmsChannel<int>;
                Assert.NotNull(typedChannel);
                Assert.Equal(new[] { 10, 20, 30, 40 }, typedChannel.Data);

                _output.WriteLine("Successfully read multi-segment DAQmx data");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private void WriteString(BinaryWriter writer, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            writer.Write((uint)bytes.Length);
            writer.Write(bytes);
        }
    }
}