#!/usr/bin/env bash
# info-panel (P5.6b): Browse > Info Panel (Shift+F9) is the container for the pages that scripts add (the original has no page of its own). Without a page the item is
# disabled and Shift+F9 does nothing; with the hidden option --test-info-page (a page that counts the selected books) the item is enabled, Shift+F9 opens the panel under the
# browser (the item is checked), 500 ms after a selection settled the page is given the books, the setting is saved (Settings.xml) and comes back after a relaunch.
. "$(dirname "$0")/../lib.sh"; e2e_begin
SMALL="$FIXTURES/small"; need "$SMALL"
SETTINGS="$E2E_DATA/Data/Settings.xml"

# --- no page: the item is there and disabled
launch_app --theme light
expect_log "Library window" 30
sleep 2
item=$(menu_line Browse "Info Panel")
if [ -n "$item" ]; then ok "Browse has Info Panel: $(printf '%s' "$item" | tr '\t' ' ')"; else bad "Browse has no Info Panel item"; e2e_end; fi
shortcut=$(printf '%s' "$item" | awk -F'\t' '{print $2}'); mark=$(printf '%s' "$item" | awk -F'\t' '{print $3}'); enabled=$(printf '%s' "$item" | awk -F'\t' '{print $4}')
case "$shortcut" in *Shift+*) ok "its shortcut is Shift+F9 ($shortcut)";; *) bad "its shortcut is '$shortcut'";; esac
if [ "$enabled" = "no" ]; then ok "it is disabled while no page exists"; else bad "it is '$enabled' with no page"; fi
press shift+f9
sleep 1
mark=$(menu_line Browse "Info Panel" | awk -F'\t' '{print $3}')
if [ -z "$mark" ] || [ "$mark" = "-" ]; then ok "Shift+F9 changed nothing"; else bad "the item has the mark '$mark' after Shift+F9 with no page"; fi
quit_app

# --- with the test page
launch_app --theme light --fixture-library "$SMALL" --test-info-page
expect_log "Scan finished|Fixture library" 60
expect_shown_count 50 "the fixture library" 40
sleep 3
item=$(menu_line Browse "Info Panel")
enabled=$(printf '%s' "$item" | awk -F'\t' '{print $4}'); mark=$(printf '%s' "$item" | awk -F'\t' '{print $3}')
if [ "$enabled" = "yes" ]; then ok "the item is enabled with the test page"; else bad "the item is '$enabled' with the test page"; fi
if [ -z "$mark" ] || [ "$mark" = "-" ]; then ok "it is not checked at the start"; else bad "it has the mark '$mark' at the start"; fi
shot closed

press shift+f9
sleep 1
mark=$(menu_line Browse "Info Panel" | awk -F'\t' '{print $3}')
if [ "$mark" = "✓" ]; then ok "Shift+F9 checked Browse > Info Panel"; else bad "Browse > Info Panel has the mark '$mark' after Shift+F9"; fi
expect_log "Info panel: Test Page shows 0 book" 10
shot open

# a click selects a book; 500 ms later the page is given it
front_app
must click 531 "$(gy 219)"
expect_log "Info panel: Test Page shows 1 book" 15
shot selected

for i in $(seq 1 20); do grep -q 'ShowInfo' "$SETTINGS" 2>/dev/null && break; sleep 0.5; done
if grep -q '<ShowInfo>true</ShowInfo>' "$SETTINGS" 2>/dev/null; then ok "Settings.xml records ShowInfo"; else bad "ShowInfo is not in Settings.xml"; fi
quit_app

# --- relaunch: still open
launch_app --theme light --test-info-page
expect_log "Library window" 10
mark=$(menu_line Browse "Info Panel" | awk -F'\t' '{print $3}')
if [ "$mark" = "✓" ]; then ok "after the relaunch Browse > Info Panel is still checked"; else bad "after the relaunch the item has the mark '$mark'"; fi
press shift+f9
sleep 1
mark=$(menu_line Browse "Info Panel" | awk -F'\t' '{print $3}')
if [ -z "$mark" ] || [ "$mark" = "-" ]; then ok "Shift+F9 closed the panel"; else bad "the item is still checked after Shift+F9: '$mark'"; fi
quit_app
e2e_end
