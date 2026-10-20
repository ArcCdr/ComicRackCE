#!/usr/bin/env bash
# reader-keys: page-turn keys, the fit and layout shortcuts and the View menu radio marks of a reader window
# (mvp-acceptance.md 5.2, 5.10, 5.11). The book is opened with --read-book (24 pages, no library needed).
. "$(dirname "$0")/../lib.sh"; e2e_begin
BOOK="$FIXTURES/BD-reader/Amargo/Amargo - 01 - Le bal des vautours.cbz"; need "$BOOK"

launch_app --read-book "$BOOK"
expect_log "Reader check: .*pages" 40
pages=$(printf '%s' "$LAST_OUT" | sed -n 's/.*, \([0-9]*\) pages.*/\1/p')
ok "book has $pages pages"
sleep 1
front_app
R=$(other_window_id)
[ -n "$R" ] || { bad "no reader window"; e2e_end; }
ok "reader window id $R"
shot page-first "$R"
# The reader's own menu bar replaces the library's while the reader is the front window.
before=$(log_count '\] Page turn ')
for i in 1 2 3 4 5; do press right; sleep 0.6; done
sleep 1
after=$(log_count '\] Page turn ')
if [ $((after - before)) -eq 5 ]; then ok "Right x5 logged 5 Page turn lines"; else bad "Right x5 logged $((after - before)) Page turn lines"; fi
press left; sleep 0.8
after2=$(log_count '\] Page turn ')
if [ $((after2 - after)) -eq 1 ]; then ok "Left logged one more Page turn line"; else bad "Left logged $((after2 - after)) Page turn lines"; fi
shot page-after-turns "$R"

# Digit shortcuts go through physical key codes (US positions 1=18 2=19 3=20 5=23): on this Mac's AZERTY layout the character
# "3" needs Shift, so the character-based "key cmd+3" would send another shortcut. A radio item carries a mark when selected.
mark_of() { menu_line View "$1" | awk -F'\t' '{print $3}'; }
expect_marked() { # <item> <what>
	if [ -n "$(mark_of "$1")" ]; then ok "$2: $1 is marked"; else bad "$2: $1 is not marked"; fi
}
expect_unmarked() {
	if [ -z "$(mark_of "$1")" ]; then ok "$1 lost its mark"; else bad "$1 is still marked"; fi
}
press cmd+code20; sleep 0.8
expect_marked "Fit Width" "Cmd+3"; expect_unmarked "Best Fit"
press cmd+code23; sleep 0.8
expect_marked "Fit Height" "Cmd+5"; expect_unmarked "Fit Width"
press cmd+code19; sleep 0.8
expect_marked "Best Fit" "Cmd+2"; expect_unmarked "Fit Height"
press opt+cmd+code19; sleep 1
expect_marked "Two Pages" "Option+Cmd+2"; expect_unmarked "One Page"
shot two-pages "$R"
press opt+cmd+code18; sleep 0.8
expect_marked "One Page" "Option+Cmd+1"; expect_unmarked "Two Pages"
press cmd+w; sleep 1
check_log "Reader closed: .*Le bal des vautours" "Cmd+W closed the reader and logged the page"
quit_app
e2e_end
