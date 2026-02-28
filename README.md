# TDMSSharp

TDMSSharp is a .NET library for writing and reading TDMS files (Technical Data Management Streaming), with emphasis on:

- Correctness against TDMS segment/metadata rules
- High-throughput writes for numeric channels
- Incremental metadata and append optimization
- Interoperability validation with `npTDMS`

## Project Status

The project currently targets `net9.0` and includes:

- `TdmsFileWriter` for synchronous TDMS writing
- `AsyncTdmsWriter` for thread-safe queued writes
- `TdmsReader` for reading channels and segment metadata
- Usage tests, including Python `npTDMS` interop tests

## Repository Layout

- `TDMSSharp/` library source
- `TDMSSharp.UsageTests/` xUnit usage and interop tests
- `scripts/` helper scripts and vendored `npTDMS`
- `SPECIFICATION.md` TDMS format/spec reference (including migrated legacy README content)

## Quick Start

### Create and write a file

```csharp
using TdmsSharp;

using var writer = new TdmsFileWriter("sample.tdms");

var wave = writer.CreateChannel("Acquisition", "Waveform", TdmsDataType.I16);
wave.WriteValues(new short[] { 1, 2, 3, 4 });

var names = writer.CreateChannel("Meta", "Names", TdmsDataType.String);
names.WriteStrings(new[] { "alpha", "beta" });

writer.WriteSegment();
```

### Read a file

```csharp
using TdmsSharp;
using System.IO;

using var reader = new TdmsReader(new FileStream("sample.tdms", FileMode.Open, FileAccess.Read, FileShare.Read));
var file = reader.ReadFile();

var data = file.GetGroup("Acquisition")?.GetChannel("Waveform")?.ReadData<short>();
```

## Writing Model

- Data is buffered per channel.
- `WriteSegment()` flushes buffered channel data.
- The writer decides between raw append and creating a new metadata segment based on append eligibility rules.
- String channels are forced to new segments to keep string offset-table/index semantics safe.

## Running Tests

### .NET usage tests

```bash
dotnet test TDMSSharp.UsageTests/TDMSSharp.UsageTests.csproj
```

### Interop tests with vendored `npTDMS`

Set `TDMS_PYTHON` to a Python interpreter with required deps (notably `numpy`), then run tests:

```bash
TDMS_PYTHON="/path/to/python" dotnet test TDMSSharp.UsageTests/TDMSSharp.UsageTests.csproj --filter "FullyQualifiedName~NptdmsInteropTests"
```

## API Documentation

TDMSSharp uses XML documentation comments and DocFX for API reference generation.

Build docs locally:

```bash
./scripts/build-api-docs.sh
```

Generated site entry:

- `docs/_site/index.html`

Doc source files:

- `docs/docfx.json`
- `docs/index.md`
- `docs/articles/getting-started.md`

## Specification and References

- Primary spec/reference document: [SPECIFICATION.md](SPECIFICATION.md)
- Legacy NI TDMS format article previously in README is now preserved inside `SPECIFICATION.md`.

## Limitations and Notes

- Reader property parsing is consumed for alignment, but full property materialization APIs are still limited.
- Interleaved and DAQmx raw-data handling is not yet a complete end-to-end feature set.
- Public APIs and package metadata are still evolving.
