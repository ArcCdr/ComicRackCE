#!/usr/bin/env bash
# write-back (P5.9): with "Allow writing of Book info into files" on (a Settings.xml written before the launch), a Series edited in the Info dialog (Cmd+I, Details tab, OK) marks the book; the file is
# not touched until File > Update all Book Files (Cmd+Shift+U) queues it; the Core then writes ComicInfo.xml through the bundled 7zz and `crcli info show` reads the new Series from the file.
# The books are a private copy of three fixture books (the fixtures are never changed). Click points are those of the Info dialog window (client 593 x 517 below a 28 point title bar), as in info-edit.
. "$(dirname "$0")/../lib.sh"; e2e_begin
SMALL="$FIXTURES/small"; need "$SMALL"
CRCLI="$E2E_ROOT/mac/ComicRack.Cli/bin/Debug/net10.0/osx-arm64/crcli"
[ -x "$CRCLI" ] || skip_case 3 "crcli is not built (dotnet build mac/ComicRack.Cli/ComicRack.Cli.csproj)"
[ -x "$E2E_ROOT/mac/artifacts/native/7zz" ] || skip_case 3 "7zz is not fetched (mac/native/fetch-7zz.sh)"

DETAILS="94 48"      # the second tab
SERIES="123 98"      # the Series box of the Details tab
OK_BUTTON="369 522"  # OK, bottom right

BOOKS="$E2E_DATA/books"
mkdir -p "$BOOKS" "$E2E_DATA/Data"
cp "$SMALL/Fixture Series 01 001.cbz" "$SMALL/Fixture Series 01 002.cbz" "$SMALL/Fixture Series 01 003.cbz" "$BOOKS/"
FIRST="$BOOKS/Fixture Series 01 001.cbz"
SECOND="$BOOKS/Fixture Series 01 002.cbz"
cat > "$E2E_DATA/Data/Settings.xml" <<'X'
<?xml version="1.0" encoding="utf-8"?>
<Settings xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <UpdateComicFiles>true</UpdateComicFiles>
</Settings>
X
before=$(shasum -a 256 "$FIRST" | awk '{print $1}')
before2=$(shasum -a 256 "$SECOND" | awk '{print $1}')

# series_of <file>: the Series that `crcli info show` reads from the file (empty if none)
series_of() { "$CRCLI" info show "$1" 2>/dev/null | sed -n 's/^  Series: //p'; }

launch_app --theme light --fixture-library "$BOOKS"
expect_log "Scan finished|Fixture library" 60
expect_shown_count 3 "the three books" 40
expect_log "File updates ready" 15
sleep 2

# File has Update all Book Files with Shift+Cmd+U (MF.07)
item=$(menu_line File "Update all Book Files")
if [ -n "$item" ]; then ok "File has Update all Book Files: $(printf '%s' "$item" | tr '\t' ' ')"; else bad "File has no Update all Book Files item"; fi
case "$(printf '%s' "$item" | awk -F'\t' '{print $2}')" in *Shift+Cmd+U*) ok "its shortcut is Shift+Cmd+U";; *) bad "the shortcut of Update all Book Files is not Shift+Cmd+U: $item";; esac

# edit the Series of the first book in the Info dialog
front_app
must click 388 "$(gy 219)"
sleep 1
press cmd+i
expect_log "Info dialog opened for Fixture Series 01 #1" 15
WIN=$(window_id_by_title "^Fixture Series 01 #1$" 15) || { bad "no window titled 'Fixture Series 01 #1'"; e2e_end; }
front_app
must click $DETAILS --window "$WIN"
sleep 1
front_app
must click $SERIES --window "$WIN"
front_app
must key cmd+a
must type "Written Back"
sleep 0.5
front_app
must click $OK_BUTTON --window "$WIN"
expect_log "Info dialog closed with OK" 15
sleep 3

# the edit marked the book but the automatic update is off: the file is as it was
now=$(shasum -a 256 "$FIRST" | awk '{print $1}')
if [ "$now" = "$before" ]; then ok "the book file is untouched after the edit (no automatic update)"; else bad "the book file changed without Update all Book Files"; fi
shot edited

# File > Update all Book Files (Cmd+Shift+U)
front_app
press cmd+shift+u
expect_log "Update all Book Files: 3 books offered" 15
got=""
for i in $(seq 1 60); do
	got=$(series_of "$FIRST")
	[ "$got" = "Written Back" ] && break
	sleep 1
done
if [ "$got" = "Written Back" ]; then ok "crcli info show reads the new Series from the file: $got"; else bad "the file has Series '$got', expected 'Written Back'"; fi
after=$(shasum -a 256 "$FIRST" | awk '{print $1}')
if [ "$after" != "$before" ]; then ok "the book file was rewritten"; else bad "the book file did not change"; fi
# a book that was not edited may be rewritten too: showing its cover teaches the Core the page sizes, which marks the book changed (the original does the same)
if [ "$(shasum -a 256 "$SECOND" | awk '{print $1}')" = "$before2" ]; then info "an unedited book is left alone"; else info "an unedited book was rewritten (its page sizes were learned from the cover, as in the original)"; fi
shot written
front_app
quit_app
if grep -q 'Update Book Files.*error\|Update all Book Files.*error' "$E2E_DATA/Logs/ComicRack.log" 2>/dev/null; then bad "the log reports an error for the update"; else ok "no update error in the log"; fi
e2e_end
