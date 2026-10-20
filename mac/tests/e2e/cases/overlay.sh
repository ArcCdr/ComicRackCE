#!/usr/bin/env bash
# overlay: Ctrl+Option+Cmd+P toggles the performance overlay: Window menu check mark, a Perf line in the log within 12 s, off again
# (mvp-acceptance.md 6.1, 6.2, 6.5).
. "$(dirname "$0")/../lib.sh"; e2e_begin

launch_app
mark() { menu_line Window "Show Performance Overlay" | awk -F'\t' '{print $3}'; }
[ -z "$(mark)" ] && ok "overlay is off at the start (no check mark)" || bad "overlay is already on at the start"
press ctrl+opt+cmd+p
sleep 1
if [ -n "$(mark)" ]; then ok "Ctrl+Option+Cmd+P: the menu item is checked"; else bad "Ctrl+Option+Cmd+P: the menu item is not checked"; fi
shot overlay-on
expect_log "Perf: fps" 14
press ctrl+opt+cmd+p
sleep 1
if [ -z "$(mark)" ]; then ok "Ctrl+Option+Cmd+P again: the check mark is gone"; else bad "the check mark stayed after the second toggle"; fi
n=$(log_count 'Perf: fps')
sleep 12
n2=$(log_count 'Perf: fps')
if [ "$n" = "$n2" ]; then ok "no Perf line while the overlay is hidden (12 s)"; else bad "$((n2 - n)) Perf line(s) while the overlay was hidden"; fi
shot overlay-off
quit_app
e2e_end
