# ComicRack CE (MVP) for macOS: quick start

Apple Silicon only, macOS 14 or later. This is an MVP to judge performance and feel, not a release. It never writes to a Windows library: it works on its own copy.

## Launch

The app is `mac/artifacts/ComicRack CE (MVP).app` (166 MB, ad-hoc signed, not notarized).

- First launch: **right-click the app > Open > Open** (Gatekeeper). After that a double-click works.
- `xattr -d com.apple.quarantine` is not needed for a bundle built on this Mac (it carries no quarantine flag). Only a copy that came through a download or AirDrop needs it.
- Terminal: `open "mac/artifacts/ComicRack CE (MVP).app"`.
- Check the signature any time: `codesign --verify --deep --strict "mac/artifacts/ComicRack CE (MVP).app"` (prints nothing when fine).

## Open a library

**Import a Windows library** (File > Import Windows Library...). Use the fixture copy first:

1. Database file: Choose... `mac/fixtures/ComicDb.xml` (14,706 books with stand-in covers; `mac/fixtures/BD` holds the matching files).
2. Path mappings, one per line, `Windows prefix = folder on this Mac`. For the fixtures:
   `S:\media\BD\=/Users/<you>/dev/ComicRackCE/mac/fixtures/BD/`
   For your own library, one line per drive, for example `D:\Comics\=/Users/<you>/Comics/`.
3. Import. The file is copied into the app's data folder and the copy opens. Your source file is not changed. A library that was open is kept as `ComicDb.before-import-<date>.xml` in the data folder.

Watching of folders is switched off in the copy.

**Scan a folder** (File > Open Folder..., Cmd+O): pick a folder of CBZ, CBR, CB7, CBT or PDF. A panel shows the count and current file; Cancel is safe (books already added stay). Scanning the same folder again only adds new books. A first scan of thousands of books takes minutes.

## Where things live

| What | Where |
|---|---|
| Library (`ComicDb.xml`, `.bak`) | `~/Library/Application Support/ComicRack CE (MVP)/Data/` |
| Thumbnail and page caches | `~/Library/Caches/ComicRack CE (MVP)/` |
| Logs | `~/Library/Logs/ComicRack CE (MVP)/` |

The library is saved on quit (Cmd+Q). To start from scratch, quit and move the Application Support folder away. Send the log with any bug report; with the performance overlay on, a line with the numbers is logged every 10 seconds.

## Shortcuts

**Library window**

| Key | Action |
|---|---|
| Cmd+O | Open Folder (scan) |
| Enter, double-click | Open the book in the reader |
| Arrows, Home, End, Page Up/Down | Move the selection |
| Ctrl+Cmd+S | Show/hide the sidebar |
| Ctrl+Cmd+F | Full screen |
| Opt+Cmd+P | Performance overlay (FPS, decode and page-turn time, thumbnail queue, heap, footprint, RSS) |
| Cmd+M, Cmd+Q | Minimize, quit |
| Toolbar | Search box (words in title or series), sort (series, title, date added), cover size slider |

**Reader window**

| Key | Action |
|---|---|
| Right, Page Down / Left, Page Up | Next / previous page (swapped when reading right to left) |
| Space / Shift+Space | Scroll a tall page, then turn / the reverse |
| Home, End | First, last page |
| Up, Down | Scroll the page |
| Two-finger swipe left/right | One page per swipe |
| Cmd+2 / Cmd+3 / Cmd+5 | Best Fit / Fit Width / Fit Height |
| Opt+Cmd+1 / 2 / 3 | One page / Two pages / Two pages (adaptive) |
| Opt+Cmd+R | Read right to left |
| Cmd+= / Cmd+- / Cmd+0 | Zoom in / out / reset (also pinch) |
| Drag or two-finger scroll | Pan when zoomed |
| Ctrl+Cmd+F, Esc | Full screen in / out |
| Cmd+W, Esc | Close the reader (the page is remembered) |
| Opt+Cmd+P | Performance overlay |

## Known limitations

Not in the MVP (planned for the full version):

- Metadata editing and ComicInfo write-back, the smart-list editor (lists are shown read-only), export, the backup UI, preferences.
- Scripts and plugins: smart lists that need scripts show a "Requires scripts (not in MVP)" badge and an empty result.
- HEIF/AVIF/JXL pages, JPEG 2000 and TIFF pages do not decode; password-protected PDFs return no pages; CBR (RAR) was never tested with a real file.
- Developer ID signing and notarization (hence the right-click Open); localization of the new UI.
- Not signed for distribution: it needs a library-validation entitlement that a Developer ID signature will remove.

Also known (from the open items in the track plan):

- Only prefix path mappings in the import, no preview of how many books will be found.
- The import drops Windows watch folders; there is no folder watching at all, and no "remove missing books".
- The grid and sidebar counts are not refreshed after reading or scanning until the library is reloaded; the grid shows no read progress.
- No context menus, multi-select, drag and drop, or list editing in the grid and sidebar.
- Search is words in title or series only; sorting is not natural order inside titles ("Tome 10" before "Tome 2").
- Covers are at most 512 px high (soft at the largest size on Retina).
- Reader: no click-to-turn, go to page, bookmarks, page thumbnails, rotation, colour adjustment, continuous layout or double-page overlap; the fit, layout and reading direction are not saved between launches. A swipe on a zoomed page pans instead of turning.
- Closing the library window quits the app. Quit saves synchronously (about 1 s for 14,706 books).
- The Dock icon is soft at large sizes (the source image is 256 px). The bundle id `com.comicrackce.mvp` is a placeholder.
- Everything involving a real screen (native menus, Metal drawing, trackpad swipe and pinch, native full screen) was tested only with a headless platform before this hand-over: use `mac/plans/mvp-acceptance.md` to check it.

Deferred tasks: M1.5c (`crcli db synth`), M1.7 (CI), M1.8 (Release benchmarks), M2.5 (polish after your notes), and the gates G1/G2.

## Rebuild

```bash
mac/tools/make-app.sh          # about 3 minutes; needs the .NET 10 SDK; stops if less than 3 GB is free
codesign --verify --deep --strict "mac/artifacts/ComicRack CE (MVP).app"
T=$(mktemp -d); "mac/artifacts/ComicRack CE (MVP).app/Contents/MacOS/ComicRack.Mac" --smoke-test=2 --headless --data-dir "$T"; echo $?   # 0
```

Run from source: `dotnet run --project mac/ComicRack.Mac`. Tests: `dotnet test mac/tests/ComicRack.Mac.Tests` (about 3 minutes).
Add `--read-book FILE` to the smoke command to check that a book opens (exit 1 if page 1 or 2 does not show).

The acceptance checklist is `mac/plans/mvp-acceptance.md`.
