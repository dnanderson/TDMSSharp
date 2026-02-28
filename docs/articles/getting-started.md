# Getting Started

## Prerequisites

- .NET SDK 9.0+
- Optional: Python with `numpy` for `npTDMS` interop tests

## Build

```bash
dotnet build TDMSSharp.sln
```

## Test

```bash
dotnet test TDMSSharp.UsageTests/TDMSSharp.UsageTests.csproj
```

For interop tests:

```bash
TDMS_PYTHON="/path/to/python" dotnet test TDMSSharp.UsageTests/TDMSSharp.UsageTests.csproj --filter "FullyQualifiedName~NptdmsInteropTests"
```

## Generate API Docs

```bash
./scripts/build-api-docs.sh
```

Generated site output:

- `docs/_site/index.html`
