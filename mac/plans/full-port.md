# macOS full port: master plan (after the MVP)

> **Status:** adopted 2026-10-17 by the owner's requirements (§1.1). The MVP track (`mac/plans/mvp-track.md`, M0 to M2.6) is merged on `macos` and the owner verified it ("performance very good"), so gates G1 and G2 are passed.
> **Amended 2026-10-20** by owner requirement R9 (feature parity and original chrome, §1.1, §2.11, D9): new tasks P5.T1 to P5.T5, P5.R1 to P5.R3, P5.32a to P5.32c, P5.33, P5.34, P5.22c, P7.A1 to P7.A4; UI tasks carry a `Parity:` line. The parity references are `mac/plans/chrome-guidelines.md` (tokens, fonts, metrics, icons, chrome decisions) and `mac/plans/parity-ledger.md` (every original feature, its Mac task and status).
> **Relation to other files:** `MACOS_NATIVE_PATH.md` keeps the binding decisions (§0 D1 to D9), the architecture (§5), the v1 scope (§6) and the guardrails (§9.2). `mac/plans/mvp-track.md` stays the record of M0 to M2.6 and of the facts found there (§3); its §4 (rules) and §10 (how to run) are **superseded** by §2 of this file. `mac/plans/RUNBOOK.md` says how an orchestrating session runs this plan unattended. `mac/plans/OWNER-ACTIONS.md` is the only place where the owner is asked for something. `mac/UPSTREAM-SYNC.md` is the owner's merge procedure.
> **Task status:** every task block in §7 has a `Status:` line; it is the source of truth. The table below is a summary that the orchestrator updates after each task.

Run state: RUNNING (2026-10-20, orchestrator; next task: RV5b)

| Phase | Tasks | Done | Next todo | Notes |
|---|---|---|---|---|
| P0 Safety net and tooling | 12 | 12 | - | fast gate, replacement manifest, upstream harness, UI automation, goldens, unattended loop |
| P1 Core runtime gaps | 3 | 3 | - | zero-edit fixes through pass B replacements |
| P2 Platform pieces | 9 | 1 | P2.1 | shell, codecs, ComicInfo write, payload, TR |
| P3 Fidelity, performance, CI | 12 | 1 | P3.9 | includes M1.5c, M1.7, M1.8 and the HiDPI harness P3.H |
| P4 Scripting and Script UI layer | 36 | 2 | P4.3 | S4 and S6 spikes first, go/no-go recorded |
| P5 Screens (main window parity) | 66 | 11 | P5.4 | waves B, C, D; theme and chrome foundation (P5.T*) and restyle (P5.R*) open wave B; P5.T5 optional |
| P6 Packaging and release | 7 | 0 | P6.1 | real signing needs checkpoint C5 |
| P7 Hardening, parity audits, v1 acceptance | 12 | 0 | P7.1 | parity audits P7.A1 to P7.A4; ends with checkpoint C6 |
| RV Review fixes and upstream sync | 8 | 6 | RV5b | `RV<n>` tasks from reviews and `U<date>.<n>` tasks from upstream merges; they run first |
| **Total** | **165** | **36** | | estimate in §10 |

## RESUME (every session starts here)

1. **Orchestrator** (the session the owner starts with the resume prompt in `mac/plans/RUNBOOK.md` §1): follow `RUNBOOK.md`. It picks the next task from §6, runs it in one fresh subagent, verifies, commits and continues until a checkpoint (§4) or a STOP condition (§2.6).
2. **Task session (subagent)**: read §2, §3, your task block in §7 and the blocks it depends on, and `MACOS_NATIVE_PATH.md` §0 and §9.2. Do that one task. The exact prompt is `RUNBOOK.md` §3.
3. **After an upstream merge** by the owner: `mac/UPSTREAM-SYNC.md`.
4. **Owner**: open `mac/plans/OWNER-ACTIONS.md`. The "Inbox" lists what is asked of you; each block ends with the prompt to resume.

Where things are: decisions §1, rules §2 (parity and chrome policy §2.11), facts §3, checkpoints §4, harness designs §5, order §6, tasks §7, the MVP open-item mapping §8, discovered work §9, estimate §10. Parity references: `mac/plans/parity-ledger.md`, `mac/plans/chrome-guidelines.md`.

---

## 1. Owner requirements and settled decisions

### 1.1 Requirements (owner, 2026-10-17, binding)

| # | Requirement | Where it is implemented |
|---|---|---|
| R1 | Full port per `MACOS_NATIVE_PATH.md` D1 to D7, v1 scope §6.1 including Python scripts with the Script UI Compatibility Layer (§5.9). Deferred items (§6.3) stay deferred. | §7 P1 to P7 |
| R2 | Work as unattended as possible; ask the owner only at a few consolidated checkpoints; intermediate owner tests only when unavoidable. | §4, `RUNBOOK.md` |
| R3 | The owner merges `upstream` (maforget/ComicRackCE) into `macos` regularly with minimal conflict work. All macOS work stays under `mac/` (zero-edit rule). One long-lived branch `macos`; tasks commit directly on it; no per-task branches, no PRs. | §2.1, §2.2, `mac/UPSTREAM-SYNC.md` |
| R4 | Tests validate unattended work and every upstream merge: an upstream sync harness (build, all tests, census, drift tests, new platform API, new files, actionable summary) and a merge procedure for the owner. | P0.4, P0.5a to c, §5.1, §5.2 |
| R5 | Every owner action has clear instructions (what, expected result, what is next, exact resume prompt). | `OWNER-ACTIONS.md`, `RUNBOOK.md` §6 |
| R6 | Screen Recording and Accessibility are granted to VS Code and the GUI session is unlocked: sessions may launch, drive and screenshot the real app. A reusable UI-automation tool and an end-to-end smoke suite verify real-window behaviour. | P0.6a, P0.6b, §5.3 |
| R7 | Open decisions are settled with defaults (§1.2), not asked. | §1.2 |
| R8 | Known owner inputs (Developer ID and notarization credentials, a real CBR, a plugin corpus) are explicit checkpoints. | §4 C1, C4, C5 |
| R9 | (2026-10-20) (a) **Features stay exactly identical to the Windows app**: behaviours, defaults, menus and commands with their semantics, dialogs and fields, list and smart-list behaviour, settings, keyboard shortcuts (Ctrl mapped to Cmd where macOS convention says so, otherwise kept), file formats. A deviation is allowed only where macOS makes a Windows feature impossible; each one is listed and justified in the parity ledger. (b) **The look stays as close as possible to the WinForms app**: ComicRack's own chrome (theme colours, toolbar and button look, original icons, browser/list/tile look, dialog layouts, reader chrome, status bar, font size proportions) is kept wherever it does not conflict with native macOS features that clearly make more sense native (scrollbars, title bar and traffic lights, menu bar, standard dialogs, full screen, context-menu rendering, text editing, focus rings, Dock and app menu). When in doubt the original look wins; every native-over-original choice is a row in the chrome decisions table. | D9; §2.11; `mac/plans/parity-ledger.md`; `mac/plans/chrome-guidelines.md`; tasks P5.T*, P5.R*, P5.32a to c, P5.33, P5.34, P5.22c, P7.A1 to A4; C2 and C6 contact sheets |

### 1.2 Decisions settled with defaults (recorded 2026-10-17; the owner may override any of them in `OWNER-ACTIONS.md`)

| # | Question | Decision |
|---|---|---|
| DD1 | Thumbnail decoder | Keep the safe route of M2.2a: decode the stored JPEG/PNG bytes (`ThumbnailImage.Data`) with Skia; never wrap Core-owned bitmaps (owner delegated). The same rule holds for pages (M2.3a). |
| DD2 | MVP names | `ComicRack CE (MVP)`, `com.comicrackce.mvp` and the MVP data folders stay until P6.1, which renames them for v1 (app name `ComicRack CE`, data folder `~/Library/Application Support/ComicRack Community Edition`, default bundle id `org.comicrackce.ComicRack`, confirmed at C5) and migrates an existing MVP library on first launch. |
| DD3 | Compatibility with the Windows app | Through XML. The library is `ComicDb.xml` in the unchanged Core schema; the Mac app imports a Windows library with path remapping (done, M2.1) and can export one for Windows with the reverse mapping (P5.28b). Mac settings are their own XML file; the portable parts of a Windows `Config.xml` can be imported (P5.28b). The Mac app never writes a Windows library in place. |
| DD4 | Platform | arm64 only, macOS 14 or later (D5). |
| DD5 | Upstream edits | None. Changes to original files are made as pass B replacements under `mac/` (§2.3), never in place. An exception needs owner approval at a checkpoint (§2.2). Fixes worth upstreaming are written as patch files in `mac/upstream-proposals/` for the owner to send. |
| DD6 | Net48 characterization tests (P0.2/P0.3 of §9.3) | Not possible without Windows. Replaced by net10 golden tests (P0.7a/b). Because no original file is edited, the Windows build cannot change. An optional Windows recorder (P4.16) is v1-optional. |
| DD7 | Closing the library window | The app keeps running (Mac convention, ledger deviation of type native); Window > Library and a Dock click reopen it (P5.10b). Amended by R9: the shortcut is Option+Cmd+0, because Cmd+0 is the original Right to Left (Ctrl+0, ledger MD.12). |
| DD8 | Reader defaults | WinForms defaults: single page, Best Fit ("Fit All"), right to left from the book's manga flag, swipe at the edge of a zoomed page turns the page; all remembered in preferences (P5.1, P5.23d). |
| DD9 | Cover cache | 256 MB stays the default and becomes a preference (P5.22b); P3.3 may lower it if the Release numbers say so. |
| DD10 | Archive writing (Q3) | Bundle the official 7-Zip macOS console binary `7zz` and keep the Core's own 7z path for ComicInfo writes (P2.4a/b); no RAR creation ever. |
| DD11 | Codec route (Q2) | Decided by spike P2.3a; default ImageIO for decode. |
| DD12 | Script corpus (Q10) | Bundled samples plus public plugins that can be downloaded; the owner's list at C1 if given. Only scan results are committed (`mac/scripting/compat-usage.md`). |
| DD13 | `.compat` overlays (Q11) | Allowed as a last resort, maintained in this fork (P4.14). |
| DD14 | HTML panel bridge (Q12) | Small in-house WKWebView wrapper (P4.11c). |
| DD15 | Details-view control | A custom virtualized table on `ItemsRepeater` (already used). Avalonia's `TreeDataGrid` licence must be checked before any use (P5.11a records it); no paid component (D1 note). |
| DD16 | Workspaces and tabs of the WinForms main window | **Superseded by R9 (2026-10-20):** ported. The main window hosts the reader with book tabs (P5.32a), the browser docks and has its own tabs (P5.32b), workspaces are saved and applied (P5.32c); "Open in New Window" stays (P5.16a). |
| DD17 | Translations of new strings | Existing TR translations are reused where the key exists; new Mac-only keys ship in English until translators add them (P5.27a). |
| DD18 | Web comics (`.cbw`) and Ghostscript | **Amended by R9:** "Update Web Comics" and its startup option are ported (Core feature, P5.19b, ledger MF.08, PB.04); Ghostscript stays whatever the Core does. |
| DD19 | Keyboard shortcut editor of the Windows Preferences | **Superseded by R9:** ported as in the original (Preferences > Reader > Keyboard, import/export of the original layout files, P5.22c). |
| DD20 | Keyboard shortcut mapping (R9) | Ctrl becomes Cmd, Alt becomes Option, Shift, function keys and the reader's single keys stay. A chord that macOS reserves or that is a standard Mac command with a different meaning gets Option added, then Ctrl+Cmd if that is taken too. Mac aliases are added where the command has a Mac standard (Cmd+, Ctrl+Cmd+F, Cmd+Shift+Z, Cmd+?, Cmd+W for Close, Cmd+Backspace for Remove). The full list is `mac/plans/chrome-guidelines.md` §6; every value is decided in the ledger's "Win → Mac" column, which tasks implement and P7.A1 checks. Replaces the shortcut bullet of the MVP rules. |
| DD21 | Chrome defaults (R9 b) | The defaults of `mac/plans/chrome-guidelines.md` §7 (C-* rows): native for window frame, menu bar, scrollbars, standard panels and alerts, full screen, popup-menu rendering, text editing, focus rings, Dock and app menu, appearance switching, system font family; original for everything ComicRack draws itself (tokens, toolbars, tabs, status bar, item views, trees, headers, splitters, group boxes, dialogs and their button order, Preferences layout, dialog controls, reader chrome, icons, font sizes). The owner confirms or flips the rows with a flip cost on the C2 contact sheet. |
| DD22 | R9 and the D4 scope | R9 does not widen D4: device sync, library sharing (remote libraries, server settings, wireless setup) and DjVu stay deferred; their ledger rows say `deferred (D4)`. Everything else in the original is in v1, including what earlier defaults had dropped (workspaces, tabs, keyboard editor, minimal UI, realistic page display, splash, tray equivalent, web comics). |

---

## 2. Conventions (supersede `mvp-track.md` §4 and §10)

The MVP rules about one branch per task (`mac/<task-id>`, `mac/mvp`), PR descriptions and merges (`mvp-track.md` §4 rule 2, §10.1 to §10.5) no longer apply. History is not rewritten; those branches and merge commits stay as they are.

### 2.1 Branch and commits
- **One long-lived branch: `macos`.** Every task commits directly on it. No other branches, no worktrees, no PRs, no rebases, no force pushes.
- One task = one commit (plus, rarely, one fix-up commit by the orchestrator). Message: `mac: <TASK-ID> <title> (<one-line result>)`, ending with the Co-Authored-By trailer that the session's environment specifies.
- Sessions **never push** and **never merge**. The owner pushes when he wants CI, and merges upstream himself (`mac/UPSTREAM-SYNC.md`). Sessions may run `git fetch upstream` (it only updates remote refs).
- Allowed git commands for sessions: `status`, `diff`, `log`, `show`, `add`, `commit`, `stash push -u -m` / `stash list` (to park the changes of a failed attempt), `fetch upstream`, `rev-parse`, `merge-file` (P0.4 tool), `restore` limited to paths under `mac/`. Never `reset --hard`, `clean -fd` outside `mac/artifacts`, `checkout` of another branch, `merge`, `rebase`, `push`.

### 2.2 Zero-edit rule (rule 1, still in force)
- No file outside `mac/` changes, except `MACOS_NATIVE_PATH.md` and `.github/workflows/mac-*.yml`. `mac/tools/verify.sh` checks it mechanically against `upstream/master` (if the ref exists) or `master`, including uncommitted files.
- Because nothing outside `mac/` is edited, an upstream merge cannot conflict and the Windows net48 build cannot change.
- **Exception process:** if a change to an original file looks unavoidable and no pass B replacement can do it (§2.3), the session STOPs that task (status `blocked (needs edit: <file>)`), writes the proposal (exact diff, why a replacement cannot work, upstreamable yes/no) to `mac/upstream-proposals/<TASK-ID>.md`, adds an owner block to `OWNER-ACTIONS.md`, and the orchestrator continues with other tasks. An approved edit is logged in `mac/EDITS.md` (`| \`path\` | reason | upstreamable |`), which `verify.sh` reads as an allow list.

### 2.3 How original code is changed without editing it
| Kind | Use when | How | Drift guard |
|---|---|---|---|
| `excluded` | The file is Windows-only or deferred and nothing needs it | `Exclude=` in the shadow project's `Sources.props` / csproj | P0.4 manifest: the original's hash |
| `stub` | An excluded type is still referenced | Same full type name in `mac/<project>/Replacements/*.cs`, minimal body | manifest hash |
| `patched-copy` | A few lines of an original must differ on macOS (example: `CacheManager.cs`, one line) | Copy into `Replacements/PassB/`, change the minimum, `Compile Remove` the original in pass B only | manifest hash + changed-line budget; re-synced by `git merge-file` (§5.1) |
| `rewrite` | The body must differ (example: `PdfiumReaderEngine.cs`, `FileSystemEnumerable.cs`) | Same type and public surface in `Replacements/PassB/` | manifest hash; upstream diff reviewed by a session |

Every replacement gets a manifest entry in `mac/replacements.json` (P0.4) in the same commit. Prefer a patched copy over a rewrite: it re-syncs automatically.

### 2.4 Gates
- **Fast gate** = `mac/tools/verify.sh` (disk guard, rule 1, build of `mac/ComicRack.Mac.slnx`, all tests except `Slow` and `E2E`, and from P5.T3 on the parity ledger check; about 1 to 2 minutes incremental). Every task's acceptance includes it.
- **Full gate** = `mac/tools/verify.sh --full` (adds `Slow` tests), then `mac/tools/e2e.sh` (after P0.6b; exit 4 = screen locked, recorded as skipped), then `dotnet run --project mac/tools/census -- --pass B` (0 errors) and `dotnet run --project mac/tools/drawing-coverage` (PASS), then after P0.5c `mac/tools/upstream-sync-check.sh --no-merge`. The orchestrator runs it every 5 tasks and at the end of each wave (`RUNBOOK.md` §4).
- A task is done only when its acceptance commands pass. A check that needs the owner is recorded under the task ("Owner check") and folded into a checkpoint, never faked.

### 2.5 Test categories
- Default (no trait) = fast; must stay deterministic (no timing races: assert sets, not orders, for parallel work; see the P0.1 facts).
- **No timing-dependent assertion without a controllable clock or gate** (R2). A test never relies on work being slower or faster than its own polling: park the worker on an event the test releases (example: `WindowTests.Pressing_Cancel_...` holds the scanner in front of its 6th file), advance a `ManualTime`, or wait for a condition with a deadline that only bounds a hang. A count of process-wide resources (pixel blocks, live images) is compared with a baseline taken after `Gc.SettleLive()` and checked after `Gc.WaitForLive(baseline)`; a memory check compares points of the same run (cache full against end), never the process with a start value that earlier tests influence.
- **Every new custom drawn control or draw operation adds a 1x/2x test with the HiDPI harness** (P3.H): `mac/tests/ComicRack.Mac.Tests/HiDpi/` (`Scales.Main` = 1x and 2x, `Scales.All` adds 1.5x and 3x for cheap tests; `Dpi.Apply` moves a shown window to a scale, `Dpi.Capture` gives the frame in device pixels as a `Raster`; `[Trait("Category", "HiDpi")]`). The test states what is constant in logical units (geometry, hit testing, pan and zoom math), what scales (the frame, text and border widths in device pixels) and that edges are crisp (no blended pixel) at every scale. Existing controls: `GridHiDpiTests`, `ReaderHiDpiTests`, `DrawingHiDpiTests`.
- `[Trait("Category", "Slow")]` for tests over 20 s (today: the full scroll over the fixture library, 76 s).
- `[Trait("Category", "E2E")]` for .NET tests that need the real window server (rare; most e2e checks are shell cases under `mac/tests/e2e/cases/`, §5.3).
- Tests that need `mac/fixtures/*` or owner samples skip themselves when the files are missing (existing `FixtureFact` pattern in `mac/tests/ComicRack.Mac.Tests/Support.cs`).
- New test and tool projects that reference SkiaSharp set `<RuntimeIdentifier>osx-arm64</RuntimeIdentifier>` and `<SelfContained>false</SelfContained>` (about 275 MB of Windows DLLs otherwise) and are added to `mac/ComicRack.Mac.slnx`.

### 2.6 STOP rules (the task ends, the orchestrator decides what next)
1. Free space below 3 GB on the home volume after deleting `mac/**/bin`, `mac/**/obj` and `mac/artifacts/{verify,ui-drive,upstream-sync}` (`df -h ~`). Budgets: `mac/fixtures` at most 1.5 GB unless a task says otherwise; one `.app` at a time.
2. A change outside `mac/` looks necessary (§2.2).
3. The acceptance needs something only the owner has (hardware, credentials, a sample, a judgement): finish everything else, mark `done-with-caveat` or `blocked (Cn)`, add the owner part to the checkpoint.
4. A STOP condition written in the task block triggers.
5. Anything would touch the owner's real data: `~/Library/{Application Support,Caches,Logs}/ComicRack*`, `/Users/vince/dev/ComicRackAPI`, or files outside the repo and the temp folders. Always use `--data-dir` temp folders and copies.
6. Two serious attempts failed the acceptance: stop, propose a split in §9 (rule "two failures => split").

### 2.7 Scope and records
- Do only what the task lists. New work goes to §9 as `- [<TASK-ID>] description`.
- Facts later tasks need go to §3 as a "Facts from <TASK-ID>" bullet list, in the same commit.
- A spike's Status carries its verdict: `done DATE, verdict GO` or `done DATE, verdict NO-GO (see report)`. A dependency written `P4.1 (GO)` is satisfied only by a GO verdict; a NO-GO raises checkpoint C3.
- Update the task's `Status:` line: `todo`, `doing (started DATE)`, `done DATE`, `done-with-caveat DATE (what is unverified)`, `blocked (reason)`, `skipped (reason)`, `split -> IDs`.
- No model identifiers or session links in code, comments or docs (guardrail 8). Tier names in the plan files are fine.

### 2.8 App code rules (unchanged from the MVP)
- Every subscription to a Core event goes through `UiDispatch` (`mac/ComicRack.Mac/UiDispatch.cs`); the deliberate exception is documented in `mac/README.md` (scanner cancel).
- Nothing slow on the UI thread: list evaluation, catalog builds, file IO and decodes run on the pool.
- Pixels: never wrap Core-owned bitmaps (DD1); `CoverImage`/`PixelBlock` reference counting for anything drawn.
- Log through `AppLog`. New UI strings go through TR (after P2.6).
- Shortcuts: DD20 (R9). The value of every shortcut is the one in the ledger's "Win → Mac" column; a task that finds a clash not foreseen there applies the DD20 rule, writes the value into the ledger row and reports it. Function keys stay bound (they need fn on laptops).

### 2.9 Rollback (default for every task)
Each task is one commit on `macos` that touches only `mac/` (and possibly `MACOS_NATIVE_PATH.md` or `.github/workflows/mac-*`): roll back with `git revert <commit>`. Windows is never affected. A task block names a different rollback only when it has one (data migration, bundle identity).

### 2.10 UI automation safety (R6)
- Only through `mac/tools/ui-drive` (P0.6a). It types or clicks only while the app it launched is frontmost, never into another app; it refuses to start if an owner-run copy of the app is running; it always uses a temp `--data-dir`.
- Never change system settings except, when `E2E_ALLOW_APPEARANCE=1`, the light/dark appearance, which it restores.
- Never scan protected folders (`~/Documents`, `~/Desktop`, `~/Downloads`, external or network volumes): that would raise TCC prompts in the owner's session. Use folders under the repo or `$TMPDIR`.
- The grants belong to VS Code: run sessions from VS Code (integrated terminal or the Claude Code extension). From another terminal app, `screencapture` and System Events fail (exit 4 "no GUI access").

### 2.11 Parity and chrome policy (R9, D9; binding for every UI task)

Applies to every task that adds or changes something a user sees: all P5 tasks, P4.5, P4.10a to P4.11e, P5.T*, P5.R*, P6.1 (icon), P7.1, P7.5, P7.A*. The task block's `Parity:` line names the ledger rows, view specs and chrome decisions that apply.
1. **Derive a view spec from the WinForms original before coding.** For every original form, user control or custom-drawn view the task touches, write or extend `mac/specs/views/<OriginalTypeName>.md` (content and the `designer-dump` helper: `mac/plans/chrome-guidelines.md` §8). The original files are read, never edited.
2. **Reproduce layout, fields and labels exactly**: same groups, order, controls, defaults, enable rules and button set; every label, menu text and message through the **original TR key** (section = form type name, key = control name, `ComicRack/Output/Languages/<culture>/<Form>.xml`), never a retyped English string. New Mac-only text gets a new key (DD17) and a `mac-only` ledger row.
3. **Same behaviour and defaults**: commands do what the original's handler does (read `ComicRack/MainForm.cs` command bindings, the view's code-behind and the Core); option defaults equal `ComicRack/Config/Settings.cs` (tested in P5.1); shortcuts per the ledger (DD20).
4. **Original icons and images** only, through `Icons.Get("<ResourceName>")` (P5.T2); no SF Symbols or new artwork for ComicRack chrome.
5. **One shared theme**: colours only from the `Cr.*` tokens of `mac/ComicRack.Mac/Themes/` (P5.T1, chrome-guidelines §2), fonts and metrics from chrome-guidelines §3 and §4, control looks from the P5.T4 styles. No literal colours in views (a lint test from P5.T4 enforces it).
6. **What stays native** is exactly chrome-guidelines §7 (window frame, menu bar, scrollbars, standard panels and alerts, full screen, popup-menu rendering, text editing, focus rings, Dock and app menu, appearance switching). Any other native-over-original choice needs a new §7 row with rationale, added by the task and listed in its report.
7. **Ledger**: in the same commit, the task sets each of its ledger rows to `done <date>`, `deviation` (with `impossible: ...` or `native: <C-id>`) or leaves `todo` with the task that will finish it. `mac/tools/ledger-check` (P5.T3; part of `verify.sh` from then on) rejects malformed rows and original menu items without a row.
8. **Visual check**: headless token and metric assertions, ui-drive shots in light and dark, and a contact sheet for the views the task changed (chrome-guidelines §9); the sheet path goes into the task's report and §3 facts. Look-and-feel verdicts belong to the owner and are collected on the C2 and C6 sheets, never claimed by a session.
9. **Additions are allowed only as additions**: a Mac convenience (an alias shortcut, a trackpad gesture, a Mac group in Preferences) never replaces or hides an original feature and always gets a `mac-only` row.
10. **Chrome only in the chrome layer (R10, RV5)**: everything that gives the app its look lives in `Themes/`, `Controls/` and `Platform/` (`mac/plans/chrome-architecture.md`), so that single elements can be swapped to a native look through `ChromeVariants` without touching a view. Views and view models use semantic parts only: `Cr*` controls, `cr-*` style classes, `Cr.*` tokens, `CrIcon`/`Icons.Get`. They contain no colour, font, corner radius, opacity or border width, no inline `<Style>`, template or theme, no picture path and no drawing namespace (`ChromeArchitectureTests`, `NoLiteralColorsTests` fail and list the offenders). A new chrome control implements `IChromeElement`, binds in its constructor and has a `<Control>.Original` theme in `Themes/Controls/CrChromeThemes.axaml`; a new kind of text or surface is a class in `CrParts.axaml`. Restyle tasks (P5.R2, P5.R3 and every later UI task) build their looks there and give the views classes and Cr controls.

---

## 3. Facts (append-only; continues `mvp-track.md` §3)

**Facts from the planning session (2026-10-17, commit after `bbe383a`)**
- The owner verified the MVP: performance very good; the full port proceeds (G1 and G2 passed). `mac/plans/mvp-acceptance.md` has no values filled in in the repository; P5.0 handles owner notes if they appear.
- Disk: **39 GB free** on the home volume now (5 GB during the MVP). The 3 GB STOP and the 1.5 GB fixture budget stay.
- `upstream` is a configured remote but **`upstream/master` is not fetched** locally; `master` is `7fd9fd3` (the base of the MVP work). `verify.sh` therefore compares against `master` until a session or the owner runs `git fetch upstream`.
- Test suite: 782 tests in 5 projects; full run 2 min 26 s, fast gate about 1 minute incremental (781 tests). The only test over 20 s is `ComicRack.Mac.Tests.Browser.FixtureGridTests.A_full_scroll_over_the_fixture_library_stays_under_the_cache_bound` (76 s), now `[Trait("Category", "Slow")]`. Next slowest: 4.8 s.
- Flaky assertion fixed: `PageServiceTests.Moving_the_window_cancels_running_decodes_and_drops_queued_ones_of_the_pages_that_were_left` compared the start order of two decodes that run in parallel; it now compares them as sets.
- `mac/tools/verify.sh` (P0.1): exit 0 pass, 1 fail, 2 bad arguments, 3 disk STOP. It runs a pass A build once if the `Microsoft.WindowsDesktop.App.Ref` pack is missing (the drift tests need it). Logs and `.trx` files in `mac/artifacts/verify/`. The root `.gitignore` ignores `*.tmp` (a probe file named `*.tmp` is invisible to the rule-1 check; tested with a `.cs` probe).
- Tools on this Mac: `swiftc` 6.1 (Command Line Tools at `/Library/Developer/CommandLineTools`), `osascript` with System Events (frontmost-process query works), `jq`, `python3` (no imaging packages assumed), `ruby` 2.6. **Not installed:** `7zz`, `rar`/`unrar`, `cjxl`, `avifenc`.
- `.claude/settings.local.json` is ignored by the user's global git ignore (`~/.config/git/ignore`), so a permission allowlist there does not trip rule 1. `.claude/worktrees/` is not ignored (worktrees are not used any more).
- Localization: `cYo.Common/Localize/TRDictionary.cs` reads `Languages/` next to the **entry assembly** (`Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)`); in the bundle that is `Contents/MacOS/Languages`. `ComicRack/Output/Languages/<culture>/` holds 72 XML files per culture, 19 cultures.
- ComicInfo writes: every archive accessor inherits `FileBasedAccessor.WriteInfo` (`ComicRack.Engine/IO/Provider/Readers/Archive/FileBasedAccessor.cs:31`), which calls `SevenZipEngine.UpdateComicInfos`, which runs `7z.exe` from `Resources\x64\7z.exe` next to the Engine assembly (`SevenZipEngine.cs:22-27`, hard-coded backslashes). The PDF engines return false. So one patched copy of `SevenZipEngine.cs` plus a bundled `7zz` covers CBZ, CB7 and CBT writes (P2.4b).
- WinForms surfaces to mirror (for P5): main menu (File, Edit, View, Read, Display, Help), browser context menu (Open, Open With, Info, Edit List, Add to List, Export, Automation, Update Book Files, Reveal, Copy/Paste/Clear Data, Mark as, My Rating, Remove, stacks), view menu (Thumbnails/Tiles/Details, group/stack/arrange, view filters, list layouts), library tree menu (new folder/list/smart list, rename, copy/paste list, export/import reading list, open in new window, favorites), Info dialog tabs (Summary, Details, Plot & Notes, Catalog, Custom, Pages, Colors; Summary/Notes/Review sub-pages), Preferences tabs (Reader, Libraries, Behavior, Scripts, Advanced). Read from `ComicRack/MainForm.Designer.cs`, `ComicRack/Views/ComicBrowserControl.Designer.cs`, `ComicRack/Views/ComicListLibraryBrowser.Designer.cs`, `ComicRack/Dialogs/{ComicBookDialog,PreferencesDialog}.Designer.cs`.
- Plugin surface: `ComicRack.Plugins/Automation/{IApplication,IBrowser,IOpenBooksManager}.cs` (`IApplication` has `GetLibraryBooks`, `GetComicPage`, `GetComicThumbnail`, `ReadInternet`, `AskQuestion`, `ShowComicInfo`, `ScanFolders`, ...); the WinForms app implements them in `ComicRack/` (not linked). The Mac host implements them itself (P4.3).


**Facts from RV1 (2026-10-17; called R1 until RV4a renamed the review fix tasks, because R1 to R9 are the owner requirements of §1.1)**
- Causes of the flaky fast gate: (1) the Avalonia headless session runs every `Ui.Run` job in a fresh isolated application and resets the dispatcher first; a thread left running by an earlier test (a worker that posts to the UI thread) could become the dispatcher owner before the session thread, and the job failed with "a different thread owns it" before it started; (2) a test that failed in its body or in its own `Dispose` could leave the process-wide `MacHost` open, so every later host test failed with "A MacHost is already open" (the 12-failure run); (3) assertions on process-wide counters (`PixelBlock.LiveCount`, `Image.LiveStorageCount`) took their baseline while an earlier test's decode or finalizer was still releasing; (4) one test (`A_page_that_cannot_be_decoded...`) flipped its fake failure while a prefetch of the failing page could still run.
- Fixes, all under `mac/tests` and `mac/tools`: `Ui.Run` offers a job again when the setup failed before the job started (`Support.cs`); `TestGuard` (assembly-level, `TestInfrastructure.cs`) closes a leaked `MacHost` after each test and fails that test, and waits up to 300 ms for the pixel block count to return to its value at the start of the test; `Launch.Start` and `CoreThumbnailDecoderTests.Env` close the host when they fail half way; `Compat.Drawing.Tests` take the live-storage baseline through `TestImages.LiveSettled()` (collect and finalize until stable); `verify.sh` runs the test projects one after the other (`dotnet test -m:1`) so a CPU-heavy project cannot disturb a timing test of another.
- Rule for new tests: a test that asserts on a process-wide counter takes its baseline after `Gc.Flush()` (Mac.Tests) or `LiveSettled()` (Drawing.Tests); a test that opens a `MacHost` closes it in `using` or `finally`; never assert the start order of parallel work.
- Gate numbers after RV1: 10 consecutive `mac/tools/verify.sh` runs passed (781 tests; 64 to 92 s each, mean about 76 s), plus 11 more passing runs on the final test code; the gate stays far under 3 minutes. Before R1 one of three runs on the same tree failed.

**Facts from P0.3 (2026-10-20)**
- Clean build of `mac/ComicRack.Mac.slnx` before: 1158 lines matching `warning ` (579 distinct; each is printed twice). After: **52** (the acceptance command's count). `mac/Shadow.props` now has a shadow-only `NoWarn` of CS3001;CS3002;CS3003;CS3009;SYSLIB0003;SYSLIB0004;SYSLIB0021;SYSLIB0050;CA2022;CS0649;CS0472;CS0169;CS0114;CS0659;CS1717;CS8632;CS8981 (every id measured only in linked original files; the mac-owned `Replacements/` files in the shadow projects are covered too).
- The 52 left are all mac-owned: NU1904 (System.Drawing.Common 4.7.0 vulnerability, from the Compat/Core package references, 28 lines), NU1510 (redundant `Microsoft.CSharp` and `System.Text.Json` references, 8), CS0618 (`Bitmap.Save(string, int?)` in 5 test files, 12), xUnit2013/xUnit2031 (2 tests, 4).
- `verify.sh --full` prints an INFO line with the warning-line count of its build log (only a clean build gives the full count).

**Facts from P0.4 (2026-10-20)**
- `mac/replacements.json` has 38 entries: 34 track an original (a file or a glob `pattern`; each with `base` 7fd9fd3 and a SHA-256) and 4 are stubs of types that have no original file (`SystemWebServicesProtocols.cs`, `CommunicationException.cs`, `BitmapImageCreator.cs`, `NamespaceMarkers.cs`: `original` and `sha256` absent). Several entries may share one replacement file (`CommonWindowsTypes.cs` stands for four `cYo.Common.Windows` originals, `ThemeTypes.cs` for three). A helper that belongs to a replacement and has no original of its own is listed under `companions` (`MacImagePool.cs` with `CacheManager`). `GestureEventArgs.cs` is a `patched-copy` (budget 2 lines: it only adds a comment), so `replacement-sync.sh` can re-sync it too. Entry fields: `original`|`pattern`, `replacement`, `kind`, `pass` (`A`, `B` or `both`), `base`, `sha256`, `maxChangedLines`, `companions`, `reason`.
- A new `Exclude=`/`Compile Remove=` in a `Sources.props` or in `ComicRack.Plugins.Core.csproj`, or a new `.cs` file under `mac/*/Replacements/`, fails `ReplacementManifestTests` until the manifest has an entry (the test ignores `obj`/`bin` excludes and everything that resolves into `mac/`). A glob entry hashes the sorted `path sha256` lines of the matched files joined by LF (obj/ and bin/ ignored); a new file in an excluded folder therefore changes the hash.
- `mac/tools/replacement-sync.sh <original-or-pattern> [--accept|--record] [--against <ref>]`, `--all` (dry run of every entry, exit 1 if any changed), `--list`. Exit 0 unchanged or applied, 1 action needed, 2 bad arguments or manifest problem. A changed `patched-copy` is three-way merged with `git merge-file` (ours = replacement, base = `git show <base>:<original>`, theirs = the original now); a clean merge is written by `--accept`, a conflict leaves the merged text with markers in `mac/artifacts/replacement-sync/<name>.merged` and changes nothing (resolve by hand, then `--record`). Other kinds print `git diff <base> -- <original>`; after the port `--accept` records the hash. The new `base` is `upstream/master` if its tree matches the working tree for that path, else `HEAD`, so commit the merge before recording. Needs `jq`, `perl`, `shasum`.
- Tests: `ReplacementManifestTests` (hash of every original, exclusion coverage, Replacements coverage, entry shape, changed-line budget of patched copies, mutation checks on temporary files) and `ReplacementSyncScriptTests` (the script against a temporary git repository: unchanged, clean merge, conflict, rewrite, bad arguments). `COMICRACK_REPLACEMENT_MANIFEST=<file>` redirects the hash test (and the script) to another manifest; absolute `original` paths are allowed there. Mutation run: a scratch copy of `CacheManager.cs` with one space appended, referenced by such a manifest, makes `Every_original_still_has_the_recorded_hash` fail.
- `OriginalSourceDriftTests` keeps the line-exact CacheManager test and now reads the hash from the manifest. `mac/reports/census.md` has the missing `FileSystemEnumerable` row.

**Facts from P0.6a (2026-10-20)**
- `mac/tools/ui-drive/ui-drive.sh` commands: launch, wait-window, windows, front, menu, menu-items, key, keycode, type, click, shot, probe, stats, choose-path, expect-log, status, check, quit, kill (`mac/tools/ui-drive/README.md`). Exit codes: 0 ok, 1 failed/timeout, 2 bad arguments or no session, 4 screen locked / no GUI / no Accessibility (record as SKIPPED), 5 safety check failed, 6 launch refused (a foreign `ComicRack.Mac` process runs). Session in `mac/artifacts/ui-drive/session.env` (UD_PID, UD_DATA, UD_LOG, ...); `UI_DRIVE_DIR` moves the artifacts folder (the script tests use it).
- Deviation from the §5.3 wording: input commands (menu, key, keycode, type, click, choose-path) do **not** re-activate the app on their own; they exit 5 when the launched pid is not frontmost (this is what the acceptance test with Finder in front needs). Cases call `front` once after `wait-window`; `--reactivate` or `UI_DRIVE_REACTIVATE=1` allows one activation. `quit` calls `front` itself.
- The dev app is `mac/ComicRack.Mac/bin/Debug/net10.0/osx-arm64/ComicRack.Mac` (the RID sub folder exists); `launch --dev` runs an incremental `dotnet build` first (`UI_DRIVE_NO_BUILD=1` skips). With `--data-dir D` the app writes its log to `D/Logs/ComicRack.log` (ui-drive's `expect-log` reads it) and its console output goes to `mac/artifacts/ui-drive/app.out`. The app is started in its own session by a wrapper that records the exit code (`app.exit`), so `quit` can print it although it is not a child of the later script.
- Through System Events the app menu bar item of the dev build **and** of the 2026-10-17 bundle is named `Avalonia Application` (not `ComicRack CE (MVP)`); the menu bar items are Apple, Avalonia Application, File, View, Window, Help. P0.6b's `menu-structure` case expects the other name (see section 9). The Window menu also shows an AppKit item "Enter Full Screen" with the shortcut Fn+F next to the app's own View > Enter Full Screen (Ctrl+Cmd+F).
- `menu-items <Menu>` output: `name<TAB>shortcut<TAB>mark<TAB>enabled`, shortcut written `Ctrl+Option+Shift+Cmd+Key` (from AXMenuItemCmdChar/Modifiers/VirtualKey), mark `✓` for a checked item. A native open panel appears as a second window titled like its prompt (`Choose a folder of comics to add to the library`); `choose-path` (Cmd+Shift+G, path, Return, Return) drove a real scan of `mac/fixtures/small` (50 books) in the check.
- `crcli image probe <png> <x> <y> [--box N]` (N x N box around the point, clipped at the edges; prints `rgba: r,g,b,a` and `hex`) and `crcli image stats <png> [--require-nonblank]` (size, mean/min/max per channel, distinct colours up to 65536, `blank: yes|no`, exit 1 when blank and required). Window screenshots are device pixels (2200x1496 for a 1100x748 window on this Retina display), click coordinates are points. The window of the app was found on a second display at x = -1433 once: positions are global display coordinates.
- Tests: `ImageCommandTests` (7) and `UiDriveScriptTests` (5: bash syntax, help/usage, no-session and bad-argument exits, no helper compiled) in `mac/tests/ComicRack.Cli.Tests`; they need no window server.

**Facts from the R9 planning session (2026-10-20)**
- Correction of the planning-session fact on WinForms surfaces: the original main menu is **File, Edit, Browse, Read, Display, Help** (`ComicRack/MainForm.Designer.cs:436-445`); there is no View or Book menu. The MVP menus (File, View, Window, Help, plus the reader's) are restructured by P5.R1 and P5.R3.
- The parity inventory was extracted mechanically from the Designer files (menu trees with names, texts, shortcut keys and images), `ComicRack/MainForm.cs:1261-1558` (command bindings, `commands.Add`) and `:1563-1800` (reader `KeyboardMap`, groups Library, Browse, Auto Scroll, Scroll, Display Options, Page Display, ZoomAndRotate, Edit, Other), the Behavior options (`Settings.cs` booleans with `Category`/`Description`, rendered by `cYo.Common.Windows/Forms/FormUtility.cs:219-232`) and `ComicRack.Plugins/PluginEngine.cs` (17 hook types). Result: `mac/plans/parity-ledger.md`.
- The Behavior pane of Preferences is generated, not designed: `PreferencesDialog.cs:105` calls `FormUtility.FillPanelWithOptions(pageBehavior, Program.Settings, TR.Load("Settings"))`; 40 options in 8 categories (ledger PB rows, defaults included).
- Script hooks `Editor`, `BookOpened` and `ReaderResized` were missing from P4.3/P4.5; P4.5 now owns them.
- The original has two themes, Default (Windows system colours) and an experimental Dark selected only by `-dark`; colour tables, fonts, metrics, icon folders and the custom-drawn controls are documented with source lines in `mac/plans/chrome-guidelines.md` §1 to §5. The repository has **no screenshots** of the app; references are owner screenshots (optional), the optional Windows CI job P5.T5, or view specs.
- MVP shortcuts that clash with the original and are realigned by P5.R1/P5.R3: Cmd+0 (DD7 Window > Library; original Right to Left), Option+Cmd+P (MVP performance overlay; original Previous Book), Ctrl+Cmd+S (MVP Show Sidebar; original Shift+F6), MVP fit shortcuts (original Ctrl+1 to Ctrl+0 for fit and layout).
- Original shortcut duplicates kept as in the original: Ctrl+Shift+N (New fileless Book Entry and Next Bookmark), Ctrl+Shift+0 (Only fit if oversized and Rotation 270°).

**Facts from P0.6b (2026-10-20)**
- `mac/tools/e2e.sh [--bundle] [case ...]` runs `mac/tests/e2e/cases/*.sh` (10 cases: launch-quit, menu-structure, open-folder, import, grid-keys, reader-keys, fullscreen, overlay, quit-during-scan, pdf-cb7) with the helpers of `mac/tests/e2e/lib.sh` (`launch_app`, `press`, `front_app`, `shot`, `expect_log`, `check_log`, `expect_shown_count`, `scan_folder`, `need`, `ok`/`bad`, `e2e_end`). A case prints `ok`/`FAIL` lines and ends with `PASS`, `FAIL` or `SKIP`; exit codes of a case 0, 1, 3 (fixture missing) and 4 (locked screen or no GUI access); the runner exits 0 (no failure), 1 (a failure) or 4 (every case skipped for a locked screen). Each case uses a new temp data folder and kills the app on exit; screenshots of the checks are in `mac/artifacts/e2e/<case>/` (git-ignored). Full dev run: 10 passed in about 6 minutes; `--bundle menu-structure pdf-cb7` passed.
- This Mac has an **AZERTY layout**: a digit needs Shift, so `ui-drive key cmd+3` (a character) is a different shortcut, and the menus show `Cmd+É` for the key at the position of 2. ui-drive `key` now accepts `code<N>` (physical key, US positions 1=18 2=19 3=20 5=23 0=29): the digit shortcuts of the reader are sent as `cmd+code20`. Letters and `choose-path` text work on any layout. Radio items carry the mark `•` (check boxes `✓`).
- New ui-drive command `texts`: the static texts of the app's windows through the accessibility tree (about 4 s; status bar, `N books`, sidebar counts). Cases use it to compare the number of books the window shows with the saved library (`expect_shown_count`); the number is formatted with the system locale (`14.706` here), so cases compare digits only.
- The app name: the app menu was named `Avalonia Application` in the dev build **and in the bundle** (visible in the menu bar, not only through System Events); `Name="ComicRack CE (MVP)"` on `Application` in `mac/ComicRack.Mac/App.axaml` fixes the menu bar item and the `Hide ...` item. `About ...` in the app menu is still Avalonia's own `About Avalonia` (the `About ComicRack CE (MVP)` item of `MainMenu.BuildAppMenu` does not show; see section 9). The 2026-10-17 bundle was rebuilt with `mac/tools/make-app.sh` for the bundle cases.
- The AppKit item `Enter Full Screen` (Fn+F) is added to the **View** menu next to the app's own (Ctrl+Cmd+F); the P0.6a fact that named the Window menu was imprecise. The menu-structure case lists it as info only.
- Native full screen on this display (notch): the window is `0,38 1920x1205` on a `1920x1243` screen (the strip below the menu bar/notch is excluded), so the fullscreen case requires the full screen width and a height within 80 points of the screen height, and the original size after leaving. Esc in the reader leaves full screen first, a second Esc closes the reader; Ctrl+Cmd+F toggles in both windows.
- The scan of `mac/fixtures/small` (50 books) takes 1.4 s; `mac/fixtures/BD-reader` (200 files) would finish before Cmd+Q could arrive, so `quit-during-scan` scans `mac/fixtures/BD` (14,706 books; after 6 s about 2,300 to 2,500 are in the library) and requires the saved library to hold some but not all books and the relaunch to show the same number. The status bar `N books` label is in the library window only.
- Import dialog (640 x 437 points): `Choose...` at (580,159), the mappings box at (320,265), `Import` at (591,403); Return in the mappings box only adds a line (the box accepts returns), so the case clicks the Import button. The native pickers are separate windows titled with their prompt (`Choose a folder of comics to add to the library`, `Choose the ComicDb.xml of the Windows installation`).
- Another app (a browser) took the focus twice during the first runs; `front_app` (retries the front call up to four times) and `shot` (fronts first) make the cases robust against that. If another app takes the focus between `front_app` and the key, ui-drive refuses (exit 5) and the case fails with that message instead of typing into the other app.
- Tests without a window server: `E2eScriptTests` (bash syntax of the runner, library and cases, the case list, no direct osascript in a case, bad arguments exit 2) and one more `UiDriveScriptTests` case for `code<N>`; `ComicRack.Cli.Tests` now has 33 tests.


**Facts from P1.2 (2026-10-20)**
- `NativeLibraryResolver` (mac/ComicRack.Mac.Host) is now installed on five assemblies (`NativeLibraryResolver.CoreAssemblies()`: ComicRack.Engine.Core, cYo.Common.Core, ComicRack.Plugins.Core, cYo.Compat.Forms, cYo.Compat.Drawing). A library name that is a Windows system module (`kernel32 user32 shell32 gdi32 shlwapi ole32 uxtheme dwmapi credui` plus advapi32, ntdll, comctl32, comdlg32, oleaut32, winmm, psapi, version, imm32, mpr, netapi32, setupapi, propsys, crypt32, wininet; with or without `.dll`, any case or path) increments `NativeLibraryResolver.WindowsCalls` on every call and, on the first call per module and entry point, writes `[WARN] ... Windows API <module>!<entry> called from` plus the managed stack (up to 40 frames, one `at Type.Method` per line) to the host log. The resolver still answers "not found", so the runtime raises its DllNotFoundException as before. API: `WindowsCalls`, `WindowsCallRecords` (module, entry, stack, count), `ResetWindowsCalls()`, `WindowsModuleOf(name)`, `Log` (set by `MacHost` while a host is open). The entry point is the `DllImport` method found on the stack, so it needs no entry-point argument from the runtime.
- Names that are not Windows system modules are not watched: `Resources\x64\libwebp.dll`, `libheif`, the JPEG XL import (codec tasks of P2) and `7z.exe`.
- Test: `WindowsTripwireTests` in `ComicRack.Mac.Host.Tests` (name table, installed assemblies, a deliberate `cYo.Common.Win32.Keyboard` call logged once with its stack, and the v1 flow: `FixtureCommands` books, scan, thumbnail, page, smart lists, save, close, reopen). The flow lists two known findings in `KnownUntilP13`; any other Windows call fails it. P1.3 fixes the two and then deletes that list so the test requires zero. The test project now references `ComicRack.Cli` (for `FixtureCommands`).
- The app smoke run (`--smoke-test=3 --headless`, empty library) logs no tripwire line.

**Facts from P1.3 (2026-10-20)**
- `MacHost.ConfigureCore` sets `EngineConfiguration.Default.DisableNTFS = true`, so `NtfsInfoStorage` never calls kernel32. Pass B has two more replacements (manifest entries): `cYo.Common.Core/Replacements/PassB/Resources.cs` (rewrite of `Properties/Resources.Designer.cs`: `Folder`, `FolderHome`, `PlusOverlay`, `Wikipedia` decoded from embedded files, logical names `cYo.Common.Resources.<file>`, a new Bitmap per access) and `.../FileUtility.cs` (patched copy, `PathGetCharType` is a managed table with the shlwapi results for controls and `"<>|:` (invalid), `*?` (wild), `\/` (separator)).
- `WindowsTripwireTests` no longer has a known list: the v1 flow must leave `WindowsCalls` at 0 and the log without a `Windows API` line. Tests added to `CoreRuntimeTests`: the four Common resources, and `MakeValidFilename` without a Windows call.

**Facts from P1.1 (2026-10-20)**
- Three patched copies in pass B (`Compile Remove` of the originals in the two `Sources.props`; manifest entries with budgets 8, 12 and 10 changed lines): `cYo.Common.Core/Replacements/PassB/ThreadUtility.cs` (`Abort(thread, timeOut)`: `Join(timeOut)`, then `Trace.TraceWarning("Thread '<name>' did not stop within <ms> ms and is abandoned ...")` and return), `.../BackgroundRunner.cs` (`Dispose`: same after 5 s; the wait handles `runEvent` and `exitEvent` are then **not** disposed, because the abandoned thread still waits on them and would die with an unhandled `ObjectDisposedException`), `ComicRack.Engine.Core/Replacements/PassB/ComicScanner.cs` (`Stop`: same after 10 s; `abortScanning` stays set as in the original). All Core worker threads are background threads, so an abandoned one cannot keep the process alive. Only `ThreadUtility.Abort` (called from `ImageProvider.Dispose`, 30 s) and `ComicScanner.Stop` (called from `QueueManager.Dispose` and the "Abort Scanning" task) were reachable; `BackgroundRunner` has no user in the linked Core. `ProcessingQueue.Stop`/`Dispose` in this tree only join with a timeout and never abort.
- `MacHost` adds a `HostTraceListener` (new file `mac/ComicRack.Mac.Host/HostTraceListener.cs`) while it is open: `Trace.TraceWarning`/`TraceError` from the Core go to the host log as `[WARN]`/`[ERROR]` lines; plain `Trace.Write` and `Debug` output is ignored. `MacHost.Dispose` had no Abort special case (nothing to remove).
- Tests: `ThreadAbandonTests` (4: `ThreadUtility.Abort` stops in time / ignores the timeout, `BackgroundRunner.Dispose` abandons a blocked tick and the thread ends quietly later, `MacHost.Dispose` with a scanner whose `ScanNotify` handler blocks 15 s: completes in about 10 s, the log has the abandon line and "Host closed.", the database is saved). Takes about 15 s. The old `Thread_Abort_throws...` characterization in `CoreRuntimeTests` now asserts that `thread.Abort()` itself still throws while `ThreadUtility.Abort` does not.

**Facts from RV2 (2026-10-20)**
- Causes and fixes (tests, test infrastructure and one product hook): (1) `WindowTests.Pressing_Cancel_...` raced with a 200-book scan that ends in under a second; the scanner is now parked in front of its 6th file by a `ScanNotify` handler the test releases, and the test asserts the panel, the disabled button and "Cancelling...", then `Cancelled` with exactly 5 books. (2) The `Dispose` pixel-block checks of the reader and page tests compared the count with a baseline that an earlier test's still-finishing decode or the headless session reset (which releases the previous application's last frame) changed: `Gc.SettleLive()` takes the baseline, `Gc.WaitForLive(baseline)` waits for pool work that was cancelled, `Ui.BeginTest()`/`Ui.LiveBaseline()` take the baseline inside the test's first job. (3) The sidebar and grid load on worker threads that read the library's list caches while a test changed the library: `MainViewModel.WhenLibraryShownAsync()` (newest reload, then the sidebar counts) is awaited by `Launch.Start` and the sidebar tests before they touch the library. (4) `FixtureGridTests` full scroll compared process RSS with a start value that earlier tests change (150 to 790 MB managed at start); it now compares the footprint at "cache full" (30% of the rows) with the footprint at the end, both after a full GC, with one cache bound as tolerance. (5) `PerfOverlayTests.The_footprint_follows_a_big_allocation` measured a process-wide counter that falls when other memory is given back; it tries up to 5 times after a GC.
- Rule added to section 2.5: no timing-dependent assertion without a controllable clock or gate.
- Gate numbers: 15 passing `mac/tools/verify.sh --full` runs (840 tests each: 33 + 61 + 355 + 351 + 40; 2 min 20 s to 4 min per run including the build and the 76 s scroll), no failure. One further run was aborted from outside (a second verify run was started by mistake and killed; its result does not count).

**Facts from P0.5a (2026-10-20)**
- `dotnet run --project mac/tools/upstream-scan -- --range A..B [--json out.json] [--all] [--repo dir] [--compile-items file]` classifies every file of `git diff --name-status -M A B` (`A...B` uses the merge base). Exit 0 scan done, 2 bad arguments or git/MSBuild failure. It prints one block per category (order of the list below), the `ACTION:` follow-up lines and a `SUMMARY:` line; `--all` lists the `macOS (ours)` and `payload` categories file by file (otherwise a per-folder count when over 25 files). The JSON (`Files`, `Packages`, `Counts`, `From`, `To`) is the input for P0.5c. A rename is reported as a `D` of the old path plus an `A` of the new path (`RenamedTo`/`RenamedFrom`), so a moved replaced original shows up as a deleted original.
- Categories and first-match order: `macOS (ours)` (`mac/**`, `MACOS_NATIVE_PATH.md`, `.github/workflows/mac-*`), `project files` (`*.csproj`), `excluded` / `replaced` (from `mac/replacements.json`: kind `excluded` vs stub, patched-copy, rewrite; an `original` or a `pattern` glob), `linked` (pass B compile and embedded items of the three shadow projects, from `dotnet msbuild <proj> -p:CensusPass=B -getItem:Compile -getItem:EmbeddedResource`, about 1 s for the three; files under `mac/` and `obj/`/`bin/` ignored), `payload` (`ComicRack/Output/**`), `UI not linked` (`ComicRack/`, `cYo.Common.Windows/`, `cYo.Common.Presentation/`, `ComicRack.Engine.Display.Forms/`, plus the `Controls/` folders), then the three source folders (`cYo.Common/`, `ComicRack.Engine/`, `ComicRack.Plugins/`), `build/CI` (`.github/`, yml, ps1, bat, iss, sln, props, targets, `.gitignore`) and `other`. Real run on the 40 upstream commits before the base (`7fd9fd3~40..7fd9fd3`): linked 39, replaced 1 (`PdfiumReaderEngine.cs`), excluded 2, UI not linked 27, project files 3, payload 45, build/CI 6, other 10. `7fd9fd3..HEAD`: 331 files, all `macOS (ours)`, nothing else.
- New files (`Compiles` field, printed `[compiles: yes|no]`): when the working tree has the file, `yes` means it is a compile item and `no` means it sits in a linked project folder that no shadow project compiles (category `other`); when the tree is not at the end of the range yet (preview before the merge) a new `.cs` file of a linked folder is predicted from the glob rule (`yes`, detail says so). P0.5c confirms with the build.
- A deleted file that a `Sources.props` still names by path (the single linked UI files such as `ItemViewConfig.cs`) is `linked` with an `ACTION` line, because the shadow build breaks.
- `project files`: the `PackageReference` (attribute or `<Version>` child) and `Reference` entries of the changed project file are compared old against new and against the mirror (`cYo.Common.csproj` with `cYo.Common.Core/Sources.props`, `ComicRack.Engine.csproj` with `ComicRack.Engine.Core/Sources.props`, `ComicRack.Plugins.csproj` with `ComicRack.Plugins.Core.csproj`). `Mac` per change: `same`, `different` (mac pins another version), `absent` (add it, `ACTION` on the project file), `replaced` (`bblanchon.PDFium.Win32` by `.macOS`; an `ACTION` only when versions differ), `dropped` (System.Management, System.ServiceModel, System.Web.Services, System.IdentityModel, Microsoft.VisualBasic, System.ComponentModel.DataAnnotations), `still-referenced` (removed upstream, still in the mac file) or `not-applicable` (UI projects). Real run: the three package bumps of the last upstream commits were all found as `same`, PDFium as `replaced`, System.Management as `dropped`.
- Tests: new project `mac/tests/Upstream.Tests` (in the solution, 11 tests, 4 s; no SkiaSharp, so no RuntimeIdentifier) builds synthetic git repositories in a temp folder (`SyntheticRepo`) and runs `Scanner.Run` with an injected compile-item set, plus one test that asks MSBuild about this repository. P0.5b and P0.5c add their tests there. `mac/tools/upstream-scan` is in the solution under `/tools/`.

**Facts from P0.5b (2026-10-20)**
- `dotnet run --project mac/tools/api-inventory -- --compare <baseline.json> [--json out.json] [--no-build]` builds pass A clean (unless `--no-build`), scans, and prints `no new platform API` (exit 0) or the new types, members and DllImports (exit 1; exit 2 for a missing or invalid baseline or a failed build). Each new type and member carries "covered by the shim: yes|no" and its top files; each new DllImport carries "linked: yes|no|unknown". `--write-baseline [file]` writes `mac/reports/api-baseline.json` (80 types, 297 members, 108 DllImports now); the report mode (`mac/reports/api-inventory.md`, `inventory.json` for `drawing-coverage`) is unchanged. About 4 s with the shims already built.
- Baseline identity (no call sites, no file lists, sorted, LF, byte-identical on a rerun): a type is scope + name; a member is scope, declaring type, kind, name, signature and accessors (a property that gains a setter is a new entry); a DllImport is module (case and `.dll` ignored), entry point, declaring type, name and parameter signature (overloads of one entry point stay separate). The kind of a type is not part of its identity.
- Coverage (`ShimCoverage.cs`): `cYo.Compat.Drawing` plus the framework's `System.Drawing.Primitives` for the Drawing and Primitives scopes, `cYo.Compat.Forms` for the Forms scopes, read with a MetadataLoadContext from the newest `mac/cYo.Compat.*/bin/*/net10.0/` build (the tool builds `cYo.Compat.Forms` first unless `--no-build`). Same rule as `drawing-coverage` ("exact" or "convertible" parameters, same return type, property accessors present, trailing optional parameters allowed), without its stub detection: a shim member whose body only throws still counts as covered. The Forms shim is types-only, so a new Forms member is usually "no".
- Linked: a DllImport is linked when the file that holds its declaring type's code is a pass B compile item (`dotnet msbuild -p:CensusPass=B -getItem:Compile` of the three shadow projects, about 1 s each, run only when a new DllImport exists); when the PDB has no file for the type, a calling file decides; with neither the answer is `unknown` (treat as linked). `no` also covers a file that pass B replaces with a copy under `mac/`: check `mac/replacements.json`.
- Limits: enum constants are inlined by the compiler and are invisible (the pass B compile stays the authority); reflection use is not seen.
- Tests: `ApiDeltaTests` in `mac/tests/Upstream.Tests` (10; the project now references `api-inventory` and builds both shim projects first without referencing their output): baseline shape and byte-stable round trip, identical baselines, one removed member reported as new and covered, a removed type and Forms member, invented API reported as not covered, every Drawing member and type of the baseline covered, a removed DllImport with the linked states, the DllImport identity, normalization of an in-memory inventory. `mac/reports/api-baseline.json` is committed: regenerate it with `--write-baseline` after an upstream merge has been reviewed.


**Facts from P0.5c (2026-10-20)**
- `mac/tools/upstream-sync-check.sh [--range A..B] [--no-merge] [--record] [--skip-verify]` (about 4 to 5 minutes; log files in `mac/artifacts/upstream-sync/`, report `report.md`). Steps and result lines: `preconditions`, `upstream`, `files` / `action-files` / `new-files` / `ui-parity` / `payload` (from `upstream-scan --json`), `packages`, `drift` (`replacement-sync.sh --all`, then one call per changed entry for the clean-merge or conflict answer), `rule-1` / `build` / `tests` (`verify.sh --full`), `census-A` / `census-B` (written to `--out mac/artifacts/upstream-sync/census-<pass>.md`, so `mac/reports` is not touched), `new-api` (`api-inventory --compare`), `drawing`. Last lines `NEXT:` and `EXIT:`. Exit 0 clean (INFO never counts), 1 any FAIL or WARN, 2 build or non-drift test failure or a tool that could not run, 3 precondition (nothing was checked), bad arguments 2.
- Verdict rules decided here: a new platform API that the shims cover is `WARN` (review, then `api-inventory --write-baseline`), an uncovered member or a linked/unknown DllImport is `FAIL`; a package change that the Mac side lacks, pins differently or still references is `WARN` (others `INFO`); a failing drift guard (`Every_original_still_has_the_recorded_hash`, `The_original_CacheManager_is_unchanged`, `The_replacement_differs_from_the_original_only_...`, `Patched_copies_stay_within_their_changed_line_budget`) while step 3 reports a changed original is shown as `FAIL tests (only the drift guards fail)` and keeps the exit code at 1; any other failing test or a failed build is exit 2. A pass B compile error of a new linked file is therefore exit 2 (build) with the same finding in `census-B` and `new-api`.
- Range: `lastSyncedUpstream` of `mac/upstream-sync.json` up to `upstream/master` if that is in `HEAD`, else the second parent of a merge commit `HEAD`. Without `--no-merge` an unmerged end is exit 3. `--no-merge` with an end that is not in `HEAD` is a preview: `files`, `packages`, `drift` are capped at INFO ("would be FAIL after the merge"), steps 4 to 6 check the current tree. `--record` (merged mode, exit 0 only) writes `lastSyncedUpstream` (the end of the range), `date` and `reportDigest` and commits only `mac/upstream-sync.json`; it refuses `--no-merge` and `--skip-verify`. `UPSTREAM_SYNC_ANY_BRANCH=1` allows another branch than `macos` (CI). Precondition tree check ignores `.DS_Store` and `mac/artifacts`.
- Rehearsal (throwaway `cp -Rc` copy of the repository with its own `.git`, no remotes; a fake upstream commit on top of `7fd9fd3` built with `git commit-tree`, published as `refs/remotes/upstream/master` and merged with `git merge --no-ff`): changes (1) `MemoryThumbnailCacheSize = 4096` in `ComicRack.Engine/CacheManager.cs`, (2) new `ComicRack.Engine/IO/NewThing.cs`, (3) `PackageReference Newtonsoft.Json 13.0.3` in `ComicRack.Engine/ComicRack.Engine.csproj`, (4) new `ComicRack/Dialogs/NewDialog.cs`. Variant A, `NewThing` uses `Application.StartupPath` (in the shim, not in the baseline): exit 1 in 4 min 13 s with `drift FAIL` (CacheManager: "merges cleanly", NEXT `replacement-sync.sh ... --accept`), `tests FAIL (only the drift guards fail, 4 tests)`, `packages WARN` (Newtonsoft.Json absent on the Mac), `new-api WARN` (`Application.StartupPath`, covered yes), `ui-parity INFO` (NewDialog.cs), `new-files INFO`, census A and B 0 errors. Fix-ups (`replacement-sync.sh ComicRack.Engine/CacheManager.cs --accept`, the package added to `mac/ComicRack.Engine.Core/Sources.props`, `api-inventory --write-baseline`) then a second run: exit 0 (861 tests) and `--record` committed the state file at the fake upstream commit. Variant B, `NewThing` uses `Clipboard.SetText` (not in the shim): exit 2 in 32 s, `build FAIL` (`CS0117: 'Clipboard' does not contain a definition for 'SetText'`), `census-B FAIL` (1 error), `new-api FAIL` (`Clipboard.SetText(string)`, covered no), drift and packages as in A, `tests SKIP`. Not rehearsed: a patched-copy conflict (covered by `ReplacementSyncScriptTests`) and a new DllImport (covered by `ApiDeltaTests`).
- Real preview after `git fetch upstream` (2026-10-20): `upstream/master` is `7fd9fd3`, the base of the port, so **0 upstream commits are waiting**; `upstream-sync-check.sh --no-merge --range 7fd9fd3..upstream/master` exits 0 (875 tests, census 0/0, no new platform API, drawing PASS). The fetch also brought remote branches and the tags `v0.9.180`, `v0.9.182`, `v0.9.184`, `nightly` (no effect on the plan).
- Tests: `SyncCheckScriptTests` in `mac/tests/Upstream.Tests` (14 cases: bash syntax, help, six bad-argument forms, wrong branch, dirty tree with `.DS_Store` and artifacts ignored, merge in progress, missing or wrong state file, unmerged end of range, missing `upstream/master`), run against a synthetic repository; they stop before any build. `Upstream.Tests` now has 35 tests.
- `mac/upstream-sync.json`: `{ "lastSyncedUpstream": "7fd9fd35a9cf829fffc6aa9cd0b44c7325df0cb7", "date": "2026-10-20", "reportDigest": null }`. `mac/UPSTREAM-SYNC.md` is final (commands, sample outputs); `RUNBOOK.md` §4 and §5 name the new report and exit codes.


**Facts from P0.7a (2026-10-20)**
- New project `mac/tests/ComicRack.Characterization` (xUnit, in the solution, `RuntimeIdentifier` osx-arm64, test parallelization off because the host is a singleton; 16 tests, about 1 s). Goldens are in `Golden/`: `SmallDb.xml` (200 books in 12 series, 8 of them duplicates, 24 smart lists and a fixed list: more than 10 matcher kinds, a group, a negated matcher, a base list and "not in base list", three limits, a folder that combines with And, a fixed list with an unknown id, nested folders), `SmallDb.lists.txt` (kind, count and a digest of the sorted member ids of every list), `FileNames.txt` (200 names, NFC and NFD, CJK, prose, annuals, covers, dates) with `NameInfo.json` (both parsers, one line per name), `FixtureDb.lists.txt` (`mac/fixtures/ComicDb.xml`, skipped when the file is missing; same numbers as the M2.2c table).
- `SmallDb.xml` is produced by `SmallDbBuilder` (ComicBook objects, computed ids, fixed dates; the default lists of a new database are removed because they look at today's date) and saved through `MacHost.SaveDatabase`; a test requires the generator to reproduce the committed file, another loads the committed file, saves it and requires equal canonical XML (`CanonicalXml`: attributes sorted, whitespace dropped, LF). A third round trip stores the lists' caches (`CacheStorage`, `BookCount`, ...), reloads, counts from the stored caches and saves again; only `NewBookCountDate` (the time of the update) is dropped from that comparison.
- `UPDATE_GOLDENS=1 dotnet test mac/tests/ComicRack.Characterization` rewrites every golden that differs and prints a diff of what changed (test output and stderr); without it a difference fails and the message shows the first differing lines and the path of the full actual text in `mac/artifacts/golden/<name>.actual`. Tried: one expected count changed in `SmallDb.lists.txt` fails `Every_list_has_its_golden_count`, and the update run restores it (identical file).
- `The_counts_agree_with_a_plain_query_of_the_books` checks 23 list counts against LINQ over the book properties, so the goldens are not just a copy of the matcher code. The fixture golden skips the time dependent smart lists (`Matchers.Any(TimeDependant)`: Recent (to scrape), Recently Added, Recently Read, Rescrape(able)); the other 18 rows equal the M2.2c table.
- Core behaviour pinned by the goldens: a book with no `FileCreationTime`/`FileModifiedTime` gets `1601-01-01T00:00:00Z` (FILETIME zero) on its first load and save, so a file written without those times does not round trip (the builder sets them; files written by the apps have them). A new database gets the "Temporary Lists" folder with a random id on load unless the file has it. The legacy and the new file name parser disagree on many names (for example `Batman 012 - The Joker Returns` gives the series `Batman  - The Joker Returns` with the new parser, `Batman` with the legacy one); the golden records both. The new parser gives the same result under en, fr, de and ja cultures (tested); the legacy parser formats the number with the current culture (`f.ToString()`), so the golden is produced under the invariant culture.
- **Race found:** `ComicBook.GetIsoCulture` fills a static unsynchronized `Dictionary` and `ComicBookLanguageMatcher` calls it for every book while PLINQ evaluates a list of more than 100 books; the first evaluation of a list with a language matcher in a process can lose books (a list of 64 gave 30 or 31 in 2 of 12 fresh processes, 64 in 25 of 25 after the dictionary was filled on one thread). The characterization tests fill it first (`CoreRaces.WarmLanguageNames`); the product fix is a section 9 item.
- The goldens are the net10 side of the §9.3 characterization net. The same goldens can run on net48 in the Windows CI job later (P3.10) by linking the test sources; nothing here is net10 specific except the host used to open the file.
- **Facts from RV3:** `MacHost.ConfigureCore` now warms `ComicBook.GetIsoCulture` for every neutral-culture two-letter code, so list evaluation only reads the dictionary; `CoreRaces` is gone. Audit of static caches reachable from list evaluation: `hasAsText` and `WebComic.rxCache` are locked, `searchableProperties`, `Description` are idempotent single assignments, `cachedTypeList` is filled by the matcher registration in `ConfigureCore`; no original file needs a proposal. The race did not reproduce in 30 fresh processes even with the warm-up disabled, so `LanguageRaceTests` (10 child processes through `Program.cs` of the test project) is a regression net, not a proven detector.

**Facts from P0.7b (2026-10-20)**
- `ArchiveGoldenTests` and `PerceptualHashTests` in `mac/tests/ComicRack.Characterization` (the project now references `ComicRack.Cli`; 12 more tests, the project has 28, about 8 s). A `GeneratedSet` class fixture writes the books into a temp folder at test time with `FixtureCommands.Run` (`make --books 2 --pages 4 --seed 7 --formats cbz`, `cb7` repack of the first CBZ with SharpCompress, `pdf --pages 3`); nothing binary is committed. The CBZ pages are JPEG, PNG and WebP (2 to 6 MP).
- `Golden/Archives.txt` (tab separated, one line per page): entry name and size from `GetImageInfo`, `sha:` the first 16 hex digits of the SHA-256 of `GetByteImage` (`sha:-` for the PDF, whose entry size is 0 and whose bytes are a Pdfium rendering), the decoded size through `ImagePool.GetPage`, `ah:` a 64-bit average hash of the decoded page, then the thumbnail size (`GetThumbnailKey(i)` through `ImagePool.GetThumbnail`, 512 px high) and its `ah:`. `AverageHash` is integer arithmetic over 8 x 8 cells of mean luminance (bit set above the mean of the cells), independent of the scale of the picture. `PerceptualGolden.Check` accepts up to 3 differing bits in an `ah:` word and nothing else; `UPDATE_GOLDENS=1` rewrites as for the other goldens.
- The byte hashes are those of the Skia encoders for the pinned SkiaSharp version: a SkiaSharp upgrade can change the generated bytes, and then the golden is regenerated and reviewed (the CBZ against CB7 test, which compares the same pages in both containers, does not depend on it). The PDF page size is 1920 x 2720 and the three PDF pages are alike at 8 x 8 (the hash tells page 3 apart only).
- Other tests: the CB7 has the same entry names, byte hashes, sizes and hashes as its CBZ; the page list shape (4 pages, names in order, all three image formats); two pages never share a byte hash; two hosts give the same report.
- Owner samples: `Owner_samples_open_and_match_their_golden_when_there_is_one` skips without `mac/fixtures/owner/*.cbr|*.cb7`. With samples it checks pages > 0, provider count, decoded and thumbnail sizes, and compares with `Golden/Owner.<file>.txt` when that exists (`UPDATE_GOLDENS=1` writes it). Run once with a copy of `mac/fixtures/cb7/test.cb7` as the sample (24 pages, passed), then removed. CBT is not covered (no generator in the CLI).


**Facts from P3.H (2026-10-20)**
- Avalonia.Headless 12 can simulate a display scale: `TopLevel.SetRenderScaling(double)` (extension in `Avalonia.Headless`) on a shown window raises `ScalingChanged` and a layout pass; `CaptureRenderedFrame()` then returns the frame in device pixels (`Floor(client size x scale)`, Dpi 96 x scale). No wrapper was needed. Pointer events of the headless window (`MouseDown`, `MouseMove`, `MouseUp`) take logical points. A `MouseDown` without a `MouseMove` to the same point first can hit the previous target, so the tests move first.
- Harness: `mac/tests/ComicRack.Mac.Tests/HiDpi/HiDpiSupport.cs`: `Scales` (`Main` 1x and 2x, `All` adds 1.5x and 3x, `Device(length, scale)` rounds like the painter), `Dpi.Apply/Settle/Capture/Area`, `Raster` (frame or image in RGBA with `Bounds`, `CountInRow/Column`, `Crop`, exact area-average `Downscale`, `Compare` giving mean, fraction above a threshold and maximum; `Save`), `RectInt.Snapped`, `Pictures.Lines` (a picture of 1 pixel lines that shows any blur). With `CRMAC_FRAMES_DIR` set the frames are saved as `<name>@2x.png`. 104 tests in `Category=HiDpi` (5 s alone; the fast gate runs 1005 tests in about 3 minutes): `HarnessTests` 11, `GridHiDpiTests` 39, `ReaderHiDpiTests` 34 (most at 1x, 1.5x, 2x and 3x; the frame-equals-1x property at 1x and 2x), `DrawingHiDpiTests` 20.
- What is constant: the logical layout (cell and cover rectangles, columns, viewport and extent, page rectangles, zoom, scroll, selection by a click) is the same at every scale to one device pixel (`1/scale`: the layout rounds to whole device pixels, so at 1.5x a 222 point cell is 221.33). What scales: frame size, text ink width (1.99 at 2x, 2.99 at 3x for the same string), the 2 point selection border (exactly `Device(2, scale)` pure pixels), device size of the fitted page. The 2x frame of a window with plain covers, reduced to 1x by area averaging, differs from the 1x frame by a mean of 0.27 of 255 (text only); the reader page by 0.09.
- Reader: `ReaderPainter` snaps every page to whole device pixels at every scale: layout rectangle and drawn rectangle agree to the pixel, no blended edge, no gap or overlap between the pages of a spread (checked at odd window sizes so that the shared edge falls on a fraction of a pixel at 1.5x), and a page shown at one device pixel per source pixel is a pixel copy (nearest filter) at 1x, 1.5x, 2x and 3x. Zoom about a point and the drag are logical: the same `ScrollX`, `ScrollY` and rectangles at every scale, and a pan by 40 points moves the frame by exactly `Device(40, scale)` pixels.
- Covers: `CoverDrawOperation` copies a picture that has the device size of its box exactly at 1x, 2x and 3x (mean difference 0), but not at 1.5x: it does not snap the destination to device pixels (section 9). The thumbnail request is in device pixels (`BookGridView.RequestPixels` = `ceil(size x scale)`); **decision: the 512 px cap stays**: the Core stores 512 px high thumbnails, a 2x display shows covers up to a 256 point cell cover at their own resolution (3x: 170), and the slider maximum of 360 points enlarges the 512 px cover by 1.41 at 2x and 2.11 at 3x; serving larger covers from the page is P3.7. `GridHiDpiTests.The_thumbnail_request...` and `The_largest_cells_enlarge...` pin the numbers; P3.7 changes them. Cache steps: 512, 384, 288, 216, 162, 121, 91, ... (`ThumbnailSteps.Snap`).
- **Bug found and fixed:** `BookGridView` did not react to a scale change of its window (dragging it between a Retina and a 1x display), so the covers stayed at the resolution of the old display until the thumbnail size changed. It now listens to `TopLevel.ScalingChanged` and asks each cell again (`RefreshThumbnail(force: false)`: only when the cached step changes; the old picture stays until the new one arrives). Test: `Moving_the_window_to_a_display_with_another_scale_asks_for_the_covers_again` (216 px covers at 1x become 512 px at 2x and back).
- The System.Drawing stand-in (`cYo.Compat.Drawing`) has no DPI at all: no `HorizontalResolution`, `VerticalResolution`, `SetResolution`, `DpiX`, `DpiY`, `PageScale`; `Graphics.PageUnit` throws `NotSupportedException` (its only Core user is `SimpleTextRenderer`; `FormUtility.ScaleDpiX/Y` is in the UI projects that are not linked). `DrawingHiDpiTests` pins that (a reflection test fails when one is added, which is the reminder to add the 1x/2x tests of the new path) and checks that scaled `DrawImage` between bitmaps works in pixels.
- Real screen: `mac/tests/e2e/cases/hidpi.sh` (and `screen_scales` in `lib.sh`, the backing scale factor per screen): screenshot width must equal the window width in points times the scale of the screen that holds the window (library window and reader window), not blank. Written and syntax checked, **not run** (screen locked, exit 4, SKIPPED). This Mac has one display, 1920 x 1243 points at scale 2. No public tool sets a non-Retina mode (`displayplacer`, `cliclick` are not installed and system settings are off limits), so the 1x half is reported as info.
- Avalonia details that matter for tests: `Rect.Contains(Point)` is inclusive on the right and bottom edge; a `Border` with a corner radius does not hit-test its corner; the grid's `Columns` can disagree with the repeater (section 9, 760 point window).

**Facts from P4.1 (2026-10-20)**
- Verdict GO with a patch (`mac/reports/s4-ironpython.md` has the resolution table and the reproduction commands). Stock IronPython 2.7.12 cannot create any new-style Python class on .NET 9 and later: `NewTypeMaker.ImplementCustomTypeDescriptor` overrides every method of `ICustomTypeDescriptor` with a helper of the same name, and .NET 9 added four members with a default implementation (`GetConverterFromRegisteredType`, `GetEventsFromRegisteredType`, `GetPropertiesFromRegisteredType`, `RequireRegisteredTypes`) that have no helper (`ArgumentException: Type doesn't have a method with a given name and signature`). Visible as `ValueError` / `Exception: Error` on `import os`, on `class UIControl(Control)`, on any `class X(object)`. Old-style classes and C# based modules (`re`, `datetime`, `itertools`, `time`) are not affected.
- The patch: `mac/spikes/S4-patch` (Mono.Cecil 0.11.6) inserts `if (!m.IsAbstract) return;` at the start of `NewTypeMaker.ImplementCTDOverride(MethodInfo)`; usage `dotnet mac/spikes/S4-patch/bin/Release/net10.0/patch-ironpython.dll IN.dll OUT.dll`. With it: 45 of 45 probed StdLib modules import, `class UIControl(Control)` and an `OnPaint` override work, `Sample.py` hooks ParseComicPath, CreateBookList, NetSearch, ComicInfoHtml and QuickOpenHtml run on stand-in objects, and the Drawing stand-in works from a script (`SolidBrush`, `FillRectangle`). Same results in the signed bundle.
- `PythonCommand.CompileExpression` and `ComicBookExpressionMatcher` work on the stock engine (Python 2 integer division kept, a syntax error gives a `null` delegate).
- StdLib: package `IronPython.StdLib` 2.7.12 (contentFiles `Lib/`, 1043 files, 11 MB) is copied next to the host assembly and found without any setting; in the bundle `Contents/MacOS/Lib` (accepted by `codesign --deep` and `--verify --strict`). The IronPython package also copies a real `System.Drawing.Common.dll` 4.7.0 (transitive dependency) and `IronPython.SQLite.dll`, `IronPython.Wpf.dll`.
- Hardened runtime: the bundle process runs the checks (`ComicRack.Mac --script-selftest FILE`). `allow-jit` is required (without it CoreCLR does not start); `allow-unsigned-executable-memory` is not needed by IronPython; `disable-library-validation` stays while the signature is ad-hoc.
- Names that scripts use do not resolve as on Windows: `clr.AddReference[ByPartialName]("System.Windows.Forms")` raises IOException (no such assembly; first lines of 5 of the 7 bundled scripts); `System.Drawing` leads to the real `System.Drawing.Common` 4.0.2.0 whose `Bitmap` throws (`Gdip` type initializer), `Point`/`Size`/`Rectangle`/`Color` come from `System.Drawing.Primitives`; the stand-ins work when a script names them (`cYo.Compat.Drawing`, `cYo.Compat.Forms`); when two assemblies hold the same type name in one namespace **the one referenced last wins**; `ComicRack.Engine`, `ComicRack.Plugins`, `cYo.Common` do not exist (the shadow assemblies are `*.Core`); `cYo.Common.Windows` (FormUtility) is not linked. An `AssemblyLoadContext.Default.Resolving` hook cannot map a requested name to an assembly with another simple name (InvalidOperationException). P4.2 decides the identity; its options are in the report.
- `cYo.Compat.Forms` today has `Form`, `Control`, `DialogResult`, `Keys` (and the types the Core uses); no `Label`, `NumericUpDown`, `ListBox`, `MessageBox`, `DockStyle`, `Button`, `TextBox`, `ComboBox`, `CheckBox`. `Sample.py` loads (top level) with it; its UI hooks fail with NameError until P4.10.
- Tooling: `mac/spikes/S4-ironpython/run.sh orig|patched|patched-rewrite|patched-resolver` (console project, not in the solution, compiles `mac/ComicRack.Mac/ScriptSelfTest.cs`); the hidden option `--script-selftest FILE` of `ComicRack.Mac` runs the same code (no window, no host, exit 0 or 1; variables `COMICRACK_SCRIPTS_DIR`, `COMICRACK_SELFTEST_REWRITE_REFS`, `COMICRACK_SELFTEST_RESOLVER`). The probe file is a Python script whose `#@Hook Startup` functions are scenarios, each in a fresh engine (`PASS`/`FAIL` lines are checks, `RES` lines observations). 4 tests in `ComicRack.Mac.Tests/Scripting/ScriptSelfTestTests.cs` (option parsing, probe run with the stock engine, exit codes).
- `DuckTyping.Implement` throws NotSupportedException (CodeDom generator excluded); no bundled script and no Core code calls it; Python cannot reach the generic overloads (ambiguous `params` overloads).

**Facts from P4.2 (2026-10-20)**
- Verdict GO, real-window half skipped (`ui-drive check` exit 4 after the session start; run `mac/spikes/S6-modal/real.sh all` on an unlocked screen and record the result in `mac/reports/s6-modal.md`). `mac/spikes/S6-modal/run.sh --no-real` (about 30 s) runs everything else: 78 headless checks (5 consecutive runs identical), the shutdown run, the autopilot scenario, the unhandled-exception run and the identity runs; results in `mac/artifacts/s6/`, committed copies in `mac/reports/s6-modal/`.
- Modal loop: `window.ShowDialog(owner)` (task ignored) then `Dispatcher.UIThread.PushFrame(new DispatcherFrame())`, ended by `frame.Continue = false` in the facade's close path (an exit requested before the frame runs is remembered); from another thread `Dispatcher.UIThread.Invoke` of the whole `ShowDialog`; `Application.DoEvents` is `Dispatcher.UIThread.RunJobs()`. A click handler that shows a dialog runs the nested frame inside the input dispatch, so a driver must not wait for that click to return. Closing an owner closes its open dialogs (both `ShowDialog` calls return, innermost first). Shutdown (`desktop.Shutdown()`) during a dialog closes it (Cancel) and the process exits 0.
- **An exception that escapes a job on the Avalonia UI thread aborts the process (exit 134)** unless `Dispatcher.UIThread.UnhandledException` sets `Handled`. The facade guards every peer-to-script call (`FormsRuntime.Guard` routes to `Application.ThreadException`); the host must still install the Dispatcher handler (P4.5 or app start).
- Identity decision: **literal assembly names**. `AssemblyName` `System.Windows.Forms` (`mac/cYo.Compat.Forms`) and `System.Drawing.Common` (`mac/cYo.Compat.Drawing`), both AssemblyVersion 10.0.0.0 and unsigned; `ComicRack.Engine`, `ComicRack.Plugins`, `cYo.Common` for the three shadow projects; the real `System.Drawing.Common` package (transitive through IronPython) excluded from the output. Measured: the binder ignores the public key token (a reference with `b77a5c561934e089` binds to an unsigned assembly or one with another token) and refuses a version below the requested one (a 1.0.0.0 facade fails for a reference to 4.0.0.0 in Python and in `Assembly.LoadFrom`); a `Resolving` handler only helps to load a facade from outside the app folder (the simple name must still be literal); the framework's `System.Drawing.dll` forwards `Bitmap`/`Graphics` to `System.Drawing.Common, Version=0.0.0.0, PublicKeyToken=cc7b13ffcd2ddd51`, so a stand-in of that name serves `clr.AddReference('System.Drawing')`. With all renames the unmodified bundled scripts load on the real Core (27 of 27 checks; `Sample.py` completely; `RefreshView.py` still needs an assembly named `cYo.Common.Windows`). `mac/spikes/S6-modal/Tools` (`s6-rebind`, Cecil) renames assemblies and references in a copy of a build output without touching a project.
- Places that name the assemblies and must follow the rename (P4.8): `mac/tools/api-inventory/ShimCoverage.cs` and `Program.cs` (`Assemblies`), `mac/tests/Compat.Forms.Tests/ReferencePack.cs` and `Compat.Drawing.Tests/ReferencePack.cs` (reference pack and shim now share a name in the `MetadataLoadContext`), `mac/tests/ComicRack.Mac.Host.Tests/WindowsTripwireTests.cs` and `NativeLibraryResolver.CoreAssemblies()`, `mac/ComicRack.Mac/ScriptSelfTest.cs` (`RewriteRefs` and `Resolver` experiments become obsolete), `make-app.sh`.
- Facade rules learned: state lives in the facade and peers are built from it at every show (a closed modal form is shown again with its old values); a form closed without a button returns `DefaultCloseResult` (Cancel; a message box with only OK returns OK); a button `DialogResult` closes only a modal form; cross-thread property writes are stored at once and posted in order (coalesce per control and property before P4.9 ships); the peer paints a Python `OnPaint` into a `cYo.Compat.Drawing.Bitmap` at the render scaling and copies it into an `ICustomDrawOperation` (4 to 16 microseconds per call); the stand-in `Graphics` still throws for text, ellipses, clips and `Save`/`Restore`.
- Avalonia details: `Window.Width`/`Height` are the client size; `Canvas`/`Panel.Render` is sealed (paint in a child control); `CheckBox` derives from `Button` (order of type switches); the headless platform does not block the owner of a modal window; Fluent `NumericUpDown` at 65 points hides its value behind the spinner buttons.

**Facts from RV4a (2026-10-20)**
- Review fix tasks are called `RV<n>` (formerly `R<n>`: R1 to R9 are the owner requirements of section 1.1); old commit subjects keep `mac: R1` to `mac: R4a`, and `plan-status.sh` maps them to `RV...` when it writes the run log. The orchestrator's grep for the task block must use the new name (`#### RV4a`).
- `mac/tools/plan-status.sh` (python in `mac/tools/plan-status/plan_status.py`): `--write` regenerates the status table and the `next task:` part of the `Run state:` line (Notes cells are kept; counts, Done and Next todo come from the Status lines; `done*`, `skipped*` and `split*` count as done), `--check` is the Accept test, `--pick N` is what `run-plan.sh` uses (order: `RV`/`U` tasks, then the section 6 waves, then file order; a `(GO)` dependency needs `verdict GO`; a checkpoint dependency needs its OWNER-ACTIONS block `DONE`), `--backfill-log` / `--check-log` handle `mac/plans/run-log.md` (one row per `mac: <task id>` commit, hash-deduplicated). No unit test compares the real plan with its generated table: it differs legitimately while a task is `doing`.
- Allowlist: `mac/tools/claude-allowlist.json` has no `osascript`, scoped `git add`/`git restore` (paths under `mac/`), exactly three `git stash` forms, the two `find ... -exec rm -rf {} +` commands and the three `rm -rf mac/artifacts/<x>` commands, and `Edit`/`Write` scoped with project-root paths (`/mac/**`, `/MACOS_NATIVE_PATH.md`, `/.github/workflows/mac-*.yml`). `AllowlistTests` parse the RUNBOOK (fenced blocks and inline commands of sections 2, 4, 5) and require every command to be covered. The path scoping uses the project-root form of the permission syntax; it could not be exercised in this session (see section 9).
- `e2e.sh` exit codes: 0 (a case passed, none failed), 1 (a case failed), 3 (disk STOP), 4 (all locked), 5 (no case passed: fixtures or bundle missing), 6 (nothing passed and a launch was refused). A case exits 6 for a refused launch (lib.sh `ui()`); 3 stays "fixture or bundle missing". `E2E_CASES_DIR` points the runner at a folder of fake cases (used by `LoopScriptTests`).
- `ui-drive.sh`: `pid_owner_state` (process exists, launch wrapper has not written the exit file, `ps -o command=` starts with `UD_EXE`) guards every command that uses the session pid; a foreign pid gives exit 5 for input and "nothing was killed" for `kill`. `type` and `choose-path` type 8 characters per frontmost check. `--data-dir` is validated by `check-data-dir` logic before the build (home, `~/Library`, `~/Documents|Desktop|Downloads`, `/Volumes`, system folders, owner folders; symlinks resolved; nothing created). The session file is written with `printf %q`.
- `crcli image stats`: blank = largest R/G/B standard deviation below 2 (new `deviation:` line); a flat window with noise is blank, 1 percent of high contrast pixels is not.
- `upstream-sync-check.sh`: `ui-parity` is WARN (INFO in a preview) for every changed Windows UI file that `mac/plans/upstream-ui-log.md` does not list with the upstream commit that changed it; the U-task adds the rows, which makes the next run INFO. `UPSTREAM-SYNC.md` and RUNBOOK section 5 say the same.
- `verify.sh` unsets `UPDATE_GOLDENS` (and prints a notice); `run-plan.sh` takes the model from `--model`, `COMICRACK_PLAN_MODEL` or the `Loop model:` line of RUNBOOK section 7, and no script under `mac/tools` or `mac/tests/e2e` holds a tier name (test).


**Facts from RV4b (2026-10-20)**
- Scanner teardown: the pass B `ComicScanner` (budget now 13 changed lines) keeps `LastScanThread` (the thread of the most recent scan, never reset) and `HasAbandonedThread` (a thread that `Stop` gave up on and that is still alive). An abandoned thread does not raise its final `ScanNotify` once the scanner is disposed, so nothing reaches `QueueManager.ComicScanned` after `MacHost.IsDisposed`. `MacHost.Teardown` reads the scanner before it disposes the `QueueManager`; with an abandoned thread it logs a `[WARN]` line and `DisposeAndVerify` returns a `SaveVerification` with `Ok` (file written and well formed), `Unchecked = true` and a `Note`; the book count is not compared (`ToString` ends with `UNCHECKED: ...`). Books that thread adds after the final save are still not in the file; the warning is the record.
- `FolderScanner` waits at the start of a scan for `scanner.LastScanThread` (up to 15 s), not for every thread named "Book Scanner": a thread of another owner no longer delays a scan.
- Manifest budgets are the actual counts (ThreadUtility 3, BackgroundRunner 6, ComicScanner 13, others unchanged); `Patched_copy_budgets_are_not_looser_than_the_actual_change` allows 2 lines of slack, so a merge that shrinks a patch asks for a new budget. No second hash of `CacheManager.cs` in `OriginalSourceDriftTests` on purpose (the comment there says why): the line-exact test compares the copy with the original itself.
- Test strength: `FixtureGridTests` also bounds `peakAfterWarm - footWarm` by two cache bounds (measured +193 MB, bound 512 MB); `GridHiDpiTests` reduced-lines test counts periods with contrast (51 of 63 rows at 2x, 25 of 31 at 1x, needs 60 percent) and the 1x case of the frame comparison asserts two renders are identical; `The_default_parser_follows_the_engine_setting` uses `Batman.012.1940.cbz`, which the two parsers read differently, and switches the setting both ways; the language race child process also prints `LANGUAGES <n>` (the private `ComicBook.languages` count by reflection, test only) and the test requires every neutral-culture ISO code.
- `--script-selftest` is a developer option: `ScriptSelfTest.IsEnabled` is true in a Debug build and, in a Release build (the bundle), only with `COMICRACK_ENABLE_SELFTEST=1`; otherwise `Program.Main` prints a message and exits 2. `Run` refuses (FAIL, exit 1, before any script runs) a scripts folder or probe file below `~/Library/Application Support` or `~/Library/Caches` whose first folder starts with `ComicRack` or `cYo` or that has a `Scripts` folder in its path (`IsRealScriptsFolder`), and deletes its `comicrack-s4-*` temp copy (`COMICRACK_SELFTEST_REWRITE_REFS`). To run the check in the bundle: `COMICRACK_ENABLE_SELFTEST=1 .../Contents/MacOS/ComicRack.Mac --script-selftest FILE`.


**Facts from P2.6 (2026-10-20)**
- Payload: `mac/ComicRack.Mac/ComicRack.Mac.csproj` has six `None` items (`CopyToOutputDirectory` and `CopyToPublishDirectory` PreserveNewest, `Link` keeps the folder layout): `Languages/**` (19 cultures, 72 files for `de`), `Scripts/**`, `Help/*.ini` (the two links; no `.djvu`, no `readme.txt`), `DefaultLists.txt`, `Resources/Icons/**` (827 files, 24 MB) and `Resources/Textures/**` (18 files). The Windows binaries (`Resources/*.exe|dll`, `x64`, `x86`) are not named. The test assembly gets the same files through the project reference (`AppContext.BaseDirectory/Languages`). The published folder, and so `Contents/MacOS` of the bundle, has them next to the entry assembly; `make-app.sh` fails if a payload item is missing or a Windows binary is present; `codesign --verify --deep --strict` accepts the bundle (198 MB).
- `mac/ComicRack.Mac/Localization/Localization.cs` (public static `Localization`): `Apply(setting, languagesFolder = null, preferred = null)` sets `TR.ResourceFolder`, `TR.DefaultCulture` (always, also for English, because an unset default makes the Core use `CultureInfo.CurrentUICulture`) and, for a translation, `CultureInfo.DefaultThreadCurrentUICulture` and the thread's UI culture; it drops the texts the Core had loaded. `App.OnFrameworkInitializationCompleted` calls it first, with `AppOptions.Language` (`--language CULTURE` or `COMICRACK_MAC_LANGUAGE`; until P5.1 has a settings object this is the only setting). `Choose`, `Match`, `AvailableCultures` and `MacPreferredLanguages` (`CFLocaleCopyPreferredLanguages`) are public for tests and later callers (a language picker in Preferences, P5.15).
- Choice rule: the setting if the app can show it, else the first preferred macOS language the app can show, else English. English counts as shown: the first `en*` tag stops the search with English (decision; the literal wording "exists in Languages/" would skip `en-BE` on this Mac, whose list is en-BE, fr-BE, nl-BE, and show French). A tag is matched as it is, then with subtags removed from the right (`zh-Hans-CN` to `zh-Hans` to `zh`), then English for `en*`, then the first folder of the same language (`pt-PT` to `pt-BR`, `nl-NL` to `nl-BE`, `cs` to `cs-CZ`). A setting without a translation is logged and the macOS list decides. The log line is `Language: <culture>; N translations in <folder>.`
- English has no folder in `Languages/`: `TR` returns the text the code passes as default (`TR.Load("Default")["Clear", "Clear"]`), so every UI string keeps its English default in code and the key; `this[key]` alone returns the key.
- **Encoding:** 8 Croatian files declare `encoding="windows-1250"`. .NET (unlike .NET Framework) cannot read them without `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`; without it `TRDictionary.LoadFile` swallows the exception and returns an empty `TR`, so `hr` silently showed English. `Localization.Apply` registers the provider; any other entry point that reads `Languages/` (a tool, crcli) must do the same.
- The cached statics of the Core `TR` (`TR.Default`, `TR.Messages`, `TRDictionary.Info`) cannot be reset; `Apply` is meant to run once at start. Tests (`LocalizationTests`, 32) restore `TR.LanguageResources`, `ResourceFolder`, `DefaultCulture` and the cultures and load only through `TR.Load(name)`.
- Smoke runs of the signed bundle: `--language de` logs `Language: de; 19 translations ...`, no argument on this Mac logs `Language: en (no translation)`; both exit 0.

**Facts from P5.T1 (2026-10-20)**
- Dictionaries: `mac/ComicRack.Mac/Themes/ComicRackTheme.Light.axaml` (173 colours) and `.Dark.axaml` (185, the 12 dark-only ones are `ThemeTokens.DarkOnly`). Every token is a `Color` `Cr.<Name>` (format `#AARRGGBB`) and a `SolidColorBrush` `Cr.<Name>Brush`, merged in `App.axaml` as `ThemeDictionaries` `Light` and `Dark` (`avares://ComicRack.Mac/Themes/...`). Code: `ThemeTokens.Get(ThemeTokens.Names.X, variant = null)`, `TryGetColor`, `GetBrush`, `ThemeTokens.Current`; XAML: `{DynamicResource Cr.XBrush}`. A comment in an `.axaml` must not contain two hyphens (XML), which broke the build once.
- Token groups: 32 system colours (`KnownColorTableEx`: light = comment column "existing", dark = the array value) and `WhiteSmoke` (dark = BlackSmoke #303030), 62 app tokens (`ThemeColorTable`/`DarkThemeColorTable`, plus `ReaderBackground` and `VistaColor`), 12 dark-only (`DarkColors`), 21 control tokens (`Button*`, `CheckBox*`, `Edit*Frame`, `TrackBar*`, `TabPage*`: light §2.5 assumed, dark `DarkColors` where it has one, otherwise the nearest original colour and marked in the file) and 57 toolstrip tokens `Cr.Tool<ProfessionalColorTable property>`. Not mapped: `PreferencesLabelOverlays` (dark `ThemeColors.Lossboro` depends on class initialisation order, P5.22c decides).
- `ThemeTokenTests` (14 tests) re-reads the original files with regexes (`Theme/OriginalColors.cs`: a small evaluator for `Color.FromArgb`, `SystemColors.X`, named colours, `DarkColors.A.B` fields) and compares every token; a token without a source in the test fails. `CRMAC_THEME_SOURCE_ROOT=<dir>` redirects a file (looked up by path, then by name; other files stay in the repo). Mutation check done: a copy of `ThemeColorTable.cs` with `ItemViewGroupText => Color.DarkRed` fails "ItemViewGroupText: the Mac token is #FF00008B but the original defines #FF8B0000", a copy of `DarkColors.cs` with SidePanel 26 fails likewise; both restored (the scratch folders were outside the repo).
- **Dark toolstrip:** `DarkProfessionalColors.cs` is dead code (no reference anywhere in the repository); the dark theme renders toolstrips with a plain `ProfessionalColorTableEx` on the dark system colours, so the dark `Cr.Tool*` values are that table's formulas, not the #606060 / #1F1F1F values of the dead class. A test fails when upstream starts using the class.
- **Light toolstrip:** `ProfessionalColors.Compute(palette, aero, optimized)` ports the dotnet/winforms formulas (`InitSystemColors`, `InitCommonColors`, Aero branch of `InitThemedColors`; MIT notice kept in the file); Windows 10's theme file is `aero.msstyles`, so the menu item background is the "selected highlight" blend #B5D7F3 and the Optimized table changes apply (MenuStrip gradient end #F0F0F0, selected gradient ends at alpha 128). The values are in chrome-guidelines §2.3; the dictionaries were generated by an independent script and the test compares them with the C# port plus 16 hand-calculated values.
- `StyledSelection` (in `ThemeTokens.cs`): `GetState(selected, hot, focused)`, `For(state, viewFocused, variant)` give frame alpha and fill alpha: hot 92/23, selected 164/41, selected+hot 255/63, focused 164/no fill, none 0/0; 1 px frame, colour `Cr.SelectionFocused` or `Cr.SelectionUnfocused` (the original truncates `alpha * 0.25f` to int, so 63 not 64).
- Appearance: `--theme light|dark` (or `COMICRACK_MAC_THEME`) sets `Application.RequestedThemeVariant`; the default `Default` follows macOS. `AppOptions.Theme` is "light", "dark" or null; anything else is a usage error (exit 2). Fluent is still the base control theme, so controls ignore the tokens until P5.T4.

**Facts from P5.T2 (2026-10-20)**
- Images: `mac/ComicRack.Mac/Themes/Icons.cs`. `Icons.Get(name, variant = null)` (cached shared `Bitmap`, never dispose), `TryGet`, `Contains`, `HasDark`, `PathOf`, `Names`, `NamesOf(project)`, `ProjectOrder`, `CreateImage(name)` (an `Image` at the picture's size in units, `BitmapInterpolationMode.HighQuality`), `Decode(stream, keyMagenta)`. The name is the resx name of the WinForms code; `Project:Name` picks a project when a name exists twice (GoFirst, GoLast, GoNext, GoPrevious, Close in the app and the reader project; SortUp, SortDown, SmallArrowDown in the app and cYo.Common.Windows; the first project of `ProjectOrder` wins: ComicRack, cYo.Common.Windows, ComicRack.Engine, ComicRack.Engine.Display.Forms, then `ComicRack.Icons` = icon1 to icon4 and uninst_103 from `ComicRack/Icons`).
- The map `Assets/icon-map.json` (committed, generated by `python3 mac/tools/icon-map/icon-map.py [--out f] [--root dir]`, deterministic) lists 277 names: ComicRack 230 (27 with a dark variant), cYo.Common.Windows 15 (6), ComicRack.Engine 13, ComicRack.Engine.Display.Forms 14, ComicRack.Icons 5; 266 distinct names. Only the file-referenced image entries of the resx files are in it (strings and the byte arrays Certificate, Certificate2, EmptyCursor are not). Dark rule = `ResourceManagerEx`: a bitmap "DarkX" is the dark variant of "X" when "X" exists; the dark name is also a name of its own. `Icons.Get` without a variant uses `ThemeTokens.Current`.
- Linking: `AvaloniaResource` items with `Link` (`Assets\Original\<project>\Resources\...`) for the four Resources folders (whole folders, so unused files are linked too) and `ComicRack/Icons/*.ico`; the git status of the original folders stays empty. ICO sources decode through Skia.
- Magenta is keyed out only for `.gif` and `.bmp` sources (`Icons.KeysMagenta`); none of the 17 original GIFs has an opaque #FF00FF pixel left after loading (tested), PNG sources are untouched. A GIF is decoded as its first frame (BigBallAnimation is animated: the task that needs it plays the frames itself).
- `IconTests` (11 tests): the map is not stale (regenerated in a temp folder), 135 resource names used in `*.Designer.cs` of the four projects resolve in light and dark, all 277 mapped names decode in both variants, sizes, project order, dark rule, magenta (a hand-made 2 x 1 GIF, a PNG that keeps its magenta). `IconHiDpiTests` (5, HiDpi): a 16 pixel image is 16 units at 1x, 1.5x, 2x and 3x, covers 16 x scale device pixels with nothing drawn outside, and the 2x frame equals the 1x frame enlarged with filtering. Mutation check: a changed path in the map fails the stale test and the dark-rule test (tried, restored).


**Facts from P5.T3 (2026-10-20)**
- `python3 mac/tools/designer-dump/designer-dump.py FILE.Designer.cs [--all] [--no-tr] [--languages DIR] [--json]`: tree of the controls in `Controls.Add` / `Items.AddRange` / `DropDownItems.AddRange` order (the first control of a container is its front-most one), one line `Type name "text" at (x,y) size (w,h) dock= anchor=TLBR tab= image=<Resources name> keys=Ctrl+O on:<handler> tr:Form/name`; `tr?` instead of `tr:` marks a key that the reference culture file (`Languages/de/<Form>.xml`) lacks, so the app shows the Designer's English text; `---` is a separator; non-visual components are listed in one line at the end; `this.Size = new Size(...)` style statements are form properties, not controls (`VALUE_TYPES`). Reads only; the Preferences dump has the five tab check boxes `tabReader`, `tabLibraries`, `tabBehavior`, `tabScripts`, `tabAdvanced` and the pages `pageReader`, `pageLibrary`, `pageBehavior`, `pageScripts`, `pageAdvanced`.
- `python3 mac/tools/ledger-check/ledger-check.py [--report] [--menus FILE [--azerty]]`: exit 0 / 1 (findings, each a `FAIL` line) / 2 (bad input). Rules: ID `AA.01[a]` unique, Status `todo | done YYYY-MM-DD | deviation | deferred (D4) | mac-only`, a `deviation` note starts with `impossible:` or `native:`, a `todo` row names a Mac task, every task id of the "Mac task" column is a `#### <id>` heading of this file, and coverage: every `ToolStripMenuItem`, `ToolStripButton`, `ToolStripSplitButton`, `ToolStripDropDownButton` declared in `ComicRack/**/*.Designer.cs` is named (in backticks, or by a `prefix*` pattern) in some row. Inputs can be redirected with `LEDGER`, `PLAN`, `ORIGINAL_ROOT`. `verify.sh` runs it as the `ledger` line (about 0.3 s, no build). The first run found 197 names without a row (114 in `MainForm.Designer.cs`); they were added to the "Original location" cell of the row they belong to (a new row `MF.00` for the menu bar strip and its six menus; the note of `PR.06` now starts with `impossible:`). Now 380 rows, 403 names covered. A name that two Designer files declare counts once (the `dummy*` placeholders of dynamic sub-menus are listed in the row of the menu they stand for). Items that code adds at run time (workspaces, layouts, script commands) are not seen: the rows MB.13, MB.18, MF.12 name them in prose.
- `--menus FILE` takes `ui-drive.sh menu-items` output (a line `## <Menu>` may start each menu); for every `done` row whose location names a menu or toolbar item it needs an item with the row's label (the label, without a parenthesised part, or the last segment of `A > B`; `&` and `...` ignored) and, if the row has a Mac shortcut, that shortcut or one of its aliases (modifier order ignored). Dump items that no row names are only counted. `--azerty` reads the digit-row characters of an AZERTY layout (`& É " ' ( § È ! Ç À`) as 1 to 0, which settles the P0.6a note about `Cmd+É`.
- Hidden app options (`mac/ComicRack.Mac/AppOptions.cs`): `--show NAME` opens a registered view over the library window after launch (`Views/ViewRegistry.cs`: `Register(name, factory)` in its static constructor, `Names`, `Show`; `MainWindow` is the library window and needs no factory; the name is the original type name where there is one, so spec, ledger and sheet agree; **every later UI task registers its views and lists the names in its report**), `--list-views` prints the names and exits, `--fixture-library DIR` scans DIR into the library once the engine is up (needs `--data-dir` or `--smoke-test`, so a real library is never filled). The app logs `Showing the view <Name> ("<window title>").`, which `chrome-sheet.sh` reads to find the window. `ChromeGallery` is a placeholder (one swatch per token of the current appearance, colours through resource references); P5.T4 replaces its content.
- `crcli image sheet --out f.png --cols N [--cell-width W=480] [--gap G=12] [--background #RRGGBB] input[::caption]...`: row-major cells scaled to the cell width, 26 px caption under each (file name, or the text after `::`), 12 px margin; the same inputs give the same file. A `.json` input is a reference card drawn first: `{"title","subtitle","width","background","foreground","lines":[...],"swatches":[{"name","color"}],"icons":[{"name","file"}]}`, all optional; lines keep their leading spaces and wrap, the height follows the content (`Core/ImageSheet.cs`; `ImageCommands` is now `partial`).
- `mac/tools/chrome-sheet.sh <label> (--all | <View>...)` (helper `mac/tools/chrome-sheet/chrome_sheet.py`): per view a row reference | Mac light | Mac dark in `mac/artifacts/chrome/<label>/sheet.png` and `sheet.html` (plus `<View>-light.png`, `-dark.png`, the card json and png, `rows.json`; the HTML also lists the chrome decisions `C-...` that the spec mentions, or C-APP-1, C-FONT-1, C-ICON-1, with the element and default from chrome-guidelines section 7). Reference = `mac/fixtures/owner/reference/<View>.png`, else `.../ci/<View>.png`, else a card built from `mac/specs/views/<View>.md` (for `MainWindow` also `MainForm.md`): the first code block under a heading that mentions "layout", every `Cr.<Token>` the spec names (light values), every `<ResourceName>` that is in `icon-map.json`; without a spec the outline comes from the view's `*.Designer.cs` through designer-dump (two levels) and the card says so. Mac shots: ui-drive `launch --dev --data-dir <temp> -- --theme light|dark --show <View> --fixture-library <mac/fixtures/small, `CHROME_SHEET_FIXTURES`, or 24 generated books>`, `expect-log` for the window title, `wait-window`, `shot --window`, `quit`, a blank check. Exit 0, 1, 2, 4 (screen locked: both files are still written, with cards and a "Mac shot not taken" cell, so a locked run is visible as SKIPPED) or 6 (launch refused).
- The README image (`https://github.com/maforget/ComicRackCE/assets/11904426/4748925c-662f-4ccd-bfb7-62ec46ae881e`, downloaded to `mac/artifacts/chrome/reference/readme-hero.png`, 597 x 299 PNG) is a brand banner (a painted landscape with the title and a slogan), not a screenshot: it gives no layout reference and is only linked at the top of `sheet.html` (chrome-guidelines section 1 says so).
- Contact sheet of this task: `mac/artifacts/chrome/T3/sheet.png` and `sheet.html` (MainWindow and ChromeGallery: reference cards, Mac cells say "screen locked"). OA-5 (optional, C1 (e)) in `OWNER-ACTIONS.md` asks for the Windows reference screenshots (`<View>[-dark].png`).
- Tests: `ImageSheetTests` (7), `ParityToolTests` (14: designer-dump on a synthetic and the two real Designer files, ledger-check rules, mutation by a missing row, `--report`, `--menus`, `--azerty`) and `ChromeSheetToolTests` (8) in `ComicRack.Cli.Tests`; `ChromeToolingTests` (6: option parsing, registry, the gallery window in light and dark with the Highlight swatch pixel equal to the token) in `ComicRack.Mac.Tests`.


**Facts from P5.T4 (2026-10-20)**
- Controls (`mac/ComicRack.Mac/Controls/`, namespace `ComicRack.Mac.Controls`): `CrToolBar` (Border, 25 high, `Children`, `Transparent` for the tab bar slot), `CrToolButton` (Button that draws itself: `IconName`, `Text`, `IsChecked`, `CheckOnClick`, `DisplayState` to pin a look), `CrSplitButton` (`DropDown` flyout, `DropDownOpening`, 14 wide arrow part; a press on it never clicks), `CrToolSeparator`, `CrTabBar` (+ `CrTabItem`; `ComputeLayout`, `Geometry`, `HitTest`, `Click`, `UpdateHot`, events `SelectedTabChanged` and `TabCloseRequested`, `ToolbarSlot`), `CrStatusBar` (+ `CrStatusSlot`; `AddRight`, `AddLeft`, `SetSpring`), `CrCollapsibleGroup` (`IsExpanded`/`Collapsed`, templated, content hidden while collapsed, `MinHeight` 24), `CrSizableContainer` (`Grip`, `ExpandedExtent`, `IsExpanded`, `KeepGripVisible`, slide through `AdvanceAnimation(elapsed)` driven by a 16 ms timer, `EnableAnimation` static switch, `PreviewGrip`), `CrHeader` (+ `HeaderAdornments`, `HeaderState`), `CrIcon` (an original image by resource name), `StyledSelectionVisual` (selection style as a control, follows the item, pointer and list of a template or is driven by hand with `Follow = false`), `CrDialogWindow` (+ `DialogButtons`), `ChromeMetrics` and `ChromeFonts` (the numbers of chrome-guidelines 3 and 4), `ChromePaint` (token brushes cached per appearance, vertical and horizontal gradients, crisp lines as filled rectangles, `Selection`, `Image`).
- Drawing rule: custom controls override `Render` and take every colour through `ChromePaint.Brush/VerticalGradient(control, token)` (the appearance of the control, light or dark); lines and frames are filled rectangles, so they never blend at 1x, 1.5x or 2x. `Panel.Render` is sealed in Avalonia 12: a control that draws its own background derives from `Border`, `Decorator` or `Control`. A `Rect` has no `IsEmpty` and `Contains` is inclusive on the right and bottom edge, so an unset `default(Rect)` contains (0, 0): `TabGeometry.HasClose` tests the width.
- Image names that exist in two projects: the column header, the split button arrow and the tab arrows use the cYo.Common.Windows ones (`cYo.Common.Windows:SortDown`, `cYo.Common.Windows:SmallArrowDown`; the main app has pictures of the same names that are 16 px icons); `Magnifier` of the main app is the 650 x 400 reader picture, not a toolbar icon (use `Zoom`); `MozillaGrip` is 120 x 5 and is clipped to the grip strip.
- Control look: `Themes/Controls/CrControlResources.Light.axaml` and `.Dark.axaml` (generated once, kept by hand) set the base theme's own lightweight-styling keys (`ButtonBackground...`, `CheckBoxCheckBackgroundFill...`, `TextControl...`, `ComboBox...`, `Slider...`, `TabItem...`, `ToolTip...`, `MenuFlyout...`, 298 brushes each) to brushes whose colour is a `Cr` token (`<SolidColorBrush x:Key="ButtonBackground" Color="{DynamicResource Cr.ButtonBack}" />`); they are merged into the Light and Dark theme dictionaries after the token files. `CrMetrics.axaml` (application resources) holds `Cr.FontSize.*` and the size keys (`ControlContentThemeFontSize` 11, `ControlCornerRadius` 0, text boxes 21, slider thumb 11 x 21 and no tick margins, list item padding 4,2). `CrControlThemes.axaml` has the control themes of the custom controls, of `CheckBox` and `RadioButton` (13 unit box or circle, own templates; the base templates hard-code 20) and of `TreeViewItem` (`PART_LayoutRoot` row height = line + 8, `PART_Header`, chevron toggle with `CrIcon`, `StyledSelectionVisual`, bold selected label). `CrControls.axaml` has the styles (square 23 high buttons and toggle buttons, default button border, selection of list boxes, 13 unit popup menu text, tab control frame, accent focus ring `FocusAdorner`). All of it overrides Fluent, which stays the base control theme. The standard controls of the MVP windows change look with it (font 11, flat Windows 10 look).
- A key of the base theme that is a `GridLength` (`SliderPreContentMargin`) must be declared as `<GridLength>`, not `<Thickness>`, or the slider throws on first use. A style selector for a type from an own namespace is written `c|CrIcon#Name` with `xmlns:c` in the file.
- Native pieces (`mac/ComicRack.Mac/Platform/`): `ObjC` (Objective-C runtime: `Class`, `Selector`, `Call` shapes, `NSString`, `HasInstanceMethod`, `HasClassMethod`), `MacAlert` (`ShowAsync(owner, options)`; `NSAlert` through `alloc/init`, `setMessageText:`, `setInformativeText:`, `addButtonWithTitle:` (the first is the default), `setKeyEquivalent:` on the cancel button, `setShowsSuppressionButton:`, `suppressionButton`, `runModal` (response 1000 + index); `MacAlertOptions.FromQuestion` splits the original's question text; `UseNative` or `COMICRACK_MAC_NATIVE_ALERTS=0` forces the window), `Views/AlertWindow` (the fallback, on `CrDialogWindow`), `WindowTabbing.Disable()` (`NSWindow.setAllowsAutomaticWindowTabbing:NO`, called from `App.OnFrameworkInitializationCompleted` except headless), `ScrollerStyle.IsOverlay` (`NSScroller.preferredScrollerStyle`; `App` sets `ScrollViewer.AllowAutoHide` from it once at start). Tests never open an `NSAlert` (it runs a blocking modal loop and needs the main thread): they check that every class and selector exists (`HasInstanceMethod`) and run the window fallback; the real alert was driven through ui-drive.
- Real alert check (unlocked screen, 2026-10-20): `--show ChromeGallery`, Cmd+Shift+A (or the "Show alert..." button) opens the system alert (window layer 8, 260 x 250 points here) with the message, description, suppression check box and the buttons OK (default, rightmost, Mac order) and Cancel; Return gives `button 0`, Esc `button 1`; both are in the e2e case.
- `CrDialogWindow`: modal over an owner (`ShowDialogAsync`), `WindowStartupLocation.CenterOwner`, not resizable, buttons 80 x 24 six apart, 15 from the right edge and 12 from the bottom, order OK, Cancel, Apply, texts `btOK`, `btCancel`, `btApply` of `TrSection` (default `PreferencesDialog`) without the `&`; Return and Esc work through `Button.IsDefault` and `IsCancel`; `OkClicked` can be cancelled (validation), `ApplyClicked` does not close. A dialog sets `TrSection` to its form type name and fills `Body`.
- Gallery: `--show ChromeGallery` (`Views/ChromeGalleryWindow.axaml(.cs)`, 1000 x 880) shows every control above and the restyled standard controls in their states, then the token swatches (`Swatches` panel). It logs two `Chrome gallery probe: <Token> #RRGGBB at x,y in a client area of WxH` lines (Highlight and Window swatches) and `Chrome gallery alert: ...` lines; e2e case `chrome-gallery` reads the probes from a real window screenshot (scale = shot width / client width, title bar = shot height / scale - client height: 28 points on this Mac) and drives the alert. Run on this Mac: PASS (light #0078D7 and #FFFFFF, dark #2864B4 and #323232, alert Return and Esc).
- Tests: `ChromeMetricsTests` (37: toolbar 25 with 23 x 22 buttons and token pixels of hot, pressed and checked, split button, tab bar geometry, shrink, close box and clicks, toolbar slot, status bar, group box gradient and toggle, grip rectangles, slide, drag, grip colours, header frames by state, tree rows and selection frame, standard control sizes and token pixels, font resources), `ChromeDialogTests` (15), `NoLiteralColorsTests` (2), `HiDpi/ChromeHiDpiTests` (10, Category HiDpi), `ChromeToolingTests` gallery test updated (tall window, panel found by name). Mutation check of the lint: `Background="#FF0000"` added to `Views/AboutWindow.axaml` failed the test ("Views/AboutWindow.axaml: \"#FF0000"), restored; the scanner test feeds it a literal of every kind, and a file listed as exception without a literal fails too.
- `NoLiteralColorsTests` scans `Views/**` and `Controls/**` C# files and every `.axaml` outside `Themes/` (hex colours, named colours in XAML attributes, `Colors.X`, `Brushes.X`, `Color.FromRgb/FromArgb/Parse`, `new SolidColorBrush(`, string hex); `Transparent` is allowed, `Controls/ChromePaint.cs` (the token brush factory) is skipped. Known exceptions until the restyle tasks: `Views/BookCell.cs` (P5.R2), `Views/ImportWindow.axaml` (P5.R1), `Views/PerfOverlay.cs`, `Views/ReaderView.cs`, `Views/ReaderWindow.axaml` (P5.R3); the test fails when a listed file has no literal any more.
- Contact sheet of this task: `mac/artifacts/chrome/T4/sheet.png` and `sheet.html` (ChromeGallery: reference card, Mac light shot, Mac dark shot, from a real window on an unlocked screen). The `chrome-sheet.sh` launch, wait, shot and quit path (the P5.T3 open item) worked on the first real run.
- View specs added: `mac/specs/views/{TabBar,SizableContainer,CollapsibleGroupBox,HeaderControl,SplitButton,StatusStrip,TreeViewSkinner,ChromeGallery}.md`.

**Facts from P5.R1 (2026-10-20)**
- **Avalonia.Native owns the app menu.** The Application exporter makes the default app menu (About Avalonia, Services, Hide, Hide Others, Show All, Quit) before `App.OnFrameworkInitializationCompleted` runs and does not read a menu that is set afterwards, which is why the MVP's `About ComicRack CE (MVP)` never showed. `MainMenu.FillAppMenu(NativeMenu.GetMenu(app), vm)` edits that menu in place (About replaced, Settings... added, `Hide Others` corrected from Option+Cmd+Q, Avalonia's mistake, to Option+Cmd+H); Quit stays Avalonia's and appears once. The same code builds the menu in tests when there is none.
- Menu bar (`MainMenu.BuildMenuBar`): File, Edit, Browse, Read, Display, Window, Help with the texts of `MainForm.xml` through `UiText.Get("MainForm", key, english)` (mnemonic dropped, `UiText.Raw` keeps it). Items: File > Add Folder to Library... (Cmd+Shift+A, the MVP's Open Folder), File > Import Windows Library... (MA.04), Browse > Sidebar (Shift+F6, checked), Display > Full Screen (F11, checked while full screen, alias Ctrl+Cmd+F as a window key binding), Window > Minimize (Cmd+M), Zoom, Show Performance Overlay (Ctrl+Option+Cmd+P, constants `MainMenu.PerfKey/PerfModifiers`, also used by the reader window), Help > ComicRack Documentation... (F1, alias Cmd+? as a key binding; still "not available yet"). Edit and Read are empty (AppKit adds Emoji & Symbols to Edit). There is no View menu any more, so AppKit's own Enter Full Screen item is gone too. `NativeMenuItem` carries one gesture: aliases are `KeyBinding`s of the window (`MainMenu.Aliases`).
- Mac-only texts use the section `MacOnly` of `UiText` (keys ImportWindowsLibrary, Settings, WindowMenu, Minimize, Zoom, ShowPerformanceOverlay): no language file has it, so they stay English (DD17).
- ui-drive `menu-items` shows a function key as a private use character (`Shift+` followed by U+F709 for F6) and shows nothing for F1 and F11 without a modifier; `ledger-check.py --menus` decodes U+F704..U+F71B to F1..F24, accepts an item without shortcut for a bare F key, and no longer treats status strip labels (`tsText`, `tsBook`, ...) as menu items.
- Window (`MainWindow.axaml`): title "ComicRack" (`MainViewModel.WindowTitle`, TR MainForm/MainForm; e2e and chrome-sheet wait for `^ComicRack$`), `CrSizableContainer` `SidebarPanel` (252, grip right, `IsExpanded` two way with `Sidebar.IsVisible`), `CrToolBar` `BrowserToolBar` (Sidebar `CrToolButton`, Arrange `CrSplitButton` with the sort menu, `CrSearchBox` `SearchBox` 130 x 24 that doubles while focused, cue "Search All"), `CrStatusBar` `StatusBar` with `StatusLabel` (spring), `BookSegment`, `PageSegment`, `PageCountSegment` (texts None, NA, NA) and `SizeSlot` with `SizeSlider` (80 to 360, 120 x 22 slot). `HostText` and the MVP `SummaryLabel`, `SortBox` are gone from the UI.
- Status text: `SelectionInfo.Build` (`Browser/SelectionInfo.cs`) is `ComicBrowserControl.SelectionInfo` of the original with its TR texts (`ComicSingle`, `ComicMulti`, `ComicFiltered`, `ComicSelected`) and `FileLengthFormat` sizes: `Library: 50 Books / 54,50 MB - /path/of/the/selected.cbz`. `BrowserViewModel.SummaryText` holds it (`ListName` is the selected list), `MainViewModel.StatusLine` shows it once a library is open and `StatusText` before that. e2e `shown_book_count` reads the number before ` Books` in it. The number is plain (`{0}`), no thousands separator, as in the original.
- Tree: `SidebarNode.IconName` maps `ComicListItem.ImageKey` (Library, Folder = SearchFolder, Search = SearchDocument, List, TempFolder); counters are `CrGaugePill` (a `Border`: total in `GaugeTotal`, text `GaugeTotalText`, alpha 192 to 128 gradient, no counter for a list without books); the MVP badge for script lists is now a tool tip (the notice in the grid still says why the list is empty).
- Alerts: `AvaloniaLibraryDialogs.ShowMessageAsync` calls `MacAlert.ShowAsync` (title = message, text = description, warning style); `MessageWindow` is deleted; `App` sets `MacAlert.UseNative = false` in a headless run. `ImportWindow` error text uses `Cr.SmartQueryNegation`; `NoLiteralColorsTests` has no exception left for it.
- The `import` e2e case had stale click points since the P5.T4 control sizes (the dialog is 640 x 410 now); fixed together with the new menu cases. Cases changed: `menu-structure`, `open-folder`, `quit-during-scan`, `overlay` (Window menu, Ctrl+Option+Cmd+P), `fullscreen` (Display menu), `import`, `lib.sh` (`scan_folder`, `shown_book_count`, window title).
- Tests: `MainWindowShellTests` (metrics and order, sidebar button/menu/panel, quick search, gauge pill pixels in both appearances, icon names, `SelectionInfo`, `UiText`, menu bar and app menu structure) and `HiDpi/MainWindowHiDpiTests` (toolbar 25, status bar 24, side panel 252, search box 130 x 24 at 1x and 2x, one unit borders crisp). Existing tests were adapted (`SidebarTests`, `BrowserViewModelTests`, `FixtureGridTests`, `WindowTests` message test, `PerfOverlayWindowTests` key).
- View specs added: `mac/specs/views/{MainForm,ComicExplorerView,ComicBrowserControl,ComicListLibraryBrowser}.md`. Chrome decision added: C-MENU-4. Contact sheet: `mac/artifacts/chrome/R1/sheet.png` and `sheet.html` (MainWindow light and dark from a real window).
- Registered views: `MainWindow` (already registered by P5.T3; no new view name).

**Facts from RV5 (2026-10-20)**
- Map of the chrome layer: `mac/plans/chrome-architecture.md` (layers, public surface for views, swap mechanism, audit, enforcement). Rule R10 is §2.11 item 10 and a line of the RUNBOOK subagent prompt. Chrome layer = `Themes/`, `Controls/`, `Platform/`; everything else in `mac/ComicRack.Mac` is the view layer.
- `Themes/ChromeVariants.cs`: `ChromeElement` (Toolbar, TabBar, StatusBar, Tree, ListHeader, GroupBox, SidePanel, Icons, Dialogs, Buttons, ComboBox, Menus), `ChromeVariant` (Original, Native), `IChromeElement` (`Element`, default-implemented `OnChromeVariantChanged`), `ChromeVariants.Defaults` (all Original: the one table of the shipped look), `NativeAvailable` (StatusBar), `Get/Set/Configure/TryParse/Reset/Bind/FindTheme/ThemeKey`, event `Changed`. Every `Cr*` control (and `StyledSelectionVisual`, `CrTreeView`, `AlertWindow` through `CrDialogWindow`) calls `ChromeVariants.Bind(this)` in its constructor; `Bind` sets `Control.Theme` to the application resource `<ControlTypeName>.<Variant>` (nearest base type) and registers the control weakly. `Set` moves live controls; nothing else changes. Overrides: `--chrome SPEC` / `COMICRACK_MAC_CHROME` (`Element=Native,...`, `native`, `original`; invalid = exit 2), applied in `App.OnFrameworkInitializationCompleted` after `Reset()` (each test application therefore starts from the defaults). `CHROME_SHEET_ARGS='--chrome StatusBar=Native' mac/tools/chrome-sheet.sh <label> MainWindow` shoots a Native variant.
- A `ControlTheme` can be assigned to non-templated controls (`Border`, `Decorator`, `Control` subclasses) through `Control.Theme`; a theme applies only once the control is in a tree (an unattached control keeps its default property values until it is attached). The variant themes live in a `Styles` file included after `FluentTheme` (`Themes/Controls/CrChromeThemes.axaml`, `Styles.Resources`), so `BasedOn="{StaticResource {x:Type Window}}"` and `{x:Type TreeView}` resolve; `Application.TryGetResource(key, null, out v)` finds them. Type selectors are exact: a style for a `Border` subclass is written `:is(Border).cr-x`.
- Native status bar (proof): `CrStatusBar.Native` sets `Background` = `SystemControlBackgroundChromeMediumLowBrush` and `BorderBrush` = `SystemControlForegroundBaseLowBrush` (`CrStatusBar.ApplyTokens` clears its hand-set gradient for Native), `CrStatusSlot.Native` sets `Sunken` false; height and layout are unchanged (24), so the same views work. Elements without a Native variant (all others) stay Original when Native is requested (logged).
- New semantic parts: `CrTreeView` (member names `IconMember`, `TextMember`, `CountMember`, `CountVisibleMember`, `ToolTipMember`, `ChildrenMember`, `ExpandedMember`; builds the row of icon, label and counter pill and the two way `IsExpanded` in code; theme `CrTreeView.Original` carries what the view set: transparent, no border, no padding, no horizontal scroll bar), classes of `Themes/Controls/CrParts.axaml` (`cr-window`, `cr-panel`, `cr-card`, `cr-display`, `cr-title`, `cr-heading`, `cr-bold`, `cr-small`, `cr-secondary`, `cr-hint`, `cr-error`) and of `CrMvpLook.axaml` (the former literals of the MVP surfaces, unchanged, as `Cr.Mvp.*` resources and `cr-book-cell` (`:selected` pseudo-class set by `BookCell.SetSelected`), `cr-book-title/subtitle`, `cr-reader*`, `cr-overlay*`). `ChromePaint.ResourceBrush(control, key)` gives custom-drawn views (`CoverView`, `ReaderView`) those brushes. `ReaderView.PaperBrush` (public static) is gone. `AlertWindow` moved from `Views/` to `Controls/` (namespace `ComicRack.Mac.Controls`).
- Enforcement: `ChromeArchitectureTests` + `ChromeLint` (scan of the view layer: fonts, shapes, inline styles/templates/themes, image loading, forbidden namespaces in `Views/` and `ViewModels/`; exceptions `ChromeLint.Showcases` = the gallery, `ChromeLint.Allowed` = the `ItemsRepeater` element factory of `BookGridView`; stale exceptions fail), controls and themes per variant, theme keys; `NoLiteralColorsTests` has no exception left; `ChromeVariantTests` (parse, flip at both appearances, later controls, weak registry); `ChromeSnapshotTests` (14 headless frames of 7 views in light and dark against a baseline: take it with `CRMAC_CHROME_SNAPSHOT_DIR=<dir>` before a change, compare with `CRMAC_CHROME_BASELINE_DIR=<dir>` after; tolerance 0.05 % of the pixels over 4 per channel, mean 0.05; the RV5 move was identical to the baseline; the baseline frames are in the git-ignored `mac/artifacts/chrome/RV5/before/`) and a test of the values of the moved surfaces. Mutation check (once): `FontSize`, a colour literal and an inline `<Style>` added to `AboutWindow.axaml` failed `ChromeArchitectureTests` ("font (FontSize=", "inline style, template or theme (<StackPanel.Styles", "(<Style") and `NoLiteralColorsTests`; removed again.
- Real window: contact sheets `mac/artifacts/chrome/RV5/sheet.png` (MainWindow, ChromeGallery; default look, the MainWindow shot is visually the same as `R1/MainWindow-light.png`) and `mac/artifacts/chrome/RV5-native/sheet.png` (MainWindow with `--chrome StatusBar=Native`: flat bar, no gradient). `mac/tools/e2e.sh chrome-gallery` passed 4 of 4 runs in a row; two earlier runs right after the full test run failed one swatch probe each (the gallery window had scrolled: the gallery focuses its first tree on `Opened`, which scrolls the window; a timing race of the gallery itself, listed in §9).


**Facts from P5.R2 (2026-10-20)**
- Parts (chrome layer, element `ChromeElement.ItemView`, default Original): `Controls/CrThumbnailItem.cs` (the item: a `Border` over a private panel of backdrop, `CoverView`, overlay and the caption `TextBlock`; semantic API `CoverHeight`, `CaptionText`, `Badges` (`ThumbnailBadges`: page count, the two ribbon pages, ratings, state icon names, page number), `RatingMode`, `SameSizeCovers`, `DogEarEnabled`, `BackImage`, `LoadingText`, `SetSelected`, `SetHot`, `IsFocusedItem`, `ViewFocused`, `Geometry`, `CaptionHighlight`), `Controls/CoverView.cs` (moved from `Browser/`; picture only, `FillBox`, `SnapToUnits`, `PictureOpacity`, fade-in `StartFadeIn` with `FadeEnabled` and `AdvanceFades(ms)`), `Controls/CoverDrawOperation.cs` (moved; optional source rectangle, opacity and a clip triangle), `Controls/ThumbnailLayout.cs` (`ThumbnailMetrics`: border 4, shadow 4, three label lines, `LabelFontScale`, `OverlaysHeight`, `TagHeight`, `CellWidth/CellHeight`; `ThumbnailLayout.Compute` gives every rectangle), `Controls/CrBookToolTip.cs`. The colours of the original's renderers are `Cr.Thumb.*` in `Themes/Controls/CrThumbnailLook.axaml` (read through `ChromePaint.ResourceBrush`); the caption and the selection use tokens. The `Cr.Mvp.Grid*`, `Cr.Mvp.Cover*` entries and the `cr-book-*` classes are gone; `cr-item-view` (the grid's paper `Cr.ItemViewMainBack`) is in `CrParts.axaml`.
- View layer: `Views/BookCell` derives from `CrThumbnailItem` and keeps the thumbnail rules of M2.2a (cache first, cancel on recycle, step check); it now also fades a cover that arrives later in (not one from the cache), asks for the page behind the dog-ear (`ThumbnailService.RequestResult(book, page, size, token)` and `TryGetCached(book, page, size, out)`, new overloads; `ThumbnailService.FrontCover` = -1) only while the cell is selected or hot, sets the tool tip card on hot, and keeps `TitleText`/`SubtitleText` for tests (the caption drawn is `ComicBook.Caption`). `Browser/ThumbnailOptions.cs` (defaults of `Settings.cs:270-282` and `EngineConfiguration.cs:660`, `Changed` event, `MarkerOf` hook for the open and last markers), `Browser/ThumbnailBadgeBuilder.cs` (state icon order of `CoverViewItem.OnDraw`), `Browser/BookToolTipData.cs`; `BrowserViewModel.ThumbnailOptions`; `BookGridView.Options`, `CoverHeight`.
- Geometry decided here: the grid is still uniform (`UniformGridLayout`); an item is `round(H * 2/3) + 8` wide and `H + 8 + label` high (H = the item size 80 to 360, label = `(int)(3 * (itemFontHeight * clamp(H/192, 0.7, 1) + 2))`), grid margin 1 and spacing 2 (`BookGridView.GridMargin`, `Spacing`: the original's item padding), the cells are centred on a whole unit by an explicit margin and width of the repeater (`CenterCells`; the layout needs the width to be at least columns x (cell + spacing) or it lays out one column fewer: 6.9999 columns), so frames stay sharp at 1x. The cover frame is drawn as the original draws it: one unit on the picture's left and top edge and one unit outside it on the right and at the bottom; the shadow is 0.33 black over 4 units on the right and bottom (linear strips, radial corners). Pictures and frames are on whole units (`CoverView.SnapToUnits`).
- Pointer: Cmd+wheel changes the item size by 16 per notch in the tunnel phase (`ScrollViewer` below scrolls with any wheel before a bubbling handler runs), a trackpad pinch (`PointerTouchPadGestureMagnify`) scales it; `Gestures` is internal in Avalonia 12. Headless tests send `MouseMove` before `MouseWheel` (the wheel goes to what the pointer is over). The item has a transparent background so that the whole cell takes the pointer.
- `CoverView.FadeEnabled` is true only in a running desktop app (`App` sets it from `ApplicationLifetime`); a headless test session (all tests) shows covers opaque at once, so the unchanged M2.2b tests keep reading the cover colour right after it arrives. Tests of the fade turn it on and call `CoverView.AdvanceFades(ms)`.
- Tests: `Browser/ThumbnailLookTests` (22: the numbers, layout, frame and shadow pixels, empty tile, fade, caption selection by state, dog-ear selected and hot, stars at alpha 1.0 and 0.25, community and user stars, numeric tags, ribbons, state icons, page badge, Cmd+wheel, options, tool tip, Settings.cs defaults) and `HiDpi/ThumbnailHiDpiTests` (7, 1x/2x/3x: frame one unit of whole device pixels, shadow four units deep and monotonic, a dressed item at 2x equals 1x reduced, logical layout equal). `GridHiDpiTests` changed for the new look (covers built from the box size, the first row and column of a cover are the frame, the selection test checks the 1 unit frame behind the caption, the text test measures the caption block, the gap is `BookGridView.Spacing`); `BookGridTests`, `FixtureGridTests` and the other M2.2b tests are unchanged except a `using ComicRack.Mac.Controls;` for the moved `CoverView` and `CoverDrawOperation`.
- View `BookGrid` (`Views/BookGridWindow`, registered in `ViewRegistry`, shows the library window's own browser; title "ComicRack Book Grid"; logs two `Book grid probe` lines once the first cover is there). e2e case `grid-look` (light and dark, shot, probes of the paper and of the black frame, Right key selects the first book, second shot) passed on the unlocked screen. Contact sheet: `mac/artifacts/chrome/R2/sheet.png` and `sheet.html` (BookGrid, light and dark, from a real window); shots in `mac/artifacts/e2e/grid-look/`. View spec added: `mac/specs/views/CoverViewItem.md`. Chrome decisions added: C-LIST-2, C-TIP-1. `chrome_sheet.py` maps the view `BookGrid` to the spec `CoverViewItem`.
- The ribbons of an unread linked book sit at the top of the right edge (both pages are 0, and `ThumbRenderer.DrawBookmarks` draws every entry without a zero test); that is the original's code, kept.

**Facts from P5.R3 (2026-10-21)**
- Reader window (`Views/ReaderWindow.axaml(.cs)`): `CrTabBar` `TabBar` (31, one tab with the book caption and a close box that closes the reader) with `CrToolBar` `ReaderToolBar` (transparent) in its `ToolbarSlot`, the page area `ContentArea` (`ReaderView` `PageView`, the overlay layer with a 16 unit margin, `CrNavigationOverlay`), `CrStatusBar` `StatusBar` with `BookSegment`, `PageSegment`, `PageCountSegment` (tsBook, tsCurrentPage, tsPageCount). Tab strip and status bar are hidden in full screen (`ReaderViewModel.ShowStatus`). The window follows the macOS appearance (no more forced dark). Public accessors for tests: `Tabs`, `ReaderToolbar`, `PageInfo`, `Loading`, `Message`, `Part`, `Navigation`, `ReadingArea`.
- Toolbar (`ReaderPopupMenus`): Previous Page and Next Page split buttons (First Page, Last Page in the arrow), Page Layout (button cycles, `LayoutIconName` incl. the Rtl pictures), Fit (cycles Fit All, Fit Width, Fit Height, `FitIconName`), Zoom (text "100%", button cycles 100, 125, 150, 200; menu has Zoom In, Zoom Out, 100 to 400 %), Full Screen (checked while full screen), Tools (About..., Exit). Page menu (right click, `ReaderView.ContextFlyout`): Info... (Cmd+I; raises `InfoRequested`, no dialog until P5.7a), My Rating (None, 1 to 5 stars, Option+Shift+0..5, written through `IReadingSession.SetRating` to `ComicBook.Rating`), Page Layout. Native menu bar of the reader (`ReaderMenu`): File, Edit, Browse, Read, Display, Window, Help in the original's order; Read and Display as the ledger says.
- Keys (`ReaderViewModel.HandleKey`): PageDown/PageUp and Option+Right/Left turn the page, Cmd+B/P/N/E and Cmd+Home/End the Read menu commands, Space/Shift+Space parts, Home/End page start and end, arrows scroll (Left and Right no longer turn the page and no longer swap in right to left), F/F11/Ctrl+Cmd+F full screen, T cycles the layout, 7/8/9/0 layout and direction (Cmd+ for the menu), 2/3/5 fit, Q quits (`ExitRequested`; the window shuts the application down), Cmd+W closes, Esc leaves full screen then closes. Cmd+0 is Right to Left now; the old Option+Cmd+1/2/3/R keys and Cmd+0 zoom reset are gone (`ZoomReset` stays a command; the 100% preset does the same). `FitBest` is `FitAll` (the original's name for `ImageFitMode.Fit`).
- Overlays (chrome layer, element `ReaderOverlay`): `CrTextOverlay` (page info 150 x 25 minimum, loading 200 x 30, message 300 x 30; glass `BlackGlassPanel` cut by `GlassPainter.NineSlice`, white text at 16 units, size = text + 15, at most 400 x 400; the page info is "4 (of 12)" through `ComicBook.FormatNumber`), `CrPartOverlay` (page fitted into 200 x 200 plus 16, whole page at 0.3, visible part at 1.0 clipped, close button), `CrNavigationOverlay` + `NavigationLayout` (every rectangle of the original: buttons 28, slider (W - 151) x 24, strip frame, labels), pictures from `INavigationThumbnailSource` (`Reader/CorePageThumbnails` over the browser's `ThumbnailService`; page numbers in Times 32 until a picture arrives). `ReaderOverlayClock` (static, one 16 ms timer; `Enabled` is off in a headless run, `Advance(ms)` moves every running fade and slide, as `CoverView.AdvanceFades`): fade 100/2000/100 (`CrReaderOverlay.StartFade`, `FadeOnly`), slide 300 ms after 500 ms in and 200 ms out. In a test a fade starts at opacity 0 and only `ReaderOverlayClock.Advance` moves it, so frames carry no overlay unless a test asks for it.
- Settings with the original's defaults are in `ReaderOptions` (not persisted, no Preferences controls: P5.1, P5.22a): `ShowCurrentPageOverlay`, `ShowVisiblePagePartOverlay`, `HideVisiblePartOverlayClose`, `ShowStatusOverlay`, `ShowNavigationOverlay`, `NavigationOverlayOnTop`, `CurrentPageShowsName` (no effect yet), `HideCursorFullScreen`, `AutoHideCursorDelay` 5000, `RealisticPages` true. The navigation overlay shows when the pointer is in the 500 x 50 zone at the edge (no button down) or over it, or when toggled (`ToggleNavigationOverlayCommand`); a click on the page puts a toggled overlay away.
- Page look: paper `Cr.ReaderBackground` (WhiteSmoke #F5F5F5, dark #303030); `PageFrame`: 1 unit black outline per page (left and top on the edge, right and bottom one unit outside) and a shadow around the pages, depth 1 percent of the smaller side (4 at a 430 wide page), 0.6 black fading out, both from snapped device rectangles (`ReaderPainter.SnapToDevice`). The existing HiDpi reader tests set `RealisticPages` off (they test the painter's snapping); `ReaderChromeHiDpiTests` test the outline and the shadow at 1x and 2x. Because the tab strip (31) and status bar (24) are not whole device pixels at 1.5x the reading area differs by a unit between scales; the scale tests allow one unit.
- Page loading text: `ReaderViewModel.LoadingText` ("Loading Page N...", `TR.Messages["LoadingPage"]`) is separate from `Notice` (the message overlay: "Opening the book '...'..." from `OpeningComic`, open and page failures); `OpenFailed` marks an open failure (`--read-book` waits on it). `PageInfoChanged` fires for every shown page; `PartInfo` gives the geometry of the visible-part overlay.
- `CrMvpLook.axaml` is deleted; the colours are `Themes/Controls/CrReaderLook.axaml` (`Cr.Reader.OverlayTextBrush`, `GlassIconBrush`, `PageFrameBrush`, `PageShadowBrush`, and `Cr.Perf.*` for the performance overlay whose classes `cr-overlay*` moved to `CrParts.axaml`). New view `Reader` (`--show Reader`, opens the first book of the library like a double click; `--show` views now open after the fixture library is scanned, retried for 20 s for a view that has no book yet). e2e cases `reader-keys` (new keys and the Display > Page Layout marks), `reader-look` (paper token pixel in light and dark), `fullscreen` and `overlay` pass on an unlocked screen. Contact sheet: `mac/artifacts/chrome/R3/sheet.png` and `sheet.html` (Reader, light and dark, from a real window). View specs `ComicDisplayControl.md` and `NavigationOverlay.md`. Chrome decisions added: C-READ-2, C-READ-3.

**Facts from P5.1 (2026-10-21)**
- Files: `mac/ComicRack.Mac.Host/Settings/AppPreferences.cs` (the values), `SettingsStore.cs` (file, debounce, atomic write). The store is `HostService.Preferences` (read on first use, before the host opens, from `<data>/Settings.xml`; `--data-dir D` gives `D/Data/Settings.xml`); the app passes `Preferences.Current` to `MainViewModel`, `ReaderManager` and the window trackers, `MacSettings(AppPreferences)` is the Core's view of the same values (its cache and update setters write them, `CacheSettingsChanged` follows them). A `MainViewModel` or `ReaderManager` made without preferences keeps nothing (tests that do not care).
- `AppPreferences`: XmlSerializer root `Settings`, a value equal to its `[DefaultValue]` is not written. The 61 mirrored properties have the name, type, `DefaultValue` and clamps of `Settings.cs`: the 40 Behavior options (PB.01 to PB.40), the reader overlays, the 10 cache settings and 3 update flags the Core reads, `LookForShared`, `CultureName`. The field citations name the line of the original. The Mac-only properties carry `[MacOnly]`: `LibraryWindow`, `ReaderWindow` (`WindowFrame`: `X`, `Y` screen pixels of `Window.Position`, `Width`, `Height` device independent pixels of `ClientSize`, `Maximized`), `SidebarWidth` 252, `SidebarVisible`, `ThumbnailSize` 200, `SortBy` (a `BookSort` name), `ReaderFit`, `ReaderLayout` (names of `ImageFitMode` and `PageLayoutMode`), `RightToLeftDefault`, `LastFolder`. A new mirrored option is added with the original's name and default and is compared automatically; a new Mac-only one gets `[MacOnly]`.
- `SettingsDefaultsTests` (Host.Tests) parses `Settings.cs`: for every mirrored property the `DefaultValue` attribute, the initializer of the field its getter returns and the constants they name equal the value of a new `AppPreferences` and the attribute here; the ledger's 40 PB rows each name a saved property with the ledger's default; every Category/Description option of the original Behavior pane is saved. Mutation check: `dogEarThumbnails = false` in `AppPreferences.cs` fails 2 tests (restored). `MacSettingsTests` reads the citations from `MacSettings.cs` and `AppPreferences.cs` now (case-insensitive: an automatic property is cited at the property).
- Saving: a change schedules a save after 750 ms of quiet (`SettingsStore.SaveDelay`, `ISaveScheduler` so tests release it by hand); `HostService.Dispose` (quit) flushes first. A save writes `Settings.xml.<id>.tmp` and renames it over the file. A file that cannot be read (damaged XML, other root, empty) gives the defaults, a copy as `Settings.xml.bad` (an older copy is replaced), `SettingsStore.LoadNote` (logged as INFO, not as an error of the app) and a save of the defaults soon after, so the damaged file is not reported at every start. A failed save is logged as an error and tried again at the next change and at the flush.
- Window frames (`Services/WindowFrameTracker`, `WindowPlacement`): `Restore(window, saved)` before `Show` applies the frame corrected for the current screens (`ScreenArea` = working area in pixels and scale: a size larger than the screen is shrunk to it, a frame whose title bar is not on any screen (a monitor that is gone) is centred on the closest one, a top above the menu bar is pulled down; a title bar with at least 100 units or its width and half its height visible is left where it is); `Attach` reports moves, resizes and the zoomed state (looked at one dispatcher turn after the event, so the frame before a zoom is not overwritten; full screen and minimized report nothing). The frame the window opens with is not reported, so an untouched window leaves `Settings.xml` alone. A window of the reader opens at the saved reader frame moved aside by 26 pixels per reader already open. Real window on this Mac (AX position/size = whole window): saved `90,80 997x618` (client area) is the window `90 80 997 646`; relaunch gives exactly the same frame; a saved `-31000,-31000` came back to `462 318 997 646` (centred on the 1920 x 1243 screen).
- Library state kept: side panel visible and width (`SidebarViewModel.Width`, two way with `CrSizableContainer.ExpandedExtent`; the panel now hides its content from Tab and accessibility while collapsed, after the slide ends), thumbnail size, sort, `ThumbnailOptions` (`Bind(preferences)`: the five options and the two update flags, two way), last folder (set when a folder is scanned; the picker starts there through `ILibraryDialogs.PickFolderAsync(title, startFolder)`). Reader state: `ReaderOptions.From(preferences)` (overlays, cursor, fit, layout, direction), `ReaderManager` writes fit, layout and, for a book that has no direction of its own, the direction back. Language: `--language` over the saved `CultureName` (the original's `ExtendedSettings.Language ?? Settings.CultureName`); no UI chooses it yet (P5.22a).
- Options that are saved but do nothing yet stay `todo` in the ledger until their task makes them work (PB.01 to PB.09, PB.11 to PB.20, PB.22, PB.26, PB.28 to PB.40); the ones that already worked (PB.10, PB.21, PB.23 to PB.25, PB.27) now survive a restart. FF.02 (the portable parts of a Windows `Config.xml`) stays P5.28b.
- `ui-drive.sh frame <title> <x> <y> <w> <h>` moves and resizes the window with that exact title through System Events (no key or click, works without the app in front). `lib.sh` `launch_app` and `quit_app` now use `front_app` (four tries) because another app (Screen Sharing on this Mac) sometimes takes the focus between two steps. e2e case `state-restore`: move and resize, hide the side panel (Shift+F6), `Settings.xml` appears while the app runs, quit, relaunch gives the same frame and a hidden panel, a frame edited to -31000,-31000 comes back on a screen, a damaged file starts with the defaults and keeps a `.bad` copy.
- Tests: `Settings/PreferencesTests.cs` in `ComicRack.Mac.Tests` (`WindowPlacementTests` 10, `PreferencesTests` 8, `WindowFrameTrackerTests` 3, `SettingsFileFlowTests` 3, `ReaderPreferencesTests` 1 on the stand-in books), `SettingsStoreTests` 12 (the damaged-file one with 5 inputs) and `SettingsDefaultsTests` 5 in `ComicRack.Mac.Host.Tests`. `Launch.Start` passes `Service.Preferences.Current` to the view model like the app does.

**Facts from P5.2 (2026-10-21)**
- Change collection: `Host/Library/LibraryWatcher` listens to `ComicLibrary.BookChanged` and `BookListChanged` (any thread; the handlers only note what happened under a lock) and delivers one `LibraryChange` (changed books, list changed, reading only) per quiet period on an `ISaveScheduler`: 1 s (`DefaultDelay`), 3 s when the only changes are `CurrentPage`, `LastPageRead`, `OpenedTime`, `OpenedCount`, `NewPages` (`DefaultReadingDelay`; a change of another kind brings the timer forward), `Flush()` delivers at once (a closed reader). `MainViewModel.EnableLiveRefresh(scheduler = null)` turns it on (the app calls it, a timer scheduler; no test or `Launch` does, so the existing tests see no background work; a test passes a hand released scheduler and `WhenLiveRefreshedAsync()`), `OnHostOpened` restarts the watcher for a new host, `Closing` stops it.
- Delivery on the UI thread: `BrowserViewModel.NotifyBooksChanged(books)` (the grid draws the cells of those books again: `BookCell.RefreshLook` now also rereads the caption), then one refresh pass at a time (`RequestLiveRefresh`; a request during a pass runs another after it): `SidebarViewModel.RefreshCountsAsync()` (every list, children first, off the UI thread) and `BrowserViewModel.RefreshAsync(build)` (the shown list is built again on a worker; `BookCatalog.HasSameContentAs` says whether the books and their shown strings are the same: then `Items` stays the same array; else `SetCatalog(keepView: true)`). The pass logs `Live refresh: ... Counters <list> total/unread/new, ...` (the e2e case reads it).
- Selection and scroll: `RunQuery` finds the selected book in the new list by its Core book (it compared the old `BookItem` object before, so every reload lost the selection), `BrowserViewModel.KeepsView` is true while a refresh replaces `Items` and `SelectedIndex`; `BookGridView` then keeps the scroll offset (put back after the layout if the extent was not known yet) and does not call `EnsureVisible`. A refresh whose list was replaced by the user's choice meanwhile is dropped (`loadVersion`).
- Counters: `Host/Library/ListStats` (`Total`, `Unread`, `New` of the books a list evaluates to; the rule of `ComicListItem.CreateBookCacheStatus`: not `HasBeenRead` and added within `IsRecentInDays` (14) is new, else unread; a book that is only started counts as unread; checked against the Core's own `BookCount`, `UnreadBookCount`, `NewBookCount` of the library list in a test). `SidebarNode.Count`, `UnreadCount`, `NewCount`, `SetStats`; `SidebarViewModel.GaugesVisible`, `GaugeTotal`, `GaugeUnread`, `GaugeNew`, `GaugeNumeric` follow `AppPreferences.DisplayLibraryGauges` and `LibraryGaugesFormat` (new mirrored properties; the enum `ComicRack.Mac.Host.LibraryGauges` copies the original's names and values, default 0x1007; `SettingsDefaultsTests.Format` writes an enum as `Type.Name`).
- Look: `CrGaugeStrip` replaces `CrGaugePill` (deleted): `GaugeStripLayout.Compute` is the arithmetic of `LibraryTreeSkin.DrawNodeLabel/DrawMarkers/DrawNumberMarker` (pill = (int)text + 4, one unit apart from the right: Total, Unread, New; numbers only if the sum of (width + 1) is below the room behind the label and `Numeric` is set, else bars of room / 3, at most 6, none at 2 or less; the unread counter takes the new books when the New counter is off; corners per counter as the original). The original's gradient angle is 270 (bottom to top), so alpha 192 is at the bottom and 128 at the top; P5.R1 had them the other way round, now corrected. `CrTreeRow` (chrome layer, `ChromeElement.Tree`) lays icon, label and strip out: the label takes what it needs (trimmed when the row is short) and the strip gets the rest; the strip settles its layout to its final width when it is arranged (the measure of an older pass can carry a stale constraint). `CrTreeView` members: `TotalMember`, `UnreadMember`, `NewMember` (replace `CountMember`, `CountVisibleMember`) and the properties `GaugesVisible`, `GaugeTotal`, `GaugeUnread`, `GaugeNew`, `GaugeNumeric`.
- State icons: `BrowserViewModel` fills `ThumbnailOptions.MarkerOf`: `IsBookOpen` (set from `IReaderLauncher.IsOpen`; the interface got `IsOpen` and the event `OpenBooksChanged`, raised by `ReaderManager` when a reader opens or closes) wins, else the book of the shown list with the latest `OpenedTime` is the last opened one (`BookCatalog.LastOpened`, over the shown items as `UpdateBookMarkers`).
- Tests: `Host.Tests/LiveRefreshTests` (`ListStats` rule, equality with the Core's counters, gauge settings default and round trip, `LibraryWatcher` batching, reading delay, flush, dispose), `Tests/Theme/GaugeStripTests` (layout arithmetic, drawing in light and dark, 1x and 2x crisp sides, the row room, the settings), `Tests/Browser/LiveRefreshTests` (selection and scroll kept, unchanged refresh leaves the list alone, a dropped stale refresh, cells redrawn, markers, reading updates counters and the Reading list, books that arrive, reader close flush), e2e case `refresh-after-read` (reads 3 pages of the first book of `fixtures/small`, closes the reader: log `Live refresh: ... Reading 1/0/1`; passed on an unlocked screen, shots `mac/artifacts/e2e/refresh-after-read/`). Contact sheet: `mac/artifacts/chrome/P5.2/sheet.png` and `sheet.html` (MainWindow light and dark). Note for tests: a posted `UiDispatch` item may run after the awaiting continuation, so tests call `Dispatcher.UIThread.RunJobs()` after `WhenLiveRefreshedAsync()` (the helper `Fire` in the test file).


**Facts from P5.3 (2026-10-21)**
- `Browser/SelectionModel.cs` (no Avalonia) is the selection of the browser: selected books, focused book and Shift anchor, kept per book (the Core book, else the item) so that it follows its books through a sort, a search, a refresh and a new list. Operations (the rules of `ItemView`): `Click(index, toggle, extend)` (`UpdateSelectionFromMouse`; Shift resets the anchor to the focused book), `ClickEmpty`, `Focus`, `Move(target, toggle, extend)` (`OnKeyDown`; plain = only the target, Shift = range from the anchor, toggle = focus only), `Space(toggle)`, `SelectAll`, `Invert`, `Clear`, `SelectOnly`, `SelectBooks`, `SetItems(items, keepHidden)`; reads `Count`, `FocusedIndex`, `IsSelected`, `SelectedIndices/Items/Books`, `SelectedSize`. `Changed` is raised once per operation. "Ctrl" of the original is Cmd (toggle).
- `BrowserViewModel.Selection` holds it; `SelectedIndex` is now the focused index (the setter selects that book alone, -1 clears), `SelectedItem` the focused item (Enter and double click open it), `SelectedBooks` the Core books in grid order (what commands use), `SelectedCount`, event `SelectionChanged`, commands `SelectAllCommand`, `InvertSelectionCommand`, `RemoveSelectedCommand` and event `RemoveBooksRequested` (P5.5 answers it). A search keeps the selected books it hides (they come back when the search is cleared, as the original keeps the state of items that are not displayed) and a refresh or another list forgets them; the status text counts the selection (`SelectionInfo`: "- 3 selected / 4,50 MB", the file of a single book).
- Grid (`BookGridView`): pointer as `ItemView.OnMouseDownView`: a press on an unselected book acts at once, on a selected one at the release (it could start a drag); a context click selects an unselected book and keeps the selection otherwise; a press on the empty area clears (not on a scroll bar); a double click opens the focused book. Keys: arrows, Home, End, Page Up and Down move the focus (Up, Down and the page keys keep the column and stop in the first or last row, as `GetRelativeItem`; the MVP jumped to the first or last book), Space, Option+Space, Cmd+A (only while the grid has the focus, so text fields keep theirs), Delete and Cmd+Backspace. `BookCell.SetState(selected, focused)`: the focused book of a multi selection is drawn as selected and hot (255/63), the others as selected (164/41), a focused unselected book with the frame only (the existing `StyledSelection` states).
- `ui-drive click` has `--cmd` and `--shift` (flags on the mouse events). e2e case `grid-multiselect` (status text checks; click points are those of the default 1100 x 748 window with 5 columns). Tests: `Browser/SelectionTests` (model, view model, grid with keys and clicks).

**Facts from RV6 (2026-10-21)**
- Original rules read from `ItemView.cs` (read only): `OnKeyDown` takes `displayedItems[0]` when nothing has the focus, so the first Right selects the second book, Left and Up stay on the first, Down goes to row 1 of column 0, and Space selects the first book without focusing it (only the movement keys call `Focus`); a mouse press scrolls nothing but the release does (`OnMouseUpView`: `EnsureItemVisible(FocusedItem)`). `UpdateSelectionFromMouse(null)` (a press on no item) clears the selection and the focus unless Shift or Ctrl is held, for any button, and records the press point and the state (the band); `UpdateSelection` runs on every move and on every scroll: the band rectangle is kept in content coordinates (it grows when the view scrolls), only the items that intersect the viewport are re-evaluated (an item scrolled out of view keeps the state it had), an item is reset to the state at the press when Shift or Ctrl is held now (else to unselected) and then selected (flipped with Ctrl) if the band touches it (`Rectangle.IntersectsWith`: strict, a zero width band inside an item counts), the focus is cleared and goes to the item under the pointer. `OnPaint`: the band is filled with `Highlight` at alpha 128 two units inside and framed by a `Highlight` pen (GDI+ draws it one unit wider and higher). `ScrollControl` (Drag mode): a left press starts a 20 ms timer, the content moves by the distance of the pointer beyond the control edge (10 and a tenth of the rest above 10). `OnItemDrag` fires after the pointer moved more than `SystemInformation.DragSize` (4) from the press, for any button.
- What is an item: `CoverViewItem.Contains` and `IntersectsWith` test the rectangle `ThumbIconRenderer.Draw` returned, the cover picture's rectangle together with the caption text as laid out, not the whole cell. `CrThumbnailItem.HitRegion` and `HitTest` are that (item coordinates, right and bottom edges excluded); the 4 unit border, the shadow and the free part of the caption area are the empty area of the view. The hot state still follows the whole cell (pointer enter and exit): section 9.
- Code: `Browser/SelectionModel` (`BeginMarquee`, `UpdateMarquee(visible, hit, pointed, keepBase, flip)`, `EndMarquee`, `IsMarqueeActive`; no event when a band move changed nothing; `Space` no longer sets the focus), `Controls/CrSelectionRectangle` (chrome layer, element `ItemView`, theme `CrSelectionRectangle.Original`; `Shapes(band, out fill, out frame)`), `Views/BookGridView` (content is a `Grid` of the scroll viewer and the band; `Band`, `IsMarqueeActive`, `MarqueeRectangle`, `AdvanceAutoScroll()` and the static `AutoScrollEnabled` (the app turns the 20 ms timer on, a test calls `AdvanceAutoScroll`), `DragThreshold`, `DragStarter`, `DragEffects`). Content coordinates of the band are those of the repeater: viewport point plus scroll offset minus the repeater margin; the pointer is captured while the band is down (moves outside the window still count); `ApplySelection` does not scroll to the focused book while a band is down.
- Drag out: `Browser/BookDrag.cs` (`BookDragRequest`, `IBookDragStarter`, `AvaloniaBookDragStarter`, `BookDrag.BooksFormat` = in-process `DataFormat<ComicBookContainer>` named `ComicRack.ComicBookContainer`, `Container`, `Files` (linked books), `Create`). **The system's drag session ends the process with an `NSGenericException` ("N items on the pasteboard, but M drag images") when the transfer has an item that is not a pasteboard item** (found on the real screen): the in-process format therefore rides on the first file item, and without files one text item carries it (`BookDrag.Text`). A drag of the 50 fixture books works on screen (`Drag started: 50 books, 50 files`). Files come from `TopLevel.StorageProvider.TryGetFileFromPathAsync` (complete at once on the desktop); the log lines `Drag started: N books, M files` and `Drag ended: <effect>` are what the e2e case reads. P5.16c reads `BookDrag.BooksFormat` from the drop and adds Move.
- ui-drive: `drag <x1> <y1> <x2> <y2> [--cmd] [--shift] [--steps N] [--hold-ms N]` (WindowTool `drag`: left button down, `leftMouseDragged` events, hold, up; the click safety checks for both points). e2e case `grid-marquee` passed on an unlocked screen: bands with no modifier, Shift and Cmd (status text), the pure Highlight probe pixel of the band's left line in a screenshot taken while the button is held (scale 2), a real drag session of one book and of all fifty. Shots: `mac/artifacts/e2e/grid-marquee/`.
- Tests: `Browser/RubberBandTests` (17: model rules; grid with real pointer events: press in the cover border starts a band, the band selects what it touches and focuses the book under the pointer, up-left, Shift, Cmd and shrinking, right button press, no scroll to the focus during the band and a scroll at the release, auto scroll below and above with the step values 19 and 12 and the ends, scroll while the band is down with books out of view keeping their state, pointer outside the window, hit region), `Browser/BookDragOutTests` (6: threshold 4, whole selection in grid order without collapsing, empty area / right button / hover, double click, transfer layout), `HiDpi/RubberBandHiDpiTests` (6: fill and one unit frame at 1x and 2x in light and dark, frame pixels pure, nothing outside; the band over the grid at 1x and 2x with the same logical rectangle). Existing keyboard tests changed for the first key. Mutation check: drag threshold -1, first key from index -1 and a whole-cell hit test together failed 14 tests (restored).

---

## 4. Checkpoints (the only times the owner is asked)

A checkpoint is a block in `mac/plans/OWNER-ACTIONS.md` (template in `RUNBOOK.md` §6). The orchestrator writes it when the checkpoint's trigger is reached and keeps working on everything that does not depend on the answer. It STOPs only when no independent task is left.

| # | When it occurs | What the owner is asked | Why then | If there is no answer |
|---|---|---|---|---|
| **C1** Samples and corpus | Now (written in the initial inbox; optional, non-blocking) | (a) one or two real `.cbr` files and a `.cb7` made by real 7-Zip, copied to `mac/fixtures/owner/` (git-ignored); (b) the plugins you use (names, or a copy of your Scripts folder to `mac/fixtures/owner/scripts/`); (c) optional: a copy of your Windows `Config.xml` to `mac/fixtures/owner/Config.xml`; (d) optional: your notes from `mac/plans/mvp-acceptance.md` §8.12; (e) optional (added by P5.T3, R9): screenshots of the Windows app for the views it lists, to `mac/fixtures/owner/reference/` | P0.7b, P2.4b, P4.6b and P5.28b use them; (e) becomes the reference column of the contact sheets; the earlier the better | CBR/real-7z tests stay skipped (recorded as unverified); the corpus is the bundled samples plus public plugins; `Config.xml` import is tested on a synthetic file |
| **C2** Mid-port review | After wave B (§6): about 35 tasks in, when the main window has editing, context menus and write-back | 30 to 60 minutes with a copy of your library: (a) the checks only a person can do: trackpad swipe and pinch feel, scroll feel, Retina sharpness, Metal frame rate; (b) an SMB share scan and the TCC prompts; (c) a CBR from your library; (d) push `macos` to `origin` once and look at the Actions tab (P3.9 CI; if P5.T5 exists, run "mac-reference-shots" once and unzip its artifact into `mac/fixtures/owner/reference/ci/`); (e) approve or reject pending edit proposals and upstream proposals, if any; (f) **chrome review, about 10 minutes**: open `mac/artifacts/chrome/C2/sheet.png` (or `sheet.html`), one row per view: reference, Mac light, Mac dark; answer per row "ok" or what differs, and confirm or flip the chrome-guidelines §7 defaults that have a flip cost (C-MENU-3, C-TB-1, C-TREE-1, C-DLG-2 button order, C-PREF-1, C-CTRL-1, C-FONT-1 small captions) | First moment the app is used like a daily tool; real-input defaults (swipe threshold, pinch speed) can still be tuned cheaply; the chrome foundation is done but most screens are not built yet, so a flip is cheap | Work continues through waves C to F; the items move to C6; the §7 defaults stand |
| **C3** Script layer go/no-go (conditional) | Only if spike P4.1 (IronPython) or P4.2 (modal loop, identity mapping) ends NO-GO or needs a choice that weakens D6 | Choose between the options the spike wrote (for example IronPython 3.4 with a compatibility notice, or modeless-only dialogs) | D6 ("scripts run unmodified") is binding; only the owner can relax it | All non-script work continues; scripts run headless only (P4.3 to P4.6); the Script UI tasks (P4.7 onwards) stay blocked |
| **C4** Plugin acceptance | After P4.15 (end of wave E) | Run your top plugins on the Mac with `mac/plans/plugin-acceptance.md` (P4.15), about 1 hour | Only you know what "works" means for your scripts | v1 ships with the automated corpus results; the checklist moves to C6 |
| **C5** Release identity and credentials | When P6.1 to P6.6 are done (or earlier, whenever the owner is ready) | (a) Developer ID Application certificate in the login keychain; (b) `xcrun notarytool store-credentials comicrack-notary ...`; (c) confirm the bundle id `org.comicrackce.ComicRack` and the name `ComicRack CE`, or give others; (d) optional: a 1024 px icon source; (e) GitHub secrets for `mac-release.yml` if you want CI releases | Signing and notarization cannot be done without them; ad-hoc builds continue until then | P6.7 (signed, notarized release candidate) stays blocked; everything else proceeds |
| **C6** v1 acceptance and release go | After P7.8 | `mac/plans/v1-acceptance.md` (all leftover owner-only checks from C2 and C4, a clean-user Gatekeeper launch, the **parity summary**: every ledger deviation with its reason and the `deferred (D4)` list; the **final chrome contact sheet** `mac/artifacts/chrome/C6/sheet.png` of every view, light and dark, about 15 minutes; a final verdict) | End of the plan | Nothing is released; the app stays a local build |

**Checks that remain owner-only** (everything else is automated with `mac/tools/e2e.sh` or headless tests): trackpad gestures (two-finger swipe, pinch, momentum; macOS has no public API to synthesize them), scroll and page-turn *feel*, Metal/ProMotion frame-rate perception and multi-display behaviour, SMB/NAS libraries, TCC prompts on really protected folders, real CBR and real 7-Zip archives (samples), the owner's own plugins, Developer ID signing and notarization, Gatekeeper on a clean user or Mac, VoiceOver judgement, look and feel verdicts in light and dark mode (collected on one contact sheet at C2 and one at C6, chrome-guidelines §9), changes of the system default apps (P5.34).

---

## 5. Harness design notes (implemented by P0.4, P0.5a to c, P0.6a/b, P0.8)

### 5.1 Replacement manifest and drift guard (P0.4)
- File `mac/replacements.json`:
  ```json
  { "version": 1, "entries": [
    { "original": "ComicRack.Engine/CacheManager.cs",
      "replacement": "mac/ComicRack.Engine.Core/Replacements/PassB/CacheManager.cs",
      "kind": "patched-copy", "pass": "B", "base": "7fd9fd35a9cf829fffc6aa9cd0b44c7325df0cb7",
      "sha256": "ceeb0ad49cf94086f68c5a611996c9f240a5eb500130a1fbbbdcf58a053014e8",
      "maxChangedLines": 2, "reason": "MacImagePool instead of ImagePool (M1.5a)" } ] }
  ```
  `kind` is `excluded`, `stub`, `patched-copy` or `rewrite` (§2.3); `pattern` replaces `original` for glob exclusions (`ComicRack.Engine/IO/Network/**`, hashed as the sorted list of `path sha256` lines of the matched files). Hash = SHA-256 of the file without BOM and with LF line endings (the existing `OriginalSourceDriftTests` rule).
- Tests `mac/tests/ComicRack.Mac.Host.Tests/ReplacementManifestTests.cs`: (1) every original still has its hash (the message names the `mac/tools/replacement-sync.sh` command to run); (2) every `Exclude=`/`Compile Remove=` in `mac/*/Sources.props` and `mac/ComicRack.Plugins.Core/ComicRack.Plugins.Core.csproj` and every file under `mac/*/Replacements/**` has an entry, and every entry points at something that exists; (3) a patched copy differs from its original by at most `maxChangedLines` lines (header comment excluded).
- Tool `mac/tools/replacement-sync.sh <original> [--accept]`: `patched-copy`: writes `git show <base>:<original>`, runs `git merge-file -p <replacement> <base-copy> <original>` into a temp file, shows the result and conflicts, and with `--accept` writes it, then records the new `base` (the upstream commit, `git rev-parse upstream/master`) and `sha256`. `rewrite`/`stub`/`excluded`: prints `git diff <base> upstream/master -- <original>` for a session to port by hand, and `--accept` records the new hash after the port.

### 5.2 Upstream sync harness (P0.5a to c)
- Entry point `mac/tools/upstream-sync-check.sh [--range A..B] [--no-merge] [--record]`. State file `mac/upstream-sync.json` (`lastSyncedUpstream`, date, report digest). Range default: `lastSyncedUpstream..upstream/master`; just after an owner merge it uses the merge commit's second parent.
- Steps and their verdicts (each PASS, WARN, FAIL or INFO, in this order):
  1. Preconditions: on `macos`, no merge in progress (`.git/MERGE_HEAD`), tree clean except `mac/artifacts`, disk guard. Else exit 3.
  2. **Changed and new upstream files** (`git diff --name-status <range>`), classified by `mac/tools/upstream-scan` (P0.5a): *linked* (in the pass B compile items of the three shadow projects, from `dotnet msbuild -getItem:Compile`), *replaced* (manifest), *excluded*, *UI not linked* (`ComicRack/`, `cYo.Common.Windows/`, `cYo.Common.Presentation/` outside the linked files, `ComicRack.Engine.Display.Forms/`, `ComicRack.Engine/Controls/`, `ComicRack.Plugins/Controls/`), *project files* (`*.csproj`: PackageReference and Reference changes compared with `mac/*/Sources.props`), *payload* (`ComicRack/Output/**`: Languages, Scripts, Help, Resources), *build/CI*, *other*. New files that the shadow globs pick up automatically are listed with "compiles: yes/no" from step 4.
  3. **Replacement drift**: the P0.4 manifest test; for each changed original the `replacement-sync.sh` dry run (patched copies: clean auto-merge or conflict; rewrites: diff size).
  4. **Build and tests**: `mac/tools/verify.sh --full` (includes the drift tests).
  5. **Census**: `dotnet run --project mac/tools/census -- --pass A` and `--pass B`, totals must stay 0.
  6. **New platform API** (P0.5b): `api-inventory` against the committed baseline `mac/reports/api-baseline.json`: new `System.Windows.Forms` / `System.Drawing.Common` members with "covered by the shim" yes/no (drawing-coverage logic, Forms surface), new `DllImport`s (module, entry point, declaring type, linked or not). The pass B compile stays the authority for inlined enum constants (`mvp-track.md` §9, M1.2a).
  7. **Summary** to stdout and `mac/artifacts/upstream-sync/report.md`: one line per verdict, the exact follow-up for each FAIL/WARN, and the line `NEXT: ...`.
- Exit codes: 0 clean (with `--record` the state file is updated and committed by the caller), 1 action items a session can fix, 2 build or tests broken, 3 precondition failed.
- The weekly CI dry run (P3.10) runs the same script on a temporary merge in a GitHub runner.

### 5.3 UI automation and e2e (P0.6a/b)
- `mac/tools/ui-drive/ui-drive.sh <command>` plus a Swift helper `mac/tools/ui-drive/WindowTool.swift`, compiled on first use with `swiftc -O` to `mac/artifacts/ui-drive/windowtool` (window list with ids, titles and bounds via `CGWindowListCopyWindowInfo`; mouse clicks via `CGEvent`; screen-lock detection via `CGSessionCopyCurrentDictionary`).
- Commands: `launch [--bundle|--dev] [--data-dir D] [-- app args]` (starts `Contents/MacOS/ComicRack.Mac` or the dev build directly so the pid and exit code are known; writes `mac/artifacts/ui-drive/session.env`), `wait-window <title-regex> [s]`, `windows`, `front`, `menu <Menu> <Item> [<Sub>]`, `menu-items <Menu>` (names, shortcuts and check marks through System Events), `key cmd+shift+g`, `keycode <n>`, `type <text>`, `click <x> <y>` (window-relative), `shot <name>` (`screencapture -l<windowid> -o -x` to `mac/artifacts/ui-drive/shots/`), `probe <png> <x> <y>` (pixel colour through `crcli image probe`, added by P0.6a), `choose-path <path>` (in an open or save panel: Cmd+Shift+G, type, Return, Return), `expect-log <regex> [s]` (the app log under the data dir), `quit` (Cmd+Q, waits, prints the exit code), `kill`.
- Safety (§2.10): every input command checks that the frontmost process is the launched pid (`unix id of first process whose frontmost is true`), re-activates once, else aborts with exit 5. Exit 4 = screen locked or no GUI access.
- `mac/tools/e2e.sh [--bundle] [case ...]` runs `mac/tests/e2e/cases/*.sh` (each case sources `mac/tests/e2e/lib.sh`, prints `PASS`/`FAIL`/`SKIP` lines and leaves screenshots); summary and exit 0, 1, or 4 when every case was skipped for a locked screen. Every UI task from P0.6b on adds at least one case.

### 5.4 Unattended loop (P0.8)
- `mac/tools/run-plan.sh [--max-tasks N] [--dry-run]` starts one headless orchestrator run per task (`claude -p "<resume prompt>" --model sonnet --effort high`), checks the `Run state:` line of this file after each run, and ends on `STOPPED`, on a checkpoint STOP, on `--max-tasks`, or after two runs in a row that commit nothing. It needs a permission allowlist (`RUNBOOK.md` §7); `--dangerously-skip-permissions` is the owner's choice and is not used by default.

### 5.5 Parity tooling (P5.T1 to P5.T4)
- `Cr.*` theme tokens with a test that re-reads the original colour tables (P5.T1); `--theme light|dark` app option.
- `Icons.Get(name)` over the original images linked at build time, `Assets/icon-map.json` generated from the `Resources.resx` files with a staleness test (P5.T2).
- `mac/tools/designer-dump/designer-dump.py <Form.Designer.cs>` (view-spec skeleton), `mac/tools/ledger-check/ledger-check.py [--report] [--menus <ui-drive dump>]` (ledger format, coverage of every original menu and toolbar item, comparison of the running app's menus and shortcuts with the ledger), hidden app options `--show <ViewName>` and `--fixture-library <dir>`, `crcli image sheet`, `mac/tools/chrome-sheet.sh <label> [--all|View...]` (contact sheets in `mac/artifacts/chrome/<label>/`) (P5.T3).
- Control themes and custom controls (`CrToolBar`, `CrSplitButton`, `CrTabBar`, `CrStatusBar`, `CrCollapsibleGroup`, `CrSizableContainer`, `CrHeader`, `CrDialogWindow`, `MacAlert`), the chrome gallery (`--show ChromeGallery`) and a lint test against literal colours in views (P5.T4).
- After an upstream merge, `ledger-check` fails on a new or renamed original menu item and the token test fails on a changed original colour: both become U-tasks (`RUNBOOK.md` §5).

---

## 6. Execution order

The orchestrator takes the first task in this list whose status is `todo` and whose dependencies are `done` or `done-with-caveat`. Waves are not barriers; they set priority. The full gate and a review run at each wave end.

- **Wave A, safety net and tooling:** P0.2, P0.3, P0.4, P0.6a, P0.6b, P0.8, P1.2, P1.1, P1.3, P0.5a, P0.5b, P0.5c, P0.7a, P0.7b, P4.1 (S4), P4.2 (S6).
- **Wave B, chrome foundation, then daily use of the main window:** P5.0, P2.6, P5.T1, P5.T2, P5.T3, P5.T4, P5.R1, P5.R2, P5.R3, P5.1, P5.2, P5.3, P5.4, P2.1, P5.32a, P5.5, P5.6, P2.4a, P2.4b, P5.7a, P5.7b, P5.7c, P5.8, P5.9, P5.10a, P5.10b, P4.3, P3.9, P5.T5 (optional). → **C2** (with the C2 contact sheet).
- **Wave C, browsing and library management:** P5.32b, P5.11a, P5.11b, P5.12, P5.13a, P5.13b, P5.14, P5.15a, P5.15b, P5.16a, P5.16b, P5.16c, P5.17a, P5.17b, P5.17c, P5.18, P2.2, P5.19a, P5.19b, P5.20, P5.21, P3.1, P3.2, P3.3, P3.10, P4.4, P4.5, P4.6a, P4.6b.
- **Wave D, reader, preferences, export, codecs:** P3.4, P3.5a, P3.5b, P3.6, P5.22a, P5.22b, P5.22c, P5.32c, P5.33, P5.23a, P5.23b, P5.23c, P5.23d, P5.23e, P5.23f, P2.3a, P2.3b, P2.3c, P2.5, P5.24a, P5.24b, P5.25, P5.26, P5.27a, P5.27b, P5.28a, P5.28b, P5.29, P5.30, P5.31, P5.34, P3.7, P3.8.
- **Wave E, Script UI layer** (runs only if P4.2 is GO, else C3): P4.7, P4.8, P4.9a, P4.9b, P4.9c, P4.9d, P4.9e, P4.9f, P4.9g, P4.10a, P4.10b, P4.10c, P4.10d, P4.10e, P4.10f, P4.10g, P4.11a, P4.11b, P4.11c, P4.11d, P4.11e, P4.12a, P4.12b, P4.13a, P4.13b, P4.13c, P4.14, P4.15 → **C4**; P4.16 (optional).
- **Wave F, packaging, hardening and parity audits:** P6.1, P6.5, P6.2, P6.3, P6.4, P6.6, P7.1, P7.2, P7.3, P7.4, P7.5, P7.6, P7.7, P7.A1, P7.A2, P7.A3, P7.A4, P6.7 (needs **C5**), P7.8 → **C6**.
- **Front of the list:** every `todo` task named `RV<n>[a-z]` or `U<yyyymmdd>.<n>` (their Status lines say which; `mac/tools/plan-status.sh` prints the next runnable task).
- **P3.H** (HiDPI 1x/2x test harness) runs right after P0.7b and before any P5 task.
- **RV5** (chrome isolation) runs right after P5.R1 and before P5.R2/P5.R3 and all further screens.
- **RV5b** runs at the end of wave B, before C2.
- **RV6** runs right after P5.3, before P5.4.
- Tasks created by an upstream sync (`U<date>.<n>`, §7 "Upstream sync tasks") and review fixes (`RV<n>`, formerly `R<n>`; renamed because R1 to R9 are the owner requirements of §1.1) go to the **front** of the list.

---

## 7. Tasks

Template (from `MACOS_NATIVE_PATH.md` §9.5, adapted): **Depends** · **Effort** (session effort level, Sonnet tier for all implementation) · **Unattended** (`yes`, or `partial: <owner part> -> Cn`) · **Status**; **Goal**; **Add**; **Edit** (files under `mac/`; originals: none unless stated); **Out of scope**; **Accept** (commands; `verify.sh` = `mac/tools/verify.sh`); **Rollback** (default §2.9 unless stated); **Parity** (UI tasks: ledger rows by ID or prefix, view specs, chrome decisions; §2.11 applies in full, including the ledger update and the contact sheet, even where the Accept line does not repeat it). Paths are relative to the repository root.

### P0 Safety net and tooling

#### RV1 Make the fast gate deterministic (flaky tests)
- Depends: none · Effort: high · Unattended: yes · Status: done 2026-10-17
- Goal: `mac/tools/verify.sh` passes reliably (10 consecutive runs). Found by P0.2: run 1 had 12 failures in `ComicRack.Mac.Tests` (ReaderFlowTests "A MacHost is already open", CoreThumbnailDecoderTests), run 2 had 2 failures in `Compat.Drawing.Tests` (DisposeTests finalizer / live-image counters), run 3 passed.
- Do: find the causes (process-wide static state: only one MacHost may be open; xunit parallelism across test classes and projects; GC-dependent assertions sharing global counters) and fix them in tests/test infrastructure under `mac/` only: xunit collections or `[assembly: CollectionBehavior(DisableTestParallelization = true)]` where static state is shared, serialize the projects in verify.sh if they share the global state, make GC-counter assertions robust (retry with GC.Collect/WaitForPendingFinalizers loops, local counters). No weakening of what the tests check.
- Out of scope: product code changes unless a real race is found (then fix it in `mac/` product code and say so).
- Accept: `for i in 1 2 3 4 5 6 7 8 9 10; do mac/tools/verify.sh || break; done` all PASS (record durations; keep the fast gate under ~3 minutes if possible).

#### RV2 Fix the remaining flaky UI tests
- Depends: none · Effort: high · Unattended: yes · Status: done 2026-10-20
- Goal: no flaky test in the fast gate or `--full`. Known: `WindowTests.Pressing_Cancel_in_the_window_cancels_the_running_scan` (progress-panel check races with a 200-book scan that finishes early; make the scan deterministic, e.g. a fake or a large/slow scan source, or gate it with a controllable scanner), `ReaderWindowTests.The_arrow_keys_space_home_and_end_turn_the_pages_and_the_frame_follows` (failed once in `--full`, `Dispose` assertion expected 2, actual 1; find the race), plus anything the repeated runs reveal.
- Do: run `mac/tools/verify.sh --full` 15 times, collect failures, fix the causes in tests/test infrastructure (or product code if a real race), never weaken assertions. Add the rule "no timing-dependent assertions without a controllable clock/gate" to full-port.md §2 if not present.
- Accept: 15 consecutive `mac/tools/verify.sh --full` runs PASS.

#### P3.H HiDPI test harness: 1x and 2x (Retina) rendering
- Depends: P0.6b · Effort: high · Unattended: yes · Status: done-with-caveat 2026-10-20 (the real-screen case `hidpi` was SKIPPED because the screen was locked, so it has never run; no public tool sets a 1x display mode, so a real 1x run is not possible on this Mac)
- Goal: rendering is tested at 1.0x and 2.0x (also 1.5x/3.0x where cheap), because DPI handling is a classic weak spot. Requirement added by the owner 2026-10-20.
- Do: (1) A headless test fixture that renders windows/controls at a chosen render scaling (Avalonia.Headless: set the screen/window scaling; if headless cannot, add a test-only scaling wrapper), capturing frames to PNG at device pixels. (2) Tests, run at 1x and 2x for: grid cells and thumbnails (covers sharp, not upscaled/blurred; the 512 px cap vs 2x cells is evaluated and a decision recorded), reader page drawing (pixel snapping, no seams in spreads, correct fit at both scales, zoom), custom draw operations (`CoverDrawOperation`, `ReaderView`), text/icon sizes (logical size constant, device size doubles), 1px lines/borders crisp, hit-testing and pan/zoom math in logical vs device units, and the Compat.Drawing/Graphics shim paths that use DPI (`DpiX/DpiY`, `Graphics.PageUnit`, resolution of Bitmap). Property: the same logical layout yields identical logical geometry at every scale; device-pixel frames differ only by the scale factor (compare downscaled 2x with 1x within a tolerance). (3) Real-screen e2e case in `mac/tests/e2e/cases/`: screenshot of the running app on this Retina display and a check of the window scale; if a non-Retina or scaled display mode can be set via public tools, run once at 1x, else record as SKIPPED and rely on (1)/(2). (4) A rule in full-port.md §2 and the subagent prompt: every new custom-drawn control must add a 1x/2x test using this harness. Existing MVP custom controls get tests now.
- Out of scope: restyling (P5.R*).
- Accept: `dotnet test mac/tests/ComicRack.Mac.Tests --filter Category=HiDpi` passes (state the count, both scales covered per control); `mac/tools/verify.sh`.

#### RV3 Fix the GetIsoCulture race in the host (smart lists lose books)
- Depends: P0.7a · Effort: medium · Unattended: yes · Status: done 2026-10-20
- Goal: smart lists with a language matcher return correct results every time. P0.7a found that `ComicBook.GetIsoCulture` fills a static unlocked `Dictionary`, and PLINQ threads race on first evaluation (a 64-book list returned 30 or 31 in 2 of 12 fresh processes).
- Do: in `MacHost.ConfigureCore` (mac-owned) fill the dictionary once on one thread before any list evaluation (warm-up through the public API that triggers it, for all cultures it caches); remove `CoreRaces.WarmLanguageNames` workaround from the tests so they test the real path; audit other lazily filled static caches reachable from PLINQ list evaluation (grep the Core for static Dictionary/List fields used in matchers/ComicBook properties) and warm or document them. Record any race that cannot be fixed without editing an original file as a proposal in `mac/upstream-proposals/` (do not edit originals).
- Accept: `dotnet test mac/tests/ComicRack.Characterization` passes 5 times in separate processes; a new test evaluates the language list in 10 fresh child processes and always gets the golden count; `mac/tools/verify.sh`.

#### RV4a Review fixes: plan tooling, safety scripts, docs (wave A review, High 1-2, Medium 3-6, 10-11, Low 2-4, 8-10)
- Depends: none · Effort: high · Unattended: yes · Status: done 2026-10-20
- Goal: fix the findings of the wave A review in tooling and docs. Source: the review report summarised here.
- Do: (1) `mac/tools/plan-status.sh`: regenerate the status table and `Run state:`/next-task line of full-port.md from the Status lines, and backfill `mac/plans/run-log.md` from `git log` (one line per `mac: <ID>` commit); RUNBOOK step 6 calls the script; add a test. (2) `mac/tools/claude-allowlist.json`: remove raw `Bash(osascript *)`, scope Edit/Write to `mac/**` and `MACOS_NATIVE_PATH.md`, list exact stash subcommands (push -u -m, list, show), replace the loose `rm -rf mac/*` with exact find/rm patterns, add the commands the RUNBOOK itself needs (`git restore mac/reports`, `python3 mac/tools/...`); check run-plan.sh/RUNBOOK use only allowed commands. (3) RUNBOOK §4 full gate: `git restore mac/reports` (or census output to artifacts) before `upstream-sync-check.sh --no-merge`. (4) `mac/tools/e2e.sh`: non-zero exit when no case passed; separate exit code for 'launch refused'. (5) `ui-drive.sh`: verify pid ownership (process path matches UD_EXE and exit file empty) before input/quit/kill; per-chunk frontmost checks while typing; reject protected `--data-dir` (~, ~/Documents, ~/Library root, /Users/vince/dev/ComicRackAPI...) ; escape session-file values with printf %q; blank check = low variance not identical pixels. (6) Align UPSTREAM-SYNC.md and RUNBOOK §5 on new upstream UI (ledger row + U-task; make `ui-parity` WARN in upstream-sync-check). (7) P4.3 block: move the IronPython patch build step, `IronPython.StdLib` package and the sample-script acceptance to what is achievable in wave B (acceptance = headless hooks of Sample.py run with the rewritten-refs temp copy as S4 did; full unmodified acceptance stays in P4.8), update dependencies. (8) `verify.sh`: unset UPDATE_GOLDENS (or refuse). (9) `run-plan.sh`: model default from `COMICRACK_PLAN_MODEL` env/RUNBOOK, no tier name literal in code. (10) OWNER-ACTIONS C2 list: add hidpi e2e + S6 real.sh; MACOS_NATIVE_PATH §9.1 'task branch' wording; rename review fixes to `RV<n>` (rename R1-R4 consistently in docs, keep history).
- Out of scope: product code and tests (RV4b).
- Accept: `mac/tools/plan-status.sh --check` exits 0 and the table equals the Status lines; new/updated script tests pass; `mac/tools/verify.sh`.

#### RV4b Review fixes: teardown, scanner wait, memory test, test strength (wave A review, Medium 7-9, Low 1, 5, 6, 7)
- Depends: none · Effort: high · Unattended: yes · Status: done 2026-10-20
- Goal: fix product/test findings. (1) MacHost teardown after an abandoned scanner thread (MacHost.cs ~312-315, PassB/ComicScanner.cs ~100-106): detect a live 'Book Scanner' thread, warn, mark save verification 'unchecked' instead of false failure/silent loss, do not raise ScanNotify on a disposed QueueManager. (2) FolderScanner.WaitForScannerThreadToExit waits on the Thread object this host started, not the global name. (3) FixtureGridTests memory test: assert `peakAfterWarm - footWarm` against a bound as well. (4) Drift guards: keep an independent hash for CacheManager in the test or document why not; set manifest line budgets to actual counts. (5) Strengthen weak tests: GridHiDpiTests `dark > 0`, MainData 1.0 self-compare, NameInfo golden name that the parsers read differently (e.g. 'Batman.012.1940.cbz'), deterministic check of ComicBook.languages in the language race test. (6) `ScriptSelfTest.cs`: dev-only (not reachable in release bundle unless env var), refuse the real Scripts folder, delete its temp folder. Add tests for each fix (mutation-check where feasible).
- Accept: new tests fail without the fix (show once) and pass with it; `mac/tools/verify.sh --full` once.

#### RV5 Chrome isolation: one swappable layer (owner requirement 2026-10-20)
- Depends: P5.R1 · Effort: high · Unattended: yes · Status: done 2026-10-20
- Goal: the chrome (everything that makes the app look like ComicRack rather than stock Avalonia/macOS) lives in ONE isolated layer that can be changed or replaced per element without touching views, view-models or logic, so the owner can later move individual elements to a native macOS look. Rule added as R10: views and view-models never contain chrome (no literal colours/fonts/sizes/paths of icons, no custom templates inline); they use semantic parts only.
- Do: (1) Audit the current code (`mac/ComicRack.Mac/Controls/`, `Themes/`, `Views/`, `Browser/`, `Reader/` views) and write `mac/plans/chrome-architecture.md`: the layers (tokens -> control themes/templates -> `Controls/` wrappers (Cr* controls) -> views), the public surface views may use (Cr* controls, style classes like `cr-toolbar`, `Cr.*` tokens via ThemeTokens, `Icons.Get(name)`), and the swap mechanism. (2) Implement a per-element chrome variant switch in one place: `ChromeVariants` (e.g. Toolbar, TabBar, StatusBar, Tree, ListHeader, Buttons, ComboBox, Dialogs, Menus...) each `Original` (default) | `Native`, set from one file/setting (`mac/ComicRack.Mac/Themes/ChromeVariants.cs` + optional env/pref override), resolved by control themes (e.g. ControlTheme selection by variant class on the root) so a variant can be flipped without editing any view. Implement at least one real `Native` variant end-to-end as proof (e.g. the status bar or the buttons use stock Fluent/macOS-leaning look) and a test proving the flip changes only the theme, not the view tree. (3) Move any chrome leaking in views (inline styles, templates, fonts, icon paths, sizes constants that are look-only) into the layer; views reference style classes/Cr* controls only. (4) Enforcement tests (architecture tests): views/viewmodels assemblies' sources contain no colour literals/FontFamily literals/inline `<Style>`/`<ControlTemplate>`; Views may reference only the allowed namespaces; every Cr* control resolves through a ControlTheme keyed by variant; `Icons` is the only way to load icons; failing test lists offenders. (5) Add the rule to full-port.md §2.11 and to the subagent prompt in RUNBOOK §3 ('chrome only in the chrome layer'); add a 'how to switch an element to native' section to `chrome-guidelines.md`.
- Out of scope: new visual designs; changing the default look.
- Accept: architecture tests pass and fail when an inline colour/template is added to a view (show once); variant flip test passes; `mac/tools/verify.sh`; the e2e `chrome-gallery` case unchanged (default look identical: compare screenshots before/after within a tolerance via `crcli image`).

#### RV5b Chrome isolation, part 2: Buttons, ComboBox, Menus and the remaining elements switchable
- Depends: RV5, P5.R3 · Effort: high · Unattended: yes · Status: todo
- Goal: finish what RV5 left: every element listed in `ChromeVariants` is switchable Original|Native without editing views (Buttons, ComboBox, Menus have global base-theme overrides today; toolbar, tab bar, header, group box, side panel and tree have no Native variant). Native = a reasonable macOS-leaning look built from stock Fluent/native controls, not pixel-perfect AppKit.
- Do: convert the global overrides into per-control ControlThemes selected by variant; add Native variants for the remaining elements per `chrome-guidelines.md` §10; tests: flip each element, view tree unchanged, default look unchanged (snapshot baselines), light and dark, 1x and 2x.
- Accept: `dotnet test mac/tests/ComicRack.Mac.Tests --filter "ChromeVariant|ChromeArchitecture|ChromeSnapshot"`; `mac/tools/verify.sh`; contact sheet with all-Native next to all-Original in `mac/artifacts/chrome/RV5b/`.

#### RV6 Browser selection parity: first arrow key, rubber band, drag-out
- Depends: P5.3 · Effort: high · Unattended: yes · Status: done 2026-10-21
- Goal: R9 says behaviour must be identical to the Windows app. P5.3 left three deviations: (1) with nothing focused the original's first arrow key moves relative to item 0 (Right selects the second book); the Mac grid selects the first book; (2) rubber band (marquee) selection is missing; (3) dragging the selection out of the grid (drag-and-drop source) is missing. Derive the exact behaviour from `cYo.Common.Windows/Forms/ItemView*.cs` (read-only) and implement it in the Mac grid with tests; keep any remaining true deviation in the parity ledger with a reason.
- Accept: new tests for each (model + real keys/pointer events, 1x and 2x for the marquee drawing); `mac/tools/verify.sh`; ledger rows BV.* updated.

#### P0.1 Fast gate `mac/tools/verify.sh`
- Depends: none · Effort: medium · Unattended: yes · **Status: done 2026-10-17** (planning session)
- Goal: one command that every task and the orchestrator run: disk guard, rule 1, build, fast tests, concise summary.
- Add: `mac/tools/verify.sh`. Edit: `mac/tests/ComicRack.Mac.Tests/Browser/FixtureGridTests.cs` (full scroll marked `Slow`), `mac/tests/ComicRack.Mac.Tests/Reader/PageServiceTests.cs` (order-independent assertion).
- Accept: `mac/tools/verify.sh` exits 0 on the current tree (done: 781 tests, about 1 minute); a non-ignored probe file under `ComicRack/` makes rule 1 FAIL (done); `--bogus` exits 2 (done).

#### P0.2 Plan and licence hygiene
- Depends: none · Effort: low · Unattended: yes · **Status: done 2026-10-17**
- Goal: make the reference document consistent with what the MVP learned.
- Edit: `MACOS_NATIVE_PATH.md` only: §5.3 "GeneXus.Drawing.Common ... (MIT)" to Apache-2.0 and "not adopted (G0)"; §11.4 spike log: S1 (census totals from `mac/reports/census.md`), S2 (from `mac/reports/drawing-route.md`), one line each with date and decision; §9.1/§9.2 references to `mvp-track.md` §4 point to `full-port.md` §2; record in §2.5 whether `LICENSE.txt` and the README say "version 2 only" or "or later" (read only).
- Out of scope: any other file.
- Accept: `grep -n "Apache-2.0" MACOS_NATIVE_PATH.md` shows §5.3; `grep -n "S2" MACOS_NATIVE_PATH.md | grep 2026` shows the §11.4 entry; `verify.sh`.

#### P0.3 Warning hygiene for the linked sources
- Depends: none · Effort: medium · Unattended: yes · Status: done 2026-10-20
- Goal: the build output shows the warnings of mac-owned code, not the ~550 of the decompiled sources.
- Edit: `mac/Shadow.props` (a `NoWarn` list for the shadow projects only: `CS3001;CS3002;CS3003;SYSLIB0003;SYSLIB0004;SYSLIB0021;CA2022;CS0649;CS0472` plus any other id that occurs only in linked files, measured), `mac/tools/verify.sh` (print the warning count of a clean build as an INFO line with `--full`).
- Out of scope: fixing warnings in mac-owned projects (list them in §9 if any).
- Accept: `rm -rf mac/*/bin mac/*/obj && dotnet build mac/ComicRack.Mac.slnx -nologo -v q 2>&1 | grep -c "warning "` drops below 60 (record the number in §3); `verify.sh`.

#### P0.4 Replacement manifest, generic drift guard, `replacement-sync.sh`
- Depends: none · Effort: high · Unattended: yes · Status: done 2026-10-20
- Goal: every replaced, stubbed or excluded original is tracked with a hash and can be re-synced after an upstream change (§5.1).
- Add: `mac/replacements.json` (entries for every `Exclude`, `Compile Remove` and `Replacements/**` file that exists today: CacheManager, PdfiumReaderEngine, PdfStorageProvider, Resources.Designer, FileSystemEnumerable, KnownColorTableEx, UserCredentialsDialog, the M0.3b stubs and exclusions; `base` = `7fd9fd35a9cf829fffc6aa9cd0b44c7325df0cb7`), `mac/tests/ComicRack.Mac.Host.Tests/ReplacementManifestTests.cs`, `mac/tools/replacement-sync.sh`.
- Edit: `mac/tests/ComicRack.Mac.Host.Tests/OriginalSourceDriftTests.cs` (keep the CacheManager line-exact test; read the hash from the manifest). Regenerate `mac/reports/census.md` (the M1.5b note: `FileSystemEnumerable` missing from the replacement table).
- Out of scope: changing any replacement's behaviour.
- Accept: `dotnet test mac/tests/ComicRack.Mac.Host.Tests --filter FullyQualifiedName~ReplacementManifest` passes; mutation: append a space to a scratch copy and point a test-only manifest at it (the test project gets a `--manifest` override through an environment variable) and see it fail; `mac/tools/replacement-sync.sh ComicRack.Engine/CacheManager.cs` prints "unchanged since base"; `verify.sh`.

#### P0.5a Upstream scan: file classification
- Depends: P0.4 · Effort: high · Unattended: yes · Status: done 2026-10-20
- Goal: given a commit range, classify every changed, added, renamed or deleted file as in §5.2 step 2, including new files that the shadow globs pick up and project-file package changes.
- Add: `mac/tools/upstream-scan/` (net10 console, `UpstreamScan.csproj`, in the solution under `/tools/`): `dotnet run --project mac/tools/upstream-scan -- --range A..B [--json out.json]`; tests `mac/tests/Upstream.Tests/` (new project, in the solution) with a synthetic repository built in a temp folder by the test (`git init`, commits that add a linked file, change a replaced file, add a `PackageReference`, add a UI file).
- Out of scope: running builds (P0.5c), API analysis (P0.5b).
- Accept: `dotnet test mac/tests/Upstream.Tests`; `dotnet run --project mac/tools/upstream-scan -- --range 7fd9fd3..HEAD` lists only `mac/` files as "macOS (ours)" and nothing else; `verify.sh`.

#### P0.5b Upstream scan: platform API delta
- Depends: P0.5a · Effort: high · Unattended: yes · Status: done 2026-10-20
- Goal: after a merge, list new WinForms, System.Drawing and P/Invoke usage in linked code and whether the shims cover it.
- Add: `mac/reports/api-baseline.json` (normalized from `mac/artifacts/api-inventory/inventory.json`: type, member, signature, kind; DllImports as module, entry point, declaring type; no call-site file lists), an `--compare <baseline>` mode in `mac/tools/api-inventory` (new members, covered by `cYo.Compat.Drawing` / `cYo.Compat.Forms` yes/no, new DllImports with linked yes/no) and `--write-baseline`.
- Edit: `mac/tools/api-inventory/*`.
- Accept: `dotnet run --project mac/tools/api-inventory -- --compare mac/reports/api-baseline.json` prints "no new platform API" on this tree; a test in `mac/tests/Upstream.Tests` feeds a baseline with one member removed and expects it reported as new and covered; `verify.sh`.

#### P0.5c `upstream-sync-check.sh`, rehearsal, final `mac/UPSTREAM-SYNC.md`
- Depends: P0.5a, P0.5b · Effort: high · Unattended: yes · Status: done 2026-10-20
- Goal: the one command the owner runs after `git merge upstream/master` (§5.2), proven on a rehearsal merge.
- Add: `mac/tools/upstream-sync-check.sh`, `mac/upstream-sync.json` (initial `lastSyncedUpstream` = `7fd9fd35a9cf829fffc6aa9cd0b44c7325df0cb7`).
- Edit: `mac/UPSTREAM-SYNC.md` (remove "harness pending", final commands and outputs), `RUNBOOK.md` §5 if the fix-up loop changes.
- Rehearsal (in a throwaway clone under `$TMPDIR`, never in the working repo): a fake "upstream" commit that (1) changes one line in `ComicRack.Engine/CacheManager.cs`, (2) adds `ComicRack.Engine/IO/NewThing.cs` using `System.Windows.Forms.Clipboard.SetText`, (3) adds a `PackageReference` to `ComicRack.Engine/ComicRack.Engine.csproj`, (4) adds `ComicRack/Dialogs/NewDialog.cs`; merge it into a copy of `macos`; run the harness. Expected: drift FAIL with a clean auto-merge offer, new API FAIL naming `Clipboard.SetText` (covered: yes, it is in the slice; pick an uncovered member if so), package INFO, UI parity INFO, exit 1. Also run `git fetch upstream` in the real repo and `mac/tools/upstream-sync-check.sh --no-merge --range 7fd9fd3..upstream/master` as a read-only preview, recorded in §3 (number of upstream commits waiting, findings).
- Accept: the rehearsal transcript is summarized in §3; `mac/tools/upstream-sync-check.sh --no-merge` exits 0 on the current tree; `verify.sh`.

#### P0.6a UI-drive tool
- Depends: none · Effort: high · Unattended: partial (needs an unlocked GUI; if locked, the task is `blocked (screen locked)` and the orchestrator retries it later) · Status: done 2026-10-20
- Goal: launch, drive and screenshot the real app safely (§5.3, §2.10).
- Add: `mac/tools/ui-drive/ui-drive.sh`, `mac/tools/ui-drive/WindowTool.swift`, `mac/tools/ui-drive/README.md` (commands, safety rules); `crcli image probe <png> <x> <y> [--box N]` and `crcli image stats <png>` (mean, min, max per channel; non-blank check) in `mac/ComicRack.Cli` (new file `mac/ComicRack.Cli/Core/ImageCommands.cs`, tests in `mac/tests/ComicRack.Cli.Tests`).
- Out of scope: test cases (P0.6b).
- Accept: `mac/tools/ui-drive/ui-drive.sh launch --dev --data-dir "$(mktemp -d)" && mac/tools/ui-drive/ui-drive.sh wait-window "ComicRack" 30 && mac/tools/ui-drive/ui-drive.sh menu-items File && mac/tools/ui-drive/ui-drive.sh shot launch && mac/tools/ui-drive/ui-drive.sh quit` prints the File items and exit code 0, and `crcli image stats mac/artifacts/ui-drive/shots/launch.png` reports a non-blank image; the safety check is tested by activating Finder (`osascript -e 'tell application "Finder" to activate'`) before `key cmd+o` and expecting exit 5 with nothing typed; `verify.sh`.

#### P0.6b E2E smoke suite (closes the MVP "not verified on a real screen" items)
- Depends: P0.6a · Effort: high · Unattended: partial (gestures and feel stay owner checks -> C2) · Status: done 2026-10-20
- Goal: `mac/tools/e2e.sh` with cases that check what the MVP could only check headlessly.
- Add: `mac/tools/e2e.sh`, `mac/tests/e2e/lib.sh`, cases in `mac/tests/e2e/cases/`: `launch-quit` (window appears, Cmd+Q exit 0, exactly one data folder below the temp dir), `menu-structure` (bundle: app menu named `ComicRack CE (MVP)`, Quit exactly once; File, View, Window, Help items and shortcuts as in `mvp-acceptance.md` 1.5), `open-folder` (Cmd+O, `choose-path mac/fixtures/small`, log shows the scan end and the count), `import` (dialog, Choose... with `choose-path`, mapping text, 14,706 books when `mac/fixtures/BD` exists, else SKIP), `grid-keys` (arrows, Enter opens a reader window), `reader-keys` (Right x5 gives 5 `Page turn` log lines; Cmd+3 and Cmd+5 change the View menu radio mark; Option+Cmd+2 spread), `fullscreen` (Ctrl+Cmd+F: window bounds equal the screen; Esc restores), `overlay` (Option+Cmd+P: menu check mark on, a perf log line within 12 s; off again), `quit-during-scan` (Cmd+Q while scanning `mac/fixtures/BD-reader`; relaunch shows the saved count), `pdf-cb7` (open both fixtures through `--read-book` in the bundle, screenshots non-blank).
- Out of scope: trackpad gestures (no public API), appearance switching unless `E2E_ALLOW_APPEARANCE=1`.
- Accept: `mac/tools/e2e.sh` and `mac/tools/e2e.sh --bundle menu-structure pdf-cb7` exit 0 (or 4 when the screen is locked: then `done-with-caveat`); results written to §3; `verify.sh`.

#### P0.7a Golden tests on net10: database, lists, names, matchers
- Depends: P0.4 · Effort: high · Unattended: yes · Status: done 2026-10-20
- Goal: the characterization net that P0.3 of `MACOS_NATIVE_PATH.md` §9.3 asked for, on net10 (DD6).
- Add: `mac/tests/ComicRack.Characterization/` (xUnit, in the solution): a committed synthetic `Golden/SmallDb.xml` (about 200 books, written by a test helper from `ComicBook` objects, with lists and smart lists of 10 matcher kinds) and goldens: load then save round-trip equal after canonical XML normalization; the count of every list; `ComicNameInfo` parsing of 200 file names (`ComicRack.Engine/ComicNameInfo.cs`) to a JSON golden; the fixture-DB counts of M2.2c as a skippable golden (needs `mac/fixtures/ComicDb.xml`). A `UPDATE_GOLDENS=1` switch rewrites goldens and prints a diff.
- Accept: `dotnet test mac/tests/ComicRack.Characterization`; changing one expected count makes it fail (tried, restored); `verify.sh`.

#### P0.7b Golden tests on net10: archives, pages, thumbnails
- Depends: P0.7a · Effort: high · Unattended: partial (real CBR and real 7-Zip CB7 need C1 samples; skipped without) · Status: done-with-caveat 2026-10-20 (real CBR and real 7-Zip CB7 not verified: no samples in mac/fixtures/owner, C1)
- Goal: page enumeration, page byte hashes and thumbnail perceptual hashes for CBZ, CB7 (SharpCompress-written), PDF and, if present, `mac/fixtures/owner/*.cbr|*.cb7`.
- Add: `mac/tests/ComicRack.Characterization/ArchiveGoldenTests.cs`, a small committed fixture set generated at test time (`crcli fixtures make` into a temp folder with a fixed seed), a 64-bit average-hash helper.
- Accept: `dotnet test mac/tests/ComicRack.Characterization --filter FullyQualifiedName~Archive`; `verify.sh`.

#### P0.8 Unattended loop `mac/tools/run-plan.sh`
- Depends: none · Effort: medium · Unattended: yes · Status: done 2026-10-20
- Goal: §5.4: run the orchestrator one task per headless run, until STOP.
- Add: `mac/tools/run-plan.sh`, `mac/tools/claude-allowlist.json` (the permission allowlist of `RUNBOOK.md` §7 as a ready-to-merge settings fragment).
- Accept: `mac/tools/run-plan.sh --dry-run --max-tasks 2` prints the two commands it would run and the task each would pick (it reads §6 and the Status lines itself, no headless run), exit 0; `bash -n mac/tools/run-plan.sh`; `verify.sh`.

### P1 Core runtime gaps (zero-edit)

#### P1.1 Replace `Thread.Abort` at its three Core sites
- Depends: P0.4 · Effort: high · Unattended: yes · Status: done 2026-10-20
- Goal: no `PlatformNotSupportedException` on close or cancel paths (`mvp-track.md` M1.5a facts), Windows semantics otherwise unchanged.
- Add (patched copies, pass B only, manifest entries): `mac/cYo.Common.Core/Replacements/PassB/ThreadUtility.cs` (`Abort(thread, timeout)`: `Join(timeout)`, then log and abandon the thread instead of `Thread.Abort`), `mac/cYo.Common.Core/Replacements/PassB/BackgroundRunner.cs` (same in `Dispose`), `mac/ComicRack.Engine.Core/Replacements/PassB/ComicScanner.cs` (same in `Stop`; `abortScanning` behaviour unchanged). `Compile Remove` lines in the two `Sources.props`.
- Edit: `mac/ComicRack.Mac.Host/MacHost.cs` only if `Dispose` needs no special case any more (remove the catch documented in M1.5a).
- Accept: new host test: a queue whose worker ignores its stop flag for 15 s; `MacHost.Dispose` completes without exception and every later disposal step runs; manifest test passes (changed lines within budget); `verify.sh`.

#### P1.2 Windows P/Invoke tripwire
- Depends: none · Effort: high · Unattended: yes · Status: done 2026-10-20
- Goal: a linked Core path that calls `kernel32`, `user32`, `shell32`, `gdi32`, `shlwapi`, `ole32`, `uxtheme`, `dwmapi` or `credui` is logged once with its managed stack, counted, and fails tests, instead of being swallowed (the `FileSystemEnumerable` lesson of M1.5b).
- Edit: `mac/ComicRack.Mac.Host/NativeLibraryResolver.cs` (resolver for all Core assemblies: Windows module names log `WARN Windows API <module>!<first entry> called from <stack>` once per module and entry, increment `WindowsCalls`), `mac/ComicRack.Mac.Host/HostLog.cs` if needed.
- Add: `mac/tests/ComicRack.Mac.Host.Tests/WindowsTripwireTests.cs` (open, scan `crcli fixtures make` output, thumbnail, page, smart lists, save, close: zero calls; a deliberate call to `cYo.Common.Win32` code is detected).
- Out of scope: fixing the paths found (record each in §9 with the stack; P1.3 fixes the ones on the v1 path).
- Accept: `dotnet test mac/tests/ComicRack.Mac.Host.Tests --filter FullyQualifiedName~Tripwire`; the app smoke run (`dotnet run --project mac/ComicRack.Mac -- --smoke-test=3 --headless --data-dir "$(mktemp -d)"`) logs no tripwire line; `verify.sh`.

#### P1.3 Runtime gap sweep: Common resources, NTFS store, tripwire findings
- Depends: P1.2 · Effort: medium · Unattended: yes · Status: done 2026-10-20
- Goal: close the known small runtime gaps.
- Add: `mac/cYo.Common.Core/Replacements/PassB/Resources.cs` (Common's `Properties/Resources`: `Wikipedia`, `PlusOverlay` from embedded files, same pattern as the Engine replacement of M1.3) with manifest entry.
- Edit: `mac/ComicRack.Mac.Host/MacHost.cs` (`ConfigureCore`: `EngineConfiguration.Default.DisableNTFS = true`, `ComicRack.Engine/EngineConfiguration.cs:600`, so `NtfsInfoStorage` never reaches kernel32), plus a fix per tripwire finding of P1.2 that is on a v1 path (each as a replacement with a manifest entry).
- Accept: new host test reading both Common resources; the P1.2 tripwire test still shows zero calls; `verify.sh`.

### P2 Platform pieces

#### P2.1 Shell services: Trash, Reveal in Finder, open with the default app
- Depends: P1.2 · Effort: high · Unattended: yes · Status: todo
- Goal: the Mac equivalents of `cYo.Common/Win32/ShellFile.cs` and `FileExplorer.cs` for the UI and for any Core caller.
- Add: `mac/ComicRack.Mac.Host/Platform/MacShell.cs` (`MoveToTrash(path)` through `NSFileManager trashItemAtURL:resultingItemURL:error:` via the Objective-C runtime (`objc_msgSend` P/Invoke), `Reveal(paths)` via `/usr/bin/open -R`, `OpenWithDefaultApp(path)` via `/usr/bin/open`), tests in `mac/tests/ComicRack.Mac.Host.Tests/MacShellTests.cs` (trash a temp file and find it in `~/.Trash` by the resulting URL, then delete it there). If `grep -rn "ShellFile\.\|FileExplorer\." ComicRack.Engine cYo.Common` shows Core callers on v1 paths (for example book deletion), add a pass B rewrite of `ShellFile` with the same surface that calls `MacShell` (manifest entry).
- Accept: `dotnet test mac/tests/ComicRack.Mac.Host.Tests --filter FullyQualifiedName~MacShell`; `verify.sh`.

#### P2.2 System services: sleep prevention, memory info, volumes
- Depends: P1.2 · Effort: high · Unattended: yes · Status: todo
- Goal: `IOPMAssertionCreateWithName` while reading and scanning; RAM size for cache defaults; a `DriveChecker` that understands `/Volumes/*` (needed for "Remove missing books", P5.19b).
- Add: `mac/ComicRack.Mac.Host/Platform/{PowerAssertion,SystemInfo}.cs`; pass B rewrite `mac/cYo.Common.Core/Replacements/PassB/DriveChecker.cs` (same surface as `cYo.Common/IO/DriveChecker.cs`; a path is "connected" if its volume root exists; manifest entry); tests including a disk image (`hdiutil create -size 20m -fs APFS`, attach, check, detach).
- Accept: `dotnet test mac/tests/ComicRack.Mac.Host.Tests --filter "FullyQualifiedName~PowerAssertion|FullyQualifiedName~DriveChecker"`; `pmset -g assertions | grep -i comicrack` shows the assertion during a test run (printed by the test); `verify.sh`.

#### P2.3a Spike S3: ImageIO decode vs bundled dylibs
- Depends: none · Effort: high · Unattended: yes · Status: todo
- Goal: decide Q2 with data: `CGImageSourceCreateWithData` for HEIF/HEIC, AVIF, JPEG XL, JPEG 2000, TIFF, WebP vs libheif/libjxl/CSJ2K.
- Add: `mac/spikes/S3-imageio/` (not in the solution), `mac/reports/codec-route.md` (formats decoded on macOS 14 and on this macOS, speed per MP, memory, what encoders exist for fixtures: `sips -s format heic|jp2|tiff`, ImageIO AVIF encode yes/no), sample sources with licences (generated with `sips`; JXL/AVIF from a public test set with recorded URL and SHA-256 if no encoder exists).
- Accept: the report has a decision line (default ImageIO) and the per-format table; `MACOS_NATIVE_PATH.md` §11.4 gets the S3 line; `verify.sh`.

#### P2.3b ImageIO decode fallback in the shim and the app decoders
- Depends: P2.3a · Effort: high · Unattended: yes · Status: todo
- Goal: `Image.FromStream`/`FromFile` and the reader's and thumbnails' Skia decode fall back to ImageIO for formats Skia cannot decode (TIFF, HEIF/HEIC, AVIF, JXL, JP2 as the report says).
- Add: `mac/cYo.Compat.Drawing/Internal/ImageIODecoder.cs` (CoreGraphics/ImageIO P/Invoke, decode into the BGRA storage), public `SkiaInterop.TryDecodeWithImageIO`; tests in `mac/tests/Compat.Drawing.Tests/ImageIOTests.cs` with fixtures generated by `sips` at test time.
- Edit: `mac/cYo.Compat.Drawing/Internal/ImageCodec.cs`, `mac/ComicRack.Mac/Reader/PagePixels.cs`, `mac/ComicRack.Mac/Thumbnails/ThumbnailPixels.cs`.
- Accept: `dotnet test mac/tests/Compat.Drawing.Tests --filter FullyQualifiedName~ImageIO`; `crcli page` on a CBZ with HEIC and TIFF pages (made by a test helper) gives the right sizes; `verify.sh`.

#### P2.3c Core codec classes on macOS
- Depends: P2.3b, P0.4 · Effort: high · Unattended: yes · Status: todo
- Goal: archives with HEIF/AVIF/JXL/JP2/WebP pages work through the Core's own paths (`ComicRack.Engine/IO/Provider/{HeifAvifImage,JpegXLImage,WebpImage,Jpeg2000Image}.cs`, hard-coded `Resources\x64\libwebp.dll`).
- Add: pass B rewrites of those four classes (same surface, ImageIO or Skia inside; manifest entries), so `ImageProvider` conversions and the Core's thumbnail generation work.
- Accept: `crcli thumb` and `crcli page` on generated books with each format; the P1.2 tripwire shows no native-load attempt for `jxl`, `libwebp`, `libheif`; `verify.sh`.

#### P2.4a Bundle `7zz`
- Depends: none · Effort: medium · Unattended: yes · Status: todo
- Goal: the official 7-Zip macOS console binary for ComicInfo writes and CB7 creation (DD10).
- Add: `mac/native/fetch-7zz.sh` (downloads the current official `7z<ver>-mac.tar.xz` from 7-zip.org into `mac/artifacts/native/`, checks a pinned SHA-256 written in the script, extracts `7zz`; never uses Homebrew), `mac/ComicRack.Mac.Host/Platform/ToolLocator.cs` (`SevenZip` path: next to the executable, else `mac/artifacts/native/7zz` in a dev tree).
- Edit: `mac/tools/make-app.sh` (copy and sign `7zz` into `Contents/MacOS`), `mac/ComicRack.Cli/Fixtures/FixtureCommands.cs` (`fixtures make --formats cb7` uses the located `7zz`; adjust the M1.6 skip test).
- Accept: `mac/native/fetch-7zz.sh && mac/artifacts/native/7zz i | head -3`; `crcli fixtures make "$(mktemp -d)" --books 2 --pages 3 --formats cb7` writes CB7 files that `crcli page` opens; `verify.sh`.

#### P2.4b ComicInfo write-back engine
- Depends: P2.4a, P0.4 · Effort: high · Unattended: partial (CBR behaviour with a real CBR -> C1/C2) · Status: todo
- Goal: `ComicBook.WriteInfoToFile` works for CBZ, CB7 and CBT on macOS.
- Add: pass B patched copy `mac/ComicRack.Engine.Core/Replacements/PassB/SevenZipEngine.cs` (console paths from `ToolLocator`; `Path.Combine` instead of backslashes; nothing else), manifest entry; `crcli info show <book>` and `crcli info write <book> --set Field=Value ...` (`mac/ComicRack.Cli/Core/InfoCommands.cs`); tests: write Series, Number, Summary with accents, read back through the Core and through SharpCompress (the `ComicInfo.xml` entry), the page entries unchanged (hashes), file timestamps updated.
- Accept: `dotnet test mac/tests/ComicRack.Cli.Tests --filter FullyQualifiedName~Info`; manifest test; `verify.sh`. Owner check (C2): a real CBR (the Core's behaviour for RAR, recorded in §3).

#### P2.5 PDF extras
- Depends: none · Effort: medium · Unattended: yes · Status: todo
- Goal: password-protected PDFs, memory on large PDFs, render flags.
- Edit: `mac/ComicRack.Engine.Core/Replacements/PassB/PdfiumReaderEngine.cs` (password from the Core's provider path, annotations flag as the original), tests with a password PDF generated by a test helper (pdfium cannot encrypt; record the approach: a committed tiny encrypted PDF made with `cupsfilter`/Preview is not reproducible, so generate with a minimal PDF encryption writer or skip with a reason in §3).
- Accept: tests; a 300-page generated PDF read end to end with RSS growth recorded in §3; `verify.sh`.

#### P2.6 Bundle payload and TR bootstrap
- Depends: none · Effort: high · Unattended: yes · Status: done 2026-10-20
- Goal: Languages, Scripts, Help, default lists and textures from `ComicRack/Output/` ship with the app (copied at build time, never edited), and the Core's TR uses them.
- Edit: `mac/ComicRack.Mac/ComicRack.Mac.csproj` (`None Include="../../ComicRack/Output/Languages/**" CopyToOutputDirectory`, same for `Scripts`, `Help` (the `*.ini` links; not the `.djvu` introduction, DjVu is deferred), `DefaultLists.txt`, `Resources/Icons` and `Resources/Textures`; never the Windows binaries in `Resources/` and `Resources/x64`), `mac/tools/make-app.sh` (they end up in `Contents/MacOS` next to the entry assembly, where `TRDictionary` looks), `mac/ComicRack.Mac/App.axaml.cs` (language: setting, else the first macOS preferred language that exists in `Languages/`, else English).
- Add: `mac/tests/ComicRack.Mac.Tests/LocalizationTests.cs` (a German TR string resolves; English fallback).
- Accept: tests; `ls "mac/artifacts/ComicRack CE (MVP).app/Contents/MacOS/Languages/de" | wc -l` is 72 after `mac/tools/make-app.sh`; `verify.sh`.

### P3 Fidelity, performance, CI

#### P3.1 `crcli db synth` (M1.5c, part 1)
- Depends: none · Effort: medium · Unattended: yes · Status: todo
- Goal: synthetic libraries of N books written straight as XML (fast for 100,000).
- Add: `mac/ComicRack.Cli/Core/SynthCommand.cs`: `crcli db synth --count N --out x.xml [--files <fixture-folder>] [--seed S]` (plausible series, numbers, titles, publishers, writers, dates, read state; file paths cycle over existing fixture books so covers resolve).
- Accept: `crcli db synth --count 100000 --out "$(mktemp -d)/db.xml"` in under 30 s; `crcli db stats` on it reports 100000; `verify.sh`.

#### P3.2 `crcli bench` (M1.5c, part 2)
- Depends: P3.1 · Effort: medium · Unattended: yes · Status: todo
- Goal: one reproducible suite printing markdown and JSON.
- Add: `mac/ComicRack.Cli/Core/BenchCommand.cs`: `crcli bench <fixture-folder> [--runs 5] [--json out.json]`: DB load and save at 10k and 100k (synth), scan, page decode p50/p95 cold and cached, thumbnail throughput (`thumbs <db> --count N`), smart-list evaluation of all lists, machine info (model, RAM, macOS, power, load average).
- Accept: `crcli bench mac/fixtures/small --runs 1` completes and writes JSON; `verify.sh`.

#### P3.3 Release benchmark baseline (M1.8) and regression guard
- Depends: P3.2 · Effort: medium · Unattended: yes · Status: todo
- Goal: Release numbers on a quiet machine (median of 5) and a stored baseline.
- Add: `mac/benchmarks/baseline.md`, `mac/benchmarks/baseline.json`; `crcli bench --compare mac/benchmarks/baseline.json` (WARN above +25 %, FAIL above +100 %).
- Decide and record in §3 (the M1.2b, M1.3, M2.1, M2.2b, M2.3b watch items): 24 bpp convert cost, GC pressure, `Resources` decode cost, scan time per book (parallel scan needed or not), spread prefetch `ReaderOptions.Ahead`, cover cache default (DD9).
- Accept: the baseline files exist; `crcli bench mac/fixtures/small --runs 1 --compare mac/benchmarks/baseline.json` exits 0; `verify.sh`.

#### P3.4 Drawing shim: text
- Depends: none · Effort: high · Unattended: yes · Status: todo
- Goal: `Font`, `FontFamily`, `StringFormat`, `SystemFonts`, `Graphics.DrawString`/`MeasureString`, `TextRenderingHint` on Skia with HarfBuzz shaping, family mapping (Microsoft Sans Serif, Segoe UI, Tahoma, Arial to the system font).
- Edit: `mac/cYo.Compat.Drawing/Types/{Font,FontFamily,StringFormat,SystemFonts,Graphics}.cs`; tests `mac/tests/Compat.Drawing.Tests/TextTests.cs` (measure within ±10 % of expected widths for a reference string; alignment flags; wrapping; trimming).
- Accept: tests; `drawing-coverage` stub count drops (record); `ImagePool.CreateErrorPage` of the original renders (test calls the base implementation); `verify.sh`.

#### P3.5a Drawing shim: state, clipping, pens, shapes
- Depends: none · Effort: high · Unattended: yes · Status: todo
- Goal: the M1.2c "not done" list: `Save`/`Restore`, `Clip`/`SetClip`, `DrawLine`/`DrawRectangle`/`DrawEllipse`/`FillEllipse` with a real `Pen`, named `Brushes`/`Pens`, `ColorMatrix.Matrix00..44`, `ImageAttributes` overloads, mirrored `DrawImage`; relax the locked-bitmap rule only if a caller needs it.
- Edit: `mac/cYo.Compat.Drawing/Types/*`; tests.
- Accept: tests; `drawing-coverage` PASS; `verify.sh`.

#### P3.5b Drawing shim: paths, gradients, matrices
- Depends: P3.5a · Effort: high · Unattended: yes · Status: todo
- Goal: `GraphicsPath` (lines, beziers, arcs, rectangles, rounded rectangles), `LinearGradientBrush`, `Matrix` (`Rotate`, `Translate`, `TransformPoints`), `Graphics.Transform`.
- Accept: tests (pixel checks); `verify.sh`.

#### P3.6 Imaging regression goldens
- Depends: P0.7a · Effort: high · Unattended: yes · Status: todo
- Goal: the `ImageProcessing` adjustments (brightness, contrast, saturation, gamma, sharpen, auto contrast), `BitmapExtensions` resizes and `PageRendering` composites of the Core, recorded as goldens on net10 (perceptual hash plus mean error) so shim changes cannot alter them unnoticed.
- Add: `mac/tests/ComicRack.Characterization/ImagingGoldenTests.cs`, `Golden/imaging/*.json`.
- Accept: tests; changing the shim's bilinear weights breaks them (tried, restored); `verify.sh`.

#### P3.7 Covers: quality, Retina sizes, cache preference
- Depends: P3.3 · Effort: high · Unattended: partial (look judged at C2) · Status: todo
- Goal: covers sharp at 360 DIP on Retina (serve above 512 px from the cover page through the reader decode path when the step is above 512), the Core's `ThumbnailImage.Resampling` choice measured on stand-in covers (aliasing metric from M1.2c), cover-cache size from settings.
- Edit: `mac/ComicRack.Mac/Thumbnails/*`, `mac/ComicRack.Mac.Host/MacHost.cs`.
- Accept: tests for the size steps above 512 and the resampling metric; `verify.sh`.

#### P3.8 (conditional) Skip the Core's eager thumbnail decode
- Depends: P3.3 · Effort: medium · Unattended: yes · Status: todo
- Goal: only if P3.3 shows first-scroll CPU as the cost: read the Core's thumbnail disk-cache file format directly (M2.2a item). Otherwise mark `skipped (P3.3: not needed)`.
- Accept: bench thumbnail throughput improves by at least 20 %; `verify.sh`.

#### P3.9 macOS CI build and test workflow (M1.7)
- Depends: none · Effort: medium · Unattended: partial (a green run needs the owner to push -> C2) · Status: todo
- Goal: `.github/workflows/mac-build.yml` on `macos-15`: setup-dotnet 10.0.x, pass A build of `mac/ComicRack.Plugins.Core` (reference pack), `mac/tools/verify.sh --full` (CI mode: rule 1 against the merge base of the push), `drawing-coverage`, app headless smoke; NuGet cache; triggers on `mac/**` and the five linked source folders; existing workflows untouched.
- Add: `.github/workflows/mac-build.yml`; a CI mode in `verify.sh` (`--ci`: no disk STOP below 3 GB on runners, base `origin/master`).
- Accept: `ruby -ryaml -e 'YAML.load_file(".github/workflows/mac-build.yml")'` parses; `mac/tools/verify.sh --ci --no-build --no-test` runs; `verify.sh`.

#### P3.10 Weekly upstream dry-run workflow
- Depends: P0.5c, P3.9 · Effort: medium · Unattended: partial (runs only after a push) · Status: todo
- Goal: `.github/workflows/mac-upstream-dryrun.yml` (weekly schedule and manual): fetch upstream, merge into a temporary branch in the runner, run `mac/tools/upstream-sync-check.sh`, upload the report as an artifact, never push.
- Accept: YAML parses; the script's `--range` mode works without a merge commit; `verify.sh`.

### P4 Scripting and the Script UI Compatibility Layer

Spikes first; their verdicts decide whether wave E runs. Headless scripting (P4.3 to P4.6) does not depend on the Script UI verdict.

#### P4.1 Spike S4: IronPython 2.7.12 on .NET 10 arm64, also in the hardened bundle
- Depends: none · Effort: high · Unattended: yes (a NO-GO raises C3) · Status: done 2026-10-20, verdict GO (with a build-time binary patch of IronPython.dll; stock 2.7.12 is NO-GO on .NET 9 and later, see `mac/reports/s4-ironpython.md`)
- Goal: `PythonCommand.CompileExpression` with a smart-list expression, `Sample.py` headless hooks, `clr.AddReference('System.Drawing')` / `('System.Windows.Forms')` and `from System.Drawing import Bitmap` resolution (which assembly loads), `DuckTyping` need, StdLib import (`import os, re, datetime`), all inside the signed bundle (hardened runtime, `allow-jit`).
- Add: `mac/spikes/S4-ironpython/` (console, not in the solution) and a hidden `--script-selftest FILE` option in `mac/ComicRack.Mac/AppOptions.cs` (so the bundle's own process runs it), `mac/reports/s4-ironpython.md`.
- Accept: report with GO/NO-GO and the resolution table; `MACOS_NATIVE_PATH.md` §11.4 line; `verify.sh`. NO-GO: write C3 options to `OWNER-ACTIONS.md`.

#### P4.2 Spike S6: Script UI modal loop and assembly identity
- Depends: none · Effort: xhigh · Unattended: partial (needs an unlocked GUI for the real-window cases; a NO-GO raises C3) · Status: done-with-caveat 2026-10-20 (verdict GO; the real-window runs of `mac/spikes/S6-modal/real.sh` are SKIPPED, screen locked; headless evidence complete, see `mac/reports/s6-modal.md`)
- Goal: `mac/spikes/S6-modal/` (Avalonia app, not in the solution): a minimal `Form` facade with `ShowDialog()` on a nested `DispatcherFrame` from the UI thread and from a background thread, `MessageBox.Show` blocking, closing during a nested frame, a Python `Control` subclass overriding `OnPaint` (IronPython), the identity choice (literal `System.Windows.Forms.dll` name vs `AssemblyLoadContext.Resolving` hook, public-key-token behaviour); `Sample.py` and `Autonumber.py` dialogs shown.
- Add: the spike, `mac/reports/s6-modal.md` (each case PASS/FAIL, ui-drive screenshots, GO/NO-GO, identity decision).
- Accept: report exists; `MACOS_NATIVE_PATH.md` §11.4 line; `verify.sh`.

#### P4.3 Headless plugin engine in the host
- Depends: P4.1 (GO), P2.6 · Effort: high · Unattended: yes · Status: todo
- Goal: scripts run on the Mac without UI: hooks Startup, Shutdown, Books, NewBooks, Library, ParseComicPath, CreateBookList, NetSearch; Python smart-list expressions; `ComicBookPluginMatcher` lists evaluate (the two fixture lists get real counts; the "Requires scripts" badge appears only when scripts are off). Wave B scope: this task does not need the assembly identity (P4.8, wave E) or the UI facade (P4.7 to P4.10); what depends on them is accepted there.
- Add: `mac/ComicRack.Mac.Host/Scripting/{MacPluginEnvironment,MacScriptApplication,ScriptFolders,ScriptHost}.cs` (implementations of `IPluginEnvironment`, `IApplication`, `IBrowser`, `IOpenBooksManager` over the host; UI calls go to an injectable `IScriptUi` that headless runs answer with defaults), script folders = bundled `Scripts/` (P2.6) + `<data>/Scripts`; `crcli script run <file.py> --hook Books --db x.xml` and `crcli script expr "<expr>" --db x.xml`.
- Add (from the facts of P4.1): the IronPython patch as a build step: `mac/tools/patch-ironpython` (a copy of `mac/spikes/S4-patch`, Mono.Cecil, run after restore on the `IronPython.dll` of the host output and, from `mac/tools/make-app.sh`, of the bundle; without it no new-style Python class can be created on .NET 10); the `IronPython.StdLib` package of the same version (`Lib/` next to the host assembly, `Contents/MacOS/Lib` in the bundle); the Apache-2.0 licence text of IronPython in the bundle; a test that creates a new-style Python class (`class X(object)`) and fails when the patch is missing.
- Edit: `mac/ComicRack.Mac.Host/MacHost.cs`, `mac/ComicRack.Mac.Host/Library/LibraryLists.cs`, `mac/ComicRack.Mac/ViewModels/SidebarViewModel.cs`, `mac/tools/make-app.sh`.
- Accept: tests: the headless hooks of every bundled sample that has them run on a fixture DB from a temporary copy whose `clr.AddReference*` lines for `System.Windows.Forms`, `System.Drawing` and the original Core assembly names are rewritten, as the S4 selftest did with `COMICRACK_SELFTEST_REWRITE_REFS` (`Sample.py`: ParseComicPath, CreateBookList, NetSearch, ComicInfoHtml, QuickOpenHtml; the UI hooks are not run); the same hooks through the bundle's `--script-selftest`; a Python expression list gives the same count as the equivalent non-Python matcher; `crcli smartlist --all --remap ...` on the fixture DB shows numbers for "Missing (to fix)" and "Next issues to read" (recorded); the new-style class test above; `verify.sh`. The unmodified bundled scripts (no rewritten references) are accepted by P4.8 (identity) and the UI hooks by P4.10.
- Parity: SC.01 to SC.03, SC.05, SC.06, SC.09 to SC.11, SC.18 (headless part; ledger rows stay `todo` until P4.5 adds the UI part where the row names it).

#### P4.4 `.crplugin` install and script management
- Depends: P4.3 · Effort: medium · Unattended: yes · Status: todo
- Goal: install, list, enable and disable plugins (`ComicRack.Engine/PackageManager.cs`), avoiding the `CommitInstallPackage` `SafeDelete` bare-filename bug without editing it (call with full paths, or a patched copy with a manifest entry), plus `mac/upstream-proposals/P4.4-packagemanager-safedelete.md` (the upstream fix as a patch).
- Add: `mac/ComicRack.Mac.Host/Scripting/PluginPackages.cs`; `crcli script install <file.crplugin>`, `crcli script list`.
- Accept: tests with a generated `.crplugin` (zip of a sample script); `verify.sh`.

#### P4.5 Scripts menu, automation commands, script console
- Depends: P4.3, P5.3, P5.5 · Effort: high · Unattended: yes · Status: todo
- Goal: the "Automation" commands of the WinForms menus: script commands listed by hook and menu, run on the selected books on a worker with progress and cancel, output in a script console window (the `ScriptOutputForm` equivalent), errors shown. Also the hooks that need the UI (R9): `Editor` (commands in the Info dialog), `BookOpened` (on every book open in the reader) and `ReaderResized` (on reader size changes), with the arguments the WinForms app passes (read `ComicRack/MainForm.cs` and `ComicRack/ScriptUtility.cs`).
- Add: `mac/ComicRack.Mac/Scripting/{ScriptCommandsViewModel,ScriptConsoleWindow}.*`; menu items in `MainMenu.cs` and the grid context menu; e2e case `scripts-menu` (runs a sample command on two books, console shows output).
- Accept: tests (a test script's `BookOpened` and `ReaderResized` hooks are called with the book and the size); `mac/tools/e2e.sh scripts-menu`; `verify.sh`.
- Parity: MF.12, BC.15, SC.01 to SC.04, SC.07, SC.08, DL.27; view spec `ScriptOutputForm`; C-DLG-2.

#### P4.6a Script compatibility scanner `crcli script-scan`
- Depends: P4.3 · Effort: high · Unattended: yes · Status: todo
- Goal: §5.9.9 steps 2 to 3: parse `.py` with the IronPython parser (AST), resolve `System.Windows.Forms`/`System.Drawing` names, attribute chains, `clr.AddReference*`, `ctypes`/`DllImport`; read member references of bundled .NET plugin assemblies (`System.Reflection.Metadata`); output `type.member -> #scripts` and per-script Tier C findings.
- Add: `mac/ComicRack.Cli/Scripting/ScriptScanCommand.cs` (+ tests on the bundled samples).
- Accept: `crcli script-scan ComicRack/Output/Scripts --json out.json` lists `Form`, `Button`, `MessageBox`, `SaveFileDialog`... with counts; `verify.sh`.

#### P4.6b Corpus collection and usage ranking
- Depends: P4.6a · Effort: medium · Unattended: partial (the owner's plugin list -> C1, optional) · Status: todo
- Goal: `mac/scripting/compat-usage.md`, the ranked backlog for P4.7 to P4.13.
- Add: `mac/scripting/corpus.md` (sources: bundled samples, FrederikBaerentsen/ComicRack_Scripts, Comic Vine Scraper CE, Data Manager, CVIssueCount, maforget's AmazonScrapper, `mac/fixtures/owner/scripts/` if present; URL, licence, commit), `mac/scripting/fetch-corpus.sh` (clones into `mac/artifacts/script-corpus/`, never committed), the report.
- Accept: the report exists with Tier A/B/C counts; `verify.sh`.

#### P4.7 Facade surface generator
- Depends: P4.2 (GO), P4.6b · Effort: high · Unattended: yes · Status: todo
- Goal: generate the full public member surface (signatures, `virtual`/`protected`, events, nested types, enums) of every WinForms and System.Drawing type in Tier A and B plus their base types, as partial classes whose bodies throw `NotSupportedException("System.Windows.Forms.X.Y is not yet supported on macOS (ComicRack script compatibility layer)")`.
- Add: `mac/tools/facade-gen/` (MetadataLoadContext over the reference packs), output `mac/cYo.Compat.Forms/Generated/*.cs` and `mac/cYo.Compat.Drawing/Generated/*.cs`; hand-written files become `partial` where they overlap.
- Edit: drift tests in `mac/tests/Compat.Forms.Tests` and `mac/tests/Compat.Drawing.Tests` (generated members must match the reference exactly).
- Accept: generator is deterministic (`dotnet run --project mac/tools/facade-gen && git diff --exit-code mac/cYo.Compat.*/Generated`); census pass B 0 errors; `verify.sh`.

#### P4.8 Assembly identity mapping in the script host
- Depends: P4.2, P4.3, P4.7 · Effort: high · Unattended: yes · Status: todo
- Goal: `clr.AddReference('System.Windows.Forms')`, `clr.AddReferenceByPartialName`, `clr.AddReference('System.Drawing')` and the framework's `System.Drawing` forwarders land on the facades, as S6 decided (literal assembly names `System.Windows.Forms` and `System.Drawing.Common`, AssemblyVersion 10.0.0.0, shadow assemblies named `ComicRack.Engine`, `ComicRack.Plugins`, `cYo.Common`; details in `mac/reports/s6-modal.md` and "Facts from P4.2"), in the CLI and in the bundle.
- Edit: `mac/ComicRack.Mac.Host/Scripting/ScriptHost.cs`, the `AssemblyName`/`AssemblyVersion` of `mac/cYo.Compat.Forms/cYo.Compat.Forms.csproj`, `mac/cYo.Compat.Drawing/cYo.Compat.Drawing.csproj` and the three shadow projects, the tools and tests that name the assemblies (see "Facts from P4.2").
- Accept: test: a script imports `Form`, `Bitmap` and `Color` and reports their assembly names; same through `--script-selftest` in the bundle; the headless hooks of the bundled samples that P4.3 ran from rewritten copies now run from the unmodified files (`Sample.py`, `Autonumber.py` and the other bundled scripts load without a reference error); `verify.sh`.

#### P4.9a Facade object model and events
- Depends: P4.7 · Effort: xhigh · Unattended: yes · Status: todo
- Goal: `Control`/`Form`/`UserControl` state (Text, Bounds, Location, Size, ClientSize, Visible, Enabled, Font, colours, Tag, Name, Parent, Controls collection, TabIndex, Focus), event raising from facade state with WinForms ordering (Load, Shown, Activated, FormClosing, FormClosed, TextChanged, Click, SelectedIndexChanged, CheckedChanged also on programmatic change), `IFormsBackend` interface and a null backend.
- Add: `mac/cYo.Compat.Forms/Model/*`, tests `mac/tests/Compat.Forms.Tests/ModelTests.cs` (event traces as expected lists).
- Accept: tests; `verify.sh`.

#### P4.9b Layout: Anchor and Dock
- Depends: P4.9a · Effort: high · Unattended: yes · Status: todo
- Goal: port `DefaultLayout` (dotnet/winforms, MIT; licence header kept) for Anchor, Dock, Padding/Margin, `SuspendLayout`/`ResumeLayout`, resize behaviour.
- Accept: layout tests with expected rectangles taken from the dotnet/winforms algorithm cases; `verify.sh`.

#### P4.9c Layout: FlowLayoutPanel and TableLayoutPanel
- Depends: P4.9b · Effort: high · Unattended: yes · Status: todo
- Accept: tests (wrap, flow direction, column/row styles: absolute, percent, autosize, spans); `verify.sh`.

#### P4.9d AutoSize, AutoScale, font metrics
- Depends: P4.9b, P3.4 · Effort: high · Unattended: yes · Status: todo
- Goal: `AutoSize` with grow-to-fit for labels and buttons, `AutoScaleMode.Font`/`Dpi` against a 96 DPI baseline, font mapping and overflow logging (§5.9.3).
- Accept: tests; `verify.sh`.

#### P4.9e Threading: auto-marshalling, Invoke, DoEvents, Timer
- Depends: P4.9a · Effort: xhigh · Unattended: yes · Status: todo
- Goal: setters off the UI thread post through the backend dispatcher, getters read facade state, `Invoke`/`BeginInvoke`/`InvokeRequired`, `Application.DoEvents`, `System.Windows.Forms.Timer`; thread-safe facade state.
- Accept: stress tests (1,000 updates from 4 threads, no lost or torn values; deadlock-free `Invoke` from a worker while the UI waits in a nested frame); `verify.sh`.

#### P4.9f Modal semantics
- Depends: P4.9e · Effort: xhigh · Unattended: yes · Status: todo
- Goal: `ShowDialog` and every `MessageBox.Show` overload return `DialogResult` after the form closes; `AcceptButton`/`CancelButton`, Enter/Esc, button `DialogResult`, `FormClosing` cancel, nested modals, `ShowDialog` from a background thread blocks that thread; implemented against an `IFormsBackend.RunModal` abstraction (the null backend runs a test frame).
- Accept: conformance tests for nested modals, closing during a nested frame, background-thread `ShowDialog`; `verify.sh`.

#### P4.9g Conformance recorder
- Depends: P4.9a · Effort: high · Unattended: yes · Status: todo
- Goal: dump a control tree (type, name, bounds, text, visible, enabled, items) and an event trace as JSON at `Shown`; goldens for the bundled samples' dialogs; normalized bounds (±2 px).
- Add: `mac/cYo.Compat.Forms/Diagnostics/ConformanceRecorder.cs`, `mac/tests/Compat.Forms.Tests/Golden/*.json`.
- Accept: tests; `verify.sh`.

#### P4.10a FormsBackend skeleton
- Depends: P4.9f, P5.T4 · Effort: xhigh · Unattended: partial (real-window checks need an unlocked GUI) · Status: todo
- Goal: new project `mac/ComicRack.Mac.FormsBackend` (Avalonia; in the solution): peer registry, `Form` to `Window` (owner = main window, modal), Canvas-based containers at facade-computed rectangles, nested `DispatcherFrame` for `RunModal`, peers for `Label`, `Button`, `Panel`, `GroupBox`; wired into the app's `IScriptUi`. R9: peers use the P5.T4 control themes and `Cr.*` tokens (C-CTRL-1, C-DLG-2), so a script dialog looks like a ComicRack WinForms dialog; this holds for every later P4.10/P4.11 peer.
- Accept: headless tests (frames saved); e2e case `script-dialog-basic` (a test script shows a dialog with a label and OK; ui-drive clicks OK; the script receives `DialogResult.OK`); `verify.sh`.

#### P4.10b Input controls
- Depends: P4.10a · Effort: high · Unattended: yes · Status: todo
- Goal: peers for `TextBox` (single, multi-line, `PasswordChar`, `ReadOnly`), `CheckBox`, `RadioButton`, `ComboBox` (DropDown, DropDownList), `ListBox`, `CheckedListBox`, `NumericUpDown`, with two-way state through the facade.
- Accept: headless tests per control; e2e case `script-inputs` (`Autonumber.py` dialog filled and confirmed); `verify.sh`.

#### P4.10c Display controls
- Depends: P4.10a · Effort: high · Unattended: yes · Status: todo
- Goal: `PictureBox` (shim `Image` to `SKImage`, `SizeMode`), `ProgressBar`, `TrackBar`, `DateTimePicker`, `LinkLabel`, `ToolTip`, `Cursors.WaitCursor`.
- Accept: tests; e2e case `script-display`; `verify.sh`.

#### P4.10d Containers
- Depends: P4.10a, P4.9c · Effort: high · Unattended: yes · Status: todo
- Goal: `TabControl`/`TabPage`, `SplitContainer`, `FlowLayoutPanel`/`TableLayoutPanel` hosting, `UserControl`.
- Accept: tests; `verify.sh`.

#### P4.10e Owner-draw surface
- Depends: P4.10a, P3.4, P3.5a · Effort: high · Unattended: yes · Status: todo
- Goal: `Paint` events and `OnPaint` overrides of `Control`/`Panel`/`PictureBox` draw through `cYo.Compat.Drawing.Graphics` over the peer's `SKCanvas`; `Invalidate`/`Refresh`; `Sample.py`'s `UIControl(Control)` renders.
- Accept: headless frame test of `Sample.py` UI control (pixel checks); `verify.sh`.

#### P4.10f Dialogs
- Depends: P4.10a · Effort: high · Unattended: partial (native panels are checked by e2e when unlocked) · Status: todo
- Goal: `MessageBox` (alert window with mapped buttons and icons), `OpenFileDialog`/`SaveFileDialog`/`FolderBrowserDialog` through `StorageProvider` inside a nested frame (filters, initial directory, multi-select, result paths).
- Accept: tests; e2e case `script-filedialogs` (`choose-path`); `verify.sh`.

#### P4.10g Menus and strips
- Depends: P4.10a · Effort: high · Unattended: yes · Status: todo
- Goal: `ContextMenuStrip`, `ToolStripMenuItem`, `MenuStrip`, `StatusStrip`, `ToolStrip` peers.
- Accept: tests; `verify.sh`.

#### P4.11a Hook host: `ComicInfoUI` and `QuickOpenUI` panels
- Depends: P4.10a, P5.6, P5.29 · Effort: high · Unattended: yes · Status: todo
- Goal: `ScriptPanelHost` shows a script's control as a tab of the info panel and in Quick Open, calls `ShowInfo(books)` on selection change (contract of `ComicRack/ScriptUtility.cs` `CreatePagesUI`).
- Accept: tests with `Sample.py`; e2e case `script-panel`; `verify.sh`.
- Parity: SC.14, SC.16, IP.01; view spec `ComicPageContainerControl`.

#### P4.11b Hook host: `DrawThumbnailOverlay`
- Depends: P4.10e · Effort: high · Unattended: yes · Status: todo
- Goal: the grid calls overlay hooks per cell with a `Graphics` over the cell canvas (clip = cell); results cached with the thumbnail and invalidated on book change; budget below 1 ms per visible cell (measured, logged).
- Accept: tests (`FadeReadThumbnails`-style sample darkens read covers; cache hit avoids the Python call); `verify.sh`.
- Parity: SC.17 (overlay drawn over the P5.R2 cell exactly where the original draws it: after the cover, before the selection).

#### P4.11c WKWebView host control
- Depends: none · Effort: xhigh · Unattended: partial (real rendering needs an unlocked GUI) · Status: todo
- Goal: `mac/ComicRack.Mac/Web/WebViewHost.cs`: WKWebView in `NativeControlHost` through Objective-C runtime interop (DD14), `Navigate(url)`, `LoadHtml(html)`, script message handler.
- Accept: e2e case `webview` (loads a local HTML string, screenshot shows its colour); `verify.sh`.

#### P4.11d `window.external` bridge and HTML hooks
- Depends: P4.11c, P4.3 · Effort: high · Unattended: yes · Status: todo
- Goal: `ComicInfoHtml`/`QuickOpenHtml` (strings starting with `!` are URLs), the JavaScript proxy for `window.external` forwarding to the `ScriptProvider` object model (`ComicRack.Plugins/Controls/HtmlComicPageControl.cs` semantics), snapshot for synchronous property reads, the `WebBrowser` facade control (Navigate, DocumentText).
- Accept: test with `Sample.py`'s HTML panel (`window.external.ComicRack.OpenBooks.OpenFile` call reaches the host); `verify.sh`.
- Parity: SC.13, SC.15.

#### P4.11e Plugin environment extras and Scripts preferences
- Depends: P4.10a, P5.22a · Effort: high · Unattended: yes · Status: todo
- Goal: `IPluginEnvironment.MainWindow` (facade handle of the main window) and `Theme` (facade `ToolStripRenderer` with the original ComicRack colours from the `Cr.*` tokens, light or dark as the app), `ConfigScript` through the modal path, the Scripts preferences pane exactly as the original Scripts tab (groups Script Packages, Available Scripts, Script Settings with their buttons and options) plus a Mac group with the per-script compatibility report from P4.6a; install by the original Install... button and by drag and drop.
- Accept: tests; e2e case `scripts-prefs`; `verify.sh`.
- Parity: PR.14 to PR.16, SC.12, FF.04; view spec `PreferencesDialog` (Scripts tab); C-GRP-1.

#### P4.12a Corpus smoke run
- Depends: P4.6b, P4.10a · Effort: high · Unattended: yes · Status: todo
- Goal: run each corpus script's hooks headless with a fake library; fail on `NotSupportedException` from Tier A/B members; table in `mac/scripting/corpus-smoke.md`.
- Accept: `crcli script smoke mac/artifacts/script-corpus` produces the table; `verify.sh`.

#### P4.12b Dialog snapshots of the corpus
- Depends: P4.12a, P4.9g · Effort: high · Unattended: partial (screenshots need an unlocked GUI) · Status: todo
- Goal: control-tree JSON and ui-drive screenshots of every corpus dialog reachable without network; goldens.
- Accept: report and goldens; `verify.sh`.

#### P4.13a Tier B: `ListView` and `ImageList`
- Depends: P4.10d · Effort: high · Unattended: yes · Status: todo
- Goal: Details, List, LargeIcon views, columns, sorting, check boxes, `ImageList`.
- Accept: tests; `verify.sh`.

#### P4.13b Tier B: `TreeView` and `DataGridView` (basic)
- Depends: P4.10d · Effort: high · Unattended: yes · Status: todo
- Accept: tests; `verify.sh`.

#### P4.13c Tier B long tail by rank
- Depends: P4.13a · Effort: high · Unattended: yes · Status: todo
- Goal: the next members by `compat-usage.md` rank (`RichTextBox` plain and simple formatting, `ColorDialog`, `FontDialog`, scroll bars, `MaskedTextBox`, read-only `PropertyGrid`) as far as one session allows; the rest goes to §9 with ranks.
- Accept: tests; corpus smoke has fewer failures (numbers in §3); `verify.sh`.

#### P4.14 `.compat` overlays and the install-time compatibility report
- Depends: P4.12a, P4.4 · Effort: medium · Unattended: yes · Status: todo
- Goal: `Scripts/.compat/<plugin>.py` loaded after the original module, versioned by the plugin's hash (DD13); the scanner's Tier C report shown before install and in Scripts preferences.
- Accept: tests; `verify.sh`.

#### P4.15 Plugin acceptance pack
- Depends: P4.12b, P4.13c · Effort: medium · Unattended: partial (the owner's run -> C4) · Status: todo
- Goal: `mac/plans/plugin-acceptance.md`: top plugins by rank and from C1, per plugin the steps, the expected result, a Value and a Verdict column; ui-drive runs of each plugin's dialogs attached as screenshots; writes the C4 block.
- Accept: the pack exists; `verify.sh`.

#### P4.16 (optional) Windows recorder for conformance
- Depends: P4.9g, P3.9 · Effort: high · Unattended: partial (needs the owner to push) · Status: todo
- Goal: a net48 recorder under `mac/tools/winforms-recorder/` built only by a `windows-latest` job in `.github/workflows/mac-conformance.yml`, recording control trees and event traces with real WinForms for the corpus dialogs, compared with the facade goldens.
- Accept: YAML parses; recorder builds on the runner (after a push); `verify.sh`.

### P5 Screens (main window parity)

Every P5 task: view-model tests on the headless platform (frames saved when `CRMAC_FRAMES_DIR` is set), at least one e2e case under `mac/tests/e2e/cases/`, and the parity and chrome policy of §2.11: a view spec in `mac/specs/views/` for every original view it touches (read from the named files, never edited), labels through the original TR keys (P2.6), colours, fonts, metrics and icons from the P5.T1, P5.T2 and P5.T4 foundation, the ledger rows of its `Parity:` line updated in the same commit, shots in light and dark and a contact sheet of the changed views (path in the report). "Exactly as the original" in a Goal means: same items, order, labels, defaults, enable rules and shortcuts as the ledger says.

#### P5.0 Owner MVP notes intake (M2.5)
- Depends: none · Effort: low · Unattended: yes · Status: skipped (no notes) 2026-10-20
- Goal: if `mac/plans/mvp-acceptance.md` has Verdicts or `OWNER-ACTIONS.md` has MVP notes (C1 d), turn each "Issue"/"Fail" into a task `P5.0.<n>` in §7 and §6 (front of wave B); else mark `skipped (no notes)`. A note that asks for a non-original look or behaviour is checked against R9 first: if it conflicts, it becomes a question in the C2 block instead of a task.
- Accept: §7 updated or the skip recorded; `verify.sh`.

#### P5.T1 Theme tokens and appearance (chrome foundation 1)
- Depends: none · Effort: high · Unattended: yes · Status: done 2026-10-20
- Goal: `mac/plans/chrome-guidelines.md` §2 as Avalonia resources: every `Cr.*` token in a light and a dark dictionary, the light toolstrip colours computed from the Windows 10 system colours, the selection style of §2.4 as a helper, appearance switching per C-APP-1 (follow macOS; `--theme light|dark` overrides, used by contact sheets and e2e).
- Add: `mac/ComicRack.Mac/Themes/ComicRackTheme.Light.axaml`, `ComicRackTheme.Dark.axaml`, `ThemeTokens.cs` (typed access; `StyledSelection` frame and fill alpha per state), `ProfessionalColors.cs` (port of the system-colour formulas of dotnet/winforms `ProfessionalColorTable` `InitSystemColors`/`InitCommonColors`, MIT header kept, plus the `OptimizedProfessionalColorTable` changes); `mac/tests/ComicRack.Mac.Tests/Theme/ThemeTokenTests.cs` (reads the original files named in chrome-guidelines §2 with regexes, compares every token; light system values from the comments of `KnownColorTableEx.cs`; the source root can be redirected by an environment variable for the mutation check).
- Edit: `mac/ComicRack.Mac/App.axaml(.cs)` (theme dictionaries per `ThemeVariant`; Fluent stays the base control theme until P5.T4), `mac/ComicRack.Mac/AppOptions.cs` (`--theme`), `mac/plans/chrome-guidelines.md` §2.3 (write the computed light toolstrip values into the table).
- Out of scope: control templates (P5.T4), existing views (P5.R*).
- Accept: `dotnet test mac/tests/ComicRack.Mac.Tests --filter FullyQualifiedName~ThemeToken`; mutation: a scratch copy of `ThemeColorTable.cs` with one changed colour, pointed to by the variable, fails the test (tried, restored); `dotnet run --project mac/ComicRack.Mac -- --smoke-test=3 --headless --theme dark --data-dir "$(mktemp -d)"` exits 0; `verify.sh`.
- Parity: C-APP-1; chrome-guidelines §2.

#### P5.T2 Original icons and images (chrome foundation 2)
- Depends: none · Effort: medium · Unattended: yes · Status: done 2026-10-20
- Goal: chrome-guidelines §5: every original image available by its resource name, without copying or editing an original file; dark variants; magenta transparency; Retina scaling as the original at 200 %.
- Edit: `mac/ComicRack.Mac/ComicRack.Mac.csproj` (`AvaloniaResource` items with `Link` for `ComicRack/Resources/**`, `cYo.Common.Windows/Resources/**`, `ComicRack.Engine/Resources/**`, `ComicRack.Engine.Display.Forms/Resources/**`, `ComicRack/Icons/*.ico`).
- Add: `mac/tools/icon-map/icon-map.py` (python3 standard library: reads the `Properties/Resources.resx` files of the four projects, writes `mac/ComicRack.Mac/Assets/icon-map.json`: resource name → linked path, dark variant if any), the committed `icon-map.json`, `mac/ComicRack.Mac/Themes/Icons.cs` (`Icons.Get(name)` → cached `Bitmap`, dark lookup by the current appearance, magenta key for GIF/BMP sources), tests `mac/tests/ComicRack.Mac.Tests/Theme/IconTests.cs` (the map is not stale: regenerating it in a temp folder gives the same JSON; every `global::...Properties.Resources.<Name>` used in `ComicRack/**/*.Designer.cs` resolves; a magenta pixel becomes transparent).
- Accept: `dotnet test mac/tests/ComicRack.Mac.Tests --filter FullyQualifiedName~IconTests` (number of resolved names recorded in §3); `git status --porcelain ComicRack cYo.Common.Windows ComicRack.Engine ComicRack.Engine.Display.Forms` empty; `verify.sh`.
- Parity: C-ICON-1.

#### P5.T3 Parity tooling: view specs, ledger check, contact sheets (chrome foundation 3)
- Depends: P0.6a · Effort: high · Unattended: partial (the contact sheet needs an unlocked GUI; if locked: `done-with-caveat`) · Status: done-with-caveat 2026-10-20 (the real-window half of `chrome-sheet.sh` (launch, shot, quit per view and theme) was not run: the screen was locked, ui-drive exit 4; the sheet was written with reference cards only)
- Goal: the tools of §5.5 that every later UI task and the audits use.
- Add: `mac/tools/designer-dump/designer-dump.py` (chrome-guidelines §8 skeleton from a `*.Designer.cs`); `mac/tools/ledger-check/ledger-check.py` (rules: unique IDs, valid statuses, `deviation` rows have a reason starting `impossible:` or `native:`, every Mac task ID exists in §7; coverage: every `ToolStripMenuItem`, `ToolStripButton`, `ToolStripSplitButton` and `ToolStripDropDownButton` name in `ComicRack/**/*.Designer.cs` appears in some row (rows may list several names or `prefix*` patterns); `--report` (counts per area and status); `--menus <file>` compares a `ui-drive menu-items` dump with the ledger's labels and Mac shortcuts); a call of `ledger-check.py` in `mac/tools/verify.sh` (fast, no build); hidden app options `--show <ViewName>` (opens a named window, dialog or view after launch; `ChromeGallery` and `MainWindow` at least, every later task registers its views) and `--fixture-library <dir>` (temp library from a `crcli fixtures make` folder) in `mac/ComicRack.Mac/AppOptions.cs`; `crcli image sheet --out <png> --cols N <image-or-card.json>...` (Skia composition with captions, in `mac/ComicRack.Cli/Core/ImageCommands.cs`, tests); `mac/tools/chrome-sheet.sh <label> [--all|<View>...]` (per view: launch with `--theme light` and `--theme dark` plus `--show`, shot, reference per chrome-guidelines §1, a reference card from `mac/specs/views/<View>.md` (layout outline, token swatches, icons), output `mac/artifacts/chrome/<label>/sheet.html` and `sheet.png`); the README hero image downloaded once to `mac/artifacts/chrome/reference/` (record the URL in §3).
- Edit: `mac/plans/parity-ledger.md` (the first `ledger-check` run lists original item names without a row: add them to the matching rows' "Original location"; a genuinely new feature gets a new row), `mac/plans/OWNER-ACTIONS.md` (one optional block, C1 (e), "Reference screenshots of the Windows app": the list of views (main window with browser and reader, Info dialog each tab, Preferences each tab, Edit Smart List both dialogs, Export Books, Book Display Settings, List Options, a context menu of the browser, the reader with the navigation overlay), light theme, plus `-dark` if convenient, saved as `mac/fixtures/owner/reference/<View>[-dark].png`).
- Accept: `python3 mac/tools/ledger-check/ledger-check.py` exits 0; removing a row in a scratch copy (`LEDGER=<copy>`) makes it exit 1 naming the item; `python3 mac/tools/designer-dump/designer-dump.py ComicRack/Dialogs/PreferencesDialog.Designer.cs | grep -c "Reader\|Libraries\|Behavior\|Scripts\|Advanced"` is at least 5; `dotnet test mac/tests/ComicRack.Cli.Tests --filter FullyQualifiedName~ImageSheet`; `mac/tools/chrome-sheet.sh T3 MainWindow` writes both files (exit 4 = locked: SKIPPED); `verify.sh`.
- Parity: chrome-guidelines §8 and §9; the whole ledger (format and coverage).

#### P5.T4 Base control styles and native pieces (chrome foundation 4)
- Depends: P5.T1, P5.T2, P5.T3 · Effort: high · Unattended: partial (real-window shots need an unlocked GUI) · Status: done 2026-10-20
- Goal: chrome-guidelines §7 as reusable styles and controls, shown together in a gallery: toolbar (C-TB-1: 25 px, flat 16 px icon buttons with the `Cr.Tool*` hover and pressed fills, split button with a 14 px arrow part and a drop-down menu, separators, text-and-icon buttons like "100 %"), tab bar (C-TAB-1: tab height 24, bar 31, left indent 8, min 60 / max 250 tab width, selected `TabBarSelectedBack`, close box, a toolbar slot at the right), status bar (C-SB-1: 24 px, stretching text segment, 20 × 19 sunken icon slots, slider slot), collapsible group box (C-GRP-1), sizable container with the 6 px MozillaGrip and collapse animation (C-SPLIT-1), column header (C-HDR-1), tree item template (C-TREE-1 base: row height font + 8, bold selected label, arrow glyph images, `StyledSelection`), control themes for buttons, check boxes, radio buttons, combo boxes, numeric up/down, sliders, list boxes, text box frames and tab controls from §2.1/§2.5 tokens (C-CTRL-1), popup-menu style (C-MENU-3), overlay scroll bars (C-SCROLL-1), native focus ring (C-FOCUS-1), font resources (C-FONT-1), `CrDialogWindow` (C-DLG-2: modal, centred on the owner, OK / Cancel / Apply row as the original, Return and Esc), `MacAlert` (C-DLG-1: `NSAlert` through the Objective-C runtime, suppression check box for the "do not show again" questions; fallback: the MVP `MessageWindow` restyled), automatic window tabbing off (C-WIN-3).
- Add: `mac/ComicRack.Mac/Themes/Controls/*.axaml`, `mac/ComicRack.Mac/Controls/{CrToolBar,CrSplitButton,CrTabBar,CrStatusBar,CrCollapsibleGroup,CrSizableContainer,CrHeader,CrDialogWindow}.cs`, `mac/ComicRack.Mac/Platform/MacAlert.cs`, `mac/ComicRack.Mac/Views/ChromeGalleryWindow.axaml(.cs)` (`--show ChromeGallery`: every control and state, light and dark), tests `mac/tests/ComicRack.Mac.Tests/Theme/ChromeMetricsTests.cs` (headless frames: the metrics above in DIPs, token pixels of toolbar hover, selection frame alpha 164 in Highlight, group header gradient ends) and `NoLiteralColorsTests.cs` (lint: no `#RRGGBB`, `Colors.` or `Brushes.` literal in `mac/ComicRack.Mac/Views/**` and `.axaml` outside `Themes/`; existing MVP files are listed as known exceptions until P5.R1 to P5.R3 remove them).
- Edit: `mac/ComicRack.Mac/App.axaml` (Cr control themes override Fluent where defined).
- Accept: `dotnet test mac/tests/ComicRack.Mac.Tests --filter "FullyQualifiedName~ChromeMetrics|FullyQualifiedName~NoLiteralColors"`; e2e case `chrome-gallery` (shots light and dark, probes of two tokens); `mac/tools/chrome-sheet.sh T4 ChromeGallery`; `verify.sh`.
- Parity: chrome-guidelines §7 rows C-TB-1, C-TAB-1, C-SB-1, C-GRP-1, C-SPLIT-1, C-HDR-1, C-TREE-1, C-CTRL-1, C-MENU-3, C-SCROLL-1, C-FOCUS-1, C-FONT-1, C-DLG-1, C-DLG-2, C-WIN-3.

#### P5.T5 (v1-optional) Windows reference screenshots in CI
- Depends: P3.9, P5.T3 · Effort: high · Unattended: partial (needs the owner to push and start the workflow -> C2 d) · Status: todo
- Goal: real reference images without a Windows machine in the sessions: `.github/workflows/mac-reference-shots.yml` (`windows-latest`, manual trigger only): builds the WinForms solution (net48), runs `mac/tools/winforms-shots/` (a net48 console built only in that job, referencing the built `ComicRack.exe`; it creates a temp data folder with a generated library, constructs the forms of the P5.T3 owner list, selects each tab, and saves `Control.DrawToBitmap` PNGs in light and `-dark`), uploads them as an artifact named for the commit.
- Out of scope: pixel comparison (the contact sheet shows them side by side).
- Accept: `ruby -ryaml -e 'YAML.load_file(".github/workflows/mac-reference-shots.yml")'`; the harness compiles on this Mac against `Microsoft.NETFramework.ReferenceAssemblies` and the WindowsDesktop reference pack if that works, else the attempt is recorded in §3 and the compile is left to the CI job; `verify.sh`. Owner check (C2 d): one run and the downloaded images.
- Parity: chrome-guidelines §1 reference source 2.

#### P5.R1 Restyle the MVP main window shell and menu bar
- Depends: P5.T4, P0.6b, P2.6 · Effort: high · Unattended: yes · Status: done 2026-10-20
- Goal: the MVP main window looks and is organised as the original (`ComicRack/MainForm.Designer.cs`, `ComicRack/Views/ComicExplorerView.Designer.cs`, `ComicRack/Views/ComicBrowserControl.Designer.cs`): menu bar App, File, Edit, Browse, Read, Display, Window, Help with the original labels (TR `MainForm`), order, separators and the ledger shortcuts for every item the MVP can already do (other items appear when their tasks land; nothing invented); browser toolbar (C-TB-1) in the original position with the MVP functions mapped (Sidebar toggle, quick search box 130 × 24, the MVP sort as the Arrange split button); the MVP size slider moves to the status bar (SB.05); status bar segments (SB.01, SB.04); the sidebar becomes the side panel (252 wide, grip) with the library tree look (C-TREE-1, list icons by the Core's list types, counts); About, Settings… and Quit in the app menu (C-MENU-1); standard panels and alerts native (DL.34, `MacAlert`); the MVP literal colours are removed.
- Edit: `mac/ComicRack.Mac/Views/MainWindow.axaml(.cs)`, `MainMenu.cs`, `mac/ComicRack.Mac/ViewModels/{MainViewModel,SidebarViewModel,BrowserViewModel}.cs`, `mac/ComicRack.Mac/Views/MessageWindow.axaml(.cs)`, e2e cases `menu-structure` and `open-folder` (new menu titles, paths and shortcuts), `mac/plans/mvp-acceptance.md` is not changed (MVP record).
- Add: view specs `mac/specs/views/{MainForm,ComicExplorerView,ComicBrowserControl,ComicListLibraryBrowser}.md` (first versions).
- Accept: tests; `mac/tools/e2e.sh menu-structure open-folder`; `python3 mac/tools/ledger-check/ledger-check.py --menus <menu-items dump>` reports no mismatch for the rows set `done`; `mac/tools/chrome-sheet.sh R1 MainWindow`; `verify.sh`.
- Parity: MB.05, SB.01, SB.04, SB.05, LT.01 (look), MA.01, MA.03, DL.34, the MF/ME/MB/MR/MD/MH rows of commands the MVP already has; C-MENU-1, C-MENU-2, C-TB-1, C-SB-1, C-TREE-1, C-SPLIT-1, C-DLG-1.

#### P5.R2 Restyle the browser items (thumbnails)
- Depends: P5.T4, P5.R1 · Effort: high · Unattended: yes · Status: done 2026-10-20
- Goal: grid cells drawn as `ThumbRenderer`, `ThumbIconRenderer`, `RatingRenderer` and `CoverViewItem` draw them (chrome-guidelines §4): shadow, 1 px border, 2:3 placeholder with "Loading Thumbnail...", caption lines with the size formula, selection per §2.4, dog-ear on hover and selection (`DogEarThumbnails`), state icons (open, last, new pages, fileless, deleted), user and community stars and numeric ratings (`NumericRatingThumbnails`), page-count badge, bookmark ribbons, fade-in (`FadeInThumbnails`), tooltips (`ShowToolTips`), same-size covers (`CoverThumbnailsSameSize`), item size by Cmd+scroll, pinch and the status-bar slider (BV.04). Pixel rules of §2.8 hold (refcounted `CoverImage`/`PixelBlock`, no Core-owned bitmaps); the M2.2b scroll budgets hold.
- Edit: `mac/ComicRack.Mac/Views/{BookCell,BookGridView}.cs`, `mac/ComicRack.Mac/Browser/CoverDrawOperation.cs`, and (RV5, rule R10) the look of the cells in the chrome layer: replace the `Cr.Mvp.Grid*`/`Cr.Mvp.Cover*` entries and the `cr-book-*` classes of `mac/ComicRack.Mac/Themes/Controls/CrMvpLook.axaml` with tokens and a real cell theme; views keep only classes and Cr controls (`mac/plans/chrome-architecture.md`).
- Add: view spec `mac/specs/views/CoverViewItem.md` (thumbnail mode).
- Accept: headless frame tests (shadow and border pixels; selection frame; dog-ear only on the selected cell when the option is on; star alpha 1.0 and 0.25); the existing M2.2b virtualization and timing tests unchanged; e2e case `grid-look` (shots light and dark, probes); `mac/tools/chrome-sheet.sh R2 BookGrid`; `verify.sh`.
- Parity: BV.01, BV.04, BV.08, PB.21, PB.23 to PB.25, PB.27; C-LIST-1.

#### P5.R3 Restyle the reader chrome
- Depends: P5.T4 · Effort: high · Unattended: yes · Status: done 2026-10-21
- Goal: the reader as the original (`ComicRack.Engine.Display.Forms/`, `MainForm.Designer.cs` reader part): the book tab strip with the reader toolbar at its right (TB.01 to TB.08 for what the MVP does; the Tools drop-down TB.09 with the items that exist), until P5.32a docks the reader the reader window shows that strip with one tab; default background WhiteSmoke, page shadow, overlays (page info top right with fade, message and loading centre, navigation overlay with the glass images and its auto-show option, visible-part overlay) with the §4 metrics; Read and Display menus and shortcuts per the ledger (MR.01 to MR.04, MD.03, MD.04, MD.06, MD.08 to MD.10, MD.12, MD.25; reader keys RK.05, RK.06, RK.09 to RK.11, RK.14, RK.16, RK.17, RK.21, RK.23, RK.26, RK.30 realigned to the original); page context menu (PC.01, PC.02, PC.06); `HideCursorFullScreen`. The MVP performance overlay moves to Ctrl+Option+Cmd+P (Option+Cmd+P is Previous Book).
- Edit: `mac/ComicRack.Mac/Views/{ReaderWindow.axaml(.cs),ReaderView.cs,PerfOverlay.cs}`, `mac/ComicRack.Mac/Reader/ReaderPainter.cs`, `ReaderMenu.cs`, `MainMenu.cs`, e2e cases `reader-keys`, `overlay`, `fullscreen`; (RV5, rule R10) the looks go to the chrome layer: replace the `Cr.Mvp.Reader*`/`Cr.Mvp.Overlay*` entries and the `cr-reader*`/`cr-overlay*` classes of `mac/ComicRack.Mac/Themes/Controls/CrMvpLook.axaml` with tokens and Cr controls (`CrTabBar`, `CrToolBar` ...) so that the views keep only classes and Cr controls.
- Add: view specs `mac/specs/views/{ComicDisplayControl,NavigationOverlay}.md`.
- Accept: tests; `mac/tools/e2e.sh reader-keys overlay fullscreen`; `mac/tools/chrome-sheet.sh R3 Reader`; `verify.sh`.
- Parity: the rows listed in the Goal, PB.10; C-READ-1, C-TB-1, C-TAB-1, C-WIN-2.

#### P5.1 Settings persistence and window state
- Depends: none · Effort: high · Unattended: yes · Status: done-with-caveat 2026-10-21 (e2e `state-restore`: every check passed in some run, but no single run of the final tree finished, because Screen Sharing took the focus away from the app at a quit or launch in the last runs; the last change, replacing a damaged file at start, is covered by unit tests and a headless run)
- Goal: `MacSettings` and app preferences saved as XML (`<data>/Settings.xml`, XmlSerializer, atomic write) on change (debounced) and at quit: window frames (library, readers), sidebar width and visibility, grid size and sort, reader fit, layout, right to left default, last folder; restore on launch with off-screen correction. R9: the preference properties that mirror `ComicRack/Config/Settings.cs` keep the original names and **defaults** (every PB row and the reader, library and cache settings that the Mac app uses).
- Add: `mac/ComicRack.Mac.Host/Settings/{AppPreferences,SettingsStore}.cs`; tests, including `SettingsDefaultsTests` (parses `ComicRack/Config/Settings.cs` `DefaultValue` attributes and initializers for the mirrored properties and compares).
- Edit: `mac/ComicRack.Mac.Host/MacSettings.cs`, `mac/ComicRack.Mac/Services/HostService.cs`, view models and windows that own state.
- Accept: tests (round trip, corrupt file falls back to defaults and keeps a `.bad` copy, defaults equal the original); e2e case `state-restore` (move and resize, quit, relaunch, same frame); `verify.sh`.
- Parity: PB.01 to PB.40 (defaults only; each row stays `todo` until its owning task makes the option work), FF.02.

#### P5.2 Live refresh: read state, counts, scan progress in the grid
- Depends: P5.1, P5.R2 · Effort: high · Unattended: yes · Status: done 2026-10-21
- Goal: covers show read progress as the original thumbnail does (state icons and markers of P5.R2), sidebar counts and live counters refresh after scan, reading and edits (debounced, off the UI thread) with the original gauge look (new, unread, total pills, numbers option), the grid shows new books every few seconds during a scan keeping selection and scroll.
- Edit: `mac/ComicRack.Mac/Browser/*`, `mac/ComicRack.Mac/ViewModels/{BrowserViewModel,SidebarViewModel,MainViewModel}.cs`.
- Accept: tests; e2e case `refresh-after-read` (read 3 pages, close, the cover shows the reading marker, "Reading" count +1); `verify.sh`.
- Parity: LT.01 (gauges), PR.10 (behaviour; the pane is P5.22b).

#### P5.3 Multi-selection
- Depends: none · Effort: high · Unattended: yes · Status: done 2026-10-21
- Goal: Cmd-click, Shift-click, Shift+arrows, Cmd+A (Select All), Invert Selection, selection kept across sort and search; `SelectedBooks` for commands; keyboard as the original `ItemView` (arrows, Return opens, Delete removes with the alias Cmd+Backspace).
- Add: `mac/ComicRack.Mac/Browser/SelectionModel.cs`; tests.
- Accept: tests; e2e case `grid-multiselect`; `verify.sh`.
- Parity: BV.09, BV.10, BC.19.

#### P5.4 Browser toolbar and main window layout
- Depends: P5.1, P5.R1 · Effort: medium · Unattended: yes · Status: todo
- Goal: the browser toolbar complete in the original order (Sidebar, Previous, Next | Views, Group, Stack, Arrange, quick search, List Layouts | Duplicate | Undo, Redo; items whose feature is not built yet are present and disabled, then enabled by their task), Cmd+F focuses the quick search, the side panel and info panel splitters (`SizableContainer`) persisted, the status bar complete; no unified macOS toolbar (C-TB-1).
- Edit: `mac/ComicRack.Mac/Views/MainWindow.axaml(.cs)`, `MainMenu.cs`.
- Accept: tests; e2e case `chrome` (Cmd+F focuses search; splitter drag persisted); `mac/tools/chrome-sheet.sh P5.4 MainWindow`; `verify.sh`.
- Parity: BT.01 (and the toolbar slots of BT.02 to BT.12), GK.01; view spec `ComicBrowserControl`; C-TB-1, C-SPLIT-1.

#### P5.5 Grid context menu, part 1
- Depends: P5.3, P2.1 · Effort: high · Unattended: yes · Status: todo
- Goal: the browser context menu exactly as the original `contextMenuItems` (all items in order; this task implements Open, Info..., My Rating (stars, Quick Rating and Review dialog `ComicRack/Dialogs/QuickRatingDialog.cs`), Mark as (Unread, Read, Checked, Unchecked), Reveal in Finder, Remove... (`DeleteItemDialog` semantics: from list, library, optionally to the Trash), Select All and Invert Selection; the other items are present and disabled until P5.13b, P5.16c, P5.20, P5.21, P5.24a, P4.5 enable them), also in the Edit menu where the original has them (My Rating); `AutoShowQuickReview`; "do not show again" questions through `MacAlert`.
- Accept: tests; e2e case `context-menu-1` (right click via ui-drive, mark read, count changes); `verify.sh`.
- Parity: BC.01, BC.04, BC.07, BC.08, BC.17, BC.23, ME.04, DL.06, DL.18, DL.30, PB.18; view spec `ComicBrowserControl` (context menu), `QuickRatingDialog`, `DeleteItemDialog`; C-MENU-3, C-DLG-1, C-DLG-2.

#### P5.6 Info panel and small preview
- Depends: P5.4 · Effort: medium · Unattended: yes · Status: todo
- Goal: the Info Panel (Shift+F9; `ComicExplorerView.pluginContainer`, `ComicPageContainerControl` book info page with cover, fields, search links (`ShowSearchLinks`), read state; multiple selection summary) and the Small Preview (Shift+F7; `SmallComicPreview`: page viewer with its toolbar Open, First, Previous, Next, Last, Two Pages, Refresh, Close, "Nothing Selected"), both in the original positions and sizes (162 high at the bottom; 207 high under the tree).
- Accept: tests; e2e case `info-panel`; `mac/tools/chrome-sheet.sh P5.6 MainWindow`; `verify.sh`.
- Parity: MB.06, MB.08, IP.01, SP.01, PB.22; view specs `SmallComicPreview`, `ComicPageContainerControl`.

#### P5.7a Info editor: shell, Summary and Details
- Depends: P5.6 · Effort: high · Unattended: yes · Status: todo
- Goal: the `ComicBookDialog` (`ComicRack/Dialogs/ComicBookDialog.cs`) exactly: window size and tabs (Summary, Details, Plot & Notes, Catalog, Custom, Pages, Colors; the tab set of the original even where later tasks fill them), Previous/Next book, OK/Cancel/Apply, Summary and Details fields with their labels (TR `ComicBookDialog`), the Core's value lists (autocomplete from library values), the 3D cover (`InformationCover3D`), the thumbnail menu (Reset); Undo/Redo through the Core's `Database.Undo` (`ComicBookContainerUndo`) where the WinForms app uses it, also in the Edit menu and the browser toolbar.
- Accept: tests (edit, apply, cancel restores, previous/next keeps edits, undo/redo); e2e case `info-edit` (edit Series, OK, grid shows it); `mac/tools/chrome-sheet.sh P5.7a ComicBookDialog`; `verify.sh`.
- Parity: ME.01 to ME.03, BT.12, DL.01, IN.01, IN.02, IN.08, PB.38; view spec `ComicBookDialog`; C-DLG-2, C-CTRL-1.

#### P5.7b Info editor: Plot and Notes, Review, Catalog, Custom
- Depends: P5.7a · Effort: high · Unattended: yes · Status: todo
- Goal: the remaining text tabs exactly as the original, with `CatalogOnlyForFileless` and `ShowCustomBookFields`.
- Accept: tests; `verify.sh`.
- Parity: IN.03 to IN.05, PB.32, PB.33.

#### P5.7c Info editor: Pages, Colors and cover
- Depends: P5.7a · Effort: high · Unattended: yes · Status: todo
- Goal: Pages tab (page list with thumbnails, page type, rotation, bookmark, delete flag, front-cover choice, "Order by Name" / "Order by Name (numeric)" menu, custom thumbnail set/remove; thumbnail invalidation `ThumbnailService.Invalidate(book)`, new) and Colors tab (per-book colour adjustment, "Double Click on Color to set White Point").
- Accept: tests; `verify.sh`.
- Parity: IN.06, IN.07.

#### P5.8 Multi-edit
- Depends: P5.7a, P5.3 · Effort: high · Unattended: yes · Status: todo
- Goal: `MultipleComicBooksDialog` exactly (layout, mixed values shown as the original shows them, a change applies only to touched fields, all selected books updated in one undoable step).
- Accept: tests; e2e case `multi-edit`; `verify.sh`.
- Parity: DL.02; view spec `MultipleComicBooksDialog`.

#### P5.9 ComicInfo write-back UI
- Depends: P2.4b, P5.7a · Effort: high · Unattended: yes · Status: todo
- Goal: "Update Book Files" (context menu) and "Update all Book Files" (File menu, Cmd+Shift+U) through the `QueueManager` file update queue, the automatic update setting (`IComicUpdateSettings`), queue progress with the status-bar activity icon; failures listed in the Errors dialog (P5.26).
- Accept: tests: edit a CBZ's fields, update, `crcli info show` reads the new values; e2e case `write-back`; `verify.sh`.
- Parity: MF.07, BC.16.

#### P5.10a Opening files from Finder, Open File..., Recent Books
- Depends: P5.1 · Effort: high · Unattended: yes · Status: todo
- Goal: documents opened from Finder or `open -a` (Avalonia activation `UrlsOpened`/files) open in the reader and are added to the library per `AddToLibraryOnOpen`; File > Open File... (Cmd+O); the MVP's Open Folder becomes "Add Folder to Library..." (Cmd+Shift+A, `miAddFolderToLibrary`); File > Recent Books (persisted; label as the original). Update the e2e cases `menu-structure` and `open-folder` for the new shortcuts.
- Edit: `mac/ComicRack.Mac/App.axaml.cs`, `MainMenu.cs`, `mac/tools/make-app.sh` (`CFBundleDocumentTypes` for cbz, cbr, cb7, cbt, pdf, crplugin, cbl as viewer).
- Accept: tests; e2e case `open-from-finder` (`open -a <bundle> <file.cbz>` opens a reader); `verify.sh`.
- Parity: MF.01, MF.05, MF.16, PB.08.

#### P5.10b Dock menu, Window menu, keep running without windows, quit and restart flow
- Depends: P5.10a · Effort: high · Unattended: yes · Status: todo
- Goal: DD7 (amended: Window > Library is Option+Cmd+0); Dock menu with recent books; Window menu (C-MENU-2); File > Restart (Ctrl+Cmd+Shift+Q, relaunches the app); quit with a progress sheet when the save takes over 0.5 s; a failed save at quit asks "Quit anyway?" (M2.1 items).
- Accept: tests; e2e case `close-reopen` (close the library window, app keeps running, Option+Cmd+0 reopens); `verify.sh`.
- Parity: MF.17, MF.18, MA.02; C-MENU-2, C-DOCK-1.

#### P5.32a Main window hosts the reader: book tabs, Reader in own Window
- Depends: P5.R1, P5.R3, P5.1 · Effort: xhigh · Unattended: yes · Status: todo
- Goal: the main window as `MainForm` (MV.01): the reader area with the book tab bar (one tab per open book, empty slot from New Tab, tab context menu: Close, Close All But This, Close All to the Right, Show in Browser, Reveal in Finder, Copy Full Path), the browser panel docked at the bottom (250 high, grip, collapsible; all docking modes in P5.32b), a Quick Open area when no book is open (filled by P5.29), Reader in own Window (F12 and the reader key D toggle; the MVP reader window becomes this mode, `ReaderForm`), File > Close / Close All / New Tab / Open Books, Read > Show in Browser / Previous Tab / Next Tab / Track current Page, Open in new Tab from the browser; open books restored at launch (`OpenLastFile`), `OpenInNewTab`.
- Edit: `mac/ComicRack.Mac/Views/{MainWindow,ReaderWindow}.axaml(.cs)`, `mac/ComicRack.Mac/Services/ReaderManager.cs`, view models; e2e cases `grid-keys` and `reader-keys` (Return opens a tab in the main window).
- Add: e2e case `book-tabs` (open 3 books, Cmd+Shift+K and Cmd+Shift+J cycle, Close All But This, F12 undocks and docks back).
- Accept: tests; `mac/tools/e2e.sh book-tabs grid-keys reader-keys`; `mac/tools/chrome-sheet.sh P5.32a MainWindow Reader`; `verify.sh`.
- Parity: MV.01, MF.02 to MF.04, MF.15, MR.08 to MR.10, MR.13, MD.26, TC.01 to TC.07, BC.02, RK.08, RK.15, DL.28, PB.02, PB.09; view spec `MainForm`; C-TAB-1.

#### P5.11a Details view: virtualized table
- Depends: P5.3, P5.T4 · Effort: high · Unattended: yes · Status: todo
- Goal: a table of books (DD15: check and record Avalonia `TreeDataGrid`'s licence first; default a custom `ItemsRepeater` table) that looks like the original details view (column headers C-HDR-1 with sort arrow, row height 16 + padding, zebra rows `DetailRowHighlight` at alpha 96, selection §2.4): resizable and reorderable columns, sort by header click, column chooser from the header menu, in-place Edit (F2), keyboard and selection shared with the grid, 100,000 rows smooth.
- Accept: tests (virtualization counts like M2.2b); e2e case `details-view`; `verify.sh`.
- Parity: BV.03, BC.06; view spec `ItemView` (details); C-LIST-1, C-HDR-1.

#### P5.11b Columns, list layouts, per-list view settings
- Depends: P5.11a · Effort: high · Unattended: yes · Status: todo
- Goal: the Core's columns, `ItemViewConfig` round trip in `DisplayListConfig` per list (view mode, columns, widths, sort, grouping) so a Windows library keeps its layouts; List Options dialog (`ListLayoutDialog`: Details, Thumbnails, Tiles tabs; Edit List Layout Cmd+L), Save List Layout..., Edit Layouts... (`ListEditorDialog`, Option+Cmd+L), Set all Lists to current Layout, saved layouts applied from Browse > List Layout and the toolbar's List Layouts drop-down.
- Accept: tests (a fixture list's columns survive import, edit, save, reload and match the original XML elements); `verify.sh`.
- Parity: MB.14 to MB.18, BT.10, DL.10, DL.11, FF.01; view specs `ListLayoutDialog`, `ListEditorDialog`.

#### P5.12 Tiles view and view-mode switch
- Depends: P5.11b · Effort: medium · Unattended: yes · Status: todo
- Goal: Tiles as `ThumbTileRenderer` draws them (192 × 96 default, inset 4, cover at most half the width, embossed text, `ThumbnailViewItemBack` fill), switch Thumbnails/Tiles/Details from the Views split button per list (persisted in the list's view config).
- Accept: tests; e2e case `view-modes`; `verify.sh`.
- Parity: BT.03, BV.02; C-LIST-1.

#### P5.13a Grouping
- Depends: P5.11b · Effort: high · Unattended: yes · Status: todo
- Goal: group by the Core's groupers from the Group split button, group headers as `ItemView` draws them (40 high, 1.15 × font, `ItemViewGroupText`, count, separator lines, expand image), collapse/expand one and all, Show Group Headers (Cmd+Shift+G); works in all three view modes.
- Accept: tests; `verify.sh`.
- Parity: BT.04, BT.06.

#### P5.13b Stacks
- Depends: P5.13a · Effort: high · Unattended: yes · Status: todo
- Goal: stack by the Core's stackers (Stack split button), stacks drawn as the original (inset cover with rotated covers behind, `StackFill`/`StackBorder`, "N books" caption), open stack bar (Close Stack, Previous, Next), Reset Top of Stack, Set as Top of Stack, custom stack thumbnail set/remove (`StacksConfig`), `CommonListStackLayout`.
- Accept: tests; e2e case `stacks`; `verify.sh`.
- Parity: BT.07, BV.05, BC.21, PB.28.

#### P5.14 Sorting by every field, natural order
- Depends: P5.11b · Effort: medium · Unattended: yes · Status: todo
- Goal: the Arrange split button with all Core sort keys, secondary order as the WinForms app, natural number order inside text ("Tome 2" before "Tome 10"), `BookCatalog` extended without losing its M2.2b timings.
- Accept: tests (timings over 100,000 items stay within 2x of M2.2b); `verify.sh`.
- Parity: BT.08.

#### P5.15a Quick search scopes and view filters
- Depends: P5.11b · Effort: medium · Unattended: yes · Status: todo
- Goal: quick search scope menu (All, Series, Writer, Artists, Descriptive, Catalog, Filename) on the search box, `LocalQuickSearch` (each list keeps its own search), view filters in the Views menu (Show All, not Read, Reading, Read, only Books, only fileless Entries, Duplicates) with the original "Because of active Views options, not all Books are displayed." bar and Display All.
- Accept: tests; `verify.sh`.
- Parity: BT.05, BT.09, BV.06, GK.01, PB.26.

#### P5.15b Search browser panes
- Depends: P5.15a · Effort: high · Unattended: yes · Status: todo
- Goal: the search browser (Shift+F8; three value columns with type combo boxes, counts, multi-select narrowing, 160 high with a grip), semantics and look read from `ComicRack.Engine/Controls/SearchBrowserControl.cs`.
- Accept: tests; e2e case `search-browser`; `verify.sh`.
- Parity: MB.07, SR.01; view spec `SearchBrowserControl`.

#### P5.32b Browser docking, browser tabs and list history
- Depends: P5.32a · Effort: high · Unattended: yes · Status: todo
- Goal: F3 shows and hides the browser (Escape and the grip from the reader as RK.04), docking modes Dock Bottom, Left, Right, Fill and Info Panel Right from the Docking Mode split button on the browser tab strip (Option+Cmd+Shift+1 to 5), browser tabs (Library F6; Folders F7 and Pages F8 as placeholders that P5.30 and P5.23b fill; lists opened in tabs, "Open in New Tab" of the tree, New Tab button), previous/next list history (toolbar buttons, Cmd+J, Cmd+K, mouse buttons 4 and 5), `CloseBrowserOnOpen`, `AlwaysDisplayBrowserDockingGrip`, `AnimatePanels`.
- Accept: tests; e2e case `browser-dock` (F3 hides and shows, Option+Cmd+Shift+2 docks left, state persisted after relaunch); `verify.sh`.
- Parity: MV.02 to MV.04, MB.01, MB.02, MB.09, MB.10, BT.02, GK.02, RK.04, LT.02 (New Tab), LT.10, PB.07, PB.29, PB.36; view spec `MainView`; C-TAB-1, C-SPLIT-1.

#### P5.16a Sidebar editing
- Depends: P5.2 · Effort: high · Unattended: yes · Status: todo
- Goal: the library tree menu and toolbar exactly as `ComicListLibraryBrowser`: Edit..., Rename (F2, inline), Sort, New Folder..., New List..., New Smart List... (opens P5.17a/b when they exist, a query editor until then), Copy List / Paste List, Open in New Window (`ComicBrowserForm`), Refresh, Remove... (with confirmation), Expand/Collapse all, tree quick search (Option+Cmd+F), Duplicate list from the browser toolbar, Edit List dialog (`EditListDialog`), name prompts through the `SelectItemDialog` equivalent; changes saved with the library.
- Accept: tests; e2e case `sidebar-edit`; `verify.sh`.
- Parity: LT.02 to LT.07, LT.09, LT.13, LT.14, BT.11, GK.03, DL.08, DL.29, DL.31; view specs `ComicListLibraryBrowser`, `EditListDialog`.

#### P5.16b Sidebar drag and drop, favorites, collapse state
- Depends: P5.16a · Effort: medium · Unattended: yes · Status: todo
- Goal: reorder and nest by drag, Favorites ("Add to Favorites", "Show Favorites", favorites panel with Refresh and Remove...), `ComicListItemFolder.Collapsed` persisted, list icons from the Core's image keys.
- Accept: tests; `verify.sh`.
- Parity: LT.12, LT.15, LT.16.

#### P5.16c Reading lists
- Depends: P5.16a, P5.3 · Effort: medium · Unattended: yes · Status: todo
- Goal: Add to List (menu of lists), Edit List > Move to Top/Bottom (Option+Cmd+T/B), Apply current Order, Export Reading List... / Import Reading List... (Cmd+Shift+C/V, `.cbl`, `ComicRack.Engine/Database/ComicReadingListItem.cs`), `ExportedListsContainFilenames`.
- Accept: tests (a `.cbl` round trip); `verify.sh`.
- Parity: BC.09, BC.10, LT.08, PB.40, FF.03.

#### P5.17a Smart-list editor: query text
- Depends: P5.16a · Effort: high · Unattended: yes · Status: todo
- Goal: the `SmartListQueryDialog` exactly: query text with the original syntax colouring (`SmartQuery*` tokens), its edit menu (Undo, Redo, Cut, Copy, Paste, Select All, Quick Format Cmd+F, Insert Match, Insert Value), the Core parser, error position, live count off the UI thread, save into the list.
- Accept: tests (all 19 fixture smart lists round trip through their query text and keep their counts); `verify.sh`.
- Parity: DL.22, LT.03; view spec `SmartListQueryDialog`.

#### P5.17b Smart-list editor: rule builder
- Depends: P5.17a · Effort: xhigh · Unattended: yes · Status: todo
- Goal: `SmartListDialog`/`MatcherEditor`/`MatcherGroupEditor` exactly: rows of field, operator, value; groups with all/any/not; base list; limit and order; the rule menu (New Rule Cmd+R, New Group Cmd+G, Delete, Cut, Copy, Paste, Move Up Cmd+U, Move Down Cmd+D); editors for text and number matchers.
- Accept: tests (build each fixture list's rules, compare XML with the original); e2e case `smartlist-editor`; `verify.sh`.
- Parity: DL.12, DL.21; view specs `SmartListDialog`, `MatcherEditor`, `MatcherGroupEditor`.

#### P5.17c Smart-list editor: remaining value editors
- Depends: P5.17b · Effort: high · Unattended: yes · Status: todo
- Goal: dates (relative and absolute), ratings, enums (manga, black and white, format...), yes/no, custom values, virtual tags, the plugin (script) matcher, the Match Value dialog (`ValueEditorDialog`).
- Accept: tests (every matcher type in the Core has an editor; a reflection test lists them); `verify.sh`.
- Parity: DL.25.

#### P5.18 Drag and drop
- Depends: P5.16c · Effort: high · Unattended: partial (drag from Finder is checked by e2e with a CGEvent drag; record if not reliable) · Status: todo
- Goal: files and folders from Finder onto the window (add to library or open, as the WinForms setting; nothing when `DisableDragDrop`), books onto a sidebar list (add), books to Finder (copy the file), `.crplugin` onto the window (install, P4.4).
- Accept: tests; e2e case `drag-drop`; `verify.sh`.
- Parity: LT.16, PB.30.

#### P5.19a Watch folders
- Depends: P5.1 · Effort: high · Unattended: yes · Status: todo
- Goal: per-folder watch on/off (`WatchFolder.Watch`), re-enable for remapped imports, FSEvents cost measured on the 14,706-book stand-in tree (CPU, memory, latency of a new file), new files appear in the library (checked per `NewBooksChecked`).
- Accept: tests (copy a CBZ into a watched temp folder, it appears within 5 s); numbers in §3; `verify.sh`.
- Parity: PB.39.

#### P5.19b Missing files, rescans, cover thumbnails, web comics
- Depends: P5.19a, P2.2 · Effort: high · Unattended: yes · Status: todo
- Goal: "Scan Book Folders" (Cmd+Shift+S) and `ScanStartup`, "Remove missing books" (`removeMissing: true` with the P2.2 `DriveChecker`), locate/relink a missing book (by path, then by size and hash), missing books shown as such in the grid, "Generate Cover Thumbnails", "Update Web Comics" (Cmd+Shift+W) and `UpdateWebComicsStartup` (DD18 amended; tested with a generated `.cbw` pointing to a local file URL).
- Accept: tests including a detached disk image (books on it are "missing", not removed); `verify.sh`.
- Parity: MF.06, MF.08, MF.09, PB.03, PB.04.

#### P5.20 Grid context menu, part 2
- Depends: P5.5, P5.16c · Effort: high · Unattended: yes · Status: todo
- Goal: Copy Data, Paste Data... (`ComicDataPasteDialog`: choose fields), Clear Data, Add to Library, Browse Books, Show in List, Web... (Cmd+R, opens the book's Web field), Refresh (F5 and the context item), Set as List Background and Reset List Background, New fileless Book Entry... (Cmd+Shift+N); Export Books and Update Book Files entries enabled when their tasks are done.
- Accept: tests; e2e case `context-menu-2`; `verify.sh`.
- Parity: BC.05, BC.11 to BC.13, BC.18, BC.20, BC.22, BV.07, ME.14, MF.13, DL.03; view spec `ComicDataPasteDialog`.

#### P5.21 Open With external programs
- Depends: P5.5 · Effort: medium · Unattended: yes · Status: todo
- Goal: "Open With" submenu with the configured programs (`ComicRack/Config/ExternalProgram.cs` semantics) and "Manage Programs..." (`OpenWithDialog` layout); Launch Services applications for the file type are added below as a `mac-only` part.
- Accept: tests; `verify.sh`.
- Parity: BC.03, DL.15; view spec `OpenWithDialog`.

#### P5.22a Preferences window: infrastructure, Reader, Behavior
- Depends: P5.1, P5.T4 · Effort: high · Unattended: yes · Status: todo
- Goal: the `PreferencesDialog` exactly (C-PREF-1): left column with the five tabs and their 32 px icons, collapsible groups, OK / Cancel / Apply, opened by Cmd+, from the app menu (and Cmd+F9). Reader tab: Display (colour sliders, Automatic Contrast Enhancement, High Quality, Anamorphic Scaling, Reset), Overlays (with the overlay preview panel), Mouse Scrolling, Hardware Acceleration (deviation per PR.06), the Keyboard group as a placeholder until P5.22c, and a separate "macOS" group for the Mac-only reader options (swipe at the edge, DD8). Behavior tab: generated from the mirrored Settings booleans by category and description (TR `Settings`), exactly as `FormUtility.FillPanelWithOptions`.
- Accept: tests (the Behavior tab lists the 40 options of the ledger in the original categories and order); e2e case `preferences`; `mac/tools/chrome-sheet.sh P5.22a PreferencesDialog`; `verify.sh`.
- Parity: ME.16, DL.16, PR.01, PR.02, PR.04 to PR.07, PR.13; view spec `PreferencesDialog`; C-PREF-1, C-GRP-1.

#### P5.22b Preferences: Libraries and Advanced
- Depends: P5.22a, P5.19a · Effort: high · Unattended: yes · Status: todo
- Goal: Libraries tab (Book Folders with watch check boxes and Add/Remove/Change/Open, Scanning, Display live counters, Virtual Tags; Sharing and Server Settings hidden as D4), Advanced tab (Books: cover filter and file-writing options; Caches & Memory Usage with sizes, usage labels and Clear buttons (cover cache size DD9 is the thumbnail memory cache); Messages and Social reset; Languages as P5.27b; Database Backup and Backup Manager as P5.25; Explorer Integration as P5.34; Wireless Setup hidden as D4).
- Accept: tests; `verify.sh`.
- Parity: PR.08 to PR.12, PR.18, PR.21, PR.22, PR.24; view spec `PreferencesDialog`.

#### P5.22c Preferences: keyboard shortcut editor
- Depends: P5.22a · Effort: high · Unattended: yes · Status: todo
- Goal: DD19 superseded: Reader > Keyboard as `cYo.Common.Windows/Forms/KeyboardShortcutEditor.cs` (command list by group with the original command ids, names and icons, up to the original number of keys per command, key capture as `KeyInputForm`), Import... with the "Default" layout entry, Export... (`*.xml` in the `cYo.Common.Windows/KeyboardShortcuts.cs` format, so layouts move between Windows and Mac); the reader uses the edited map.
- Accept: tests (export then import round trip; a layout file written in the original format imports; changing Next Page to a new key works in the reader); `verify.sh`.
- Parity: PR.03, DL.32, FF.05, RK.01 to RK.31 (keys editable); view spec `KeyboardShortcutEditor`.

#### P5.32c Workspaces
- Depends: P5.32b, P5.22a · Effort: high · Unattended: yes · Status: todo
- Goal: DD16 superseded: Save Workspace... (Option+Cmd+W, `SaveWorkspaceDialog`), Edit Workspaces... (Ctrl+Cmd+W, `ListEditorDialog`), the workspace list in Browse > Workspaces and in the Tools drop-down; applying a workspace restores what `ComicRack/Config/DisplayWorkspace.cs` stores (window layout, docking, panel visibility and sizes, reader display settings); stored in the Mac settings with the same fields.
- Accept: tests (round trip; applying restores dock mode and panel visibility); e2e case `workspaces`; `verify.sh`.
- Parity: MB.11 to MB.13, DL.10, DL.19; view specs `SaveWorkspaceDialog`, `ListEditorDialog`.

#### P5.33 Notification-area equivalent (menu bar status item)
- Depends: P5.22a · Effort: medium · Unattended: yes · Status: todo
- Goal: C-TRAY-1: with `MinimizeToTray` (minimize) or `CloseMinimizesToTray` (close), ComicRack hides its windows and its Dock icon (activation policy accessory) and shows an `NSStatusItem` with the original small icon and the menu Restore / Exit; Restore brings everything back (policy regular).
- Accept: tests of the policy logic; e2e case `tray` (option on, Cmd+M hides the window, the status item's Restore brings it back); `verify.sh`.
- Parity: NT.01, PB.34, PB.35; C-TRAY-1.

#### P5.23a Reader: navigation extras
- Depends: P5.1 · Effort: high · Unattended: yes · Status: todo
- Goal: Previous/Next/Random Book (Option+Cmd+P/N/O and the reader keys P, N, L, in the current list order; `AutoNavigateComics`), bookmarks (Set Bookmark... with a name, Remove, Previous, Next, Last Page Read; the Core's page bookmarks; Cmd+Shift+B/D/P/N/L, Cmd+PageUp/PageDown), Copy Page (Cmd+C, clipboard image), Export Page... (Cmd+Shift+C), `OpenLastPage`, the page context menu's Books and Bookmarks submenus, remembered page failures for a few seconds (M2.3a item). No feature that the original lacks (the earlier "Go to Page" idea is dropped: Cmd+G is Reveal).
- Accept: tests; e2e case `reader-nav`; `verify.sh`.
- Parity: MR.05 to MR.07, ME.07 to ME.13, PC.04, PC.05, PC.07, RK.01 to RK.03, RK.07, PB.06, PB.11.

#### P5.23b Pages view and page properties
- Depends: P5.23a, P5.32b · Effort: high · Unattended: yes · Status: todo
- Goal: the Pages browser tab (F8; `ComicPagesView`: thumbnails, tiles or details of the open book's pages, Group, Arrange, Page Filter) with the `PagesView` menu (Page Type, Page Rotation, Page Position Default/Near/Far, Set/Remove Bookmark, Move to Top/Bottom, Reset original Order, Copy Page, Merge Pages, Select All, Invert Selection, Refresh, Mark as Deleted), Edit > Page Type and Page Rotation, the permanent page rotation keys Y and Shift+Y; click to go.
- Accept: tests; `verify.sh`.
- Parity: MB.04, ME.05, ME.06, PC.03, PV.01 to PV.05, RK.29; view specs `ComicPagesView`, `PagesView`.

#### P5.23c Reader: display settings
- Depends: P5.23a, P3.6 · Effort: high · Unattended: yes · Status: todo
- Goal: Book Display Settings (F9; `ComicRack/Dialogs/ComicDisplaySettingsDialog.cs` exactly): background colour or texture, paper texture, page margins, colour adjustment through the Core's `BitmapAdjustment`/`ImageProcessing` (per book and default), page display change animation and `BlendWhilePaging`.
- Accept: tests (an adjusted page differs as the P3.6 golden says); `verify.sh`.
- Parity: MD.01, DL.04, PB.16; view spec `ComicDisplaySettingsDialog`.

#### P5.23d Reader: magnifier, click zones, wheel paging, swipe at edge, auto scroll
- Depends: P5.22a · Effort: high · Unattended: partial (feel -> C2) · Status: todo
- Goal: magnifier (Option+Cmd+M, key M; the magnifier image frame; `MagnifySetupControl` in the toolbar drop-down: size, zoom, options), click-to-turn zones, mouse-wheel paging and `PageChangeDelay`, `ScrollingDoesBrowse`, swipe at the edge of a zoomed page turns the page (DD8, `mac-only`), auto scrolling (Cmd+S, key S) and double page auto scrolling (Option+Shift+S, Shift+S), move part keys (V, B, Cmd+Down/Up), Windows touch gestures mapped where a trackpad gesture exists.
- Accept: tests; `verify.sh`.
- Parity: MD.27, MR.11, MR.12, TB.07, DL.33, RK.12, RK.13, RK.19, RK.31, PB.12, PB.13.

#### P5.23e Reader: remaining fit modes, zoom, rotation, page filter, full-resolution zoom
- Depends: P5.23a · Effort: high · Unattended: yes · Status: todo
- Goal: Original Size, Fit Width (adaptive), Fit Best (the Core's `BestFit`), Only fit if oversized (Cmd+Shift+0, key O); Zoom In/Out (Cmd+=, Cmd+-, keys Z and Shift+Z), Toggle Zoom (Option+Cmd+Z, smart zoom), presets 100 to 400 %, Custom... (`ZoomDialog`); rotation menu (Rotate Left/Right, 0/90/180/270°, Autorotate Double Pages; keys R, Shift+R, A); `ResetZoomOnPageChange`, `ZoomInOutOnPageChange`, `TrueRightToLeftReading`, `LeftRightMovementReversed`; the navigator's page filter (deleted pages); re-decode the visible page without the 48 MP cap when zoomed; a background pass that learns page sizes for spread parity (M2.3b item).
- Accept: tests; `verify.sh`.
- Parity: MD.02, MD.05, MD.07, MD.13 to MD.23, RK.22, RK.24, RK.25, RK.27, RK.28, DL.26, PB.14, PB.15, PB.19, PB.20.

#### P5.23f Reader: continuous layout, double-page overlap, flip parts, minimal UI, realistic display
- Depends: P5.23e, P5.32a · Effort: high · Unattended: yes · Status: todo
- Goal: no longer optional (R9): Continuous layout, double-page overlap, flip parts, Minimal User Interface (F10, key K toggles its menu, `AutoMinimalGui`), Toggle Realistic Display (Shift+D, page ornaments; §8 item 132 reinstated), and the deviations TB.10 and PB.37 recorded (the macOS menu bar cannot be hidden from a window).
- Accept: tests; `verify.sh`.
- Parity: MD.11, MD.24, TB.10, RK.18, RK.20, PB.17, PB.37.

#### P5.24a Export
- Depends: P5.20 · Effort: high · Unattended: yes · Status: todo
- Goal: the `ExportComicsDialog` exactly, as a modal dialog window (C-DLG-2): formats CBZ, CB7 (with `7zz`), CBT, folder of images, image format and quality, resize, naming, target folder, "add to library", saved export settings, Export Books... (Cmd+Shift+E) and Export with Previous (Option+Cmd+E); runs on the Core's `ComicExporter` with progress and cancel; BMP/GIF/TIFF writers through ImageIO if the dialog offers them.
- Accept: tests (exported CBZ opens with the same page count; image format honoured); e2e case `export`; `verify.sh`.
- Parity: BC.14, DL.09, FF.08; view spec `ExportComicsDialog`.

#### P5.24b PDF export
- Depends: P5.24a · Effort: high · Unattended: yes · Status: todo
- Goal: pass B rewrite of `PdfStorageProvider` (today a throwing replacement) writing PDF with Skia `SKDocument` (one page per image, page size from the image), same options; manifest entry.
- Accept: test: export 5 pages to PDF, `crcli page` reads them back with pdfium at the expected sizes; `verify.sh`.
- Parity: FF.08.

#### P5.25 Backup and restore
- Depends: P5.22a · Effort: medium · Unattended: yes · Status: todo
- Goal: the Advanced tab's Database Backup (Backup Database..., Restore Database... with a confirmation and a pre-restore copy) and Backup Manager groups (location, items, number kept, alternate configs, backup on startup and shut down; `ComicRack.Engine/Backup/{BackupManager,BackupArchiveCreator}.cs`), the backup status-bar icon.
- Accept: tests (backup, change, restore, the change is gone); `verify.sh`.
- Parity: PR.19, PR.20, FF.06.

#### P5.26 Tasks, errors, progress
- Depends: P5.9 · Effort: high · Unattended: yes · Status: todo
- Goal: Tasks dialog (Cmd+Shift+T; `TasksDialog` Background Tasks tab with cancel; Server Statistics tab hidden as D4), Errors dialog (`ShowErrorsDialog`), the status-bar activity icons with their animations (scan, read info, write info, pages, export, backup) and their click actions, and a Mac `AutomaticProgressDialog`/`ProgressDialog` (the M0.3b stub runs inline) for long Core operations.
- Accept: tests; `verify.sh`.
- Parity: MF.11, SB.02, DL.17, DL.20, DL.24; view specs `TasksDialog`, `ShowErrorsDialog`.

#### P5.27a Localization sweep
- Depends: P2.6, P5.22b · Effort: medium · Unattended: yes · Status: todo
- Goal: every Mac view string through TR with the **original keys** (§2.11 rule 2; section = original form, key = control name); new keys only for Mac-only text, in `mac/ComicRack.Mac/Languages/en/Mac.xml` (DD17; copied next to the TR files at build); a lint test that fails on literal user-facing strings in `.axaml` and menus and on a Mac key whose English text equals an existing original key's text.
- Accept: tests; `verify.sh`.

#### P5.27b Language choice
- Depends: P5.27a · Effort: medium · Unattended: yes · Status: todo
- Goal: the Advanced tab's Languages group (list, restart note, "Help localizing ComicRack..."); e2e in German shows translated menus.
- Accept: e2e case `german`; `verify.sh`.
- Parity: PR.17.

#### P5.28a Windows import, version 2
- Depends: P5.1 · Effort: high · Unattended: yes · Status: todo
- Goal: preview ("N of M books found"), UNC prefixes, NFC normalization of paths, relink by size and hash for books not found by prefix, cleanup or restore of `ComicDb.before-import-*.xml`, the CLI's `PathRemap` replaced by the host's `PathRemapRule` and import (M1.5b, M2.1, M2.2c items).
- Accept: tests; `verify.sh`.
- Parity: the import dialog is a `mac-only` row (no original equivalent); add it.

#### P5.28b Settings and library compatibility through XML
- Depends: P5.28a, P5.22b · Effort: high · Unattended: partial (a real `Config.xml` -> C1, else synthetic) · Status: todo
- Goal: DD3: import the portable sections of a Windows `Config.xml` (reader and library defaults, smart-list related settings, external programs, workspaces, list layouts, keyboard layout, read with an `XmlReader` without linking `ComicRack/Config/Settings.cs`), and "Export Library for Windows..." (reverse mapping, never in place).
- Accept: tests (round trip: import a Windows DB, export it back with the reverse mapping, XML equal except paths); `verify.sh`.
- Parity: FF.01, FF.02.

#### P5.29 Quick Open pane
- Depends: P5.32a · Effort: medium · Unattended: yes · Status: todo
- Goal: the WinForms `QuickOpenView` in the reader area when no book is open (`ShowQuickOpen`): the "Quick Open" caption card (616 × 289, `CaptionControl` look), recent and continue-reading books as 128 px thumbnails, buttons Open, Browser, Open File...; host location for `QuickOpenUI`/`QuickOpenHtml` (P4.11a/d).
- Accept: tests; `mac/tools/chrome-sheet.sh P5.29 MainWindow`; `verify.sh`.
- Parity: QO.01, PB.31; view spec `QuickOpenView`.

#### P5.30 Folders browser
- Depends: P5.11a, P5.32b · Effort: medium · Unattended: yes · Status: todo
- Goal: the Folders browser tab (F7; `ComicRack/Views/ComicListFolderFilesBrowser.cs` semantics and look): folder tree with its toolbar (Show Favorites, Include all Subfolders, New Window, New Tab, Refresh) and menu (Open in New Window, Open in New Tab, Add Folder to Favorites, Add Folder to Library, Refresh), folder favorites, books of a folder (in the library or not), open and add.
- Accept: tests; `verify.sh`.
- Parity: MB.03, FB.01 to FB.04; view spec `ComicListFolderFilesBrowser`.

#### P5.31 Splash, About, News, updates, Help
- Depends: P2.6 · Effort: medium · Unattended: yes · Status: todo
- Goal: the original splash (`Splash`: 619 × 317 image with version text, fade) at startup when `ShowSplash`, and the same panel as About (app menu; Option+F1) with the GPL notice and source link added below as `mac-only`; News dialog (`NewsDialog`) and `NewsStartup`; "Check For Update..." opens the release page; Help menu exactly: ComicRack Documentation... (F1, Cmd+?), Plugins (script help entries), Choose Help System (`ComicRack/Output/Help/*.ini`), ComicRack Homepage... (Shift+F1), ComicRack User Forum... (Option+Cmd+F1); Quick Introduction stays deferred (DjVu, D4).
- Accept: tests; `verify.sh`.
- Parity: MH.01 to MH.10, DL.13, DL.23, PB.01, PB.05; view specs `Splash`, `NewsDialog`; C-SPLASH-1.

#### P5.34 Finder integration (file associations)
- Depends: P5.10a, P5.22b · Effort: medium · Unattended: partial (changing the real default apps is an owner check -> C6; tests use a fake) · Status: todo
- Goal: the Advanced tab's Explorer Integration group as "Finder Integration": the original format list (`PreferencesDialog.cs:867-872`), "Overwrite existing associations" (= make ComicRack CE the default app through Launch Services, `LSSetDefaultRoleHandlerForContentType`, versus only being listed in Open With through `CFBundleDocumentTypes`), Change... applies. Sessions never call the real API (it would change the owner's system, §2.6 rule 5): an `IFileAssociations` adapter with a fake in tests.
- Accept: tests with the fake adapter; `verify.sh`. Owner check (C6): one association set and reverted.
- Parity: PR.23.

### P6 Packaging and release

#### P6.1 v1 identity, document types, MVP migration
- Depends: P5.10a, P2.6 · Effort: high · Unattended: yes (the final id is confirmed at C5) · Status: todo
- Goal: DD2: `ComicRack CE`, default id `org.comicrackce.ComicRack` (env `BUNDLE_ID`), `Product` attribute and data folders `ComicRack Community Edition` (Application Support, Caches, Logs), first-launch offer to copy an MVP library, `Info.plist` (`CFBundleDocumentTypes`, `UTImportedTypeDeclarations` for cbz/cbr/cb7/cbt/cbl/crplugin, version from git), icon (`.icns` built from the original `ComicRack/Icons/icon1.ico` 256 px frame, C-ICON-1; C5 may supply a 1024 px rendering of the same artwork).
- Edit: `mac/tools/make-app.sh` (new name `mac/artifacts/ComicRack CE.app`), `mac/ComicRack.Mac/ComicRack.Mac.csproj`, `mac/ComicRack.Mac.Host/MacPaths.cs`, tests and docs that name the MVP folders.
- Rollback: revert the commit; a migrated library stays in the new folder and the MVP folder is untouched (copy, never move).
- Accept: tests; e2e `launch-quit` and `menu-structure` with the new name; `plutil -lint` of the plist; `verify.sh`.

#### P6.2 Signing script (inside-out, minimal entitlements)
- Depends: P6.1 · Effort: high · Unattended: partial (Developer ID run -> C5) · Status: todo
- Goal: `mac/packaging/sign.sh` signs every Mach-O inside-out (dylibs, `7zz`, the executable, the bundle) with `SIGN_IDENTITY` (default `-`), hardened runtime, timestamp when not ad-hoc, entitlements `mac/packaging/app.entitlements` (`allow-jit`, `allow-unsigned-executable-memory`; `disable-library-validation` only for ad-hoc builds); non-executable payload moved to `Contents/Resources` where the .NET host allows it (measure; keep in `MacOS` otherwise).
- Accept: ad-hoc: `codesign --verify --deep --strict`, the bundle smoke and e2e `launch-quit` pass; `verify.sh`.

#### P6.3 Notarization, stapling, DMG, quarantine test
- Depends: P6.2 · Effort: medium · Unattended: partial (notarization -> C5) · Status: todo
- Goal: `mac/packaging/make-dmg.sh` (`hdiutil` UDZO with an Applications link), `mac/packaging/notarize.sh` (`xcrun notarytool submit --keychain-profile "$NOTARY_PROFILE" --wait`, `xcrun stapler staple`), a quarantine test of a copy (`xattr -w com.apple.quarantine "0081;$(printf %x $(date +%s));Safari;" <copy>`, `spctl -a -t exec -vv`), dry-run mode without credentials.
- Accept: `mac/packaging/make-dmg.sh` builds a DMG that mounts and contains the app; `mac/packaging/notarize.sh --dry-run` prints the commands; `verify.sh`.

#### P6.4 Release and nightly workflow
- Depends: P6.3, P3.9 · Effort: medium · Unattended: partial (secrets -> C5) · Status: todo
- Goal: `.github/workflows/mac-release.yml` (tag `mac-v*`: build, sign, notarize, DMG, GitHub release asset) and a nightly unsigned artifact; documented secrets (`MAC_CERT_P12`, `MAC_CERT_PASSWORD`, `NOTARY_KEY_ID`, `NOTARY_ISSUER`, `NOTARY_KEY`).
- Accept: YAML parses; `verify.sh`.

#### P6.5 Third-party notices and docs
- Depends: P6.1 · Effort: medium · Unattended: yes · Status: todo
- Goal: `mac/packaging/THIRD_PARTY_NOTICES.md` generated from the restored packages and bundled binaries (Avalonia, SkiaSharp, HarfBuzzSharp, IronPython and StdLib, PDFium, 7-Zip with the unRAR restriction, SharpCompress, SharpZipLib, DynamicExpresso, MySqlConnector, CSJ2K, LibHeifSharp, CommunityToolkit.Mvvm, ...), copied into `Contents/Resources`; macOS section in `mac/README.md`; `mac/upstream-proposals/P6.5-readme-macos.md` for the root README (owner decides).
- Accept: a test that every `PackageReference` of the app's closure appears in the notices; `verify.sh`.

#### P6.6 Homebrew cask template (optional)
- Depends: P6.3 · Effort: low · Unattended: yes · Status: todo
- Add: `mac/packaging/comicrack-ce.rb` (template, version and SHA-256 placeholders filled by the release workflow).
- Accept: `ruby -c mac/packaging/comicrack-ce.rb`; `verify.sh`.

#### P6.7 Signed, notarized release candidate
- Depends: P6.2, P6.3, C5 · Effort: medium · Unattended: yes once C5 is answered · Status: todo
- Goal: build, sign with the Developer ID, notarize, staple, DMG; verify `spctl -a -t exec -vv` says "accepted, source=Notarized Developer ID" and the quarantined copy launches; drop `disable-library-validation`.
- Accept: the commands' output in §3; e2e `launch-quit --bundle` on the notarized app; `verify.sh`.

### P7 Hardening, parity audits and v1 acceptance

#### P7.1 Crash reports and logs
- Depends: P5.10b · Effort: medium · Unattended: yes · Status: todo
- Goal: an unhandled exception shows a crash dialog (`CrashDialog` layout and semantics: details, copy, quit or continue), "Report a Problem..." in Help zips the logs and settings (no library) to a chosen folder (a `mac-only` ledger row), log rotation checked.
- Accept: tests; e2e case `report-problem`; `verify.sh`.
- Parity: DL.05; view spec `CrashDialog`; C-DLG-2.

#### P7.2 Large library
- Depends: P3.1, P5.11a · Effort: high · Unattended: yes · Status: todo
- Goal: a 100,000-book synthetic library end to end (open, grid and details scroll, sidebar counts lazily for series matchers if over budget, search, save at quit), memory and time budgets in §3.
- Accept: `Slow` tests; `verify.sh --full`.

#### P7.3 Volumes and missing media
- Depends: P5.19b · Effort: high · Unattended: partial (SMB -> C6) · Status: todo
- Goal: disk-image tests (attach, scan, detach, books missing, reattach, books back; cache keys stable), network-volume notes; slow-archive cases for `PageService`/`ThumbnailService` dispose (move to a worker if a read can block beyond 1 s).
- Accept: `Slow` tests; `verify.sh --full`.

#### P7.4 Permissions (TCC) UX
- Depends: P5.19b · Effort: medium · Unattended: partial (real prompts -> C6) · Status: todo
- Goal: an `UnauthorizedAccessException`/EPERM during scan or open gives a clear message with the System Settings path (Files and Folders, Full Disk Access); a `chmod 000` folder test.
- Accept: tests; `verify.sh`.

#### P7.5 Accessibility pass
- Depends: P5.31 · Effort: medium · Unattended: partial (VoiceOver judgement -> C6) · Status: todo
- Goal: automation names and roles on the grid cells, the reader, the sidebar rows, toolbar items; keyboard focus order; an AX listing through ui-drive (System Events UI elements) checks names exist.
- Accept: e2e case `accessibility`; `verify.sh`.

#### P7.6 Concurrency stress
- Depends: P5.9, P5.19a · Effort: high · Unattended: yes · Status: todo
- Goal: scan, watch events, reader turns, edits, write-back and thumbnails at once for 10 minutes with Avalonia thread checks on; no exception, no deadlock, consistent library.
- Accept: `Slow` test; `verify.sh --full`.

#### P7.7 Performance tuning against the baseline
- Depends: P3.3, P7.2 · Effort: high · Unattended: yes · Status: todo
- Goal: launch time (cold, warm), memory after a full scroll, page turn and scroll numbers from `crcli bench --compare` and the overlay log; fix the top regressions within the session; update the baseline only with a reason.
- Accept: `crcli bench ... --compare mac/benchmarks/baseline.json` exits 0; `verify.sh --full`.

#### P7.A1 Parity audit: menus, commands and shortcuts
- Depends: P5.32c, P5.22c, P5.23f, P5.31, P5.33, P4.5 · Effort: high · Unattended: partial (real-window checks need an unlocked GUI) · Status: todo
- Goal: walk the ledger areas MF, ME, MB, MR, MD, MH, MA, TB, TC, PC, NT, BT, BC, LT, FB, PV, RK, GK in the running app: `ui-drive menu-items` for every menu compared with the ledger by `ledger-check --menus` (labels, order, shortcuts, check marks, enabled state with and without a book open), each context menu opened with `--show` and a right click, reader keys driven by e2e; fix gaps that take under an hour in this task, turn bigger ones into `R<n>` tasks; set rows to `done` or `deviation` (reason) with the date.
- Add: e2e case `menu-parity` (the full menu dump compared with the ledger).
- Accept: `python3 mac/tools/ledger-check/ledger-check.py --report` shows no `todo` in these areas except rows whose owning task is not done (listed in §9 with the task); `mac/tools/e2e.sh menu-parity`; `verify.sh`.
- Parity: the areas listed.

#### P7.A2 Parity audit: dialogs, Preferences, settings and files
- Depends: P7.A1, P5.24b, P5.25, P5.26, P5.27b, P5.28b, P5.34, P4.11e · Effort: high · Unattended: partial (shots need an unlocked GUI) · Status: todo
- Goal: walk DL, IN, PR, PB, FF: every dialog opened with `--show` and compared with its view spec (an Avalonia control-tree dump against `designer-dump`: same groups, controls, order, TR keys and defaults); every Behavior option toggled and its effect checked (a test per PB row where feasible, else a scripted e2e step); the `SettingsDefaultsTests` of P5.1 extended to every mirrored property; file round trips (FF rows); a contact sheet `A2 --dialogs`.
- Accept: `ledger-check --report` shows no `todo` in these areas (same exception rule); `mac/tools/chrome-sheet.sh A2 --dialogs`; `verify.sh`.
- Parity: the areas listed; chrome-guidelines §7 C-DLG-2, C-PREF-1, C-CTRL-1.

#### P7.A3 Parity audit: browser, lists, reader and scripting behaviour
- Depends: P7.A1, P4.15 · Effort: high · Unattended: partial (shots need an unlocked GUI) · Status: todo
- Goal: walk BV, MV, SB, SR, SP, IP, QO, SC with behaviour scenarios on fixture libraries (grouping, stacking, sorting, filters, quick search scopes, search browser narrowing, list layouts per list, docking modes, workspaces, reader modes and overlays, every hook type with a sample script); compare the look of each view with chrome-guidelines §4 metrics (headless assertions) and produce a contact sheet `A3 --views`.
- Accept: `ledger-check --report` shows no `todo` in these areas (same exception rule); `mac/tools/chrome-sheet.sh A3 --views`; `verify.sh`.
- Parity: the areas listed; chrome-guidelines §7 C-LIST-1, C-TREE-1, C-READ-1, C-TAB-1, C-SB-1.

#### P7.A4 Final parity and chrome audit before v1 acceptance
- Depends: P7.A1, P7.A2, P7.A3, P7.1 to P7.7 · Effort: medium · Unattended: partial (the sheet needs an unlocked GUI) · Status: todo
- Goal: the whole ledger has no `todo` row (only `done`, `deviation` with a valid reason, `deferred (D4)`, `mac-only`); every chrome-guidelines §7 row is applied as decided (or as the owner flipped it at C2); the literal-colour lint and `ledger-check` are green; the C6 contact sheet `mac/tools/chrome-sheet.sh C6 --all` (every view, light and dark, with references); a parity summary for `mac/plans/v1-acceptance.md` (counts per status, the deviation list with reasons, the D4 list).
- Accept: `python3 mac/tools/ledger-check/ledger-check.py --report --strict` (strict: any `todo` fails) exits 0; the sheet files exist; `verify.sh --full`.
- Parity: the whole ledger.

#### P7.8 v1 acceptance pack and hand-over
- Depends: every non-optional task (including P7.A4) · Effort: medium · Unattended: partial (the owner's run -> C6) · Status: todo
- Goal: `mac/plans/v1-acceptance.md` (all owner-only items left from C2 and C4, a clean-user Gatekeeper launch, real library, SMB, TCC, gestures, plugins, the P7.A4 parity summary and the C6 contact sheet `mac/artifacts/chrome/C6/sheet.png`, verdict table), `mac/QUICKSTART.md` for v1, the full gate, an Opus-tier review of the whole `mac/` tree against §2.8 and §2.11, the C6 block.
- Accept: full gate passes; the pack exists; `verify.sh --full`.

### Upstream sync tasks
_(Created by the orchestrator from an `upstream-sync-check.sh` report, IDs `U<yyyymmdd>.<n>`, same template; `mac/UPSTREAM-SYNC.md` §4.)_

### Review fix tasks
_(Created from review findings of severity high, IDs `RV<n>`, same template; `RUNBOOK.md` §4. RV1 to RV4b sit with the P0 blocks above.)_

---

## 8. Mapping: every open item of `mvp-track.md` §9 (and the deferred MVP tasks)

Items are listed in the order of `mvp-track.md` §9; "closed" means a later MVP task already did it (the closing task in brackets).

| # | Item (source task: short form) | Placed in / outcome |
|---|---|---|
| 1 | [M0.3a] census needs full Roslyn diagnostics | closed (M0.3b) |
| 2 | [M0.3a] ~50 pass A errors from Common.Windows/Presentation | closed (M0.3b stubs) |
| 3 | [M0.3a] NetworkManager and Sync reference excluded types | closed (M0.3b, M1.3) |
| 4 | [M0.3a] ComicStorageMsSql / MemoryInfo | closed (excluded); RAM info for defaults: P2.2 |
| 5 | [M0.3a] drop System.Text.Json / Microsoft.CSharp packages (NU1510) | dropped: package lists stay 1:1 with the originals so P0.5a can diff them mechanically |
| 6 | [M0.3a] census tool not in the solution | closed (M1.3) |
| 7 | [M0.3b] decision record | closed |
| 8 | [M0.3b] ItemViewConfig stub drops view settings | closed (M1.1) |
| 9 | [M0.3b] IViewableItem/IColumn reduced stubs | closed (M1.1 kept them); the Mac table has its own column model (P5.11a) |
| 10 | [M0.3b] JPEG 2000 CSJ2K has no bitmap creator | P2.3c |
| 11 | [M0.3b] ComicLibraryServerConfig real files | dropped: library sharing is deferred (D4) |
| 12 | [M0.3b] DuckTyping throws | P4.1 checks script need; generator only if a script needs it (P4.13c) |
| 13 | [M0.3b] inert stubs (AutomaticProgressDialog inline, DeviceSync none, ...) | AutomaticProgressDialog: P5.26; device sync deferred (D4) |
| 14 | [M0.3b] 13 pass B errors of mirroring replacements | closed (M1.3) |
| 15 | [M0.3b] api-inventory in solution and CI | closed (solution, M1.3); guard: P0.5b, P3.9 |
| 16 | [M0.3b] M0.4 reads inventory.json | closed |
| 17 | [M0.4] GeneXus licence Apache-2.0, §5.3 says MIT, §11.4 log, GPLv2-only check | P0.2 |
| 18 | [M0.4] reuse S2-coverage as accept | closed (drawing-coverage, M1.2a) |
| 19 | [M0.4] implement what GeneXus lacks | closed (M1.2b, M1.2c) |
| 20 | [M0.4] scripts and `System.Drawing` resolution | P4.1, P4.2, P4.8 |
| 21 | [M0.4] one Skia version | closed (M1.2a) |
| 22 | [M1.1] `Cursor.Draw` | closed (M1.2a) |
| 23 | [M1.1] Forms slice is types-only; assembly name for scripts | P4.2 (decision), P4.7, P4.8, P4.9a |
| 24 | [M1.1] real IColumn/IViewableItem cannot be linked | closed (no action; P5.11a own model) |
| 25 | [M1.1] Compat.Forms.Tests needs the reference pack | closed by `verify.sh` (pass A build when missing); CI: P3.9 |
| 26 | [M1.1] shadow projects outside the solution | closed (M1.3) |
| 27 | [M1.2a] assembly identity of the Drawing shim | P4.1, P4.2, P4.8 |
| 28 | [M1.2a] PdfStorageProvider throws | P5.24b |
| 29 | [M1.2a] inlined enums invisible to api-inventory | P0.5b (pass B compile is the authority, noted in the report) |
| 30 | [M1.2a] drawing-coverage in the solution | closed (M1.3) |
| 31 | [M1.2a] static `SystemFonts`/`Brushes`/... throw | P3.4, P3.5a |
| 32 | [M1.2a] 5 remaining pass B errors | closed (M1.3) |
| 33 | [M1.2b] `Bitmap(Image, int, int)` | closed (M1.2c) |
| 34 | [M1.2b] check-3 pattern in tests | closed (M1.2c) |
| 35 | [M1.2b] TIFF decode; BMP/GIF/TIFF save | decode P2.3b; save P5.24a |
| 36 | [M1.2b] script-only members (`RawFormat`, resolution, ...) | P4.7 (generated surface), P4.13c by rank |
| 37 | [M1.2b] 24 bpp convert cost | P3.3 (decide from Release numbers) |
| 38 | [M1.2b] Unpremul pages | closed (M2.3a `detectOpaque`) |
| 39 | [M1.2b] finalizer and GC pressure | P3.3 (watch), P7.7 |
| 40 | [M1.2b] RuntimeIdentifier fix for other projects | closed (M1.2c); rule for new projects in §2.5 |
| 41 | [M1.2b] CI runs drawing tests after pass A | P3.9 |
| 42 | [M1.2c] large reductions alias (Mitchell) | closed for app paths (M2.2a/M2.3a mipmaps); Core resizes: P3.6, P3.7 |
| 43 | [M1.2c] CreateErrorPage needs DrawString | closed (M1.5a `MacImagePool`); text on the error page: P3.4 |
| 44 | [M1.2c] KnownColorTableEx | closed (M1.3) |
| 45 | [M1.2c] cheap Graphics members not done | P3.5a, P3.5b, P4.10e |
| 46 | [M1.2c] `HasOutstandingLocks` blocks `FromImage` | P3.5a (relax only if a caller needs it) |
| 47 | [M1.2c] items closed by M1.2c | closed |
| 48 | [M1.3] items closed by M1.3 | closed |
| 49 | [M1.3] override CreateErrorPage | closed (M1.5a) |
| 50 | [M1.3] `Resources` decoded on each access | P3.3 (watch) |
| 51 | [M1.3] CI steps (build, tests, coverage, census guard) | P3.9 |
| 52 | [M1.3] ~550 warnings from linked sources | P0.3 |
| 53 | [M1.3] runtime gaps compile cannot show | P1.2, P1.3 |
| 54 | [M1.5a] items closed by M1.5a | closed |
| 55 | [M1.5a] error page without text | P3.4 |
| 56 | [M1.5a] `Thread.Abort` at three sites | P1.1 |
| 57 | [M1.5a] `DatabaseManager.Save` swallows errors; save time | closed (M2.1 `SaveVerification`); timing P3.3 |
| 58 | [M1.5a] entry assembly attributes decide folders | closed (M1.5b, M2.0a); revisited by P6.1 |
| 59 | [M1.5a] "Free Image Memory" thread never stops | closed (documented, harmless) |
| 60 | [M1.5a] MacSettings not persisted | P5.1 |
| 61 | [M1.5a] NativeLibraryResolver in the host | closed (M1.4) |
| 62 | [M1.5a] CI: host tests need the repo checkout | P3.9 |
| 63 | [M1.6] `crcli page` check on fixtures | closed (M1.5b) |
| 64 | [M1.6] CB7 writer untested, CBR not producible | CB7 via `7zz`: P2.4a; CBR: C1 sample, P0.7b, P2.4b |
| 65 | [M1.6] fixture DB has `S:\` paths | closed (M2.1 import) |
| 66 | [M1.6] default typeface on CI | P3.9 (do not depend on page text) |
| 67 | [M1.5b] ComicScanner swallows platform failures | P1.2 |
| 68 | [M1.5b] census report misses the FileSystemEnumerable replacement | P0.4 |
| 69 | [M1.5b] a FileSystemWatcher per watch folder | P5.19a |
| 70 | [M1.5b] remap after load raises FileRenamed | closed (M2.1 importer rewrites XML); CLI: P5.28a |
| 71 | [M1.5b] Release medians on a quiet machine | P3.3 |
| 72 | [M1.4] PdfiumReaderEngine drift test; drop PDFiumSharpV2 | drift: P0.4; package dropped from the plan: the original `Pdfium.cs` still compiles against it and package parity is kept (item 5) |
| 73 | [M1.4] PDF passwords, render flags, large-PDF memory | P2.5 |
| 74 | [M1.4] bundle ships libpdfium | closed (M2.0b, M2.3a) |
| 75 | [M1.4] CBR unverified; real 7-Zip CB7 | C1, P0.7b |
| 76 | [M1.4] `fixtures make --formats cb7` fallback | P2.4a (uses the bundled `7zz`) |
| 77 | [M2.0a] native accept check (menu, Quit, Cmd+Q exit code) | P0.6b |
| 78 | [M2.0a] app name in the menu bar; single data folder | P0.6b |
| 79 | [M2.0a] CI headless smoke | P3.9 |
| 80 | [M2.0a] closing the window quits the app | DD7, P5.10b |
| 81 | [M2.0a] `Books.Count` on the UI thread | closed (cheap; rule §2.8) |
| 82 | [M2.0b] `disable-library-validation`, inside-out signing, loose DLLs | P6.2, P6.7 |
| 83 | [M2.0b] bundle id, GUI launch, icon | id: DD2, P6.1, C5; GUI launch: P0.6b; icon: P6.1, C5 |
| 84 | [M2.0b] crcli on R2R assemblies | closed (M2.3a) |
| 85 | [M2.0b] `make-app.sh` in CI | P6.4 |
| 86 | [M2.1] scan speed; parallel scan; books during scan | P3.3 (decide), P5.2 |
| 87 | [M2.1] pickers, import dialog, menu, Cmd+Q during scan unverified | P0.6b |
| 88 | [M2.1] import drops watch setting | P5.19a |
| 89 | [M2.1] CLI `PathRemap` duplicate | P5.28a |
| 90 | [M2.1] import preview | P5.28a |
| 91 | [M2.1] prefix mappings only, UNC untried | P5.28a |
| 92 | [M2.1] before-import backups never deleted | P5.28a |
| 93 | [M2.1] quit saves on the UI thread; failed save at quit | P5.10b |
| 94 | [M2.1] no "remove missing", no rescan | P5.19b |
| 95 | [M2.1] embedded ComicInfo read at scan unchecked | P2.4b (fixtures with ComicInfo), P5.9 |
| 96 | [M2.2a] wiring notes for M2.2b | closed (M2.2b) |
| 97 | [M2.2a] Core's eager thumbnail decode | P3.8 (conditional on P3.3) |
| 98 | [M2.2a] cover quality (`FastBilinear`) | P3.7 |
| 99 | [M2.2a] no invalidation API | P5.7c (`Invalidate(book)`), P5.20 (Refresh) |
| 100 | [M2.2a] request queue unbounded | dropped: the grid cancels on recycle and no planned consumer requests ahead of the viewport |
| 101 | [M2.2a] `ThumbnailService.Dispose` blocks up to 3 s | P7.3 (slow archives), P7.6 |
| 102 | [M2.2a] `SkiaInterop.GetSKBitmap` unused; reader ownership | closed (M2.3a) |
| 103 | [M2.2a] PixelBlock process-wide statics | closed (test design, no action) |
| 104 | [M2.2b] memory above bound plus baseline | P3.3, P3.7, DD9 |
| 105 | [M2.2b] 100,000 books unmeasured | P3.1, P7.2 |
| 106 | [M2.2b] wiring notes for M2.2c | closed (M2.2c) |
| 107 | [M2.2b] grid not refreshed during a scan | P5.2 |
| 108 | [M2.2b] search limits and natural sort | P5.14, P5.15a |
| 109 | [M2.2b] covers at most 512 px | P3.7 |
| 110 | [M2.2b] no context menu, multi-select, drag, accessibility | P5.5, P5.20, P5.3, P5.18, P7.5 |
| 111 | [M2.2b] review checklist unchanged | closed |
| 112 | [M2.2b] the Core's eager decode per thumbnail shows in the scroll numbers | P3.8 (conditional on P3.3) |
| 113 | [M2.2c] stale stored list results (owner note) | closed (documented; lists are recomputed) |
| 114 | [M2.2c] first evaluation cost at 100,000 books | P7.2 |
| 115 | [M2.2c] counts computed once per open | P5.2 |
| 116 | [M2.2c] folders containing script lists | P4.3 (script lists become computable) |
| 117 | [M2.2c] sidebar features missing | P5.16a, P5.16b, P5.4 (splitter) |
| 118 | [M2.2c] crcli remap after load changes stored results | P5.28a |
| 119 | [M2.3a] real input and Metal unverified | P0.6b (keys, menus, windows); gestures and feel: C2 |
| 120 | [M2.3a] seams for M2.3b | closed (M2.3b) |
| 121 | [M2.3a] reader features out of MVP scope | P5.23a, P5.23b, P5.23c, P5.23d, P5.23e |
| 122 | [M2.3a] read state not shown in grid and counts | P5.2 |
| 123 | [M2.3a] 48 MP cap, full-res zoom; EXIF/ICC ignored | zoom: P5.23e; EXIF/ICC dropped: GDI+ ignores them too, so the Windows app shows the same |
| 124 | [M2.3a] `PageService.Dispose` on the UI thread | P7.3 |
| 125 | [M2.3a] `RefreshInfoFromFile` regenerates covers once | closed (Core behaviour, documented) |
| 126 | [M2.3a] Core page cache under the reader | closed (no action) |
| 127 | [M2.3a] failed page retried every turn | P5.23a |
| 128 | [M2.3a] reader window state and fit not saved; closing quits | P5.1, P5.10b |
| 129 | [M2.3a] items closed by M2.3a | closed |
| 130 | [M2.3b] real pinch, pan, full screen, Metal unverified | P0.6b (full screen, keys, menus); pinch and pan feel: C2 |
| 131 | [M2.3b] reading mode not persisted | P5.1 |
| 132 | [M2.3b] Original/BestFit/FitWidthAdaptive, Continuous, overlap, FlipParts, ornaments, auto scroll, page filter | P5.23e (fit modes, page filter), P5.23d (auto scroll), P5.23f (continuous, overlap, FlipParts, and realistic page ornaments: reinstated by R9, ledger RK.18) |
| 133 | [M2.3b] swipe at the edge of a zoomed page | DD8, P5.23d |
| 134 | [M2.3b] spread parity for pages not yet decoded | P5.23e |
| 135 | [M2.3b] spread prefetch memory (`Ahead`) | P3.3 |
| 136 | [M2.4] overlay unverified on a real screen | P0.6b (shortcut, check mark, log); ProMotion FPS: C2 |
| 137 | [M2.4] overlay not persisted; passive FPS | dropped: a diagnostic tool; a passive FPS needs Avalonia internals |
| 138 | [M2.6] quarantine claim untested | P6.3 |
| 139 | [M2.6] SMB, CBR, TCC never tried | C1 (CBR), C2 and C6 (SMB, TCC), P7.3, P7.4 |
| 140 | [M2.6] acceptance numbers feed cache default and `Ahead` | P3.3, DD9 |
| 141 | [M2.6] "Report a Problem" menu item | P7.1 |
| — | Deferred MVP tasks: M1.5c, M1.7, M1.8, M2.5; gates G1, G2; spikes S3, S4, S6; P0.2/P0.3 (net48 tests) | M1.5c: P3.1, P3.2; M1.7: P3.9; M1.8: P3.3; M2.5: P5.0; G1 and G2: passed (owner, 2026-10-17); S3: P2.3a; S4: P4.1; S6: P4.2; net48 tests: replaced by P0.7a/b (DD6), optional P4.16 |

---

## 9. Discovered work (append-only)

_(Sessions append items here instead of doing them. Format: `- [<TASK-ID>] description`. The orchestrator turns items into tasks when they block v1, else they wait for post-v1.)_

- [P0.2] verify.sh was flaky in two of three runs on an unchanged code tree (docs-only change): run 1 had 12 failures in ComicRack.Mac.Tests (ReaderFlowTests "A MacHost is already open", CoreThumbnailDecoderTests), run 2 had 2 failures in Compat.Drawing.Tests (DisposeTests finalizer and live-image counters, which look process-global and sensitive to parallel test classes); run 3 passed. Make these tests isolated or serialized.
- [P0.6a] P0.6b `menu-structure` case: the System Events name of the app menu is `Avalonia Application` in the dev build and in the 2026-10-17 bundle, not `ComicRack CE (MVP)`; either the case reads the name through the bundle's `CFBundleName` check in Info.plist or the app sets its application name (AppBuilder `WithApplicationName`) so the menu bar item matches. Decide in P0.6b.
- [P0.6b] The app menu's `About` item is Avalonia's own (`About Avalonia`): `MainMenu.BuildAppMenu` adds `About ComicRack CE (MVP)` through `NativeMenu.SetMenu(Application, ...)` but it does not show in the app menu; P5.R1 (menu restructure) must make the About item show with the app name (the menu-structure case only requires exactly one item starting with `About `).
- [P0.6b] The Window-menu note of P0.6a is wrong: the AppKit `Enter Full Screen` (Fn+F) item appears in the View menu; P5.R1/P5.R3 still decide the final structure.
- [P0.6b] `Application.Name` is set in `App.axaml` to `ComicRack CE (MVP)`; P6.1 (bundle identity and name) must change it together with the `Product`, `APP_NAME` in `make-app.sh`, the titles in the `.axaml` windows and the menu-structure case (`APP_NAME` in `mac/tests/e2e/lib.sh`).
- [P0.6b] Shortcuts on digit keys depend on the keyboard layout (AZERTY shows `Cmd+É`); ledger rows for shortcuts compare against the US names, so the P5.T3 `ledger-check --menus` comparison must map layout characters or run on a US layout (the e2e cases use physical key codes).
- [P0.6a] The Window menu shows two "Enter Full Screen" items (AppKit's Fn+F one and the app's View > Enter Full Screen); P5.R1/P5.R3 decide on the final menu structure.

- [P1.2] Tripwire finding 1, `kernel32!CreateFile`: `NtfsInfoStorage.Load` -> `AlternateDataStreamFile.Exists` -> `AlternateDataStreamFile+NativeMethods.CreateFile`, called by `ComicProvider.LoadInfo` <- `ComicBook.RefreshInfoFromFile` <- `ComicScanner.OnProcessScannedFile` (once per scanned book; the DllNotFoundException is swallowed). P1.3: `EngineConfiguration.Default.DisableNTFS = true` in `MacHost.ConfigureCore`.
- [P1.2] Tripwire finding 2, `shlwapi!PathGetCharType`: `FileUtility+Native.PathGetCharType` <- `FileUtility.MakeValidFilename` <- `ComicProvider.IsSupportedImage` <- `ArchiveComicProvider.OnParse` <- `ImageProvider.Open` <- `ComicBook.RefreshInfoFromFile` <- `ComicScanner` (once per archive entry; `MakeValidFilename` catches the exception and falls back to `NetMakeValidFilename`, so the result is right but every entry of every scanned archive throws and catches a DllNotFoundException). P1.3: a patched copy of `FileUtility.cs` (pass B, manifest entry) that skips the P/Invoke on macOS.
- [P1.2] The tripwire does not watch non-system native names (libwebp, libheif, libjxl under `Resources\x64\`, `7z.exe`); the codec tasks of P2 (WebP, HEIF, AVIF, JXL) must add their own checks. Linked but not on the v1 flow tested by P1.2: `cYo.Common.Win32` shell, power, credentials, `FileExplorer`, `ShellRegister`, `Keyboard` code paths (P2.1 and P2.x decide each).
---
- [P1.3] Flaky under load (failed once in a full verify run, passed alone 3 times and on the rerun): `WindowTests.Pressing_Cancel_in_the_window_cancels_the_running_scan` (the progress panel check races with a 200-book scan that finishes early); make it deterministic.

- [P0.5c] The full gate of `RUNBOOK.md` §4 runs `verify.sh --full` and census B and then `upstream-sync-check.sh --no-merge`, which runs both again (about 4 minutes more). If the gate time matters, let the check reuse the results of the preceding commands (a `--reuse-logs` option) or run it only at wave ends.
- [P0.5c] The CI dry run of P3.10 must set `UPSTREAM_SYNC_ANY_BRANCH=1` (a runner checks out a detached or temporary branch) and needs `git fetch upstream` first; `--record` is not meant for CI.
- [P0.7a] Race in the Core: `ComicBook.GetIsoCulture` (`ComicRack.Engine/ComicBook.cs`, static `languages` Dictionary, no lock) is first used from PLINQ threads when a smart list with a `ComicBookLanguageMatcher` is evaluated over more than 100 books, so the first evaluation in a process can silently lose books (the exception is swallowed by `GetLanguageName`). Fix without editing the original: fill the dictionary on one thread in `MacHost.ConfigureCore` (loop over `CultureInfo.GetCultures(NeutralCultures)` and call `ComicBook.GetLanguageName`, as `CoreRaces.WarmLanguageNames` in the characterization tests does), then delete the workaround in the tests. Other static caches that are filled lazily from parallel matchers were not audited.
- [P0.7a] The characterization goldens run on net10 only; P3.10 (CI) should run them on every push, and the Windows CI job (P5.T5) could run the same sources on net48 to compare (`UPDATE_GOLDENS` stays off there).
- [P3.H] `CoverDrawOperation` draws the picture at the unsnapped, centred rectangle with linear and mipmap sampling, so a cover whose picture has the device size of its box is blurred when the rectangle starts on a fraction of a device pixel (1.5x: mean difference 32 of 255 against the picture; 1x, 2x and 3x are exact for the 2:3 test picture in a 160 point box, where the centring offset is a whole number of device pixels). Covers with other proportions have a fractional offset at every scale, so their edges are soft by up to half a pixel. P3.7 should snap the destination like `ReaderPainter.DrawPage` (same rounding, nearest filter at exactly 1:1) and add the 1.5x case to `A_cover_with_the_pixels_of_its_box_is_drawn_sharp_and_not_resampled`.
- [P3.H] `BookGridView` at a viewport of 760 points (900 x 520 and 1100 are fine): `Columns` reports 4 and the cells are arranged in 4 columns (the 4th ends at x = 840, outside the 760 point window), but the `ItemsRepeater` is measured 548 points wide (3 columns), so the 4th column is cut off and its cells receive no clicks (the hit test stops at the repeater's bounds). Probably the scrollbar width is taken in the measure but not in `Columns`/the arrange. Reproduce: `GridHarness(760, 520)`, 40 items, click cell 7. Fix in the P5.2 grid rework (or earlier if cheap); the HiDpi tests use 900 points.
- [P3.H] `CoverView` placeholder: the outline pen (1 point, grey alpha) is centred on the edge of the tile, so at 1x it is two half-intensity pixels (`#efefef #dedede` against `#ececec`), at 2x it is two pixels, at 1.5x soft. The tile edges are fractional (centred, aspect 2:3). Inset the tile by half the pen and snap it (P5.T4 or P3.7).
- [P3.H] Run `mac/tools/e2e.sh hidpi` on an unlocked screen once (the orchestrator or the owner) and record the ratio line in section 3. A real 1x run needs a non-Retina display or a scaled mode; the owner can check it in C2 together with "Retina sharpness".
- [P3.H] Icons: no icon is drawn yet (P5.T2). `Icons.Get` must add its 1x/2x test: the original has no high DPI variants and scales the 16 px images by 2 on Retina (chrome-guidelines section 5), so the test checks that the 16 point image occupies 32 device pixels at 2x with the high quality filter and no seam in toolbar strips.
- [P4.1] P4.3 adds the IronPython patch as a build step (copy of the S4 patch tool as `mac/tools/patch-ironpython`, run after restore on the `IronPython.dll` of the output and of `make-app.sh`; the Apache-2.0 licence text of IronPython in the bundle; a test that creates a new-style Python class fails when the patch is missing) and the `IronPython.StdLib` package (`Lib/` next to the host); the block of P4.3 lists them (RV4a moved them into what wave B can do). Without the patch no real plugin runs.
- [P4.1] The IronPython package copies a real `System.Drawing.Common.dll` 4.7.0 into the output; scripts that reference `System.Drawing` get its `Bitmap` (it throws on macOS) instead of the stand-in. P4.2/P4.10a must decide the identity (stand-in assemblies named `System.Windows.Forms` and `System.Drawing.Common`, or a `clr` wrapper) and remove or exclude the real assembly (`ExcludeAssets` on the transitive dependency).
- [P4.1] The shadow assemblies are named `ComicRack.Engine.Core`, `ComicRack.Plugins.Core`, `cYo.Common.Core`; scripts that call `clr.AddReferenceByPartialName('ComicRack.Engine')` (`RefreshView.py`) fail. Rename `AssemblyName` to the original names (types and namespaces stay) or map the names in the identity solution of P4.2; decide before P4.3 ships.
- [P4.1] The hardened bundle may not need `com.apple.security.cs.allow-unsigned-executable-memory` (the S4 checks pass without it); P6 can try dropping it once a notarized build exists.
- [P4.1] `ScriptSelfTest` (app) is a diagnostic: when P4.3 adds the real script host (`Host/Scripting/ScriptHost`), `--script-selftest` should reuse it instead of building its own `PluginEnvironment`.
- [P4.2] Run `mac/spikes/S6-modal/real.sh all` once on an unlocked screen (the orchestrator at a gate, or the owner) and copy the ui-drive shots to `mac/reports/s6-modal/`: it checks Avalonia.Native's modal behaviour (owner blocked, Return/Escape, the Python-drawn rectangle in a real window, shutdown during a dialog). A failure becomes a P4.9 fix (for example the facade disabling the owner chain itself).
- [P4.2] P4.8 applies the identity decision: `AssemblyName` and `AssemblyVersion` of `mac/cYo.Compat.Forms` (`System.Windows.Forms`, 10.0.0.0), `mac/cYo.Compat.Drawing` (`System.Drawing.Common`, 10.0.0.0) and of the three shadow projects (`ComicRack.Engine`, `ComicRack.Plugins`, `cYo.Common`); `ExcludeAssets="all"` for the transitive `System.Drawing.Common` 4.7.0; the tools and tests that name the assemblies (list in "Facts from P4.2"); the drift tests of `Compat.Forms.Tests` and `Compat.Drawing.Tests` must tell the reference pack and the shim apart by path. Also P4.7: the generated facade keeps the assembly name `System.Windows.Forms`.
- [P4.2] `RefreshView.py` (`FormUtility.FindActiveService[IRefreshDisplay]()`) needs an assembly named `cYo.Common.Windows` with `cYo.Common.Windows.Forms.FormUtility.FindActiveService<T>()` backed by the host (the UI layer is not linked); P4.3/P4.8 decide whether it is a small stand-in assembly or a replacement; the other bundled scripts do not need it.
- [P4.2] The host must install `Dispatcher.UIThread.UnhandledException` (`Handled = true`, log through `AppLog`) before any script UI runs: an exception escaping a UI job aborts the process (P4.5 or `App` start).
- [P4.2] P4.9 (peers): coalesce cross-thread property writes per control and property (a script loop that updates a label 100,000 times would queue 100,000 jobs); a `NumericUpDown` peer that shows its value at the original 65 point width; AutoSize for `CheckBox` and `RadioButton` must add the glyph width; `Form.AutoSize`, `AutoScaleMode.Font` and Anchor layout are not in the spike. P4.10: text, ellipse, clip and `Save`/`Restore` in the stand-in `Graphics` for owner-draw scripts.
- [P4.2] After P4.8 the S4 diagnostics `COMICRACK_SELFTEST_REWRITE_REFS` and `COMICRACK_SELFTEST_RESOLVER` in `mac/ComicRack.Mac/ScriptSelfTest.cs` and `mac/spikes/S4-ironpython/run.sh` (`patched-rewrite`, `patched-resolver`) are obsolete: remove them in P4.8.
- [RV4a] The first unattended loop run (owner, after installing the allowlist) should confirm that the scoped `Edit(/mac/**)` and `Write(/mac/**)` rules do not prompt; if the installed version reads the pattern differently, fix the one pattern in `mac/tools/claude-allowlist.json` (`AllowlistTests` keep the scope check).
- [RV4a] `crcli image stats --require-nonblank` is now variance based (threshold 2 of 255): a contact-sheet or e2e check that expects a near-uniform view (an empty list pane) must read the `deviation:` line of `stats` instead of using `--require-nonblank`.
- [P2.6] The language setting is only `--language` / `COMICRACK_MAC_LANGUAGE` for now; P5.1 (settings) stores `CultureName` as the original does (`ExtendedSettings.Language ?? Settings.CultureName`, `ComicRack/Program.cs:792`) and passes it to `Localization.Apply`; the language choice in Preferences (original Preferences, `Settings.CultureName`) needs a restart as in the original and offers `AvailableCultures` plus English.
- [P2.6] `crcli` and the tools that load the Core's `TR` find no `Languages/` (the entry assembly is `crcli`); only the app and the bundle have the payload. A task that needs translated Core texts in the CLI calls `Localization.Apply` with the app's `Languages` folder (and must register the code page provider).
- [P5.T1] `PreferencesLabelOverlays` (Preferences > Reader > Overlays label colour) has no token: light is Gainsboro, but the dark value `ThemeColors.Lossboro` (= `SystemColors.ControlDarkDark` in a static initialiser) is #5A5A5A or #696969 depending on whether the class is initialised before or after the dark palette is installed. P5.22c adds `Cr.PrefsOverlayLabel` once a reference screenshot (owner C1 (e) or P5.T5) shows which one the dark original draws.
- [P5.T1] The light control tokens (`Button*`, `CheckBox*`, `Edit*Frame`, `TrackBar*`, `TabPage*`) are the assumed Windows 10 values of chrome-guidelines §2.5 and ten dark control tokens have no original source (marked in `ComicRackTheme.Dark.axaml`); P5.T4 and the C2 contact sheet confirm or correct them, and `ThemeTokenTests.LightControlValues` must change with them.
- [P5.T3] Run `mac/tools/chrome-sheet.sh T3 MainWindow ChromeGallery` once on an unlocked screen (a gate or the owner): the launch, wait, shot and quit path of `shoot()` was only checked in pieces (title extraction from the log line, regex, argument checks, the locked path); the first real run may need small fixes (settle time before the shot, window title match).
- [P5.T3] Coverage of `ledger-check` sees Designer-declared items only; menus built in code (`MainForm.cs` workspaces, layouts, plugin and script entries, Open With, favourites) are covered in prose by their rows. P7.A1 to P7.A4 should compare the running app's menus (`--menus`) for those.
- [P5.T4] The restyled standard controls changed the MVP windows (control font 11, flat Windows 10 buttons and boxes); the MVP views keep their own layouts until P5.R1 to P5.R3, and their literal colours stay in the `NoLiteralColorsTests` exception list. `MessageWindow` is untouched: P5.R1 moves its callers to `MacAlert.ShowAsync` (the planned DL.34 work) and deletes it.
- [P5.T4] `ScrollViewer.AllowAutoHide` follows `NSScroller.preferredScrollerStyle` once at start; a change of "Show scroll bars" while the app runs is not picked up (observe `NSPreferredScrollerStyleDidChangeNotification` if the owner wants it).
- [RV5] The elements `Buttons`, `ComboBox` and `Menus` are listed in `ChromeElement` but have no Native variant: their original look is global overrides of the base theme (`CrControlResources.*` brush keys, `CrControls.axaml` styles, type-keyed themes of check box, radio button, tree item), which a per-control theme cannot undo. Making them switchable means splitting those files into per-variant style sets that `ChromeVariants` adds to or removes from `Application.Styles` and re-resolving the type-keyed themes of live controls. Needed before the owner can flip them; not scheduled (an owner decision at C2 if they want native buttons or combo boxes).
- [RV5] `Native` exists for the status bar only (proof); the other self-drawn elements (toolbar, tab bar, header, group box, side panel, tree) have an empty `Original` theme and draw in C#: a Native variant needs `OnChromeVariantChanged` branches or templates (`chrome-guidelines.md` section 10).
- [RV5] `e2e chrome-gallery` can fail a probe when the gallery window is scrolled: `ChromeGalleryWindow` focuses `TreeFocused` on `Opened`, which can scroll the window by its focus bring-into-view before the shot (2 of 6 runs once). Fix in the gallery (no focus on open, or scroll to the top before the probes), not in the case.
- [RV5] The `Cr.Mvp.*` entries and `cr-book-*`, `cr-reader*`, `cr-overlay*` classes of `CrMvpLook.axaml` keep the MVP values; P5.R2 and P5.R3 replace them with tokens and real cell and reader themes and delete them.
- [P5.T4] `CrToolBar` has no overflow chevron (the original's toolstrip moves items that do not fit to a drop-down); `CrTabBar` has no scroll arrows, no overflow list and no drag reorder (P5.32a); `CrSizableContainer` does not persist its extent (done by P5.1: `SidebarViewModel.Width`) or react to a double click on the grip (P5.R1). The unselected and hot tab fills and the check box, radio and slider colours are the assumed Windows 10 values (chrome-guidelines 2.5): the owner confirms them on the C2 sheet.
- [P5.T4] The tab control and the combo box popup come from the base theme with token colours only; the exact Windows 10 tab and combo look (border radius 0, hover tint) is checked on the C2 contact sheet, and `ListBoxItem`/`ComboBoxItem` hover colours use `Cr.ButtonHoverBack` and `Cr.Highlight`.
- [P5.T4] The popup menu selection uses the system accent (`SystemAccentColor`), not a `Cr` token: it is the "Mac selection" of C-MENU-3 and follows the user's accent colour. The menu bar itself stays the native menu (P5.R1).
- [P5.T4] `MacAlert.ShowNative` blocks the UI thread in AppKit's modal loop (`runModal`), so no Avalonia timer or render tick runs while an alert is open, and an alert from a background thread is not allowed (`ShowAsync` falls back to the window there). P5.R1 must call it from the UI thread only; long operations must not wait on it.
- [P5.R1] The reader window's menu (`ReaderMenu.cs`) still has the MVP names (File, View, Go) and its own fit shortcuts; P5.R3 restructures it with the original's Read and Display menus. The perf overlay item there already uses the new shortcut.
- [P5.R1] The side panel hides with the 100 ms slide but its tree stays reachable by Tab while collapsed (extent 0, clipped); P5.1 (persisting the panel) should also make a collapsed panel unfocusable (done by P5.1: `CrSizableContainer` hides its child from Tab once the slide ends).
- [P5.R1] The Arrange button's click on the button part opens the sort menu; the original flips ascending and descending there (P5.14 adds the direction and the original's column list). The tree toolbar (LT.02), the list history and view buttons of the browser toolbar (BT.02 to BT.07), Undo and Redo (BT.*) are not in the window yet: their tasks add them in the original positions.
- [P5.R1] Status text sizes use the current culture's decimal separator (`FileLengthFormat` as in the original), so on a comma locale the status shows `54,50 MB`.
- [P5.R2] Variable item width: the original sizes each thumbnail item by its cover (`GetThumbnailSizeSafe`: the cover scaled into 2H x H, the page size from `ComicPageInfo` before the thumbnail exists) and packs the rows by those widths; the Mac grid keeps uniform 2:3 cells, so a landscape cover is small inside its box. It needs a virtualizing layout with per-item sizes (rows from the estimated sizes) that keeps the M2.2b scroll budgets (C-LIST-2).
- [P5.R2] Click on the star strip under a cover sets the rating (`CoverViewItem` click region, `RatingRenderer.GetRatingFromStrip`, only when `Comic.EditMode.CanEditProperties()`); needs the selection and edit commands of P5.5 (My Rating). The front cover button ("- 1/2 +" under the cover of a selected book with several front covers, `DrawFrontCoverButton`) and `DrawCustomThumbnailOverlay` (scripts, P4.5) are not drawn yet.
- [P5.R2] `ThumbnailOptions` is persisted since P5.1 (`Bind`) and has no Preferences check boxes yet ( P5.22a shows `ShowToolTips`, `FadeInThumbnails`, `DogEarThumbnails`, `NumericRatingThumbnails`, `CoverThumbnailsSameSize`); `UpdateComicFiles` and `UpdateComicBookFiles` (the update state icon) should come from `MacSettings`, and `MarkerOf` from the reader state (P5.2: the open and last markers, and a refresh of a cell when its book changes: the cells read the book once at bind).
- [P5.R2] The cover thumbnail of a stacked item (the stack look: two rotated back covers) and the tile mode of `ThumbRenderer` (`ThumbTileRenderer`, with the tags and the text blocks) are P5.12 and P5.13b; the tool tip card reuses its text lines only.
- [P5.R2] The page behind the dog-ear is requested at the cover's pixel size and kept in a cell only while it is selected or hot; with many hot changes this asks the thumbnail service for page 2 thumbnails (not cached by the Core before): if scrolling feels slower with the option on, ask only after the pointer rests on a cell.
- [P5.R3] Shift+PageUp and Shift+PageDown (RK.09) turn by a spread in the two-page layouts: `SpreadLayout` pairs the book once from its first page, the original pairs from the page it is on; a pairing from an arbitrary page (`PagesFrom`) is needed for a true single page step (P5.23f).
- [P5.R3] Zoom In and Zoom Out keep the MVP's factor 1.25 per step (and `ReaderOptions.ZoomStep`); the original adds 0.1 and clamps 1 to 8 (menu items and Ctrl+wheel; Cmd+wheel here already adds 0.1). P5.23e aligns the step and adds the keys Z and Shift+Z.
- [P5.R3] The message overlay shows no book picture (`ComicBox3D.CreateDefaultBook` of the front cover) and the navigation overlay no battery indicator (`BatteryStatus`, laptops only); the page overlay ignores `CurrentPageShowsName` (the page file names under the number).
- [P5.R3] Left and Right scroll; the original also turns the page when scrolling hits the page edge (`ScrollingDoesBrowse`, PB.13) and after `PageChangeDelay` (PB.12): P5.23d. The two-finger tap that toggles the navigation overlay (RK.21) needs a native gesture hook (P5.23d); `ToggleNavigationOverlayCommand` exists.
- [P5.R3] The page menu's Info... (PC.01) raises `InfoRequested` and nothing handles it until the Info dialog (P5.7a); Quick Rating and Review... (PC.02) is P5.5. The Tools drop-down has only About and Exit.
- [P5.R3] The reader window keeps one tab; the tab's context menu, a second tab, the browser dock and "Reader in own Window" are P5.32a. `ReaderForm.xml` ("Reader") is not used: the window title is the book caption.
- [P5.R3] The navigation overlay's time label does not repaint by itself every minute (the original repaints only when the overlay redraws, as here); the page strip scroll by dragging is implemented but not covered by a test.
- [P5.1] Settings.xml keeps one frame for the reader windows (`ReaderWindow`); with the docked reader and tabs of P5.32a the reader window goes away and the frame of the main window carries it (P5.32a/b decide what of `ReaderWindow` stays). A zoomed (Maximized) library window is saved and restored as zoomed with the frame it returns to; that path (unit tested with a made-up state only) was not driven in a real window.
- [P5.1] The saved options have no Preferences check boxes yet (P5.22a, P5.22b, PR.xx rows); until then Settings.xml is edited by hand to change them. `ShowSplash`, `OpenLastFile` and the other options of the Behavior pane are saved with the original's defaults and act when their tasks (ledger column Mac task) are done.
- [P5.1] e2e flakiness: Screen Sharing (and other apps) take the focus between two ui-drive steps on this Mac; `lib.sh` retries `front`, but ui-drive `quit` makes one single attempt of its own (`do_front`), so a case can still end with "could not bring pid to the front". A retry loop inside `cmd_quit` would remove it.
- [P5.2] The live refresh keeps the tree's rows as they are (it counts the lists again but does not rebuild the tree): a list that is added, renamed or removed shows after the next full reload (scan end, import); the list editing tasks (P5.16a to c) must call `SidebarViewModel.ReloadAsync` or a tree refresh after each change.
- [P5.2] A book whose cover picture changes (custom thumbnail, a new cover after an edit) is redrawn with its new state but keeps the picture it has until the cell is rebound; the metadata editor (P5.7a) and the cover tasks should ask the `ThumbnailService` for the new picture of that book.
- [P5.2] The pass takes the evaluation lock of the sidebar's `ListEvaluator` per list, so a very large smart list that is not cached delays other lists' counters during a scan; the Core's cache makes later passes cheap. If a scan of a 100,000 book library feels slow, move the grid refresh behind the counts or lower the pass rate (P3.3).
- [P5.2] e2e: the "cover shows the reading marker" part of `refresh-after-read` is checked by the unit tests and by a screenshot only (the ribbon and the state icon are custom drawn, with no accessibility text); the owner sees it on the C2 sheet.

- [P5.3] Rubber band selection (drag a rectangle in the empty area, `ItemView.UpdateSelection`) and dragging the selection out (`OnItemDrag`) are not done; they belong with the drag and drop work (P5.16c) or a small follow-up task. Done by RV6.
- [P5.3] Parity question: with nothing focused the original's arrow keys move relative to the first item (Right selects the second book), the Mac grid selects the first book with the first movement key (the MVP behaviour, kept so as not to surprise). Owner may decide to copy the original. Decided by R9: copied, RV6.
- [P5.3] The selection model works on the shown list only; stacks (`IsStack`, `SelectedItems` expanding a stack to its books) arrive with P5.13a and must extend `SelectedBooks`.
- [RV6] The hot state (the stronger caption and the dog-ear) still follows the whole cell; the original's `UpdateHotItemState` uses the item hit test, so the 4 unit border and the free caption area are not hot there (`CrThumbnailItem.HitRegion` exists now). Small; with the next change to `CrThumbnailItem`.
- [RV6] P5.16c: Move effect and removal from an editable list after a drop (`itemView_ItemDrag` with `IEditableComicBookListProvider`), the grid as a drop target with the position marker, the drag cursor picture (`DragDropCursorAlpha`; Avalonia has no custom drag image) and `PB.30` (DisableDragDrop). `BookGridView.DragEffects` is Copy until then; read the books from `BookDrag.BooksFormat`.
- [RV6] A very large drag (the whole 14,706 book library) was not tried: the system makes one drag image per file. If it is slow, cap the file items (the books stay in the in-process container).
- [RV6] A secondary button drag neither starts a band nor drags books (the original does both for any button); on the Mac the secondary button is the context click. Listed in BV.11 and BV.12.

## 10. Estimates (low confidence)

| Phase | Tasks | Agent time per task (typical) | Agent hours |
|---|---|---|---|
| P0 | 12 | 1.5 to 2.5 h | 20 to 28 |
| P1 | 3 | 1.5 to 2 h | 5 to 6 |
| P2 | 9 | 1.5 to 2.5 h | 15 to 22 |
| P3 | 11 | 1 to 2 h | 12 to 20 |
| P4 | 36 | 2 to 3 h (xhigh tasks at the top) | 75 to 105 |
| P5 | 66 (52 + 14 added by R9; the existing blocks also grew: view specs, exact layouts, ledger) | 1.5 to 2.5 h | 105 to 175 |
| P6 | 7 | 1 to 2 h | 8 to 13 |
| P7 | 12 (8 + 4 parity audits) | 1.5 to 2.5 h | 18 to 32 |
| **Total** | **157** | | **about 260 to 400 h**, plus about 15 minutes of orchestrator verification per task (about 40 h) and retries (+15 %) |

R9 adds roughly 30 to 55 agent hours (the chrome foundation, the restyle, the main-window structure, workspaces, keyboard editor, tray, Finder integration and the audits) and makes every UI task somewhat longer.

Calendar: with the unattended loop running most of the day and night, about 8 to 12 tasks a day when nothing blocks, so **5 to 7 weeks of agent wall time**; with GUI-lock gaps (e2e tasks and contact sheets wait for an unlocked screen), retries, owner checkpoints (C2 to C6: about 4.5 to 6.5 hours of owner time in all, including two contact-sheet reviews of 10 to 15 minutes) and upstream merges, plan for **9 to 12 weeks calendar** to C6. The Script UI layer (wave E, 28 tasks) is the largest uncertainty: a NO-GO at P4.2 removes most of it from v1 and moves C3 forward.
- [P0.3] Mac-owned warnings left (52 lines in a clean build): NU1904 System.Drawing.Common 4.7.0 (Compat/Core package references), NU1510 redundant `Microsoft.CSharp` / `System.Text.Json` references in `ComicRack.Plugins.Core` and `cYo.Common.Core`, CS0618 `Bitmap.Save(string, int?)` in 5 test files, xUnit2013 (`BookGridTests.cs:328`), xUnit2031 (`FolderScannerTests.cs:57`).
