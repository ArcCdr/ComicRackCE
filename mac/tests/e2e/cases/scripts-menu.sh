#!/usr/bin/env bash
# scripts-menu (P4.5): the commands of a script appear in the menus, run from the File menu's Automation list on a worker thread, and their output lands in the script
# output window (Window > Script Output, or shown at start with --show-script-console, the original's -ssc). The hooks Startup, BookOpened and ReaderResized reach the script.
# The script is a generic test plugin in the data folder (<data>/Data/Scripts); it writes what it sees into hooks.txt next to itself, the Core's undo marker and the books it
# marked go into the library, which is read after the app quit.
. "$(dirname "$0")/../lib.sh"; e2e_begin
SMALL="$FIXTURES/small"; need "$SMALL"
E2E_BROWSER_HEIGHT=500

SCRIPTS="$E2E_DATA/Data/Scripts"; mkdir -p "$SCRIPTS"
cat > "$SCRIPTS/e2e.py" <<'PY'
import clr
import os

def note(text):
	f = open(os.path.join(ScriptPath, 'hooks.txt'), 'a')
	f.write(text + '\n')
	f.close()

#@Name E2E mark two books
#@Key E2EMarkTwo
#@Hook Library
def E2EMarkTwo(books):
	chosen = list(books)[:2]
	for b in chosen:
		b.SetCustomValue('e2e.marked', 'yes')
	print 'e2e ran on', len(chosen), 'books'
	note('library command ran on %d books' % len(chosen))

#@Name E2E startup
#@Key E2EStartup
#@Hook Startup
def E2EStartup():
	note('startup')

#@Name E2E opened
#@Key E2EOpened
#@Hook BookOpened
def E2EOpened(book):
	note('opened ' + book.ShadowSeries + ' ' + book.ShadowNumber)

#@Name E2E resized
#@Key E2EResized
#@Hook ReaderResized
def E2EResized(width, height):
	note('resized %d %d' % (width, height))
PY

# wait_note <regex> <what> [seconds]: the script wrote a line into hooks.txt
wait_note() {
	local deadline=$(( $(date +%s) + ${3:-20} ))
	while :; do
		if grep -E -q -- "$1" "$SCRIPTS/hooks.txt" 2>/dev/null; then ok "$2: $(grep -E -m1 -- "$1" "$SCRIPTS/hooks.txt")"; return 0; fi
		[ "$(date +%s)" -lt "$deadline" ] || break
		sleep 0.5
	done
	bad "$2: no line matching /$1/ in hooks.txt"; return 1
}

launch_app --theme light --show-script-console --fixture-library "$SMALL"
expect_log "Scan finished|Fixture library" 60
expect_shown_count 50 "the fixture library" 40
expect_log "Scripting: [0-9]+ commands" 10
wait_note '^startup$' "the Startup hook ran when the window and the library were up"

# the script output window opened at start, as -ssc does
id=$(window_id_by_title '^Script Output$' 10) && ok "the script output window is open ($id)" || bad "no window called Script Output"

# File > Automation lists the Library command; choosing it runs it on a worker
items=$("$UI" menu-items File 2>/dev/null | awk -F'\t' '{print $1}' | tr '\n' '|')
case "$items" in *"Automation"*) ok "File has the Automation item";; *) bad "File has no Automation item: $items";; esac
front_app
must menu File Automation "E2E mark two books"
wait_note 'library command ran on 2 books' "the Library command ran on two books"
expect_log "Script command E2EMarkTwo finished" 15
# its output is in the window (the accessibility tree names the text box value)
sleep 1
if "$UI" texts 2>/dev/null | grep -q 'e2e ran on 2 books'; then ok "the script output window shows: e2e ran on 2 books"; else info "the text of the script output window is not in the accessibility tree"; fi
if id=$(window_id_by_title '^Script Output$' 5); then ui shot "$E2E_CASE-console" --window "$id"; cp "$LAST_OUT" "$E2E_OUT/console.png" 2>/dev/null; ui stats "$E2E_OUT/console.png"; if printf '%s\n' "$LAST_OUT" | grep -q 'blank: no'; then ok "the script output window is drawn with text in it"; else bad "the script output window is blank"; fi; fi
# the undo marker of the command is the first entry of Edit > Undo
front_app
undo_line=$(menu_line Edit "Undo: Automation 'E2E mark two books'")
if [ -n "$undo_line" ]; then ok "Edit has Undo: Automation 'E2E mark two books'"; else bad "no undo entry for the command: $("$UI" menu-items Edit 2>/dev/null | head -4 | tr '\n' '|')"; fi

# the close button of the script output window closes it; Window > Script Output opens it again, then it is closed for good (it covers the grid)
close_console() {
	local cid
	cid=$(window_id_by_title '^Script Output$' 5) || { bad "no script output window to close"; return 1; }
	front_app; must click 14 14 --window "$cid"
	sleep 0.6
	if window_id_by_title '^Script Output$' 1 >/dev/null; then bad "the script output window is still open after its close button"; else ok "the script output window closed"; fi
}
close_console
front_app; must menu Window "Script Output"
if window_id_by_title '^Script Output$' 8 >/dev/null; then ok "Window > Script Output opened the window again"; else bad "Window > Script Output did not open the window"; fi
close_console

# a book opened in the reader reaches BookOpened and ReaderResized
front_app; must click --double 388 "$(gy 380)"
wait_note '^opened Fixture Series 01 [0-9]+$' "the BookOpened hook got the book"
wait_note '^resized [0-9]+ [0-9]+$' "the ReaderResized hook got the size of the reader" 20
shot reader

quit_app
# the two books carry the value the command wrote (the library is saved when the app quits)
n=$(grep -c 'e2e.marked' "$E2E_DATA/Data/ComicDb.xml" 2>/dev/null || echo 0)
if [ "$n" = 2 ]; then ok "the saved library has the value on exactly two books"; else bad "the saved library has $n books with e2e.marked, expected 2"; fi
e2e_end
