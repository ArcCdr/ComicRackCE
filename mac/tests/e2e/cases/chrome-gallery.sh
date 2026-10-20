#!/usr/bin/env bash
# chrome-gallery: the control gallery (--show ChromeGallery, P5.T4) opens in the light and in the dark theme, a screenshot of its window is not
# blank, and two probe pixels (the Highlight and Window swatches, whose place the app logs) have the colour of their Cr token in that theme.
. "$(dirname "$0")/../lib.sh"; e2e_begin

# near_hex <RRGGBB> <RRGGBB>: every channel within 4.
near_hex() {
	local i a b d
	for i in 0 2 4; do
		a=$((16#${1:$i:2})); b=$((16#${2:$i:2})); d=$((a - b))
		[ "$d" -ge -4 ] && [ "$d" -le 4 ] || return 1
	done
}

# probe_check <png> <theme>: reads the "Chrome gallery probe" lines of the log, finds the pixel of each swatch in the window screenshot
# (title bar height = screenshot height / scale - client height) and compares it with the logged token colour.
probe_check() {
	local png=$1 theme=$2 w h scale offset line token hex x y px py got
	read -r w h < <(ui stats "$png" >/dev/null; printf '%s\n' "$LAST_OUT" | awk '/size:/ { split($2, d, "x"); print d[1], d[2]; exit }')
	[ -n "$w" ] || { bad "$theme: cannot read the screenshot size"; return; }
	local n=0
	while IFS= read -r line; do
		token=$(printf '%s' "$line" | sed -E 's/.*probe: ([A-Za-z]+) #([0-9A-Fa-f]{6}) at ([0-9]+),([0-9]+) in a client area of ([0-9]+)x([0-9]+).*/\1/')
		hex=$(printf '%s' "$line" | sed -E 's/.*probe: ([A-Za-z]+) #([0-9A-Fa-f]{6}) at ([0-9]+),([0-9]+) in a client area of ([0-9]+)x([0-9]+).*/\2/')
		x=$(printf '%s' "$line" | sed -E 's/.* at ([0-9]+),([0-9]+) in a client area of ([0-9]+)x([0-9]+).*/\1/')
		y=$(printf '%s' "$line" | sed -E 's/.* at ([0-9]+),([0-9]+) in a client area of ([0-9]+)x([0-9]+).*/\2/')
		cw=$(printf '%s' "$line" | sed -E 's/.* at ([0-9]+),([0-9]+) in a client area of ([0-9]+)x([0-9]+).*/\3/')
		ch=$(printf '%s' "$line" | sed -E 's/.* at ([0-9]+),([0-9]+) in a client area of ([0-9]+)x([0-9]+).*/\4/')
		scale=$(awk -v a="$w" -v b="$cw" 'BEGIN { printf "%.4f", a / b }')
		offset=$(awk -v h="$h" -v s="$scale" -v c="$ch" 'BEGIN { printf "%.4f", h / s - c }')
		px=$(awk -v x="$x" -v s="$scale" 'BEGIN { printf "%d", x * s }')
		py=$(awk -v y="$y" -v o="$offset" -v s="$scale" 'BEGIN { printf "%d", (y + o) * s }')
		got=$("$UI" probe "$png" "$px" "$py" --box 3 2>&1 | awk '/hex/ { gsub(/#/, "", $2); print $2; exit }')
		if [ -z "$got" ]; then bad "$theme: probe of $token at $px,$py gave nothing"; continue; fi
		if near_hex "$got" "$hex"; then
			ok "$theme: the $token swatch is #$got (token #$hex) at $px,$py"
		else
			bad "$theme: the $token swatch is #$got, the token is #$hex (at $px,$py, scale $scale, title bar $offset)"
		fi
		n=$((n + 1))
	done < <(app_log | grep 'Chrome gallery probe:' | tail -2)
	[ "$n" -eq 2 ] && ok "$theme: both probe lines were logged" || bad "$theme: expected 2 probe lines, found $n"
}

# wait_probes <n>: waits until the log holds n probe lines (the two runs share the data folder, so the second run waits for 4).
wait_probes() {
	local i
	for i in $(seq 1 40); do
		[ "$(log_count 'Chrome gallery probe:')" -ge "$1" ] && return 0
		sleep 0.5
	done
	bad "the log has only $(log_count 'Chrome gallery probe:') probe lines, expected $1"
}

# alert_check: Cmd+Shift+A opens the system alert (NSAlert, C-DLG-1); Return presses the default button, a second alert answers Esc with the cancel button.
alert_check() {
	local id n
	for key in return escape; do
		n=$(log_count 'Chrome gallery alert: opening')
		press cmd+shift+a
		wait_log_count 'Chrome gallery alert: opening' $((n + 1)) 10 || { bad "no alert opened"; return; }
		sleep 1
		id=$("$UI" windows 2>/dev/null | awk -F'\t' '$2 == 8 { print $1; exit }')
		if [ -n "$id" ]; then shot "alert-$key" "$id"; else info "the alert window was not found by layer"; fi
		front_app
		must key "$key"
		if [ "$key" = return ]; then expect_log "Chrome gallery alert: button 0, suppressed no" 10; else expect_log "Chrome gallery alert: button 1, suppressed no" 10; fi
	done
}

# wait_log_count <regex> <n> <seconds>: waits until the log has at least n matching lines.
wait_log_count() {
	local i
	for i in $(seq 1 $(($3 * 2))); do
		[ "$(log_count "$1")" -ge "$2" ] && return 0
		sleep 0.5
	done
	return 1
}

want=0
for theme in light dark; do
	launch_app --theme "$theme" --show ChromeGallery
	want=$((want + 2))
	wait_probes "$want"
	id=$(window_id_by_title "ComicRack Chrome Gallery" 10) || { bad "$theme: the gallery window did not appear"; quit_app; continue; }
	ok "$theme: gallery window id $id"
	shot "gallery-$theme" "$id"
	probe_check "$E2E_OUT/gallery-$theme.png" "$theme"
	if [ "$theme" = light ]; then alert_check; fi
	quit_app
done
e2e_end
