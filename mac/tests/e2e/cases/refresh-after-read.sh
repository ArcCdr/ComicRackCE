#!/usr/bin/env bash
# refresh-after-read (P5.2): the library window follows the library while it changes. Three pages of a book are read in a tab of the main window and the tab is closed:
# the lists are counted again without a reload (log "Live refresh: ... Counters ... Reading 1/..."), the cover of the book is drawn again with its reading state, and
# the sidebar's "Reading" list holds the book. Before the reading no list is called "Reading" in the counters.
. "$(dirname "$0")/../lib.sh"; e2e_begin
SMALL="$FIXTURES/small"; need "$SMALL"

launch_app --fixture-library "$SMALL"
expect_log "Scan finished|Fixture library" 60
expect_shown_count 50 "the fixture library" 40
sleep 3
front_app
before=$(log_count 'Counters .*Reading [0-9]')
if [ "$before" = 0 ]; then ok "no list has a Reading counter before anything is read"; else bad "a Reading counter exists before anything is read ($before lines)"; fi
shot before-reading

# select the first book and open it (Return), read three pages, close the reader
front_app
must click 700 462   # a spot in the grid: focuses it
must key home; sleep 0.3
must key return
expect_log "Reader opened" 30
sleep 2
for i in 1 2 3; do press pagedown; sleep 0.7; done
sleep 1
press cmd+w
expect_log "Reader closed" 20
# the counters are refreshed when the reader is closed (no timer to wait for), a second or two at most
for i in $(seq 1 20); do log_has 'Live refresh: .*Reading 1/' && break; sleep 0.5; done
if log_has 'Live refresh: .*Reading 1/'; then ok "the lists were counted again: $(app_log | grep 'Live refresh: .*Reading 1/' | tail -1 | sed 's/.*Counters //')"; else bad "no live refresh counted the Reading list after the reader closed"; fi
sleep 1
front_app
shot after-reading
expect_shown_count 50 "after the refresh" 10
quit_app
e2e_end
