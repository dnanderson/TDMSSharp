using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TDMSSharp;

namespace TDMSReader
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string filePath = "example.tdms";

            Console.WriteLine($"Attempting to read TDMS file at: {Path.GetFullPath(filePath)}");

            if (!File.Exists(filePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nError: The file '{filePath}' was not found.");
                Console.ResetColor();
                return;
            }

            try
            {
                Console.WriteLine("\n=== EFFICIENT TDMS FILE READING DEMO ===\n");

                // Method 1: Traditional full load (for comparison)
                await DemoTraditionalReading(filePath);

                // Method 2: Lazy loading with metadata only
                await DemoLazyMetadataReading(filePath);

                // Method 3: Targeted channel reading
                await DemoTargetedChannelReading(filePath);

                // Method 4: Streaming data processing
                await DemoStreamingProcessing(filePath);

                // Method 5: Advanced channel operations
                await DemoAdvancedChannelOperations(filePath);

                // Method 6: Performance comparison
                await DemoPerformanceComparison(filePath);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"An error occurred: {ex.Message}");
                Console.ResetColor();
                Console.WriteLine(ex.StackTrace);
            }
        }

        static async Task DemoTraditionalReading(string filePath)
        {
            Console.WriteLine("1. TRADITIONAL READING (Full Load)");
            Console.WriteLine("=====================================");
            
            var sw = Stopwatch.StartNew();
            var tdmsFile = TdmsFile.Open(filePath);
            sw.Stop();
            
            Console.WriteLine($"  Load time: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"  Memory before GC: {GC.GetTotalMemory(false) / 1024.0 / 1024.0:F2} MB");
            
            DisplayFileInfo(tdmsFile);
            
            // Clean up for next demo
            tdmsFile.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            Console.WriteLine();
        }

        static async Task DemoLazyMetadataReading(string filePath)
        {
            Console.WriteLine("2. LAZY METADATA READING");
            Console.WriteLine("========================");
            
            var options = new TdmsReadOptions
            {
                LazyLoad = true,
                MetadataOnly = true
            };
            
            var sw = Stopwatch.StartNew();
            var tdmsFile = TdmsFile.Open(filePath, options);
            sw.Stop();
            
            Console.WriteLine($"  Metadata load time: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"  Memory usage: {GC.GetTotalMemory(false) / 1024.0 / 1024.0:F2} MB");
            
            // Display structure without loading data
            foreach (var group in tdmsFile.ChannelGroups)
            {
                Console.WriteLine($"  Group: {group.Path}");
                foreach (var channel in group.Channels)
                {
                    Console.WriteLine($"    Channel: {channel.Path}");
                    Console.WriteLine($"      Samples: {channel.NumberOfValues:N0}");
                    Console.WriteLine($"      Type: {channel.DataType}");
                    
                    // Show properties if any
                    if (channel.Properties.Any())
                    {
                        Console.WriteLine("      Properties:");
                        foreach (var prop in channel.Properties.Take(3))
                        {
                            Console.WriteLine($"        - {prop.Name}: {prop.Value}");
                        }
                    }
                }
            }
            
            tdmsFile.Dispose();
            Console.WriteLine();
        }

        static async Task DemoTargetedChannelReading(string filePath)
        {
            Console.WriteLine("3. TARGETED CHANNEL READING");
            Console.WriteLine("===========================");
            
            // First, get channel list with metadata-only read
            var metaOptions = new TdmsReadOptions { MetadataOnly = true };
            var metaFile = TdmsFile.Open(filePath, metaOptions);
            
            // Find first channel
            var firstChannel = metaFile.ChannelGroups.FirstOrDefault()?.Channels.FirstOrDefault();
            if (firstChannel == null)
            {
                Console.WriteLine("  No channels found");
                return;
            }
            
            var channelPath = firstChannel.Path;
            var totalSamples = firstChannel.NumberOfValues;
            metaFile.Dispose();
            
            Console.WriteLine($"  Target channel: {channelPath}");
            Console.WriteLine($"  Total samples: {totalSamples:N0}");
            
            // Now read with lazy loading
            var options = new TdmsReadOptions { LazyLoad = true };
            var tdmsFile = TdmsFile.Open(filePath, options);
            
            // Load specific range
            if (totalSamples > 100)
            {
                var sw = Stopwatch.StartNew();
                var rangeData = await tdmsFile.LoadChannelRangeAsync(channelPath, 0, Math.Min(100, (long)totalSamples));
                sw.Stop();
                
                Console.WriteLine($"  Loaded first 100 samples in: {sw.ElapsedMilliseconds}ms");
                
                if (rangeData.Length > 0)
                {
                    Console.WriteLine($"  First value: {rangeData.GetValue(0)}");
                    Console.WriteLine($"  Last value: {rangeData.GetValue(rangeData.Length - 1)}");
                }
            }
            
            tdmsFile.Dispose();
            Console.WriteLine();
        }

        static async Task DemoStreamingProcessing(string filePath)
        {
            Console.WriteLine("4. STREAMING DATA PROCESSING");
            Console.WriteLine("============================");
            
            var tdmsFile = TdmsFile.Open(filePath);
            var firstChannel = tdmsFile.ChannelGroups.FirstOrDefault()?.Channels.FirstOrDefault();
            
            if (firstChannel == null || firstChannel.NumberOfValues == 0)
            {
                Console.WriteLine("  No data to stream");
                return;
            }
            
            Console.WriteLine($"  Streaming channel: {firstChannel.Path}");
            Console.WriteLine($"  Data type: {firstChannel.DataType}");
            
            // Stream process based on data type
            if (firstChannel.DataType == TdsDataType.DoubleFloat)
            {
                await ProcessDoubleStream(firstChannel);
            }
            else if (firstChannel.DataType == TdsDataType.SingleFloat)
            {
                await ProcessFloatStream(firstChannel);
            }
            else if (firstChannel.DataType == TdsDataType.I32)
            {
                await ProcessIntStream(firstChannel);
            }
            else
            {
                Console.WriteLine($"  Streaming demo not implemented for type: {firstChannel.DataType}");
            }
            
            tdmsFile.Dispose();
            Console.WriteLine();
        }

        static async Task ProcessDoubleStream(TdmsChannel channel)
        {
            int batchCount = 0;
            double globalMin = double.MaxValue;
            double globalMax = double.MinValue;
            double globalSum = 0;
            long globalCount = 0;
            
            await foreach (var batch in channel.StreamData<double>(batchSize: 1000))
            {
                batchCount++;
                var batchMin = batch.Min();
                var batchMax = batch.Max();
                var batchAvg = batch.Average();
                
                globalMin = Math.Min(globalMin, batchMin);
                globalMax = Math.Max(globalMax, batchMax);
                globalSum += batch.Sum();
                globalCount += batch.Length;
                
                if (batchCount <= 3)
                {
                    Console.WriteLine($"    Batch {batchCount}: {batch.Length} samples, " +
                                    $"Min={batchMin:F3}, Max={batchMax:F3}, Avg={batchAvg:F3}");
                }
            }
            
            Console.WriteLine($"  Processed {batchCount} batches");
            Console.WriteLine($"  Global stats: Min={globalMin:F3}, Max={globalMax:F3}, " +
                            $"Avg={globalSum/globalCount:F3}");
        }

        static async Task ProcessFloatStream(TdmsChannel channel)
        {
            int batchCount = 0;
            await foreach (var batch in channel.StreamData<float>(batchSize: 1000))
            {
                batchCount++;
                if (batchCount <= 3)
                {
                    Console.WriteLine($"    Batch {batchCount}: {batch.Length} samples");
                }
            }
            Console.WriteLine($"  Processed {batchCount} batches");
        }

        static async Task ProcessIntStream(TdmsChannel channel)
        {
            int batchCount = 0;
            await foreach (var batch in channel.StreamData<int>(batchSize: 1000))
            {
                batchCount++;
                if (batchCount <= 3)
                {
                    Console.WriteLine($"    Batch {batchCount}: {batch.Length} samples");
                }
            }
            Console.WriteLine($"  Processed {batchCount} batches");
        }

        static async Task DemoAdvancedChannelOperations(string filePath)
        {
            Console.WriteLine("5. ADVANCED CHANNEL OPERATIONS");
            Console.WriteLine("==============================");
            var metaOptions = new TdmsReadOptions { LazyLoad = true };
            var tdmsFile = TdmsFile.Open(filePath, metaOptions);
            var channel = tdmsFile.ChannelGroups.LastOrDefault()?.Channels.LastOrDefault();
            
            if (channel == null || channel.NumberOfValues < 10)
            {
                Console.WriteLine("  Not enough data for advanced operations");
                return;
            }
            
            // Channel view for efficient access
            if (channel.DataType == TdsDataType.DoubleFloat)
            {
                var view = channel.AsView<double>();
                
                // Random access
                Console.WriteLine($"  Random access demonstration:");
                Console.WriteLine($"    Sample[0] = {view[0]}");
                Console.WriteLine($"    Sample[5] = {view[5]}");
                if (view.Length > 10)
                    Console.WriteLine($"    Sample[10] = {view[10]}");
                
                // Statistics
                if (view.Length > 100)
                {
                    var stats = view.ComputeStatistics();
                    Console.WriteLine($"  Statistics:");
                    Console.WriteLine($"    Count: {stats.Count:N0}");
                    Console.WriteLine($"    Mean: {stats.Mean:F3}");
                    Console.WriteLine($"    StdDev: {stats.StdDev:F3}");
                    Console.WriteLine($"    Min: {stats.Min:F3}");
                    Console.WriteLine($"    Max: {stats.Max:F3}");
                }
                
                // Decimation for visualization
                if (view.Length > 20)
                {
                    var decimated = view.Decimate(10);
                    Console.WriteLine($"  Decimated to 10 points: [{string.Join(", ", decimated.Take(5).Select(v => v.ToString("F2")))}...]");
                }
            }
            
            // Windowed analysis
            if (channel.NumberOfValues > 100)
            {
                Console.WriteLine($"  Windowed analysis:");
                int windowCount = 0;
                foreach (var stats in channel.WindowedStatistics<double>(windowSize: 50, stepSize: 25).Take(5))
                {
                    windowCount++;
                    Console.WriteLine($"    Window {windowCount}: Mean={stats.Mean:F3}, StdDev={stats.StdDev:F3}");
                }
            }
            
            tdmsFile.Dispose();
            Console.WriteLine();
        }

        static async Task DemoPerformanceComparison(string filePath)
        {
            Console.WriteLine("6. PERFORMANCE COMPARISON");
            Console.WriteLine("=========================");
            
            // Traditional full load
            GC.Collect();
            var memBefore = GC.GetTotalMemory(false);
            var sw = Stopwatch.StartNew();
            var fullFile = TdmsFile.Open(filePath);
            sw.Stop();
            var memAfterFull = GC.GetTotalMemory(false);
            var fullLoadTime = sw.ElapsedMilliseconds;
            var fullMemory = (memAfterFull - memBefore) / 1024.0 / 1024.0;
            fullFile.Dispose();
            
            // Lazy load with metadata only
            GC.Collect();
            memBefore = GC.GetTotalMemory(false);
            sw.Restart();
            var lazyFile = TdmsFile.Open(filePath, new TdmsReadOptions { LazyLoad = true, MetadataOnly = true });
            sw.Stop();
            var memAfterLazy = GC.GetTotalMemory(false);
            var lazyLoadTime = sw.ElapsedMilliseconds;
            var lazyMemory = (memAfterLazy - memBefore) / 1024.0 / 1024.0;
            
            Console.WriteLine($"  Full Load:");
            Console.WriteLine($"    Time: {fullLoadTime}ms");
            Console.WriteLine($"    Memory: {fullMemory:F2} MB");
            
            Console.WriteLine($"  Lazy Load (metadata only):");
            Console.WriteLine($"    Time: {lazyLoadTime}ms");
            Console.WriteLine($"    Memory: {lazyMemory:F2} MB");
            
            if (fullLoadTime > 0 && lazyLoadTime > 0)
            {
                Console.WriteLine($"  Speedup: {fullLoadTime / (double)lazyLoadTime:F1}x");
                Console.WriteLine($"  Memory reduction: {(1 - lazyMemory/fullMemory) * 100:F0}%");
            }
            
            lazyFile.Dispose();
            Console.WriteLine();
        }

        static void DisplayFileInfo(TdmsFile file)
        {
            Console.WriteLine($"  File Properties: {file.Properties.Count}");
            foreach (var prop in file.Properties.Take(3))
            {
                Console.WriteLine($"    - {prop.Name}: {prop.Value}");
            }
            
            Console.WriteLine($"  Groups: {file.ChannelGroups.Count}");
            long totalChannels = 0;
            ulong totalSamples = 0;
            
            foreach (var group in file.ChannelGroups)
            {
                totalChannels += group.Channels.Count;
                foreach (var channel in group.Channels)
                {
                    totalSamples += channel.NumberOfValues;
                }
            }
            
            Console.WriteLine($"  Total Channels: {totalChannels}");
            Console.WriteLine($"  Total Samples: {totalSamples:N0}");
        }
    }
}