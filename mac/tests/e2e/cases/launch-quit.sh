#!/usr/bin/env bash
# launch-quit: the window appears, the host opens, Cmd+Q ends the app with exit code 0 and the data folder is the only one used.
. "$(dirname "$0")/../lib.sh"; e2e_begin

launch_app
ok "window: $WINDOW_LINE"
expect_log "Host opened by the app" 20
shot launch
if [ "$(log_count '\[ERROR\]')" = 0 ]; then ok "no ERROR line in the log so far"; else bad "the log has ERROR lines: $(app_log | grep -F '[ERROR]' | head -3)"; fi
quit_app
check_log "Exit requested, code 0" "log records the exit with code 0"

# One data folder: everything the app wrote is below the folder it was given (Cache, Data, Logs), nothing named after the executable.
entries=$(cd "$E2E_DATA" && ls | tr '\n' ' ')
if [ "$entries" = "Cache Data Logs " ]; then ok "data folder holds exactly: ${entries% }"; else bad "unexpected content of the data folder: $entries"; fi
stray=$(find "$E2E_DATA" -maxdepth 3 -type d -iname 'ComicRack.Mac*' | head -1)
if [ -z "$stray" ]; then ok "no folder named after the executable"; else bad "stray folder: $stray"; fi
if [ -s "$E2E_DATA/Data/ComicDb.xml" ]; then ok "library file saved at exit"; else bad "no Data/ComicDb.xml after the exit"; fi
e2e_end
