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

**Facts from M0.3a (2026-10-17)**
- Layout: `mac/Shadow.props` (shared by the three shadow projects) holds the `CensusPass` switch (default `B`; `-p:CensusPass=A` selects `net10.0-windows` + `UseWindowsForms` + `EnableWindowsTargeting`). It also defines `CENSUS_PASS_A`/`CENSUS_PASS_B` and turns off all default item globbing and `GenerateAssemblyInfo`. `cYo.Common.Core/Sources.props` and `ComicRack.Engine.Core/Sources.props` hold each project's sources and packages.
- **`CensusMerged=true`** (property): the downstream projects compile the upstream sources and packages directly instead of through `ProjectReference`. It exists because a failed compile emits no assembly, so with a plain reference chain the errors in Common would hide every error in Engine and Plugins. The upstream `AssemblyInfo.cs` files are left out of the merged compile (duplicate assembly attributes) and covered by compiling each project's own `AssemblyInfo.cs` in its own build. M0.3b and M1.3 can keep using it until Common is green.
- **`dotnet build` (csc) reports only the declaration phase.** Roslyn does not bind method bodies while declaration-level errors exist (missing types in usings, signatures, base types). The totals from `dotnet build` are therefore far too low (pass A: 70, pass B: 645). `mac/tools/census` therefore also asks MSBuild for the exact compile inputs (`-getItem:Compile,ReferencePathWithRefAssemblies`) and asks Roslyn (`Microsoft.CodeAnalysis.CSharp` 5.0.0, NuGet) for all diagnostics. The reports use this full count and also state the csc count. Fixing types still reveals some further errors, so even the full count is a lower bound.
- **Census totals (commit `caadf68`, SDK 10.0.402, 883 linked source files in the merged Plugins build):**

  | Pass | Full Roslyn errors | `dotnet build` (csc) errors | Common / Engine / Plugins |
  |---|---|---|---|
  | A | **134** | 70 | 18 / 96 / 20 |
  | B | **1319** | 645 | 844 / 427 / 48 |

- Pass A (framework gaps only) is small and dominated by what the shadow projects deliberately do not link yet: `cYo.Common.Windows` and `cYo.Common.Presentation` types (`ItemViewConfig`, `IViewableItem`, `RendererImage`, `LocalizeUtility`, `AutomaticProgressDialog`: about 50 errors), the excluded `IO/Network` (`NetworkManager.cs` alone has 36), `Sync/*` and `Win32/PortableDevices`, WCF in `Runtime/SingleInstance.cs`, `System.Management` in `Win32/MemoryInfo.cs`, `System.Data.SqlClient` in `ComicStorageMsSql.cs`, `CodeDomDuckTypeGenerator` in `DuckTyping.cs` and `Theme/*` in Plugins.
- Pass B is dominated by System.Drawing (about 750 `CS1069` plus about 300 `CS0103` for `PixelFormat`, `ContentAlignment`, `ImageLockMode`, `StringAlignment`, `StringFormatFlags`, `GraphicsUnit`, `ImageFormat`; top types: `Bitmap` 386, `Image` 48, `Graphics` 44, `Brush` 39, `Font` 36, `Pen` 36) and `System.Windows.Forms` (59 `CS0234`, 36 files; `Application` alone 11). The census does not show the System.Drawing *members* used. That is the M0.3b inventory.
- All NuGet packages restore for `net10.0` (`CSJ2K`, `LibHeifSharp`, `MySqlConnector`, `PDFiumSharpV2`, `SharpCompress`, `IronPython 2.7.12`, ...). `sharpPDF.dll` (a .NET Framework assembly) resolves as a plain `Reference` with a `HintPath` and produces no errors.
- Restore warnings, left alone: `NU1510` for `System.Text.Json` and `Microsoft.CSharp` (the packages are in the framework); `NU1904` (critical advisory) for `System.Drawing.Common` 4.7.0, which comes in transitively through `IronPython 2.7.12` → `DynamicLanguageRuntime` → `System.Configuration.ConfigurationManager` → `System.Security.Permissions` → `System.Windows.Extensions`. It is not in the compile references (verified), so it does not clash with the Drawing shim at compile time.
- Disk: a pass A build downloaded the `Microsoft.WindowsDesktop.App.Ref` pack into `~/.nuget/packages`; the whole NuGet cache is 343 MB. All `bin/obj` of the M0.3a projects total under 20 MB.
- Reports: `mac/reports/census-A.md` and `census-B.md` (regenerated by `dotnet run --project mac/tools/census -- --pass A|B`; raw error logs go to the git-ignored `mac/artifacts/census/`). `mac/ComicRack.Mac.slnx` still builds with exit code 0 because the shadow projects are not in it (they fail by design; M1.3 adds them).

**Facts from M0.3b (2026-10-17)**
- **Pass A builds with 0 errors** (`dotnet build mac/ComicRack.Plugins.Core/ComicRack.Plugins.Core.csproj -p:CensusPass=A` builds all three). No pass A error needed an edit of an existing file ("Needs edit": none). Pass B went from 1319 to **1195** full errors (csc 645 to 582): System.Drawing 1067, WinForms 117, Win32/security 5, other 6, `cYo.Common.Windows`/WCF 0. `mac/reports/census.md` lists every exclusion and replacement with its reason.
- **Replacements:** `Replacements/*.cs` folders sit next to each shadow project (`mac/cYo.Common.Core/`, `mac/ComicRack.Engine.Core/`, `mac/ComicRack.Plugins.Core/`). `Sources.props` (and the Plugins csproj) compile them in both passes and in merged mode. The `cYo.Common.Windows`/`Presentation` stubs live in `ComicRack.Engine.Core/Replacements/CommonWindowsTypes.cs` and `RendererImage.cs`. **M1.1 deletes the `ItemViewConfig` stub when it links the real `ItemViewConfig*`; M1.3 deletes the `RendererImage` stub when it links the real one.**
- **Unreferenced inside Core, so excluded without replacement:** `NetworkManager.cs`, `Win32/MemoryInfo.cs`, `Runtime/SingleInstance.cs` + `ISingleInstance.cs`, `Sync/PortableDeviceSyncProvider.cs`. `DuckTyping` is not called from anywhere else in the repository.
- **The .NET 10 SDK adds the WinForms analyzer `WFO1000`** (errors in linked WinForms sources once declaration errors are gone). `Shadow.props` suppresses it in pass A only.
- **Build quirk:** the census builds Engine and Plugins "merged", and when they succeed, the merged assemblies overwrite the normal outputs in `bin/`. `mac/tools/api-inventory` therefore always runs a clean (`--no-incremental`), non-merged pass A build first and refuses to run on merged output. The restore result (`project.assets.json`) is per pass, so a bare `dotnet msbuild` query needs `-restore` after the other pass ran; `dotnet build` restores by itself.
- **The Core's real WinForms/GDI+ surface is small** (`mac/reports/api-inventory.md`; the per-member data for M0.4 is in `mac/artifacts/api-inventory/inventory.json`, git-ignored, regenerated by the tool in seconds):
  - `System.Drawing.Common`: 47 types (18 enums, 24 classes, 4 static classes, 1 interface), **19 constructors, 63 methods, 42 properties, 2 fields**, 714 call sites. `Bitmap`/`Image`/`Graphics`/`BitmapData` dominate; the hot spots are `cYo.Common/Drawing/{ImageProcessing,BitmapExtensions,GraphicsExtensions}.cs`.
  - `System.Windows.Forms`: 22 types, 7 constructors, 9 methods, 29 properties, 1 field, 2 events, 76 call sites; mostly `Application` (`ProductName`, `CompanyName`, `ProductVersion`, `Idle`), `Padding`, `Cursor`, `DataObject`, `Control`/`Form`, `ProfessionalColorTable`, `Keys`, `Clipboard`.
  - `System.Drawing.Primitives`: 10 types (`Color`, `Rectangle`, `Point`, `Size`, `SystemColors`, ...), already in .NET 10.
  - Text drawing (`StringFormat`, `Font`, `DrawString`) appears only in `ComicTextBuilder`, `RatingRenderer`, `TextLine`, `SimpleTextRenderer` and `FC`, which the MVP can leave unsupported.
  - Roslyn emits member references against the **declaring type** (`Image.Width`, not `Bitmap.Width`), so the shim's `Image` base class must carry the shared members.
  - **P/Invoke:** 108 declarations in 16 native modules, 134 call sites. Declarations per module: `jxl.dll` 23, `kernel32` 26, `shell32` 16, `user32` 12, `libwebp` 14, `uxtheme` 5, `credui` 3, `gdi32` 5, `libheif` 1, `dwmapi`, `ole32`, `shlwapi`. Only `jxl`, `libwebp` and `libheif` are codec natives; the rest is shell/UI, which the PAL replaces (not needed for the MVP).
- **Tools:** `mac/tools/census` now also prints an "Errors per bucket" table and dumps every diagnostic to `mac/artifacts/census/diagnostics.<pass>.txt`. Neither it nor `mac/tools/api-inventory` is in the solution yet.

**Facts from M0.4 (2026-10-17; details in `mac/reports/drawing-route.md`)**
- **GeneXus.Drawing.Common 2.2.3** (latest stable, published 2025-09-11) is **Apache-2.0, not MIT** (the plan text in this file and in `MACOS_NATIVE_PATH.md` §5.3 says MIT). `netstandard2.0`; depends on `SkiaSharp` 2.88.8. Its types live in `GeneXus.Drawing.*`, including its own `Color`/`Point`/`Size`/`Rectangle` structs, which are **different types** from the BCL `System.Drawing.*` ones and have no conversion to them.
- **Coverage of the Core's 47 `System.Drawing.Common` types and 126 members:** 35 types present (74.5 %); 83 members exact (65.9 %); 94 source-compatible (74.6 %); 90 not stubs (71.4 %); 72.4 % of the 714 call sites. All 17 present enums match GDI+ in names and values. **Missing:** `BitmapData`, `ColorMatrix`, `ImageAttributes`, `Encoder`, `EncoderParameter(s)`, `ImageCodecInfo`, `ContentAlignment`, `IDeviceContext`, `SystemBrushes/Pens/Fonts`; `ImageFormat` is an enum there.
- **Six checks on the Mac: 2 pass, 4 fail** (same with SkiaSharp 3.119.4). Pass: JPEG decode from a stream; zero-copy `SKBitmap` through the public explicit operator `(SKBitmap)bitmap`. Fail: `LockBits` throws `NotImplementedException` (and returns `object`); `InterpolationMode` is ignored (output identical to nearest neighbour); no `ImageAttributes`/`ColorMatrix`; no `Encoder*`/`ImageCodecInfo` (JPEG quality works only through its own `Save(Stream, ImageFormat, int)`).
- GeneXus stores pixels as **`Rgba8888` premultiplied**, not BGRA. `Image.FromStream` reports `Format24bppRgb` for a JPEG while storing RGBA.
- **Recommendation: custom shim** (the default rule). Size: 47 types (18 enum, 24 class, 4 static class, 1 interface), 19 ctors, 63 methods, 42 properties, 2 fields; about 55 members need real behaviour in the MVP.
- **`Avalonia.Skia` 12.1.4 (latest stable Avalonia 12) depends on `SkiaSharp` 3.119.4** (nuspec). `cYo.Compat.Drawing` (M1.2a) and the app (M2.0a) must resolve to the same SkiaSharp version; SkiaSharp 4.153.1 is also on NuGet, so pin explicitly.
- Spike projects: `mac/spikes/S2-coverage/` (reflection coverage tool) and `mac/spikes/S2-genexus/` (xUnit checks). Neither is in the solution. `dotnet test mac/spikes/S2-genexus` exits non-zero by design (4 failed checks are results). `-p:SkiaOverride=<version>` runs the checks against another SkiaSharp.
- `System.Drawing.Primitives` types need nothing in the shim: .NET 10 provides them.

**Facts from M1.1 (2026-10-17)**
- **Layout:** `mac/cYo.Compat.Forms` (assembly `cYo.Compat.Forms`, namespace `System.Windows.Forms`, net10.0, 9 small files; `Enums.cs` and `ProfessionalColorTable.cs` are generated from the reference assembly). `Shadow.props` adds the `ProjectReference` in **pass B only**; pass A keeps the real WinForms. Project and tests (`mac/tests/Compat.Forms.Tests`) are in `ComicRack.Mac.slnx`; `dotnet test mac/tests/Compat.Forms.Tests` runs 40 tests.
- **Result:** pass A still builds with 0 errors; pass B went from 1195 to **1073** (System.Drawing 1067, Win32/security 5, WinForms **1**: `Cursor.Draw(Graphics, Rectangle)` in `BitmapCursor.cs`, which needs `Graphics` and so waits for M1.2a). No `System.Windows.Forms` namespace/type error is left. The WinForms slice is 6 enums (`Keys`, `ImageLayout`, `MouseButtons`, `DialogResult`, `FormWindowState`, `SortOrder`), `Padding` (fully functional), `Application`, `Control`/`Form`/`TreeView`/`TreeNode`, `Cursor`/`Screen`, `DataObject`/`DataFormats`/`Clipboard`, `CommonDialog`, `IWin32Window`, `MethodInvoker`, `ToolStripRenderer`, `ProfessionalColorTable` (57 virtual colours, all `Color.Empty`).
- **Not needed in the shim:** `ThreadExceptionEventHandler`/`ThreadExceptionEventArgs` already exist in .NET 10 (`System.Threading`, assembly `System.Threading.Thread`), so a second declaration only produces CS0436. `System.Drawing.Primitives` types need nothing either.
- **`Application` is a `sealed class` with a private constructor** (as in WinForms), not a static class; members are static. It returns `cYo` / `ComicRack Community Edition` / `0.9.184` (the values of `ComicRack/Properties/AssemblyInfo.cs`; a test compares them with that file so they cannot drift). `IniFile` and `BackupArchiveCreator` therefore build `.../cYo/ComicRack Community Edition/...` on the Mac. `RaiseIdle` invokes `Idle`; `ThreadException` is never raised.
- **`DataObject` must implement `System.Runtime.InteropServices.ComTypes.IDataObject`** (as the real one does), because `cYo.Common.Win32.DataObjectEx` declares the interface again and only implements some members itself. The COM members throw `NotSupportedException`; `SetData`/`GetData` use an in-memory table.
- **The real `ItemViewConfig` pulls in four more files:** `SortOrder` (WinForms enum, persisted as an XML attribute), `ItemViewGroupsStatus`, `GroupHeaderInformation` and `ItemViewColumnInfo`'s `IColumn` argument. The first three are linked as real files (all in `Engine.Core/Sources.props`, project folder `CommonWindows`). `IViewableItem` and `IColumn` stay as reduced stubs (real signatures for the members they keep), because the real interfaces reach `ItemView` (4,770 lines of WinForms control), `Graphics`, `HeaderState` and `StringAlignment`. Nothing in the Core implements them.
- **`ComicDb.xml` view settings now round-trip** (columns, sort orders, thumbnail/tile size, group state): `ItemViewConfigTests` deserializes a reduced `<View>` excerpt of the fixture with the real class and compares the re-serialized XML.
- **The surface test is a drift guard:** `ShimSurfaceTests` loads the shim and `Microsoft.WindowsDesktop.App.Ref/<newest>/ref/net10.0/System.Windows.Forms.dll` with `System.Reflection.MetadataLoadContext` and checks, per shim type, kind and abstract/sealed, and that every public member exists in the reference with the same signature; per enum, names, values, underlying type and `[Flags]`. It fails with a clear message if the pack is missing (run a pass A build first; M1.7 CI needs that step or a restore of the pack). Mutating one enum value makes it fail (tried).
- `mac/reports/api-inventory.md` was regenerated: `System.Windows.Forms` is 23 types now (`SortOrder` came in with the real `ItemViewConfig`); `Size` and `Rectangle` use counts rose (`ItemViewConfig`, `GroupHeaderInformation`). Member counts are unchanged.

**Facts from M1.2a (2026-10-17)**
- **Layout:** `mac/cYo.Compat.Drawing` (assembly `cYo.Compat.Drawing`, root namespace `System.Drawing`; `.Drawing2D`, `.Imaging`, `.Text` as in the reference), net10.0, **one file per type in `Types/`** (48 files: the 47 inventory types plus `ImageFlags`), so that M1.2b and M1.2c can work in parallel without merge conflicts. It is in `ComicRack.Mac.slnx`. `Shadow.props` references it in **pass B only**; `cYo.Compat.Forms` references it too (`Cursor.Draw(Graphics, Rectangle)`, now a throwing member; the M1.1 placeholder comment is gone). `Compat.Forms.Tests` passes (40 tests); its `ReferencePack.cs` now also adds `cYo.Compat.Drawing.dll` to the load context, because `Cursor.Draw` names `Graphics`.
- **Skia pin:** `SkiaSharpVersion` = **3.119.4** in `mac/Directory.Build.props` (the version `Avalonia.Skia` 12.1.4 uses). Projects reference `SkiaSharp` and `SkiaSharp.NativeAssets.macOS` as `$(SkiaSharpVersion)`; the M2.0a app must do the same and must not add its own version. Nothing in the skeleton calls Skia yet.
- **How the skeleton was made:** a throwaway generator (not committed) read `inventory.json` and the `System.Drawing.Common` reference assembly of the `Microsoft.WindowsDesktop.App.Ref` pack (10.0.13) with `MetadataLoadContext`, and wrote exact signatures (names, parameter names, defaults, `ref`/`out`, static, accessors as observed at the call sites). The files are plain C# now and are edited by hand from here on. Every body throws `NotSupportedException("System.Drawing.X.Y is not yet supported on macOS")`, with these deliberate exceptions: the `IDisposable` pattern (`Dispose()` plus `protected virtual Dispose(bool)`, no-ops, so `using` blocks cannot fail in cleanup); `Encoder.Quality`/`Encoder.ColorDepth` hold their real GUIDs through a real `Encoder(Guid)` constructor and `Guid` property (a readonly static field cannot throw). Types whose constructors the Core never calls get an `internal` parameterless one; `Brush.Clone` is abstract and the three brushes override it, as in the reference.
- **Result:** pass B went from 1073 to **5** full errors; **no System.Drawing and no WinForms error is left** (the 5 are `SystemEvents` and `UIPermission` in `KnownColorTableEx.cs` and `UserCredentialsDialog.cs`, for M1.3). Pass A is unchanged (0 errors; the regenerated `api-inventory.md` differs only in the commit hash).
- **Two things the inventory could not see.** (1) Enum constants are inlined by the compiler, so an enum used only as a constant is not in `inventory.json`: `ImageFlags` (`HeifAvifImage.cs`: `(ImageFlags)bitmap.Flags & ImageFlags.HasAlpha`) was found by the pass B compile and added (generated from the reference). The pass B compile, not the inventory, is the authority for enums. (2) `PdfStorageProvider.cs` passes a `Bitmap` to `sharpPDF.pdfPage.addImage(System.Drawing.Image, int, int)`. sharpPDF is a .NET Framework assembly, bound to the framework's `System.Drawing` identity (a forwarder to `System.Drawing.Common, Version=0.0.0.0, PublicKeyToken=cc7b13ffcd2ddd51`), which a differently named shim assembly cannot satisfy (CS0012, CS1503). It is handled by a **pass B only** replacement: `ComicRack.Engine.Core/Replacements/PassB/PdfStorageProvider.cs` (same type and `[FileFormat]`, throws `NotSupportedException`); `Sources.props` removes the original from the pass B compile only, so pass A and the inventory still see it.
- **Coverage check:** `dotnet run --project mac/tools/drawing-coverage` (adapted from `mac/spikes/S2-coverage`; the spike is untouched and keeps the GeneXus result) compares `cYo.Compat.Drawing` plus the framework's `System.Drawing.Primitives` with `inventory.json`, and the 18 enums with the reference assembly. Result: **57/57 types, 249/249 members with exact signatures (126 `System.Drawing.Common` + 123 `System.Drawing.Primitives`), 0 enum differences**, 100 members still stubs (the figure M1.2b and M1.2c reduce). Exit code is 1 unless coverage is 100 %; a mutated enum value and a changed parameter list were both caught. It needs the `Microsoft.WindowsDesktop.App.Ref` pack (a pass A build downloads it). It is not in the solution yet (M1.3 adds the tools).
- **Disk:** the whole M1.2a work added under 20 MB of `bin/obj`; the NuGet cache gained nothing (SkiaSharp 3.119.4 and its macOS native asset were already there).

**Facts from M1.2b (2026-10-17)**
- **Layout:** `Types/{Image,Bitmap,BitmapData,ImageFormat,ImageCodecInfo,EncoderParameter,EncoderParameters}.cs` have real bodies. New: `Internal/{PixelKinds,PixelConvert,PixelTransform,ImageCodec}.cs`, `SkiaInterop.cs` (public, namespace `cYo.Compat.Drawing`, the only public type outside the reference namespaces) and `AssemblyInfo.cs` (`InternalsVisibleTo("Compat.Drawing.Tests")`). `mac/tests/Compat.Drawing.Tests` is in `ComicRack.Mac.slnx`: `dotnet test mac/tests/Compat.Drawing.Tests` runs **218 tests** (Debug and Release). Test parallelization is off (assembly attribute), because the dispose tests count the live SKBitmaps of the whole process. `ShimSurfaceTests` is a drift guard like the Forms one (it needs the `Microsoft.WindowsDesktop.App.Ref` pack): every shim type is compared with the `System.Drawing.Common` reference assembly (enum names and values, and no public member that the reference lacks). Adding a bogus method and changing an enum value both made it fail (tried).
- **Storage:** one `SKBitmap` per `Image`, always `Bgra8888`, with the alpha type `Unpremul` (`Format32bppArgb`), `Premul` (`Format32bppPArgb`) or `Opaque` (`Format32bppRgb` and `Format24bppRgb`). There is no 3-byte storage. Invariants: in opaque storage the alpha byte is always 255 (new bitmaps are erased to opaque black, an unlock after a write normalizes it, a decode normalizes the rows that Skia could not decode); new alpha bitmaps are transparent black. Width, Height, the pixel pointer and the row bytes are cached in managed fields, because the Core reads `Width`/`Height` in hot loops. Pixel formats that are no bitmap format (`Undefined`, `Canonical`, ...) give `ArgumentException`; real but unsupported ones (indexed, 16, 48 and 64 bpp) give `NotSupportedException`. The Core never creates them, and decoded 8-bit PNG/GIF come out as 24 or 32 bpp.
- **LockBits:** zero-copy exactly when the requested format has the layout of the storage (`Scan0` points into the SKBitmap at the region; `Stride` is the bitmap's row bytes). Every other request (24 bpp always, `Argb` to `PArgb`, an opaque bitmap locked as `Argb`) converts into a separate pinned array (POH, nothing to free by hand). It is filled from the bitmap unless the mode is `WriteOnly` (then it is zeroed) and written back by `UnlockBits` unless the mode is `ReadOnly`. A 24 bpp stride is the row bytes rounded up to a multiple of 4; the order is BGR. All conversions go through the not-premultiplied ARGB value (premultiply rounds `c*a/255`, unpremultiply rounds and clamps, both tested exhaustively); targets without alpha drop it, sources without alpha read as 255. Overlapping regions that are locked at the same time give `InvalidOperationException("Bitmap region is already locked.")` (disjoint regions, also from different threads, are fine); `UnlockBits` of a region that is not locked gives `InvalidOperationException`; `RotateFlip` and `SetPixel` are refused while any region is locked; `UserInputBuffer` gives `NotSupportedException`; `DontCare` locks in the bitmap's own format. `Dispose` with an outstanding lock is safe, and the late `UnlockBits` throws.
- **Decode and encode:** `FromStream` and `FromFile` decode eagerly with `SKCodec`, from the current stream position to the end, so the stream is not needed afterwards and `FromFile` does not lock the file. Tested: JPEG, PNG, WebP, GIF and BMP. **TIFF is not decoded** (`SKCodec.Create` returns null; GDI+ decodes it), and so are all formats that Skia lacks. There is no colour conversion and no EXIF rotation. A source without alpha gives `Format24bppRgb`, every other one `Format32bppArgb` (not premultiplied). Truncated data that Skia can partly decode is accepted; anything else gives `ArgumentException("Parameter is not valid.")`. `Save` writes JPEG (quality 0 to 100, default 75, 4:2:0, alpha ignored and the colour channels kept as stored, not premultiplied, as GDI+ does) and PNG (24-bit RGB for opaque bitmaps, RGBA otherwise). BMP, GIF and TIFF give `NotSupportedException`; `Encoder.ColorDepth` is accepted and ignored; `ImageCodecInfo.GetImageEncoders()` lists JPEG and PNG.
- **Lifetime:** `Image` has a finalizer, calls `GC.AddMemoryPressure`/`RemoveMemoryPressure` for the pixel bytes, and `Dispose` is idempotent. The internal `Image.LiveStorageCount` lets the tests check that every path (decode, `Clone`, `RotateFlip`, failed calls) releases its SKBitmap. Code that holds a raw pointer keeps the owner alive with `GC.KeepAlive`. `SkiaInterop.GetSKBitmap(Bitmap)` returns the owned SKBitmap itself: the caller must not dispose it and must not use it after the Bitmap is gone.
- **Internal API for M1.2c** (same assembly): `Image.Storage` (the SKBitmap), `PixelsAddress`, `RowBytes`, `StorageKind`, `HasOutstandingLocks`, `Bitmap.FromStorage(SKBitmap, PixelFormat)` (takes ownership), `Bitmap.CopyOf`, `PixelConvert.Convert`, `PixelKinds`. `Graphics` must call `Storage.NotifyPixelsChanged()` after drawing.
- **Skia experiment for M1.2c:** an `SKCanvas` over an `Unpremul`, a `Premul` and an `Opaque` BGRA bitmap works in all three cases. A 50 % red (255,0,0,128) drawn onto a cleared `Unpremul` bitmap gives the bytes `00 00 FF 80` (straight alpha, correct); an `Unpremul` bitmap drawn onto a `Premul` one gives `00 00 80 80` (correct). So `Graphics.FromImage` can draw straight onto the storage in all four formats. Skia converts on every draw, so blending over translucent pixels in an `Unpremul` bitmap is correct but rounds each time.
- **Added beyond the inventory** (all exist in the reference): `Bitmap.SetPixel`; `BitmapData.Width`/`PixelFormat`/`Reserved`, the setters and the public constructor; the 12 `ImageFormat` statics (GUIDs read from `System.Drawing.Common` 10.0.0) with `ImageFormat(Guid)`, `Equals`, `GetHashCode`, `ToString`; `ImageCodecInfo.Clsid`/`CodecName`/`DllName`/`FormatDescription`/`FilenameExtension`/`MimeType`; `EncoderParameter.Encoder`/`NumberOfValues`. Not declared, because the Core does not use them (scripts might, D6): `Image.RawFormat`, `HorizontalResolution`/`VerticalResolution`/`SetResolution`, `PropertyItems`, `Palette`, `GetThumbnailImage`, the `Bitmap(Stream)`/`Bitmap(string)`/`Bitmap(Image)` constructors, `MakeTransparent`, `ImageCodecInfo.Flags`/`Version`/signatures. `Image.Flags` is `ColorSpaceRgb` plus `HasAlpha` for the two alpha formats; `HasTranslucent` is not computed.
- **Still throwing in `Image`/`Bitmap`:** `Bitmap(Image, int, int)` (needs `DrawImage`, M1.2c), `GetHbitmap` and `GetHicon` (Windows handles).
- **Results:** `drawing-coverage` still reports 57/57 types, 249/249 exact signatures, 0 enum differences; stubs went from 100 to **69** (the tool does not count `Bitmap(Image, int, int)`, whose body is longer than its 16-byte IL heuristic, as it did not in M1.2a). Pass B is unchanged at **5** errors (census re-run; the report was not committed because only the commit hash and the timings differed). `mac/ComicRack.Mac.slnx` builds with 0 warnings and 0 errors; `Compat.Forms.Tests` still passes (40 tests).
- **Timings** (Debug build, M4, 2000 x 3000 = 6 MP; the JPEG is a smooth gradient, so the decode time is optimistic): decode 4 to 10 ms; `LockBits` 24 bpp `ReadOnly` 6 to 9 ms; `ReadWrite` plus unlock 11 to 13 ms; `Clone` to `Argb` 5 to 8 ms, to `PArgb` 8 to 10 ms; `RotateFlip` 90 degrees 2 to 8 ms; `Save` as JPEG 10 to 12 ms. Conversions run in parallel from 262,144 pixels. Real page timings are M1.8's.
- **Disk (important):** the `SkiaSharp` 3.119.4 package depends on the native assets of every platform, so any executable or test project that references the shim copies about **275 MB of Windows DLLs** (`runtimes/win-x86`, `win-x64`, `win-arm64`) into its `bin/<config>`. Before this fix `Compat.Forms.Tests` (295 MB), `tools/drawing-coverage` (290 MB) and the new tests (295 MB) each did; a Release build doubles it. `Compat.Drawing.Tests` therefore sets `<RuntimeIdentifier>osx-arm64</RuntimeIdentifier>` with `<SelfContained>false</SelfContained>`, which copies only the macOS asset: its `bin` is 20 MB (41 MB with Debug and Release). The M2.0a app is published with `-r osx-arm64`, so it is not affected. Libraries (the shim itself) are not affected. At the end of the task all `bin/obj` of `mac/` were deleted (they are git-ignored and regenerate).

**Facts from M1.2c (2026-10-17)**
- **Layout:** `Types/{Graphics,ColorMatrix,ImageAttributes,SolidBrush,Brushes,Pen}.cs` have real bodies; `Bitmap(Image, int, int)` is implemented in `Bitmap.cs`. `Compat.Drawing.Tests` now runs **351 tests** (Debug and Release; new files `GraphicsTests`, `DrawImageTests`, `ColorMatrixTests`, `BrushPenTests`; `TestImages.Solid` is shared). `Compat.Forms.Tests` still passes (40), `ComicRack.Mac.slnx` builds with 0 warnings and 0 errors, pass A builds with 0 errors, pass B is unchanged at **5** errors (census re-run, report not committed: only hash and timings differed).
- **How `Graphics` works:** `Graphics.FromImage` creates an `SKCanvas` straight over `Image.Storage` (no copy; tested for Argb, PArgb, Rgb32 and 24 bpp destinations). `DrawImage` takes the source as a **zero-copy sub-image** (`SKImage.FromPixels` over the source rectangle's pixels), so filtering never reads outside the source rectangle (the Core's `DrawShadow` stretches 1-pixel strips) and no pixels are copied. A source rectangle that reaches outside the image is clipped and the destination shrinks in proportion (as GDI+). Drawing an image onto itself goes through a temporary copy. `Graphics` does not own the bitmap; it throws `ObjectDisposedException` once the image or the Graphics is disposed, and `InvalidOperationException("Bitmap region is already locked.")` while the bitmap has a `LockBits` region (also at `FromImage`). `Dispose` is idempotent and releases the canvas. `RotateFlip` replaces the image's `SKBitmap`; the Graphics notices on its next call and follows the new storage, keeping its transform (tested).
- **Members:** real: `FromImage`, `Dispose`, `Clear`, `FillRectangle` (both overloads, `SolidBrush` only), `DrawImage` (all 7 overloads of the inventory), `InterpolationMode`, `SmoothingMode`, `CompositingQuality`, `CompositingMode`, `PixelOffsetMode`, `TranslateTransform`/`ScaleTransform` (with and without `MatrixOrder`, default Prepend, tested) and `ResetTransform`. `CompositingMode`, `TranslateTransform`/`ResetTransform`, the `MatrixOrder` overloads and the getters of the state properties are in the reference but were not in the inventory (the task listed them). Still throwing `NotSupportedException`: `Clip`, `SetClip`, `Save`/`Restore`, `DrawString`, `MeasureString`, `TextRenderingHint`, `DrawIcon`, `FillEllipse`, `GetHdc`/`ReleaseHdc`, `PageUnit`, `TransformPoints`. Any other `Brush` than `SolidBrush` in `FillRectangle` gives `NotSupportedException` (the gradient brushes cannot be constructed anyway).
- **State properties:** a new `Graphics` starts with `InterpolationMode.Bilinear`, `SmoothingMode.None`, `CompositingQuality.Default`, `CompositingMode.SourceOver`, `PixelOffsetMode.Default`. `Invalid` and out-of-range values give `InvalidEnumArgumentException`. `CompositingMode.SourceCopy` is Skia's `Src` blend. `SmoothingMode.AntiAlias`/`HighQuality` set anti-aliasing for `FillRectangle` (whole-pixel rectangles are unaffected). `PixelOffsetMode` and `CompositingQuality` are stored and have no effect.
- **Interpolation mapping (as specified):** `NearestNeighbor` to nearest; `Default`/`Low`/`Bilinear` to linear; `HighQualityBilinear` to linear plus mipmaps; `Bicubic`/`HighQualityBicubic`/`High` to `SKCubicResampler.Mitchell`. **One deliberate addition:** a draw that maps source pixels one to one onto whole destination pixels (scale exactly 1, whole-pixel translation) is always drawn as nearest neighbour, because Skia's cubic filter would otherwise blur it (observed: a smooth gradient copied with `HighQualityBicubic` changed by 1 level) while GDI+ copies exactly.
- **Quality warning:** Skia's Mitchell and linear filters do **not** pre-filter a large reduction. Measured on 1-pixel stripes with period 3 (true mean 170) reduced 8:1: nearest 0..255, `Bilinear` 128..255, `HighQualityBicubic` **119..255** (aliasing), `HighQualityBilinear` (mipmaps) **159..191**. The 1-pixel checkerboard reduced 4:1 gives mid grey 128 +-2 in all four modes (that is the M0.4 check 3, now `Checkerboard_downscaled_4_to_1_gives_mid_grey`), because the sample positions fall exactly between the squares; it does not prove quality for other ratios. Timing, Release, M4: 1200 x 1800 to 200 x 300 takes 0.2 ms (nearest), 0.4 ms (bilinear), 1.5 ms (mipmaps), 1.7 ms (Mitchell) on a blank page.
- **Pixel semantics:** `Clear(Color)` writes the value directly (exact; Skia would round-trip it through premultiplied alpha): Argb as is, PArgb premultiplied, and a format without alpha gets the colour **times its alpha** with alpha 255, so `Clear(Color.Transparent)` gives black, as in GDI+. Note that `Color.Transparent` is (0, 255, 255, 255), so on an Argb bitmap it leaves pixels `0x00FFFFFF`, which is also what GDI+ stores. `Clear` ignores the transform. Skia does not force the alpha byte of an opaque bitmap to 255 (a `SourceCopy` of a translucent colour or image leaves it low), so `Graphics` sets it back to 255 in the changed region after a draw on a `Format32bppRgb`/`Format24bppRgb` bitmap in `SourceCopy` mode (the colour then holds colour times alpha); `SourceOver` keeps it at 255 by itself (tested). Blending over translucent pixels of an `Argb` bitmap is correct but rounds by up to 1 to 2 levels per draw (tests use a tolerance of 1 to 2 there). `DrawImage` accepts only `GraphicsUnit.Pixel`; a negative width or height (a mirrored draw) gives `NotSupportedException`; zero sizes draw nothing.
- **`ColorMatrix` and `ImageAttributes`:** `ColorMatrix` is the GDI+ 5 x 5 matrix in the row-vector convention (`[R G B A 1] x M`), identity by default, with the indexer (get and set), `Matrix33` (get and set) and the jagged-array constructor (copies; anything but 5 x 5 gives `ArgumentException`). The `Matrix00`..`Matrix44` properties and `Matrix` are not declared (the Core does not use them). `ImageAttributes.SetColorMatrix` copies the matrix; `Clone` and `Dispose` work; no other attribute exists. The matrix becomes `SKColorFilter.CreateColorMatrix` with the **transpose** of the 4 x 4 block (Skia rows are outputs) and the fifth row as the translation, in **0 to 1 units** (not 0 to 255). Checked per pixel on a 16 x 16 colour grid: BT.601 grayscale within +-2 for destinations Argb, PArgb, Rgb32 and 24 bpp and sources 24 bpp and PArgb; red/blue swap within 1; translation, clamping, `Matrix33` opacity over transparent and over white. Skia applies the filter to not-premultiplied colour, as GDI+ does.
- **`Bitmap(Image, int, int)`:** as in .NET: a new `Format32bppArgb` bitmap, cleared to `Color.Transparent`, then `DrawImage(original, 0, 0, width, height)` with the default bilinear mode. `null` gives `ArgumentNullException`, a non-positive size `ArgumentException`, a disposed original `ObjectDisposedException`; none leaks a bitmap (the `Live` counter is checked). A big reduction through this constructor aliases (see the quality warning); the Core calls it in `BitmapExtensions` and `UserCredentialsDialog` only.
- **Brushes and pens:** `SolidBrush` has `Color` (get and set) and a real `Clone`. `Brushes.Black` is one shared read-only `SolidBrush` (setting `Color` gives `ArgumentException`, `Dispose` does nothing); the other ~140 named brushes are not declared (the Core uses only `Black`, in `ImagePool.CreateErrorPage`). `Pen` has `Color`, `Width`, `Pen(Color, float)` and `Clone`; nothing can draw with a pen yet. `SystemBrushes`/`SystemPens` stay empty classes: the Core never reaches them, because `ComicRack.Engine/Global.cs` aliases both names to the `...Ex` classes in `cYo.Common` (which only use `new SolidBrush`/`new Pen`).
- **Results:** `drawing-coverage` reports 57/57 types, 249/249 exact signatures, 0 enum differences, and **48 stubs** (M1.2b: 69). The tool counts only bodies that build an exception inline, so M1.2c wrote the remaining `Graphics` throws that way too, so that the figure is honest. The 48 are: `Bitmap.GetHbitmap`/`GetHicon`, 5 `GraphicsPath` members, the `Matrix` members (`Rotate`, `Translate`, `TransformPoints`), the gradient-brush members, `Font`/`FontFamily`/`StringFormat`/`SystemFonts` members, `Icon` members and the `Graphics` members listed above.
- **Disk:** `RuntimeIdentifier=osx-arm64` and `SelfContained=false` are now also in `Compat.Forms.Tests` (`bin` 20 MB, before about 295 MB) and `tools/drawing-coverage` (`bin` 15 MB, before about 290 MB). The tool needs the repository root as working directory (its default paths are relative): `dotnet run --project mac/tools/drawing-coverage` from the root.

**Facts from M1.3 (2026-10-17)**
- **Pass B builds with 0 errors** (full Roslyn count 0, `dotnet build` 0), pass A still 0. `dotnet build mac/ComicRack.Mac.slnx` exits 0 from a clean tree (about 10 s on the M4): the solution now holds the three shadow projects (pass B is the default), both shims, both test projects and the tools `api-inventory`, `census` and `drawing-coverage` (folders `/core/`, `/shims/`, `/tests/`, `/tools/`). Its warnings are the original sources' own (about 550: `CS3001/2/3` CLS compliance, `SYSLIB0003/4/21`, `CA2022`) plus `NU1510`/`NU1904`; nothing was suppressed. Nothing in the 7 original folders was edited (rule 1 check prints nothing); no "Needs edit" item.
- **Pass B only replacements** (new folders `Replacements/PassB/` in Common.Core and Engine.Core; `Sources.props` removes the original from the pass B compile only, so pass A and the inventory still see the originals): `KnownColorTableEx` (managed colour table instead of private reflection and `SystemEvents`; same public surface, `SetColor` does not change System.Drawing; nothing in Core uses it), `UserCredentialsDialog` (same surface, never prompts, `RunDialog` reports cancelled; `UIPermission` is gone), `Resources` (see next point). `mac/reports/census.md`, section "Added by M1.3", has the table.
- **`Engine.Properties.Resources` is replaced** (`Replacements/PassB/Resources.cs`; `Properties/Resources.Designer.cs` removed from the pass B compile). The 12 PNG/JPEG files of `ComicRack.Engine/Resources/` are `EmbeddedResource`s of `ComicRack.Engine.Core` with the logical name `ComicRack.Engine.Resources.<file>`; every property (`ErrorPage`, `RedCross`, `ShadowMap`, `Unknown`, `NewPages`..`NewPages5Plus`, `PageCurl`, `PageCurlShadow`) decodes through `Image.FromStream` into a **new** `Bitmap` the caller owns (the original shared one cached instance). `Resources.ResourceManager` is a `ResourceManager` subclass that overrides `GetObject(name[, culture])` the same way and returns null for an unknown name, which is what `CacheManager.RequestResourceThumbnail` needs (`ThumbnailKey.ResourceKey`, e.g. `"Unknown"`). `Certificate`/`Certificate2` are not declared (only the excluded `ComicLibraryServer` uses them). Checked at run time on the Mac with a throwaway console project (deleted): all bitmaps decode (`ErrorPage` 1000 x 1402 Rgb24, `RedCross` 252 x 252 Argb, `Unknown` 340 x 512), `ImagePool.CreateErrorThumbnail(300)` gives a 200 x 300 bitmap with the red cross, and `KnownColorTableEx` / `UserCredentialsDialog` construct and work (Set/Reset/ResetAll, `Initialize(true)` then `GetSystemColor`).
- **`ImagePool.CreateErrorPage()` still throws on the Mac**, as predicted: `NotSupportedException: System.Drawing.SystemFonts.MenuFont is not yet supported on macOS` (before `DrawString`). The M1.5a host (or a later task) must override it (it is `virtual`) or the shim must get a text path; the bitmap itself is available as `Resources.ErrorPage`.
- **Real files linked instead of the stub:** `cYo.Common.Presentation/RendererImage.cs` and `RendererGdiImage.cs` (project folder `Presentation`); `Replacements/RendererImage.cs` is deleted. They need only `Bitmap` and `cYo.Common.ComponentModel`. `Ceco/**` is not linked: `XmlInfoProviderFactory.cs` only imports the namespace, so the marker class in `NamespaceMarkers.cs` stays. `NetworkManager`, `DeviceSyncFactory` and `SingleInstance` were already excluded or replaced in M0.3b.
- **Common's own `Properties/Resources.Designer.cs` is still the original** (no `.resx` is embedded): `Resources.Wikipedia` (`WikiSearch`'s static field) and `Resources.PlusOverlay` (`BitmapCursor`) throw `MissingManifestResourceException` at run time. Nothing in the MVP path reaches them.
- **Census (after M1.3):** pass A 0 / pass B 0 full Roslyn errors, files compiled 317 / 875 / 892 (Common / Engine / Plugins, merged). `census-A.md`, `census-B.md`, `census.md` and `api-inventory.md` are regenerated. Tests unchanged: `Compat.Forms.Tests` 40 and `Compat.Drawing.Tests` 351 pass; `drawing-coverage` still reports 57/57 types, 249/249 exact signatures, 48 stubs, PASS.
- **Disk:** the whole task added under 25 MB of `bin/obj` (the census and the tools need no runtime identifier); all `bin/obj` of `mac/` were deleted at the end. A pass A and a pass B build share `obj/`, so never run them at the same time.

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
- **Status:** done 2026-10-17 · **Effort:** medium
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
- **Status:** done 2026-10-17 · **Effort:** high
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
- **Status:** done 2026-10-17 · **Effort:** high
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
- **Status:** done 2026-10-17 · **Effort:** high
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

**G0 outcome (owner, 2026-10-17):** census accepted; no "Needs edit" items. Q1: custom shim in the literal `System.Drawing` namespaces; GeneXus is not adopted. Licence follow-up (Apache-2.0 vs GPL) is non-blocking.

### M1: Core-lite runs on macOS

#### M1.1 `cYo.Compat.Forms`, types-only slice
- **Status:** done 2026-10-17 · **Effort:** high
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
- **Status:** done 2026-10-17 · **Effort:** high
- **Do:**
  - Create `mac/cYo.Compat.Drawing` (namespaces `System.Drawing`, `.Drawing2D`, `.Imaging`, `.Text`), referencing `SkiaSharp` and `SkiaSharp.NativeAssets.macOS`.
  - Declare every type and member in `api-inventory.md` with its exact signature. Bodies throw `NotSupportedException("System.Drawing.X.Y is not yet supported on macOS")`.
  - Do not redeclare `Point`, `Size`, `Rectangle`, `Color` or other types that `System.Drawing.Primitives` already provides.
- **Accept:** pass B has no System.Drawing errors left.

#### M1.2b `cYo.Compat.Drawing`: images and pixels
- **Status:** done 2026-10-17 · **Effort:** xhigh
- **Do:** give real behaviour to these, backed by `SKBitmap`:
  - `Image` and `Bitmap`: from stream, from size + `PixelFormat`, `Clone`, `Width`/`Height`, `PixelFormat`, `RotateFlip`, `Save(stream, ImageFormat)`, `Save(stream, ImageCodecInfo, EncoderParameters)` (JPEG quality), `GetPixel`/`SetPixel`, `Dispose`.
  - `LockBits`/`UnlockBits` → `BitmapData` (`Scan0`, `Stride`, `Width`, `Height`, `PixelFormat`).
  - The mapping is fixed: `Format32bppArgb`/`Format32bppPArgb`/`Format32bppRgb` → BGRA8888 (unpremul, premul and opaque); `Format24bppRgb` → convert on lock and unlock.
  - Add the public `SkiaInterop.GetSKBitmap(Bitmap)` (no copy).
- **Tests:** `mac/tests/Compat.Drawing.Tests`, one test per member above, including the 24-bpp lock round-trip and JPEG quality affecting file size.
- **Accept:** the tests pass.

#### M1.2c `cYo.Compat.Drawing`: Graphics subset
- **Status:** done 2026-10-17 · **Effort:** high
- **Do:**
  - Implement `Graphics.FromImage`, `DrawImage` (the overloads in the inventory), `FillRectangle`, `Clear`, `InterpolationMode`, `SmoothingMode`, `CompositingQuality`, `TranslateTransform`/`ScaleTransform`/`ResetTransform`, `Dispose`.
  - Implement `ImageAttributes.SetColorMatrix` → `SKColorFilter`.
  - Interpolation mapping: `NearestNeighbor` → nearest; `Bilinear`/`Low` → linear; `HighQualityBilinear` → linear + mipmaps; `Bicubic`/`HighQualityBicubic`/`High` → `SKCubicResampler.Mitchell`.
  - Text (`DrawString`, `MeasureString`) stays unsupported in the MVP.
- **Tests:** each member; the `ColorMatrix` grayscale result is checked per pixel within ±2.
- **Accept:** the tests pass.

#### M1.3 Pass B to green
- **Status:** done 2026-10-17 · **Effort:** high
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

- [M0.3a] The census is only meaningful with the full Roslyn diagnostics (see §3, "Facts from M0.3a"). For pass A to "build with 0 errors" (M0.3b accept), the real `dotnet build` is the judge, but expect it to need several rounds, because each round reveals method-body errors that were hidden by declaration errors. M0.3b should re-run `mac/tools/census` after every round and use `CensusMerged=true` while Common is not green.
- [M0.3a] About 50 of the 134 pass A errors are `cYo.Common.Windows` and `cYo.Common.Presentation` types (`ItemViewConfig`, `IViewableItem`, `RendererImage`, `LocalizeUtility`, `AutomaticProgressDialog`, `Themes`, ...). M0.3b's rules allow only exclusions and `Replacements/` files, while M1.1 links `ItemViewConfig*` and M1.3 links `RendererImage`. Decision needed before M0.3b: either M0.3b adds `Replacements/` declarations for them, or it leaves them in the pass A list as "deferred to M1.1/M1.3" and the "pass A builds with 0 errors" accept is relaxed to that list.
- [M0.3a] `Engine/NetworkManager.cs` (36 errors in pass A) and `Engine/Sync/*` still reference the excluded `IO/Network` and PortableDevices types. The plan already expects `NetworkManager` and `DeviceSyncFactory` replacements in M1.3; M0.3b has to decide whether to exclude these files and replace them earlier, since pass A cannot be green without it.
- [M0.3a] `ComicStorageMsSql.cs` needs `System.Data.SqlClient` (not in the framework on .NET 10; the replacement package is `Microsoft.Data.SqlClient`) and `Win32/MemoryInfo.cs` needs `System.Management` (dropped by the task). The MySql/MsSql storage is not needed for the MVP, so M0.3b can exclude `Database/Storage/ComicStorageMsSql.cs` and replace or exclude `MemoryInfo` (check who calls it).
- [M0.3a] Possible cleanup: drop the `System.Text.Json` and `Microsoft.CSharp` package references in the shadow projects (NU1510). They were kept so the package list matches the originals.
- [M0.3a] Not done, and cheap to add later: the census tool is not part of `ComicRack.Mac.slnx`, so CI (M1.7) will not compile it. Add it when the solution is populated (M1.3).
- [M0.3b] Decision recorded: the `cYo.Common.Windows`/`Presentation` errors were resolved with `Replacements/` stubs (full type names kept); the "0 errors" accept was not relaxed. The M0.3a open question above is closed.
- [M0.3b] `ItemViewConfig` is serialized inside `DisplayListConfig` and `StacksConfig` in `ComicDb.xml`. The stub has only `Grouping`, `SortKey`, `GrouperId` and `StackerId`, so the rest of the view settings (columns, sort order, thumbnail size, ...) are dropped on load and not written back. M1.1 must link the real `ItemViewConfig`, `ItemViewColumnInfo`, `ItemViewMode` (and `ItemViewGroupsStatus`, which `ItemViewConfig` uses) and delete the stub. Until then do not save a DB through the Core and overwrite a real one.
- [M0.3b] `IViewableItem` and `IColumn` stubs are reduced to what Engine reads (`IColumn`: `Id`, `Name`, `ColumnSorter`, `ColumnGrouper`). The real interfaces use WinForms `Control`, `Graphics` and `HeaderState`. M1.1 decides whether to keep the stubs. Nothing in the Core implements them.
- [M0.3b] JPEG 2000: CSJ2K 3.0.0 resolves to its `netstandard1.3` build on net10.0, which has no `System.Drawing` image creator. `CSJ2K.Util.BitmapImageCreator.Register()` is a no-op stub, so `Jpeg2000Image` cannot decode into `Bitmap`. Needs a creator on top of the Drawing shim (post-MVP; with S3 or P2).
- [M0.3b] `ComicLibraryServerConfig` is an empty stub. The real `IO/Network/ComicLibraryServerConfig.cs`, `LibraryShareMode.cs` and `ServerOptions.cs` look free of WinForms/WCF (`System`, `cYo.Common` only), so a later task can link those three files and drop the stub if `MacSettings.Shares` needs real values. Not needed for the MVP.
- [M0.3b] `DuckTyping` now throws `NotSupportedException` (CodeDom generator excluded, guardrail 6). No code in the repository calls it, but Python scripts could. Check in S4 (IronPython spike); if needed, write a `Reflection.Emit` generator.
- [M0.3b] Several stubs are intentionally inert: `AutomaticProgressDialog.Process` runs the action inline (no UI), `DeviceSyncFactory` discovers nothing, `ComicStorageMsSql` throws, `CommunicationException` is a plain exception. `WirelessSyncProvider` and `StorageSync` still compile against the real `System.Net` sockets. The M1.5a host must not start device sync (`QueueManager` is given an empty device list, as planned).
- [M0.3b] The `Replacements/` files that mirror WinForms-facing originals (`RendererImage.cs`, `GestureEventArgs.cs`, `ThemeTypes.cs`) produce 13 pass B errors that vanish with M1.1/M1.2a. Check them off in M1.3.
- [M0.3b] Add `mac/tools/api-inventory` to the solution together with `mac/tools/census` (M1.3), and consider running `api-inventory` in CI (M1.7) as a guard that the Drawing shim keeps covering the inventory.
- [M0.3b] M0.4 should read `mac/artifacts/api-inventory/inventory.json` (regenerate with `dotnet run --project mac/tools/api-inventory`), not parse the markdown. Each member has `type`, `kind`, `name`, `sig` (display signature, parameter types by simple name), `isStatic`, `accessors`, `calls` and `files`. The signature has no namespaces, so overload matching in M0.4 should use the simple type names.
- [M0.4] Licence follow-ups for G0: GeneXus is Apache-2.0, not MIT. Correct `MACOS_NATIVE_PATH.md` (§5.3 says "MIT") and the M0.4 task text when the owner decides Q1, and log the S2 result in `MACOS_NATIVE_PATH.md` §11.4. The FSF considers Apache-2.0 incompatible with GPLv2-only; check whether the repository is "GPLv2 only" or "or later" before any third-party source is copied (not needed if the custom shim is written from scratch).
- [M0.4] Reuse `mac/spikes/S2-coverage` as an accept check for M1.2a: point it at `cYo.Compat.Drawing` instead of GeneXus (the loader and the name mapping need a small change: namespace `System.Drawing` on both sides) and require 100 % type and member coverage of `inventory.json`. If kept, move it to `mac/tools/` and add it to the solution with the other tools (M1.3).
- [M0.4] M1.2b/M1.2c must implement what GeneXus lacks, in this order of weight: `BitmapData` + `LockBits`/`UnlockBits` (BGRA, 24 bpp convert on lock), `ColorMatrix` + `ImageAttributes`, `Encoder`/`EncoderParameter(s)`/`ImageCodecInfo.GetImageEncoders()`, and `ImageFormat` as a class with `Guid`. Apply `InterpolationMode` for real (GeneXus ignores it). M1.2b's tests should include the check-3 pattern (1-pixel checkerboard 4:1 gives mid grey).
- [M0.4] The scripts question (does `from System.Drawing import Bitmap` resolve to the shim, and does a script that does `clr.AddReference('System.Drawing')` load it?) was reasoned, not tested. Add one case to S4 (IronPython spike) once the shim assembly exists. The shim assembly name must then not clash with the framework's `System.Drawing.Common`/`System.Drawing` reference names that `clr.AddReference` resolves.
- [M0.4] Skia version pin: choose one SkiaSharp version for the shim and the Avalonia app (Avalonia.Skia 12.1.4 uses 3.119.4). Decide in M1.2a and record it in `mac/Directory.Build.props` or a shared `.props` file so both agree.
- [M1.1] `Cursor.Draw(Graphics, Rectangle)` is the one WinForms-member error left in pass B. M1.2a adds it to `cYo.Compat.Forms` (a project reference from Forms to Drawing, the member throwing `NotSupportedException`) and deletes the placeholder comment in `mac/cYo.Compat.Forms/Cursor.cs`. The pass B "no WinForms errors" check is then fully clean.
- [M1.1] `mac/cYo.Compat.Forms` is a types-only slice, not the D6 script facade: `Control`, `Form`, `TreeView` and `TreeNode` have only the members the Core uses (`Control.Handle`, `TreeNode.Handle` throw). P4 (script UI layer, with S4) has to grow them. S4 should also test whether `clr.AddReference('System.Windows.Forms')` resolves to an assembly named `cYo.Compat.Forms`; if scripts need the real assembly name, rename the assembly then (no Core code depends on it).
- [M1.1] The real `IColumn`/`IViewableItem` cannot be linked until `ItemView` and its drawing types exist (post-MVP, UI-free Core never implements them). If a later task needs them, keep the stubs and extend them by member rather than linking `ItemView.cs`.
- [M1.1] `Compat.Forms.Tests` needs the `Microsoft.WindowsDesktop.App.Ref` pack in the NuGet cache (pass A downloads it). M1.7 must run a pass A restore/build before `dotnet test`, or the tests fail with a message that says so.
- [M1.1] Shadow projects are still outside `ComicRack.Mac.slnx`; the shim and its tests are in it now. M1.3 adds the shadow projects (pass B) and can then also reference `cYo.Compat.Forms` explicitly instead of through `Shadow.props` if that reads better.
- [M1.2a] **Assembly identity of the Drawing shim is an open design question.** The shim assembly is named `cYo.Compat.Drawing`, so any binary compiled against the framework's `System.Drawing` (sharpPDF today; IronPython scripts doing `clr.AddReference('System.Drawing')` or `from System.Drawing import Bitmap`, D6) binds to the real `System.Drawing.Common` identity, not to the shim. For compiled code the cost is one pass B replacement (`PdfStorageProvider`). For scripts it decides whether D6 works at all. S4 (IronPython spike) should test: (a) what `clr.AddReference('System.Drawing')` and `clr.AddReference('System.Drawing.Common')` load on the Mac, (b) whether renaming the shim assembly to `System.Drawing.Common` (with the same version `0.0.0.0` and public key token `cc7b13ffcd2ddd51`, which would need public signing with the .NET open-source public key) or a `System.Drawing` facade plus a resolver hook is needed. Cheap to change now, because only the project and assembly name are affected.
- [M1.2a] `PdfStorageProvider` is a pass B only replacement that throws. Real PDF export on the Mac needs a PDF writer that does not take a `System.Drawing.Image` (post-MVP; with the sharpPDF question above). M1.3 should keep it when it makes the replacements permanent for the shadow build.
- [M1.2a] `mac/tools/api-inventory` cannot see enums used only through inlined constants (found: `ImageFlags`). If it matters, extend it to scan the pass A assemblies for fields of those enums via the source (Roslyn `IFieldSymbol` constant uses) rather than IL. M1.2b and M1.2c should expect more such enums if they add code paths, and M1.7 CI should run `drawing-coverage` as a guard (it needs the WindowsDesktop pack: run a pass A build first).
- [M1.2a] Add `mac/tools/drawing-coverage` to `ComicRack.Mac.slnx` with the other tools in M1.3 (it builds on its own today).
- [M1.2a] Static properties that Core code reads at type initialization will throw until M1.2b/M1.2c: `ImageFormat.Png/Jpeg/...`, `SystemFonts.*`, `SystemBrushes.*`, `SystemPens.*`, `Brushes.*`. M1.2b should make `ImageFormat` a real class (with `Guid`) first, since `Save` and the codecs need it. Text (`Font`, `StringFormat`, `Graphics.DrawString`) stays unsupported for the MVP.
- [M1.2a] The 5 remaining pass B errors (M1.3): `KnownColorTableEx.cs` (a constructor at line ~120) reflects into the private `System.Drawing.KnownColorTable.colorTable` of the assembly that holds `Color` (guardrail 6: private reflection; on .NET 10 that assembly is `System.Drawing.Primitives` and the field may be named differently, so the lookup can return null and throw) and then subscribes to `Microsoft.Win32.SystemEvents.UserPreferenceChanging`, which throws `PlatformNotSupportedException` outside Windows. Referencing the `Microsoft.Win32.SystemEvents` package fixes the compile only; check the runtime behaviour in M1.3 and prefer a `Replacements/` stand-in for `SystemEvents` (a no-op event) over the package. `UserCredentialsDialog.cs` needs `UIPermission` (`System.Security.Permissions` package or a marker type).
- [M1.2b] `Bitmap(Image original, int width, int height)` still throws (2 Core call sites: `BitmapExtensions`, `UserCredentialsDialog`). It needs `DrawImage`; M1.2c should implement it once `Graphics` works (decide the interpolation: GDI+ uses the `Graphics` default for a new bitmap).
- [M1.2b] The check-3 pattern from the M0.4 item (1-pixel checkerboard downscaled 4:1 gives mid grey) needs `DrawImage` and so moves to the M1.2c tests. The M1.2b task text has no drawing, so it was not written here.
- [M1.2b] TIFF pages are not decoded (`ArgumentException`), and BMP/GIF/TIFF cannot be saved. The MVP does not need either (CBZ pages are JPEG, PNG or WebP; export is post-MVP, and `StorageProvider` is the export path that asks for `ImageFormat.Tiff/Bmp/Gif`). If a TIFF page shows up, S3 (ImageIO) can decode it; a BMP writer is about 30 lines.
- [M1.2b] Members that scripts could call and the Core never does (D6, P4): `Image.RawFormat`, `HorizontalResolution`/`VerticalResolution`/`SetResolution` (Skia has no DPI; return 96), `PropertyItems`, `Palette`, `GetThumbnailImage`, `Bitmap(Stream)`/`Bitmap(string)`/`Bitmap(Image)`, `MakeTransparent`, `Image.FromHbitmap`, `ImageCodecInfo.GetImageDecoders`. The S4 spike can check which of them real scripts use.
- [M1.2b] M1.8 watch item: a decoded JPEG is opaque BGRA in memory, so the Core's `Copy`/`FastBitmapLock` (24 bpp `LockBits`, then a copy to 32 bpp) convert 4 to 3 bytes and back. It costs about 6 to 13 ms per 6 MP page in Debug. Do not optimize before the M1.8 numbers say so.
- [M1.2b] For M2.2a: Core page bitmaps are usually `Format32bppArgb`, which is `Unpremul` storage even when every pixel is opaque. Drawing it works, but Skia adds a premultiply step per draw. If the grid or reader shows it, reinterpret the pixels as opaque (`SKPixmap.WithAlphaType(SKAlphaType.Opaque)` over the same memory) instead of copying.
- [M1.2b] The finalizer releases bitmaps that the Core forgets to dispose, but only when the GC runs; `GC.AddMemoryPressure` makes it run earlier. If M1.8 shows GC thrash with many large pages, revisit the pressure bookkeeping in `Image`.
- [M1.2b] Apply the `RuntimeIdentifier` fix of `Compat.Drawing.Tests` (see "Disk (important)" in the M1.2b facts) to `mac/tests/Compat.Forms.Tests` and `mac/tools/drawing-coverage` (about 290 MB each otherwise), and to every future test or tool project that references the shim (M1.3 adds the tools to the solution). With the 3 GB free-space STOP rule this matters on this machine.
- [M1.2b] M1.7 CI must run `dotnet test mac/tests/Compat.Drawing.Tests` after a pass A build (the drift guard needs the reference pack), like `Compat.Forms.Tests`.
- [M1.2c] **Large reductions alias** with `HighQualityBicubic`/`Bicubic`/`High` (Mitchell without a pre-filter: 119..255 on stripes whose mean is 170) while `HighQualityBilinear` (mipmaps) gives 159..191. The mapping is as specified, but if M1.8 or M2.2a thumbnails look wrong, map these modes to linear plus mipmaps when the reduction is larger than 2:1 (one `if` in `Graphics.Sampling`/`DrawImageCore`), or reduce in two steps. The Core's own `ImageProcessing` resizers may make this moot; M1.8 should measure which resize path the thumbnail and page code really takes.
- [M1.2c] `ImagePool.CreateErrorPage` calls `DrawString` with `FC.Get(SystemFonts.MenuFont, 32f)` and will throw `NotSupportedException` on the Mac; `CreateErrorThumbnail` works (Clear, `DrawImage`). `Resources.ErrorPage`/`RedCross` are `.resx` file references of type `System.Drawing.Bitmap, System.Drawing`: the shadow project embeds no `.resx`, so M1.3/M1.5a must supply those two bitmaps (a `Replacements/` file that decodes embedded PNG/JPEG through `Image.FromStream`) and either provide text (a minimal `DrawString`, or an overriding `CreateErrorPage` in the host) or draw the error page without text. Text stays unsupported in the shim for the MVP.
- [M1.2c] `KnownColorTableEx` (M1.3): its constructor looks up `System.Drawing.SafeNativeMethods` and `KnownColorTable` by name in the assembly that holds `Color`; on .NET 10 both lookups return null and the next call throws `NullReferenceException`. The `SystemBrushes`/`SystemPens` field lookups are null-safe. This is in addition to the `SystemEvents` problem noted under M1.2a.
- [M1.2c] Not done, cheap, and probably needed by the first UI code (M2.2a) or by scripts (D6): `Graphics.Save`/`Restore` (the `SaveState` extension uses them), `SetClip`/`Clip`, `FillEllipse`, `DrawLine`/`DrawRectangle` with a real `Pen`, `Brushes.*`/`Pens.*` for all named colours, `TextRenderingHint` (store only), the `Matrix00`..`Matrix44` properties of `ColorMatrix`, `ImageAttributes.SetColorMatrix(ColorMatrix, ColorMatrixFlag[, ColorAdjustType])`, `SetWrapMode`, `Graphics.Transform`/`Matrix` overloads, mirrored `DrawImage` (negative size) and non-pixel `GraphicsUnit`s. Wait for a caller (run the Core, then the app, then S4) before adding them.
- [M1.2c] The `HasOutstandingLocks` rule means `Graphics.FromImage` fails on a bitmap that is locked, which GDI+ allows. Nothing needed it so far; relax the rule only if M1.5b/M1.8 hits it.
- [M1.2c] Closed by this task, from earlier items: `Bitmap(Image, int, int)` (M1.2b), the check-3 pattern for the tests (M0.4, M1.2b), and the `RuntimeIdentifier` fix for `Compat.Forms.Tests` and `drawing-coverage` (M1.2b). `drawing-coverage` still has to be added to the solution by M1.3 and run in CI by M1.7.
- [M1.3] Closed by this task, from earlier items: the 13 pass B errors of the `Replacements/` files that mirror WinForms-facing originals (gone since M1.2a, verified: none left); the `RendererImage` stub (replaced by the real file); `KnownColorTableEx`/`UserCredentialsDialog` (pass B replacements); the `ErrorPage`/`RedCross` resources (embedded and decoded through the shim); `api-inventory`, `census` and `drawing-coverage` are in the solution; `PdfStorageProvider` stays a pass B replacement.
- [M1.3] M1.5a must override `ImagePool.CreateErrorPage()` in the host (or the shim must get a text path): it throws at `SystemFonts.MenuFont`. Simplest: copy `Resources.ErrorPage` and return it without text, or draw the message with Skia text in the app. `CacheManager` creates the `ImagePool` itself, so the override needs either a subclass hook or a replacement of the factory; check how `CacheManager` can be given a pool before M1.5a starts (`ImagePool` is built inside the `CacheManager` constructor, so a pass B `CacheManager` replacement may be needed; that file is not yet replaced).
- [M1.3] Unlike the original, `Resources.ErrorPage` and the other bitmaps are fresh instances on each access (callers own and dispose them). That is safe for every Core caller, but each access decodes (ErrorPage is 1000 x 1402 JPEG). If M1.8 shows `CreateErrorPage` hot (many failed pages), cache the decoded bytes, not the bitmap.
- [M1.3] M1.7 CI: run `dotnet build mac/ComicRack.Mac.slnx` (now includes the shadow projects, so the CI also guards pass B), then `dotnet test` of both test projects and `dotnet run --project mac/tools/drawing-coverage`; add a pass A build (`-p:CensusPass=A`) first on a fresh machine for the reference pack. Consider also `dotnet run --project mac/tools/census -- --pass A` and `--pass B` as a guard that both stay at 0 (about 40 s each); they rewrite `mac/reports/census-*.md`, so CI should check `git diff --exit-code` only on the totals, not the timings.
- [M1.3] Warning noise: the solution now prints about 550 warnings from the linked sources (`CS3001/2/3` CLS compliance, `SYSLIB0003/4/21`, `CA2022`, `CS0649`, `CS0472`). They are not behaviour issues of the port, but they hide new warnings of the mac code. If wanted, add `NoWarn` for those ids (and `CLSCompliant(false)`-style ids) to `Shadow.props` only; mac-owned projects are warning-free.
- [M1.3] The MVP path still has runtime gaps that compiling does not show: `Thread.Abort` sites, `BinaryFormatter` fallbacks (cYo.Common `DiskCache`), `Win32` P/Invoke classes that are linked but never called, and Common's missing `Resources` (see the M1.3 facts). M1.5a/M1.5b are where they will surface; record them there.

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
