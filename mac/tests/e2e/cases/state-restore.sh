#!/usr/bin/env bash
# state-restore (P5.1): the library window is moved and resized and the side panel is hidden; the settings are saved after the change (not only at quit);
# after Cmd+Q and a relaunch the window is back at the same position and size and the panel is still hidden. A saved frame on a monitor that is gone
# comes back on a screen, and a damaged Settings.xml starts the app with the defaults and keeps a .bad copy.
# the default dock: an untouched window writes no Settings.xml (--browser-height would be saved like a drag of the grip)
E2E_BROWSER_HEIGHT=
. "$(dirname "$0")/../lib.sh"; e2e_begin

SETTINGS="$E2E_DATA/Data/Settings.xml"

# geom <window id>: "x y w h" in points from ui-drive windows.
geom() { "$UI" windows 2>/dev/null | awk -F'\t' -v id="$1" '$1 == id { split($4, p, ","); split($5, d, "x"); print p[1], p[2], d[1], d[2]; exit }'; }

# main_geom: geometry of the library window ("ComicRack").
main_geom() { "$UI" windows 2>/dev/null | awk -F'\t' '$6 == "ComicRack" && $2 == 0 && $3 == 1 { split($4, p, ","); split($5, d, "x"); print p[1], p[2], d[1], d[2]; exit }'; }

on_a_screen() { # "x y w h": the top left corner of the window is on a screen
	local g=($1) s
	while read -r s; do
		set -- $s
		if [ "${g[0]%.*}" -ge "${1%.*}" ] && [ "${g[0]%.*}" -lt $(( ${1%.*} + ${3%.*} )) ] && [ "${g[1]%.*}" -ge "${2%.*}" ] && [ "${g[1]%.*}" -lt $(( ${2%.*} + ${4%.*} )) ]; then return 0; fi
	done < <(screen_frames)
	return 1
}

read -r SX SY SW SH < <(screen_frames | head -1)
SX=${SX%.*}; SY=${SY%.*}; SW=${SW%.*}; SH=${SH%.*}
WANT_X=$((SX + 90)); WANT_Y=$((SY + 80)); WANT_W=$((SW / 2 + 37)); WANT_H=$((SH / 2 + 25))
info "screen 1: $SX,$SY ${SW}x${SH}; target frame $WANT_X,$WANT_Y ${WANT_W}x${WANT_H}"

# ---- first run: move, resize, hide the side panel
launch_app
MAIN=$(printf '%s' "$WINDOW_LINE" | awk -F'\t' '{print $1}')
if [ -e "$SETTINGS" ]; then bad "Settings.xml exists before anything changed"; else ok "no Settings.xml before anything changed"; fi
item=$(menu_line Browse "Sidebar"); mark=$(printf '%s' "$item" | awk -F'\t' '{print $3}')
if [ "$mark" = "✓" ]; then ok "Browse > Sidebar is checked at the start"; else bad "Browse > Sidebar has the mark '$mark' at the start"; fi
must frame ComicRack "$WANT_X" "$WANT_Y" "$WANT_W" "$WANT_H"
ok "$LAST_OUT"
sleep 1
FIRST=$(geom "$MAIN"); ok "library window after the move: $FIRST"
press shift+f6
sleep 0.5
item=$(menu_line Browse "Sidebar"); info "Browse > Sidebar after Shift+F6: $(printf '%s' "$item" | awk -F'\t' '{print $3}')"

# the change is on disk after the quiet time, while the app still runs
for i in $(seq 1 20); do grep -q 'LibraryWindow' "$SETTINGS" 2>/dev/null && grep -q 'SidebarVisible' "$SETTINGS" 2>/dev/null && break; sleep 0.5; done
if grep -q 'LibraryWindow' "$SETTINGS" 2>/dev/null; then ok "Settings.xml was written while the app runs: $(tr -d '\n' < "$SETTINGS" | sed 's/  */ /g' | cut -c1-200)"; else bad "no LibraryWindow in Settings.xml 10 s after the move"; fi
shot moved
quit_app
if grep -q '<SidebarVisible>false</SidebarVisible>' "$SETTINGS"; then ok "Settings.xml records the hidden side panel"; else bad "SidebarVisible=false is not in Settings.xml"; fi
cp "$SETTINGS" "$E2E_OUT/Settings-after-first-run.xml"

# ---- second run: same frame, panel still hidden
launch_app
MAIN=$(printf '%s' "$WINDOW_LINE" | awk -F'\t' '{print $1}')
SECOND=$(geom "$MAIN")
if [ "$SECOND" = "$FIRST" ]; then ok "after the relaunch the window is at the same frame: $SECOND"; else bad "the window is at $SECOND after the relaunch, it was at $FIRST"; fi
expect_log "Library window restored to" 10
item=$(menu_line Browse "Sidebar")
mark=$(printf '%s' "$item" | awk -F'\t' '{print $3}')
if [ -z "$mark" ] || [ "$mark" = "missing value" ]; then ok "Browse > Sidebar is not checked: the panel stays hidden"; else bad "Browse > Sidebar shows the mark '$mark' after the relaunch"; fi
shot restored
quit_app

# ---- a frame on a monitor that is gone
sed -E 's/(<LibraryWindow[^>]*) X="-?[0-9]+"/\1 X="-31000"/; s/(<LibraryWindow[^>]*) Y="-?[0-9]+"/\1 Y="-31000"/' "$SETTINGS" > "$SETTINGS.edit" && mv "$SETTINGS.edit" "$SETTINGS"
if grep -q 'X="-31000"' "$SETTINGS"; then ok "Settings.xml now holds a frame at -31000,-31000"; else bad "could not edit the frame in Settings.xml"; fi
launch_app
MAIN=$(printf '%s' "$WINDOW_LINE" | awk -F'\t' '{print $1}')
THIRD=$(geom "$MAIN")
if on_a_screen "$THIRD"; then ok "the window came back onto a screen: $THIRD"; else bad "the window is off every screen: $THIRD"; fi
shot corrected
quit_app

# ---- a damaged settings file
printf '<Settings><SidebarWidth>wide</SidebarWidth>' > "$SETTINGS"
launch_app
expect_log "could not be read" 10
if [ -s "$SETTINGS.bad" ]; then ok "a .bad copy of the damaged file was kept"; else bad "no Settings.xml.bad"; fi
shot defaults
quit_app
if grep -q '<Settings' "$SETTINGS" && ! grep -q 'wide' "$SETTINGS"; then ok "the next save replaced the damaged file"; else bad "Settings.xml still holds the damaged text"; fi
if [ "$(log_count '\[ERROR\]')" = 0 ]; then ok "no ERROR line in the log"; else bad "ERROR lines: $(app_log | grep -F '[ERROR]' | head -3)"; fi
e2e_end
