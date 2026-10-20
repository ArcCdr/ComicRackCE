#!/usr/bin/env bash
# open-folder: File > Add Folder to Library... (Cmd+Shift+A) with the native picker, a real scan of mac/fixtures/small, the saved count after a restart.
. "$(dirname "$0")/../lib.sh"; e2e_begin
SMALL="$FIXTURES/small"; need "$SMALL"
expected=$(find "$SMALL" -type f \( -iname '*.cbz' -o -iname '*.cbr' -o -iname '*.cb7' -o -iname '*.cbt' -o -iname '*.pdf' \) | wc -l | tr -d ' ')

launch_app
press cmd+shift+a
must wait-window "Choose" 15
ok "the native folder picker opened: $LAST_OUT"
shot picker "$(window_id_by_title Choose 5)"
must choose-path "$SMALL"
expect_log "Scan of .*small/? started" 15
expect_log "Scan finished" 90
if printf '%s\n' "$LAST_OUT" | grep -q -E " $expected books added, $expected in the library"; then ok "scan result: $expected books added"; else bad "expected $expected books added and in the library: $LAST_OUT"; fi
expect_shown_count "$expected" "after the scan" 10
shot library
quit_app
saved=$(saved_book_count)
if [ "$saved" = "$expected" ]; then ok "saved library holds $saved books"; else bad "saved library holds $saved books, expected $expected"; fi

# Second launch with the same data folder: the library is still there and nothing is rescanned or lost.
launch_app
expect_shown_count "$expected" "after the relaunch" 20
shot relaunch
quit_app
saved2=$(saved_book_count)
if [ "$saved2" = "$expected" ]; then ok "after a restart the library still holds $saved2 books"; else bad "after a restart the library holds $saved2 books"; fi
e2e_end
