using System;
using System.IO;
using System.Linq;
using TDMSSharp;

namespace TDMSReader
{
    public static class Reader2Tests
    {
        public static void RunAllTests()
        {
            Console.WriteLine("\n=== TDMSREADER2 TESTS ===\n");
            TestStandardFile("example.tdms");
            TestInterleavedFile("interleaved.tdms");
            TestCorruptedFile("example.tdms");
        }

        private static void TestStandardFile(string path)
        {
            Console.WriteLine($"[TEST] Reading standard file: {path}");
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var reader = new TdmsReader2(stream);
                    var file = reader.ReadFile();
                    Assert(file.ChannelGroups.Count > 0, "Standard file should have channel groups.");
                    Console.WriteLine("  [PASS] Standard file read successfully.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL] {ex.Message}");
            }
        }

        private static void TestInterleavedFile(string path)
        {
            Console.WriteLine($"[TEST] Reading interleaved file: {path}");
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var reader = new TdmsReader2(stream);
                    var file = reader.ReadFile();
                    Assert(file.ChannelGroups.Count > 0, "Interleaved file should have channel groups.");
                    var group = file.ChannelGroups.First();
                    Assert(group.Channels.Count > 1, "Interleaved file should have multiple channels.");
                    var channel1 = (TdmsChannel<int>)group.Channels[0];
                    var channel2 = (TdmsChannel<double>)group.Channels[1];
                    Assert(channel1.Data != null && channel2.Data != null && channel1.Data.Length > 0 && channel1.Data.Length == channel2.Data.Length, "Interleaved channels should have same number of values.");
                    Assert(channel1.Data[1] == 1 && channel2.Data[1] == 2.0, "Interleaved data values are incorrect.");
                    Console.WriteLine("  [PASS] Interleaved file read successfully.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL] {ex.Message}");
            }
        }

        private static void TestCorruptedFile(string originalPath)
        {
            var corruptedPath = "corrupted.tdms";
            Console.WriteLine($"[TEST] Reading corrupted file (from {originalPath})");
            try
            {
                var originalBytes = File.ReadAllBytes(originalPath);
                // Corrupt the file by truncating it
                var corruptedBytes = new byte[originalBytes.Length / 2];
                Array.Copy(originalBytes, corruptedBytes, corruptedBytes.Length);
                File.WriteAllBytes(corruptedPath, corruptedBytes);

                using (var stream = File.OpenRead(corruptedPath))
                {
                    var reader = new TdmsReader2(stream);
                    var file = reader.ReadFile();
                    // We expect it to read something, even if it's incomplete, without crashing.
                    Assert(file != null, "Reader should not return null for a corrupted file.");
                    Console.WriteLine("  [PASS] Corrupted file handled gracefully without crashing.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL] {ex.Message}");
            }
            finally
            {
                if (File.Exists(corruptedPath))
                    File.Delete(corruptedPath);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception($"Assertion failed: {message}");
        }
    }
}