#!/usr/bin/env bash
# pdf-cb7: the app (the bundle with --bundle) opens a PDF and a CB7 through --read-book, shows the first page and turns one
# page (the "Reader check" log line); the main window screenshots with the book are not blank (mvp-acceptance.md 5.20).
E2E_BROWSER_HEIGHT=
. "$(dirname "$0")/../lib.sh"; e2e_begin
PDF="$FIXTURES/pdf/test.pdf"; CB7="$FIXTURES/cb7/test.cb7"; need "$PDF"; need "$CB7"

open_check() { # <file> <name>
	launch_app --read-book "$1"
	expect_log "Reader check: $(basename "$1"),.*pages" 40
	if printf '%s\n' "$LAST_OUT" | grep -q "FAILED"; then bad "$2: page 2 failed: $LAST_OUT"; fi
	sleep 1
	front_app
	local R; R=$(main_window_id)
	if [ -n "$R" ]; then ok "$2: main window id $R"; shot "$2" "$R"; else bad "$2: no main window"; fi
	check_log "Reader opened: .*$(basename "$1")" "$2: the log records the reader"
	if [ "$(log_count '\[ERROR\]')" = 0 ]; then ok "$2: no ERROR line"; else bad "$2: ERROR lines: $(app_log | grep -F '[ERROR]' | head -2)"; fi
	quit_app
	# a new data folder for the next launch
	rm -rf "$E2E_DATA"; E2E_DATA=$(mktemp -d "${TMPDIR:-/tmp}/e2e-data.XXXXXX"); E2E_DATA=$(cd "$E2E_DATA" && pwd -P)
}
open_check "$PDF" pdf
open_check "$CB7" cb7
e2e_end
