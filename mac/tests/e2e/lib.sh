# Shared helpers of the e2e cases (mac/tools/e2e.sh). Source it from a case:  . "$(dirname "$0")/../lib.sh"; e2e_begin
#
# A case prints "  ok ..." and "  FAIL ..." lines and ends with e2e_end, which prints PASS, FAIL or SKIP and exits with
# 0 (pass), 1 (fail), 3 (skipped: a fixture is missing), 4 (skipped: screen locked, no GUI or Accessibility access) or
# 6 (skipped: ui-drive refused to launch because an owner-run copy of the app is running).
# Everything that touches the app goes through ui-drive (safety rules: mac/tools/ui-drive/README.md). Never call osascript
# to type or click from here.

E2E_ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)
UI="$E2E_ROOT/mac/tools/ui-drive/ui-drive.sh"
E2E_MODE=${E2E_MODE:-dev}
E2E_CASE=${E2E_CASE:-adhoc}
E2E_OUT="$E2E_ROOT/mac/artifacts/e2e/$E2E_CASE"
APP_NAME="ComicRack CE (MVP)"
FIXTURES="$E2E_ROOT/mac/fixtures"

E2E_FAILS=0
E2E_LAUNCHED=0
E2E_DATA=""
LAST_OUT=""
LAST_RC=0

say() { printf '%s\n' "$*"; }
ok() { say "  ok   $*"; }
bad() { say "  FAIL $*"; E2E_FAILS=$((E2E_FAILS + 1)); }
info() { say "  info $*"; }

e2e_begin() {
	rm -rf "$E2E_OUT"; mkdir -p "$E2E_OUT"
	E2E_DATA=$(mktemp -d "${TMPDIR:-/tmp}/e2e-data.XXXXXX")
	E2E_DATA=$(cd "$E2E_DATA" && pwd -P)
	trap e2e_cleanup EXIT
}

# bg_run <command...>: runs a command in the background and remembers it. e2e_cleanup ends whatever is still running (a drag that holds the mouse button
# down gets SIGTERM, releases the button at its start point and exits), so a failed or interrupted case never leaves the button pressed.
E2E_BG_PIDS=()
LAST_BG=""
bg_run() { "$@" & LAST_BG=$!; E2E_BG_PIDS+=("$LAST_BG"); }

# e2e_kill_tree <pid>: TERM to the children first (the helper that holds the button), then to the process itself.
e2e_kill_tree() {
	local p=$1 c
	for c in $(pgrep -P "$p" 2>/dev/null); do e2e_kill_tree "$c"; done
	kill -TERM "$p" 2>/dev/null
}

e2e_stop_background() {
	local p
	for p in ${E2E_BG_PIDS[@]+"${E2E_BG_PIDS[@]}"}; do
		if kill -0 "$p" 2>/dev/null; then e2e_kill_tree "$p"; fi
		wait "$p" 2>/dev/null
	done
	E2E_BG_PIDS=()
}

e2e_cleanup() {
	e2e_stop_background
	if [ "$E2E_LAUNCHED" = 1 ]; then "$UI" kill >/dev/null 2>&1; E2E_LAUNCHED=0; fi
	if [ -n "$E2E_DATA" ] && [ "${E2E_KEEP:-0}" != 1 ]; then rm -rf "$E2E_DATA"; fi
}

e2e_end() {
	if [ "$E2E_FAILS" -eq 0 ]; then say "PASS $E2E_CASE"; exit 0; fi
	say "FAIL $E2E_CASE: $E2E_FAILS check(s) failed"; exit 1
}

# skip_case <3|4|6> <reason>
skip_case() { say "SKIP $E2E_CASE: $2"; exit "$1"; }

# Case needs a fixture: skip (exit 3) when it is missing.
need() { [ -e "$1" ] || skip_case 3 "fixture missing: ${1#$E2E_ROOT/}"; }

# ui <ui-drive args>: runs ui-drive, output in LAST_OUT, exit code in LAST_RC (and returned).
# Exit 4 (locked screen, no GUI access) skips the whole case; exit 6 (launch refused) skips it with its own code, so a run in which
# nothing could start is not mistaken for a missing fixture; exit 5 (the safety check refused to send input) fails it.
ui() {
	LAST_OUT=$("$UI" "$@" 2>&1); LAST_RC=$?
	case $LAST_RC in
		4) skip_case 4 "screen locked or no GUI/Accessibility access: $LAST_OUT" ;;
		5) bad "ui-drive refused (the app was not frontmost), nothing was sent: ui-drive $*: $LAST_OUT"; e2e_end ;;
		6) skip_case 6 "launch refused: $LAST_OUT" ;;
	esac
	return $LAST_RC
}

# must <ui-drive args>: like ui, but any other failure ends the case.
must() { ui "$@" || { bad "ui-drive $* failed (exit $LAST_RC): $LAST_OUT"; e2e_end; }; }

# launch_app [app arguments...]: starts the app in the current mode with this case's data folder and waits for the library window.
launch_app() {
	if [ "$E2E_MODE" = bundle ]; then
		[ -x "$E2E_ROOT/mac/artifacts/ComicRack CE (MVP).app/Contents/MacOS/ComicRack.Mac" ] || skip_case 3 "no bundle (run mac/tools/make-app.sh)"
	fi
	ui launch "--$E2E_MODE" --data-dir "$E2E_DATA" -- "$@" || { bad "launch failed: $LAST_OUT"; e2e_end; }
	E2E_LAUNCHED=1
	must wait-window "^ComicRack$" 60
	WINDOW_LINE=$LAST_OUT
	front_app
}

# Application log (the data folder's Logs/ComicRack.log) helpers.
app_log() { cat "$E2E_DATA/Logs/ComicRack.log" 2>/dev/null; }
log_count() { app_log | grep -E -c -- "$1" || true; }
log_has() { app_log | grep -E -q -- "$1"; }

# expect_log <regex> [seconds]: waits for a log line, records ok/FAIL; the line is left in LAST_OUT.
expect_log() {
	if ui expect-log "$1" "${2:-15}"; then ok "log: $LAST_OUT"; return 0; fi
	bad "no log line matching /$1/ within ${2:-15} s ($LAST_OUT)"; return 1
}

quit_app() {
	front_app
	ui quit
	if [ "$LAST_RC" = 0 ]; then E2E_LAUNCHED=0; ok "Cmd+Q: $LAST_OUT"; else bad "quit failed: $LAST_OUT"; fi
	return "$LAST_RC"
}

# press <combo>: brings the launched app to the front (only that pid), then sends the key.
press() { front_app; must key "$1"; }

# front_app: brings the launched app to the front, retrying (another app may take the focus between two steps).
front_app() {
	local i
	for i in 1 2 3 4; do
		if "$UI" front >/dev/null 2>&1; then return 0; fi
		sleep 0.7
	done
	must front
}

# shot <name> [window id]: window screenshot into mac/artifacts/e2e/<case>/<name>.png; checks that it is not blank.
shot() {
	local name=$1 win=${2:-}
	front_app
	if [ -n "$win" ]; then ui shot "$E2E_CASE-$name" --window "$win"; else ui shot "$E2E_CASE-$name"; fi
	local f=$LAST_OUT
	[ -s "$f" ] || { bad "screenshot $name failed: $LAST_OUT"; return 1; }
	cp "$f" "$E2E_OUT/$name.png"
	ui stats "$E2E_OUT/$name.png"
	if printf '%s\n' "$LAST_OUT" | grep -q 'blank: no'; then ok "screenshot $name.png is not blank ($(printf '%s\n' "$LAST_OUT" | awk '/distinct/ {print $3}') colours)"; return 0; fi
	bad "screenshot $name.png is blank"; return 1
}

# windows of the launched app as "id<TAB>layer<TAB>onscreen<TAB>x,y<TAB>WxH<TAB>title"
windows() { ui windows; printf '%s\n' "$LAST_OUT"; }

# window_id_by_title <regex> [seconds]: id of the first on-screen window of the app whose title matches (waits).
window_id_by_title() {
	local deadline=$(( $(date +%s) + ${2:-10} )) id
	while :; do
		id=$("$UI" windows 2>/dev/null | awk -F'\t' -v re="$1" '$2 == 0 && $3 == 1 && $6 ~ re { print $1; exit }')
		if [ -n "$id" ]; then printf '%s\n' "$id"; return 0; fi
		[ "$(date +%s)" -lt "$deadline" ] || return 1
		sleep 0.3
	done
}

# Window that is not the library window (a reader, a dialog): its id.
other_window_id() {
	"$UI" windows 2>/dev/null | awk -F'\t' -v app="$APP_NAME" '$2 == 0 && $3 == 1 && $6 != app && $5 !~ /^[0-9]x/ { print $1; exit }'
}

# Screens as "x y w h" lines in top-left global points.
screen_frames() {
	osascript -l JavaScript -e 'ObjC.import("AppKit"); var s=$.NSScreen.screens; var main=s.objectAtIndex(0).frame.size.height; var o=[]; for (var i=0;i<s.count;i++){var f=s.objectAtIndex(i).frame; o.push([f.origin.x, main-(f.origin.y+f.size.height), f.size.width, f.size.height].join(" "));} o.join("\n")' 2>/dev/null
}

# Screens as "x y w h scale" lines in top-left global points; scale is the backing scale factor (2 on a Retina display, 1 on others).
screen_scales() {
	osascript -l JavaScript -e 'ObjC.import("AppKit"); var s=$.NSScreen.screens; var main=s.objectAtIndex(0).frame.size.height; var o=[]; for (var i=0;i<s.count;i++){var sc=s.objectAtIndex(i); var f=sc.frame; o.push([f.origin.x, main-(f.origin.y+f.size.height), f.size.width, f.size.height, sc.backingScaleFactor].join(" "));} o.join("\n")' 2>/dev/null
}

# menu_line <Menu> <item name>: the menu-items line of one item (name, shortcut, mark, enabled), or nothing.
menu_line() {
	"$UI" menu-items "$1" 2>/dev/null | awk -F'\t' -v n="$2" '$1 == n { print; exit }'
}

# Number of books recorded in the saved library file (after the app quit).
saved_book_count() { grep -c '<Book ' "$E2E_DATA/Data/ComicDb.xml" 2>/dev/null || echo 0; }

# scan_folder <folder> <seconds>: File > Add Folder to Library... (Cmd+Shift+A), picker, waits for "Scan finished". Leaves the log line in LAST_OUT.
scan_folder() {
	press cmd+shift+a
	must wait-window "Choose" 15
	must choose-path "$1"
	expect_log "Scan finished" "${2:-60}"
}

# check_log <regex> <message>: the log (read from the file, also after the app has exited) has a matching line.
check_log() {
	if log_has "$1"; then ok "$2"; else bad "$2: no log line matching /$1/"; fi
}

# shown_book_count: the number of books in the status bar text of the library window (read through the accessibility tree); empty if absent.
# The text is the original's SelectionInfo: "Library: 14706 Books (12 filtered) / 1.20 GB - 1 selected"; the number before " Book(s)".
shown_book_count() {
	"$UI" texts 2>/dev/null | awk -F'\t' '{ if (match($2, /(^|: )[0-9][0-9.,]* Books?( |$)/)) { t = substr($2, RSTART, RLENGTH); sub(/^: /, "", t); sub(/ Books?.*$/, "", t); gsub(/[^0-9]/, "", t); print t; exit } }'
}

# expect_shown_count <n> <what> [seconds]: waits until the window shows exactly n books.
expect_shown_count() {
	local deadline=$(( $(date +%s) + ${3:-20} )) got=""
	while :; do
		got=$(shown_book_count)
		if [ "$got" = "$1" ]; then ok "$2: the window shows $got books"; return 0; fi
		[ "$(date +%s)" -lt "$deadline" ] || break
		sleep 1
	done
	bad "$2: the window shows '${got:-nothing}' books, expected $1"; return 1
}
