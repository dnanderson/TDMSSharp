using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using TDMSSharp;

class Program
{
    static async Task Main(string[] args)
    {
        // Example 1: Basic usage with automatic file management
        await BasicUsageExample();

        // Example 2: High-performance streaming with multiple channels
        await HighPerformanceStreamingExample();

        // Example 3: Writing with properties at all levels
        await PropertiesExample();

        // Example 4: Incremental writing with segment optimization
        await IncrementalWritingExample();

        // Example 5: Writing different data types
        await DataTypesExample();

        // Example 6: Waveform data with metadata
        await WaveformExample();

        // Example 7: Using memory streams for in-memory TDMS
        await InMemoryExample();

        Console.WriteLine("All examples completed successfully!");
    }

    static async Task BasicUsageExample()
    {
        Console.WriteLine("Example 1: Basic Usage");
        
        var options = new TdmsWriterOptions
        {
            Version = 4713,
            CreateIndexFile = true // Create index file for faster reading
        };

        using (var writer = new TdmsWriter("example1.tdms", options))
        {
            // Write root properties
            writer.WriteRoot(new Dictionary<string, object>
            {
                ["Author"] = "TDMS.NET",
                ["Description"] = "Basic example file",
                ["DateTime"] = DateTime.UtcNow
            });

            // Write a group
            writer.WriteGroup("Measurements", new Dictionary<string, object>
            {
                ["SamplingRate"] = 1000.0,
                ["Equipment"] = "DAQ-9000"
            });

            // Write channel data
            var data = new double[1000];
            for (int i = 0; i < data.Length; i++)
                data[i] = Math.Sin(2 * Math.PI * i / 100.0);

            writer.WriteChannel<double>("Measurements", "Sine Wave", data, new Dictionary<string, object>
            {
                ["Unit"] = "Volts",
                ["Sensor"] = "Voltage Probe A1"
            });

            // Write segment (automatic on dispose, but can be called explicitly)
            writer.WriteSegment();
        }

        Console.WriteLine("  Created: example1.tdms and example1.tdms_index\n");
    }

    static async Task HighPerformanceStreamingExample()
    {
        Console.WriteLine("Example 2: High-Performance Streaming");
        
        var sw = Stopwatch.StartNew();
        const int numSamples = 1_000_000;
        const int numChannels = 10;

        using (var writer = new TdmsWriter("example2_streaming.tdms"))
        {
            writer.WriteRoot(new Dictionary<string, object>
            {
                ["Title"] = "High-Performance Streaming Test",
                ["SampleCount"] = numSamples * numChannels
            });

            writer.WriteGroup("StreamData", new Dictionary<string, object>
            {
                ["ChannelCount"] = numChannels,
                ["TotalSamples"] = numSamples
            });

            // Generate and write large datasets efficiently
            var buffer = new float[numSamples];
            var random = new Random(42);

            for (int ch = 0; ch < numChannels; ch++)
            {
                // Fill buffer with simulated data
                for (int i = 0; i < numSamples; i++)
                {
                    buffer[i] = (float)(random.NextDouble() * 10.0 + ch * 100);
                }

                writer.WriteChannel<float>($"StreamData", $"Channel_{ch:D2}", buffer.AsSpan(), 
                    new Dictionary<string, object>
                    {
                        ["ChannelIndex"] = ch,
                        ["DataType"] = "Float32"
                    });
            }

            writer.WriteSegment();
        }

        sw.Stop();
        var totalMB = (numSamples * numChannels * sizeof(float)) / (1024.0 * 1024.0);
        Console.WriteLine($"  Written {totalMB:F2} MB in {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"  Throughput: {totalMB / sw.Elapsed.TotalSeconds:F2} MB/s\n");
    }

    static async Task PropertiesExample()
    {
        Console.WriteLine("Example 3: Properties at All Levels");
        
        using (var writer = new TdmsWriter("example3_properties.tdms"))
        {
            // File-level properties
            writer.WriteRoot(new Dictionary<string, object>
            {
                ["FileVersion"] = "1.0.0",
                ["CreatedBy"] = Environment.UserName,
                ["Machine"] = Environment.MachineName,
                ["Timestamp"] = DateTime.Now,
                ["ProcessID"] = Environment.ProcessId
            });

            // Group-level properties
            writer.WriteGroup("TestResults", new Dictionary<string, object>
            {
                ["TestID"] = "123",
                ["TestType"] = "Performance",
                ["PassCriteria"] = 95.5,
                ["Temperature"] = 23.5,
                ["Humidity"] = 45.0,
                ["Passed"] = true
            });

            // Channel with various property types
            var testData = new[] { 1.1, 2.2, 3.3, 4.4, 5.5 };
            writer.WriteChannel<double>("TestResults", "Measurements", testData.AsSpan(),
                new Dictionary<string, object>
                {
                    ["Min"] = 1.1,
                    ["Max"] = 5.5,
                    ["Mean"] = 3.3,
                    ["StdDev"] = 1.58113883,
                    ["Unit"] = "mm",
                    ["CalibrationDate"] = DateTime.Parse("2024-01-15"),
                    ["CalibrationValid"] = true,
                    ["SensorID"] = 12345L,
                    ["Precision"] = (byte)3
                });

            writer.WriteSegment();
        }

        Console.WriteLine("  Created file with properties at file, group, and channel levels\n");
    }

    static async Task IncrementalWritingExample()
    {
        Console.WriteLine("Example 4: Incremental Writing with Segments");
        
        var options = new TdmsWriterOptions
        {
            Version = 4713,
            CreateIndexFile = true,
            BufferSize = 131072 // 128KB buffer
        };

        using (var writer = new TdmsWriter("example4_incremental.tdms", options))
        {
            // Initial metadata
            writer.WriteRoot(new Dictionary<string, object>
            {
                ["ExperimentName"] = "Incremental Data Acquisition"
            });

            writer.WriteGroup("Sensors", new Dictionary<string, object>
            {
                ["Location"] = "Lab A"
            });

            // Simulate real-time data acquisition in chunks
            var random = new Random();
            const int chunkSize = 100;
            const int numChunks = 10;
            bool should_segment = false;

            for (int chunk = 0; chunk < numChunks; chunk++)
            {
                if (should_segment)
                {
                    writer.BeginSegment();
                    should_segment = true;
                }

                // Generate chunk data
                var tempData = new double[chunkSize];
                var pressureData = new float[chunkSize];

                for (int i = 0; i < chunkSize; i++)
                {
                    tempData[i] = 20.0 + random.NextDouble() * 5.0;
                    pressureData[i] = (float)(100.0 + random.NextDouble() * 10.0);
                }

                // Write chunk
                writer.WriteChannel<double>("Sensors", "Temperature", tempData.AsSpan(), 
                    chunk == 0 ? new Dictionary<string, object> { ["Unit"] = "°C" } : null);
                
                writer.WriteChannel<float>("Sensors", "Pressure", pressureData.AsSpan(),
                    chunk == 0 ? new Dictionary<string, object> { ["Unit"] = "kPa" } : null);

                writer.WriteSegment();

                Console.WriteLine($"    Written chunk {chunk + 1}/{numChunks}");
                
                // Simulate delay between acquisitions
                await Task.Delay(10);
            }
        }

        Console.WriteLine("  Completed incremental writing\n");
    }

    static async Task DataTypesExample()
    {
        Console.WriteLine("Example 5: Different Data Types");
        
        using (var writer = new TdmsWriter("example5_datatypes.tdms"))
        {
            writer.WriteRoot(new Dictionary<string, object>
            {
                ["Description"] = "Demonstrating all supported data types"
            });

            writer.WriteGroup("DataTypes");

            // Signed integers
            writer.WriteChannel<sbyte>("DataTypes", "Int8", new sbyte[] { -128, 0, 127 });
            writer.WriteChannel<short>("DataTypes", "Int16", new short[] { -32768, 0, 32767 });
            writer.WriteChannel<int>("DataTypes", "Int32", new int[] { int.MinValue, 0, int.MaxValue });
            writer.WriteChannel<long>("DataTypes", "Int64", new long[] { long.MinValue, 0, long.MaxValue });

            // Unsigned integers
            writer.WriteChannel<byte>("DataTypes", "UInt8", new byte[] { 0, 128, 255 });
            writer.WriteChannel<ushort>("DataTypes", "UInt16", new ushort[] { 0, 32768, 65535 });
            writer.WriteChannel<uint>("DataTypes", "UInt32", new uint[] { 0, 2147483648, uint.MaxValue });
            writer.WriteChannel<ulong>("DataTypes", "UInt64", new ulong[] { 0, 9223372036854775808, ulong.MaxValue });

            // Floating point
            writer.WriteChannel<float>("DataTypes", "Float32", new float[] { -3.14159f, 0.0f, float.MaxValue });
            writer.WriteChannel<double>("DataTypes", "Float64", new double[] { Math.PI, Math.E, double.MaxValue });

            // Boolean
            writer.WriteChannel<bool>("DataTypes", "Boolean", new bool[] { true, false, true, false });

            // Strings
            writer.WriteStringChannel("DataTypes", "Strings", new string[]
            {
                "Hello, TDMS!",
                "Unicode: 你好 мир 🌍",
                "Special chars: @#$%^&*()",
                ""  // Empty string
            });

            writer.WriteSegment();
        }

        Console.WriteLine("  Created file with all supported data types\n");
    }

    static async Task WaveformExample()
    {
        Console.WriteLine("Example 6: Waveform Data");
        
        using (var writer = new TdmsWriter("example6_waveform.tdms"))
        {
            writer.WriteRoot(new Dictionary<string, object>
            {
                ["Title"] = "Waveform Data Example"
            });

            writer.WriteGroup("Waveforms", new Dictionary<string, object>
            {
                ["AcquisitionRate"] = 10000.0,
                ["Device"] = "NI-DAQ-6001"
            });

            // Generate waveform data
            const int samples = 10000;
            const double samplingRate = 10000.0;
            const double frequency = 50.0; // 50 Hz signal
            
            var waveformData = new double[samples];
            var startTime = DateTime.UtcNow;
            
            for (int i = 0; i < samples; i++)
            {
                double t = i / samplingRate;
                waveformData[i] = 5.0 * Math.Sin(2 * Math.PI * frequency * t) + 
                                  0.5 * Math.Sin(2 * Math.PI * frequency * 3 * t); // Add harmonic
            }

            // Use the waveform extension method
            writer.WriteWaveform<double>("Waveforms", "Signal_50Hz", waveformData.AsSpan(), 
                startTime, 1.0 / samplingRate,
                new Dictionary<string, object>
                {
                    ["SignalType"] = "Voltage",
                    ["Unit"] = "V",
                    ["Frequency"] = frequency
                });

            // Write another waveform with noise
            var noisyData = new double[samples];
            var random = new Random();
            
            for (int i = 0; i < samples; i++)
            {
                double t = i / samplingRate;
                noisyData[i] = 3.0 * Math.Sin(2 * Math.PI * 25.0 * t) + 
                              (random.NextDouble() - 0.5) * 0.5; // Add noise
            }

            writer.WriteWaveform<double>("Waveforms", "NoisySignal_25Hz", noisyData.AsSpan(),
                startTime, 1.0 / samplingRate,
                new Dictionary<string, object>
                {
                    ["SignalType"] = "Voltage",
                    ["Unit"] = "V",
                    ["Frequency"] = 25.0,
                    ["NoiseLevel"] = 0.5
                });

            writer.WriteSegment();
        }

        Console.WriteLine("  Created waveform file with proper metadata\n");
    }

    static async Task InMemoryExample()
    {
        Console.WriteLine("Example 7: In-Memory TDMS");
        
        // Create TDMS file in memory
        using var dataStream = new MemoryStream();
        using var indexStream = new MemoryStream();
        
        using (var writer = new TdmsWriter(dataStream, indexStream, ownsStreams: false))
        {
            writer.WriteRoot(new Dictionary<string, object>
            {
                ["InMemory"] = true,
                ["Purpose"] = "Testing"
            });

            writer.WriteGroup("TestData");

            // Write some test data
            var data = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            writer.WriteChannel<int>("TestData", "Counter", data.AsSpan(),
                new Dictionary<string, object>
                {
                    ["Description"] = "Simple counter"
                });

            writer.WriteSegment();
        }

        Console.WriteLine($"  Data stream size: {dataStream.Length} bytes");
        Console.WriteLine($"  Index stream size: {indexStream.Length} bytes");

        // Optionally write to disk
        await File.WriteAllBytesAsync("example7_from_memory.tdms", dataStream.ToArray());
        await File.WriteAllBytesAsync("example7_from_memory.tdms_index", indexStream.ToArray());
        
        Console.WriteLine("  Saved in-memory TDMS to disk\n");
    }
}