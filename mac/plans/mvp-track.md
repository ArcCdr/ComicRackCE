# macOS MVP track: from feasibility to a launchable app

> **Status:** Adopted by the owner on 2026-10-16 (decision D7 in `MACOS_NATIVE_PATH.md`).
> **Relation to `MACOS_NATIVE_PATH.md`:** This file adds a vertical-slice track in front of §9.3. It does not change the §0 decisions or the §9.2 guardrails. Read §0 and §9.2 of that file before starting any task here.
> **Execution model:** Every task is sized and specified for a **Sonnet** session at a stated effort level. This replaces the Opus "Architect" and "Reviewer" roles of §9.1 for this track. How to run a task: §10.
> **Task status:** Each task carries a `Status:` line. Update it in the PR that completes the task.

## 1. Why a separate track

§9.3 builds the port in horizontal layers: tests, then the full Core, then the PAL, then the UI. A launchable app appears only after P0, P1, P2 and P5.0, roughly 6–9 weeks in. That is too late to learn whether performance and UX are good enough.

This track cuts one **vertical slice** through the same layers: just enough Core, shims, natives and UI to open a library, browse it and read a book on a real Mac. The riskiest assumptions get tested in about 3–5 weeks, and the owner can judge performance and UX before committing to the full plan.

The slice is not throwaway. It builds the same projects as the full plan (shadow projects, compat shims, CLI harness, Avalonia shell), only with less breadth.

## 2. What the MVP is

A double-clickable, ad-hoc-signed `ComicRack CE (MVP).app` for Apple Silicon that does the following:

1. **Open a library.** Either scan a folder of CBZ/CBR/CB7/CBT (plus PDF if M1.4 lands it), or load a *copy* of a Windows `ComicDb.xml` with a path-prefix remap.
2. **Browse.** A virtualized thumbnail grid with sort (series, title, added) and a text search. A sidebar shows the DB's existing lists and smart lists. These are read-only and evaluated by the Core matchers.
3. **Read.** A reader window with single page and two-page spread; fit width, height and best fit; keyboard and trackpad paging; zoom/pan; native full screen; and the right-to-left flag.
4. **Persist.** The DB and the last page read are saved on quit, into a separate MVP data folder. The app never writes to a Windows DB.

**Not in the MVP** (still in v1 scope): metadata editing and ComicInfo write-back, the smart-list editor, export, the backup UI, preferences, scripts (spiked separately, §7), HEIF/AVIF/JXL, PAL beyond paths and natives, Developer ID signing/notarization, and localization of new UI.

## 3. Facts established on 2026-10-16 (commit `7fd9fd3`)

**Machine**
- Apple M4, 16 GB, macOS 26.6. Homebrew is present.
- .NET SDK 10.0.402 is installed (runtime 10.0.13; `dotnet` at `/opt/homebrew/bin/dotnet` → `/usr/local/share/dotnet`).
- **Disk: only 8.7 GB free (96 % used).** Every task that writes large outputs must stay within the budgets in M1.6 and M2.0b, and must delete its own temporary output. If free space drops below 3 GB: **STOP and report.** Check with `df -h ~`.
- Sessions in this setup run *on the Mac*, not only on Linux as §9.1 assumed. They can build, run, benchmark and launch the app. The owner still judges UX.

**Owner decisions (2026-10-16)**
- This track is adopted (D7).
- The MVP app is named `ComicRack CE (MVP)` and uses its own data folders.
- Test data is the fixture DB below.

**Owner test data: `/Users/vince/dev/ComicRackAPI/tests/fixtures/ComicDb.xml`**
- **Never modify the source file.** Always copy it into `mac/fixtures/` (git-ignored) first.
- 32 MB, **14,706 books**. All of them are `.cbz` under `S:\media\BD\` (French BD, 1200 px wide pages, about 50 pages per book).
- 22 list items: 19 `ComicSmartListItem`, 2 `ComicListItemFolder`, 1 `ComicIdListItem` and 1 library item.
- The smart lists use 24 matcher types. They include **2 `ComicBookPluginMatcher`** (script-based, so they need IronPython, not in the MVP) and `SmartListSeries*` matchers.
- The `<WatchFolders>` entries point to `S:\incoming\ToSort\Cleaned_bd` and `S:\media\BD`. The import must remap or drop them.
- **The book files are not available on this Mac** (no mounted volume). Thumbnails and the reader therefore use stand-in CBZ files generated at the remapped paths (M1.6).
- The XML declaration uses single quotes (`<?xml version='1.0' encoding='utf-8'?>`), so the file was probably rewritten by Python tooling in ComicRackAPI rather than by ComicRack. Check that the Core's `XmlSerializer` loads it (the first run of `crcli db stats`). If it does not, record why in §3 and **STOP**.

**Coupling not covered, or mis-scoped, by `MACOS_NATIVE_PATH.md` §3.1**
- `ComicRack.Engine.csproj` has project references to `cYo.Common.Windows` **and** `cYo.Common.Presentation`.
- Outside `Controls/` and `Display/`, 14 Engine files import those namespaces:
  - **`ItemViewConfig` family (expected, §5.3):** `Database/{ComicDatabase,StacksConfig,DisplayListConfig}.cs`, `Metadata/ComicBook/ComicBookMetadata{,Collection,Manager}.cs`, `Metadata/VirtualTags/ComicBookVirtualTagComparer.cs`.
  - **`IO/MemoryOptimizedImage.cs`:** subclasses `cYo.Common.Presentation.RendererImage`. Link `RendererImage.cs` and its dependencies into Core rather than excluding Presentation wholesale.
  - **`IO/Provider/XmlInfo/XmlInfoProviderFactory.cs`:** uses `cYo.Common.Presentation.Ceco`.
  - **`Drawing/Thumb{,Tile,Icon}Renderer.cs`:** use WinForms theme resources. Nothing outside `Drawing/` or `Controls/` references them, so exclude them for the MVP.
  - **`ComicPageInfo.cs`, `IO/Provider/XmlInfo/MetronInfoProvider.cs`:** import `cYo.Common.Windows` with no visible use (probably decompiler leftovers). The namespace must still exist for them to compile.
- **`Display/` should not be excluded wholesale** (unlike the §5.2 example). It holds pure enums (`ImageFitMode`, `PageLayoutMode`, …). `IComicDisplay` is referenced by `ComicRack.Plugins/{IPluginEnvironment,PluginEnvironment}.cs`. Only `Display/ComicDisplay.cs` and `GestureEventArgs.cs` use WinForms.
- **WinForms imports in Core outside UI folders:**
  - Engine: `ComicBook.cs`, `IO/Provider/Writers/PdfStorageProvider.cs`, `Backup/BackupArchiveCreator.cs`.
  - Common: `Drawing/RectangleExtensions.cs`, `Drawing/ExtendedColors/ProfessionalColorTableEx.cs`, `Net/HttpAccess.cs`, `Runtime/{CrashWatchDog,IniFile,GitVersion,Diagnostic}.cs`, `Text/FunctionParser/FunctionFactory.cs`, `Threading/BackgroundRunner.cs`.

**Things that may need no edit at all**
- **BinaryFormatter (`cYo.Common/IO/DiskCache.cs`):** every call sits inside `try/catch` with an XML fallback. On .NET 10 it throws and gets caught, so it needs zero edits if the compile is allowed. Suppress SYSLIB0011.
- **`Thread.Abort`:** three Core sites (`ComicScanner.cs`, `Threading/BackgroundRunner.cs`, `Threading/ThreadUtility.cs`). It compiles on .NET 10 and throws only on cancel paths, which is acceptable for the MVP. The real fix stays in P1.4.
- **Archive engine:** `EngineConfiguration.CbzUses`/`CbrUses`/`Cb7Uses`/… default to `CbEngines.SevenZip`. The Mac host sets them to `SharpCompress` at startup (no edit).
- **Paths:** `SystemPaths(bool useLocal, string alternateConfig, string databasePath, string cachePath)` accepts explicit DB and cache paths. Other folders still come from `SpecialFolder.ApplicationData`/`LocalApplicationData`; verify what .NET 10 maps them to on macOS.

**App composition root**
- It lives in the WinForms project, `ComicRack/Program.cs` (`StartNew`):
  ```
  DatabaseManager
  CacheManager(DatabaseManager, SystemPaths, ICacheSettings, ResourceManager)
  QueueManager(DatabaseManager, CacheManager, IComicUpdateSettings, devices)
  ```
- The WinForms `Settings` class implements `ICacheSettings`, `IComicUpdateSettings`, `ISharesSettings` and `IVirtualTagSettings`. The Mac host implements these on a small `MacSettings` class, so `ComicRack/Config/Settings.cs` is not linked.

## 4. Rules for every task

1. **Zero edits to existing files.** Do not edit files in the 7 original project folders. Use exclusion lists plus `Replacements/` files, `NoWarn` and runtime fallbacks instead. If an edit looks unavoidable: **STOP and report.** Do not make it. The owner decides, and any approved edit is logged in `mac/EDITS.md`.

   Mechanical check before every PR:
   ```bash
   git diff --name-only master...HEAD | grep -vE '^(mac/|MACOS_NATIVE_PATH\.md$|\.github/workflows/mac-)'
   ```
   It must print nothing.
2. **One task per session, one branch per task.** The branch is `mac/<task-id>` (for example `mac/M0.2`), or `worktree-<task-id>` when run in a worktree, off `mac/mvp`. It merges into `mac/mvp` (§10).
3. **Stay in scope.** Do only what the task lists. If you discover new work, add it to §9 "Discovered work" and do not do it.
4. **Accept checks are commands.** Run every accept check and paste its output in the PR description. A task is not done until its checks pass.
5. **STOP conditions win.** When a task's STOP condition triggers, commit what works, write what you found under the task, and end the session.
6. **Two failures mean the task is too big.** If a task fails its accept checks after two serious attempts, stop and propose a split into smaller tasks in §9. Do not keep iterating.
7. **Record facts.** Append new facts that later tasks need to §3, in the same PR.
8. **No model identifiers or session links** in code, comments or docs (guardrail 8).

## 5. Milestones and gates

| Milestone | Content | Estimate (low confidence) |
|---|---|---|
| M0 | Toolchain, `mac/` skeleton, compile census and API inventory (S1), Drawing route (S2) | 3–5 days |
| M1 | Core-lite compiles and runs headless on macOS; CLI; benchmarks | 1.5–2.5 weeks |
| M2 | Avalonia MVP app: library, grid, reader; `.app` bundle | 2–3 weeks |

| Gate | Owner decides | Evidence |
|---|---|---|
| **G0** (after M0) | Is the error census acceptable? Which Drawing shim route? | `mac/reports/census.md`, `mac/reports/drawing-route.md` |
| **G1** (after M1) | Does the Core work on the Mac with acceptable performance? | `mac/benchmarks/M1.md` |
| **G2** (after M2) | Are UX and performance good enough to commit to the full plan? | Acceptance checklist (M2.6); re-estimate §9.6 |

## 6. Tasks

Every task runs on **Sonnet**. **Effort** is the level to set for that session (§10). The default is `high`; `xhigh` is used where correctness depends on subtle semantics.

### M0: Toolchain and compile census

#### M0.1 Install the toolchain
- **Status:** done 2026-10-16 (SDK 10.0.402) · **Who:** owner
- **Do:**
  - `brew install --cask dotnet-sdk`
  - `dotnet --list-sdks` must show `10.0.x`.

#### M0.2 `mac/` skeleton
- **Status:** todo · **Effort:** medium
- **Add:**
  - `mac/Directory.Build.props`:
    - Properties: `TargetFramework net10.0`, `LangVersion 14`, `Nullable disable`, `ImplicitUsings disable`, `AllowUnsafeBlocks true`, `DefineConstants $(DefineConstants);MACOS`.
    - `NoWarn`: `$(NoWarn);SYSLIB0011;SYSLIB0006;SYSLIB0014;CA1416`.
    - Its presence stops MSBuild from importing the root `directory.build.props`. Do not import that file.
  - `mac/ComicRack.Mac.slnx` (empty solution).
  - `mac/EDITS.md`: a header row `| File | Reason | Upstreamable |` and no entries.
  - `mac/README.md`: prerequisites and build command.
  - `mac/.gitignore`: `artifacts/`, `fixtures/`, `bin/`, `obj/`.
- **Accept:**
  - `dotnet build mac/ComicRack.Mac.slnx` exits 0.
  - The §4 rule 1 check prints nothing.

#### M0.3a Shadow projects and two-pass census build
- **Status:** todo · **Effort:** high
- **Goal:** compile the original Core sources on .NET 10 *without fixing anything*, so that the errors and the API usage can be measured exactly.
- **Add:**
  - Three shadow projects, each `<Compile Include>`-linking the original sources with `LinkBase`:
    - `mac/cYo.Common.Core`
    - `mac/ComicRack.Engine.Core` (references Common.Core)
    - `mac/ComicRack.Plugins.Core` (references both)
  - NuGet packages: the same as the original csproj files, minus `bblanchon.PDFium.Win32` and `System.Management`. Use `IronPython 2.7.12` instead of 2.7.4.
  - A `CensusPass` property selects one of two passes:

    | Pass | Settings | What it shows |
    |---|---|---|
    | **A** | `TargetFramework=net10.0-windows`, `UseWindowsForms=true`, `EnableWindowsTargeting=true` (builds on macOS; never runs) | Only the .NET Framework → .NET 10 gaps (WCF, `System.Web.Services`, removed WinForms controls, …). WinForms and System.Drawing still resolve |
    | **B** | `TargetFramework=net10.0`, the real target | Everything, including the Windows-only APIs |

  - Initial exclusions, always `obj/**` and `bin/**` too:
    - Common: `Win32/PortableDevices/**`, `Reflection/CodeDomDuckTypeGenerator.cs`.
    - Engine: `Controls/**`, `Display/ComicDisplay.cs`, `Display/GestureEventArgs.cs`, `Drawing/Thumb*Renderer.cs`, `IO/Network/**`.
    - Plugins: `Controls/**`.
  - `mac/tools/census/` (a small .NET console tool):
    1. Run `dotnet build` for a pass with `-flp:errorsonly;logfile=…`.
    2. Parse the `CS*` errors and group them by missing symbol (`CS0246`, `CS0234`, `CS0103`, `CS1061`, `CS0117`, …) and by file.
    3. Write markdown.
- **Do not** fix errors, add shims, or change exclusions beyond the list above.
- **Accept:**
  - `dotnet run --project mac/tools/census -- --pass A` and `-- --pass B` each write `mac/reports/census-A.md` and `census-B.md`, with total errors, a per-symbol table and a per-file table.
  - The §4 rule 1 check prints nothing.

#### M0.3b Pass A to green, then the API inventory
- **Status:** todo · **Effort:** high
- **Goal:** make pass A compile, then extract the exact list of System.Drawing, WinForms and P/Invoke members the Core uses.
- **Do:**
  1. Resolve the pass A errors **only** by:
     - adding files to the exclusion list, when the file is Windows-only or deferred per D4;
     - adding a `Replacements/` file that declares a missing type, when an excluded type is still referenced.
  2. **STOP condition:** if a pass A error can be fixed only by editing an existing file, list it in `mac/reports/census.md` under "Needs edit" and keep going with the others.
  3. Once pass A builds, add `mac/tools/api-inventory/` (a .NET console tool using `System.Reflection.Metadata`). It reads the three pass A output assemblies and lists every `MemberReference`/`TypeReference` whose resolution scope is `System.Drawing.Common`, `System.Drawing.Primitives` (types only) or `System.Windows.Forms`, plus every `DllImport` (module, entry point, declaring type).
- **Output:**
  - `mac/reports/api-inventory.md`: `Type.Member → #call sites → files`, sorted by usage.
  - `mac/reports/census.md`: pass A and B totals, buckets, the "Needs edit" list, and every exclusion and replacement added, each with a one-line reason.
- **Accept:**
  - Pass A builds with 0 errors.
  - Both reports exist.
  - The §4 rule 1 check prints nothing.

#### M0.4 Spike S2: Drawing route data
- **Status:** todo · **Effort:** high
- **Goal:** produce the data for decision Q1. The owner decides at G0.
- **Do:**
  1. Create the throwaway project `mac/spikes/S2-genexus/`. Reference `GeneXus.Drawing.Common` (latest stable; record the version and licence).
  2. For every System.Drawing type in `api-inventory.md`, check by reflection whether GeneXus has the same type and member signature. Output a coverage table.
  3. Write 6 xUnit checks that run against GeneXus on the Mac:
     - decode a JPEG from a stream
     - `LockBits` on `Format32bppArgb` (`Scan0`, `Stride`, byte order BGRA)
     - `DrawImage` downscale with `HighQualityBicubic`
     - save a JPEG with an `EncoderParameter` quality
     - `ImageAttributes` + `ColorMatrix` grayscale
     - get the underlying `SKBitmap` without a copy (does a public or reflection-free path exist?)
  4. Count the inventory members by kind (types, ctors, methods, properties) to size a custom shim.
- **Output:** `mac/reports/drawing-route.md` with the coverage %, the 6 check results, the shim size and a recommendation, using this rule:
  - **Default:** a custom shim declared in the literal `System.Drawing` namespaces. D6 needs that namespace for scripts, and the UI needs zero-copy `SKBitmap` access.
  - Copy GeneXus source (MIT) into it per member where the GeneXus implementation passed the checks.
  - Recommend adopting GeneXus as a package **only** if coverage is ≥ 90 %, all 6 checks pass, and namespace aliasing works for scripts. That last point is unlikely, so say why.
- **Accept:** the report exists and the spike project is not referenced by the solution.

**G0:** the owner reads `census.md` and `drawing-route.md`, decides Q1, and approves or rejects each "Needs edit" item.

### M1: Core-lite runs on macOS

#### M1.1 `cYo.Compat.Forms`, types-only slice
- **Status:** todo · **Effort:** high
- **Do:**
  - Create `mac/cYo.Compat.Forms` (namespace `System.Windows.Forms`). Declare **only** the WinForms types that `api-inventory.md` lists for Core, with exact names, values and `[Flags]`.
  - Add an `Application` static facade: `ProductName`, `CompanyName`, `ProductVersion`, `StartupPath`, `ExecutablePath`. It returns the same company and product strings as the Windows app (read them from `ComicRack/Properties/AssemblyInfo.cs`), because `IniFile` search paths derive from them.
  - Members that the Core uses but that are not types (for example `Clipboard`) get a stub that throws `NotSupportedException("System.Windows.Forms.X.Y is not yet supported on macOS")`.
  - Declare the `cYo.Common.Windows` namespace (one internal marker type) if pass B still reports it missing.
  - Link `cYo.Common.Windows/Forms/{ItemViewConfig,ItemViewColumnInfo,ItemViewMode}.cs` into `ComicRack.Engine.Core`.
- **Test:** `mac/tests/Compat.Forms.Tests` (xUnit, net10). For every enum in the slice, it compares names and values to the same enum read via `System.Reflection.Metadata` from the WinForms reference assembly in the `Microsoft.WindowsDesktop.App.Ref` pack, which pass A already downloads.
- **Accept:**
  - Pass B has no WinForms-type errors left.
  - The enum test passes.

#### M1.2a `cYo.Compat.Drawing`: surface skeleton
- **Status:** todo · **Effort:** high
- **Do:**
  - Create `mac/cYo.Compat.Drawing` (namespaces `System.Drawing`, `.Drawing2D`, `.Imaging`, `.Text`), referencing `SkiaSharp` and `SkiaSharp.NativeAssets.macOS`.
  - Declare every type and member in `api-inventory.md` with its exact signature. Bodies throw `NotSupportedException("System.Drawing.X.Y is not yet supported on macOS")`.
  - Do not redeclare `Point`, `Size`, `Rectangle`, `Color` or other types that `System.Drawing.Primitives` already provides.
- **Accept:** pass B has no System.Drawing errors left.

#### M1.2b `cYo.Compat.Drawing`: images and pixels
- **Status:** todo · **Effort:** xhigh
- **Do:** give real behaviour to these, backed by `SKBitmap`:
  - `Image` and `Bitmap`: from stream, from size + `PixelFormat`, `Clone`, `Width`/`Height`, `PixelFormat`, `RotateFlip`, `Save(stream, ImageFormat)`, `Save(stream, ImageCodecInfo, EncoderParameters)` (JPEG quality), `GetPixel`/`SetPixel`, `Dispose`.
  - `LockBits`/`UnlockBits` → `BitmapData` (`Scan0`, `Stride`, `Width`, `Height`, `PixelFormat`).
  - The mapping is fixed: `Format32bppArgb`/`Format32bppPArgb`/`Format32bppRgb` → BGRA8888 (unpremul, premul and opaque); `Format24bppRgb` → convert on lock and unlock.
  - Add the public `SkiaInterop.GetSKBitmap(Bitmap)` (no copy).
- **Tests:** `mac/tests/Compat.Drawing.Tests`, one test per member above, including the 24-bpp lock round-trip and JPEG quality affecting file size.
- **Accept:** the tests pass.

#### M1.2c `cYo.Compat.Drawing`: Graphics subset
- **Status:** todo · **Effort:** high
- **Do:**
  - Implement `Graphics.FromImage`, `DrawImage` (the overloads in the inventory), `FillRectangle`, `Clear`, `InterpolationMode`, `SmoothingMode`, `CompositingQuality`, `TranslateTransform`/`ScaleTransform`/`ResetTransform`, `Dispose`.
  - Implement `ImageAttributes.SetColorMatrix` → `SKColorFilter`.
  - Interpolation mapping: `NearestNeighbor` → nearest; `Bilinear`/`Low` → linear; `HighQualityBilinear` → linear + mipmaps; `Bicubic`/`HighQualityBicubic`/`High` → `SKCubicResampler.Mitchell`.
  - Text (`DrawString`, `MeasureString`) stays unsupported in the MVP.
- **Tests:** each member; the `ColorMatrix` grayscale result is checked per pixel within ±2.
- **Accept:** the tests pass.

#### M1.3 Pass B to green
- **Status:** todo · **Effort:** high
- **Do:**
  - Clear the remaining pass B errors with replacements and exclusions only (same rules and STOP condition as M0.3b). Expected replacements include `NetworkManager`, `DeviceSyncFactory` and `SingleInstance`; each keeps its full type name and does a no-op or throws `NotSupportedException`.
  - Link `cYo.Common.Presentation/RendererImage.cs` and its dependencies, plus `Ceco/**` if `XmlInfoProviderFactory` needs it.
  - Add the three shadow projects and the two compat projects to `mac/ComicRack.Mac.slnx`.
- **Accept:**
  - `dotnet build mac/ComicRack.Mac.slnx` has 0 errors.
  - `census.md` is updated.
  - The §4 rule 1 check prints nothing.

#### M1.4 Natives for the MVP
- **Status:** todo · **Effort:** medium
- **Do:**
  - Add `bblanchon.PDFium.macOS` to `ComicRack.Engine.Core`.
  - Add a `NativeLibraryResolver` (in `mac/ComicRack.Mac.Host`) that maps `pdfium` to the app's native folder.
  - **Time-box ½ day.** If PDF does not render by then, mark PDF "dropped from MVP" here and stop.
  - WebP and other raster pages decode through the shim (Skia); no `libwebp`.
  - HEIF/AVIF/JXL are out of the MVP.
- **Accept:** `crcli page` (M1.5b) renders a CBZ, a CBR, a CB7 and a PDF page (unless PDF was dropped).

#### M1.5a `ComicRack.Mac.Host`
- **Status:** todo · **Effort:** high
- **Do:** create the `mac/ComicRack.Mac.Host` library, the composition root shared by the CLI and the app. Mirror `ComicRack/Program.cs` `StartNew` and keep only:
  - `MacPaths`:
    - data: `~/Library/Application Support/ComicRack CE (MVP)`
    - cache: `~/Library/Caches/ComicRack CE (MVP)`
    - logs: `~/Library/Logs/ComicRack CE (MVP)`
    - Pass these explicitly to `SystemPaths(useLocal: false, alternateConfig: null, databasePath, cachePath)`.
  - `MacSettings`, implementing `ICacheSettings`, `IComicUpdateSettings`, `ISharesSettings` and `IVirtualTagSettings`. Copy each default from the WinForms `Settings` property initializers and cite the source line in a comment.
  - Construction and disposal order: `DatabaseManager` → `CacheManager` → `QueueManager`, with an empty devices list.
  - `EngineConfiguration`: set every `Cb*Uses` to `CbEngines.SharpCompress`.
  - Logging: a file in the logs folder, plus `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` handlers.
  - Skip: news, backup, plugins, single instance, WinForms renderers and everything else in `StartNew`.
- **Accept:** a unit test opens and closes the host on an empty temp folder twice without exceptions or leaked threads.

#### M1.5b `ComicRack.Cli`: core commands
- **Status:** todo · **Effort:** medium
- **Do:** create `mac/ComicRack.Cli` (`crcli`) on the host with these commands:

  | Command | Purpose |
  |---|---|
  | `db stats <ComicDb.xml> [--remap "S:\media\BD\=/path/"]` | Load time, book count, memory. The remap is applied in memory and also covers `WatchFolders` |
  | `scan <folder> --db <out.xml>` | Scan timing per book |
  | `page <book> --page N --out p.png` | Decode through the provider and `ImagePool` |
  | `thumb <book> --out t.jpg` | Thumbnail through the Core cache |

- **Accept:**
  - Each command works on fixtures (M1.6) with the outputs checked: page PNG dimensions, thumbnail exists, DB book count.
  - `crcli db stats mac/fixtures/ComicDb.xml` reports **14706** books.

#### M1.5c `ComicRack.Cli`: benchmark commands
- **Status:** todo · **Effort:** medium
- **Do:** add these commands:

  | Command | Purpose |
  |---|---|
  | `db synth --count N --out x.xml` | Synthetic DB with plausible series/title/number/publisher, pointing at fixture files |
  | `thumbs <db> --count N` | Thumbnail throughput |
  | `smartlist <db> --list "<name>"` | Evaluate an existing smart list |
  | `bench <fixture-folder>` | Fixed suite (below), printing markdown and JSON with machine info |

  The `bench` suite: DB load at 10k and 100k books; scan; page decode p50/p95 (cold and cached); thumbnail throughput.
- **Accept:** `crcli bench` runs to completion on fixtures.

#### M1.6 Fixtures
- **Status:** todo · **Effort:** medium
- **Do:**
  - Add a `crcli fixtures make <dir> [--books N --pages M]` command (Skia-generated pages with page-number text, 2–6 MP, JPEG/PNG/WebP mix).
  - Package CBZ with SharpZipLib, and CB7/CBR with `7zz` if present (`brew install sevenzip`), otherwise skip with a message.
  - Add `crcli fixtures from-db <ComicDb.xml> --root <dir> [--pages N] [--max-books N]`:
    - Copies the DB to `mac/fixtures/ComicDb.xml`.
    - Writes a stand-in CBZ at every book's path remapped under `<dir>`.
    - Page sizes are scaled to fit within 800 px, keeping each page's `ImageWidth`/`ImageHeight` aspect ratio from the DB. JPEG quality is 70. The page text shows the series, number and page index.
    - The default is `--pages 1` (cover only, enough for the grid).
  - Fixtures live in `mac/fixtures/`, which is git-ignored.
- **Disk budget:** at most **1.5 GB** for everything in `mac/fixtures/`. Before writing, the command estimates the size, refuses to exceed the budget, and prints `df -h ~`.
- **Accept:**
  - `crcli fixtures make mac/fixtures/small --books 50 --pages 24` produces readable books that `crcli page` opens.
  - `crcli fixtures from-db /Users/vince/dev/ComicRackAPI/tests/fixtures/ComicDb.xml --root mac/fixtures/BD` produces 14,706 covers within the budget.
  - Re-run it with `--pages 24 --max-books 200` for reader tests.
- **Note:** this must land before M1.5b's accept checks. Run M1.6 right after M1.5a.

#### M1.7 macOS CI
- **Status:** todo · **Effort:** medium
- **Do:** add `.github/workflows/mac-build.yml` with:
  - runner `macos-15`
  - `actions/setup-dotnet` 10.0.x
  - `dotnet build mac/ComicRack.Mac.slnx -c Release` and `dotnet test mac/tests`
  - triggers: `push`/`pull_request` on paths `mac/**`, `cYo.Common/**`, `cYo.Common.Windows/**`, `cYo.Common.Presentation/**`, `ComicRack.Engine/**`, `ComicRack.Plugins/**`

  Existing workflows are not touched.
- **Accept:** the workflow is green on the PR.

#### M1.8 G1 benchmark run
- **Status:** todo · **Effort:** medium
- **Do:**
  - Run `crcli bench` on `mac/fixtures/small` and on the stand-in library (`mac/fixtures/BD`).
  - Run `db stats` on `mac/fixtures/ComicDb.xml` (14,706 books), remapped to the stand-ins.
  - Run `smartlist` for each of the 19 smart lists. Lists that use `ComicBookPluginMatcher` are expected to fail without scripting; record how they fail.
  - Write `mac/benchmarks/M1.md` with tables plus a "surprises" section.
  - If the Windows app is available to the owner, list the same measurements for the owner to take there.
- **Accept:** the report exists. **G1:** the owner decides.

### M2: Avalonia MVP app

#### M2.0a App shell
- **Status:** todo · **Effort:** high
- **Do:**
  - Create `mac/ComicRack.Mac`: Avalonia 12 (latest stable on NuGet; record the version), CommunityToolkit.Mvvm and the Fluent theme following the system light/dark setting.
  - `NativeMenu` with the App menu (About, Quit ⌘Q), File (Open Folder… ⌘O, Import Windows Library…), View, Window and Help.
  - The host starts on launch and is disposed on exit.
  - A `UiDispatch` helper wraps `Dispatcher.UIThread.Post`. **Every** subscription to a Core event goes through it; enforce this with a review checklist item.
  - An `Dispatcher.UIThread.UnhandledException` handler logs the exception.
- **Accept:** `dotnet run --project mac/ComicRack.Mac` opens a window with the native menu, and ⌘Q exits with code 0.

#### M2.0b `.app` bundle
- **Status:** todo · **Effort:** medium
- **Do:** add `mac/tools/make-app.sh`, which does the following:
  1. `dotnet publish -c Release -r osx-arm64 --self-contained -p:PublishReadyToRun=true`
  2. Assemble `mac/artifacts/ComicRack CE (MVP).app`:
     - `Contents/MacOS/` holds the publish output.
     - `Info.plist`: `CFBundleIdentifier` (the owner's MVP id), `LSMinimumSystemVersion 14.0`, `NSHighResolutionCapable`.
     - An icon from the existing assets, converted to `.icns` with `iconutil`.
  3. `codesign --force --deep -s - --options runtime --entitlements mac/tools/mvp.entitlements`, where the entitlements are `allow-jit` and `allow-unsigned-executable-memory`.
- **Disk:** the script deletes the previous `.app` and the publish folder before building. Only one bundle (about 150–200 MB) exists at a time.
- **Accept:**
  - `open "mac/artifacts/ComicRack CE (MVP).app"` launches the app.
  - `codesign --verify --deep --strict` passes.

#### M2.1 Open a library
- **Status:** todo · **Effort:** high
- **Do:**
  - File > Open Folder… uses the Avalonia `StorageProvider` folder picker. It scans through the Core queue, shows progress (count and current file) and can be cancelled.
  - File > Import Windows Library… takes a DB path and one or more prefix-remap pairs. It copies the DB into the MVP data folder and opens the copy.
  - The DB saves on quit.
- **Accept:**
  - Scan `mac/fixtures`, quit, relaunch: the same book count is shown.
  - Cancelling a scan leaves the app responsive.

#### M2.2a Thumbnail service
- **Status:** todo · **Effort:** xhigh
- **Do:** create `ThumbnailService` in `ComicRack.Mac`:
  - `Request(book, sizePx, CancellationToken) → Task<SKImage>`
  - It uses the Core thumbnail cache (`CacheManager`) and converts with `SkiaInterop.GetSKBitmap` → `SKImage`.
  - In-memory LRU bounded by bytes (default 256 MB), at most 4 concurrent decodes, and newest requests first, so the visible cells win.
  - Completion is marshalled to the UI thread.
- **Tests:** cancellation (a cancelled request never decodes), the LRU eviction bound, and concurrency (never more than 4 decodes in flight).
- **Accept:** the tests pass.

#### M2.2b Browser grid
- **Status:** todo · **Effort:** high
- **Do:**
  - A virtualized grid (`ItemsRepeater` + `UniformGridLayout`) whose cells draw the `SKImage` through a custom draw operation, with no per-frame copies.
  - A cell requests its thumbnail when realized and cancels when recycled.
  - A thumbnail size slider; sort by series/title/added; a search box that filters on title and series.
- **Accept:**
  - The fixture DB (14,706 books with stand-in covers) and `crcli db synth --count 100000` both load and scroll without visible hitches (owner check).
  - Memory after a full scroll stays under the LRU bound plus the baseline (measure and record).

#### M2.2c Sidebar
- **Status:** todo · **Effort:** medium
- **Do:** a tree of the DB's lists, folders and smart lists. Selecting one filters the grid using the Core's existing list evaluation (read-only).
- **Lists that need scripts:** lists using `ComicBookPluginMatcher` (2 in the fixture DB) show a "Requires scripts (not in MVP)" badge and an empty result. The app must not crash.
- **Accept:** the other smart lists in the fixture DB show the same counts as `crcli smartlist`.

#### M2.3a Reader: core
- **Status:** todo · **Effort:** xhigh
- **Do:**
  - A reader window whose `ReaderView` is a custom Skia draw control. Opening a book from the grid (double-click or ⏎) opens it.
  - Pages come from `ComicBookNavigator` and the Core `ImagePool`, with prefetch of the next 2 and previous 1 pages.
  - Single-page layout with fit best/width/height.
  - Paging: → ← space ⇧space, Home/End, and a two-finger horizontal swipe.
  - Write `LastPageRead` back on close.
- **Accept:**
  - Cached page-turn time is measured (log) and recorded.
  - Closing and reopening a book resumes on the same page.

#### M2.3b Reader: layouts and input
- **Status:** todo · **Effort:** high
- **Do:**
  - Two-page spread (cover alone; follow the WinForms behaviour in `ComicDisplayControl` for wide pages).
  - The RTL flag.
  - Zoom: pinch, ⌘+ and ⌘-; pan by scroll and drag when zoomed; native full screen (⌃⌘F).
- **Accept:** the owner's checklist items for the reader pass.

#### M2.4 Performance overlay
- **Status:** todo · **Effort:** medium
- **Do:** an overlay toggled with ⌥⌘P showing FPS, the last page decode time, thumbnail queue depth, the managed heap and the process RSS. The same values are logged every 10 seconds while visible.
- **Accept:** the overlay shows and the values update.

#### M2.5 MVP polish pass
- **Status:** todo · **Effort:** medium
- **Do:** fix the issues from the owner's first launch notes, limited to a list the owner provides in the task. No new features.

#### M2.6 Acceptance pack
- **Status:** todo · **Effort:** low
- **Do:** write `mac/plans/mvp-acceptance.md`, a checklist covering:
  - launch time
  - scan of N books, local and SMB
  - grid scroll feel and memory (Activity Monitor)
  - page-turn feel, full screen, trackpad gestures
  - TCC permission prompts
  - UX questions: does the grid and sidebar feel native? what are the right reader defaults?

  Each item has a place for the measured value and the owner's verdict.
- **G2:** the owner completes it and decides.

## 7. Parallel feasibility spikes (not part of the MVP, but early)

| ID | Effort | After | What | Accept |
|---|---|---|---|---|
| **S4** IronPython 2.7.12 | high | M1.3 | `crcli script expr "<python smart-list expression>" --db x.xml` through `PythonCommand.CompileExpression`, and `crcli script hook Sample.py Books`. Repeat inside the M2.0b `.app` (hardened runtime, ad-hoc signed) | Both work in the CLI and the `.app`; the results are written to `MACOS_NATIVE_PATH.md` §11.4 |
| **S6** Script UI modal loop | xhigh | M2.0a | Throwaway `mac/spikes/S6-modal/`: a minimal `Form` facade with `ShowDialog()` running a nested `DispatcherFrame` (from the UI thread and from a background thread), `MessageBox.Show`, and a Python `Control` subclass overriding `OnPaint` | A go/no-go report with each case's result; the write-up goes to §11.4 |
| **S3** ImageIO vs dylibs | high | G1 | HEIF/AVIF/JXL decode through `CGImageSource` vs bundled dylibs | Report |
| **P0.2/P0.3** net48 characterization tests | high | any time (needs Windows CI) | §9.3 P0 | Mandatory before the first approved edit to an existing file reaches `master` |

## 8. Order of execution

Tasks on the same line can run in parallel, in separate worktrees:

```
M0.1 → M0.2 → M0.3a → M0.3b → M0.4                       → G0
G0 → M1.1 | M1.2a → M1.2b | M1.2c → M1.3 → M1.4
   → M1.5a → M1.6 → M1.5b → M1.5c | M1.7 → M1.8         → G1      (S4 after M1.3)
G1 → M2.0a → M2.0b | M2.1 → M2.2a → M2.2b | M2.2c
   → M2.3a → M2.3b → M2.4 → M2.6 → (owner run) → M2.5   → G2      (S6 after M2.0a)
```

## 9. Discovered work

_(Sessions append items here instead of doing them. Format: `- [task that found it] description`.)_

## 10. How to run a task

### 10.1 One-time setup
1. Check that `claude -v` reports 2.1.284 or later. If not, run `claude update`.
2. Create the integration branch and commit this plan on it: `git switch -c mac/mvp master`.
3. Ignore worktrees locally, without editing the repo: `echo ".claude/worktrees/" >> .git/info/exclude`.
4. Optional, for parallel tasks: put `{"worktree": {"baseRef": "head"}}` in `.claude/settings.local.json`. Without it, `claude -w` branches from `master` instead of the `mac/mvp` checkout you launch from.

### 10.2 Model and effort
Pass both as **launch flags**: `--model sonnet --effort <level>`.
- Flags override your settings files for that session only.
- In-session `/model` and `/effort` save to `~/.claude/settings.json` and would change your defaults everywhere.
- Sonnet's default effort is `medium`, so always pass `--effort`.

| Effort | Tasks |
|---|---|
| `low` | M2.6 |
| `medium` | M0.2, M1.4, M1.5b, M1.5c, M1.6, M1.7, M1.8, M2.0b, M2.2c, M2.4, M2.5 |
| `high` | M0.3a, M0.3b, M0.4, M1.1, M1.2a, M1.2c, M1.3, M1.5a, M2.0a, M2.1, M2.2b, M2.3b, S3, S4, P0 tests; **all reviews** |
| `xhigh` | M1.2b, M2.2a, M2.3a, S6 (pixel formats, concurrency and cancellation, the reader core, re-entrant modal loops) |

Do not use `max`. When `xhigh` is not enough, split the task (§4 rule 6) instead.

### 10.3 Run a task (sequential, the normal case)
```bash
git switch mac/mvp && git switch -c mac/M0.2
claude --model sonnet --effort medium "Read mac/plans/mvp-track.md (§3, §4 and task M0.2) and MACOS_NATIVE_PATH.md §0 and §9.2. Execute task M0.2 only, following §4. Run every accept check, update the task's Status line, and commit on the current branch. Do not push. End with a short report: files changed, accept-check output, STOP findings, items added to §9."
```
For another task, change the task ID (in the branch name and in both places in the prompt) and set the effort from §10.2.

### 10.4 Run tasks in parallel
Use this only for tasks joined by `|` in §8. Launch from the `mac/mvp` checkout, with step 4 of §10.1 done:
```bash
claude -w M1.2b --model sonnet --effort xhigh "<same prompt, with M1.2b>"
```
After merging, clean up with `git worktree remove .claude/worktrees/M1.2b`.

### 10.5 Review and merge (every task)
1. Start a fresh session on the task branch with `claude --model sonnet --effort high`, then run `/code-review high`.
2. Run the §4 rule 1 check yourself.
3. Merge: `git switch mac/mvp && git merge --no-ff mac/M0.2`.
4. Push `mac/mvp` when you want CI (after M1.7).
