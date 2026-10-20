# Owner actions

## How this works

- This is the only file where the macOS port sessions ask you for something. They work unattended otherwise (`mac/plans/RUNBOOK.md`) and stop only at a checkpoint (`mac/plans/full-port.md` §4) or a STOP condition.
- Each request is one block in **Inbox**: what to do (exact steps), what you should see, how long it takes, what happens if you skip it, and how to resume.
- To answer: write under "Your answer", change `Status: OPEN` to `Status: ANSWERED`, save, then paste the resume prompt. The next session applies your answer and moves the block to **Done**.
- **Required** blocks stop some work until answered (the block says which). **Optional** blocks never stop anything: the sessions use the stated default.
- Run every session from the **VS Code integrated terminal** in `/Users/vince/dev/ComicRackCE`: Screen Recording and Accessibility are granted to VS Code, so the sessions can drive and screenshot the app from there.
- The resume prompt (also in `mac/plans/RUNBOOK.md` §1):
  ```bash
  claude --model sonnet --effort high "You are the orchestrator of the ComicRack CE macOS port in /Users/vince/dev/ComicRackCE, branch macos. Read mac/plans/RUNBOOK.md completely and follow it exactly: apply the owner's answers in mac/plans/OWNER-ACTIONS.md, then run the tasks of mac/plans/full-port.md one at a time, each in a fresh subagent, verify and commit each, until a checkpoint or a STOP condition. Never push, merge or switch branches."
  ```
- Merging upstream is a recurring action of yours, described in `mac/UPSTREAM-SYNC.md`, not here.

## Inbox

**Required now: none.** The five optional blocks below belong to checkpoint C1. Answer any of them whenever convenient; nothing waits for them.

### OA-1: Real CBR and 7-Zip samples (optional, C1)
- **Status:** OPEN
- **Checkpoint / tasks:** C1 / P0.7b, P2.4b · **Asked:** 2026-10-17 · **Blocks:** nothing; sessions use the default
- **Why now:** no `.cbr` file was ever opened by the Mac app, and every `.cb7` so far was written by SharpCompress, not by real 7-Zip (solid archives, BCJ filters). Tests for both are written early and skip until samples exist.
- **What to do:**
  1. `mkdir -p /Users/vince/dev/ComicRackCE/mac/fixtures/owner` (the folder is git-ignored; nothing in it is committed).
  2. Copy one or two `.cbr` books (ideally one RAR4 and one RAR5) and, if you have one, a `.cb7` made with 7-Zip into that folder. Small books are best (under 100 MB together).
- **What you should see:** the files in `mac/fixtures/owner/`. Nothing else changes.
- **Time needed:** about 5 minutes
- **If you skip it:** the CBR and real-7z tests stay skipped and are listed as unverified; you check one CBR yourself at C2.
- **What happens next:** P0.7b records page counts and hashes of these books as goldens; P2.4b checks what a ComicInfo write does with a CBR.
- **Your answer** (write below this line, then change Status to ANSWERED):
  >
- **Resume:** paste the resume prompt above.

### OA-2: The plugins you use (optional, C1)
- **Status:** OPEN
- **Checkpoint / tasks:** C1 / P4.6b, P4.15 · **Asked:** 2026-10-17 · **Blocks:** nothing; sessions use the default
- **Why now:** the Script UI layer is sized by which WinForms members real scripts call. Your own plugins decide the "must work" list.
- **What to do:**
  1. Write the names of the plugins you use under "Your answer" (for example Comic Vine Scraper CE, Data Manager, ...), most important first.
  2. Optional: copy your Windows Scripts folder (`%APPDATA%\cYo\ComicRack Community Edition\Scripts`) to `/Users/vince/dev/ComicRackCE/mac/fixtures/owner/scripts/` (git-ignored; only scan results are committed).
- **What you should see:** nothing changes until P4.6b runs.
- **Time needed:** about 5 to 10 minutes
- **If you skip it:** the corpus is the bundled sample scripts plus public plugins that can be downloaded (FrederikBaerentsen/ComicRack_Scripts, Comic Vine Scraper CE, Data Manager, CVIssueCount, maforget's AmazonScrapper).
- **What happens next:** P4.6b ranks the WinForms usage of the corpus; P4.15 builds your plugin checklist for C4.
- **Your answer** (write below this line, then change Status to ANSWERED):
  >
- **Resume:** paste the resume prompt above.

### OA-3: A copy of your Windows `Config.xml` (optional, C1)
- **Status:** OPEN
- **Checkpoint / tasks:** C1 / P5.28b · **Asked:** 2026-10-17 · **Blocks:** nothing; sessions use the default
- **Why now:** P5.28b imports the portable settings of the Windows app (reader and library defaults, external programs). A real file catches what a synthetic one misses.
- **What to do:**
  1. Copy `%APPDATA%\cYo\ComicRack Community Edition\Config.xml` from Windows to `/Users/vince/dev/ComicRackCE/mac/fixtures/owner/Config.xml` (git-ignored).
- **What you should see:** the file in that folder.
- **Time needed:** about 3 minutes
- **If you skip it:** P5.28b is tested on a synthetic `Config.xml` written from the Windows `Settings` class's defaults.
- **What happens next:** P5.28b uses it as a test input; the file is never modified.
- **Your answer** (write below this line, then change Status to ANSWERED):
  >
- **Resume:** paste the resume prompt above.

### OA-4: Your notes from the MVP (optional, C1)
- **Status:** OPEN
- **Checkpoint / tasks:** C1 / P5.0 · **Asked:** 2026-10-17 · **Blocks:** nothing; sessions use the default
- **Why now:** you judged the MVP's performance very good; if anything else bothered you (the "top 5" of `mac/plans/mvp-acceptance.md` §8.12, or verdicts there), P5.0 turns it into tasks at the start of wave B.
- **What to do:**
  1. Either fill in the Value/Verdict columns you care about in `mac/plans/mvp-acceptance.md`, or write a short list under "Your answer".
- **What you should see:** nothing changes until P5.0 runs.
- **Time needed:** about 5 to 15 minutes
- **If you skip it:** P5.0 is skipped; the defaults of `mac/plans/full-port.md` §1.2 apply (for example: closing the library window keeps the app running; reader defaults as on Windows).
- **What happens next:** each issue becomes a task `P5.0.<n>` early in wave B.
- **Your answer** (write below this line, then change Status to ANSWERED):
  >
- **Resume:** paste the resume prompt above.

### OA-5: Reference screenshots of the Windows app (optional, C1 (e))
- **Status:** OPEN
- **Checkpoint / tasks:** C1 / P5.T3, P5.T4, P5.R1 to P5.R3 and every later UI task (contact sheets) · **Asked:** 2026-10-20 · **Blocks:** nothing; sessions use the default
- **Why now:** the repository has no screenshot of the Windows app. The contact sheets (`mac/tools/chrome-sheet.sh`) compare each Mac view with a reference; without your screenshot the reference is a card drawn from the original source (layout outline, colour swatches, icons), which shows the structure but not the real look.
- **What to do:**
  1. On Windows, in the light theme, take a screenshot of each view below (a window or dialog alone, not the whole screen). If it is easy, also run `ComicRack.exe -dark` and take the same ones again.
  2. Save them as PNG files in `/Users/vince/dev/ComicRackCE/mac/fixtures/owner/reference/` (git-ignored; create the folder), named `<View>.png`, and `<View>-dark.png` for the dark ones:
     - `MainWindow` (main window with the browser showing covers and the side panel), `MainWindow-Reader` (a book open, with the navigation overlay shown), `MainWindow-BrowserContextMenu` (right click on a book in the browser)
     - `ComicBookDialog-Summary`, `ComicBookDialog-Details`, `ComicBookDialog-PlotNotes`, `ComicBookDialog-Catalog`, `ComicBookDialog-Custom`, `ComicBookDialog-Pages`, `ComicBookDialog-Colors` (the Info dialog, each tab)
     - `PreferencesDialog-Reader`, `PreferencesDialog-Libraries`, `PreferencesDialog-Behavior`, `PreferencesDialog-Scripts`, `PreferencesDialog-Advanced` (each tab)
     - `SmartListDialog` and `SmartListQueryDialog` (Edit Smart List, both dialogs), `ExportComicsDialog` (Export Books), `ComicDisplaySettingsDialog` (Book Display Settings), `ListLayoutDialog` (List Options)
  3. Any subset is fine; a missing file only means that row keeps its reference card.
- **What you should see:** the PNG files in that folder. Nothing else changes until a UI task makes a contact sheet; the sheet then shows your screenshot in its first column.
- **Time needed:** about 15 to 30 minutes (half of that without the dark ones)
- **If you skip it:** the first column of every contact sheet is the reference card; the look verdicts at C2 and C6 are then made against the cards and your memory of the Windows app. The optional CI job P5.T5 can produce the same images later.
- **What happens next:** `mac/tools/chrome-sheet.sh` copies each file into the sheet next to the Mac light and dark shots.
- **Your answer** (write below this line, then change Status to ANSWERED):
  >
- **Resume:** paste the resume prompt above.

## Collected for later checkpoints

_(Owner-only checks gathered from finished tasks; they become one block when their checkpoint is reached.)_

- **C2:** trackpad two-finger swipe (one swipe = one page, direction, momentum), pinch zoom speed, scroll feel, Retina sharpness, Metal frame rate, an SMB share scan, TCC prompts (Documents, Downloads, Desktop, removable and network volumes), one real CBR, one push of `macos` to see the CI run; the real-window checks that were SKIPPED on a locked screen: `mac/tools/e2e.sh hidpi` (the 1x/2x ratio line; a real 1x run needs a non-Retina display or a scaled mode) and `mac/spikes/S6-modal/real.sh all` (copy its shots to `mac/reports/s6-modal/`; owner blocked, Return/Escape, the Python-drawn rectangle in a real window, shutdown during a dialog). Any session may run both on an unlocked screen earlier, at a gate.
- **C5:** Developer ID certificate, notarization profile, final bundle id and name, optional 1024 px icon, optional GitHub secrets.

## Done

_(Answered blocks move here, newest last.)_
