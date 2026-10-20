#!/usr/bin/env bash
# quit-during-scan: Cmd+Q while a folder is being scanned ends the app normally (exit 0); the books found so far are saved and
# a relaunch keeps them (mvp-acceptance.md 2.11). The scan is of mac/fixtures/BD (14,706 books, minutes) so that it is still running.
. "$(dirname "$0")/../lib.sh"; e2e_begin
BD="$FIXTURES/BD"; need "$BD"

launch_app
press cmd+shift+a
must wait-window "Choose" 15
must choose-path "$BD"
expect_log "Scan of .*BD/? started" 15
sleep 6
if log_has "Scan finished"; then bad "the scan ended before Cmd+Q, the case proves nothing"; fi
shot scanning
quit_app
check_log "Exit requested, code 0" "the app exited normally during the scan"
saved=$(saved_book_count)
if [ "$saved" -gt 0 ] && [ "$saved" -lt 14706 ]; then ok "the library saved at exit holds $saved of 14,706 books"; else bad "the saved library holds $saved books (expected some, not all)"; fi
if grep -q "Saving the library failed\|ERROR" "$E2E_DATA/Logs/ComicRack.log"; then bad "ERROR lines in the log: $(grep -F ERROR "$E2E_DATA/Logs/ComicRack.log" | head -2)"; else ok "no ERROR line in the log"; fi

launch_app
expect_shown_count "$saved" "after the relaunch" 30
shot relaunch
quit_app
saved2=$(saved_book_count)
if [ "$saved2" -ge "$saved" ]; then ok "after the relaunch the library still holds $saved2 books (not fewer than $saved)"; else bad "the relaunch lost books: $saved -> $saved2"; fi
e2e_end
