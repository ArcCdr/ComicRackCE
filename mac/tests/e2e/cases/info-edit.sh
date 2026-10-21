#!/usr/bin/env bash
# info-edit (P5.7a): Cmd+I (Edit > Info...) opens the Info dialog of the selected book as a window of 593 x 517 points (client area) with the original's tabs; the Series box on
# the Details tab takes a new value and OK writes it into the library (the saved library has it after Cmd+Q); Edit > Undo takes the whole dialog back as one step ("Undo: Show Info");
# Cancel (Escape) writes nothing. Click points are those of the dialog window (client 593 x 517 below a 28 point title bar): the Details tab, the Series box and OK.
. "$(dirname "$0")/../lib.sh"; e2e_begin
SMALL="$FIXTURES/small"; need "$SMALL"
DB="$E2E_DATA/Data/ComicDb.xml"

DETAILS="94 48"      # the second tab
SERIES="123 98"      # the Series box of the Details tab
OK_BUTTON="369 522"  # OK, bottom right

launch_app --theme light --fixture-library "$SMALL"
expect_log "Scan finished|Fixture library" 60
expect_shown_count 50 "the fixture library" 40
sleep 3

# the Edit menu has the items of the original: Info..., Undo, Redo, My Rating
info_item=$(menu_line Edit "Info...")
if [ -n "$info_item" ]; then ok "Edit has Info...: $(printf '%s' "$info_item" | tr '\t' ' ')"; else bad "Edit has no Info... item"; e2e_end; fi
case "$(printf '%s' "$info_item" | awk -F'\t' '{print $2}')" in *Cmd+I*) ok "its shortcut is Cmd+I";; *) bad "the shortcut of Info... is not Cmd+I: $info_item";; esac
if [ -n "$(menu_line Edit Undo)" ] && [ -n "$(menu_line Edit Redo)" ]; then ok "Edit has Undo and Redo"; else bad "Edit lacks Undo or Redo"; fi

# select the second book of the first row and open the dialog
front_app
must click 531 "$(gy 219)"
sleep 1
press cmd+i
expect_log "Info dialog opened for Fixture Series 01 #2" 15
WIN=$(window_id_by_title "^Fixture Series 01 #2$" 15) || { bad "no window titled 'Fixture Series 01 #2'"; e2e_end; }
size=$("$UI" windows 2>/dev/null | awk -F'\t' -v id="$WIN" '$1 == id { print $5 }')
if [ "$size" = "593x545" ]; then ok "the dialog is 593 x 517 points (client area) under the title bar: $size"; else bad "the dialog window is $size, expected 593x545"; fi
shot summary "$WIN"

# Details: the Series box takes the new value (select all, type)
front_app
must click $DETAILS --window "$WIN"
sleep 1
shot details "$WIN"
front_app
must click $SERIES --window "$WIN"
front_app
must key cmd+a
must type "Edited Series"
sleep 0.5
shot edited "$WIN"
front_app
must click $OK_BUTTON --window "$WIN"
expect_log "Info dialog closed with OK" 15
sleep 2
if [ -z "$(window_id_by_title '^Fixture Series 01 #2$' 2)" ]; then ok "OK closed the dialog"; else bad "the dialog is still open after OK"; fi

# the whole dialog is one undo step
item=$("$UI" menu-items Edit 2>/dev/null | awk -F'\t' '$1 ~ /^Undo/ { print; exit }')
if printf '%s' "$item" | grep -q '^Undo: Show Info'; then ok "Edit > Undo is 'Undo: Show Info': $(printf '%s' "$item" | tr '\t' ' ')"; else bad "Edit > Undo does not name the Show Info step: $item"; fi
if printf '%s' "$item" | awk -F'\t' '{ exit ($4 == "yes" || $4 == "1" || $4 == "true") ? 0 : 1 }'; then ok "and it is enabled"; else info "enabled column: $(printf '%s' "$item" | awk -F'\t' '{print $4}')"; fi

# the grid shows the edit at once (the live refresh): the same book is still selected and its caption is the new one when the dialog opens again
shot grid-after-ok
front_app
must key cmd+i
for i in $(seq 1 20); do [ "$(log_count 'Info dialog opened for Edited Series #2')" -ge 1 ] && break; sleep 0.5; done
if [ "$(log_count 'Info dialog opened for Edited Series #2')" -ge 1 ]; then ok "the selected book shows the new series (Edited Series #2) when the dialog opens again"; else bad "the book kept its old caption after OK"; fi
WIN=$(window_id_by_title "^Edited Series #2$" 15) || bad "no dialog for the edited book"
front_app
must key escape
expect_log "Info dialog closed with Cancel" 15
sleep 1
quit_app
if grep -q 'Series="Edited Series"' "$DB" 2>/dev/null || grep -q '<Series>Edited Series</Series>' "$DB" 2>/dev/null; then ok "the saved library has the new series"; else bad "the saved library does not have 'Edited Series'"; fi

# second run: Cancel (Escape) writes nothing. The edited book sorts first now (Edited Series < Fixture Series 01): it is the first cell
launch_app --theme light
expect_shown_count 50 "the saved library" 40
sleep 3
front_app
must click 388 "$(gy 219)"
sleep 1
press cmd+i
# the log is kept between runs: the second dialog is the second line of its kind
for i in $(seq 1 30); do [ "$(log_count 'Info dialog opened for Edited Series')" -ge 2 ] && break; sleep 0.5; done
if [ "$(log_count 'Info dialog opened for Edited Series')" -ge 2 ]; then ok "the dialog opened for the edited book after the relaunch"; else bad "no dialog for the edited book after the relaunch"; e2e_end; fi
WIN=$(window_id_by_title "^Edited Series" 15) || { bad "no dialog window for the edited book"; e2e_end; }
front_app
must click $DETAILS --window "$WIN"
sleep 1
front_app
must click $SERIES --window "$WIN"
front_app
must key cmd+a
must type "Never written"
front_app
must key escape
for i in $(seq 1 30); do [ "$(log_count 'Info dialog closed with Cancel')" -ge 2 ] && break; sleep 0.5; done
if [ "$(log_count 'Info dialog closed with Cancel')" -ge 2 ]; then ok "Escape closed the dialog with Cancel"; else bad "the dialog did not close with Cancel"; fi
sleep 1
quit_app
if grep -q 'Never written' "$DB" 2>/dev/null; then bad "Cancel wrote the edit into the library"; else ok "Cancel wrote nothing"; fi
if grep -q 'Series="Edited Series"' "$DB" 2>/dev/null || grep -q '<Series>Edited Series</Series>' "$DB" 2>/dev/null; then ok "the first edit is still in the library"; else bad "the first edit is gone after the cancelled dialog"; fi
e2e_end
