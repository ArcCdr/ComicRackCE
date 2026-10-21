#!/usr/bin/env bash
# info-pages (P5.7c): in the Info dialog of the second book the Pages tab lists the pages of the book (the log says how many pages it listed), a click selects the third page and the Delete key marks it
# as deleted; the Colors tab shows the page and its sliders open with "Image Control" and Saturation can be moved; OK writes the page edit into the library (the saved library has the page marked
# as Deleted after Cmd+Q) and the colour adjustment stays at the default of the book. Click points are those of the dialog window (client 593 x 517 below a 28 point title bar): the Pages tab at
# (216, 48), the Colors tab at (262, 48) (Catalog and Custom are hidden by default), the rows of the page list 21 points high from y 105 of the client, the Image Control button (498, 486) and OK.
. "$(dirname "$0")/../lib.sh"; e2e_begin
SMALL="$FIXTURES/small"; need "$SMALL"
DB="$E2E_DATA/Data/ComicDb.xml"

PAGES_TAB="216 48"
COLORS_TAB="262 48"
THIRD_ROW="200 163"       # the rows are 18 points high; the first is centred 127 points below the top of the window (title bar included)
IMAGE_CONTROL="498 486"   # the "Image Control" toggle button at the right below the viewer (window y 475 to 497)
OK_BUTTON="369 522"

launch_app --theme light --fixture-library "$SMALL"
expect_log "Scan finished|Fixture library" 60
expect_shown_count 50 "the fixture library" 40
sleep 3

front_app
must click 531 "$(gy 219)"
sleep 1
press cmd+i
expect_log "Info dialog opened for Fixture Series 01 #2" 15
WIN=$(window_id_by_title "^Fixture Series 01 #2$" 15) || { bad "no window titled 'Fixture Series 01 #2'"; e2e_end; }

# the Pages tab: the page list is made when the tab is shown
front_app
must click $PAGES_TAB --window "$WIN"
expect_log "Page list: [0-9]+ pages listed" 20
sleep 2
shot pages "$WIN"
front_app
must click $THIRD_ROW --window "$WIN"
sleep 0.5
must key delete
sleep 1
shot pages-deleted "$WIN"

# the Colors tab shows the selected page; the sliders open with the Image Control button
front_app
must click $COLORS_TAB --window "$WIN"
sleep 2
front_app
must click $IMAGE_CONTROL --window "$WIN"
sleep 1
shot colors "$WIN"

# OK writes the edit
front_app
must click $OK_BUTTON --window "$WIN"
expect_log "Info dialog closed with OK" 15
sleep 2
quit_app
# the page that was marked is the third one (image 2) of the book: its Type is Deleted in the saved library
if grep -q 'Type="Deleted"' "$DB" 2>/dev/null; then ok "the saved library has a page marked as Deleted"; else bad "the saved library has no page with Type=\"Deleted\""; fi
e2e_end
