#!/usr/bin/env bash
# fullscreen: Ctrl+Cmd+F puts the main window without and with a book in a tab into native full screen (the window covers a whole
# screen) and brings it back to its size; in the reader Esc leaves full screen first and then only shows or hides the browser dock, it
# never closes the book (mvp-acceptance.md 5.16; P5.32a).
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
item=$(menu_line Display "Full Screen")
info "Display > Full Screen while in full screen (mark): $(printf '%s' "$item" | awk -F'\t' '{print $3}')"
press ctrl+cmd+f
if wait_geom "$MAIN" normal "$orig"; then ok "library window is back to its size: $GEOM"; else bad "library window did not return to its size ($orig): $GEOM"; fi
quit_app

# The book in a tab of the main window.
BOOK="$FIXTURES/BD-reader/Amargo/Amargo - 01 - Le bal des vautours.cbz"; need "$BOOK"
E2E_BROWSER_HEIGHT=
launch_app --read-book "$BOOK"
expect_log "Reader check: .*pages" 40
sleep 1
front_app
R=$(main_window_id); [ -n "$R" ] || { bad "no main window"; e2e_end; }
orig=$(geom "$R"); ok "main window id $R, $orig"
press ctrl+cmd+f
if wait_geom "$R" covers "$orig"; then ok "the window with a book covers a screen in full screen: $GEOM"; else bad "the window did not cover a screen: $GEOM"; fi
sleep 1
shot reader-fullscreen "$R"
press escape
if wait_geom "$R" normal "$orig"; then ok "Esc left full screen, the window is back to its size: $GEOM"; else bad "Esc did not restore the window ($orig): $GEOM"; fi
sleep 0.5
before=$(log_count 'Reader closed')
press escape; sleep 1.2
if [ "$(log_count 'Reader closed')" = "$before" ]; then ok "a second Esc did not close the book (it shows or hides the browser dock)"; else bad "the second Esc closed the book"; fi
press escape; sleep 0.8
quit_app
e2e_end
