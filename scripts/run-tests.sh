#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
dotnet restore ApexPost.sln
dotnet test ApexPost.sln --configuration Release --logger "trx;LogFileName=ApexPost-Tests.trx" --results-directory TestResults
