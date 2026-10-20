#!/usr/bin/env bash
# import: File > Import Windows Library, the native file picker (Choose...), a path mapping, and the imported count
# (mvp-acceptance.md 2.1, 2.2, 2.4). Needs mac/fixtures/ComicDb.xml and mac/fixtures/BD (14,706 books), else skipped.
. "$(dirname "$0")/../lib.sh"; e2e_begin
DB="$FIXTURES/ComicDb.xml"; BD="$FIXTURES/BD"
need "$DB"; need "$BD"
before=$(shasum -a 256 "$DB" | awk '{print $1}')

launch_app
front_app
must menu File "Import Windows Library…"
must wait-window "^Import Windows Library$" 15
IMPORT_WIN=$(printf '%s' "$LAST_OUT" | awk -F'\t' '{print $1}')
ok "import window opened (id $IMPORT_WIN)"
shot import-dialog "$IMPORT_WIN"

# Layout in window points (640 x 437): the "Choose..." button right of the database box, the mappings box below it.
front_app
must click 580 159 --window "$IMPORT_WIN"
must wait-window "ComicDb|Choose" 15
ok "the native file picker opened: $LAST_OUT"
must choose-path "$DB"
sleep 1
front_app
must click 320 265 --window "$IMPORT_WIN"
must type "S:\\media\\BD\\=$BD/"
shot import-filled "$IMPORT_WIN"
front_app
must click 591 403 --window "$IMPORT_WIN"   # the Import button (Return would add a line in the mappings box)
expect_log "Imported [0-9.,]+ books" 180
if printf '%s\n' "$LAST_OUT" | grep -q -E "Imported 14.{0,3}706 books"; then ok "14,706 books imported"; else bad "expected 14,706 imported books: $LAST_OUT"; fi
if printf '%s\n' "$LAST_OUT" | grep -q -E "14.{0,3}706 of them found on this Mac"; then ok "all 14,706 books were found under the mapped folder"; else bad "not every book was found: $LAST_OUT"; fi
expect_shown_count 14706 "after the import" 20
shot library
quit_app
saved=$(saved_book_count)
if [ "$saved" = 14706 ]; then ok "saved library holds $saved books"; else bad "saved library holds $saved books"; fi
after=$(shasum -a 256 "$DB" | awk '{print $1}')
if [ "$before" = "$after" ]; then ok "the source ComicDb.xml is unchanged"; else bad "the source ComicDb.xml changed"; fi
e2e_end
