#!/usr/bin/env bash
# reader-look: the reader window as the original draws it (P5.R3) in the light and in the dark theme: the window is not blank, the paper beside
# the page is the reader background of the theme (WhiteSmoke #F5F5F5, dark #303030: Cr.ReaderBackground), the page and the tab strip are drawn.
# Two shots (light and dark) feed the contact sheet (mac/tools/chrome-sheet.sh R3 Reader).
. "$(dirname "$0")/../lib.sh"; e2e_begin
BOOK="$FIXTURES/BD-reader/Amargo/Amargo - 01 - Le bal des vautours.cbz"; need "$BOOK"

near_hex() {
	local i a b d
	for i in 0 2 4; do
		a=$((16#${1:$i:2})); b=$((16#${2:$i:2})); d=$((a - b))
		[ "$d" -ge -3 ] && [ "$d" -le 3 ] || return 1
	done
}

check_theme() { # <theme> <expected paper hex>
	local theme=$1 want=$2 id w h scale px py got
	launch_app --theme "$theme" --read-book "$BOOK"
	expect_log "Reader check: .*pages" 40
	sleep 2
	front_app
	id=$(other_window_id); [ -n "$id" ] || { bad "$theme: no reader window"; quit_app; return; }
	ok "$theme: reader window id $id"
	shot "reader-look-$theme" "$id"
	read -r w h < <(ui stats "$E2E_OUT/reader-look-$theme.png" >/dev/null; printf '%s\n' "$LAST_OUT" | awk '/size:/ { split($2, d, "x"); print d[1], d[2]; exit }')
	[ -n "$w" ] || { bad "$theme: cannot read the screenshot size"; quit_app; return; }
	# the paper is beside a portrait page (12 points from the left edge, halfway down) or above a landscape one (a sixth of the way down)
	scale=$(awk -v a="$w" 'BEGIN { printf "%.4f", a / 900 }')
	px=$(awk -v s="$scale" 'BEGIN { printf "%d", 12 * s }'); py=$((h / 2))
	got=$("$UI" probe "$E2E_OUT/reader-look-$theme.png" "$px" "$py" 2>&1 | awk '/hex/ { gsub(/#/, "", $2); print $2; exit }')
	if [ -n "$got" ] && near_hex "$got" "$want"; then ok "$theme: the paper is #$got (expected #$want) at $px,$py"
	else
		px=$((w / 2)); py=$((h / 6))
		got=$("$UI" probe "$E2E_OUT/reader-look-$theme.png" "$px" "$py" 2>&1 | awk '/hex/ { gsub(/#/, "", $2); print $2; exit }')
		if [ -n "$got" ] && near_hex "$got" "$want"; then ok "$theme: the paper is #$got (expected #$want) at $px,$py"
		else bad "$theme: the paper is #$got, expected #$want (at $px,$py)"; fi
	fi
	quit_app
}

check_theme light F5F5F5
check_theme dark 303030
e2e_end
