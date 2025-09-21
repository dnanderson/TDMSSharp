using System;
using System.Diagnostics;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace TDMSSharp.Tests
{
    public class PerformanceTests
    {
        private readonly ITestOutputHelper _output;

        public PerformanceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void BenchmarkLargeFileWriteRead()
        {
            var path = Path.GetTempFileName();
            var sw = Stopwatch.StartNew();
            
            try
            {
                // Write benchmark
                var writeStart = sw.Elapsed;
                using (var stream = new TdmsFileStream(path))
                {
                    stream.AddFileProperty("Author", "Performance Test");
                    
                    const int channelCount = 10;
                    const int samplesPerWrite = 10000;
                    const int writeCount = 100;
                    
                    var data = new double[samplesPerWrite];
                    for (int i = 0; i < samplesPerWrite; i++)
                        data[i] = i * 0.1;
                    
                    for (int w = 0; w < writeCount; w++)
                    {
                        for (int c = 0; c < channelCount; c++)
                        {
                            stream.AppendData($"Group{c / 5}", $"Channel{c}", data);
                        }
                    }
                }
                var writeTime = sw.Elapsed - writeStart;
                
                var fileInfo = new FileInfo(path);
                _output.WriteLine($"Write Performance:");
                _output.WriteLine($"  Time: {writeTime.TotalSeconds:F2}s");
                _output.WriteLine($"  File Size: {fileInfo.Length / 1024.0 / 1024.0:F2} MB");
                _output.WriteLine($"  Throughput: {fileInfo.Length / writeTime.TotalSeconds / 1024.0 / 1024.0:F2} MB/s");
                
                // Read benchmark
                var readStart = sw.Elapsed;
                var file = TdmsFile.Open(path);
                var readTime = sw.Elapsed - readStart;
                
                _output.WriteLine($"\nRead Performance:");
                _output.WriteLine($"  Time: {readTime.TotalSeconds:F2}s");
                _output.WriteLine($"  Groups: {file.ChannelGroups.Count}");
                
                long totalSamples = 0;
                foreach (var group in file.ChannelGroups)
                {
                    foreach (var channel in group.Channels)
                    {
                        totalSamples += (long)channel.NumberOfValues;
                    }
                }
                _output.WriteLine($"  Total Samples: {totalSamples:N0}");
                _output.WriteLine($"  Throughput: {fileInfo.Length / readTime.TotalSeconds / 1024.0 / 1024.0:F2} MB/s");
                
                Assert.True(writeTime.TotalSeconds < 10, "Write took too long");
                Assert.True(readTime.TotalSeconds < 5, "Read took too long");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void BenchmarkBatchWrite()
        {
            var path = Path.GetTempFileName();
            var sw = Stopwatch.StartNew();
            
            try
            {
                const int channels = 20;
                const int samples = 10000;
                
                var batchStart = sw.Elapsed;
                using (var stream = new TdmsFileStream(path))
                {
                    var batch = new (string, string, Array)[channels];
                    
                    for (int i = 0; i < channels; i++)
                    {
                        var data = new double[samples];
                        for (int j = 0; j < samples; j++)
                            data[j] = Math.Sin(j * 0.01) * i;
                        
                        batch[i] = ($"Group{i / 10}", $"Channel{i}", data);
                    }
                    
                    stream.AppendBatch(batch);
                }
                var batchTime = sw.Elapsed - batchStart;
                
                _output.WriteLine($"Batch Write Performance:");
                _output.WriteLine($"  Time: {batchTime.TotalMilliseconds:F2}ms");
                _output.WriteLine($"  Channels: {channels}");
                _output.WriteLine($"  Samples per channel: {samples:N0}");
                
                Assert.True(batchTime.TotalSeconds < 1, "Batch write took too long");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void MemoryUsageTest()
        {
            var path = Path.GetTempFileName();
            
            try
            {
                var beforeMem = GC.GetTotalMemory(true);
                
                using (var stream = new TdmsFileStream(path))
                {
                    const int iterations = 100;
                    const int samplesPerWrite = 1000;
                    
                    var data = new double[samplesPerWrite];
                    for (int i = 0; i < samplesPerWrite; i++)
                        data[i] = i;
                    
                    for (int i = 0; i < iterations; i++)
                    {
                        stream.AppendData("TestGroup", "TestChannel", data);
                    }
                }
                
                var afterMem = GC.GetTotalMemory(true);
                var memIncrease = (afterMem - beforeMem) / 1024.0 / 1024.0;
                
                _output.WriteLine($"Memory Usage:");
                _output.WriteLine($"  Before: {beforeMem / 1024.0 / 1024.0:F2} MB");
                _output.WriteLine($"  After: {afterMem / 1024.0 / 1024.0:F2} MB");
                _output.WriteLine($"  Increase: {memIncrease:F2} MB");
                
                // Memory increase should be reasonable (less than 50MB for this test)
                Assert.True(memIncrease < 50, $"Memory usage too high: {memIncrease:F2} MB");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}