using System.IO;
using System.Threading.Tasks;
using TdmsSharp;
using Xunit;

public class AsyncWriterTests
{
    [Fact]
    public async Task WriteChannelData_BuffersUntilExplicitWriteSegment()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var writer = new AsyncTdmsWriter(path))
            {
                writer.WriteChannelData("G", "C", new int[] { 1, 2, 3 });
                writer.WriteChannelData("G", "C", new int[] { 4, 5 });

                await writer.WriteSegmentAsync();
            }

            using var reader = new TdmsReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            var file = reader.ReadFile();
            var group = Assert.Single(file.Groups);
            var channel = Assert.Single(group.Channels);
            Assert.Equal(new int[] { 1, 2, 3, 4, 5 }, channel.ReadData<int>());
            Assert.Single(file.Segments);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            var indexPath = path + "_index";
            if (File.Exists(indexPath)) File.Delete(indexPath);
        }
    }

    [Fact]
    public async Task FlushAsync_WritesBufferedData()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var writer = new AsyncTdmsWriter(path))
            {
                writer.WriteChannelData("G", "C", new float[] { 1.5f, 2.5f, 3.5f });
                await writer.FlushAsync();
            }

            using var reader = new TdmsReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            var file = reader.ReadFile();
            var group = Assert.Single(file.Groups);
            var channel = Assert.Single(group.Channels);
            Assert.Equal(new float[] { 1.5f, 2.5f, 3.5f }, channel.ReadData<float>());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            var indexPath = path + "_index";
            if (File.Exists(indexPath)) File.Delete(indexPath);
        }
    }
}
