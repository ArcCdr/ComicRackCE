#!/usr/bin/env bash
# Launch, drive and screenshot the real ComicRack Mac app, safely (mac/plans/full-port.md §2.10 and §5.3, task P0.6a).
# Read mac/tools/ui-drive/README.md for the commands and the safety rules.
#
# Exit codes: 0 ok, 1 failed or timed out, 2 bad arguments or no session, 4 screen locked or no GUI access (record the check as
# SKIPPED), 5 safety check failed (the app is not frontmost, or the session's pid is no longer the app this tool launched:
# nothing was typed, clicked or killed), 6 launch refused (another copy of the app is running).
set -uo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)
ART="${UI_DRIVE_DIR:-$ROOT/mac/artifacts/ui-drive}"
SESSION="$ART/session.env"
WT="$ART/windowtool"
SHOTS="$ART/shots"

die() { local code=$1; shift; echo "ui-drive: $*" >&2; exit "$code"; }

usage() {
	cat <<'EOF'
usage: ui-drive.sh <command> [args]
  launch [--dev|--bundle] [--data-dir D] [-- app args]   start the app (default --dev) with a temporary data folder
  wait-window <title-regex> [seconds=30]                 wait until a window of the app has a matching title
  windows [--all]                                        list the windows of the app: id layer onscreen x y w h title
  front                                                  bring the app to the front (activates only the launched pid)
  frame <title> <x> <y> <w> <h>                          move and resize the app's window with that exact title (point units, the whole window incl. title bar)
  menu <Menu> <Item> [<Sub>]                             click a menu item (path of any depth)
  menu-items [<Menu> [<Item>...]]                        list menu bar items, or the items of a menu: name, shortcut, mark, enabled
  key <combo>                                            e.g. cmd+shift+g, return, escape, right, f5, cmd+code19 (see README)
  keycode <n>                                            raw key code
  type <text>                                            type text
  click <x> <y> [--right] [--double] [--cmd] [--shift] [--window ID]   click, window relative points (--cmd/--shift hold the modifier)
  shot <name> [--window ID]                              screenshot of the window to artifacts/ui-drive/shots/<name>.png
  probe <png> <x> <y> [--box N]                          pixel colour (crcli image probe)
  stats <png>                                            channel statistics, non-blank check (crcli image stats)
  choose-path <path>                                     in an open/save panel: Cmd+Shift+G, path, Return, Return
  texts                                                  the static texts of the app's windows (status bar, counts, labels): window title <TAB> text
  expect-log <regex> [seconds=10]                        wait for a line of the app log (or its output) that matches
  status                                                 print the session
  check-data-dir <path>                                  print the physical path if launch would accept it as --data-dir, else exit 2
  check                                                  exit 0 if the GUI session is usable, 4 if locked or no GUI
  quit                                                   Cmd+Q, wait for the exit, print the exit code
  kill                                                   terminate the launched app
UI_DRIVE_EXE=<path> makes 'launch' start that executable instead of the app (spikes; no build, same guards).
Input commands (menu, key, keycode, type, click, choose-path) refuse with exit 5 unless the launched app is frontmost;
add --reactivate (or UI_DRIVE_REACTIVATE=1) to let them activate it once first.
EOF
}

# ---------------------------------------------------------------- helpers

ensure_tool() {
	if [ ! -x "$WT" ] || [ "$ROOT/mac/tools/ui-drive/WindowTool.swift" -nt "$WT" ]; then
		command -v swiftc >/dev/null 2>&1 || die 1 "swiftc not found (Xcode command line tools)"
		mkdir -p "$ART"
		echo "ui-drive: compiling WindowTool.swift ..." >&2
		swiftc -O "$ROOT/mac/tools/ui-drive/WindowTool.swift" -module-cache-path "$ART/modcache" -o "$WT" 2>"$ART/swiftc.log" \
			|| { cat "$ART/swiftc.log" >&2; die 1 "WindowTool.swift does not compile"; }
	fi
}

# Exit 4 when the screen is locked or there is no GUI session.
check_gui() {
	ensure_tool
	local state
	state=$("$WT" lockstate 2>/dev/null) || true
	[ "$state" = unlocked ] || die 4 "screen locked or no GUI session ($state): UI check skipped"
}

# pid_owner_state <pid> <exe> <exitfile>: prints "ok" when the pid is the app this tool started, else why not:
# "gone" (no such process), "exited" (the launch wrapper recorded an exit code) or "foreign:<command line>" (the pid now belongs to
# another program: the app exited and the pid was reused). Input, quit and kill act on the pid only when this says "ok".
pid_owner_state() {
	local pid=$1 exe=$2 exitfile=$3 cmd
	kill -0 "$pid" 2>/dev/null || { echo gone; return; }
	if [ -s "$exitfile" ]; then echo exited; return; fi
	cmd=$(ps -p "$pid" -o command= 2>/dev/null) || { echo gone; return; }
	case "$cmd" in
		"$exe"|"$exe "*|*" $exe"|*" $exe "*) echo ok ;;
		*) echo "foreign:$cmd" ;;
	esac
}

# Loads the session written by launch; exit 2 without one, exit 1 if the app is gone, exit 5 if its pid belongs to another program.
load_session() {
	[ -f "$SESSION" ] || die 2 "no session: run 'ui-drive.sh launch' first"
	# shellcheck disable=SC1090
	. "$SESSION"
	[ -n "${UD_PID:-}" ] || die 2 "broken session file $SESSION"
	local state
	state=$(pid_owner_state "$UD_PID" "${UD_EXE:-}" "${UD_EXITFILE:-/nonexistent}")
	case "$state" in
		ok) ;;
		gone|exited) die 1 "the launched app (pid $UD_PID) is not running (see $UD_STDOUT)" ;;
		*) die 5 "refused: pid $UD_PID is no longer the app this tool launched (${state#foreign:}); nothing was sent" ;;
	esac
}

# Runs AppleScript read from stdin with arguments; maps the failures to exit codes.
run_as() {
	local script=$1; shift
	local out rc
	out=$(printf '%s\n' "$script" | osascript - "$@" 2>&1); rc=$?
	if [ $rc -ne 0 ]; then
		case "$out" in
			*NOTFRONT*) die 5 "refused: the launched app (pid $UD_PID) is not frontmost (frontmost pid $(printf '%s' "$out" | sed -n 's/.*NOTFRONT \([0-9]*\).*/\1/p')). Nothing was typed or clicked. Use 'front' first." ;;
			*"not allowed assistive"*|*"-1719"*|*"-25211"*|*"-1743"*|*"-25204"*) die 4 "no Accessibility/automation access for this process: $out" ;;
			*) die 1 "AppleScript failed: $out" ;;
		esac
	fi
	[ -n "$out" ] && printf '%s\n' "$out"
	return 0
}

# AppleScript prologue shared by every input command: abort unless the launched pid is frontmost.
FRONT_CHECK='
on run argv
	set targetPid to (item 1 of argv) as integer
	tell application "System Events"
		set frontPid to unix id of first process whose frontmost is true
		if frontPid is not targetPid then error "NOTFRONT " & frontPid
'

REACTIVATE=${UI_DRIVE_REACTIVATE:-0}

# Takes the options common to input commands out of the argument list: sets REST (array) and REACTIVATE.
strip_common() {
	REST=()
	local a
	for a in "$@"; do
		case "$a" in
			--reactivate) REACTIVATE=1 ;;
			*) REST+=("$a") ;;
		esac
	done
}

# Call before the AppleScript of an input command: GUI usable, session loaded, optional single re-activation.
prepare_input() {
	load_session
	check_gui
	if [ "$REACTIVATE" = 1 ]; then
		do_front || true
	fi
}

# type_checked <text>: types the text in chunks of 8 characters, each chunk through its own frontmost check (the check and the
# keystrokes of one chunk are one System Events script), so focus lost in the middle of a long text stops the typing (exit 5)
# after at most one chunk instead of sending the rest to another app.
type_checked() {
	local text=$1 i=0 n=${#1}
	while [ $i -lt $n ]; do
		run_as "$FRONT_CHECK
		keystroke (item 2 of argv)
	end tell
end run" "$UD_PID" "${text:$i:8}" >/dev/null
		i=$((i + 8))
	done
}

do_front() {
	osascript - "$UD_PID" >/dev/null 2>&1 <<'EOF'
on run argv
	tell application "System Events"
		set frontmost of (first process whose unix id is ((item 1 of argv) as integer)) to true
	end tell
end run
EOF
	sleep 0.4
	local front
	front=$("$WT" frontpid 2>/dev/null || true)
	[ "$front" = "$UD_PID" ]
}

# Window id of the app's main window: the first (frontmost) normal window of at least 100 x 100 points.
main_window_id() {
	"$WT" windows --pid "$UD_PID" | awk -F'\t' '$4 == 0 && $5 == 1 && $8 >= 100 && $9 >= 100 { print $1; exit }'
}

# dotnet build with the output in $ART/build.log (its last lines go to stderr when it fails).
quiet_build() {
	mkdir -p "$ART"
	dotnet build "$1" -nologo -v q >"$ART/build.log" 2>&1 || { grep -E 'error' "$ART/build.log" | sort -u | head -20 >&2; return 1; }
}

crcli_bin() {
	local bin="$ROOT/mac/ComicRack.Cli/bin/Debug/net10.0/osx-arm64/crcli"
	local stale=0
	if [ ! -x "$bin" ]; then
		stale=1
	elif [ -n "$(find "$ROOT/mac/ComicRack.Cli" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -newer "$bin" -print -quit)" ]; then
		stale=1
	fi
	if [ $stale = 1 ]; then
		echo "ui-drive: building crcli ..." >&2
		quiet_build "$ROOT/mac/ComicRack.Cli/ComicRack.Cli.csproj" || die 1 "crcli does not build (see $ART/build.log)"
	fi
	printf '%s\n' "$bin"
}

# ---------------------------------------------------------------- key names

# Sets KEY_KIND (char|code), KEY_VALUE and KEY_MODS (AppleScript modifier list text, "" if none) from a combo like cmd+shift+g.
parse_combo() {
	local combo=$1 lower rest mods="" token name
	lower=$(printf '%s' "$combo" | tr 'A-Z' 'a-z')
	[ -n "$lower" ] || die 2 "empty key combo"
	rest=$lower
	KEY_KIND=""
	KEY_VALUE=""
	while :; do
		case "$rest" in
			*+?*) token=${rest%%+*}; rest=${rest#*+} ;;
			*) token=$rest; rest=""; ;;
		esac
		# A trailing "+" key (for example cmd++) leaves token empty and rest "+".
		if [ -z "$token" ] && [ -n "$rest" ]; then token="+"; rest=""; fi
		if [ -z "$rest" ]; then
			# last token: the key
			name=$token
			break
		fi
		case "$token" in
			cmd|command|meta) mods="$mods${mods:+, }command down" ;;
			shift) mods="$mods${mods:+, }shift down" ;;
			opt|option|alt) mods="$mods${mods:+, }option down" ;;
			ctrl|control) mods="$mods${mods:+, }control down" ;;
			*) die 2 "unknown modifier '$token' in '$combo'" ;;
		esac
	done
	KEY_MODS=$mods
	case "$name" in
		return|enter) KEY_KIND=code; KEY_VALUE=36 ;;
		escape|esc) KEY_KIND=code; KEY_VALUE=53 ;;
		tab) KEY_KIND=code; KEY_VALUE=48 ;;
		space) KEY_KIND=code; KEY_VALUE=49 ;;
		delete|backspace) KEY_KIND=code; KEY_VALUE=51 ;;
		forwarddelete) KEY_KIND=code; KEY_VALUE=117 ;;
		left) KEY_KIND=code; KEY_VALUE=123 ;;
		right) KEY_KIND=code; KEY_VALUE=124 ;;
		down) KEY_KIND=code; KEY_VALUE=125 ;;
		up) KEY_KIND=code; KEY_VALUE=126 ;;
		home) KEY_KIND=code; KEY_VALUE=115 ;;
		end) KEY_KIND=code; KEY_VALUE=119 ;;
		pageup) KEY_KIND=code; KEY_VALUE=116 ;;
		pagedown) KEY_KIND=code; KEY_VALUE=121 ;;
		f1) KEY_KIND=code; KEY_VALUE=122 ;;
		f2) KEY_KIND=code; KEY_VALUE=120 ;;
		f3) KEY_KIND=code; KEY_VALUE=99 ;;
		f4) KEY_KIND=code; KEY_VALUE=118 ;;
		f5) KEY_KIND=code; KEY_VALUE=96 ;;
		f6) KEY_KIND=code; KEY_VALUE=97 ;;
		f7) KEY_KIND=code; KEY_VALUE=98 ;;
		f8) KEY_KIND=code; KEY_VALUE=100 ;;
		f9) KEY_KIND=code; KEY_VALUE=101 ;;
		f10) KEY_KIND=code; KEY_VALUE=109 ;;
		f11) KEY_KIND=code; KEY_VALUE=103 ;;
		f12) KEY_KIND=code; KEY_VALUE=111 ;;
		code[0-9]*) case "${name#code}" in *[!0-9]*) die 2 "bad key code in '$combo'" ;; esac; KEY_KIND=code; KEY_VALUE=${name#code} ;;
		?) KEY_KIND=char; KEY_VALUE=$name ;;
		*) die 2 "unknown key '$name' in '$combo' (single character, code<N>, or return escape tab space delete left right up down home end pageup pagedown f1..f12)" ;;
	esac
}

send_key() { # kind value mods
	local kind=$1 value=$2 mods=$3 clause="" action
	[ -n "$mods" ] && clause=" using {$mods}"
	if [ "$kind" = code ]; then action="key code (val as integer)$clause"; else action="keystroke val$clause"; fi
	run_as "$FRONT_CHECK
		set val to item 2 of argv
		$action
	end tell
end run" "$UD_PID" "$value" >/dev/null
}

# ---------------------------------------------------------------- data folder rules

# physical_path <path>: absolute physical path of a path that may not exist yet (symlinks of the existing part resolved, "~"
# expanded). Fails when a missing part contains "..".
physical_path() {
	local p=$1 tail="" base name
	case "$p" in "~") p=$HOME ;; "~/"*) p="$HOME/${p#"~/"}" ;; esac
	case "$p" in /*) ;; *) p="$PWD/$p" ;; esac
	while [ ! -d "$p" ]; do
		name=$(basename "$p")
		[ "$name" != .. ] || return 1
		tail="/$name$tail"
		p=$(dirname "$p")
		[ "$p" != / ] || break
	done
	base=$(cd "$p" 2>/dev/null && pwd -P) || return 1
	p="${base%/}$tail"
	printf '%s\n' "${p:-/}"
}

# validate_data_dir <path>: prints the physical path, or dies with exit 2 when the folder must not hold the app's data: the
# filesystem root, /Users, the home folder, ~/Library and its Application Support, Caches, Logs and Preferences folders, anything
# in ~/Documents, ~/Desktop or ~/Downloads (macOS privacy prompts in the owner's session), the owner's ComicRack folders and
# ComicRackAPI. Temporary folders and folders under the repository are fine.
validate_data_dir() {
	local d home reason=""
	d=$(physical_path "$1") || die 2 "refused: cannot resolve the data folder '$1'"
	home=$(cd "$HOME" 2>/dev/null && pwd -P || printf '%s' "$HOME")
	case "$d" in
		/|/Users|"$home"|"$home/Library"|"$home/Library/Application Support"|"$home/Library/Caches"|"$home/Library/Logs"|"$home/Library/Preferences"|"$home/dev"|"$home/Library/Containers")
			reason="a system or home folder" ;;
		"$home/Documents"|"$home/Documents/"*|"$home/Desktop"|"$home/Desktop/"*|"$home/Downloads"|"$home/Downloads/"*)
			reason="a protected user folder (privacy prompts)" ;;
		*/Library/Application\ Support/ComicRack*|*/Library/Caches/ComicRack*|*/Library/Logs/ComicRack*|*/dev/ComicRackAPI|*/dev/ComicRackAPI/*)
			reason="one of the owner's folders" ;;
		/Volumes/*|/System/*|/Library|/Library/*|/usr/*|/bin/*|/sbin/*|/etc/*|/Applications|/Applications/*)
			reason="a system or external volume folder" ;;
	esac
	[ -z "$reason" ] || die 2 "refused: $d is $reason; use a temporary --data-dir (a new folder under \$TMPDIR or the repository)"
	printf '%s\n' "$d"
}

# ---------------------------------------------------------------- commands

cmd_check() {
	ensure_tool
	local state
	state=$("$WT" lockstate 2>/dev/null) || true
	echo "$state"
	[ "$state" = unlocked ] || exit 4
}

cmd_launch() {
	local mode=dev data="" extra=() exe
	while [ $# -gt 0 ]; do
		case "$1" in
			--dev) mode=dev ;;
			--bundle) mode=bundle ;;
			--data-dir) shift; [ $# -gt 0 ] || die 2 "--data-dir needs a folder"; data=$1 ;;
			--) shift; extra=("$@"); break ;;
			*) die 2 "launch: unknown option $1" ;;
		esac
		shift
	done
	# A --data-dir is checked before anything is built, created or started.
	[ -z "$data" ] || data=$(validate_data_dir "$data") || exit $?
	if [ -n "${UI_DRIVE_EXE:-}" ]; then
		# Another Avalonia app of this repository (a spike): same guards, no build.
		mode=custom
		exe=$UI_DRIVE_EXE
		[ -x "$exe" ] || die 1 "UI_DRIVE_EXE is not an executable file: $exe"
	elif [ "$mode" = bundle ]; then
		exe="$ROOT/mac/artifacts/ComicRack CE (MVP).app/Contents/MacOS/ComicRack.Mac"
		[ -x "$exe" ] || die 1 "no bundle at $exe (run mac/tools/make-app.sh)"
	else
		exe="$ROOT/mac/ComicRack.Mac/bin/Debug/net10.0/osx-arm64/ComicRack.Mac"
		if [ "${UI_DRIVE_NO_BUILD:-0}" != 1 ]; then
			echo "ui-drive: building the dev app (incremental) ..." >&2
			quiet_build "$ROOT/mac/ComicRack.Mac/ComicRack.Mac.csproj" || die 1 "the dev app does not build (see $ART/build.log)"
		fi
		[ -x "$exe" ] || die 1 "no dev build at $exe (run: dotnet build mac/ComicRack.Mac)"
	fi

	if [ -f "$SESSION" ]; then
		# shellcheck disable=SC1090
		( . "$SESSION"; [ "$(pid_owner_state "${UD_PID:-0}" "${UD_EXE:-}" "${UD_EXITFILE:-/nonexistent}")" = ok ] ) && die 2 "a launched app is still running (see 'status'); run 'quit' or 'kill' first"
		rm -f "$SESSION"
	fi
	check_gui

	# Never run next to a copy that somebody else started (the owner's): the data folders and the window server are shared.
	local others
	others=$( { pgrep -x ComicRack.Mac; pgrep -f 'dotnet.*ComicRack\.Mac\.dll'; } 2>/dev/null | sort -u | tr '\n' ' ')
	[ -z "${others// /}" ] || die 6 "refused: ComicRack.Mac is already running (pid ${others}) and was not launched by ui-drive (an owner-run copy?). Quit it first."

	if [ -z "$data" ]; then
		data=$(mktemp -d "${TMPDIR:-/tmp}/ui-drive-data.XXXXXX") || die 1 "cannot create a temporary data folder"
	fi
	data=$(validate_data_dir "$data") || exit $?
	mkdir -p "$data"

	mkdir -p "$ART" "$SHOTS"
	local pidfile="$ART/app.pid" exitfile="$ART/app.exit" out="$ART/app.out"
	rm -f "$pidfile" "$exitfile" "$out"
	# A new session (setsid) so the app survives the end of this script; the wrapper records the exit code of the app.
	UD_EXE="$exe" UD_OUT="$out" UD_PIDFILE="$pidfile" UD_EXITFILE="$exitfile" UD_DATA="$data" \
		nohup perl -MPOSIX -e 'POSIX::setsid(); exec @ARGV' bash -c '
			"$UD_EXE" --data-dir "$UD_DATA" "$@" >"$UD_OUT" 2>&1 </dev/null &
			pid=$!
			echo $pid >"$UD_PIDFILE"
			wait $pid
			echo $? >"$UD_EXITFILE"
		' ui-drive-wrapper ${extra[@]+"${extra[@]}"} >/dev/null 2>&1 &
	disown 2>/dev/null || true

	local i=0
	while [ ! -s "$pidfile" ] && [ $i -lt 50 ]; do sleep 0.1; i=$((i + 1)); done
	[ -s "$pidfile" ] || die 1 "the app did not start"
	local pid
	pid=$(cat "$pidfile")
	{
		# %q: the file is sourced later, so a value with a quote, a space or a $ must not run or break anything
		printf 'UD_PID=%q\n' "$pid"
		printf 'UD_MODE=%q\n' "$mode"
		printf 'UD_EXE=%q\n' "$exe"
		printf 'UD_DATA=%q\n' "$data"
		printf 'UD_LOG=%q\n' "$data/Logs/ComicRack.log"
		printf 'UD_STDOUT=%q\n' "$out"
		printf 'UD_EXITFILE=%q\n' "$exitfile"
		printf 'UD_STARTED=%q\n' "$(date +%s)"
	} >"$SESSION"
	sleep 0.5
	kill -0 "$pid" 2>/dev/null || { cat "$out" >&2 2>/dev/null; rm -f "$SESSION"; die 1 "the app exited right after the start (exit $(cat "$exitfile" 2>/dev/null || echo ?))"; }
	echo "launched pid $pid ($mode), data folder $data, log $data/Logs/ComicRack.log"
}

cmd_status() {
	[ -f "$SESSION" ] || { echo "no session"; return 0; }
	cat "$SESSION"
	# shellcheck disable=SC1090
	( . "$SESSION"; if [ "$(pid_owner_state "${UD_PID:-0}" "${UD_EXE:-}" "${UD_EXITFILE:-/nonexistent}")" = ok ]; then echo "running: yes"; else echo "running: no"; fi )
}

cmd_windows() {
	load_session; ensure_tool
	local all=""
	[ "${1:-}" = "--all" ] && all="--all"
	"$WT" windows --pid "$UD_PID" $all | awk -F'\t' '{ printf "%s\t%s\t%s\t%s,%s\t%sx%s\t%s\n", $1, $4, $5, $6, $7, $8, $9, $10 }'
}

cmd_wait_window() {
	[ $# -ge 1 ] || die 2 "wait-window needs a title regex"
	local regex=$1 timeout=${2:-30}
	case "$timeout" in ''|*[!0-9]*) die 2 "wait-window: seconds must be a whole number" ;; esac
	load_session; check_gui
	local deadline=$(( $(date +%s) + timeout )) line
	while :; do
		[ "$(pid_owner_state "$UD_PID" "$UD_EXE" "$UD_EXITFILE")" = ok ] || die 1 "the app exited while waiting for a window (see $UD_STDOUT)"
		line=$("$WT" windows --pid "$UD_PID" | awk -F'\t' -v re="$regex" '$4 == 0 && $5 == 1 && $8 >= 100 && $9 >= 100 && $10 ~ re { printf "%s\t%s\t%sx%s\t%s\n", $1, $6 "," $7, $8, $9, $10; exit }')
		if [ -n "$line" ]; then printf '%s\n' "$line"; return 0; fi
		[ "$(date +%s)" -lt "$deadline" ] || die 1 "no window matching '$regex' after ${timeout}s"
		sleep 0.3
	done
}

cmd_front() {
	load_session; check_gui
	do_front || die 5 "could not bring pid $UD_PID to the front (frontmost is $("$WT" frontpid))"
	echo "frontmost: pid $UD_PID"
}

# frame <title> <x> <y> <w> <h>: sets the position and size of the launched app's window with that exact title through System Events. It sends no key or
# click and changes nothing but the geometry of a window of the launched pid, so it needs no frontmost app.
cmd_frame() {
	[ $# -eq 5 ] || die 2 "frame needs <title> <x> <y> <w> <h>"
	local title=$1; shift
	local n
	for n in "$@"; do case "$n" in ''|*[!0-9-]*) die 2 "frame: x, y, w and h must be whole numbers" ;; esac; done
	load_session; check_gui
	local out
	out=$(printf '%s\n' '
on run argv
	set targetPid to (item 1 of argv) as integer
	set wanted to item 2 of argv
	tell application "System Events"
		set p to first process whose unix id is targetPid
		set w to first window of p whose name is wanted
		set position of w to {(item 3 of argv) as integer, (item 4 of argv) as integer}
		set size of w to {(item 5 of argv) as integer, (item 6 of argv) as integer}
		set pos to position of w
		set siz to size of w
		return ((item 1 of pos) as text) & "," & ((item 2 of pos) as text) & " " & ((item 1 of siz) as text) & "x" & ((item 2 of siz) as text)
	end tell
end run' | osascript - "$UD_PID" "$title" "$@" 2>&1); local rc=$?
	if [ $rc -ne 0 ]; then
		case "$out" in
			*"not allowed assistive"*|*"-1719"*|*"-25211"*|*"-1743"*) die 4 "no Accessibility access: $out" ;;
			*) die 1 "AppleScript failed: $out" ;;
		esac
	fi
	echo "frame: $out"
}

cmd_menu() {
	strip_common "$@"; set -- ${REST[@]+"${REST[@]}"}
	[ $# -ge 2 ] || die 2 "menu needs <Menu> <Item> [<Sub>]"
	prepare_input
	# Walk menu 1 of the menu bar item, then through the sub menus, and click the last item.
	run_as "$FRONT_CHECK
		set p to first process whose unix id is targetPid
		set m to menu 1 of menu bar item (item 2 of argv) of menu bar 1 of p
		set n to count of argv
		repeat with i from 3 to n - 1
			set m to menu 1 of menu item (item i of argv) of m
		end repeat
		click menu item (item n of argv) of m
	end tell
end run" "$UD_PID" "$@" >/dev/null
	echo "clicked: $*"
}

cmd_menu_items() {
	load_session; check_gui
	local raw
	raw=$(printf '%s\n' '
on run argv
	set targetPid to (item 1 of argv) as integer
	set out to ""
	tell application "System Events"
		set p to first process whose unix id is targetPid
		if (count of argv) is 1 then
			repeat with b in menu bar items of menu bar 1 of p
				set out to out & (name of b as text) & linefeed
			end repeat
			return out
		end if
		set m to menu 1 of menu bar item (item 2 of argv) of menu bar 1 of p
		repeat with i from 3 to count of argv
			set m to menu 1 of menu item (item i of argv) of m
		end repeat
		repeat with mi in menu items of m
			set nm to ""
			try
				set nm to name of mi
			end try
			if nm is missing value then set nm to ""
			set ch to ""
			try
				set ch to value of attribute "AXMenuItemCmdChar" of mi
			end try
			if ch is missing value then set ch to ""
			set md to ""
			try
				set md to value of attribute "AXMenuItemCmdModifiers" of mi
			end try
			if md is missing value then set md to ""
			set vk to ""
			try
				set vk to value of attribute "AXMenuItemCmdVirtualKey" of mi
			end try
			if vk is missing value then set vk to ""
			set mk to ""
			try
				set mk to value of attribute "AXMenuItemMarkChar" of mi
			end try
			if mk is missing value then set mk to ""
			set en to "yes"
			try
				if (enabled of mi) is false then set en to "no"
			end try
			set sub to ""
			try
				if (count of menus of mi) > 0 then set sub to ">"
			end try
			set out to out & nm & tab & (ch as text) & tab & (md as text) & tab & (vk as text) & tab & (mk as text) & tab & en & tab & sub & linefeed
		end repeat
	end tell
	return out
end run' | osascript - "$UD_PID" "$@" 2>&1); local rc=$?
	if [ $rc -ne 0 ]; then
		case "$raw" in
			*"not allowed assistive"*|*"-1719"*|*"-25211"*|*"-1743"*) die 4 "no Accessibility access: $raw" ;;
			*) die 1 "AppleScript failed: $raw" ;;
		esac
	fi
	if [ $# -eq 0 ]; then
		printf '%s\n' "$raw" | sed '/^$/d'
		return 0
	fi
	# name, char, modifiers, virtual key, mark, enabled, sub menu -> name <TAB> shortcut <TAB> mark <TAB> enabled [<TAB> ">"]
	printf '%s\n' "$raw" | awk -F'\t' '
		function keyname(vk) {
			if (vk == 36) return "Return"; if (vk == 48) return "Tab"; if (vk == 49) return "Space"; if (vk == 51) return "Backspace"
			if (vk == 53) return "Esc"; if (vk == 115) return "Home"; if (vk == 116) return "PageUp"; if (vk == 117) return "Delete"
			if (vk == 119) return "End"; if (vk == 121) return "PageDown"; if (vk == 123) return "Left"; if (vk == 124) return "Right"
			if (vk == 125) return "Down"; if (vk == 126) return "Up"
			if (vk == 122) return "F1"; if (vk == 120) return "F2"; if (vk == 99) return "F3"; if (vk == 118) return "F4"
			if (vk == 96) return "F5"; if (vk == 97) return "F6"; if (vk == 98) return "F7"; if (vk == 100) return "F8"
			if (vk == 101) return "F9"; if (vk == 109) return "F10"; if (vk == 103) return "F11"; if (vk == 111) return "F12"
			return "key" vk
		}
		NF >= 6 {
			name = $1; ch = $2; md = $3 + 0; vk = $4
			if (name == "") { print "-"; next }
			sc = ""
			if (ch != "" || vk != "") {
				key = (ch != "") ? toupper(ch) : keyname(vk)
				if (ch == "\r" || ch == "\n") key = "Return"
				mods = ""
				if (int(md / 4) % 2) mods = mods "Ctrl+"
				if (int(md / 2) % 2) mods = mods "Option+"
				if (md % 2) mods = mods "Shift+"
				if (int(md / 8) % 2 == 0) mods = mods "Cmd+"
				sc = mods key
			}
			line = name "\t" sc "\t" $5 "\t" $6
			if ($7 == ">") line = line "\t>"
			print line
		}'
}

cmd_key() {
	strip_common "$@"; set -- ${REST[@]+"${REST[@]}"}
	[ $# -eq 1 ] || die 2 "key needs one combo, for example cmd+shift+g"
	parse_combo "$1"
	prepare_input
	send_key "$KEY_KIND" "$KEY_VALUE" "$KEY_MODS"
	echo "key: $1"
}

cmd_keycode() {
	strip_common "$@"; set -- ${REST[@]+"${REST[@]}"}
	[ $# -eq 1 ] || die 2 "keycode needs one number"
	case "$1" in ''|*[!0-9]*) die 2 "keycode: not a number: $1" ;; esac
	prepare_input
	send_key code "$1" ""
	echo "keycode: $1"
}

cmd_type() {
	strip_common "$@"; set -- ${REST[@]+"${REST[@]}"}
	[ $# -ge 1 ] || die 2 "type needs the text"
	local text="$*"
	prepare_input
	type_checked "$text"
	echo "typed ${#text} characters"
}

cmd_click() {
	local window="" args=() a
	strip_common "$@"; set -- ${REST[@]+"${REST[@]}"}
	while [ $# -gt 0 ]; do
		case "$1" in
			--window) shift; window=${1:-}; [ -n "$window" ] || die 2 "--window needs an id" ;;
			*) args+=("$1") ;;
		esac
		shift
	done
	local coords=() flags=()
	for a in ${args[@]+"${args[@]}"}; do
		case "$a" in
			--right|--double|--cmd|--shift) flags+=("$a") ;;
			-*|'') die 2 "click: unknown option $a" ;;
			*) coords+=("$a") ;;
		esac
	done
	[ ${#coords[@]} -eq 2 ] || die 2 "click needs <x> <y> (window relative points)"
	for a in "${coords[@]}"; do case "$a" in *[!0-9.]*|'') die 2 "click: not a non-negative number: $a" ;; esac; done
	prepare_input
	[ -n "$window" ] || window=$(main_window_id)
	[ -n "$window" ] || die 1 "the app has no window to click in"
	"$WT" click --pid "$UD_PID" --window "$window" "${coords[0]}" "${coords[1]}" ${flags[@]+"${flags[@]}"}
	local rc=$?
	case $rc in
		0) ;;
		4) exit 4 ;;
		5) exit 5 ;;
		*) exit 1 ;;
	esac
}

cmd_shot() {
	local name="" window=""
	while [ $# -gt 0 ]; do
		case "$1" in
			--window) shift; window=${1:-}; [ -n "$window" ] || die 2 "--window needs an id" ;;
			-*) die 2 "shot: unknown option $1" ;;
			*) [ -z "$name" ] || die 2 "shot: one name only"; name=$1 ;;
		esac
		shift
	done
	[ -n "$name" ] || die 2 "shot needs a name"
	case "$name" in *[!A-Za-z0-9._-]*|.*) die 2 "shot: the name may only contain letters, digits, dot, dash and underscore" ;; esac
	load_session; check_gui
	[ -n "$window" ] || window=$(main_window_id)
	[ -n "$window" ] || die 1 "the app has no window on screen"
	mkdir -p "$SHOTS"
	local file="$SHOTS/$name.png"
	rm -f "$file"
	screencapture -l"$window" -o -x "$file" 2>/dev/null
	[ -s "$file" ] || die 1 "screencapture produced no image (Screen Recording access for this process?)"
	echo "$file"
}

cmd_probe() {
	[ $# -ge 3 ] || die 2 "probe needs <png> <x> <y> [--box N]"
	"$(crcli_bin)" image probe "$@"
}

cmd_stats() {
	[ $# -eq 1 ] || die 2 "stats needs <png>"
	"$(crcli_bin)" image stats "$@"
}

cmd_choose_path() {
	strip_common "$@"; set -- ${REST[@]+"${REST[@]}"}
	[ $# -eq 1 ] || die 2 "choose-path needs one path"
	local path=$1
	prepare_input
	REACTIVATE=0
	send_key char g "command down, shift down"
	sleep 0.8
	type_checked "$path"
	sleep 0.6
	send_key code 36 ""
	sleep 0.8
	send_key code 36 ""
	echo "chose: $path"
}

cmd_texts() {
	load_session; check_gui
	local out rc
	out=$(printf '%s\n' '
on run argv
	set targetPid to (item 1 of argv) as integer
	set res to ""
	tell application "System Events"
		set p to first process whose unix id is targetPid
		repeat with w in windows of p
			set wt to ""
			try
				set wt to name of w
			end try
			if wt is missing value then set wt to ""
			set els to {}
			try
				set els to entire contents of w
			end try
			repeat with e in els
				try
					if role of e is "AXStaticText" then
						set v to value of e
						if v is not missing value and (v as text) is not "" then set res to res & wt & tab & (v as text) & linefeed
					end if
				end try
			end repeat
		end repeat
	end tell
	return res
end run' | osascript - "$UD_PID" 2>&1); rc=$?
	if [ $rc -ne 0 ]; then
		case "$out" in
			*"not allowed assistive"*|*"-1719"*|*"-25211"*|*"-1743"*) die 4 "no Accessibility access: $out" ;;
			*) die 1 "AppleScript failed: $out" ;;
		esac
	fi
	printf '%s\n' "$out" | sed '/^$/d'
}

cmd_expect_log() {
	[ $# -ge 1 ] || die 2 "expect-log needs a regex"
	local regex=$1 timeout=${2:-10}
	case "$timeout" in ''|*[!0-9]*) die 2 "expect-log: seconds must be a whole number" ;; esac
	load_session
	local deadline=$(( $(date +%s) + timeout )) line
	while :; do
		line=$(cat "$UD_LOG" "$UD_STDOUT" 2>/dev/null | grep -E -m1 -- "$regex" || true)
		if [ -n "$line" ]; then printf '%s\n' "$line"; return 0; fi
		[ "$(pid_owner_state "$UD_PID" "$UD_EXE" "$UD_EXITFILE")" = ok ] || die 1 "the app exited before '$regex' appeared in $UD_LOG"
		[ "$(date +%s)" -lt "$deadline" ] || die 1 "no log line matching '$regex' after ${timeout}s in $UD_LOG"
		sleep 0.3
	done
}

cmd_quit() {
	load_session; check_gui
	do_front || die 5 "could not bring pid $UD_PID to the front, so Cmd+Q was not sent"
	REACTIVATE=0
	send_key char q "command down"
	local i=0 code
	while [ $i -lt 200 ]; do
		if [ -s "$UD_EXITFILE" ]; then
			code=$(cat "$UD_EXITFILE")
			mv -f "$SESSION" "$ART/session.last" 2>/dev/null || true
			echo "exit code $code"
			[ "$code" = 0 ] && return 0
			return 1
		fi
		sleep 0.1; i=$((i + 1))
	done
	die 1 "the app did not exit within 20 s of Cmd+Q (a dialog may be open); use 'kill' to end it"
}

cmd_kill() {
	[ -f "$SESSION" ] || { echo "no session"; return 0; }
	# shellcheck disable=SC1090
	. "$SESSION"
	local state
	state=$(pid_owner_state "${UD_PID:-0}" "${UD_EXE:-}" "${UD_EXITFILE:-/nonexistent}")
	case "$state" in
		ok)
			kill -TERM "$UD_PID" 2>/dev/null || true
			local i=0
			while [ "$(pid_owner_state "$UD_PID" "$UD_EXE" "$UD_EXITFILE")" = ok ] && [ $i -lt 50 ]; do sleep 0.1; i=$((i + 1)); done
			# KILL only a pid that is still the app: after the app is gone its pid may belong to another program
			[ "$(pid_owner_state "$UD_PID" "$UD_EXE" "$UD_EXITFILE")" = ok ] && kill -KILL "$UD_PID" 2>/dev/null
			mv -f "$SESSION" "$ART/session.last" 2>/dev/null || true
			echo "killed pid $UD_PID" ;;
		foreign:*)
			mv -f "$SESSION" "$ART/session.last" 2>/dev/null || true
			echo "pid $UD_PID is no longer the launched app (${state#foreign:}); nothing was killed" ;;
		*)
			mv -f "$SESSION" "$ART/session.last" 2>/dev/null || true
			echo "the launched app (pid $UD_PID) had already exited" ;;
	esac
}

[ $# -ge 1 ] || { usage >&2; exit 2; }
command_name=$1; shift
case "$command_name" in
	help|-h|--help) usage ;;
	launch) cmd_launch "$@" ;;
	wait-window) cmd_wait_window "$@" ;;
	windows) cmd_windows "$@" ;;
	front) cmd_front "$@" ;;
	frame) cmd_frame "$@" ;;
	menu) cmd_menu "$@" ;;
	menu-items) cmd_menu_items "$@" ;;
	key) cmd_key "$@" ;;
	keycode) cmd_keycode "$@" ;;
	type) cmd_type "$@" ;;
	click) cmd_click "$@" ;;
	shot) cmd_shot "$@" ;;
	probe) cmd_probe "$@" ;;
	stats) cmd_stats "$@" ;;
	choose-path) cmd_choose_path "$@" ;;
	texts) cmd_texts "$@" ;;
	expect-log) cmd_expect_log "$@" ;;
	status) cmd_status "$@" ;;
	check-data-dir) [ $# -eq 1 ] || die 2 "check-data-dir needs one path"; validate_data_dir "$1" ;;
	check) cmd_check "$@" ;;
	quit) cmd_quit "$@" ;;
	kill) cmd_kill "$@" ;;
	*) echo "ui-drive: unknown command '$command_name'" >&2; usage >&2; exit 2 ;;
esac
