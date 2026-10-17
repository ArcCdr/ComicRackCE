# MVP acceptance pack (M2.6)

For the owner, on an **unlocked** Mac with a trackpad, using the signed bundle `mac/artifacts/ComicRack CE (MVP).app` (rebuild: `mac/tools/make-app.sh`). This is the input to gate **G2** (`mac/plans/mvp-track.md` §5): are UX and performance good enough to commit to the full plan?

How to fill it in: write the measured number in **Value** and `OK` / `Issue` / `Fail` in **Verdict** (plus a few words if not OK). Items marked "(was headless only)" were verified by tests and PNG frames only; this is their first look on a real screen. Send back the log (`~/Library/Logs/ComicRack CE (MVP)/`) with any Issue.

Test data: `mac/fixtures/BD` (14,706 stand-in CBZ, 800 px pages), `mac/fixtures/ComicDb.xml` (Windows DB, `S:\media\BD\...` paths), `mac/fixtures/pdf`, `mac/fixtures/cb7`, `mac/fixtures/BD-reader` (24-page books). Real books of yours (CBR especially, no CBR was ever tested) are worth adding in sections 3 and 5.

## 0. Setup

| # | Step | Value | Verdict |
|---|---|---|---|
| 0.1 | Close any running copy. Quit Activity Monitor filters. Note the machine: model, RAM, macOS version, power (battery or plugged in) | | |
| 0.2 | Fresh data: `mv ~/Library/Application\ Support/ComicRack\ CE\ \(MVP\) ~/crmvp-data-backup` (only if the folder exists; skip if you want to keep a library) | | |
| 0.3 | Open `mac/artifacts/ComicRack CE (MVP).app` once with right-click > Open (Gatekeeper, see MVP-QUICKSTART.md) | Dialog shown? Y/N | |

## 1. Launch, menu, Dock, bundle (first real window; was headless only)

| # | Exact steps | Expect | Value | Verdict |
|---|---|---|---|---|
| 1.1 | Quit everything. Start a stopwatch, double-click the app (empty data folder), stop when the window is usable | Window within a few seconds | cold launch: ___ s | |
| 1.2 | Quit (Cmd+Q), launch again with the 14,706-book library loaded (after 2.2) | Status bar shows the count; window usable at once, library opens in the background | warm launch to window: ___ s; to "14,706 books" in the status bar: ___ s | |
| 1.3 | Look at the menu bar with the library window in front | App name next to the Apple menu is "ComicRack CE (MVP)" | name shown: ___ | |
| 1.4 | Open the app menu | About, Services, Hide, Hide Others, Show All and **Quit appear exactly once** (Avalonia adds Quit itself) | Quit count: ___ | |
| 1.5 | Open File, View, Window, Help | File: Open Folder... (Cmd+O), Import Windows Library...; View: Show Sidebar (Ctrl+Cmd+S), Enter Full Screen (Ctrl+Cmd+F), Show Performance Overlay (Opt+Cmd+P); Window: Minimize (Cmd+M), Zoom; Help | matches? | |
| 1.6 | Look at the Dock icon and Cmd+Tab | The ComicRack icon (soft at large sizes, known) | | |
| 1.7 | Terminal: `/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "mac/artifacts/ComicRack CE (MVP).app/Contents/Info.plist"` | `com.comicrackce.mvp` (placeholder). Do you want another id before wider use? | id wanted: ___ | |
| 1.8 | After the first launch: `ls ~/Library/Application\ Support/ \| grep -i comicrack` | Exactly one folder, `ComicRack CE (MVP)`; none named `ComicRack.Mac` | folders: ___ | |
| 1.9 | Cmd+Q from the library window; check that the app is gone from the Dock and `~/Library/Application Support/ComicRack CE (MVP)/Data/ComicDb.xml` has a new modification time (`ls -l`) | Quit once, app exits, DB saved | mtime updated: Y/N | |
| 1.10 | Dark and light: System Settings > Appearance, switch while the app runs, look at the grid, sidebar, status strip and the Import dialog | Follows the system | | |

## 2. Opening a library

### 2A. Import Windows Library (use the fixture copy; the original is never touched)

| # | Exact steps | Expect | Value | Verdict |
|---|---|---|---|---|
| 2.1 | File > Import Windows Library... The sheet opens. Click "Choose...". The native file picker shows; select `mac/fixtures/ComicDb.xml` | Picker is a real macOS sheet; the path appears in the box | picker OK? | |
| 2.2 | In the mapping box type one line: `S:\media\BD\=/Users/<you>/dev/ComicRackCE/mac/fixtures/BD/` (adjust the path). Click Import | Takes a few seconds; the library becomes **14,706 books**, covers appear | time: ___ s; count: ___ | |
| 2.3 | Click Import with an empty mapping box on a fresh run (optional) | A warning that no book was found, or books that cannot open | message: ___ | |
| 2.4 | Quit and relaunch | The same 14,706 books | count: ___ | |
| 2.5 | `shasum -a 256 mac/fixtures/ComicDb.xml` before and after the whole session | Identical (the source is never changed) | same? | |

### 2B. Open Folder (scan)

| # | Exact steps | Expect | Value | Verdict |
|---|---|---|---|---|
| 2.6 | Fresh data folder (0.2). File > Open Folder... (Cmd+O). The native folder picker shows; choose `mac/fixtures/small` | Progress panel with count and current file; books appear | picker OK? | |
| 2.7 | Scan N books **local**: pick `mac/fixtures/BD-reader` (or your own folder). Time it from OK to "done" | | N = ___ books, ___ s, ___ ms/book | |
| 2.8 | Scan N books **on SMB**: mount a share (Finder > Go > Connect to Server, `smb://...`) with at least 200 books, scan it | Works, no hang, UI stays responsive | N = ___, ___ s, ___ ms/book; slower than local by ___x | |
| 2.9 | During a scan of the big folder (`mac/fixtures/BD`, 14,706 books; expect minutes) click Cancel | The window stays responsive; the books already added stay; no crash | cancel to responsive: ___ s | |
| 2.10 | Scan the same folder again | Only the missing books are added (fast for known ones) | | |
| 2.11 | Cmd+Q **during** a scan | App quits; on relaunch the count equals what was added; no "not saved" message | | |

## 3. Permission prompts (TCC)

The app is not sandboxed and uses only user-chosen folders. Record every system prompt that appears.

| # | Exact steps | Expect | Value | Verdict |
|---|---|---|---|---|
| 3.1 | Scan a folder under `~/Documents`, then `~/Downloads`, then `~/Desktop` (first access of each) | Possibly one "would like to access files in your X folder" prompt each. Note it, allow it | prompts seen: ___ | |
| 3.2 | Scan a folder on an external disk or a network volume | Possibly "removable volume" / "network volume" prompt | prompts seen: ___ | |
| 3.3 | After allowing, quit, relaunch, scan again | No prompt again | | |
| 3.4 | Deny one prompt once (System Settings > Privacy & Security > Files and Folders) and scan that folder | The scan finds nothing or shows an error and the app does not crash | behaviour: ___ | |
| 3.5 | Any prompt for camera, microphone, contacts, Full Disk Access or Accessibility | None expected | | |

## 4. Grid, sidebar, search (real window and Metal; was headless only)

Load the 14,706-book library (2.2).

| # | Exact steps | Expect | Value | Verdict |
|---|---|---|---|---|
| 4.1 | Scroll slowly, then fast, with the trackpad (inertia) from top to bottom | No visible hitches; covers fill in as you stop; no blank cells that stay blank | feel: smooth / stutters at ___ | |
| 4.2 | Turn on the performance overlay (Opt+Cmd+P) and scroll fast | FPS stays near the display rate (60 or 120) | min FPS seen: ___; thumbnail queue peak: ___ | |
| 4.3 | Same with a mouse wheel (if available) | | | |
| 4.4 | Activity Monitor (Memory tab), search "ComicRack": note Memory (footprint) and Real Mem **at idle after loading the library** | | idle: ___ MB | |
| 4.5 | Scroll the whole library once from top to end, then note Memory again | The covers' LRU is bounded (256 MB); the headless measure was +200 to +230 MB above baseline plus the bound | after full scroll: ___ MB; peak: ___ MB | |
| 4.6 | Wait 2 minutes idle, note Memory | Does not keep growing | idle after: ___ MB | |
| 4.7 | Move the thumbnail-size slider from smallest to largest, drag and release | Smooth; covers re-sharpen after release; Retina covers look crisp (covers are served at 512 px max; at the largest size they are upscaled 1.4x) | sharpness: ___ | |
| 4.8 | Sort by Series, Title, Date added | Order changes at once (tens of ms) | | |
| 4.9 | Type "tome" or part of a series in the search box; clear it | Filters live; accents ignored | | |
| 4.10 | Click a book, press Enter; double-click another | Reader opens (section 5) | double-click interval OK? | |
| 4.11 | Arrow keys, Home, End, Page Up/Down in the grid | Selection moves | | |
| 4.12 | Show/Hide Sidebar from View (Ctrl+Cmd+S): check the menu check mark and the shortcut | Sidebar toggles; check mark follows | | |
| 4.13 | Sidebar: expand and collapse folders, click lists and smart lists, arrow-key navigation in the tree | Counts shown; grid filters; lists "Requires scripts (not in MVP)" show a badge and an empty grid, no crash | | |
| 4.14 | Look and feel questions (section 8) | | | |
| 4.15 | Resize the window down to the minimum (640 wide) | Toolbar still usable | | |
| 4.16 | Optional: a 100,000-book library does not exist (`db synth` was deferred); skip or note | | skipped | |

## 5. Reader: page turns, layouts, gestures, full screen

Open a book from the library (for the fixtures: `Fixture Series 01 001.cbz` has 2 to 6 MP pages, `BD-reader` books 24 pages). Turn on the performance overlay if useful; the log has a `Page turn ...` line for each turn.

| # | Exact steps | Expect | Value | Verdict |
|---|---|---|---|---|
| 5.1 | Double-click a book | A dark reader window opens and **takes the keys at once** (no extra click) | opens in ___ ms; focus OK? | |
| 5.2 | Right / Left / Page Down / Page Up, Home, End, Space, Shift+Space | Pages turn; Space scrolls a tall page then turns | | |
| 5.3 | Hold the Right arrow (key repeat) through 20 pages | Pages keep up, memory stable, no stuck "Loading page" | pages/s: ___ | |
| 5.4 | Page-turn feel with prefetch (turn one page at a time) | Instant (headless draw: 7.5 ms median) | feel: ___; overlay "page turn" ms: ___; log median: ___ ms | |
| 5.5 | Jump (End, then Home) | Brief old page, then the new one; "Loading page N..." only after 250 ms | decode ms on overlay: ___ | |
| 5.6 | **Two-finger horizontal swipe**: one deliberate swipe to the left | **Exactly one page forward**, not none, not several (fingers moving left = next) | pages turned per swipe: ___ ; direction OK? | |
| 5.7 | Swipe fast and hard (long momentum) | Still one page | | |
| 5.8 | Swipe right | One page back | | |
| 5.9 | Two-finger vertical scroll on a Fit Width page | Scrolls the page | | |
| 5.10 | Fit modes: Cmd+2 (Best Fit), Cmd+3 (Fit Width), Cmd+5 (Fit Height) from the View menu and the keys | Menu radio follows; page refits | | |
| 5.11 | Layout: Opt+Cmd+1 (One Page), Opt+Cmd+2 (Two Pages), Opt+Cmd+3 (Adaptive) | The cover is alone on the right half; wide pages fill the spread | | |
| 5.12 | Read Right to Left: Opt+Cmd+R, then arrows | Left becomes next; the swipe direction flips; spread order swaps | | |
| 5.13 | **Pinch** on the trackpad: spread fingers | **Zooms in** at the pointer; the speed is usable (if too fast or slow the constant is `Delta.X` in `ReaderView.OnMagnify`) | speed: too fast / right / too slow | |
| 5.14 | Cmd+= , Cmd+-, Cmd+0 (zoom in, out, reset) | Steps of x1.25; Cmd+0 resets zoom and scroll | | |
| 5.15 | Zoomed in: two-finger scroll and mouse drag | Page pans both ways; the swipe does **not** turn pages while the page overflows sideways | wheel step OK (50)? | |
| 5.16 | **Full screen**: Ctrl+Cmd+F (and the green button); then Esc | Native transition to its own Space; status strip hidden; the menu bar shows on mouse to the top; Esc leaves full screen first, a second Esc closes the reader | transition OK? window restores its size? | |
| 5.17 | Sharpness on Retina: Best Fit on a 2 to 6 MP page, then zoom 200% | Crisp text, no shimmer when pinching | | |
| 5.18 | Metal rendering: window resize while a page is shown; move the reader between two displays if you have them | No tearing, no black frames | | |
| 5.19 | Cmd+W and Esc close the reader; reopen the book | Resumes on the same page (the page is recorded at each turn) | | |
| 5.20 | Open a PDF (`mac/fixtures/pdf`) and a CB7 (`mac/fixtures/cb7`) from the library in the real bundle window | Both render | | |
| 5.21 | Open a damaged or missing file (rename a book on disk, then open it) | Notice "could not be opened", no crash | | |
| 5.22 | Open a real CBR of yours (never tested) | Opens, or a clear failure | result: ___ | |
| 5.23 | Reader menu bar: File > Close Reader, View, Go | Items present, shortcuts as listed | | |

## 6. Performance overlay (Opt+Cmd+P)

| # | Exact steps | Expect | Value | Verdict |
|---|---|---|---|---|
| 6.1 | Press Opt+Cmd+P in the library window | Panel top right: FPS, decode, page turn, thumbnails queue, heap, footprint, RSS; menu check mark on | | |
| 6.2 | Press it again | Panel off, check mark off | | |
| 6.3 | Turn it on, open a reader | Shown in the reader too (same switch) | | |
| 6.4 | In full screen (5.16) | Still visible and arrows still turn pages | | |
| 6.5 | `tail -f ~/Library/Logs/ComicRack\ CE\ \(MVP\)/*.log` while visible | One line about every 10 s; none while hidden | | |
| 6.6 | Compare overlay Footprint with Activity Monitor Memory | Same order of magnitude | overlay ___ MB vs AM ___ MB | |
| 6.7 | Idle FPS | 60 or 120 when idle (the overlay itself keeps frames flowing) | ___ | |

## 7. Quit, persistence, robustness

| # | Exact steps | Expect | Value | Verdict |
|---|---|---|---|---|
| 7.1 | Read to page 10 of a book, close it, quit, relaunch, reopen | Page 10 | | |
| 7.2 | Force-quit (Activity Monitor) mid-session, relaunch | Library opens from the last save or the `.bak`; note what is lost | | |
| 7.3 | Disk: `du -sh ~/Library/Caches/ComicRack\ CE\ \(MVP\)` after the sessions | Core caches up to 500 MB | ___ MB | |
| 7.4 | Run the shipped bundle's headless smoke (also in MVP-QUICKSTART.md) | exit 0 | | |

## 8. UX questions (answer in your own words)

| # | Question | Answer / verdict |
|---|---|---|
| 8.1 | Does the grid feel native to macOS (selection look, spacing, scroll inertia, covers, text)? What is off? | |
| 8.2 | Does the sidebar feel native (row height, selection colour, disclosure triangles, counts)? | |
| 8.3 | Is the toolbar (search, sort, size slider) where you expect it? Missing: unified toolbar, Cmd+F focus, view options? | |
| 8.4 | Default reader layout: single page, or two-page spread for landscape screens? Default fit: Best Fit or Fit Width? | |
| 8.5 | Cover alone in spreads, and wide pages across the spread: right? | |
| 8.6 | Swipe: threshold and one-swipe-one-page feel; should a swipe at the edge of a zoomed page turn the page (WinForms does)? | |
| 8.7 | Right to left: per-book flag only, or a remembered preference? | |
| 8.8 | Should closing the library window quit the app (current), or keep it running like most Mac apps? | |
| 8.9 | Is the performance overlay's information what you need? Anything to add? | |
| 8.10 | Memory after browsing: acceptable on this 16 GB Mac? Should the cover cache default drop from 256 MB to 128 MB? | |
| 8.11 | Cover quality: do the covers look noisy (the Core reduces with a bilinear filter)? Too small at the largest size? | |
| 8.12 | Anything that would stop you using it daily? List top 5, in order. This feeds M2.5 | |

## 9. Gate G2 summary (owner)

| Question | Answer |
|---|---|
| Launch and scan speed acceptable? | |
| Grid scroll and memory acceptable (14,706 books)? | |
| Reader page-turn feel and gestures acceptable? | |
| Any blocker for continuing to the full plan? | |
| Decision: continue / continue after M2.5 / change approach | |
