#!/usr/bin/env bash
# menu-structure: the menu bar and the shortcuts of the library window (mvp-acceptance.md 1.3 to 1.5, 4.12, 6.1).
# The app menu must carry the app name (Application.Name in App.axaml; without it Avalonia shows "Avalonia Application").
. "$(dirname "$0")/../lib.sh"; e2e_begin

launch_app
bar=$("$UI" menu-items | tr '\n' '|')
if [ "$bar" = "Apple|$APP_NAME|File|View|Window|Help|" ]; then ok "menu bar: ${bar%|}"; else bad "menu bar is '$bar', expected 'Apple|$APP_NAME|File|View|Window|Help|'"; fi

# expect_item <Menu> <name> <shortcut> [mark]
expect_item() {
	local line sc mark
	line=$(menu_line "$1" "$2")
	if [ -z "$line" ]; then bad "$1 > $2 is missing"; return; fi
	sc=$(printf '%s' "$line" | awk -F'\t' '{print $2}'); mark=$(printf '%s' "$line" | awk -F'\t' '{print $3}')
	if [ "$sc" = "$3" ]; then ok "$1 > $2 [${sc:-no shortcut}]"; else bad "$1 > $2 has shortcut '$sc', expected '$3'"; fi
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

expect_item File "Open Folder…" "Cmd+O"
expect_item File "Import Windows Library…" ""
expect_item View "Show Sidebar" "Ctrl+Cmd+S" "✓"
expect_item View "Enter Full Screen" "Ctrl+Cmd+F"
expect_item View "Show Performance Overlay" "Option+Cmd+P" ""
expect_item Window "Minimize" "Cmd+M"
expect_item Window "Zoom" ""
expect_item Help "ComicRack CE Help" "Cmd+?"
extra=$("$UI" menu-items View 2>/dev/null | awk -F'\t' '$1 == "Enter Full Screen" && $2 == "F"' | wc -l | tr -d ' ')
info "AppKit adds its own 'Enter Full Screen' (Fn+F) item to View: $extra"
shot menu
quit_app
e2e_end
