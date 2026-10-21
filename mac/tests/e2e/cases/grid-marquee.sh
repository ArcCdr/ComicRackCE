#!/usr/bin/env bash
# grid-marquee: the rubber band and the drag out of the browser grid (RV6, ledger BV.11 and BV.12). The band starts on the empty area (here the margin left
# of the first column), selects the books it touches (the status bar text counts the selection), Shift and Cmd act as in the original's ItemView, and is
# drawn in the pure Highlight colour while the button is down (a probe pixel of its left line in a screenshot taken during the drag). Dragging a pressed
# book more than the drag size starts the system drag session of the selected books: the app logs "Drag started: N books, M files" and, when the button
# goes up over a place that takes no drop, "Drag ended: None".
. "$(dirname "$0")/../lib.sh"; e2e_begin
SMALL="$FIXTURES/small"; need "$SMALL"

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

# click points (window relative, points) of the 1100 x 748 window with the browser dock opened to 640 (the grid starts 62 points lower than in the library window of
# the MVP: tab strip 31, reader area 25, grip 6) with five columns; EDGE is the empty margin left of the first column
C0="388 219"; C1="531 219"; C2="674 219"; C4="960 219"; C6="531 469"; C7="674 469"; EDGE="290 122"; EMPTY="280 362"

# near_hex <hex> <hex>: every channel within 4
near_hex() {
	local i a b d
	for i in 0 2 4; do
		a=$((16#${1:$i:2})); b=$((16#${2:$i:2})); d=$((a - b))
		[ "$d" -ge -4 ] && [ "$d" -le 4 ] || return 1
	done
}

launch_app --theme light
scan_folder "$SMALL" 90
sleep 2
expect_status 'Library: 50 Books' "the library is shown"
front_app

# a band from the margin to the middle of the second book of the second row: the books of the first two rows in the first two columns
must drag $EDGE 531 469
expect_status '- 4 selected' "a band over two rows and two columns selects four books"

# the same band while the button is held: a screenshot, and the pure Highlight colour on the left line of the band (two units in from where it started)
held_drag() { front_app; "$UI" drag "$@" >/dev/null 2>&1; }
bg_run held_drag $EDGE 531 469 --hold-ms 3500
HOLD=$LAST_BG
sleep 2
shot marquee-held
read -r w h < <(ui stats "$E2E_OUT/marquee-held.png" >/dev/null; printf '%s\n' "$LAST_OUT" | awk '/size:/ { split($2, d, "x"); print d[1], d[2]; exit }')
WIN_W=$("$UI" windows 2>/dev/null | awk -F'\t' '$2 == 0 && $3 == 1 && $6 == "ComicRack" { split($5, d, "x"); print d[1]; exit }')
if [ -n "$w" ] && [ -n "$WIN_W" ]; then
	scale=$(awk -v a="$w" -v b="$WIN_W" 'BEGIN { printf "%.4f", a / b }')
	px=$(awk -v s="$scale" 'BEGIN { printf "%d", 292 * s + s / 2 }'); py=$(awk -v s="$scale" 'BEGIN { printf "%d", 233 * s }')
	got=$("$UI" probe "$E2E_OUT/marquee-held.png" "$px" "$py" 2>&1 | awk '/hex/ { gsub(/#/, "", $2); print $2; exit }')
	if [ -n "$got" ] && near_hex "$got" "0078D7"; then ok "the left line of the band is Highlight #$got at $px,$py (scale $scale)"; else bad "the left line of the band is #${got:-nothing} at $px,$py (scale $scale), expected #0078D7"; fi
else
	bad "cannot read the screenshot or the window size"
fi
wait $HOLD
expect_status '- 4 selected' "the selection stays when the button is released"
shot marquee-done

# Shift adds a band to the selection; Cmd flips what the band touches
front_app; must click $EMPTY
expect_status '^Library: 50 Books / [^-]*$' "a click on the empty area clears the selection"
must click $C4
expect_status '- .*\.cbz' "one book is selected"
must drag $EDGE 531 219 --shift
expect_status '- 3 selected' "Shift: the band adds its two books to the selected one"
must drag $EDGE 531 219 --cmd
expect_status '- .*Fixture Series 01 005\.cbz' "Cmd: the band flips its two books (both were selected: off), the fifth stays"

# drag out: press on a book and move further than the drag size, release over a place that takes no drop
front_app; must click $EMPTY
before=$(log_count 'Drag started')
must drag $C1 531 392 --steps 20
sleep 1
if [ "$(log_count 'Drag started')" -gt "$before" ]; then ok "dragging a book started a drag: $(app_log | grep 'Drag started' | tail -1)"; else bad "no 'Drag started' line in the log after dragging a book"; fi
if log_has 'Drag ended'; then ok "the drag ended: $(app_log | grep 'Drag ended' | tail -1)"; else bad "no 'Drag ended' line in the log"; fi
expect_status '- .*Fixture Series 01 002\.cbz' "the dragged book is the selection"
shot drag-done

# all fifty books: one drag image per file, the system wants as many pasteboard items; the app must survive it
front_app; press cmd+a
expect_status '- 50 selected' "Cmd+A selects every book"
before=$(log_count 'Drag started')
must drag $C1 531 392 --steps 20
sleep 2
if [ "$(log_count 'Drag started')" -gt "$before" ] && app_log | grep -F 'Drag started' | tail -1 | grep -q '50 books, 50 files'; then ok "the whole selection was dragged: $(app_log | grep 'Drag started' | tail -1)"; else bad "no 'Drag started: 50 books, 50 files' line after dragging the selection"; fi
expect_status '- 50 selected' "the selection is unchanged after the drag"
quit_app
e2e_end
