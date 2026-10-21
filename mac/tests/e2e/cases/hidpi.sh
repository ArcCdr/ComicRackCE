#!/usr/bin/env bash
# hidpi: the running app draws in device pixels. The screenshot of the library window is as many pixels wide as the window has points
# times the backing scale factor of the display it is on (2 on a Retina display), the content is not blank, and the reader window of a
# book is the same. The headless 1x/2x tests (Category=HiDpi) cover the drawing; this case only checks that the real window gets the
# scale of its display. A 1x run needs a display in a non-Retina mode: no public tool sets one, so on a Retina-only Mac the 1x half is
# reported as info (see full-port.md P3.H).
. "$(dirname "$0")/../lib.sh"; e2e_begin

# geom <window id>: "x y w h" in points from ui-drive windows.
geom() { "$UI" windows 2>/dev/null | awk -F'\t' -v id="$1" '$1 == id { split($4, p, ","); split($5, d, "x"); print p[1], p[2], d[1], d[2]; exit }'; }

# scale_of <x y w h>: backing scale factor of the screen that holds the middle of the window, or empty.
scale_of() {
	local g=($1) cx cy x y w h s
	cx=$(( ${g[0]%.*} + ${g[2]%.*} / 2 )); cy=$(( ${g[1]%.*} + ${g[3]%.*} / 2 ))
	while read -r x y w h s; do
		if [ "$cx" -ge "${x%.*}" ] && [ "$cx" -lt $(( ${x%.*} + ${w%.*} )) ] && [ "$cy" -ge "${y%.*}" ] && [ "$cy" -lt $(( ${y%.*} + ${h%.*} )) ]; then echo "$s"; return 0; fi
	done < <(screen_scales)
	return 1
}

# check_pixels <window id> <name>: screenshot width / window width equals the display's scale; the shot is not blank.
check_pixels() {
	local id=$1 name=$2 g scale w pw ratio
	g=$(geom "$id"); [ -n "$g" ] || { bad "$name: no geometry for window $id"; return; }
	set -- $g; w=${3%.*}
	scale=$(scale_of "$g") || { info "$name: no screen holds the window ($g)"; scale=""; }
	shot "$name" "$id" || return
	ui stats "$E2E_OUT/$name.png"
	pw=$(printf '%s\n' "$LAST_OUT" | awk '/size:/ { split($2, d, "x"); print d[1]; exit }')
	[ -n "$pw" ] && [ "$w" -gt 0 ] || { bad "$name: cannot read the sizes (window $w points, shot '$pw' pixels)"; return; }
	ratio=$(awk -v a="$pw" -v b="$w" 'BEGIN { printf "%.2f", a / b }')
	info "$name: window $w points, screenshot $pw pixels, ratio $ratio, display scale ${scale:-unknown}"
	if [ -n "$scale" ]; then
		if awk -v r="$ratio" -v s="$scale" 'BEGIN { d = r - s; exit !(d > -0.02 && d < 0.02) }'; then ok "$name: the window is drawn at the scale of its display ($scale)"; else bad "$name: ratio $ratio differs from the display scale $scale"; fi
	fi
	if awk -v s="${scale:-1}" 'BEGIN { exit !(s >= 2) }'; then ok "$name: Retina display, device pixels are $ratio x the points"; else info "$name: 1x display: the Retina half of the check does not apply"; fi
}

info "screens (x y w h scale): $(screen_scales | tr '\n' ';')"
launch_app
MAIN=$(printf '%s' "$WINDOW_LINE" | awk -F'\t' '{print $1}')
expect_log "Host opened by the app" 20
check_pixels "$MAIN" library
quit_app

BOOK="$FIXTURES/BD-reader/Amargo/Amargo - 01 - Le bal des vautours.cbz"; need "$BOOK"
E2E_BROWSER_HEIGHT=
launch_app --read-book "$BOOK"
expect_log "Reader check: .*pages" 40
sleep 1
front_app
R=$(main_window_id); [ -n "$R" ] || { bad "no main window"; e2e_end; }
check_pixels "$R" reader
quit_app
e2e_end
