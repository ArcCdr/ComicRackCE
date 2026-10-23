#!/usr/bin/env bash
# menu-structure: the menu bar and the shortcuts of the library window (mvp-acceptance.md 1.3 to 1.5, 4.12, 6.1).
# The menu bar is the original's (File, Edit, Browse, Read, Display, Help) plus the macOS Window menu; the shortcuts are the ledger's (P5.R1).
# The app menu must carry the app name (Application.Name in App.axaml; without it Avalonia shows "Avalonia Application").
. "$(dirname "$0")/../lib.sh"; e2e_begin

launch_app
bar=$("$UI" menu-items | tr '\n' '|')
if [ "$bar" = "Apple|$APP_NAME|File|Edit|Browse|Read|Display|Window|Help|" ]; then ok "menu bar: ${bar%|}"; else bad "menu bar is '$bar', expected 'Apple|$APP_NAME|File|Edit|Browse|Read|Display|Window|Help|'"; fi

# expect_item <Menu> <name> <shortcut> [mark]
expect_item() {
	local line sc mark
	line=$(menu_line "$1" "$2")
	if [ -z "$line" ]; then bad "$1 > $2 is missing"; return; fi
	sc=$(printf '%s' "$line" | awk -F'\t' '{print $2}'); mark=$(printf '%s' "$line" | awk -F'\t' '{print $3}')
	# "*": a function key, which the accessibility tree does not name; only the item is checked
	if [ "$3" = "*" ] || [ "$sc" = "$3" ]; then ok "$1 > $2 [${sc:-no shortcut}]"; else bad "$1 > $2 has shortcut '$sc', expected '$3'"; fi
	if [ -n "${4+x}" ] && [ "$mark" != "$4" ]; then bad "$1 > $2 has mark '$mark', expected '$4'"; fi
}

# App menu: Quit exactly once, one About item, the standard Hide and Services items.
app_items=$("$UI" menu-items "$APP_NAME" 2>&1)
quits=$(printf '%s\n' "$app_items" | awk -F'\t' '$1 == "Quit"' | wc -l | tr -d ' ')
if [ "$quits" = 1 ]; then ok "app menu: Quit appears exactly once"; else bad "Quit appears $quits times in the app menu: $app_items"; fi
printf '%s\n' "$app_items" | awk -F'\t' '$1 == "Quit" && $2 != "Cmd+Q" { bad = 1 } END { exit bad }' && ok "app menu: Quit has Cmd+Q" || bad "Quit has no Cmd+Q"
abouts=$(printf '%s\n' "$app_items" | awk -F'\t' '$1 ~ /^About / ' | wc -l | tr -d ' ')
if [ "$abouts" = 1 ]; then ok "app menu: one About item"; else bad "app menu has $abouts About items"; fi
for n in Services "Hide $APP_NAME" "Hide Others" "Show All"; do
	if printf '%s\n' "$app_items" | awk -F'\t' -v n="$n" '$1 == n { f = 1 } END { exit !f }'; then ok "app menu: $n"; else bad "app menu: $n is missing"; fi
done

# File: MF.02 to MF.04 (the tabs), MF.05 (the MVP's Open Folder) and the Mac-only import (MA.04)
expect_item File "Close" "Option+Cmd+X"
expect_item File "Close All" "Shift+Cmd+X"
expect_item File "New Tab" "Cmd+T"
expect_item File "Add Folder to Library..." "Shift+Cmd+A"
expect_item File "Update all Book Files" "Shift+Cmd+U"
expect_item File "Import Windows Library…" ""
# Browse: MB.01 Browser (F3, checked while the browser dock is open) and MB.05 Sidebar, Shift+F6, checked while the side panel shows
expect_item Browse "Browser" "*" "✓"
expect_item Browse "Sidebar" "*" "✓"
# Read: the page commands (MR.01 to MR.04), Show in Browser (MR.08), the tabs (MR.09, MR.10) and Track current Page (MR.13)
expect_item Read "First Page" "Cmd+B"
expect_item Read "Previous Page" "Cmd+P"
expect_item Read "Next Page" "Cmd+N"
expect_item Read "Last Page" "Cmd+E"
expect_item Read "Show in Browser" "*"
expect_item Read "Previous Tab" "Shift+Cmd+J"
expect_item Read "Next Tab" "Shift+Cmd+K"
expect_item Read "Track current Page" "Option+Shift+T" "✓"
# Display: MD.25 Full Screen, F11 (alias Ctrl+Cmd+F is a key binding of the window), MD.26 Reader in own Window, F12
expect_item Display "Full Screen" "*" ""
expect_item Display "Reader in own Window" "*" ""
expect_item Display "Page Layout" ""
expect_item Display "Zoom" ""
# Window: Mac-only (C-MENU-2) with the performance overlay, which left Option+Cmd+P to the original's Previous Book (MR.05)
expect_item Window "Minimize" "Cmd+M"
expect_item Window "Zoom" ""
expect_item Window "Show Performance Overlay" "Ctrl+Option+Cmd+P" ""
# Help: MH.02, F1
expect_item Help "ComicRack Documentation..." "*"
# App menu items of the app: Settings... (Cmd+, , disabled until Preferences exist) and Hide Others with Option+Cmd+H
expect_item "$APP_NAME" "Settings…" "Cmd+,"
expect_item "$APP_NAME" "Hide Others" "Option+Cmd+H"
# There is no View menu any more: AppKit's own Enter Full Screen item must not be in a View menu
if "$UI" menu-items 2>/dev/null | grep -qx "View"; then bad "a View menu exists; the original has none"; else ok "no View menu"; fi
shot menu
quit_app
e2e_end
