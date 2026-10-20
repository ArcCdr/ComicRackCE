#!/usr/bin/env bash
# Builds the S6 spike and stages a patched copy in mac/artifacts/s6/app (same IronPython patch as the S4 spike).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
SPIKE="$ROOT/mac/spikes/S6-modal"
OUT="$ROOT/mac/artifacts/s6/app"
dotnet build "$SPIKE/App" -c Debug -v q --nologo >/dev/null
dotnet build "$ROOT/mac/spikes/S4-patch" -c Release -v q --nologo >/dev/null
rm -rf "$OUT"
mkdir -p "$ROOT/mac/artifacts/s6"
cp -R "$SPIKE/App/bin/Debug/net10.0/osx-arm64" "$OUT"
dotnet "$ROOT/mac/spikes/S4-patch/bin/Release/net10.0/patch-ironpython.dll" "$SPIKE/App/bin/Debug/net10.0/osx-arm64/IronPython.dll" "$OUT/IronPython.dll" >/dev/null
echo "$OUT"
