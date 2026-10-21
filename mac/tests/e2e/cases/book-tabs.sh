#!/usr/bin/env bash
# book-tabs (P5.32a): the main window hosts the reader. Three books are opened in tabs (Cmd+Shift+O, Open in new Tab, from the browser grid), Cmd+Shift+K and
# Cmd+Shift+J cycle through the tabs (the window title follows the current book), Close All But This (the tab's own menu) leaves one tab, F12 moves the reader into
# its own window and F12 puts it back, Close All closes the rest. New tabs are added at the left end of the tab strip, as in the original.
. "$(dirname "$0")/../lib.sh"; e2e_begin
SMALL="$FIXTURES/small"; need "$SMALL"
E2E_BROWSER_HEIGHT=500

# title_is <regex> <what> [seconds]: waits for the window title of the main window ("ComicRack - <caption of the book>": a book of the library shows its caption)
title_is() {
	local deadline=$(( $(date +%s) + ${3:-8} )) got=""
	while :; do
		got=$("$UI" windows 2>/dev/null | awk -F'\t' '$2 == 0 && $3 == 1 && $6 ~ /^ComicRack( - .*)?$/ { print $6; exit }')
		if printf '%s\n' "$got" | grep -E -q -- "$1"; then ok "$2: $got"; return 0; fi
		[ "$(date +%s)" -lt "$deadline" ] || break
		sleep 0.4
	done
	bad "$2: the window title is '${got:-nothing}', expected /$1/"; return 1
}

launch_app --theme light
scan_folder "$SMALL" 90
sleep 2
title_is '^ComicRack$' "no book is open: the window is called as the original calls it"
shot no-book

# open three books of the first row in new tabs; after each one the keyboard focus is in the reader, so the grid is clicked again
for spec in "388 380:Fixture Series 01 #1" "531 380:Fixture Series 01 #2" "674 380:Fixture Series 01 #3"; do
	pt=${spec%%:*}
	front_app; must click $pt
	sleep 0.3
	front_app; must key cmd+shift+o
	sleep 1.2
	title_is " - ${spec#*:}\$" "Open in new Tab opened ${spec#*:}"
done
n=$(log_count 'Reader opened')
if [ "$n" = 3 ]; then ok "three books are open in three tabs"; else bad "$n 'Reader opened' lines, expected 3"; fi
shot three-tabs
# the strip is 001, 002, 003 from the right: a new tab goes to the left end, so the strip reads 003, 002, 001 and the current tab is the first
front_app; press cmd+shift+k; sleep 0.6
title_is " - Fixture Series 01 #2\$" "Next Tab (Cmd+Shift+K) shows the second tab"
press cmd+shift+k; sleep 0.6
title_is " - Fixture Series 01 #1\$" "Next Tab again shows the third tab"
press cmd+shift+k; sleep 0.6
title_is " - Fixture Series 01 #3\$" "Next Tab after the last wraps to the first"
press cmd+shift+j; sleep 0.6
title_is " - Fixture Series 01 #1\$" "Previous Tab (Cmd+Shift+J) wraps to the last"
shot cycled

# Tab: the reader key shows the next tab as well
front_app; "$UI" click 700 150 >/dev/null 2>&1; sleep 0.3
press tab; sleep 0.6
title_is " - Fixture Series 01 #3\$" "the Tab key of the reader shows the next tab"

# the tab's own menu (right click on the current tab): Close All But This is its second item
front_app; must click 60 43 --right
sleep 0.8
shot tab-menu
must key down; must key down; must key return
sleep 1
closed=$(log_count 'Reader closed')
if [ "$closed" = 2 ]; then ok "Close All But This closed the other two tabs"; else bad "$closed 'Reader closed' lines after Close All But This, expected 2"; fi
title_is " - Fixture Series 01 #[123]\$" "one book stays open"

# F12: the reader in its own window, the browser fills the main window; F12 again puts the reader back
front_app; press f12; sleep 1.5
check_log "Reader in own window" "F12 moved the reader into its own window"
R=$(other_window_id)
if [ -n "$R" ]; then ok "a second window shows the reader (id $R)"; shot undocked "$R"; else bad "no second window after F12"; fi
shot main-undocked
front_app; press f12; sleep 1.5
R2=$(other_window_id)
if [ -z "$R2" ]; then ok "F12 again: the reader window is gone, the reader is back in the main window"; else bad "a second window is still open after the second F12 ($R2)"; fi
shot docked-again

# Close All (Cmd+Shift+X) closes the last tab
press cmd+shift+x; sleep 1
if [ "$(log_count 'Reader closed')" = 3 ]; then ok "Close All closed the last tab"; else bad "Close All did not close the last tab"; fi
title_is '^ComicRack$' "no book is open again"
quit_app
e2e_end
