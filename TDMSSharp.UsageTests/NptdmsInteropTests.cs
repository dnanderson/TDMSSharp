using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TdmsSharp;
using Xunit;

public class NptdmsInteropTests
{
    [Fact]
    public void CSharpWriter_EmptyFile_IsReadableByNpTdms()
    {
        RunInteropTest(
            writer => { },
            new
            {
                expected_segment_count = 1,
                groups = Array.Empty<object>()
            });
    }

    [Fact]
    public void CSharpWriter_MetadataOnlyChannel_IsReadableByNpTdms()
    {
        RunInteropTest(
            writer =>
            {
                writer.CreateChannel("Metadata", "NoDataYet", TdmsDataType.I32);
            },
            new
            {
                expected_segment_count = 1,
                groups = new[]
                {
                    new
                    {
                        name = "Metadata",
                        channels = new[]
                        {
                            new
                            {
                                name = "NoDataYet",
                                values = Array.Empty<int>()
                            }
                        }
                    }
                }
            });
    }

    [Fact]
    public void CSharpWriter_AllNumericAndBoolTypes_AreReadableByNpTdms()
    {
        RunInteropTest(
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
                writer.CreateChannel("Numeric", "Float32", TdmsDataType.SingleFloat).WriteValues(new float[] { 1.25f, -4.5f, 0.0f });
                writer.CreateChannel("Numeric", "Float64", TdmsDataType.DoubleFloat).WriteValues(new double[] { 1.234567, -7.891011, 0.0 });
                writer.CreateChannel("Numeric", "Boolean", TdmsDataType.Boolean).WriteValues(new bool[] { true, false, true, true });
                writer.WriteSegment();
            },
            new
            {
                float_tolerance = 1e-6,
                expected_segment_count = 1,
                groups = new[]
                {
                    new
                    {
                        name = "Numeric",
                        channels = new object[]
                        {
                            new { name = "I8", dtype = "int8", values = new sbyte[] { -10, 10 } },
                            new { name = "I16", dtype = "int16", values = new short[] { -1000, 1000 } },
                            new { name = "I32", dtype = "int32", values = new int[] { -100000, 100000 } },
                            new { name = "I64", dtype = "int64", values = new long[] { -1000000000, 1000000000 } },
                            new { name = "U8", dtype = "uint8", values = new int[] { 10, 20 } },
                            new { name = "U16", dtype = "uint16", values = new ushort[] { 1000, 2000 } },
                            new { name = "U32", dtype = "uint32", values = new uint[] { 100000, 200000 } },
                            new { name = "U64", dtype = "uint64", values = new ulong[] { 1000000000, 2000000000 } },
                            new { name = "Float32", dtype = "float32", values = new float[] { 1.25f, -4.5f, 0.0f } },
                            new { name = "Float64", dtype = "float64", values = new double[] { 1.234567, -7.891011, 0.0 } },
                            new { name = "Boolean", dtype = "bool", values = new bool[] { true, false, true, true } }
                        }
                    }
                }
            });
    }

    [Fact]
    public void CSharpWriter_StringChannels_AreReadableByNpTdms()
    {
        RunInteropTest(
            writer =>
            {
                var names = writer.CreateChannel("Strings", "Names", TdmsDataType.String);
                names.WriteStrings(new[] { "alpha", "beta", "", "delta", "microvolts_µV" });
                writer.WriteSegment();
            },
            new
            {
                expected_segment_count = 1,
                groups = new[]
                {
                    new
                    {
                        name = "Strings",
                        channels = new[]
                        {
                            new
                            {
                                name = "Names",
                                dtype = "object",
                                values = new[] { "alpha", "beta", "", "delta", "microvolts_µV" }
                            }
                        }
                    }
                }
            });
    }

    [Fact]
    public void CSharpWriter_MultiSegmentAppend_IsReadableByNpTdms()
    {
        RunInteropTest(
            writer =>
            {
                var wave = writer.CreateChannel("Acquisition", "Waveform", TdmsDataType.I16);
                var state = writer.CreateChannel("Acquisition", "State", TdmsDataType.Boolean);

                wave.WriteValues(new short[] { 1, 2, 3 });
                state.WriteValues(new bool[] { true, false, true });
                writer.WriteSegment();

                wave.WriteValues(new short[] { 4, 5, 6, 7 });
                state.WriteValues(new bool[] { false, false, true, true });
                writer.WriteSegment();
            },
            new
            {
                expected_segment_count = 2,
                groups = new[]
                {
                    new
                    {
                        name = "Acquisition",
                        channels = new object[]
                        {
                            new { name = "Waveform", dtype = "int16", values = new short[] { 1, 2, 3, 4, 5, 6, 7 } },
                            new { name = "State", dtype = "bool", values = new bool[] { true, false, true, false, false, true, true } }
                        }
                    }
                }
            });
    }

    [Fact]
    public void CSharpWriter_FileGroupChannelProperties_AreReadableByNpTdms()
    {
        RunInteropTest(
            writer =>
            {
                writer.SetFileProperty("author", "tdmssharp-tests");
                writer.SetFileProperty("run_id", 42);
                writer.SetFileProperty("gain", 2.5);
                writer.SetFileProperty("is_calibrated", true);

                var group = writer.CreateGroup("Props");
                group.SetProperty("description", "group metadata");
                group.SetProperty("group_index", 3);

                var channel = writer.CreateChannel("Props", "Measurement", TdmsDataType.DoubleFloat);
                channel.SetProperty("unit", "V");
                channel.SetProperty("resolution_bits", 16);
                channel.WriteValues(new[] { 0.5, 1.5, 2.5 });
                writer.WriteSegment();
            },
            new
            {
                float_tolerance = 1e-9,
                expected_segment_count = 1,
                root_properties = new Dictionary<string, object>
                {
                    ["author"] = "tdmssharp-tests",
                    ["run_id"] = 42,
                    ["gain"] = 2.5,
                    ["is_calibrated"] = true
                },
                groups = new[]
                {
                    new
                    {
                        name = "Props",
                        properties = new Dictionary<string, object>
                        {
                            ["description"] = "group metadata",
                            ["group_index"] = 3
                        },
                        channels = new[]
                        {
                            new
                            {
                                name = "Measurement",
                                dtype = "float64",
                                values = new[] { 0.5, 1.5, 2.5 },
                                properties = new Dictionary<string, object>
                                {
                                    ["unit"] = "V",
                                    ["resolution_bits"] = 16
                                }
                            }
                        }
                    }
                }
            });
    }

    [Fact]
    public void CSharpWriter_MultiGroupBatchSegmentApi_IsReadableByNpTdms()
    {
        RunInteropTest(
            writer =>
            {
                var g1c1 = writer.CreateChannel("Group1", "A", TdmsDataType.I32);
                var g1c2 = writer.CreateChannel("Group1", "B", TdmsDataType.SingleFloat);
                var g2c1 = writer.CreateChannel("Group2", "C", TdmsDataType.String);

                writer.WriteSegment(new ChannelData[]
                {
                    new ChannelData<int>(g1c1, new[] { 1, 2, 3, 4 }),
                    new ChannelData<float>(g1c2, new[] { 1.1f, 2.2f, 3.3f }),
                    new StringChannelData(g2c1, new[] { "x", "y", "z" })
                });
            },
            new
            {
                float_tolerance = 1e-6,
                expected_segment_count = 1,
                groups = new object[]
                {
                    new
                    {
                        name = "Group1",
                        channels = new object[]
                        {
                            new { name = "A", dtype = "int32", values = new[] { 1, 2, 3, 4 } },
                            new { name = "B", dtype = "float32", values = new[] { 1.1f, 2.2f, 3.3f } }
                        }
                    },
                    new
                    {
                        name = "Group2",
                        channels = new object[]
                        {
                            new { name = "C", dtype = "object", values = new[] { "x", "y", "z" } }
                        }
                    }
                }
            });
    }

    private static void RunInteropTest(Action<TdmsFileWriter> writeAction, object expected)
    {
        var tdmsPath = Path.GetTempFileName();
        var expectedPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_expected.json");

        try
        {
            using (var writer = new TdmsFileWriter(tdmsPath))
            {
                writeAction(writer);
            }

            File.WriteAllText(
                expectedPath,
                JsonSerializer.Serialize(expected, new JsonSerializerOptions { WriteIndented = true }));

            RunPythonValidator(tdmsPath, expectedPath);
        }
        finally
        {
            TryDelete(tdmsPath);
            TryDelete(tdmsPath + "_index");
            TryDelete(expectedPath);
        }
    }

    private static void RunPythonValidator(string tdmsPath, string expectedPath)
    {
        var pythonExe = FindPythonExecutable();
        var repoRoot = FindRepoRoot();
        var validatorScript = Path.Combine(repoRoot, "scripts", "validate_tdms_with_nptdms.py");
        var npTdmsPath = Path.Combine(repoRoot, "scripts", "npTDMS");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add(validatorScript);
        process.StartInfo.ArgumentList.Add(tdmsPath);
        process.StartInfo.ArgumentList.Add(expectedPath);

        var envVars = process.StartInfo.EnvironmentVariables;
        var existingPythonPath = envVars.ContainsKey("PYTHONPATH") ? envVars["PYTHONPATH"] : null;
        envVars["PYTHONPATH"] =
            string.IsNullOrEmpty(existingPythonPath)
                ? npTdmsPath
                : npTdmsPath + Path.PathSeparator + existingPythonPath;

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new Xunit.Sdk.XunitException(
                $"nptdms validation failed (exit {process.ExitCode}).\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        }
    }

    private static string FindPythonExecutable()
    {
        var configuredPython = Environment.GetEnvironmentVariable("TDMS_PYTHON");
        if (!string.IsNullOrWhiteSpace(configuredPython))
        {
            if (File.Exists(configuredPython))
            {
                return configuredPython;
            }

            throw new Xunit.Sdk.XunitException(
                $"TDMS_PYTHON was set but does not exist: {configuredPython}");
        }

        foreach (var candidate in new[] { "python3", "python" })
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = candidate,
                        ArgumentList = { "--version" },
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();
                if (process.ExitCode == 0)
                {
                    return candidate;
                }
            }
            catch
            {
                // Try next candidate.
            }
        }

        throw new Xunit.Sdk.XunitException("Python executable not found. Install python3 or python to run nptdms interop tests.");
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var solutionPath = Path.Combine(current.FullName, "TDMSSharp.sln");
            var scriptsPath = Path.Combine(current.FullName, "scripts");
            if (File.Exists(solutionPath) && Directory.Exists(scriptsPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new Xunit.Sdk.XunitException("Could not locate repository root containing TDMSSharp.sln and scripts directory.");
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
