using System;
using System.Diagnostics;
using System.Threading.Tasks;
using TDMSSharp;

class EfficientUsageExample
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Efficient TDMS Writing Examples");
        Console.WriteLine("===============================\n");

        // Example 1: Simple efficient streaming
        await Example1_SimpleStreaming();

        // Example 2: Raw data only optimization
        await Example2_RawDataOnlyOptimization();

        // Example 3: High-frequency data acquisition
        await Example3_HighFrequencyAcquisition();

        // Example 4: Multi-channel with metadata
        await Example4_MultiChannelWithMetadata();

        // Example 5: Performance comparison
        await Example5_PerformanceComparison();

        Console.WriteLine("\nAll examples completed!");
    }

    static async Task Example1_SimpleStreaming()
    {
        Console.WriteLine("Example 1: Simple Efficient Streaming");
        Console.WriteLine("--------------------------------------");

        var options = new TdmsWriterOptions
        {
            Version = 4713,
            CreateIndexFile = true,
            BufferSize = 131072 // 128KB buffer
        };

        using (var writer = new TdmsWriter("example1_streaming.tdms", options))
        {
            // Write metadata once at the beginning
            writer.WriteRoot(new Dictionary<string, object>
            {
                ["Title"] = "Efficient Streaming Example",
                ["Created"] = DateTime.UtcNow
            });

            writer.WriteGroup("Sensors", new Dictionary<string, object>
            {
                ["SamplingRate"] = 10000.0,
                ["Location"] = "Lab A"
            });

            // First write includes channel properties
            var initialData = new double[] { 1.0, 2.0, 3.0 };
            writer.WriteChannel("Sensors", "Temperature", initialData.AsSpan(),
                new Dictionary<string, object> { ["Unit"] = "°C" });
            
            writer.WriteSegment(); // First segment with full metadata

            // Subsequent writes - just data, no new properties
            // These will use optimized paths (same index, no metadata changes)
            Console.WriteLine("  Writing 100 data chunks with optimization...");
            var sw = Stopwatch.StartNew();
            
            var buffer = new double[1000]; // Reuse buffer
            var random = new Random(42);
            
            for (int i = 0; i < 100; i++)
            {
                // Fill buffer with data
                for (int j = 0; j < buffer.Length; j++)
                {
                    buffer[j] = 20.0 + random.NextDouble() * 5.0;
                }
                
                // Write without creating new objects or properties
                writer.WriteChannel("Sensors", "Temperature", buffer.AsSpan());
                writer.WriteSegment();
            }
            
            sw.Stop();
            Console.WriteLine($"  Time: {sw.ElapsedMilliseconds}ms for 100,000 samples");
        }

        var fileInfo = new FileInfo("example1_streaming.tdms");
        Console.WriteLine($"  File size: {fileInfo.Length:N0} bytes\n");
    }

    static async Task Example2_RawDataOnlyOptimization()
    {
        Console.WriteLine("Example 2: Raw Data Only Optimization");
        Console.WriteLine("--------------------------------------");
        
        using (var writer = new TdmsWriter("example2_rawonly.tdms"))
        {
            // Setup
            writer.WriteRoot(new Dictionary<string, object> { ["Type"] = "Raw Data Test" });
            writer.WriteGroup("Data");
            
            // Initial write with structure
            var data = new float[] { 1.0f, 2.0f, 3.0f };
            writer.WriteChannel("Data", "Signal", data.AsSpan(),
                new Dictionary<string, object> { ["Unit"] = "V" });
            writer.WriteSegment();
            
            Console.WriteLine("  First segment: Full metadata");
            
            // After first segment, if we write the same structure,
            // the writer will optimize to raw data only appending
            var buffer = new float[1000];
            
            Console.WriteLine("  Next 50 segments: Raw data only (no headers)");
            for (int i = 0; i < 50; i++)
            {
                Array.Fill(buffer, i * 0.1f);
                writer.WriteChannel("Data", "Signal", buffer.AsSpan());
                writer.WriteSegment();
            }
        }

        var fileInfo = new FileInfo("example2_rawonly.tdms");
        Console.WriteLine($"  File size: {fileInfo.Length:N0} bytes");
        Console.WriteLine($"  Optimization: Raw data appended directly after first segment\n");
    }

    static async Task Example3_HighFrequencyAcquisition()
    {
        Console.WriteLine("Example 3: High-Frequency Data Acquisition");
        Console.WriteLine("-------------------------------------------");

        var options = new TdmsWriterOptions
        {
            Version = 4713,
            InterleaveData = true, // Better cache performance
            BufferSize = 1048576 // 1MB buffer
        };

        using (var writer = new TdmsWriter("example3_highfreq.tdms", options))
        {
            const int numChannels = 16;
            const int samplesPerChannel = 10000;
            
            // Setup metadata once
            writer.WriteRoot(new Dictionary<string, object>
            {
                ["AcquisitionRate"] = "1 MHz",
                ["Channels"] = numChannels
            });

            writer.WriteGroup("HighSpeed", new Dictionary<string, object>
            {
                ["Device"] = "NI-DAQ-6363"
            });

            // Pre-allocate buffers for each channel
            var buffers = new double[numChannels][];
            for (int ch = 0; ch < numChannels; ch++)
            {
                buffers[ch] = new double[samplesPerChannel];
            }

            var sw = Stopwatch.StartNew();
            var random = new Random(42);
            
            // Simulate 10 acquisitions
            for (int acq = 0; acq < 10; acq++)
            {
                // Fill buffers with simulated data
                for (int ch = 0; ch < numChannels; ch++)
                {
                    for (int i = 0; i < samplesPerChannel; i++)
                    {
                        buffers[ch][i] = ch * 10.0 + random.NextDouble();
                    }
                    
                    // Write channel data
                    // First acquisition includes properties, subsequent ones don't
                    var properties = acq == 0 
                        ? new Dictionary<string, object> { ["ChannelIndex"] = ch }
                        : null;
                    
                    writer.WriteChannel("HighSpeed", $"AI{ch}", buffers[ch].AsSpan(), properties);
                }
                
                writer.WriteSegment();
            }
            
            sw.Stop();
            
            var totalSamples = numChannels * samplesPerChannel * 10;
            var totalMB = (totalSamples * sizeof(double)) / (1024.0 * 1024.0);
            var throughput = totalMB / sw.Elapsed.TotalSeconds;
            
            Console.WriteLine($"  Channels: {numChannels}");
            Console.WriteLine($"  Total samples: {totalSamples:N0}");
            Console.WriteLine($"  Data size: {totalMB:F2} MB");
            Console.WriteLine($"  Time: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"  Throughput: {throughput:F2} MB/s\n");
        }
    }

    static async Task Example4_MultiChannelWithMetadata()
    {
        Console.WriteLine("Example 4: Multi-Channel with Metadata");
        Console.WriteLine("---------------------------------------");

        using (var writer = new TdmsWriter("example4_multichannel.tdms"))
        {
            // File-level metadata
            writer.WriteRoot(new Dictionary<string, object>
            {
                ["TestID"] = Guid.NewGuid().ToString(),
                ["Operator"] = Environment.UserName,
                ["StartTime"] = DateTime.Now
            });

            // Different groups for organization
            writer.WriteGroup("Inputs", new Dictionary<string, object>
            {
                ["Type"] = "Analog Inputs"
            });

            writer.WriteGroup("Calculated", new Dictionary<string, object>
            {
                ["Type"] = "Derived Values"
            });

            // Reusable buffers
            var tempBuffer = new double[1000];
            var pressureBuffer = new float[1000];
            var statusBuffer = new bool[1000];
            var messageBuffer = new string[10];
            
            var random = new Random();
            
            // Simulate 5 data collections
            for (int iter = 0; iter < 5; iter++)
            {
                // Generate temperature data
                for (int i = 0; i < tempBuffer.Length; i++)
                {
                    tempBuffer[i] = 20.0 + random.NextDouble() * 10.0;
                }
                
                // Generate pressure data
                for (int i = 0; i < pressureBuffer.Length; i++)
                {
                    pressureBuffer[i] = (float)(100.0 + random.NextDouble() * 5.0);
                }
                
                // Generate status flags
                for (int i = 0; i < statusBuffer.Length; i++)
                {
                    statusBuffer[i] = random.Next(100) > 10; // 90% true
                }
                
                // Generate messages
                for (int i = 0; i < messageBuffer.Length; i++)
                {
                    messageBuffer[i] = $"Status_{iter}_{i}: OK";
                }
                
                // Write channels with properties only on first iteration
                var tempProps = iter == 0 
                    ? new Dictionary<string, object> 
                      { 
                          ["Unit"] = "°C",
                          ["Sensor"] = "PT100",
                          ["Calibration"] = DateTime.Now.AddDays(-30)
                      }
                    : null;
                
                var pressProps = iter == 0
                    ? new Dictionary<string, object> 
                      { 
                          ["Unit"] = "kPa",
                          ["Range"] = "0-200"
                      }
                    : null;
                
                var statusProps = iter == 0
                    ? new Dictionary<string, object> { ["Description"] = "System OK Flag" }
                    : null;
                
                var msgProps = iter == 0
                    ? new Dictionary<string, object> { ["Format"] = "Status_[iter]_[index]: [state]" }
                    : null;
                
                writer.WriteChannel("Inputs", "Temperature", tempBuffer.AsSpan(), tempProps);
                writer.WriteChannel("Inputs", "Pressure", pressureBuffer.AsSpan(), pressProps);
                writer.WriteChannel("Inputs", "SystemOK", statusBuffer.AsSpan(), statusProps);
                writer.WriteStringChannel("Calculated", "Messages", messageBuffer.AsSpan(), msgProps);
                
                // Write waveform with proper metadata
                var waveformData = new double[1000];
                for (int i = 0; i < waveformData.Length; i++)
                {
                    waveformData[i] = Math.Sin(2 * Math.PI * i / 100.0) * (1.0 + iter * 0.1);
                }
                
                writer.WriteWaveform<double>("Calculated", "Waveform", waveformData.AsSpan(),
                    DateTime.Now, 0.001); // 1ms increment
                
                writer.WriteSegment();
                
                Console.WriteLine($"  Iteration {iter + 1}: Written");
            }
        }
        
        var fileInfo = new FileInfo("example4_multichannel.tdms");
        Console.WriteLine($"  File size: {fileInfo.Length:N0} bytes\n");
    }

    static async Task Example5_PerformanceComparison()
    {
        Console.WriteLine("Example 5: Performance Comparison");
        Console.WriteLine("---------------------------------");

        const int numSamples = 1000000;
        const int numWrites = 10;
        
        // Test 1: Naive approach - new properties every time
        Console.WriteLine("  Test 1: Inefficient (new metadata each write)");
        var sw1 = Stopwatch.StartNew();
        
        using (var writer = new TdmsWriter("test1_inefficient.tdms"))
        {
            var data = new float[numSamples / numWrites];
            
            for (int i = 0; i < numWrites; i++)
            {
                Array.Fill(data, i * 1.0f);
                
                // Inefficient: Writing properties every time forces new metadata
                writer.WriteChannel("Test", "Data", data.AsSpan(),
                    new Dictionary<string, object> 
                    { 
                        ["Iteration"] = i,  // Changing property
                        ["Timestamp"] = DateTime.Now  // Changing property
                    });
                writer.WriteSegment();
            }
        }
        sw1.Stop();
        var size1 = new FileInfo("test1_inefficient.tdms").Length;
        
        // Test 2: Optimized approach
        Console.WriteLine("  Test 2: Optimized (metadata once, then raw data)");
        var sw2 = Stopwatch.StartNew();
        
        using (var writer = new TdmsWriter("test2_optimized.tdms"))
        {
            var data = new float[numSamples / numWrites];
            
            // Write metadata once
            writer.WriteChannel("Test", "Data", data.AsSpan(),
                new Dictionary<string, object> 
                { 
                    ["Created"] = DateTime.Now,
                    ["TotalIterations"] = numWrites
                });
            writer.WriteSegment();
            
            // Subsequent writes without metadata changes
            for (int i = 1; i < numWrites; i++)
            {
                Array.Fill(data, i * 1.0f);
                writer.WriteChannel("Test", "Data", data.AsSpan());
                writer.WriteSegment();
            }
        }
        sw2.Stop();
        var size2 = new FileInfo("test2_optimized.tdms").Length;
        
        Console.WriteLine($"\n  Results:");
        Console.WriteLine($"  --------");
        Console.WriteLine($"  Inefficient: {sw1.ElapsedMilliseconds}ms, {size1:N0} bytes");
        Console.WriteLine($"  Optimized:   {sw2.ElapsedMilliseconds}ms, {size2:N0} bytes");
        Console.WriteLine($"  Speed gain:  {(double)sw1.ElapsedMilliseconds / sw2.ElapsedMilliseconds:F2}x faster");
        Console.WriteLine($"  Size saving: {(1.0 - (double)size2 / size1) * 100:F1}% smaller");
    }
}