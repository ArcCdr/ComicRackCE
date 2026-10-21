#!/usr/bin/env bash
# grid-multiselect: Cmd-click, Shift-click, Shift+arrows, Cmd+A and a click on the empty area change the selection of the grid as the original's
# ItemView does (P5.3, ledger BV.09 and BV.10). The check reads the status bar text, which counts the selection ("... - 3 selected / 4,2 MB",
# the file of the book when one is selected). The books of mac/fixtures/small are sorted by series, five to a row in the default window.
. "$(dirname "$0")/../lib.sh"; e2e_begin
SMALL="$FIXTURES/small"; need "$SMALL"

# status_text: the status bar text of the library window ("Library: 50 Books / 54,50 MB - 2 selected / ..."), empty if absent.
status_text() { "$UI" texts 2>/dev/null | awk -F'\t' '$2 ~ /^[A-Za-z ]*: [0-9]+ Books?/ { print $2; exit }'; }

# expect_status <regex> <what> [seconds]: waits until the status text matches.
expect_status() {
	local deadline=$(( $(date +%s) + ${3:-8} )) got=""
	while :; do
		got=$(status_text)
		if printf '%s\n' "$got" | grep -E -q -- "$1"; then ok "$2: $got"; return 0; fi
		[ "$(date +%s)" -lt "$deadline" ] || break
		sleep 0.5
	done
	bad "$2: the status text is '${got:-nothing}', expected /$1/"; return 1
}

# click points (window relative, points) of the first cells of the 1100 x 748 window with the browser dock opened to 640 (62 points lower than in the library
# window of the MVP): five columns
C0="388 219"; C1="531 219"; C2="674 219"; C3="817 219"; C4="960 219"; C5="388 469"; C6="531 469"; EMPTY="280 362"

# clk <x> <y> [--cmd] [--shift]: brings the app to the front (another app may have taken the focus), then clicks
clk() { front_app; must click "$@"; }

launch_app
scan_folder "$SMALL" 90
sleep 2
expect_status 'Library: 50 Books' "the library is shown"
shot multiselect-start

front_app
clk $C1
expect_status '- .*Fixture Series 01 002\.cbz' "a click selects one book (its file is named)"
clk $C3 --cmd
expect_status '- 2 selected' "Cmd-click adds a book"
clk $C6 --shift
expect_status '- 4 selected' "Shift-click selects from the focused book (4th) to the clicked one (7th)"
shot multiselect-range
clk $C1 --cmd
expect_status '- 5 selected' "Cmd-click on an unselected book adds it"
clk $C1 --cmd
expect_status '- 4 selected' "Cmd-click on a selected book takes it out"
clk $C5
expect_status '- .*Fixture Series 02 001\.cbz' "a plain click selects that book alone"

press shift+right
press shift+right
expect_status '- 3 selected' "Shift+Right twice extends the selection"
press shift+left
expect_status '- 2 selected' "Shift+Left shrinks it"
press right
expect_status '- .*Fixture Series 02 003\.cbz' "a plain arrow collapses it to one book"
press cmd+a
expect_status '- 50 selected' "Cmd+A selects every book"
shot multiselect-all
clk $C0
press cmd+right
press space
expect_status '- 2 selected' "Cmd+Right moves the focus only, Space selects the focused book"
clk $EMPTY
expect_status '^Library: 50 Books / [^-]*$' "a click on the empty area clears the selection"
quit_app
e2e_end
