#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BASE_URL="${APEXPOST_OPENAPI_BASE_URL:-https://localhost:7001}"
cd "$ROOT"
mkdir -p contracts
curl --fail --show-error --silent "$BASE_URL/openapi/v1.json" --output contracts/v1.json
curl --fail --show-error --silent "$BASE_URL/openapi/v2.json" --output contracts/v2.json
python -m json.tool contracts/v1.json >/dev/null
python -m json.tool contracts/v2.json >/dev/null
printf 'Refreshed contracts/v1.json and contracts/v2.json from %s\n' "$BASE_URL"
