#!/usr/bin/env bash
# Regenerates the typed C# client from contracts/v2.json.
# The normal path is simply building ApexPost.Client: its OpenApiReference runs NSwag on every build.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
dotnet build src/ApexPost.Client/ApexPost.Client.csproj -c Debug
echo "Generated client written under src/ApexPost.Client/obj/ (regenerated on every build, never edited by hand)"

# Optional: produce the same output with the NSwag CLI and nswag.json.
if command -v nswag >/dev/null 2>&1; then
  nswag run nswag.json
fi
