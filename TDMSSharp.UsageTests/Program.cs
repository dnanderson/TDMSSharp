using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using TdmsSharp;

namespace TdmsDemo
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("TDMS File Writer Demo");
            Console.WriteLine("=====================\n");

            // Run different demos
            BasicUsageDemo();
            PerformanceDemo();
            WaveformDemo();
            IncrementalWriteDemo();
            DataTypesDemo();

            Console.WriteLine("\nAll demos completed successfully!");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        static void BasicUsageDemo()
        {
            Console.WriteLine("1. Basic Usage Demo");
            Console.WriteLine("-------------------");

            using (var writer = new TdmsFileWriter("basic_demo.tdms"))
            {
                // Set file properties
                writer.SetFileProperty<string>("title", "Basic TDMS Demo");
                writer.SetFileProperty<string>("author", "TDMS Writer Library");
                writer.SetFileProperty<TdmsTimestamp>("timestamp", TdmsTimestamp.Now);

                // Create a group with properties
                var group = writer.CreateGroup("Measurements");
                group.SetProperty<string>("description", "Temperature and pressure measurements");
                group.SetProperty<string>("location", "Lab A");
                group.SetProperty<int>("test_id", 12345);

                // Create channels
                var tempChannel = writer.CreateChannel("Measurements", "Temperature", TdmsDataType.DoubleFloat);
                var pressureChannel = writer.CreateChannel("Measurements", "Pressure", TdmsDataType.SingleFloat);

                // Set channel properties
                tempChannel.SetProperty<string>("unit_string", "°C");
                tempChannel.SetProperty<string>("sensor_type", "Thermocouple Type K");
                
                pressureChannel.SetProperty<string>("unit_string", "bar");
                pressureChannel.SetProperty<string>("sensor_type", "Piezoelectric");

                // Write some data
                var random = new Random();
                for (int i = 0; i < 100; i++)
                {
                    tempChannel.WriteValue(20.0 + random.NextDouble() * 10);
                    pressureChannel.WriteValue((float)(1.0 + random.NextDouble() * 0.5));
                }

                // Write the segment to disk
                writer.WriteSegment();

                Console.WriteLine("✓ Created basic_demo.tdms with 2 channels and 100 samples each\n");
            }
        }

        static void PerformanceDemo()
        {
            Console.WriteLine("2. Performance Demo");
            Console.WriteLine("-------------------");

            const int numSamples = 1_000_000;
            const int batchSize = 10000;

            using (var writer = new TdmsFileWriter("performance_demo.tdms"))
            {
                writer.SetFileProperty<string>("title", "Performance Test");
                writer.SetFileProperty<int>("sample_count", numSamples);

                var group = writer.CreateGroup("HighSpeed");
                var channel = writer.CreateChannel("HighSpeed", "Data", TdmsDataType.DoubleFloat);
                
                channel.SetProperty<double>("sampling_rate", 1000000.0);
                channel.SetProperty<string>("unit_string", "V");

                var sw = Stopwatch.StartNew();

                // Generate and write data in batches
                var data = new double[batchSize];
                var random = new Random();
                
                for (int batch = 0; batch < numSamples / batchSize; batch++)
                {
                    // Generate batch data
                    for (int i = 0; i < batchSize; i++)
                    {
                        data[i] = Math.Sin(2 * Math.PI * i / 1000.0) + 0.1 * random.NextDouble();
                    }

                    // Write using span for maximum performance
                    channel.WriteValues(data);

                    // Write segment every 10 batches to demonstrate incremental writing
                    if ((batch + 1) % 10 == 0)
                    {
                        writer.WriteSegment();
                        Console.Write($"\rProgress: {(batch + 1) * batchSize:N0} / {numSamples:N0} samples");
                    }
                }

                writer.Flush();
                sw.Stop();

                var throughput = numSamples * sizeof(double) / (sw.ElapsedMilliseconds / 1000.0) / (1024 * 1024);
                Console.WriteLine($"\n✓ Wrote {numSamples:N0} samples in {sw.ElapsedMilliseconds}ms");
                Console.WriteLine($"  Throughput: {throughput:F2} MB/s\n");
            }
        }

        static void WaveformDemo()
        {
            Console.WriteLine("3. Waveform Demo");
            Console.WriteLine("----------------");

            using (var writer = new TdmsFileWriter("waveform_demo.tdms"))
            {
                writer.SetFileProperty<string>("title", "Waveform Data");

                var group = writer.CreateGroup("Waveforms");
                
                // Create multiple waveform channels
                var sineChannel = writer.CreateChannel("Waveforms", "Sine", TdmsDataType.DoubleFloat);
                var cosineChannel = writer.CreateChannel("Waveforms", "Cosine", TdmsDataType.DoubleFloat);
                var squareChannel = writer.CreateChannel("Waveforms", "Square", TdmsDataType.DoubleFloat);

                // Set waveform properties (standard TDMS waveform attributes)
                var startTime = TdmsTimestamp.Now;
                double samplingRate = 10000.0; // 10 kHz
                int numSamples = 10000;

                foreach (var channel in new[] { sineChannel, cosineChannel, squareChannel })
                {
                    channel.SetProperty<TdmsTimestamp>("wf_start_time", startTime);
                    channel.SetProperty<double>("wf_increment", 1.0 / samplingRate);
                    channel.SetProperty<int>("wf_samples", numSamples);
                }

                // Generate waveforms
                var sineData = new double[numSamples];
                var cosineData = new double[numSamples];
                var squareData = new double[numSamples];

                for (int i = 0; i < numSamples; i++)
                {
                    double t = i / samplingRate;
                    sineData[i] = Math.Sin(2 * Math.PI * 50 * t); // 50 Hz sine
                    cosineData[i] = Math.Cos(2 * Math.PI * 50 * t); // 50 Hz cosine
                    squareData[i] = Math.Sign(Math.Sin(2 * Math.PI * 25 * t)); // 25 Hz square
                }

                // Write all data
                sineChannel.WriteValues(sineData);
                cosineChannel.WriteValues(cosineData);
                squareChannel.WriteValues(squareData);

                writer.WriteSegment();

                Console.WriteLine($"✓ Created waveform_demo.tdms with 3 waveforms, {numSamples} samples each\n");
            }
        }

        static void IncrementalWriteDemo()
        {
            Console.WriteLine("4. Incremental Write Demo");
            Console.WriteLine("-------------------------");

            using (var writer = new TdmsFileWriter("incremental_demo.tdms"))
            {
                writer.SetFileProperty<string>("title", "Incremental Writing Demo");

                var group = writer.CreateGroup("RealTimeData");
                var channel1 = writer.CreateChannel("RealTimeData", "Sensor1", TdmsDataType.DoubleFloat);
                var channel2 = writer.CreateChannel("RealTimeData", "Sensor2", TdmsDataType.SingleFloat);

                Console.WriteLine("Simulating real-time data acquisition...");

                var random = new Random();
                for (int iteration = 0; iteration < 5; iteration++)
                {
                    Thread.Sleep(100); // Simulate real-time delay

                    // Simulate acquiring new data
                    var samples = 100;
                    for (int i = 0; i < samples; i++)
                    {
                        channel1.WriteValue(random.NextDouble() * 100);
                        channel2.WriteValue((float)(random.NextDouble() * 50));
                    }

                    // Write segment (demonstrates incremental metadata optimization)
                    writer.WriteSegment();

                    Console.WriteLine($"  Iteration {iteration + 1}: Wrote {samples} samples");

                    // Change a property occasionally to demonstrate metadata updates
                    if (iteration == 2)
                    {
                        channel1.SetProperty<string>("status", "Calibrated");
                        Console.WriteLine("  → Updated channel property");
                    }
                }

                Console.WriteLine("✓ Incremental writing completed\n");
            }
        }

        static void DataTypesDemo()
        {
            Console.WriteLine("5. Data Types Demo");
            Console.WriteLine("------------------");

            using (var writer = new TdmsFileWriter("datatypes_demo.tdms"))
            {
                writer.SetFileProperty<string>("title", "All Supported Data Types");

                var group = writer.CreateGroup("DataTypes");

                // Integer types
                var int8Channel = writer.CreateChannel("DataTypes", "Int8", TdmsDataType.I8);
                var int16Channel = writer.CreateChannel("DataTypes", "Int16", TdmsDataType.I16);
                var int32Channel = writer.CreateChannel("DataTypes", "Int32", TdmsDataType.I32);
                var int64Channel = writer.CreateChannel("DataTypes", "Int64", TdmsDataType.I64);

                var uint8Channel = writer.CreateChannel("DataTypes", "UInt8", TdmsDataType.U8);
                var uint16Channel = writer.CreateChannel("DataTypes", "UInt16", TdmsDataType.U16);
                var uint32Channel = writer.CreateChannel("DataTypes", "UInt32", TdmsDataType.U32);
                var uint64Channel = writer.CreateChannel("DataTypes", "UInt64", TdmsDataType.U64);

                // Floating point types
                var floatChannel = writer.CreateChannel("DataTypes", "Float", TdmsDataType.SingleFloat);
                var doubleChannel = writer.CreateChannel("DataTypes", "Double", TdmsDataType.DoubleFloat);

                // Other types
                var boolChannel = writer.CreateChannel("DataTypes", "Boolean", TdmsDataType.Boolean);
                var timestampChannel = writer.CreateChannel("DataTypes", "Timestamp", TdmsDataType.TimeStamp);
                var stringChannel = writer.CreateChannel("DataTypes", "String", TdmsDataType.String);

                // Write sample data for each type
                for (int i = 0; i < 10; i++)
                {
                    int8Channel.WriteValue((sbyte)i);
                    int16Channel.WriteValue((short)(i * 100));
                    int32Channel.WriteValue(i * 10000);
                    int64Channel.WriteValue((long)i * 1000000);

                    uint8Channel.WriteValue((byte)i);
                    uint16Channel.WriteValue((ushort)(i * 100));
                    uint32Channel.WriteValue((uint)(i * 10000));
                    uint64Channel.WriteValue((ulong)i * 1000000);

                    floatChannel.WriteValue((float)(i * 0.1));
                    doubleChannel.WriteValue(i * 0.01);

                    boolChannel.WriteValue(i % 2 == 0);
                    timestampChannel.WriteValue(new TdmsTimestamp(DateTime.UtcNow.AddSeconds(i)));
                }

                // String data
                stringChannel.WriteStrings(new[]
                {
                    "First string",
                    "Second string with special chars: üöä",
                    "Third string",
                    "Fourth string with emoji: 🚀",
                    "Fifth and final string"
                });

                writer.WriteSegment();

                Console.WriteLine("✓ Created datatypes_demo.tdms with all supported data types");
                Console.WriteLine("  - 8 integer type channels");
                Console.WriteLine("  - 2 floating point channels");
                Console.WriteLine("  - 1 boolean channel");
                Console.WriteLine("  - 1 timestamp channel");
                Console.WriteLine("  - 1 string channel\n");
            }
        }
    }
}