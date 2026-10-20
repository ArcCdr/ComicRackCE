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
