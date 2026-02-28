#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

dotnet tool restore
dotnet build TDMSSharp/TDMSSharp.csproj -c Release
dotnet docfx docs/docfx.json

echo "API docs generated at: $repo_root/docs/_site/index.html"
