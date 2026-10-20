# ComicRack CE for macOS (work in progress)

Plans and procedures:

- `mac/plans/full-port.md`: the master plan for the rest of the port (status, decisions, rules, checkpoints, every task). Start here.
- `mac/plans/RUNBOOK.md`: how an orchestrating session runs the plan unattended; the resume prompts.
- `mac/plans/OWNER-ACTIONS.md`: everything the owner is asked to do or decide.
- `mac/UPSTREAM-SYNC.md`: how to merge `upstream/master` into `macos` and check the Mac build afterwards.
- `MACOS_NATIVE_PATH.md` (repository root): decisions, architecture, scope. `mac/plans/mvp-track.md`: the record of the MVP (M0 to M2.6) and its facts.

Fast gate (every change): `mac/tools/verify.sh` (disk guard, no file outside `mac/` changed, build, fast tests; `--full` adds the slow tests). One branch, `macos`; work commits directly on it.

Real-window checks: `mac/tools/ui-drive/ui-drive.sh` launches, drives and screenshots the app safely (`mac/tools/ui-drive/README.md`); `crcli image probe|stats` checks the screenshots.
Plan bookkeeping: `mac/tools/plan-status.sh` (`--check` / `--write` the status table and next task of `mac/plans/full-port.md` from the Status lines; `--backfill-log` fills `mac/plans/run-log.md` from `git log`).

## Prerequisites

- Apple Silicon Mac, macOS 14 or later
- .NET SDK 10.0.x (`brew install --cask dotnet-sdk`; check with `dotnet --list-sdks`)

## Build

```bash
dotnet build mac/ComicRack.Mac.slnx
```

App bundle (M2.0b): `mac/tools/make-app.sh` builds the ad-hoc signed `mac/artifacts/ComicRack CE (MVP).app` (self-contained, about 165 MB; replaces the previous one).

Files in the original project folders are not edited. `mac/EDITS.md` logs any approved exception.

## Compile census (M0.3a)

The shadow projects (`cYo.Common.Core`, `ComicRack.Engine.Core`, `ComicRack.Plugins.Core`) link the original sources. Since M1.3 both passes build with 0 errors and the shadow projects are part of the solution (pass B, the default). The census tool measures the errors of each pass (both report 0 now):

```bash
dotnet run --project mac/tools/census -- --pass A   # net10.0-windows, WinForms still resolves
dotnet run --project mac/tools/census -- --pass B   # net10.0, the real target
```

Each command writes `mac/reports/census-A.md` or `census-B.md`. `mac/reports/census.md` summarizes both and lists every exclusion and replacement.

Pass A builds with 0 errors since M0.3b (exclusions plus `Replacements/` folders, no edits to the original projects):

```bash
dotnet build mac/ComicRack.Plugins.Core/ComicRack.Plugins.Core.csproj -p:CensusPass=A
dotnet run --project mac/tools/api-inventory     # writes mac/reports/api-inventory.md
```

`api-inventory` builds pass A clean and lists the System.Drawing, System.Windows.Forms and P/Invoke members the Core uses. `dotnet run --project mac/tools/api-inventory -- --compare mac/reports/api-baseline.json` compares the current surface with the committed baseline and prints `no new platform API` (exit 0) or the new types, members and DllImports with "covered by the shim" and "linked" answers (exit 1); `-- --write-baseline` rewrites the baseline after a reviewed change (P0.5b). Pass A and B share `obj/` (the restore result is per pass): `dotnet build` restores by itself, but a bare `dotnet msbuild` query needs `-restore` after the other pass ran.

## Compat shims and tests (M1.1)

`mac/cYo.Compat.Forms` is the types-only stand-in for the `System.Windows.Forms` slice that the Core uses. The shadow projects reference it in pass B only. It is part of the solution together with its tests:

```bash
dotnet build mac/ComicRack.Mac.slnx
dotnet test mac/tests/Compat.Forms.Tests
```

The tests compare every enum (names, values, `[Flags]`) and every other type's kind and member signatures with the `System.Windows.Forms` reference assembly of the `Microsoft.WindowsDesktop.App.Ref` pack. That pack is downloaded by a pass A build, so run one first on a fresh machine.

## Drawing stand-in (M1.2a)

`mac/cYo.Compat.Drawing` declares the `System.Drawing.Common` types and members the Core uses (namespaces `System.Drawing`, `.Drawing2D`, `.Imaging`, `.Text`), one file per type in `Types/`. Images and pixels work since M1.2b: `Image`/`Bitmap` are backed by one `SKBitmap`, with `LockBits`/`UnlockBits`, decoding through Skia, `Save` as JPEG or PNG, and `cYo.Compat.Drawing.SkiaInterop.GetSKBitmap(Bitmap)` for the UI. Since M1.2c a `Graphics` subset works as well (`FromImage`, `Clear`, `FillRectangle`, `DrawImage` with the interpolation modes, transforms, `ImageAttributes.SetColorMatrix`, `Bitmap(Image, int, int)`). Text, clipping, paths and the rest (`Font`, `DrawString`, `SetClip`, `GraphicsPath`, ...) still throw `NotSupportedException`. The shadow projects reference it in pass B only. The SkiaSharp version for the whole tree is `SkiaSharpVersion` in `mac/Directory.Build.props`. To test it and to check the coverage against the API inventory (both need a pass A build first, for the reference pack):

```bash
dotnet test mac/tests/Compat.Drawing.Tests
dotnet run --project mac/tools/drawing-coverage
```

The tests include a drift guard, like the one for the Forms shim: every public type must keep the kind and the member signatures of the `System.Drawing.Common` reference assembly.

## Shadow projects in the solution (M1.3)

`dotnet build mac/ComicRack.Mac.slnx` builds the three shadow projects in pass B (`net10.0`, the real target) together with the shims, the tests and the three tools (`api-inventory`, `census`, `drawing-coverage`). Pass B differs from pass A only by `Replacements/PassB/` files (`KnownColorTableEx`, `UserCredentialsDialog`, `Resources`, `PdfStorageProvider`) and by the shim references. Pass A (`-p:CensusPass=A`) stays the basis of `api-inventory`; do not run a pass A and a pass B build at the same time (they share `obj/`).

## The app (M2.0a)

`mac/ComicRack.Mac` is the Avalonia 12.1.4 app (window, native menu bar, `MacHost` opened at launch and disposed at exit). It keeps its data in `~/Library/Application Support/ComicRack CE (MVP)` (caches in `~/Library/Caches/...`, log in `~/Library/Logs/...`).

```bash
dotnet run --project mac/ComicRack.Mac
```

Quit with Cmd+Q (exit code 0).

### Smoke test (for scripted self-checks)

```bash
dotnet run --project mac/ComicRack.Mac -- --smoke-test[=SECONDS] [--headless] [--data-dir DIR]
```

- `--smoke-test[=N]` opens the window, waits until the host has opened, stays up N seconds (default 3), closes the host and exits. Exit code **0** if the host opened and no error was logged, **1** otherwise (host failed, or an exception reached the UI-thread handler).
- `--data-dir DIR` (or the environment variable `COMICRACK_MAC_DATA_DIR`) puts data, cache and logs under `DIR/{Data,Cache,Logs}`; the log is `DIR/Logs/ComicRack.log`. A smoke test **without** it uses a new temporary folder that is deleted at the end, so it never touches the real MVP folders. Always use one of the two in scripted runs.
- `--headless` uses Avalonia's headless platform: no window server, no native menu, no drawing. It runs the same code (window, view model, host open and close, log) and needs no usable display. Use it where the screen is locked or asleep (Avalonia.Native then stops with "could not start the RenderTimer", error -6661; the app prints a hint and exits with code 4) and in CI. It does not prove that the native menu or the real window render.
- `--read-book FILE` opens FILE in a reader window as soon as the engine is up (the book is not added to the library, so nothing is recorded). With `--smoke-test` the run exits 1 if page 1 or page 2 did not show, so it checks the whole page path (archive provider, pdfium, Skia decode) without a display; the line `Reader check: ..., N pages, page 1 W x H, page 2 W x H` is in the log.
- Exit codes: 0 ok, 1 smoke test failed, 2 bad arguments, 4 no usable display.

Example (real window, needs an unlocked screen): `dotnet run --project mac/ComicRack.Mac -- --smoke-test=5 --data-dir "$TMPDIR/crmac"; echo $?`

### Rules for app code

- Every subscription to a Core event goes through `UiDispatch` (`UiDispatch.Wrap(handler)` or `UiDispatch.Post` inside the handler). Review checklist item for every M2 task.
- Log through `AppLog` (it writes to the host log once the host is open).
- Exception to the first rule, on purpose: `FolderScanner` (in `ComicRack.Mac.Host`, no UI types) subscribes to `ComicScanner.ScanNotify` directly, because the scanner reads `e.Cancel` right after the handler returns, so the cancel must be answered on the scanner thread. That handler touches no control or view model; progress goes to the UI only through `UiProgress<T>` (which posts through `UiDispatch`).

### Open Folder and Import Windows Library (M2.1)

- **File > Open Folder... (Cmd+O):** the Avalonia `StorageProvider` folder picker, then a scan through the Core's scanner queue (`FolderScanner`). The window shows the number of files and books so far and the current file, with a Cancel button. Books added before a cancel stay; the library is saved after the scan (also after a cancel), and the file is checked.
- **File > Import Windows Library...:** a dialog with the path of a Windows `ComicDb.xml` and one `windows prefix=mac prefix` mapping per line (for example `S:\media\BD\=/Users/me/Comics/BD/`). The database is copied **while the paths are rewritten** (book `File`, watch folder `Folder`; watch folders are switched off) into `<data>/ComicDb.import.xml`; then the open library is saved and closed, moved to `ComicDb.before-import-DATE.xml` (only if it had books), the copy takes its place and the host opens it. If the Core cannot load the copy the old library is put back. The source file is only read.
- **Save on quit:** `HostService.Dispose` closes the host (which saves) and then checks the file (`SaveVerification`: it exists, was modified after the save started, is complete XML, and holds the expected number of books). The Core's own save swallows every error, so a failed save is only visible this way; it is logged as an error and shown after a scan.
- The dialogs sit behind `ILibraryDialogs` (picker, import dialog, message), so view models and tests do not need a window.
- Tests (no real window; Avalonia headless platform with the Skia renderer): `dotnet test mac/tests/ComicRack.Mac.Tests`. Tests that need `mac/fixtures/BD`, `BD-reader` or `ComicDb.xml` are skipped when those are missing. Set `CRMAC_FRAMES_DIR=<folder>` to save the rendered window frames as PNG. All tests use private data folders under the temp folder and fail if the real `~/Library/.../ComicRack CE (MVP)` folders change.

### Thumbnail service (M2.2a)

`mac/ComicRack.Mac/Thumbnails/ThumbnailService` gives the grid its covers as `SkiaSharp.SKImage`s. Not wired into the window yet (M2.2b does that).

```csharp
// once the host is open (dispose the service BEFORE the host; Dispose waits a few seconds for running decodes)
var thumbnails = new ThumbnailService(host.CacheManager);
SKImage image = await thumbnails.Request(book, sizePx: 300, token);          // placeholder picture if the file is missing
ThumbnailResult r = await thumbnails.RequestResult(book, 300, token);        // r.IsPlaceholder, r.Error
if (thumbnails.TryGetCached(book, 300, out ThumbnailResult hit)) { ... }     // synchronous, never starts a decode
```

- **Sizes:** a request names a height in pixels and is served at the next step of the Core's chain (512, 384, 288, 216, 162, 121, ...; never above 512, the height the Core stores). The cache holds one image per book and step.
- **Order and limits:** at most 4 decodes at once; waiting requests are a stack (the newest is decoded first, a repeated request moves up); a request cancelled while waiting never decodes; concurrent requests for the same cover share one decode and a caller that cancels only detaches itself.
- **Threads:** the task completes on the UI thread (through `UiDispatch`), except a memory cache hit, which is already completed when it returns.
- **Image ownership (the rule for cells):** each caller gets its **own** `SKImage` over pixels that are shared and reference counted (`PixelBlock`, no copy). Dispose it when the cell is recycled or the picture replaced; if you forget, the garbage collector frees it. The cache evicting an entry (256 MB bound by default) never invalidates an image that is still being drawn. Do not call `Dispose` on an image you did not get from a request.
- **Failures** (missing volume, damaged archive) never fault the task: the result is a grey tile with a cross, remembered for 30 seconds so a grid of missing books does not retry on every scroll. The first 20 are written to the log (`INFO`), the rest are only counted (`GetStats().Failures`).
- **Pixels:** `CoreThumbnailDecoder` asks the Core's `ImagePool` for the cover (memory cache, disk cache, and only then the book file) and decodes the stored JPEG with Skia straight into the pixels the image keeps (opaque BGRA, reduced with a mipmapped filter when the step is below 512). It does not wrap the Core's decoded `ThumbnailImage.Bitmap`, which the Core replaces, releases after 15 s and disposes on eviction.
- Tests: `dotnet test mac/tests/ComicRack.Mac.Tests --filter FullyQualifiedName~Thumbnails` (a fake decoder and a pumped fake UI thread for the rules; the fixture books in `mac/fixtures/BD` for the real decoder, skipped without them).

### Browser grid (M2.2b)

The main window shows the library as a grid of covers (`Views/BookGridView`, `Views/BookCell` on `Controls/CrThumbnailItem`, `Browser/*`, `ViewModels/BrowserViewModel`; thumbnails are drawn as the original's `ThumbRenderer` does, spec `specs/views/CoverViewItem.md`): a toolbar with a search box (title and series), a sort box (Series, Title, Date added) and a thumbnail size slider; Enter or a double click on a book opens the reader (M2.3a).

- **Virtualized:** an `ItemsRepeater` with a `UniformGridLayout` (package `Avalonia.Controls.ItemsRepeater` 12.0.0; the Avalonia 12 core assemblies do not contain it) realizes only the cells around the viewport and recycles them, so 14,706 and 100,000 books create the same few dozen cells.
- **Covers:** a cell asks `ThumbnailService.TryGetCached` when it is realized, otherwise `RequestResult` with a token that it cancels when it is recycled. The picture is drawn by `CoverDrawOperation` (an `ICustomDrawOperation` that draws the `SKImage` on the render thread's canvas with one call; no bitmap copy). `CoverImage` reference-counts the image, because a queued frame may still draw it after its cell was recycled.
- **Search and sort never run on the UI thread:** `BookCatalog` is a snapshot of the library (built on a worker thread) with the three sort orders (computed once, on first use) and a search that filters the sorted order (case and accent insensitive, every word must be in the title or the series). A keystroke cancels the previous query; only the newest result is applied.
- **Lifetime of the `ThumbnailService`:** `HostService.Thumbnails` is created when a host opens and disposed before the host closes (import, quit); `MainViewModel` hands it to the grid on `Opened` and drops it on `Closing`. The catalog is rebuilt after open, import and scan.
- Tests: `dotnet test mac/tests/ComicRack.Mac.Tests --filter FullyQualifiedName~Browser` (catalog rules and timings over 14,706 and 100,000 synthetic items, view model, the grid on the headless Skia platform with frames saved as PNG when `CRMAC_FRAMES_DIR` is set, and two tests over the fixture library: the window with real covers, and a full scroll that records memory against the cache bound).

### Sidebar (M2.2c)

A tree of the library's lists on the left of the main window (`ViewModels/SidebarViewModel`, `Host/Library/LibraryLists.cs`): the library, the folders, the smart lists and the fixed lists of the database, read only, each with its book count. Selecting one shows its books in the grid (search, sort and size still apply). View > Show Sidebar (Cmd+Ctrl+S) hides and shows it.

- **Evaluation is the Core's:** `ListEvaluator` calls `ComicListItem.GetBooks()`, as the Windows browser does. Nothing is evaluated on the UI thread: the tree is built on a worker thread, the counts are computed one list at a time on a worker thread (children before the folder that combines them) and posted to the rows as they come, and a selected list is evaluated inside the grid's load.
- **Stored results are not trusted:** a `ComicDb.xml` carries each list's last result and the Core uses it when it loads the file. The fixture database has stale ones, and the numbers also depend on how the file was opened. The first evaluation of a list therefore drops its cache and lets the Core compute it from the matchers (`crcli smartlist --cached` shows the stored numbers instead).
- **Lists that need scripts:** a list with a `ComicBookPluginMatcher` (also inside a group matcher, or through its base list) gets the badge "Requires scripts (not in MVP)", no count and an empty grid with a notice. It is not evaluated, because without a plugin engine the matcher returns every book it is given.
- **`crcli smartlist <ComicDb.xml> (--list "name" | --all) [--remap "from=to"] [--cached]`** prints the count of a list or of all lists (tab separated, `list<TAB>name<TAB>kind<TAB>count<TAB>note`) with the same `ListEvaluator`. The database is opened read only.
- Tests: `dotnet test mac/tests/ComicRack.Mac.Tests --filter FullyQualifiedName~Sidebar` (tree, evaluation, stale stored results, script lists, view model, window frames with `CRMAC_FRAMES_DIR`, and over the fixture library: every sidebar count equals the `crcli smartlist --all` count).

### Reader (M2.3a)

Double click or Enter on a book in the grid opens it in a reader window (`Views/ReaderWindow`, `Views/ReaderView`, `ViewModels/ReaderViewModel`, `Reader/*`, `Services/ReaderManager`). One window per book (opening it again brings it forward); the fit mode last chosen is the start mode of the next reader. Esc or Cmd+W closes it. `--read-book FILE` opens a file without the library (see the smoke test above).

- **Pages:** `CoreReadingSession` does what `NavigatorManager.OpenComic` and `MainForm.OnBookOpened` do (`RefreshInfoFromFile`, `CreateNavigator`, `Open(async)`, then `OpenedCount` + 1, `OpenedTime`, `NewPages` = 0). `CorePageDecoder` gets each page from the Core's `ImagePool` through the navigator (its memory and disk caches, then the archive provider: SharpCompress, pdfium) and **decodes the page bytes with Skia into pixels of its own**; a page the Core rotated or colour-adjusted exists only as a Core bitmap and is copied under the Core's own `ItemMonitor` lock. Nothing the reader draws points into memory the Core can free.
- **Prefetch and cancel:** `PageService` keeps the pages of a window decoded, the one on screen first, then the next 2 and the previous 1 (`IPageLayout.Window`); at most 2 decodes run at once, a page somebody waits for gets one more slot. When the reader moves, queued jobs for pages that left the window are dropped and running ones are cancelled (the decoder checks between the Core read and the Skia decode); a held arrow key therefore decodes the page it lands on and its window, not every page it passes. The cache holds the window plus up to 160 MB of other pages. A cached page turn is complete before the key handler returns.
- **Ownership:** the cache holds one reference of a `CoverImage` per page (one `SKImage` over a reference counted `PixelBlock`); the scene, and every queued draw operation, hold their own. Evicting a page, closing the reader or turning on never frees pixels a frame in flight still draws. The same `SKImage` is handed out each time, so Skia can keep its GPU texture while the page stays cached.
- **Drawing:** `ReaderView` records one `ReaderDrawOperation` per change (page, scroll, fit, size); it draws each `SKImage` with one `DrawImageRect` on the render thread's canvas, snapped to whole device pixels. The filter depends on the scale (`ReaderSampling`): exactly 1 nearest, above 1 Mitchell, 1 to 0.5 Catmull-Rom, below 0.5 linear with mipmaps (the cubic and plain linear filters alias on large reductions; contact sheets from `SamplingLabTests`, run with `CRMAC_FRAMES_DIR` set, show it).
- **Layout:** `IPageLayout` decides which pages a position shows, how positions follow each other and where the pages go: `SinglePageLayout` and, since M2.3b, `SpreadLayout` (see "Reader (M2.3b)"). Fit modes use the Core's `ImageFitMode`: Cmd+2 Best Fit (`Fit`: the whole page, the WinForms default), Cmd+3 Fit Width (a taller page scrolls), Cmd+5 Fit Height (the height of the window; a wider page or spread pans sideways). The other values behave as Best Fit.
- **Keys:** Right / Page Down next, Left / Page Up previous, Home / End first / last, Space scrolls a page that is taller than the window by 90 % and turns when it cannot scroll further, Shift+Space the reverse (landing on the bottom of the previous page), Up / Down scroll, Esc and Cmd+W close. **Trackpad:** a two-finger horizontal swipe turns one page (`SwipeRecognizer`: the deltas of a gesture add up to a threshold, then the gesture is spent, momentum tail included, until events pause or a stronger one starts a new swipe); a vertical two-finger scroll scrolls a tall page.
- **Reading state:** each turn writes `ComicBook.CurrentPage` through the navigator (which raises `LastPageRead`), so the library saved on quit holds it; closing the reader writes the page once more. Reopening starts at `CurrentPage`. Import and quit close the readers first.
- **Timing:** every turn is measured from the command to the end of the draw call (`PageTurnMeter`) and logged as `Page turn page 12 cached: total ... ms (ui ... + draw ...)`; see `Facts from M2.3a` in `mac/plans/mvp-track.md` for the numbers.
- Tests: `dotnet test mac/tests/ComicRack.Mac.Tests --filter FullyQualifiedName~Reader` (page service with a fake decoder, layout, swipe, painting, pixels, view model, windows with frames, the Core path on real files, the app flow grid to reader to quit to relaunch to resume, timing).

### Reader (M2.3b)

Two-page spreads, right-to-left reading, zoom and pan, and native full screen, through the same `IPageLayout` seam (`Reader/PageLayout.cs`, `Reader/PageCatalog.cs`).

- **Layouts (View menu, Option+Cmd+1/2/3):** One Page; Two Pages (the WinForms *Double* layout: a lone portrait page takes half a spread); Two Pages (Adaptive) (a lone page is shown as a single page). The layout last chosen starts the next reader (in memory only).
- **Pairing** follows `ComicDisplayControl`: the front cover (page type FrontCover) and a back cover are alone, a wide page (width greater than height) is alone, a page whose position is Near starts a spread. The book is paired once from its first page, so the same spreads come up in both directions (the WinForms display pairs from the page it is on). A page's size is known from the Core's page info or when the reader decodes it (`PageService.Decoded` tells `PageCatalog`); a wide page found while loading is shown alone and the pages after it re-pair. Spreads are fitted, zoomed and scrolled as one image (the two pages scaled to the taller one's height). The status strip shows "4-5 / 50".
- **Right to left:** the book's own manga setting (`Manga` = Yes and right to left) decides, else a default (`ReaderOptions.RightToLeft`, off); Option+Cmd+R or View > Read Right to Left toggles it for this reader (not saved). Left turns forward and Right back, a swipe turns the other way, a spread shows its first page on the right, the cover stays on the right, a page wider than the window starts at its right edge.
- **Zoom:** Cmd+= and Cmd+- (x1.25 per step, 1 to 8 times the fit), Cmd+0 back to the fit, a pinch on the trackpad (about the pointer; also a pinch on a touch screen). Zoomed (or when a fit mode overflows sideways) the two-finger scroll and a mouse drag pan the page, inside its edges; a swipe pans instead of turning the page then. The zoom stays across page turns, the scroll starts at the top (left, or right when reading right to left) of each page.
- **Full screen:** Ctrl+Cmd+F or View > Enter Full Screen (`WindowState.FullScreen`); the status strip is hidden; Esc leaves full screen before it closes the reader.
- **Reading state:** a spread records its first page as `CurrentPage` and its last page as `LastPageRead` (as the WinForms display does), and reopening starts at the spread that holds `CurrentPage`.
- Tests: `SpreadLayoutTests` (pairing, order, placement, zoom and pan limits), `ReaderSpreadTests` (paging across spreads, resume, wide pages, prefetch, right to left, zoom, pan, keys), `ReaderSpreadWindowTests` (frames; with `CRMAC_FRAMES_DIR` set they are saved as PNG `m23b-*.png`), `CoreSpreadTests` (real CBZ, manga flag, what is written to the book).
