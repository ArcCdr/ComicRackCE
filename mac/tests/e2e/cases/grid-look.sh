#!/usr/bin/env bash
# grid-look: the browser grid (--show BookGrid, P5.R2) over the small fixture library in the light and in the dark theme: the thumbnails are
# drawn (the screenshot is not blank), two probe pixels have the colour the app logged for them (the paper of the grid and the black frame of the
# first cover), and a key press selects the first book, which gives a second shot for the contact sheet.
. "$(dirname "$0")/../lib.sh"; e2e_begin
need "$E2E_ROOT/mac/fixtures/small"

near_hex() {
	local i a b d
	for i in 0 2 4; do
		a=$((16#${1:$i:2})); b=$((16#${2:$i:2})); d=$((a - b))
		[ "$d" -ge -4 ] && [ "$d" -le 4 ] || return 1
	done
}

# probe_check <png> <theme>: the "Book grid probe" lines name a token, its colour and a point in the client area; the point is read from the
# window screenshot (scale = screenshot width / client width, title bar = screenshot height / scale - client height).
probe_check() {
	local png=$1 theme=$2 w h scale offset line token hex x y cw ch px py got n=0
	read -r w h < <(ui stats "$png" >/dev/null; printf '%s\n' "$LAST_OUT" | awk '/size:/ { split($2, d, "x"); print d[1], d[2]; exit }')
	[ -n "$w" ] || { bad "$theme: cannot read the screenshot size"; return; }
	local re='probe: ([A-Za-z]+) #([0-9A-Fa-f]{6}) at ([0-9]+),([0-9]+) in a client area of ([0-9]+)x([0-9]+)'
	while IFS= read -r line; do
		[[ $line =~ $re ]] || continue
		token=${BASH_REMATCH[1]}; hex=${BASH_REMATCH[2]}; x=${BASH_REMATCH[3]}; y=${BASH_REMATCH[4]}; cw=${BASH_REMATCH[5]}; ch=${BASH_REMATCH[6]}
		scale=$(awk -v a="$w" -v b="$cw" 'BEGIN { printf "%.4f", a / b }')
		offset=$(awk -v h="$h" -v s="$scale" -v c="$ch" 'BEGIN { printf "%.4f", h / s - c }')
		px=$(awk -v x="$x" -v s="$scale" 'BEGIN { printf "%d", x * s + s / 2 }')
		py=$(awk -v y="$y" -v o="$offset" -v s="$scale" 'BEGIN { printf "%d", (y + o) * s }')
		got=$("$UI" probe "$png" "$px" "$py" 2>&1 | awk '/hex/ { gsub(/#/, "", $2); print $2; exit }')
		if [ -z "$got" ]; then bad "$theme: probe of $token at $px,$py gave nothing"; continue; fi
		if near_hex "$got" "$hex"; then ok "$theme: $token is #$got (expected #$hex) at $px,$py"; else bad "$theme: $token is #$got, expected #$hex (at $px,$py, scale $scale, title bar $offset)"; fi
		n=$((n + 1))
	done < <(app_log | grep 'Book grid probe:' | tail -2)
	[ "$n" -eq 2 ] && ok "$theme: both probe lines were logged" || bad "$theme: expected 2 probe lines, found $n"
}

for theme in light dark; do
	launch_app --theme "$theme" --show BookGrid --fixture-library "$E2E_ROOT/mac/fixtures/small"
	expect_log 'Fixture library: .* scanned' 120
	for i in $(seq 1 40); do [ "$(log_count 'Book grid probe: ThumbBorder')" -ge 1 ] && break; sleep 0.5; done
	id=$(window_id_by_title "ComicRack Book Grid" 15) || { bad "$theme: the grid window did not appear"; quit_app; continue; }
	ok "$theme: grid window id $id"
	sleep 1
	shot "grid-look-$theme" "$id"
	probe_check "$E2E_OUT/grid-look-$theme.png" "$theme"
	front_app
	must key right
	sleep 1
	shot "grid-look-$theme-selected" "$id"
	quit_app
done
e2e_end
