#!/usr/bin/env bash
# Spike S4 (P4.1). Builds the console, makes a copy of its output in mac/artifacts/s4/run-<variant> and runs selftest.py there.
#   run.sh orig            IronPython 2.7.12 as shipped
#   run.sh patched         IronPython.dll patched by mac/spikes/S4-patch (NewTypeMaker.ImplementCTDOverride skips default interface methods)
# (the variants patched-resolver and patched-rewrite were identity experiments; since P4.8 the stand-ins carry the names of the originals and Sample.py runs unmodified)
# The Python probe and Sample.py are read from this folder and from ComicRack/Output/Scripts. Output goes to stdout.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
VARIANT="${1:-patched}"
SPIKE="$ROOT/mac/spikes/S4-ironpython"
OUT="$ROOT/mac/artifacts/s4/run-$VARIANT"
dotnet build "$SPIKE" -c Debug -v q --nologo >/dev/null
rm -rf "$OUT"
mkdir -p "$ROOT/mac/artifacts/s4"
cp -R "$SPIKE/bin/Debug/net10.0/osx-arm64" "$OUT"
case "$VARIANT" in
	orig) ;;
	patched)
		dotnet build "$ROOT/mac/spikes/S4-patch" -c Release -v q --nologo >/dev/null
		dotnet "$ROOT/mac/spikes/S4-patch/bin/Release/net10.0/patch-ironpython.dll" "$SPIKE/bin/Debug/net10.0/osx-arm64/IronPython.dll" "$OUT/IronPython.dll" >/dev/null
		;;
	*) echo "unknown variant $VARIANT" >&2; exit 2 ;;
esac
export COMICRACK_SCRIPTS_DIR="$ROOT/ComicRack/Output/Scripts"
dotnet "$OUT/S4-ironpython.dll" "$SPIKE/selftest.py"
