#!/usr/bin/env bash
# context-menu-1 (P5.5): a right click on a book opens the context menu of the browser (the popup is a window of its own, layer 101). Mark as > Read through the
# menu changes the counters of the lists (log "Live refresh: ... Read 1/..., Never Read 49/..."), Option+Shift+U (Unread) puts the book back, and
# Escape closes the menu. Mac menu rendering is native-style, the items are the original's (ledger BC.*).
. "$(dirname "$0")/../lib.sh"; e2e_begin
SMALL="$FIXTURES/small"; need "$SMALL"

# popup_id [not-this-id]: id of the newest popup window (layer 101) of the app, empty if there is none
popup_id() { "$UI" windows 2>/dev/null | awk -F'\t' -v skip="${1:-}" '$2 == 101 && $3 == 1 && $1 != skip { print $1; exit }'; }
# expect_popup [not-this-id]: waits for a popup window; its id is left in POPUP
expect_popup() {
	local i id
	for i in $(seq 1 20); do
		id=$(popup_id "${1:-}"); [ -n "$id" ] && { POPUP=$id; return 0; }
		sleep 0.4
	done
	POPUP=""; return 1
}

launch_app --fixture-library "$SMALL"
expect_log "Scan finished|Fixture library" 60
expect_shown_count 50 "the fixture library" 40
sleep 3

front_app
must click --right 531 "$(gy 219)"   # the second book of the first row
if expect_popup; then ok "the context menu opened as a window of its own ($POPUP)"; else bad "no popup window after the right click"; e2e_end; fi
MENU=$POPUP
front_app
ui shot "$E2E_CASE-menu" --window "$MENU"; cp "$LAST_OUT" "$E2E_OUT/menu.png" 2>/dev/null
ui stats "$E2E_OUT/menu.png"
if printf '%s\n' "$LAST_OUT" | grep -q 'blank: no'; then ok "the menu is drawn (not blank)"; else bad "the menu shot is blank"; fi
status=$("$UI" texts 2>/dev/null | awk -F'\t' '$2 ~ /^[A-Za-z ]*: [0-9]+ Books?/ { print $2; exit }')
if printf '%s\n' "$status" | grep -q 'Fixture Series 01 002'; then ok "the right click selected the book under it: $status"; else bad "the status text does not name the second book: '$status'"; fi

# Mark as (the 6th entry, about 156 points from the top of the popup), Right opens its submenu: Unread, Read, line, Checked, Unchecked
front_app
must click 80 156 --window "$MENU"
sleep 0.5
front_app
must key right
if expect_popup "$MENU"; then ok "the Mark as submenu opened ($POPUP)"; else bad "the Mark as submenu did not open"; e2e_end; fi
SUB=$POPUP
front_app
must click 50 39 --window "$SUB"   # Read
for i in $(seq 1 20); do log_has 'Live refresh: .*Read 1/0/0' && break; sleep 0.5; done
if log_has 'Live refresh: .*Read 1/0/0'; then ok "Mark as Read counted the Read list: $(app_log | grep 'Live refresh: .*Read 1/0/0' | tail -1 | sed 's/.*Counters //')"; else bad "no live refresh counted the Read list"; fi
if log_has 'Live refresh: .*Never Read 49/'; then ok "the Never Read list lost the book"; else bad "the Never Read list still counts 50"; fi
if [ -z "$(popup_id)" ]; then ok "the menu closed after the click"; else bad "a popup is still open after choosing Read"; fi

# the keys of the original: Option+Shift+U marks the selected book as unread again
front_app
must key option+shift+u
for i in $(seq 1 20); do [ "$(log_count 'Live refresh: .*Never Read 50/')" -ge 2 ] && break; sleep 0.5; done
if [ "$(log_count 'Live refresh: .*Never Read 50/')" -ge 2 ]; then ok "Option+Shift+U marked the book as unread: the Never Read list has 50 again"; else bad "Option+Shift+U did not bring the book back to Never Read"; fi

# the menu again, closed with Escape; a click on the empty area clears the selection
front_app
must click --right 531 "$(gy 219)"
expect_popup || bad "the menu did not open the second time"
front_app
must key escape
sleep 0.5
if [ -z "$(popup_id)" ]; then ok "Escape closed the menu"; else bad "the menu is still open after Escape"; fi
quit_app
e2e_end
