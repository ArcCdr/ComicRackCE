#!/usr/bin/env bash
# P4.8: runs the assembly identity probe (mac/scripting/identity-probe.py) and the unmodified scripts of the program through the --script-selftest option of a built bundle,
# in the hardened runtime of the signed process: names of the originals resolve to the facades and the Core, Sample.py loads and its headless hooks run.
#   mac/tools/bundle-selftest.sh [path/to/App.app]     (default: the bundle that mac/tools/make-app.sh builds)
# Exit 0: no FAIL line; 1: a FAIL line or no PASS line; 2: no bundle. No window opens and no data folder is touched.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APP="${1:-$ROOT/mac/artifacts/ComicRack CE (MVP).app}"
EXE="$APP/Contents/MacOS/ComicRack.Mac"
if [ ! -x "$EXE" ]; then
	echo "bundle-selftest: no bundle at $APP (run mac/tools/make-app.sh first)" >&2
	exit 2
fi
OUT="$(mktemp -d "${TMPDIR:-/tmp}/crmac-bundle-selftest.XXXXXX")"
trap 'rm -rf "$OUT"' EXIT
COMICRACK_ENABLE_SELFTEST=1 COMICRACK_SCRIPTS_DIR="$APP/Contents/MacOS/Scripts" "$EXE" --script-selftest "$ROOT/mac/scripting/identity-probe.py" >"$OUT/out.txt" 2>&1
rc=$?
pass=$(grep -c '^PASS ' "$OUT/out.txt" || true)
fail=$(grep -c '^FAIL ' "$OUT/out.txt" || true)
grep -E '^(FAIL|INFO result)' "$OUT/out.txt" || true
echo "bundle-selftest: $pass PASS, $fail FAIL, exit $rc ($APP)"
if [ "$rc" -ne 0 ] || [ "$fail" -ne 0 ] || [ "$pass" -eq 0 ]; then
	tail -20 "$OUT/out.txt" >&2
	exit 1
fi
exit 0
