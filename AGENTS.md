# TDMSSharp Test Run Guide

This repository has two test layers:
- Standard C# usage tests
- C# writer -> Python `nptdms` interoperability tests

## Prerequisites

- .NET SDK (for `dotnet test`)
- Python 3

## One-time Python setup (interop tests)

Run from repository root (`TDMSSharp/`):

```bash
python3 -m venv .venv
.venv/bin/python -m pip install --upgrade pip
.venv/bin/python -m pip install numpy
```

Note:
- The interop tests use the vendored `scripts/npTDMS` package via `PYTHONPATH`.
- Only `numpy` needs to be installed in the venv.

## Run only nptdms interoperability tests

```bash
TDMS_PYTHON="$(pwd)/.venv/bin/python" dotnet test TDMSSharp.UsageTests/TDMSSharp.UsageTests.csproj --filter "FullyQualifiedName~NptdmsInteropTests"
```

## Run full usage test suite (including interop tests)

```bash
TDMS_PYTHON="$(pwd)/.venv/bin/python" dotnet test TDMSSharp.UsageTests/TDMSSharp.UsageTests.csproj
```

## Why `TDMS_PYTHON`?

`NptdmsInteropTests` looks for:
1. `TDMS_PYTHON` (if set), then
2. `python3`, then
3. `python`

Set `TDMS_PYTHON` to ensure tests run with the intended virtual environment.

## Troubleshooting

- `ModuleNotFoundError: No module named 'numpy'`
  - Install numpy in the venv and ensure `TDMS_PYTHON` points to that venv's python.
- Interop test failures with validation mismatches
  - These are real format/read compatibility issues between TDMSSharp output and `nptdms`.
