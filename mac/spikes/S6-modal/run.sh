#!/usr/bin/env bash
# Spike S6 (P4.2): the whole matrix in one go. Results in mac/artifacts/s6/ (headless.txt, shutdown-headless.txt, identity/*.txt, real/*.txt, summary.txt).
#   run.sh            stage, headless cases, shutdown case, identity runs, real-window runs (skipped when the screen is locked)
#   run.sh --no-real  without the real-window runs
# Exit 0 when no check failed (a skipped real-window run is not a failure), 1 otherwise.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
SPIKE="$ROOT/mac/spikes/S6-modal"
ART="$ROOT/mac/artifacts/s6"
mkdir -p "$ART"
FAIL=0
SUMMARY="$ART/summary.txt"
: >"$SUMMARY"
note() { printf '%s\n' "$*" | tee -a "$SUMMARY"; }

"$SPIKE/stage.sh" >/dev/null || { echo "build failed" >&2; exit 1; }
APP="$ART/app/S6-modal"

"$APP" --headless --out "$ART/out" >"$ART/headless.txt" 2>&1; rc=$?
note "headless cases: exit $rc, $(grep -c '^PASS' "$ART/headless.txt") PASS, $(grep -c '^FAIL' "$ART/headless.txt") FAIL ($(grep '^RESULT' "$ART/headless.txt"))"
[ $rc -eq 0 ] || FAIL=1

"$APP" --headless --shutdown-case --out "$ART/out" >"$ART/shutdown-headless.txt" 2>&1; rc=$?
if [ $rc -eq 0 ] && grep -q 'ShowDialog returned' "$ART/shutdown-headless.txt"; then note "headless shutdown during a modal dialog: ShowDialog returned and the process exited with code 0"; else note "headless shutdown during a modal dialog: FAIL (exit $rc)"; FAIL=1; fi

"$APP" --interactive --headless --auto --out "$ART/out" >"$ART/interactive-auto.txt" 2>&1; rc=$?
if [ $rc -eq 0 ] && grep -q '^DONE' "$ART/interactive-auto.txt" && grep -q 'RESULT A numbers=7,8,9' "$ART/interactive-auto.txt" && grep -q 'RESULT B numbers=x,x,x' "$ART/interactive-auto.txt" && grep -q 'RESULT C answer=True' "$ART/interactive-auto.txt"; then note "interactive scenario, answered by the autopilot (headless): A=7,8,9 B=unchanged C=True"; else note "interactive scenario headless: FAIL (exit $rc)"; FAIL=1; fi

# An exception that escapes a job on the UI thread: Avalonia aborts the process unless Dispatcher.UnhandledException marks it handled.
( "$APP" --headless --unhandled-case --out "$ART/out" >"$ART/unhandled-nohandler.txt" 2>&1 ) 2>/dev/null; rc1=$?
"$APP" --headless --unhandled-case --with-handler --out "$ART/out" >"$ART/unhandled-handler.txt" 2>&1; rc2=$?
if [ $rc1 -ne 0 ] && [ $rc2 -eq 0 ] && grep -q 'still running afterwards' "$ART/unhandled-handler.txt"; then note "an exception escaping a UI job: without a handler the process aborts (exit $rc1); with Dispatcher.UnhandledException marking it handled the app survives (exit $rc2)"; else note "unhandled-exception experiment: unexpected (exit $rc1 and $rc2)"; FAIL=1; fi

"$SPIKE/identity.sh" all >"$ART/identity.txt" 2>&1 || FAIL=1
while IFS= read -r line; do note "identity $line"; done < <(grep '^  ' "$ART/identity.txt")

if [ "${1:-}" != "--no-real" ]; then
	"$SPIKE/real.sh" all >"$ART/real.txt" 2>&1; rc=$?
	if [ $rc -eq 4 ]; then note "real window runs: SKIPPED (screen locked, ui-drive exit 4)"; elif [ $rc -eq 0 ]; then note "real window runs: $(grep '^REAL RESULT' "$ART/real.txt")"; else note "real window runs: FAIL ($(grep -c '^FAIL' "$ART/real.txt") failures, see $ART/real.txt)"; FAIL=1; fi
fi
note "S6 RESULT: $([ $FAIL -eq 0 ] && echo 'no failure' || echo 'a check failed')"
exit $FAIL
