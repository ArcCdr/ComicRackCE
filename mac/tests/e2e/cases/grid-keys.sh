#!/usr/bin/env bash
# grid-keys: arrow keys, Home and End move the grid selection and Enter opens the selected book in a tab of the main window
# (mvp-acceptance.md 4.10, 4.11; P5.32a: the reader is a tab of the main window, Cmd+W closes the tab). The books of mac/fixtures/small are sorted by series ("Fixture Series 01 001" is first).
. "$(dirname "$0")/../lib.sh"; e2e_begin
SMALL="$FIXTURES/small"; need "$SMALL"

launch_app
scan_folder "$SMALL" 90
sleep 2

# open_with <keys...> <expected file name regex>: moves with the keys (the grid has the focus after a click), Enter, expects the book.
open_with() {
	local expect=${!#} keys=("${@:1:$#-1}") k before opened=$(log_count 'Reader opened')
	front_app
	must click 700 462   # a spot in the grid: focuses it
	for k in "${keys[@]}"; do must key "$k"; sleep 0.2; done
	before=$(log_count "Open book requested")
	must key return
	sleep 1.2
	local line
	line=$(app_log | grep -F "Open book requested" | tail -1)
	if [ "$(log_count 'Open book requested')" -gt "$before" ] && printf '%s\n' "$line" | grep -q -E -- "$expect"; then ok "${keys[*]} + Enter opened: ${line##*/}"; else bad "${keys[*]} + Enter: expected /$expect/, log says: $line"; fi
	if [ "$(log_count 'Reader opened')" -gt "$opened" ]; then ok "a book tab was opened in the main window"; else bad "no 'Reader opened' line after Enter"; fi
	R=$(main_window_id)
	if [ -n "$R" ]; then ok "the main window shows the book (title: $("$UI" windows 2>/dev/null | awk -F'\t' -v id="$R" '$1 == id { print $6; exit }'))"; else bad "no main window"; fi
	LAST_SHOT_WIN=$R
}

open_with home 'Fixture Series 01 001\.cbz'
shot reader-first "$LAST_SHOT_WIN"
press cmd+w; sleep 1
if [ "$(log_count 'Reader closed')" -ge 1 ]; then ok "Cmd+W closed the book tab"; else bad "Cmd+W did not close the tab"; fi
open_with home right right 'Fixture Series 01 003\.cbz'
press cmd+w; sleep 1
open_with home right right left 'Fixture Series 01 002\.cbz'
press cmd+w; sleep 1
open_with end 'Fixture Series 10 005\.cbz'
press cmd+w; sleep 1
# Down moves by one row (one row has several columns, so it lands on a later book than Right).
open_with home down 'Fixture Series (0[1-9]|10) 0'
quit_app
e2e_end
