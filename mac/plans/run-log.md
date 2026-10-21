# Run log

One line per task run by the orchestrator (`mac/plans/RUNBOOK.md` §2 step 6), and one per review (its commit is the `{FROM}` of the next review).

| Date time | Task | Result | Commit | Minutes | Note |
|---|---|---|---|---|---|
| 2026-10-17 | P0.1 | done | (planning commit) | | `mac/tools/verify.sh` written and run in the planning session: 781 fast tests pass; full-port.md, RUNBOOK.md, OWNER-ACTIONS.md, UPSTREAM-SYNC.md created |
| 2026-10-17 21:47 | P0.2 | done | 6bafb51 |  | Plan and licence hygiene (GeneXus licence and not-adopted note, S1/S2 spike log, plan references, licence wording recorded) |
| 2026-10-17 22:42 | RV1 | done | c3a9ca1 |  | (commit subject says R1) Make the fast gate deterministic (flaky tests) (10 consecutive verify.sh runs pass; dispatcher setup retry, leaked-host guard, settl... |
| 2026-10-20 11:23 | P0.3 | done | 24461d2 |  | Warning hygiene for the linked sources (clean-build warning lines 1158 -> 52; shadow-only NoWarn; verify.sh --full INFO count) |
| 2026-10-20 11:30 | P0.4 | done | 21e2d53 |  | Replacement manifest, generic drift guard, replacement-sync.sh (38 manifest entries; hash, coverage and line-budget tests; three-way re-sync script) |
| 2026-10-20 11:41 | P0.6a | done | a772b3c |  | UI-drive tool (ui-drive.sh, WindowTool.swift, crcli image probe/stats; launched, drove, screenshotted and quit the real app; Finder-in-front key refused with... |
| 2026-10-20 12:08 | P0.6b | done | e465dc2 |  | E2E smoke suite (e2e.sh with 10 real-window cases; ui-drive key codes and texts; app menu named after the app; all 10 cases and bundle menu-structure/pdf-cb7... |
| 2026-10-20 12:11 | P0.8 | done | 8abf1c0 |  | Unattended loop run-plan.sh (one headless orchestrator run per task; dry run picks P0.8 then P1.2; allowlist fragment; verify PASS) |
| 2026-10-20 12:17 | P1.2 | done | 710540c |  | Windows P/Invoke tripwire (resolver on all Core assemblies logs Windows API calls once with the stack and counts them; v1 flow test finds 2 known paths for P... |
| 2026-10-20 12:24 | P1.1 | done | 512c9a3 |  | Replace Thread.Abort at its three Core sites (pass B patched copies of ThreadUtility, BackgroundRunner and ComicScanner report and abandon a stuck thread; Ma... |
| 2026-10-20 12:30 | P1.3 | done | 66147fa |  | Runtime gap sweep (Common Resources and FileUtility.PathGetCharType in pass B, DisableNTFS in ConfigureCore; the tripwire test now requires zero Windows calls) |
| 2026-10-20 15:39 | RV2 | done | 681e472 |  | (commit subject says R2) Fix the remaining flaky UI tests (scan cancel gated on an event, pixel baselines settled, library load awaited, memory compared with... |
| 2026-10-20 15:49 | P0.5a | done | 9072e9c |  | Upstream scan: file classification (mac/tools/upstream-scan sorts every file of a commit range into nine categories, with project-file package changes compar... |
| 2026-10-20 15:58 | P0.5b | done | cbb0c86 |  | Upstream scan: platform API delta (api-inventory --compare/--write-baseline against the committed mac/reports/api-baseline.json, with shim coverage and pass... |
| 2026-10-20 16:28 | P0.5c | done | b8688ac |  | upstream-sync-check.sh (one command after an upstream merge: files, packages, drift, build and tests, census, platform API; rehearsal merge exits 1 with find... |
| 2026-10-20 16:48 | P0.7a | done | 0aae762 |  | Golden tests on net10: database, lists, names, matchers (200-book synthetic library round trip, counts of 25 lists, 200 file names with both parsers, fixture... |
| 2026-10-20 16:54 | RV3 | done | 7f85422 |  | (commit subject says R3) Fix the GetIsoCulture race in the host (MacHost warms the language dictionary on one thread; CoreRaces workaround removed; 10 fresh... |
| 2026-10-20 17:02 | P0.7b | done-with-caveat | 3a027bf |  | Golden tests on net10: archives, pages, thumbnails (CBZ/CB7/PDF generated at test time, page byte hashes and 64-bit average hashes of pages and thumbnails, o... |
| 2026-10-20 17:27 | P3.H | done-with-caveat | 37358a2 |  | HiDPI test harness: 1x and 2x rendering (headless render scaling, 104 HiDpi tests for grid, covers, reader, draw operations and the Drawing stand-in; BookGri... |
| 2026-10-20 17:42 | P4.1 | done | f122cc3 |  | Spike S4: IronPython 2.7.12 on .NET 10 arm64 (verdict GO with a build-time patch of IronPython.dll: stock engine cannot create new-style classes on .NET 9+;... |
| 2026-10-20 18:28 | P4.2 | done-with-caveat | 1a782ba |  | Spike S6: Script UI modal loop and assembly identity (verdict GO: nested DispatcherFrame modal loop, Python Control subclass with OnPaint, Autonumber.py and... |
| 2026-10-20 19:00 | RV4a | done | 4998708 |  | Review fixes: plan tooling, safety scripts, docs (plan-status.sh with table, next task and run-log backfill; scoped allowlist; e2e exit codes 5 and 6; ui-dri... |
| 2026-10-20 19:17 | RV4b | done | c6c4854 |  | Review fixes: teardown, scanner wait, memory test, test strength (abandoned scanner marks the final save unchecked and raises no late notification; FolderSca... |
| 2026-10-20 19:21 | P5.0 | skipped | b0aa72b |  | Owner MVP notes intake (skipped, no notes in mvp-acceptance.md or OWNER-ACTIONS.md) |
| 2026-10-20 19:29 | P2.6 | done | ad071b9 |  | Bundle payload and TR bootstrap (Languages, Scripts, Help links, default lists, icons and textures ship next to the executable; language from the setting, th... |
| 2026-10-20 19:45 | P5.T1 | done | c1a99c5 |  | Theme tokens and appearance (Cr.* light and dark dictionaries checked against the original colour tables, toolstrip colours computed from the Windows 10 syst... |
| 2026-10-20 19:54 | P5.T2 | done | 1326e1b |  | Original icons and images (every resx image by its resource name through Icons.Get: 277 names in 5 projects linked into the app without copying, dark variant... |
| 2026-10-20 20:14 | P5.T3 | done-with-caveat | 276a365 |  | Parity tooling: view specs, ledger check, contact sheets (designer-dump, ledger-check in verify.sh with 197 uncovered items added to the ledger, --show/--fix... |
| 2026-10-20 21:00 | P5.T4 | done | b4f20ea |  | Base control styles and native pieces (toolbar, split button, tab bar, status bar, group box, sizable container, header, tree item, control themes from the C... |
| 2026-10-20 21:40 | P5.R1 | done | a28da48 |  | Restyle the MVP main window shell and menu bar (original menus and ledger shortcuts in the native bar, About fixed, browser toolbar, side panel with original... |
| 2026-10-20 22:12 | RV5 | done | 67566c0 |  | Chrome isolation: one swappable layer (ChromeVariants per element with a Native status bar as proof, IChromeElement controls with a theme per variant, CrTree... |
| 2026-10-20 22:57 | P5.R2 | done | cd5a9eb |  | Restyle the browser items (thumbnails drawn as ThumbRenderer draws them: shadow, frame, empty tile, caption with the selection style, dog-ear, ribbons, state... |
| 2026-10-21 00:34 | P5.R3 | done | c25247e |  | Restyle the reader chrome (tab strip with the reader toolbar, status bar, paper and page frame, page info, loading, message, visible-part and navigation over... |
| 2026-10-20 11:16 | RV9 | done | 0849bd2 |  | (commit subject says R9) parity and chrome plan amendment (D9; parity ledger 379 rows; chrome guidelines; 18 new tasks, 157 total) |
| 2026-10-21 00:58 | P5.1 | done-with-caveat | 05a2118 |  | Settings persistence and window state (Settings.xml with AppPreferences mirroring Settings.cs names and defaults, debounced atomic SettingsStore with .bad fa... |
| 2026-10-21 01:53 | P5.2 | done | 743082c |  | Live refresh of read state, counts and scan progress (LibraryWatcher collects the library's change events; Total, Unread and New counters as LibraryTreeSkin... |
| 2026-10-21 02:27 | P5.3 | done | c8a1f17 |  | Multi-selection (SelectionModel with the ItemView rules: Cmd-click, Shift-click, Shift+arrows, Cmd+A, Invert, Space, Delete and Cmd+Backspace request removal... |
| 2026-10-21 03:10 | RV6 | done | f4ec45f |  | Browser selection parity (first arrow key from item 0, rubber band with auto scroll and Shift/Cmd, drag the selection out as books and files; ui-drive drag,... |
| 2026-10-21 03:25 | P5.4 | done-with-caveat | 4d47453 |  | Browser toolbar and main window layout (toolbar complete in the original order with disabled slots, CrDropDownButton, Cmd+F focuses the quick search, e2e chr... |
| 2026-10-21 03:35 | P2.1 | done | 6bab160 |  | Shell services (MacShell: Trash through NSFileManager, Reveal and Open through open; ShellFile pass B rewrite sends deletes to the Trash through a handler th... |
| 2026-10-21 03:56 | RV7 | done | a61b4e5 |  | Review fixes (settings never overwrite an unreadable file, saves off the setter path with a sequence number, ui-drive drag re-checks, caps and signal release... |
| 2026-10-21 04:35 | RV8 | done | 42d9ff3 |  | Review fixes: parity and UI correctness (Arrange flips the sort order, toolbar overflow with the quick search at the right end, reader rating keys, last-open... |
| 2026-10-21 04:53 | RV9 | done | 8528688 |  | Launch.Start double OnHostOpened race (one announcement per host in Launch, first database access made on the opening thread, regression test) |
| 2026-10-21 11:50 | P5.32a | done | 287b96a |  | Main window hosts the reader: book tabs, Reader in own Window (tabs as NavigatorManager, reader area in the main window, browser dock at the bottom, tab menu... |
| 2026-10-21 12:27 | P5.5 | done-with-caveat | d294a6f |  | Grid context menu, part 1 (all items of contextMenuItems in order; Open, Info, My Rating with the stars editor and Quick Rating dialog, Mark as, Reveal in Fi... |
| 2026-10-21 13:03 | RV10 | done | 5ef3403 |  | Verify P5.5 on a healthy disk (full gate passes, no code change needed) |
| 2026-10-21 13:40 | P5.6a | done | 810e5e8 |  | Small preview pane (Browse > Small Preview Shift+F7: toolbar, page viewer on the reader, 500 ms timer, copy of the first selected book opened as DisableAll,... |
| 2026-10-21 14:27 | P5.6b | done | 2b3006e |  | Info panel container (Browse > Info Panel Shift+F9: tab strip of script pages, 162 high bottom dock, settings kept, selected books given to the showing page,... |
| 2026-10-21 15:15 | RV11 | done | 00021ed |  | Flaky fast gate, round 2 (per-test pixel and image counts, static switch guard, flaky-scan.sh; 6 consecutive gate runs pass in about 95 s, was 209 to 340 s) |
| 2026-10-21 15:23 | P2.4a | done | 0d99a06 |  | Bundle 7zz (fetch-7zz.sh with pinned SHA-256 from the official 7-Zip 26.05 release, ToolLocator.SevenZip, make-app copies and signs it, fixtures make --forma... |
| 2026-10-21 15:32 | P2.4b | done-with-caveat | 0f348db |  | ComicInfo write-back engine (pass B patched SevenZipEngine runs the bundled 7zz, crcli info show and write, CBZ CB7 and CBT written with pages byte-identical... |
| 2026-10-21 16:34 | P5.7a | done | 964c60f |  | Info editor: shell, Summary and Details (ComicBookDialog with the original's tabs, Previous/Next, OK/Cancel/Apply, 3D cover, value lists, spin buttons, list... |
| 2026-10-21 16:53 | P5.7b | done | e59bfe2 |  | Info editor: Plot and Notes, Catalog and Custom tabs (sub-tabs Summary, Notes, Review, list selectors, catalog fields and date boxes, custom value grid; Cata... |
| 2026-10-21 17:57 | P5.7c | done | 77c045f |  | Info editor: Pages, Colors and cover (page list with thumbnails, tiles and details, page menu, Order by Name, sorting, merge; Colors tab with the Core's colo... |
