#!/usr/bin/env bash
# chrome (P5.4): the browser toolbar in the original's order, Cmd+F focuses the quick search (what is typed afterwards filters the list), and a drag of the
# side panel grip is saved: Settings.xml holds the new width while the app runs and the panel comes back at that width after a relaunch.
. "$(dirname "$0")/../lib.sh"; e2e_begin
SMALL="$FIXTURES/small"; need "$SMALL"
SETTINGS="$E2E_DATA/Data/Settings.xml"

status_text() { "$UI" texts 2>/dev/null | awk -F'\t' '$2 ~ /^[A-Za-z ]*: [0-9]+ Books?/ { print $2; exit }'; }

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

# the grip of the side panel (252 wide, 6 unit strip at its right edge) in window points: x 249, below the title bar (28) and the toolbar (25)
GRIP="249 300"; GRIP_TO="349 300"

launch_app --theme light
scan_folder "$SMALL" 90
sleep 2
expect_status 'Library: 50 Books' "the library is shown"
shot toolbar

# Cmd+F: the keyboard focus goes to the quick search, so the typed text filters the list
front_app; press cmd+f
sleep 0.5
must type "Series 01"
expect_status '\([0-9]+ filtered\)' "Cmd+F focused the quick search: typing filtered the list"
shot quick-search
press escape
sleep 0.5

# the splitter: drag the grip 100 points to the right
front_app; must drag $GRIP $GRIP_TO
for i in $(seq 1 20); do grep -q 'SidebarWidth' "$SETTINGS" 2>/dev/null && break; sleep 0.5; done
width=$(sed -n 's:.*<SidebarWidth>\([0-9.]*\)</SidebarWidth>.*:\1:p' "$SETTINGS" 2>/dev/null | head -1)
if [ -n "$width" ] && [ "${width%.*}" -ge 330 ] && [ "${width%.*}" -le 360 ]; then ok "Settings.xml holds the dragged width: $width"; else bad "SidebarWidth in Settings.xml is '${width:-missing}', expected about 352"; fi
shot dragged
quit_app
if grep -q "<SidebarWidth>${width}</SidebarWidth>" "$SETTINGS"; then ok "the width is still in Settings.xml after the quit"; else bad "the width was lost at the quit"; fi

launch_app --theme light
expect_log "Library window" 10
shot restored
# the panel is back at the saved width: its grip is where it was left, so a drag from there back to the start gives the first width again
front_app; must drag $GRIP_TO $GRIP
for i in $(seq 1 20); do
	back=$(sed -n 's:.*<SidebarWidth>\([0-9.]*\)</SidebarWidth>.*:\1:p' "$SETTINGS" 2>/dev/null | head -1)
	[ "${back%.*}" != "${width%.*}" ] && break
	sleep 0.5
done
if [ -z "$back" ]; then ok "the grip dragged back to the default width (252 is not written, it is the default)"
elif [ "${back%.*}" -ge 240 ] && [ "${back%.*}" -le 265 ]; then ok "after the relaunch the grip was at the saved width: dragged back it gives $back"
else bad "dragging the restored grip back gives the width '$back', expected about 252"; fi
shot back
quit_app
e2e_end
