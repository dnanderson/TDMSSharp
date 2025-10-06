// PerformanceTests.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TdmsSharp;
using Xunit;
using Xunit.Abstractions;

public class PerformanceTests
{
    private readonly ITestOutputHelper _output;

    public PerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

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

    [Fact]
    public void ProfileSingleChannelWriteSpeed()
    {
        const int totalValues = 1_000_000;
        const int segmentSize = 10_000;
        var data = Enumerable.Range(0, segmentSize).Select(i => (double)i).ToArray();

        string path;
        var stopwatch = new Stopwatch();

        using (var writer = CreateWriter(out path))
        {
            var channel = writer.CreateChannel("Perf", "SingleChannel", TdmsDataType.DoubleFloat);

            stopwatch.Start();
            for (int i = 0; i < totalValues / segmentSize; i++)
            {
                channel.WriteValues(data);
                writer.WriteSegment();
            }
            stopwatch.Stop();
        }

        var fileSize = new FileInfo(path).Length;
        var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
        var mbPerSecond = (fileSize / (1024.0 * 1024.0)) / elapsedSeconds;

        _output.WriteLine($"Single Channel Write Performance:");
        _output.WriteLine($"  Total values: {totalValues:N0} doubles");
        _output.WriteLine($"  File size: {fileSize / (1024.0 * 1024.0):F2} MB");
        _output.WriteLine($"  Elapsed time: {elapsedSeconds:F2} s");
        _output.WriteLine($"  Write speed: {mbPerSecond:F2} MB/s");

        Assert.True(mbPerSecond > 0);
        Cleanup();
    }

    [Fact]
    public void ProfileMultiChannelWriteSpeed()
    {
        const int numChannels = 10;
        const int totalValuesPerChannel = 100_000;
        const int segmentSize = 1_000;
        var data = Enumerable.Range(0, segmentSize).Select(i => (float)i).ToArray();

        string path;
        var stopwatch = new Stopwatch();

        using (var writer = CreateWriter(out path))
        {
            var channels = Enumerable.Range(0, numChannels)
                .Select(i => writer.CreateChannel("Perf", $"Channel_{i}", TdmsDataType.SingleFloat))
                .ToList();

            stopwatch.Start();
            for (int i = 0; i < totalValuesPerChannel / segmentSize; i++)
            {
                foreach (var channel in channels)
                {
                    channel.WriteValues(data);
                }
                writer.WriteSegment();
            }
            stopwatch.Stop();
        }

        var fileSize = new FileInfo(path).Length;
        var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
        var mbPerSecond = (fileSize / (1024.0 * 1024.0)) / elapsedSeconds;

        _output.WriteLine($"Multi-Channel ({numChannels}) Write Performance:");
        _output.WriteLine($"  Total values: {numChannels * totalValuesPerChannel:N0} floats");
        _output.WriteLine($"  File size: {fileSize / (1024.0 * 1024.0):F2} MB");
        _output.WriteLine($"  Elapsed time: {elapsedSeconds:F2} s");
        _output.WriteLine($"  Write speed: {mbPerSecond:F2} MB/s");

        Assert.True(mbPerSecond > 0);
        Cleanup();
    }
}