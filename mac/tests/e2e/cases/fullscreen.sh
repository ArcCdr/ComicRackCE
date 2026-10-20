#!/usr/bin/env bash
# fullscreen: Ctrl+Cmd+F puts the library window and the reader window into native full screen (the window covers a whole
# screen) and brings them back to their size; in the reader Esc leaves full screen first (mvp-acceptance.md 5.16).
. "$(dirname "$0")/../lib.sh"; e2e_begin

# geom <window id>: "x y w h" in points from ui-drive windows.
geom() { "$UI" windows 2>/dev/null | awk -F'\t' -v id="$1" '$1 == id { split($4, p, ","); split($5, d, "x"); print p[1], p[2], d[1], d[2]; exit }'; }

# covers_screen <x y w h>: the window is as wide as a screen and as high as the screen minus the notch/menu strip (at most 80 pt).
covers_screen() {
	local g=($1) s
	while read -r s; do
		set -- $s
		if [ "${g[2]%.*}" -eq "${3%.*}" ] && [ "${g[3]%.*}" -ge $(( ${4%.*} - 80 )) ] && [ "${g[3]%.*}" -le "${4%.*}" ]; then return 0; fi
	done < <(screen_frames)
	return 1
}

# wait_geom <id> <covers|normal> <original geometry>: waits (up to 8 s) for the window to cover a screen, or to be back at its size.
wait_geom() {
	local id=$1 want=$2 orig=$3 i g
	for i in $(seq 1 40); do
		g=$(geom "$id")
		if [ -n "$g" ]; then
			if [ "$want" = covers ] && covers_screen "$g"; then GEOM=$g; return 0; fi
			if [ "$want" = normal ] && [ "${g#* * }" = "${orig#* * }" ]; then GEOM=$g; return 0; fi
		fi
		sleep 0.2
	done
	GEOM=$g; return 1
}

info "screens: $(screen_frames | tr '\n' ';')"
launch_app
MAIN=$(printf '%s' "$WINDOW_LINE" | awk -F'\t' '{print $1}')
orig=$(geom "$MAIN"); ok "library window id $MAIN, $orig"
press ctrl+cmd+f
if wait_geom "$MAIN" covers "$orig"; then ok "library window covers a screen in full screen: $GEOM"; else bad "library window did not cover a screen after Ctrl+Cmd+F: $GEOM"; fi
sleep 1
shot library-fullscreen "$MAIN"
item=$(menu_line View "Exit Full Screen"); [ -n "$item" ] || item=$(menu_line View "Enter Full Screen")
info "View menu item while in full screen: ${item%%$'\t'*}"
press ctrl+cmd+f
if wait_geom "$MAIN" normal "$orig"; then ok "library window is back to its size: $GEOM"; else bad "library window did not return to its size ($orig): $GEOM"; fi
quit_app

# Reader window.
BOOK="$FIXTURES/BD-reader/Amargo/Amargo - 01 - Le bal des vautours.cbz"; need "$BOOK"
launch_app --read-book "$BOOK"
expect_log "Reader check: .*pages" 40
sleep 1
front_app
R=$(other_window_id); [ -n "$R" ] || { bad "no reader window"; e2e_end; }
orig=$(geom "$R"); ok "reader window id $R, $orig"
press ctrl+cmd+f
if wait_geom "$R" covers "$orig"; then ok "reader covers a screen in full screen: $GEOM"; else bad "reader did not cover a screen: $GEOM"; fi
sleep 1
shot reader-fullscreen "$R"
press escape
if wait_geom "$R" normal "$orig"; then ok "Esc left full screen, the reader is back to its size: $GEOM"; else bad "Esc did not restore the reader ($orig): $GEOM"; fi
sleep 0.5
if "$UI" windows 2>/dev/null | awk -F'\t' -v id="$R" '$1 == id { f = 1 } END { exit !f }'; then ok "the reader stayed open after the first Esc"; else bad "the first Esc closed the reader"; fi
press escape; sleep 1.2
if "$UI" windows 2>/dev/null | awk -F'\t' -v id="$R" '$1 == id { f = 1 } END { exit !f }'; then bad "the second Esc did not close the reader"; else ok "a second Esc closed the reader"; fi
quit_app
e2e_end
