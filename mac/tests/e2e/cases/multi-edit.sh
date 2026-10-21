#!/usr/bin/env bash
# multi-edit (P5.8): with all 50 books of the fixture library selected, Cmd+I (Edit > Info...) opens the multi edit dialog, a window of 525 x 460 points (client area, 525 x 488 with the 28 point
# title bar) titled "Multiple Book Information (50)". Typing in the Series box checks it, OK writes the series into every book (the saved library has it 50 times after Cmd+Q) and the whole
# dialog is one undo step ("Undo: Edit multiple Books"); Escape writes nothing. Click points are those of the dialog window (title bar included): the Series box of the Main group at (142, 101)
# and OK at (384, 467).
. "$(dirname "$0")/../lib.sh"; e2e_begin
SMALL="$FIXTURES/small"; need "$SMALL"
DB="$E2E_DATA/Data/ComicDb.xml"

SERIES_BOX="142 101"   # the Series box: the panel at (12,12) inside a one unit frame, the box at (29,50) of the Main group
OK_BUTTON="384 467"    # btOK at (346,427) 77 x 24 of the 525 x 460 client area, plus the title bar

launch_app --theme light --fixture-library "$SMALL"
expect_log "Scan finished|Fixture library" 60
expect_shown_count 50 "the fixture library" 40
sleep 3

# select every book and open the dialog
front_app
must click 531 "$(gy 219)"
sleep 1
press cmd+a
sleep 1
press cmd+i
expect_log "Multi edit dialog opened for 50 books" 20
WIN=$(window_id_by_title "^Multiple Book Information [(]50[)]$" 15) || { bad "no window titled 'Multiple Book Information (50)'"; e2e_end; }
size=$("$UI" windows 2>/dev/null | awk -F'\t' -v id="$WIN" '$1 == id { print $5 }')
if [ "$size" = "525x488" ]; then ok "the dialog is 525 x 460 points (client area) under the title bar: $size"; else bad "the dialog window is $size, expected 525x488"; fi
shot opened "$WIN"

# a click in the Series box and typing: the box takes the new series and its check box is checked
front_app
must click $SERIES_BOX --window "$WIN"
sleep 0.5
front_app
must key cmd+a
must type "Multi Edit Series"
sleep 0.5
shot typed "$WIN"
front_app
must click $OK_BUTTON --window "$WIN"
expect_log "Multi edit dialog closed with OK" 20
sleep 2
if [ -z "$(window_id_by_title '^Multiple Book Information' 2)" ]; then ok "OK closed the dialog"; else bad "the dialog is still open after OK"; fi

# one undo step for all the books
item=$("$UI" menu-items Edit 2>/dev/null | awk -F'\t' '$1 ~ /^Undo/ { print; exit }')
if printf '%s' "$item" | grep -q '^Undo: Edit multiple Books'; then ok "Edit > Undo is 'Undo: Edit multiple Books': $(printf '%s' "$item" | tr '\t' ' ')"; else bad "Edit > Undo does not name the multi edit step: $item"; fi
shot grid-after-ok
sleep 1
quit_app
count=$(grep -o 'Multi Edit Series' "$DB" 2>/dev/null | wc -l | tr -d ' ')
if [ "$count" = "50" ]; then ok "the saved library has the new series in all 50 books"; else bad "the saved library has the new series $count times, expected 50"; fi

# second run: Escape writes nothing
launch_app --theme light
expect_shown_count 50 "the saved library" 40
sleep 3
front_app
must click 531 "$(gy 219)"
sleep 1
press cmd+a
sleep 1
press cmd+i
for i in $(seq 1 30); do [ "$(log_count 'Multi edit dialog opened for 50 books')" -ge 2 ] && break; sleep 0.5; done
if [ "$(log_count 'Multi edit dialog opened for 50 books')" -ge 2 ]; then ok "the dialog opened again after the relaunch"; else bad "no dialog after the relaunch"; e2e_end; fi
WIN=$(window_id_by_title "^Multiple Book Information [(]50[)]$" 15) || { bad "no dialog window after the relaunch"; e2e_end; }
front_app
must click $SERIES_BOX --window "$WIN"
sleep 0.5
front_app
must key cmd+a
must type "Never written"
front_app
must key escape
for i in $(seq 1 30); do [ "$(log_count 'Multi edit dialog closed with Cancel')" -ge 1 ] && break; sleep 0.5; done
if [ "$(log_count 'Multi edit dialog closed with Cancel')" -ge 1 ]; then ok "Escape closed the dialog with Cancel"; else bad "the dialog did not close with Cancel"; fi
sleep 1
quit_app
if grep -q 'Never written' "$DB" 2>/dev/null; then bad "Cancel wrote the edit into the library"; else ok "Cancel wrote nothing"; fi
count=$(grep -o 'Multi Edit Series' "$DB" 2>/dev/null | wc -l | tr -d ' ')
if [ "$count" = "50" ]; then ok "the first edit is still in all 50 books"; else bad "the first edit is in $count books after the cancelled dialog"; fi
e2e_end
