#!/usr/bin/env bash
# reader-look: the reader as the original draws it (P5.R3) in the light and in the dark theme, in the main window since P5.32a: the window is not blank,
# the paper beside the page is the reader background of the theme (WhiteSmoke #F5F5F5, dark #303030: Cr.ReaderBackground), the page and the tab strip
# are drawn. Two shots (light and dark) feed the contact sheet (mac/tools/chrome-sheet.sh P5.32a MainWindow Reader).
E2E_BROWSER_HEIGHT=
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
	id=$(main_window_id); [ -n "$id" ] || { bad "$theme: no main window"; quit_app; return; }
	ok "$theme: main window id $id"
	shot "reader-look-$theme" "$id"
	read -r w h < <(ui stats "$E2E_OUT/reader-look-$theme.png" >/dev/null; printf '%s\n' "$LAST_OUT" | awk '/size:/ { split($2, d, "x"); print d[1], d[2]; exit }')
	[ -n "$w" ] || { bad "$theme: cannot read the screenshot size"; quit_app; return; }
	# the paper is beside a portrait page (12 points from the left edge, halfway down the reader area: 28 title bar + 31 tab strip + half of the 415 above the
	# 250 high browser dock in the default 1100 x 748 window) or above a landscape one (a sixth of the way down the area)
	scale=$(awk -v a="$w" 'BEGIN { printf "%.4f", a / 1100 }')
	px=$(awk -v s="$scale" 'BEGIN { printf "%d", 12 * s }'); py=$(awk -v s="$scale" 'BEGIN { printf "%d", 266 * s }')
	got=$("$UI" probe "$E2E_OUT/reader-look-$theme.png" "$px" "$py" 2>&1 | awk '/hex/ { gsub(/#/, "", $2); print $2; exit }')
	if [ -n "$got" ] && near_hex "$got" "$want"; then ok "$theme: the paper is #$got (expected #$want) at $px,$py"
	else
		px=$((w / 2)); py=$(awk -v s="$scale" 'BEGIN { printf "%d", (59 + 415 / 6) * s }')
		got=$("$UI" probe "$E2E_OUT/reader-look-$theme.png" "$px" "$py" 2>&1 | awk '/hex/ { gsub(/#/, "", $2); print $2; exit }')
		if [ -n "$got" ] && near_hex "$got" "$want"; then ok "$theme: the paper is #$got (expected #$want) at $px,$py"
		else bad "$theme: the paper is #$got, expected #$want (at $px,$py)"; fi
	fi
	quit_app
}

check_theme light F5F5F5
check_theme dark 303030
e2e_end
