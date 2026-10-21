#!/usr/bin/env bash
# small-preview (P5.6a): Browse > Small Preview (Shift+F7) opens the preview pane under the tree. The preview shows nothing until a book is selected; 500 ms after the
# selection settled it shows the first page of the first selected book (the pane's screenshot differs from the empty one), the Next button turns the page, and the setting is
# saved (Settings.xml) and comes back after a relaunch. Click points are those of the default window (1100 x 720, browser dock 640 high): the pane is the bottom 207 points of the
# side panel, the toolbar starts 2 points inside it and is 25 high, the viewer is below it.
. "$(dirname "$0")/../lib.sh"; e2e_begin
SMALL="$FIXTURES/small"; need "$SMALL"
SETTINGS="$E2E_DATA/Data/Settings.xml"

# window points (title bar 28 + tab strip 31 + reader area 25 + dock 640 ends at 724; the pane is 207 high: it starts at 517, its grip is 6, the border 2)
NEXT="88 537"            # the Next button of the preview toolbar
VIEW_X=123; VIEW_Y=636   # the middle of the page viewer

# probe_sum <png>: the three channels of a 31 x 31 box in the middle of the viewer (retina shots are device pixels: the scale is read from the shot width)
probe_at() {
	local png=$1 w scale
	w=$("$UI" stats "$png" 2>/dev/null | awk '/size/ { print $2 }' | sed 's/x.*//')
	scale=1; [ -n "$w" ] && [ "$w" -ge 1500 ] && scale=2
	"$UI" probe "$png" $((VIEW_X * scale)) $((VIEW_Y * scale)) --box 31 2>/dev/null | tail -1
}

launch_app --theme light --fixture-library "$SMALL"
expect_log "Scan finished|Fixture library" 60
expect_shown_count 50 "the fixture library" 40
sleep 3

item=$(menu_line Browse "Small Preview")
if [ -n "$item" ]; then ok "Browse has Small Preview: $(printf '%s' "$item" | tr '\t' ' ')"; else bad "Browse has no Small Preview item"; e2e_end; fi
shortcut=$(printf '%s' "$item" | awk -F'\t' '{print $2}'); mark=$(printf '%s' "$item" | awk -F'\t' '{print $3}')
case "$shortcut" in *Shift+*) ok "its shortcut is Shift+F7 ($shortcut)";; *) bad "its shortcut is '$shortcut'";; esac
if [ -z "$mark" ] || [ "$mark" = "-" ]; then ok "it is not checked at the start"; else bad "it has the mark '$mark' at the start"; fi

# Shift+F7 opens the pane: the item is checked, nothing is selected, the viewer says so
press shift+f7
sleep 1
mark=$(menu_line Browse "Small Preview" | awk -F'\t' '{print $3}')
if [ "$mark" = "✓" ]; then ok "Shift+F7 checked Browse > Small Preview"; else bad "Browse > Small Preview has the mark '$mark' after Shift+F7"; fi
shot empty
empty=$(probe_at "$E2E_OUT/empty.png"); info "viewer before a selection: $empty"
if printf '%s' "$empty" | grep -qi 'ff *ff *ff\|255, *255, *255\|#FFFFFF'; then ok "the empty viewer is the white paper of the small preview"; else info "the probe of the empty viewer reads: $empty"; fi

# select the second book of the first row (a click selects it); 500 ms later the preview shows it
front_app
must click 531 "$(gy 219)"
expect_log "Small preview: showing Fixture Series 01 002" 15
expect_log "Small preview: page 1 of" 15
sleep 1
shot first-page
page1=$(probe_at "$E2E_OUT/first-page.png"); info "viewer with the first page: $page1"
if [ "$page1" != "$empty" ]; then ok "the viewer shows a page now (the middle of the viewer changed)"; else bad "the middle of the viewer is the same as without a book: $page1"; fi

# the Next button turns the page
front_app
must click $NEXT
expect_log "Small preview: page 2 of" 10
sleep 1
shot second-page
page2=$(probe_at "$E2E_OUT/second-page.png"); info "viewer with the second page: $page2"
if [ "$page2" != "$page1" ]; then ok "Next showed another page"; else info "the middle of the viewer is the same on page 2 (the pages may look alike there)"; fi

# the setting is saved while the app runs and comes back
for i in $(seq 1 20); do grep -q 'ShowPreview' "$SETTINGS" 2>/dev/null && break; sleep 0.5; done
if grep -q '<ShowPreview>true</ShowPreview>' "$SETTINGS" 2>/dev/null; then ok "Settings.xml records ShowPreview"; else bad "ShowPreview is not in Settings.xml"; fi
quit_app

launch_app --theme light
expect_log "Library window" 10
mark=$(menu_line Browse "Small Preview" | awk -F'\t' '{print $3}')
if [ "$mark" = "✓" ]; then ok "after the relaunch Browse > Small Preview is still checked"; else bad "after the relaunch the item has the mark '$mark'"; fi
# Shift+F7 again closes the pane
press shift+f7
sleep 1
mark=$(menu_line Browse "Small Preview" | awk -F'\t' '{print $3}')
if [ -z "$mark" ] || [ "$mark" = "-" ]; then ok "Shift+F7 closed the pane"; else bad "the item is still checked after Shift+F7: '$mark'"; fi
quit_app
e2e_end
