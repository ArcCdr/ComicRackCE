#!/usr/bin/env bash
# Spike S6 (P4.2): the real-window runs, driven by mac/tools/ui-drive (UI_DRIVE_EXE points at the staged S6 app).
#   real.sh cases        every case of the app against the real window server (Avalonia.Native), autopilot inside the app
#   real.sh interactive  dialogs that wait for real keys and mouse clicks sent by ui-drive; screenshots of the windows
#   real.sh shutdown     application shutdown while a dialog is open
#   real.sh all          the three, one after the other
# Exit 0 all PASS, 1 a failure, 4 the screen is locked (record the checks as SKIPPED). Output in mac/artifacts/s6/real/.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
APP="$ROOT/mac/artifacts/s6/app/S6-modal"
OUT="$ROOT/mac/artifacts/s6/real"
export UI_DRIVE_DIR="$ROOT/mac/artifacts/s6/ui-drive"
export UI_DRIVE_EXE="$APP"
UD="${S6_UI_DRIVE:-$ROOT/mac/tools/ui-drive/ui-drive.sh}"   # S6_UI_DRIVE: a stand-in for testing this script without a window server
FAILS=0

say() { printf '%s\n' "$*"; }
ok() { say "PASS $*"; }
bad() { say "FAIL $*"; FAILS=$((FAILS + 1)); }

[ -x "$APP" ] || { echo "stage the app first: mac/spikes/S6-modal/stage.sh" >&2; exit 2; }
mkdir -p "$OUT"
"$UD" check >/dev/null 2>&1; rc=$?
if [ $rc -ne 0 ]; then say "SKIP real window runs: screen locked or no GUI access (ui-drive check exit $rc)"; exit 4; fi

wait_exit() { # <seconds>: wait for the app process of the session to end, print its exit code
	local i=0 pid
	pid=$(sed -n "s/^UD_PID='\(.*\)'/\1/p" "$UI_DRIVE_DIR/session.env")
	while kill -0 "$pid" 2>/dev/null && [ $i -lt $(($1 * 5)) ]; do sleep 0.2; i=$((i + 1)); done
	if kill -0 "$pid" 2>/dev/null; then say "app still running after $1 s"; "$UD" kill >/dev/null 2>&1; return 124; fi
	cat "$UI_DRIVE_DIR/app.exit" 2>/dev/null || echo "?"
}

window_id() { # <title regex> -> id of the first matching window
	"$UD" windows | awk -F'\t' -v re="$1" '$6 ~ re { print $1; exit }'
}

wait_window_id() { # <title regex> <seconds> -> id, or nothing after the time is up (polls the window list; dialogs may not be layer 0)
	local i=0 id=""
	while [ $i -lt $(($2 * 3)) ]; do
		id=$(window_id "$1")
		if [ -n "$id" ]; then printf '%s' "$id"; return 0; fi
		sleep 0.33; i=$((i + 1))
	done
	return 1
}

window_field() { # <title regex> <field 4=position, 5=size>
	"$UD" windows | awk -F'\t' -v re="$1" -v f="$2" '$6 ~ re { print $f; exit }'
}

app_out() { cat "$UI_DRIVE_DIR/app.out" 2>/dev/null; }

run_cases() {
	say "== real cases: every case through the real window server"
	"$UD" launch -- --real --out "$OUT/cases" >/dev/null || { say "launch failed ($?)"; return 1; }
	if "$UD" expect-log '^RESULT ' 300 >/dev/null; then :; else say "no RESULT line"; fi
	local code
	code=$(wait_exit 30)
	cp "$UI_DRIVE_DIR/app.out" "$OUT/cases.txt" 2>/dev/null
	grep -E '^(PASS|FAIL|SKIP|INFO|RESULT)' "$OUT/cases.txt" | sed 's/^/  /' | grep -E 'FAIL|SKIP|RESULT'
	local passes fails
	passes=$(grep -c '^PASS ' "$OUT/cases.txt"); fails=$(grep -c '^FAIL ' "$OUT/cases.txt")
	if [ "$fails" = 0 ] && [ "$passes" -gt 0 ] && [ "$code" = 0 ]; then ok "real cases: $passes checks passed, no failure, exit code $code"; else bad "real cases: $passes passed, $fails failed, exit code $code"; fi
}

run_interactive() {
	say "== real interactive: ui-drive keys and clicks into the spike app"
	"$UD" launch -- --interactive --out "$OUT/interactive" >/dev/null || { say "launch failed ($?)"; return 1; }
	"$UD" wait-window "S6 main window" 30 >/dev/null || { bad "the main window did not appear"; "$UD" kill >/dev/null 2>&1; return 1; }
	"$UD" front >/dev/null
	local main
	main=$(window_id "S6 main window")
	"$UD" shot S6-main --window "$main" >/dev/null

	# A: Autonumber from the UI thread. The owner must not take a click while the dialog is open; Return accepts.
	"$UD" expect-log 'READY A' 60 >/dev/null || { bad "A: dialog did not appear"; "$UD" kill >/dev/null 2>&1; return 1; }
	local dlg
	dlg=$(wait_window_id "Autonumber Wizard" 20) || bad "A: no window titled Autonumber Wizard"
	"$UD" front >/dev/null
	"$UD" shot S6-A-autonumber --window "$dlg" >/dev/null && ok "A: screenshot of the dialog ($OUT/../ui-drive/shots/S6-A-autonumber.png)"
	# The owner's button is at client (20..160, 60..90); the title bar is part of the window frame (about 28 points).
	"$UD" click 40 103 --window "$main" >/dev/null 2>&1; local crc=$?
	sleep 1
	if app_out | grep -q '^MAIN-CLICK'; then bad "A: the owner window took a click while the modal dialog was open (click exit $crc)"; else ok "A: a click on the owner window during the modal dialog was not handled (ui-drive click exit $crc; no MAIN-CLICK within 1 s)"; fi
	"$UD" front >/dev/null
	"$UD" key return >/dev/null || bad "A: key return refused"
	"$UD" expect-log 'RESULT A numbers=' 20 >/dev/null || bad "A: no RESULT"
	if app_out | grep -q 'RESULT A numbers=7,8,9'; then ok "A: Return accepted the dialog (AcceptButton); the books were numbered 7, 8, 9"; else bad "A: unexpected result: $(app_out | grep 'RESULT A')"; fi

	# B: Autonumber from a worker thread; Escape cancels.
	"$UD" expect-log 'READY B' 20 >/dev/null || bad "B: dialog did not appear"
	dlg=$(wait_window_id "Autonumber Wizard" 20) || bad "B: no window"
	"$UD" front >/dev/null
	"$UD" shot S6-B-autonumber-worker --window "$dlg" >/dev/null && ok "B: screenshot of the dialog shown from a worker thread"
	"$UD" key escape >/dev/null || bad "B: key escape refused"
	"$UD" expect-log 'RESULT B numbers=' 20 >/dev/null || bad "B: no RESULT"
	if app_out | grep -q 'RESULT B numbers=x,x,x'; then ok "B: Escape cancelled (CancelButton); the books are unchanged and the worker thread continued"; else bad "B: unexpected result: $(app_out | grep 'RESULT B')"; fi

	# C: the shutdown script's Yes/No message box from a worker thread; Return takes Yes.
	"$UD" expect-log 'READY C' 20 >/dev/null || bad "C: message box did not appear"
	local box
	box=$(wait_window_id "Shutdown Script" 20) || bad "C: no window titled Shutdown Script"
	"$UD" front >/dev/null
	"$UD" shot S6-C-messagebox --window "$box" >/dev/null && ok "C: screenshot of the message box"
	"$UD" key return >/dev/null || bad "C: key return refused"
	"$UD" expect-log 'RESULT C answer=' 20 >/dev/null || bad "C: no RESULT"
	if app_out | grep -q 'RESULT C answer=True'; then ok "C: Return chose Yes; the script returned True to its worker thread"; else bad "C: unexpected result: $(app_out | grep 'RESULT C')"; fi

	# D: the owner-draw panel; check that the Python OnPaint drawing is in the real window.
	"$UD" expect-log 'READY D' 20 >/dev/null || bad "D: panel did not appear"
	local pnl size h
	pnl=$(wait_window_id "S6 owner-draw panel" 20) || bad "D: no panel window"
	"$UD" shot S6-D-ownerdraw --window "$pnl" >/dev/null && ok "D: screenshot of the owner-draw panel"
	local png="$UI_DRIVE_DIR/shots/S6-D-ownerdraw.png" px
	size=$(window_field "S6 owner-draw panel" 5); h=${size#*x}
	# device pixels: the client area is 130 points high at the bottom of the frame; the swatch rectangle is at client (30..80, 30..70).
	local scale=2 top
	top=$(( (h - 130) * scale ))
	px=$("$UD" probe "$png" $(( 55 * scale )) $(( top + 50 * scale )) --box 5 2>/dev/null | tr '\n' ' ')
	say "  swatch pixel (mean of 5x5): $px"
	if printf '%s' "$px" | grep -Eq 'rgba: *\(?(21[0-9]|22[0-9]), *(2[0-9]|3[0-9]|4[0-9]), *(2[0-9]|3[0-9]|4[0-9])'; then ok "D: the red rectangle that the Python OnPaint drew is in the real window"; else bad "D: swatch pixel is not the expected red: $px"; fi
	"$UD" expect-log '^DONE' 30 >/dev/null || bad "D: scenario did not finish"
	local code
	code=$(wait_exit 20)
	cp "$UI_DRIVE_DIR/app.out" "$OUT/interactive.txt" 2>/dev/null
	if [ "$code" = 0 ]; then ok "the app exited with code 0 after the scenario"; else bad "app exit code $code"; fi
}

run_shutdown() {
	say "== real shutdown: the application is asked to shut down while a dialog is open"
	"$UD" launch -- --real --shutdown-case --out "$OUT/shutdown" >/dev/null || { say "launch failed ($?)"; return 1; }
	"$UD" expect-log 'SHUTDOWN dialog is open' 30 >/dev/null || { bad "shutdown: dialog did not appear"; "$UD" kill >/dev/null 2>&1; return 1; }
	local code
	code=$(wait_exit 25)
	cp "$UI_DRIVE_DIR/app.out" "$OUT/shutdown.txt" 2>/dev/null
	grep '^SHUTDOWN' "$OUT/shutdown.txt" | sed 's/^/  /'
	if grep -q 'ShowDialog returned' "$OUT/shutdown.txt" && [ "$code" = 0 ]; then ok "shutdown: Shutdown() ended the nested frame (ShowDialog returned) and the process exited with code 0"; else bad "shutdown: exit code $code; see $OUT/shutdown.txt"; fi
}

case "${1:-all}" in
	cases) run_cases ;;
	interactive) run_interactive ;;
	shutdown) run_shutdown ;;
	all) run_cases; run_interactive; run_shutdown ;;
	*) echo "usage: real.sh cases|interactive|shutdown|all" >&2; exit 2 ;;
esac
say "REAL RESULT: $([ $FAILS = 0 ] && echo 'no failures' || echo "$FAILS failed")"
[ $FAILS = 0 ]
