# ComicRackCE: the path to a native macOS application

> **Status:** Research complete. Scenario validated by the project owner on 2026-10-13. Revised the same day: D6, script UI compatibility (§5.9).
> **Audience:** (1) the project owner, to decide and track; (2) Claude agentic planning and implementation sessions, which start from this file.
> **Scope:** feasibility, technology inventory, options with prior art, the chosen architecture, typical migration challenges, and an agent-ready phase plan.
>
> Agents: read [§0 Validated decisions](#0-validated-decisions-binding-for-all-sessions) and [§9 Guardrails](#9-agentic-execution-plan) before planning anything. File references use `path:line` and were checked against commit `7fd9fd3` (`master`, Oct 2026). Line numbers drift, so re-locate code by symbol name.

---

## Table of contents

0. [Validated decisions (binding for all sessions)](#0-validated-decisions-binding-for-all-sessions)
1. [Executive summary](#1-executive-summary)
2. [Current technology stack and dependencies](#2-current-technology-stack-and-dependencies)
3. [Windows-coupling map](#3-windows-coupling-map)
4. [Options considered and prior art](#4-options-considered-and-prior-art)
5. [Target architecture (chosen scenario)](#5-target-architecture-chosen-scenario)
6. [macOS v1 scope](#6-macos-v1-scope)
7. [Typical challenges of this kind of migration](#7-typical-challenges-of-this-kind-of-migration)
8. [Risk register](#8-risk-register)
9. [Agentic execution plan](#9-agentic-execution-plan)
10. [Open questions for later decisions](#10-open-questions-for-later-decisions)
11. [Appendices: inventories and references](#11-appendices)

---

## 0. Validated decisions (binding for all sessions)

| # | Decision | Value | Consequence |
|---|---|---|---|
| D1 | Migration scenario | **Shared cross-platform Core + new Avalonia UI for macOS** | The WinForms UI is **not** ported to macOS. A new `ComicRack.Mac` (Avalonia) app runs on the same Core as the Windows app. |
| D2 | Windows build policy | **Windows app stays on .NET Framework 4.8. Shared code multi-targets `net48;net10.0`** | Windows users must see **no behavioural change**. Every change to shared code must still compile and behave the same under `net48`. Windows-only code stays where it is. |
| D3 | Guiding principle | **Change as little existing code as possible.** Put cross-platform concerns in dedicated layers (shims, a Platform Abstraction Layer, a new UI project) | Prefer adding files and projects over editing existing ones. Edits to existing files must be small, mechanical and reviewable, and should be upstreamable to `maforget/ComicRackCE`. |
| D4 | macOS v1 feature scope | Reader, library, smart lists, metadata editing, CBZ/CBR/CB7/CBT/PDF/WebP/HEIF/AVIF/JXL/JPEG2000 **plus Python scripts/plugins** | Remote library sharing, Wi-Fi device sync, USB/MTP sync and DjVu are **deferred** (post-v1). |
| D5 | Hardware and distribution | **Apple Silicon (arm64) only, macOS 14 Sonoma or later, Developer ID signed + notarized DMG** (a Homebrew cask is optional) | No Intel/universal binaries, no Mac App Store and no sandbox. Every bundled native library is arm64 only. |
| D6 | Script compatibility (added 2026-10-13) | **Existing scripts, including those that build WinForms UI, must run on macOS unmodified.** Asking authors to rewrite scripts is not acceptable. | A dedicated **Script UI Compatibility Layer** (§5.9) provides the `System.Windows.Forms` and `System.Drawing` APIs to scripts on macOS, rendered with Avalonia. It is a v1 deliverable (phase P4). |

Derived defaults (adopted unless the owner overrides them):
- Runtime **.NET 10 (LTS, supported until Nov 2028)**, self-contained, `osx-arm64`. Not NativeAOT, because IronPython needs dynamic code generation (see §7.9).
- UI toolkit **Avalonia 12.x**, the free, open-source (MIT) cross-platform UI framework, used to **write new UI code**. Do not confuse it with **Avalonia XPF**, a separate *paid* product from the same company that runs existing **WPF** apps on macOS unchanged. XPF does not apply here because ComicRack's UI is WinForms, not WPF. Paid "Accelerate" add-ons are not required either.
- 2D imaging on macOS through **SkiaSharp** (already Avalonia's renderer).
- Scripting stays on **IronPython 2.7.12** (supports `netstandard2.0`/`netcoreapp3.1`, so it runs on .NET 10). Existing Python 2 scripts keep working, and the IronPython 3.4 decision is deferred.

---

## 1. Executive summary

**Feasibility: yes, with moderate-to-high effort. The cost is concentrated in one place: the macOS UI.**

ComicRackCE is about 192k lines of decompiled C# in 7 projects on .NET Framework 4.8 + WinForms + GDI+ (System.Drawing), plus about 20 native Windows binaries. Nothing in it runs on macOS today: WinForms and System.Drawing are Windows-only on modern .NET, and the code calls Win32 in many small places.

The coupling is **unevenly distributed**, which is what makes the chosen scenario viable:

| Layer | Size | macOS outlook |
|---|---|---|
| Domain/engine logic: ComicBook model, matchers/smart lists, database (XML), scanner, metadata, queues, cache, providers | ~84k LOC (`ComicRack.Engine`, `cYo.Common`) | **Mostly portable.** Only ~17 of 587 Engine files outside `Controls/` and `Display/` reference UI assemblies, and they mostly use trivial types (`SortOrder`, `Keys`, `ContentAlignment`, `Application.ProductName`, `Clipboard`). |
| Imaging: `Bitmap`/`Graphics`/`LockBits` in ~100 Engine and Common files | woven through the engine | **The main core blocker.** It is solved by a dedicated imaging layer (a System.Drawing-compatible shim over SkiaSharp on .NET 10; real GDI+ on net48). |
| Native codecs and archivers (7z, PDFium, libwebp, libjxl, libheif, djvulibre) | ~20 PE binaries | Replaceable by arm64 dylibs or managed libraries behind a native-library resolver. |
| OS services: shell, recycle bin, registry, NTFS alternate streams, WCF IPC, power, WMI, CredUI, WPD | ~45 files with P/Invoke | Moved behind a Platform Abstraction Layer (PAL) with Windows (existing code) and macOS implementations. |
| UI: WinForms, 38 forms, ~25 user controls, custom owner-drawn controls, OpenGL reader | ~106k LOC (`ComicRack`, `cYo.Common.Windows`, `cYo.Common.Presentation`, `Display.Forms`) | **Rewritten for macOS in Avalonia** (new code). The Windows UI is untouched. |
| Third-party Python scripts that build WinForms dialogs, panels and owner-drawn overlays | Outside this repo (community plugins) | **Run unmodified (D6)** through a script-only WinForms/System.Drawing compatibility layer, rendered with Avalonia (§5.9). It is a narrow, script-facing API layer, not a port of ComicRack's own UI. |

**Recommended approach (validated):**
1. Build "shadow" .NET 10 projects that **link the existing source files** (no file moves).
2. Add tiny **compat shims** so data-model files compile unchanged.
3. Introduce a **PAL** for OS services, with today's Win32 code as the Windows implementation.
4. Add a **headless CLI harness**, so agents can verify the core on macOS without a GUI.
5. Build the **Avalonia macOS app** on top.
6. Add the **Script UI Compatibility Layer**, so community scripts with WinForms UI run unmodified (§5.9).
7. Package as a notarized arm64 `.app`.

**Effort (order of magnitude, low confidence):**

| Work | Estimate |
|---|---|
| Core + PAL + imaging shim + codecs + scripting engine | 20–30 % of total |
| Script UI Compatibility Layer (WinForms API for scripts) | 15–20 % |
| macOS Avalonia UI v1 | 40–50 % |
| Packaging, CI, signing, beta hardening | 10–15 % |

In human-equivalent terms, 8–14 person-months of senior .NET work. With Claude agent sessions doing most implementation and a human reviewing and testing on a Mac, plan for about **15–24 weeks of calendar time** across about 85–140 well-scoped agent tasks. The bottleneck is human review and on-device testing, not code generation (see §9.6).

**Why not the alternatives:**
- **Wine/CrossOver:** zero code changes, but non-native, and it depends on Rosetta 2, which Apple largely retires in macOS 28 (~fall 2027).
- **WinForms-compat runtime for the whole app:** keeps ComicRack's UI code but makes the project own a *complete* WinForms implementation on macOS, including Win32 message hacks. Highest technical risk, and it still looks like Windows. (A much smaller, script-facing WinForms layer *is* part of the plan, §5.9. Scripts use a narrow, well-behaved slice of WinForms, and community scripts are not ours to rewrite.)
- **Native SwiftUI:** best fit and finish, but the most UI work and a macOS-only UI.

Details in §4.

---

## 2. Current technology stack and dependencies

### 2.1 Solution layout

All projects are SDK-style `.csproj` targeting **`net48`**, with `LangVersion 14` and PolySharp (`directory.build.props`). Every project sets `UseWindowsForms=true` and `ImportWindowsDesktopTargets=true`. There are **no test projects**.

| Project | Kind | .cs files | LOC | Role | Notable dependencies |
|---|---|---|---|---|---|
| `cYo.Common` | lib | 326 | 37.4k | Utilities: collections, compression (7z interop), drawing/image processing, IO/disk cache, localization (TR), net, runtime (ini, single instance, crash watchdog), threading, **Win32 interop**, XML | DynamicExpresso.Core 2.19.3, SharpZipLib 1.4.2, System.Management 10.0.10, System.Text.Json 10.0.10, Microsoft.VisualBasic, System.ServiceModel |
| `cYo.Common.Windows` | lib | 161 | 30.2k | WinForms control library: ItemView, TabBar, Popup, theming (dark mode), DPI, localization of forms | Windows7.Multitouch.dll (vendored) |
| `cYo.Common.Presentation` | lib | 71 | 8.6k | Renderer abstraction (`IBitmapRenderer`), GDI+ and **OpenGL (Tao)** renderers, overlay panels, XHTML mini-renderer (Ceco) | Tao.OpenGl.dll, Tao.Platform.Windows.dll (vendored), SharpZipLib |
| `ComicRack.Engine` | lib | 587 | 46.9k | **Domain core:** ComicBook model, database, matchers, providers (readers/writers), cache, sync, network, metadata, backup | SharpCompress 0.50.4, SharpZipLib, PDFiumSharpV2 1.1.4 + bblanchon.PDFium.Win32 153, LibHeifSharp 3.2.0, CSJ2K 3.0.0, MySqlConnector 2.6.1, Microsoft.Bcl.Numerics, sharpPDF.dll (vendored), native libheif/aom/de265/x265 |
| `ComicRack.Engine.Display.Forms` | lib | 10 | 7.7k | Reader controls: `ImageDisplayControl`, `ComicDisplayControl` | Windows7.Multitouch |
| `ComicRack.Plugins` | lib | 21 | 1.9k | IronPython host, plugin model, automation interfaces | IronPython 2.7.4, Microsoft.CSharp |
| `ComicRack` | WinExe | 172 | 59.9k | Main app: MainForm, 30 dialogs, views, config, menus, program startup | Microsoft-WindowsAPICodePack-Shell 1.1.5, `UseWPF=true` (only to satisfy CodePack) |

Payload in `ComicRack/Output/`:
- `Languages/`: 19 cultures × ~73 XML files (6.9 MB)
- `Help/`
- `Scripts/`: sample Python plugins + `Package.ini`
- `Resources/`: textures, icons, native binaries (36 MB)

### 2.2 Native and vendored binaries (all Windows PE)

| Binary | Arch | Location | Used for | Loaded via |
|---|---|---|---|---|
| `7z.dll`, `7z.exe` | x64 + x86 | `ComicRack/Output/Resources/{x64,x86}` | Default CBZ/CBR/CB7/CBT reader; **all ComicInfo.xml writes** (7z.exe) | `LoadLibrary`/`GetProcAddress` (`cYo.Common/Compression/SevenZip/SevenZipFactory.cs`); `Process` for exe |
| `pdfium.dll` | x64 + x86 | NuGet `bblanchon.PDFium.Win32`, renamed by PostBuild xcopy | PDF reading (default engine) | PDFiumSharpV2 |
| `libwebp.dll`, `libsharpyuv.dll` | x64 + x86 | `Resources/{x64,x86}` | WebP decode/encode | `DllImport("Resources\\x64\\libwebp.dll")` hard-coded paths (`ComicRack.Engine/IO/Provider/WebpImage.cs`) |
| `jxl.dll`, `jxl_cms.dll`, `brotli*.dll` | x64 | `Resources/x64` | JPEG XL | `DllImport("jxl.dll")` + `SetDllDirectory` |
| `libheif.dll`, `aom.dll`, `libde265.dll`, `libx265.dll` | x64 | `ComicRack.Engine/DLL/x64` → `Resources/x64` | HEIF/AVIF | LibHeifSharp + `DllImport("libheif")` |
| `djvm.exe`, `ddjvu.exe`, `c44.exe`, `libdjvulibre.dll`, `libjpeg.dll`, `libtiff.dll`, `libz.dll` | x86 | `Resources/` | DjVu read/write | `Process` with hard-coded `Resources\\…` paths |
| `sharpPDF.dll` | managed (old) | `ComicRack.Engine/DLL` | PDF export | reference |
| `Tao.OpenGl.dll`, `Tao.Platform.Windows.dll` | managed | `cYo.Common.Presentation/DLL` | Hardware-accelerated reader | reference (WGL) |
| `Windows7.Multitouch.dll` | managed | `ComicRack.Engine.Display.Forms/DLL` | Touch gestures | reference |
| Ghostscript (`gswin64c.exe`) | external | found via registry | Optional PDF engine | registry + `Process` |

Native search path: `ComicRack/Program.cs:1146` calls `NativeLibraryHelper.RegisterDirectory("Resources\\x64")`, which uses kernel32 `SetDllDirectory`.

### 2.3 Persistence and data

| Item | Where | Format | Notes |
|---|---|---|---|
| Library database | `%APPDATA%\cYo\ComicRack Community Edition\ComicDb.xml` (`ComicRack.Engine/SystemPaths.cs`) | `XmlSerializer` | Portable. File paths inside are absolute Windows paths (see §7.2). Optional MySQL/MSSQL via `ComicRack.ini` `DataSource`. |
| Settings | `Config.xml` (`ComicRack/Config/Settings.cs`) | `XmlSerializer` | Portable format. It contains window layouts and keyboard maps that are UI-specific. |
| Engine config | `ComicRack.ini` (`cYo.Common/Runtime/IniFile.cs`) | INI | Search order uses `Application.CompanyName`/`ProductName` (WinForms). |
| Caches | `%LOCALAPPDATA%\…\Cache\{Thumbnails,Images,Files,CustomThumbnails}` | files + index | The index is saved as **BinaryFormatter** and XML (`cYo.Common/IO/DiskCache.cs:260,312`). BinaryFormatter is removed in .NET 9+. |
| Per-file metadata | NTFS alternate data streams (`IO/Provider/NtfsInfoStorage.cs`, `cYo.Common/Win32/AlternateDataStreamFile.cs`) | ADS | Can be disabled with `DisableNtfs`. |
| Backups | zip (SharpZipLib) | zip | Portable. |

### 2.4 Build, CI and release

- GitHub Actions `nightly.yml`, `release.yml` and `pr-artifact-upload.yml` run on `windows-latest`: msbuild, PowerShell `Compress-Archive`, Inno Setup (`Installer.iss`), vc_redist, Winget.
- `ComicRack.csproj` PostBuild uses **cmd syntax** (`xcopy`, `rmdir /S /Q`, `del`). CompressFolders calls `powershell` (Windows PowerShell).
- `compile_res_file.bat` produces the Win32 resource (`myressources.res`) via VS DevCmd + `rc.exe`.
- PreBuild writes `CurrentCommit.txt`/`isDirty.txt` via `git` (portable).

### 2.5 Licensing context

- The repository is **GPLv2** (`LICENSE.txt`) and is a decompilation of the original closed-source ComicRack (see README).
- GPLv2 is generally considered incompatible with Mac App Store terms (the VLC precedent). D5 avoids the App Store.
- Third-party licences to carry into the `.app`:
  - 7-Zip: LGPL, with the **unRAR licence restriction** (RAR decompression code may not be used to re-create RAR compression)
  - PDFium: BSD-3
  - libheif: LGPL-3
  - x265: GPL-2
  - libde265: LGPL-3
  - aom: BSD-2
  - libjxl: BSD-3
  - libwebp: BSD-3
  - djvulibre: GPL-2
  - SkiaSharp and Avalonia: MIT
  - IronPython: Apache-2.0

---

## 3. Windows-coupling map

Severity: 🟥 blocker on macOS · 🟧 needs adaptation · 🟩 portable as-is.

### 3.1 Engine and Common (what the shared Core needs)

| Area | Evidence | Sev. | Resolution in chosen architecture |
|---|---|---|---|
| **System.Drawing imaging** (`Bitmap`, `Graphics`, `LockBits`, `ImageCodecInfo`, `ColorMatrix`, `Image.FromStream`) | ~100 files. In non-UI Engine + Common: `Bitmap` ×654, `Graphics` ×117, `PixelFormat` ×141, `LockBits` ×28, `Font` ×58, `StringFormat` ×43, `GraphicsPath` ×19, `ColorMatrix` ×7. Key files: `cYo.Common/Drawing/{BitmapExtensions,ImageProcessing,FastBitmap,GraphicsExtensions}.cs`, `ComicRack.Engine/IO/{MemoryOptimizedImage,PageImage,ThumbnailImage}.cs`, `IO/Cache/ImagePool.cs`, `Drawing/PageRendering.cs` | 🟥 | **Imaging layer** (§5.4): a System.Drawing-compatible shim over SkiaSharp for net10; real GDI+ on net48. `Point/Size/Rectangle/Color` already live in the cross-platform `System.Drawing.Primitives` and need nothing. |
| Codec P/Invokes with Windows paths | `WebpImage.cs` (hard-coded `Resources\\x64\\libwebp.dll`), `JpegXLImage.cs` (`jxl.dll`), `HeifAvifImage.cs` (`libheif`) | 🟥 | `NativeLibrary.SetDllImportResolver` in the PAL maps logical names to `Contents/Frameworks/*.dylib`. One small edit replaces the hard-coded path strings with a logical name. |
| Archive engine (7z.dll via COM vtables, 7z.exe for writes) | `cYo.Common/Compression/SevenZip/*`, `ComicRack.Engine/IO/Provider/Readers/Archive/SevenZipEngine.cs`, `FileBasedAccessor.cs:31` (ComicInfo writes **always** use 7z.exe) | 🟥 | On macOS, default to **SharpCompress** for reading (already supported: `CbEngines.SharpCompress`). For writing, bundle the official **7-Zip `7zz` macOS console binary** and reuse the existing exe mode with PAL-resolved paths. Alternatively, a managed writer for CBZ (SharpZipLib, already used by `CbzStorageProvider`). |
| PDF (PDFium Win32 package; Ghostscript via registry) | `Readers/Pdf/PdfiumReaderEngine.cs`, `cYo.Common/Drawing/PdfImages.cs` | 🟧 | Add `bblanchon.PDFium.macOS` (arm64 dylib) for net10. Ghostscript: look up `gs` on PATH/Homebrew (optional). |
| DjVu (x86 exes) | `Readers/DjvuComicProvider.cs`, `DjVuImage.cs`, `Writers/DjVuStorageProvider.cs` | 🟧 | **Deferred (D4).** Later: Homebrew `djvulibre` CLIs via PAL tool locator. |
| NTFS alternate data streams | `NtfsInfoStorage.cs`, `cYo.Common/Win32/AlternateDataStreamFile.cs` | 🟧 | PAL `IFileMetadataStore`: Windows = ADS, macOS = no-op (v1) or `xattr` (later). |
| BinaryFormatter cache index | `cYo.Common/IO/DiskCache.cs:260,312` | 🟥 (net10) | `#if NETFRAMEWORK` around the binary path. The XML index already exists and is preferred on load. |
| `Thread.Abort` | `ComicScanner.cs:97`, `cYo.Common/Threading/{ThreadUtility.cs:247,BackgroundRunner.cs:77}` (+ UI files) | 🟥 (net10: throws `PlatformNotSupportedException`) | Cooperative cancellation for net10 paths: set a flag, then `Join` with timeout, then abandon. Keep `Abort` on net48 via `#if`. |
| CodeDom compile | `cYo.Common/Reflection/CodeDomDuckTypeGenerator.cs` | 🟧 | Appears unused. Exclude it from the net10 link set. |
| Private reflection into framework internals | `KnownColorTableEx.cs` (System.Drawing internals), `ComicRack.Engine/IO/WebComic.cs:370` (`UriParser.m_Flags`) | 🟧 | Exclude (UI theme) / guard with `#if NETFRAMEWORK` (WebComic). |
| WCF server/client (library sharing), `System.Web.Services` usings | `IO/Network/ComicLibraryServer.cs`, `ComicLibraryClient.cs`, `ServerRegistration.cs` | 🟥 | **Deferred (D4).** Exclude `IO/Network/*` from the net10 link set and stub `NetworkManager`. |
| WCF named-pipe single instance | `cYo.Common/Runtime/SingleInstance.cs` | 🟥 | macOS: LaunchServices already enforces a single instance per bundle. Open-file events arrive through Avalonia `IActivatableLifetime`/`UrlsOpened` (or an Apple Event handler). No IPC needed. |
| WPD/MTP device sync, removable-drive sync | `cYo.Common/Win32/PortableDevices/*` (~3.1k lines COM), `Engine/Sync/*` | 🟥 | **Deferred (D4).** Exclude WPD and stub `DeviceSyncFactory` on net10. The Wi-Fi provider is portable sockets and is the first post-v1 candidate. |
| Shell, recycle bin, file ops, icons | `cYo.Common/Win32/{ShellFile,ShellFolder,ShellPidl,ShellIcon,FileExplorer,FileOperations/*}.cs`, `Microsoft.VisualBasic` | 🟧 | PAL `IShellServices`: Trash (`NSFileManager trashItemAtURL`), Reveal in Finder (`NSWorkspace activateFileViewerSelectingURLs`), open with default app (`open`). |
| Registry, file associations | `cYo.Common/Win32/ShellRegister.cs`, `IO/Provider/FileFormat.cs:80-138` | 🟧 | macOS: `Info.plist` `CFBundleDocumentTypes`/`UTImportedTypeDeclarations`. Register/unregister becomes a no-op in the PAL. |
| Power, memory, WMI, CredUI, OEM codepage, 8.3 names, `PathGetCharType` | `cYo.Common/Win32/{PowerRequest,MemoryInfo,UserCredentialsDialog,ExecuteProcess,FileMethods}.cs`, `IO/FileUtility.cs` | 🟧 | PAL: `IOPMAssertionCreateWithName`, `sysctl hw.memsize`, a UI callback for credentials, UTF-8 child-process output, real paths, `Path.GetInvalidFileNameChars()`. |
| WinForms types in data model | `ItemViewConfig` (uses `SortOrder`) persisted by `StacksConfig.cs`, `DisplayListConfig.cs`, `ComicBookMetadata.cs`; `Keys` in `ComicDatabase.cs`; `Clipboard`/`DataObject` in `ComicBook.cs:2342`; `Application.ProductName` in `BackupArchiveCreator.cs:109`, `PdfStorageProvider.cs:14`, `IniFile.cs`, `Diagnostic.cs` | 🟧 | **WinForms "types-only" shim** (§5.3): `SortOrder`, `Keys`, `Padding`, `ContentAlignment` and an `Application` facade defined only for net10. Link the WinForms-free `ItemViewConfig`/`ItemViewColumnInfo`/`ItemViewMode` files into Core. `Clipboard` goes through the PAL. |
| Special folders | `SystemPaths.cs:77-83` uses `SpecialFolder.ApplicationData`/`LocalApplicationData` | 🟧 | On macOS .NET maps these to `~/.config` / `~/.local/share`. The PAL `IAppPaths` maps them to `~/Library/Application Support/ComicRack Community Edition` and `~/Library/Caches/…`. |
| Hard-coded backslashes | `"Resources\\…"` in `SevenZipEngine.cs`, `DjVu*.cs`, `WebpImage.cs`, `Program.cs`; `REMOTE:{id}\\{path}` in `ComicLibraryClient.cs:107` | 🟧 | `Path.Combine` (works on both platforms) or PAL resource locator. |
| Case-insensitive path identity | `ComicBookCollection.cs:17`, `ComicDatabase.cs:31,188,254`, `QueueManager.cs:455` (OrdinalIgnoreCase) | 🟩/🟧 | Default APFS is case-insensitive, so keep it. Document the case-sensitive-volume caveat (§7.2). |
| IronPython 2.7.4 (net48 build) | `ComicRack.Plugins/PythonCommand.cs` | 🟧 | IronPython **2.7.12** for the net10 target (D4 + derived defaults). |
| `HttpWebRequest`, `ServicePointManager` | `cYo.Common/Net/HttpAccess.cs:151`, `Program.cs:1126` | 🟩 | Obsolete but functional on .NET 10. Leave as is. |
| XML DB/config, matchers, smart-list parser, DynamicExpresso, SharpCompress, SharpZipLib, MySqlConnector, localization (TR) | — | 🟩 | Portable. |

### 3.2 UI layers (Windows-only by decision D1; listed so the macOS app knows what to rebuild)

| Area | Evidence | Note for the Avalonia app |
|---|---|---|
| Reader rendering | `IBitmapRenderer`/`IHardwareRenderer` (`cYo.Common.Presentation`). Implementations: `BitmapGdiRenderer` (GDI+) and `ControlOpenGlRenderer` (Tao + **WGL**, fixed-function GL). Selection hard-coded in `ImageDisplayControl.SetRenderer` (`ComicRack.Engine.Display.Forms/ImageDisplayControl.cs:1830-1882`) | The reader's page layout, two-page spreads, zoom, magnifier, page-turn animation and paper textures live in `ImageDisplayControl`/`ComicDisplayControl` (~6.1k LOC, WinForms-bound). v1 reimplements the reader view in Avalonia/Skia on top of the portable `ComicBookNavigator`. Later, extract the pure layout math into Core (§5.6). |
| Library views | `ItemView` (4.8k LOC owner-drawn virtualized list/tile/detail view with grouping and stacks) | Avalonia `ItemsRepeater`/`TreeDataGrid` with virtualization; reuse `ItemViewConfig` (persisted) for column/sort state. |
| Main window, 38 forms, ~25 user controls, 61 designer files | `MainForm.cs` 4.6k + 3.9k designer; `ComicBrowserControl` 3.5k; `PreferencesDialog` 1.5k + 3k designer; `ComicBookDialog` 1.2k + 2.6k designer | Rebuild the v1 subset (§6.2) as Avalonia views + view-models. |
| Win32 messages, popups, dark mode, DPI, layered splash, touch | `WndProc` overrides (`Popup.cs`, `ComboBoxEx.cs`, `ListViewEx.cs`, `TreeViewEx.cs`, `MainForm.cs:923`), `MouseWheelDelegater` (`Program.cs:57`), `UXTheme.cs`, `LayeredForm.cs`, `Windows7.Multitouch` | Not needed on macOS: native dark mode, trackpad gestures and Retina are provided by Avalonia/AppKit. |
| Threading | `Control.CheckForIllegalCrossThreadCalls = false` (`Program.cs:1147`); un-marshalled `Invalidate` from scanner/queue threads (`ItemViewItem.cs:108-123`); splash on a 2nd UI thread | **AppKit enforces main-thread UI.** All Core→UI events must be marshalled with `Dispatcher.UIThread.Post` in the macOS view-models (§7.5). |
| Keyboard | `Keys.Control` ×212 (mostly menu `ShortcutKeys` in designers); `CommandKey` enum persisted in settings | macOS uses ⌘. Avalonia `KeyGesture` with `PlatformHotkeyConfiguration`. Keymaps are stored separately per platform. |
| Shell integration | `Win7.cs` (jump lists, overlay icon) via WindowsAPICodePack; `NotifyIcon` tray | macOS equivalents: Dock menu, recent documents (`NSDocumentController`), optional menu-bar extra. |
| HTML | `NewsDialog` (IE `WebBrowser`), `HtmlComicPageControl` (plugin HTML panels with `window.external`) | v1: news as native text. Plugin HTML panels are deferred (§6.3). |

---

## 4. Options considered and prior art

### 4.1 Evaluation matrix

Scores are 1 (poor) to 5 (best). Effort and risk are inverted (5 = least).

| Option | Native feel | Change to existing code | Effort | Tech risk | Longevity | Keeps Windows intact | Verdict |
|---|---|---|---|---|---|---|---|
| **A. Wine/CrossOver bundle** (Wineskin-style `.app` with Wine + .NET 4.8) | 1 | 5 | 5 | 2 | **1** (Rosetta 2 sunset) | 5 | Stopgap only |
| **B. Framework Mono 6.14 + libgdiplus** (runs net48 assemblies directly) | 1 | 4 | 3 | 1 | 2 | 5 | Rejected |
| **C. WinForms-compat runtime on .NET 10** (Mono's managed WinForms + Cocoa/Skia driver + System.Drawing-on-Skia) | 2 | 4 | 1 | 1 | 2 | 4 | Rejected (you'd own a UI runtime) |
| **D. Shared Core + Avalonia macOS UI** ✅ | 4 | 3 | 3 | 4 | 5 | 5 | **Chosen** |
| **E. Shared Core + native AppKit/SwiftUI** (`net10.0-macos` bindings, or Swift + NativeAOT Core) | 5 | 3 | 2 | 3 | 4 | 5 | Viable alternative; mac-only UI |
| F. Eto.Forms / Uno / MAUI (Mac Catalyst) / Modern.Forms | 3–4 | 3 | 2–3 | 3 | 3 | 5 | Not preferred (see 4.3) |
| G. Full rewrite (Swift or cross-platform) | 5 | 0 | 1 | 2 | 5 | n/a | Out of scope (violates D3) |

### 4.2 Notes per option, with prior art

**A. Wine/CrossOver.**
- Prior art: CE already runs on **Linux under Wine**. WineHQ bug reports exist against Wine 11.x (bugs 59820, 60013 and 56788: freezes in the Info dialog and periodic lockups), which shows both that it works and that it is fragile.
- CodeWeavers rates classic ComicRack "Installs, Will Not Run" (stale rating).
- Fatal long-term issue: Windows x86-64 code on Apple Silicon needs **Rosetta 2**. Apple announced at WWDC25 that Rosetta stays fully available through **macOS 27**, then becomes a limited, games-oriented subset from **macOS 28 (~fall 2027)**. macOS 26.4+ already warns users about Rosetta apps.
- Use: an optional community stopgap while D ships. It needs no code changes.

**B. Framework Mono 6.14 (WineHQ, March 2025).**
- Mono was donated to WineHQ in 2024. 6.14 is the first release in five years and adds native macOS arm64 (manually tested only).
- Mono's WinForms on macOS historically used **Carbon (32-bit only)**, so on modern macOS it runs only via the X11 driver under XQuartz.
- Result: non-native look, no Retina-correct rendering guarantees, libgdiplus quirks, and Win32 P/Invokes still break. Rejected.

**C. WinForms-compat runtime.**
- Mono's managed WinForms ("MWF") sits on a pluggable `XplatUI` driver. A 64-bit **Cocoa driver** was built by Filip Navara (eM Client) in 2018, but it was never merged upstream and lives as a fork (per BizHawk developers, who evaluated it).
- On .NET 10 the project would also need a System.Drawing replacement, e.g. the SkiaSharp-backed **GeneXus.Drawing.Common** (different namespace).
- This is the "maximum reuse" dream, but the project would maintain a UI runtime larger than ComicRack itself, the result still looks like Windows, and the ~45 Win32-message hacks would still need work. Rejected.
- Note on naming: **Avalonia** (recommended here) is the free MIT UI framework used to write *new* UI. **Avalonia XPF** is a separate *paid* product that runs an *existing WPF* app on macOS without a rewrite. It would be a shortcut only if ComicRack's UI were WPF. It is WinForms, so XPF is not applicable and not part of the plan.

**D. Shared Core + Avalonia (chosen).**
- Prior art:
  - **Devolutions Remote Desktop Manager** moved a large WinForms + DevExpress app to Avalonia view by view, with the same views running on Windows, macOS and Linux (vendor case study).
  - **Plastic SCM** moved from WinForms + Xamarin.Mac to Avalonia (2022).
  - **DiskDigger** (WinForms → Avalonia), where the author found the learning curve smaller than expected.
  - JetBrains tools also ship Avalonia UIs.
- Avalonia 12 (2026):
  - Has its own native macOS backend: windowing, menus (`NativeMenu`, with standard Edit-menu behaviour), clipboard, drag and drop, tray, file dialogs, accessibility.
  - Renders with Skia over OpenGL/Metal. The v12 docs say Metal is still being migrated and is disabled by default.
- Fit for ComicRack:
  - Skia gives one imaging stack for both the Core shim and the UI.
  - MVVM fits the existing event-heavy domain model.
  - The same Avalonia app could later replace the WinForms UI on Windows. That is not planned, but it keeps the option open.

**E. Shared Core + native AppKit/SwiftUI.**
- The most Mac-like option (e.g. `NSCollectionView` library grid, native PDF and Quick Look integration).
- Two routes:
  - **C# + `net10.0-macos` (Microsoft.macOS workload)**: AppKit bindings, still C#, Core referenced directly.
  - **Swift UI + NativeAOT-compiled Core via a C ABI**: **incompatible with IronPython** (dynamic code), so scripting would need a separate process.
- Higher UI effort than Avalonia, and the UI is mac-only. Keep this as a later "polish" path: Avalonia can host native views, and the Core/PAL work in D is 100 % reusable.

**F. Others.**
- **Eto.Forms:** WinForms-like API mapped to native Cocoa; 2.11 targets `net10.0-macos`. Smaller ecosystem. Owner-drawn virtualized grids like `ItemView` would be custom anyway.
- **Uno Platform:** WinUI API, Skia on macOS.
- **.NET MAUI:** desktop macOS only through Mac Catalyst, which is a weak fit for a desktop library manager.
- **Modern.Forms:** WinForms-like on Skia; dormant since 2022. Its successor **ModernFormsNext** says itself that it is not yet a WinForms replacement and documents no macOS support.

**Domain prior art (comic apps on macOS):**
- **YACReader** (Qt, open source, cross-platform library + reader with ComicVine scraping): the closest open-source reference for macOS UX conventions of a comic library app.
- **Panels**, **Chunky** and **Simple Comic**: native macOS reader UX.
- **Kavita** and **Komga**: server-based.
- **ComicBookLover** (discontinued native Mac library manager).
- None of these has ComicRack's smart lists, scripting ecosystem or metadata depth, which is the reason to port rather than switch.

---

## 5. Target architecture (chosen scenario)

### 5.1 Layer diagram

```
┌───────────────────────────────┐        ┌──────────────────────────────────────┐
│ ComicRack (WinForms, net48)   │        │ ComicRack.Mac (Avalonia 12, net10.0) │
│ + cYo.Common.Windows          │        │  Views / ViewModels / Reader (Skia)  │
│ + cYo.Common.Presentation     │        │  macOS integration (menus, Dock,     │
│ + Engine.Display.Forms        │        │  doc types, Trash, Finder)           │
│   (UNCHANGED, Windows only)   │        │  + ComicRack.Mac.FormsBackend        │
│                               │        │    (renders script WinForms on       │
│ Python scripts use the REAL   │        │     Avalonia, see §5.9)              │
│ WinForms here                 │        └──────────────────┬───────────────────┘
└──────────────┬────────────────┘                           │
               │ project refs (as today)                    │ project refs
               ▼                                            ▼
┌───────────────────────────────────────────────────────────────────────────────┐
│ SHARED CORE  (same .cs files compiled twice)                                  │
│  net48:  existing ComicRack.Engine / cYo.Common / ComicRack.Plugins csproj    │
│  net10:  new "shadow" csproj that <Compile Include> link the same files       │
│          minus excluded Windows-only files                                    │
├───────────────────────────────────────────────────────────────────────────────┤
│ COMPAT SHIMS (net10 only, new assemblies)                                     │
│  cYo.Compat.Drawing  System.Drawing.{Bitmap,Graphics,Imaging,...} on Skia     │
│                      (used by Core AND by scripts' Paint/overlay code)        │
│  cYo.Compat.Forms    System.Windows.Forms API object model + layout engine    │
│                      (no rendering; pluggable IFormsBackend). Core only uses  │
│                      its enums/structs; scripts use the full control model    │
├───────────────────────────────────────────────────────────────────────────────┤
│ PLATFORM ABSTRACTION LAYER (PAL)                                              │
│  cYo.Platform (interfaces + static Platform.Current, net48;net10.0)           │
│  cYo.Platform.Windows  → wraps existing cYo.Common/Win32 code (net48)         │
│  cYo.Platform.MacOS    → libSystem / Foundation / AppKit interop (net10)      │
├───────────────────────────────────────────────────────────────────────────────┤
│ NATIVE: Windows PE (as today)  |  macOS arm64 dylibs in .app/Contents/        │
│                                   Frameworks: pdfium, webp, jxl, heif(+dav1d/ │
│                                   de265), 7zz (tool)                          │
└───────────────────────────────────────────────────────────────────────────────┘
```

### 5.2 The "linked-source shadow project" technique (central to D2/D3)

To keep the Windows build byte-for-byte the same and avoid moving files, **do not retarget the existing csproj files at first**. Add new projects under a new folder:

```
mac/
  Directory.Build.props            # net10.0, Nullable=disable, LangVersion 14, DefineConstants MACOS
  cYo.Common.Core/                 # links ../../cYo.Common/**/*.cs minus Win32/**, CodeDom, ...
  ComicRack.Engine.Core/           # links ../../ComicRack.Engine/**/*.cs minus Controls/**, Display/**, IO/Network/**, Sync/Portable*, ...
  ComicRack.Plugins.Core/          # links ../../ComicRack.Plugins/**/*.cs minus Controls/**
  cYo.Compat.Drawing/              # NEW: System.Drawing shim on SkiaSharp
  cYo.Compat.Forms/                # NEW: WinForms API facade (enums for Core; controls for scripts), see §5.9
  cYo.Platform/                    # NEW: PAL interfaces (also referenced by net48 projects later)
  cYo.Platform.MacOS/              # NEW
  ComicRack.Cli/                   # NEW: headless harness (scan, open DB, decode, thumbnail, run script)
  ComicRack.Mac/                   # NEW: Avalonia app
  tests/                           # NEW: characterization + parity tests (run on net48 AND net10)
```

Example link pattern (illustrative):

```xml
<ItemGroup>
  <Compile Include="..\..\ComicRack.Engine\**\*.cs"
           Exclude="..\..\ComicRack.Engine\Controls\**;..\..\ComicRack.Engine\Display\**;..\..\ComicRack.Engine\IO\Network\**;..\..\ComicRack.Engine\obj\**;..\..\ComicRack.Engine\bin\**"
           LinkBase="Engine" />
</ItemGroup>
```

Rules:
1. **An exclusion list is better than an edit.** If a file is Windows-only, exclude it and provide a net10 replacement file under `mac/…/Replacements/` with the same type name. Do this only when the type is needed; otherwise remove the call through the PAL.
2. **When an edit is unavoidable,** use the smallest construct that works:
   - Option (a): replace a direct Win32 call with a PAL call (behaviour preserved on Windows by the Windows PAL implementation).
   - Option (b): `#if NETFRAMEWORK` / `#if !NETFRAMEWORK` around a few lines.
   - Option (c): `partial` classes, with platform halves in separate files.
3. **Track every edit to existing files** in `mac/EDITS.md` (file, reason, upstreamable yes/no). This is the upstream-merge ledger.
4. **Later (optional, post-v1):** once the shadow projects are stable, convert the originals to multi-target `net48;net10.0` and delete the shadows. Do this only if upstream agrees.

### 5.3 Compat shims (dedicated layers that let current code compile as-is)

**`cYo.Compat.Forms` (the WinForms API facade; the Core uses only its types).**
- One assembly, two consumers:
  - The **Core** uses only the plain types: `SortOrder`, `Keys`, `MouseButtons`, `Padding`, `ContentAlignment` (if not in the base library for net10; verify), and a static `Application` facade exposing `ProductName`, `CompanyName`, `ProductVersion`, `StartupPath` and `ExecutablePath` from assembly attributes / the bundle.
  - **Python scripts** use the full control model (`Form`, `Button`, `ListView`, …), described in §5.9.
- **Enum names and values must match WinForms exactly.** `XmlSerializer` writes enum *names* into `Config.xml`/`ComicDb.xml`, so this preserves file compatibility.
- The assembly holds the API object model, events and layout logic, but **no rendering and no dependency on Avalonia**. Rendering is behind an `IFormsBackend` interface implemented in the macOS app (`ComicRack.Mac.FormsBackend`). So the Core stays UI-toolkit-free, and the facade can be tested headless.
- ComicRack's own WinForms UI files are still **excluded** from Core (D1). The facade exists for scripts, not to run `MainForm`.
- Bootstrapping order: P1 needs only the types, which become the first slice of the facade (one source of truth). The control model grows in P4.

**`cYo.Compat.Drawing` (System.Drawing on SkiaSharp, the largest new component of the core work).**
- Goal: the ~100 Core files that use `Bitmap`/`Graphics`/`Imaging` compile **unchanged** against net10.
- Implements the **used subset only**, discovered by the compiler and an API-usage inventory (task P1.3):
  - `Image`, `Bitmap` (ctor from size/stream/image; `LockBits`/`UnlockBits` returning `BitmapData` with `Scan0`/`Stride`; `GetPixel`/`SetPixel`; `Save(stream, ImageFormat)`; `RotateFlip`; `PixelFormat` common values)
  - `Graphics` (`FromImage`; `DrawImage` overloads incl. `ImageAttributes`/`ColorMatrix`; `FillRectangle`; `DrawRectangle`/`DrawLine`; `DrawString`/`MeasureString` with `StringFormat`; `Clip`; `Transform`/`Matrix`; `InterpolationMode`/`SmoothingMode`/`CompositingMode`; `GraphicsPath` basics)
  - `ImageFormat`, `ImageCodecInfo`/`EncoderParameters` (JPEG quality)
  - `Brush`/`SolidBrush`/`LinearGradientBrush`, `Pen`, `Font`/`FontFamily` (mapped to system fonts), `Region` basics
- Backed by `SKBitmap` (BGRA8888 premultiplied, matching GDI+ `Format32bppPArgb` semantics; document the `Format32bppArgb` differences), `SKCanvas`, `SKPaint` and `SKColorFilter` (for `ColorMatrix`).
- Must **not** be referenced by the net48 build. net48 keeps real GDI+.
- Fidelity is verified by perceptual-diff tests (P3) that run the same operations on net48/Windows and net10/macOS.
- Considered alternative: adopt **GeneXus.Drawing.Common** (SkiaSharp-backed System.Drawing API, MIT) with namespace aliasing (`global using` aliases). Evaluate it in spike S2 before writing from scratch.
- Rejected alternative: explicit refactor of all Core files to an `IImage` abstraction. It is cleaner, but it touches ~100 files that upstream also changes, so it causes heavy merge friction. It can be done incrementally post-v1.

### 5.4 Platform Abstraction Layer (PAL)

`cYo.Platform` (targets `net48;net10.0`) defines narrow interfaces and a `Platform.Current` service locator, initialized at app start by the host (WinForms app or Avalonia app).

| Interface | Windows impl (wraps existing code) | macOS impl |
|---|---|---|
| `IAppPaths` (app data, local cache, resources, scripts, temp) | `SystemPaths` logic as today | `~/Library/Application Support/ComicRack Community Edition`, `~/Library/Caches/…`, `Contents/Resources`, `-local` → next to `.app` |
| `INativeLibraryResolver` (logical name → path) | `SetDllDirectory` + `Resources\x64` | `NativeLibrary.SetDllImportResolver` → `Contents/Frameworks/lib*.dylib` |
| `IToolLocator` (7z, gs, djvu tools) | `Resources\x64\7z.exe`, registry for gs | `Contents/MacOS/7zz`, `/opt/homebrew/bin/gs`, PATH |
| `IShellServices` (Trash, reveal, open with default app, file icons) | `ShellFile`, `FileExplorer`, `ShellIcon`, `SHFileOperation` | `NSFileManager.trashItemAtURL`, `NSWorkspace.activateFileViewerSelectingURLs`, `open`, `NSWorkspace.iconForFile` |
| `IFileMetadataStore` (per-file info) | NTFS ADS (`AlternateDataStreamFile`) | No-op (v1); `xattr` later |
| `IPowerManagement` (prevent sleep while reading/syncing) | `PowerRequest`, `SetThreadExecutionState` | `IOPMAssertionCreateWithName` |
| `ISystemInfo` (RAM, CPU) | `MemoryInfo` (GlobalMemoryStatusEx + WMI) | `sysctl hw.memsize`, `hw.ncpu`, `machdep.cpu.brand_string` |
| `IClipboard` (text, image, file list) | WinForms `Clipboard`/`DataObject` | Avalonia clipboard (injected by UI host) |
| `ICredentialsPrompt` | CredUI | UI-host callback (Avalonia dialog) + Keychain later |
| `IFileAssociations` | `ShellRegister` (registry) | No-op (Info.plist) |
| `IProcessRunner` (encoding, quoting) | 8.3 short names, OEM codepage | Real paths, UTF-8 |
| `IDispatcher` (marshal to UI thread) | `Control.BeginInvoke` | `Avalonia.Threading.Dispatcher.UIThread.Post` |

The PAL stays **thin**. It is a seam, not a framework. Windows implementations **call the existing classes**, so Windows behaviour is unchanged.

### 5.5 Native libraries for macOS arm64

| Need | Source | Notes |
|---|---|---|
| PDFium | NuGet `bblanchon.PDFium.macOS` (arm64 / univ dylib) | Same PDFiumSharpV2 API. Verify the package's `runtimes/osx-arm64/native` content. |
| WebP | SkiaSharp's built-in WebP decode/encode **or** Homebrew/vcpkg `libwebp` dylib | Prefer SkiaSharp decode to remove one native dependency. Keep the P/Invoke path with a resolver for encode parity. |
| JPEG XL | `libjxl` (+ brotli, highway) dylib via vcpkg/Homebrew, rebuilt as relocatable (`@rpath`) | Alternative: Apple **ImageIO** decodes JPEG XL since macOS 14. Decode-only, which fits the reader. |
| HEIF/AVIF | `libheif` + `libde265` + `dav1d`/`aom` dylibs; LibHeifSharp is cross-platform | Alternative: ImageIO decodes HEIF/AVIF natively. Prefer ImageIO for decode, libheif only if encode is needed. |
| JPEG 2000 | CSJ2K (managed) | Needs a Skia image adapter instead of `As<Bitmap>()`. With the Drawing shim, the `Bitmap` adapter may just work. |
| Archives | SharpCompress (managed) for read; official 7-Zip **`7zz`** macOS console binary for writes (or managed CBZ writer) | 7z.dll COM-vtable interop is not used on macOS. |
| DjVu (deferred) | Homebrew `djvulibre` | Via `IToolLocator`. |

Build these in a reproducible script (`mac/native/build-natives.sh`, vcpkg manifest with an `arm64-osx-dynamic` triplet), fix install names to `@rpath`, sign each dylib, and record versions and licences in `THIRD_PARTY_NOTICES`.

An **ImageIO-based decoder** (CoreGraphics `CGImageSourceCreateWithData`) is a strong macOS-only option: one system framework decodes JPEG, PNG, TIFF, GIF, HEIF, AVIF, WebP and JPEG XL, with no bundled codec licences. Spike S3 compares it with the bundled-dylib route.

### 5.6 Reader on macOS

- The portable `ComicBookNavigator` (Core) supplies pages. The Core's `PageImage`/`ThumbnailImage` caching works unchanged through the Drawing shim.
- v1 reader view: a custom Avalonia control drawing with Skia (`ICustomDrawOperation`/`SKCanvas`).
  - Implements single page, two-page spread, fit modes, zoom/pan, magnifier, page-turn, background/paper textures, and the right-to-left (manga) flag.
  - Behaviour is specified by reading `ImageDisplayControl`/`ComicDisplayControl`.
- Optional extraction (post-v1, upstream-friendly): move the pure layout/geometry functions from `ComicDisplayControl` into a Core `ComicPageLayout` class used by both UIs. That is the only meaningful way to share reader behaviour.
- Trackpad pinch, rotate and swipe arrive as Avalonia gesture events (`PinchGestureRecognizer`, `PullGestureRecognizer`, `Gestures.Pinch`).

### 5.7 Scripting on macOS (in v1 scope)

- Use **IronPython 2.7.12** in the net10 build. It is the same Python 2 language, so existing scripts and smart-list expressions (`PythonCommand.CompileExpression`) keep their semantics, including integer division.
- **All hooks are supported, including UI hooks (D6).**
  - Headless hooks (Books, Library, NewBooks, ParseComicPath, CreateBookList, NetSearch, Startup, Shutdown, ConfigScript, …) need only the Core.
  - Scripts that build dialogs with `System.Windows.Forms`, the `ComicInfoUI`/`QuickOpenUI` panels, and `DrawThumbnailOverlay` (GDI+ `Graphics`) run through the **Script UI Compatibility Layer** (§5.9).
  - `ComicInfoHtml`/`QuickOpenHtml` run through a WebKit view with a `window.external` bridge (§5.9.8).
- `IApplication` methods that return/accept `Bitmap` work through the Drawing shim. `MainWindow` (`IWin32Window`) returns a facade window handle, so `form.ShowDialog(ComicRack.MainWindow)` works.
- `ComicRack.Plugins` contains WinForms host controls (`Controls/HtmlComicPageControl.cs`, `UIComicPageControl.cs`). Exclude them from the net10 link set. The macOS app provides its own hosts (§5.9.7).
- Packages: `.crplugin` install (`ComicRack.Engine/PackageManager.cs`) is portable. Script folders map to `IAppPaths` (`Contents/Resources/Scripts` + `~/Library/Application Support/…/Scripts`).
- The `ScriptingLibraries` setting points to a CPython 2.7 `Lib` on Windows. On macOS, bundle the IronPython 2.7.12 StdLib inside the app.
- Hardened runtime: the app needs `com.apple.security.cs.allow-jit` (and probably `allow-unsigned-executable-memory`) for .NET JIT/DLR code generation. Test this under notarization early (spike S4).

### 5.8 Data compatibility and shared libraries across OSes

- `ComicDb.xml`/`Config.xml` formats are unchanged, so a Windows DB can be imported on macOS.
- Absolute paths inside the DB (`C:\Comics\…`) will not resolve. Provide an **"Import library from Windows" path-remapping tool**: prefix map `C:\Comics\` → `/Volumes/Comics/`, normalize separators and Unicode, then re-link files by path and, as a fallback, by file size + hash (the Core already has missing-file/relink logic to reuse).
- Never write macOS paths into a DB that is also opened by Windows, unless the owner opts into a shared-DB mode (out of scope for v1).
- Keep `Config.xml` platform-specific (window layouts and keymaps differ). Share only the portable sections (smart lists, preferences) via export/import.

### 5.9 Script UI Compatibility Layer (D6: scripts with WinForms UI run unmodified)

#### 5.9.1 Why this is feasible when porting ComicRack's own UI to the same layer is not

| | ComicRack's own UI (rejected option C) | Community scripts (this layer) |
|---|---|---|
| WinForms surface used | Everything, including ~45 Win32 message hacks, native control subclassing, OpenGL, uxtheme, layered windows | A narrow, "textbook" slice: forms, standard controls, Anchor/Dock layout, events, `MessageBox`, file dialogs, timers, simple `Paint` handlers |
| Who can change the code | Us | Third-party authors, often inactive. **Must run unmodified (D6).** |
| Fidelity needed | Pixel-level look of a whole app | Functional: the dialog appears, controls work, the values come back |
| Ownership cost | A full WinForms runtime | A facade sized by what scripts actually call, measured with a scanner (§5.9.9) |

The bundled samples confirm the shape:
- `ComicRack/Output/Scripts/*.py` use `Form`, `Label`, `Button`, `TextBox`, `CheckBox`, `ComboBox`, `ListBox`, `NumericUpDown`, `SaveFileDialog`, `MessageBox`, `DialogResult`, `AnchorStyles`, `DockStyle`, `FormBorderStyle`, `Font`, `Color`, `Point`, `Size`.
- They also subclass `Control` (`class UIControl(Control)` in `Sample.py`) and draw on a `Graphics` (`FadeReadThumbnails`).
- Community plugins (Comic Vine Scraper CE, Data Manager, CVIssueCount, …) add `ListView`, `PictureBox`, `TabControl`, `ProgressBar`, background threads with `Invoke`, and `Timer`.

#### 5.9.2 How a script finds "WinForms" on macOS

A typical script does:

```python
import clr
clr.AddReference('System.Windows.Forms')      # or AddReferenceByPartialName
clr.AddReference('System.Drawing')
from System.Windows.Forms import *
from System.Drawing import *
```

On .NET 10 macOS there is no `System.Windows.Forms` assembly, and `System.Drawing.dll` is only a facade that forwards `Bitmap`/`Graphics` to `System.Drawing.Common`, which doesn't work there. The script host fixes this **without touching the script**:

1. **Same namespaces and type names.**
   - `cYo.Compat.Forms` declares its types in namespace `System.Windows.Forms`.
   - `cYo.Compat.Drawing` declares its types in `System.Drawing`, `System.Drawing.Drawing2D`, `System.Drawing.Imaging` and `System.Drawing.Text`.
   - IronPython's `from X import *` resolves *namespaces* across all loaded assemblies, so the type names are what matter.
2. **Assembly identity mapping.** The plugin host (`PythonCommand.CreateEngine`, net10 build) pre-loads both facade assemblies into the script runtime (`ScriptRuntime.LoadAssembly`). It also registers an `AssemblyLoadContext.Default.Resolving` handler that maps these requested names to the facades:

   | Requested | Resolved to |
   |---|---|
   | `System.Windows.Forms` (any version/token) | `cYo.Compat.Forms` |
   | `System.Drawing.Common` | `cYo.Compat.Drawing` |
   | `System.Design`, `Microsoft.VisualBasic.Forms` | optional stub assemblies |

   `clr.AddReference('System.Windows.Forms')` therefore succeeds. The type forwarders inside the shared-framework `System.Drawing.dll` also land on the Skia-backed `Bitmap`/`Graphics`.

   Alternative, chosen by spike S6: ship the facades with the literal assembly names `System.Windows.Forms.dll` / `System.Drawing.Common.dll` in the app folder (there is no framework copy on macOS to conflict with). Verify how the .NET binder treats the public-key-token mismatch; the `Resolving` handler is the fallback either way.
3. **Same API shape, generated, not hand-typed.**
   - The facade's public surface is **generated from the WinForms/System.Drawing reference assemblies** using an API-surface generator (GenAPI from dotnet/arcade, or a small Roslyn tool). It produces every public type and member signature, including `virtual`/`protected` modifiers, so Python subclasses that override `OnPaint`, `OnResize`, `ProcessCmdKey` etc. bind correctly.
   - Members not implemented yet throw `NotSupportedException("System.Windows.Forms.X.Y is not yet supported on macOS (ComicRack script compatibility layer)")`. Script authors and users then see a clear message instead of an `AttributeError`.
   - Implementation is then **prioritized by real usage** (§5.9.9).

#### 5.9.3 Architecture: facade object model plus native peers

```
 Python script (unchanged)
   │  Form(), Button(), .Click += handler, .ShowDialog(), e.Graphics.FillRectangle(...)
   ▼
 cYo.Compat.Forms  (namespace System.Windows.Forms, net10, no UI-toolkit dependency)
   ├─ Facade object model: Control, Form, Button, …   ← all state lives here (Text, Bounds,
   │                                                     Font, Colors, Items, Checked, …)
   ├─ Events + WinForms event ordering (Load, Shown, Activated, Closing, Click, …)
   ├─ Layout engine: Anchor/Dock, AutoSize, FlowLayoutPanel, TableLayoutPanel, AutoScale
   │   (managed algorithms ported from dotnet/winforms, MIT)
   ├─ Threading: Invoke/BeginInvoke/InvokeRequired, auto-marshalling, modal loop
   └─ IFormsBackend  ◄──────────── interface (create peer, set prop, show, run frame…)
                                      │
 ComicRack.Mac.FormsBackend  (Avalonia, macOS app only)
   ├─ Peers: Form→Window, Button→Button, TextBox→TextBox, ListView→DataGrid/ListBox, …
   ├─ Container peers are Canvas-based: absolute child positions come from the facade layout
   ├─ Owner-draw: a Paint-capable peer surface gives Skia → cYo.Compat.Drawing.Graphics
   └─ Native dialogs: MessageBox→NSAlert-style dialog, Open/Save/Folder→NSOpenPanel/NSSavePanel
                                      │
 cYo.Compat.Drawing (System.Drawing on SkiaSharp)  ←  shared with the Core (§5.3)
```

Key design rules:
- **State lives in the facade, not in the peer.** A facade control can be created and configured on any thread before a peer exists, the way scripts do it. Peers are created lazily on the UI thread when the form is shown, and are destroyed on `Dispose`/close. This also makes the whole layer **testable headless**: run with a null backend, or with Avalonia's headless platform in CI on Linux.
- **WinForms layout is computed by the facade**, using the same managed algorithms WinForms uses (`DefaultLayout` for Anchor/Dock, `FlowLayout`, `TableLayout`, `AutoScaleMode.Font` scaling), ported from the MIT-licensed dotnet/winforms sources. Avalonia only places peers at the computed rectangles inside a `Canvas`. Scripts that position controls with absolute `Location`/`Size` and `Anchor` therefore lay out as their authors designed them, independent of Avalonia's layout system.
- **Look and feel:** peers are real Avalonia controls with the macOS theme, so dialogs look like Mac dialogs, not Windows ones, while keeping the authored geometry.
- **Coordinates and DPI:** WinForms pixels map 1:1 to Avalonia device-independent pixels (96 DPI logical); Retina scaling is automatic. `AutoScaleMode.Font`/`Dpi` factors are computed against a 96-DPI baseline, so scripts' hard-coded sizes stay valid.
- **Fonts:** `Microsoft Sans Serif`, `Segoe UI`, `Tahoma` and `Arial` map to the macOS system font (SF) at the same point size. Because text metrics differ, the facade applies **"grow-to-fit" for `AutoSize` labels/buttons** and logs (does not fail) when text overflows a fixed size.

#### 5.9.4 Behaviours that must be emulated exactly (these are what make scripts "just work")

| Behaviour | WinForms semantics scripts rely on | Emulation |
|---|---|---|
| **Modal `ShowDialog()`** | Blocks the calling code until the form closes, then returns a `DialogResult`. Code after `ShowDialog()` reads the controls' values. | **Nested dispatcher frame**: show the peer window as modal (owner = ComicRack main window, or a sheet), then run `Dispatcher.UIThread.PushFrame(frame)` until the form closes (Avalonia 11+ `DispatcherFrame`; confirm in spike S6). `AcceptButton`/`CancelButton`, `DialogResult` on buttons, Enter/Esc and `FormClosing` cancel are all handled in the facade. |
| `MessageBox.Show` (all overloads) | Blocking, returns `DialogResult` | Same nested-frame technique with a native-looking alert (buttons/icons mapped: OK, OKCancel, YesNo, YesNoCancel, RetryCancel). |
| **Cross-thread access** | ComicRack sets `Control.CheckForIllegalCrossThreadCalls = false` (`Program.cs:1147`), so many scripts update controls from worker threads **without** `Invoke` | Facade property setters called off the UI thread are **auto-marshalled** with `Dispatcher.UIThread.Post` (getters read facade state directly, so they are safe). Explicit `Invoke`/`BeginInvoke`/`InvokeRequired` map to the dispatcher. Scripts must never touch Avalonia directly. |
| `ShowDialog()` called from a background thread | Works on Windows, because it creates its own message loop on that thread | Facade detects the non-UI thread, `Invoke`s the modal show onto the UI thread and blocks the calling thread until it closes. Same observable result. |
| `Application.DoEvents()` | Pumps messages, used in progress loops | `Dispatcher.UIThread.RunJobs()` when on the UI thread, otherwise a no-op. |
| `System.Windows.Forms.Timer` | Ticks on the UI thread | `DispatcherTimer`. |
| `Form.Show()` (modeless) | Window stays open, script returns | Normal Avalonia window. The facade keeps a reference so it isn't garbage-collected. |
| `Handle`, `IWin32Window` | Some scripts pass `Handle` around or use it as an owner | A synthetic, stable `IntPtr` per facade window, mapped back to the peer. Any P/Invoke with it is unsupported (§5.9.6). |
| Keyboard | Scripts test `Keys.Control`, `e.KeyCode`, `ModifierKeys` | ⌘ is reported as `Keys.Control` (and Ctrl as well), so script logic written for Windows keeps working. Menu shortcut display uses ⌘ glyphs. |
| Event order | `Load` → `Shown` → `Activated`; `FormClosing` → `FormClosed`; `TextChanged`/`SelectedIndexChanged`/`CheckedChanged` raised on programmatic changes too | Raised by the facade from facade state, not from peer callbacks, so the order matches WinForms even where Avalonia's order differs. A conformance test suite (§5.9.10) records the Windows event traces. |

#### 5.9.5 Coverage tiers (initial targets; re-ranked by the usage scanner)

| Tier | Contents | Target |
|---|---|---|
| **A: must (v1)** | `Form`, `Control`, `UserControl`, `Panel`, `GroupBox`, `Label`, `LinkLabel`, `Button`, `CheckBox`, `RadioButton`, `TextBox` (single and multi-line, `PasswordChar`, `ReadOnly`), `ComboBox` (DropDown/DropDownList), `ListBox`, `CheckedListBox`, `NumericUpDown`, `PictureBox` (Image, SizeMode), `ProgressBar`, `TabControl`/`TabPage`, `SplitContainer`, `FlowLayoutPanel`, `TableLayoutPanel`, `ToolTip`, `Timer`, `ContextMenuStrip`/`ToolStripMenuItem`, `MessageBox`, `OpenFileDialog`/`SaveFileDialog`/`FolderBrowserDialog`, `DateTimePicker`, `TrackBar`, Anchor/Dock/AutoSize/AutoScale, Paint/owner-draw on `Control`/`Panel`/`PictureBox`, Clipboard, Cursors (`WaitCursor`) | All bundled samples plus the top community plugins run |
| **B: should (v1 if the scanner shows usage)** | `ListView` (Details/List/LargeIcon with `ImageList`, columns, sorting, check boxes), `TreeView`, `MenuStrip`, `StatusStrip`, `ToolStrip`, `DataGridView` (unbound/bound basic columns), `RichTextBox` (plain text plus simple formatting), `ColorDialog`, `FontDialog`, `WebBrowser` (navigate URL / `DocumentText` only, via WebKit), `HScrollBar`/`VScrollBar`, `MaskedTextBox`, `PropertyGrid` (read-only basic) | Long-tail community plugins |
| **C: unsupported (clear error, documented)** | P/Invoke to `user32`/`gdi32`/`kernel32` via `ctypes` or `DllImport`, `WndProc`/`DefWndProc` overrides and `Message` handling, `NativeWindow`, Win32 `Handle`-based tricks, WPF (`PresentationFramework`, XAML), MSHTML DOM access through `WebBrowser.Document`, COM automation of Windows apps (Excel, IE) | Reported by the compatibility scanner before the user runs the script |

#### 5.9.6 What still cannot run, and how users are told

- Tier C APIs: the scanner (§5.9.9) flags them at install time and in the Scripts preferences ("Uses Windows-only API `user32.SendMessage` at line 120; this script cannot run on macOS"). At runtime they throw a single, readable error.
- Windows file paths hard-coded in scripts (`C:\…`, `%APPDATA%`): `os.environ['APPDATA']` is mapped to the macOS app-data folder for scripts. Literal drive paths are flagged by the scanner as warnings.
- **Escape hatch for authors (optional, never required):** `ComicRack.Platform` (`"Windows"`/`"macOS"`) exposed to scripts, so authors *can* add Mac-specific tweaks. Nothing depends on it.
- **Compatibility patches without editing the original:** a `Scripts/.compat/<plugin>.py` overlay, loaded after the original module, can monkey-patch one function for a popular plugin whose author is inactive. It is maintained in this repo and versioned against the plugin's hash. This is the last resort, used only for Tier C cases worth saving.

#### 5.9.7 ComicRack-specific UI hooks

| Hook | Contract (unchanged for scripts) | macOS host |
|---|---|---|
| `ComicInfoUI` | Function returns a `Control` that implements `ShowInfo(books)` | `ScriptPanelHost` (Avalonia control) shows the facade control's peer tree as a tab in the Info inspector and calls `ShowInfo` on selection change (same contract as `ComicRack/ScriptUtility.cs` `CreatePagesUI`). |
| `QuickOpenUI` | Same | Same host, in the Quick Open area. |
| `DrawThumbnailOverlay` | `(book, graphics, bounds, flags)`, called after a thumbnail is drawn | The macOS browser grid renders each thumbnail into an `SKCanvas`. It wraps that canvas in a `cYo.Compat.Drawing.Graphics` (clip = cell bounds) and calls the hook. Overlay results are cached with the thumbnail and invalidated on book change, because Python per-cell calls are slow (budget: < 1 ms per visible cell, measured). |
| `ConfigScript` | Usually shows a WinForms settings dialog | Works through the modal emulation. |
| `IPluginEnvironment.Theme` (`ToolStripRenderer`) | A few scripts style toolbars with it | Returns a facade renderer object whose colours come from the macOS theme. Drawing calls go through the Drawing shim. |

#### 5.9.8 HTML panels (`ComicInfoHtml`, `QuickOpenHtml`, `WebBrowser`)

- Rendering: WKWebView embedded with Avalonia's `NativeControlHost` (a small ObjC-interop wrapper; or an existing Avalonia WebView package; evaluate licences, since some Avalonia WebView options are commercial).
- Strings starting with `!` are URLs and are navigated directly (as today).
- The `window.external` bridge (used by `Sample.py`: `window.external.ComicRack.OpenBooks.OpenFile(...)`) is emulated by an injected user script:
  - It defines `window.external` as a JavaScript Proxy that forwards method calls to the host via `webkit.messageHandlers.comicrack.postMessage({path, args})`.
  - The host resolves the path against the same `ScriptProvider` object model (`ComicRack.Plugins/Controls/HtmlComicPageControl.cs`) and invokes it.
- Limitation: WebKit messaging is asynchronous. **Method calls work** (the common case). **Synchronous property reads** (`var x = window.external.Config`) are served from a snapshot injected at page load. Writes are forwarded asynchronously. The scanner flags HTML that reads properties synchronously.

#### 5.9.9 Usage-driven scope: the script compatibility scanner

The facade is sized by data, not guesswork. Build `ComicRack.Cli script-scan`:
1. Collect a **corpus** of community scripts and `.crplugin` packages: bundled samples, FrederikBaerentsen/ComicRack_Scripts, Comic Vine Scraper CE, Data Manager, CVIssueCount, the AmazonScrapper loader, forum archives. Keep the corpus **out of the repo** unless licences allow; store only the scan results.
2. Parse each `.py` with the IronPython parser (AST, so it understands Python 2 syntax). Resolve `from System.Windows.Forms import *` names, attribute chains on known types, `clr.AddReference*` calls and `ctypes`/`DllImport` use.
3. Also scan bundled **.NET assemblies** inside plugins (`clr.AddReferenceToFileAndPath`): read their member references with `System.Reflection.Metadata`. Plugins such as maforget's AmazonScrapper are C# assemblies compiled against .NET Framework WinForms. They load on .NET 10 and bind to the facade *if* the referenced member signatures exist, which is why the generated full-signature surface (§5.9.2) matters.
4. Output a ranked table `type.member → #scripts using it`. That ranking is the **implementation backlog** for P4. Commit the table to `mac/scripting/compat-usage.md`.
5. The same scanner powers the end-user compatibility report (§5.9.6).

#### 5.9.10 How fidelity is verified (agent-friendly)

- **Control-tree snapshots:** a small helper script runs a plugin's dialog to its `Shown` event and dumps the control tree (type, name, bounds, text, visible/enabled, items). It does this on **Windows with real WinForms** (CI job on `windows-latest`, net48) and on **macOS/Linux with the facade** (Avalonia headless). Agents diff the JSON: bounds within ±2 px after font-metric normalization, identical structure and values.
- **Event-trace conformance:** a scripted interaction (set text, click, select item, close) records the event sequence in both environments. The sequences must match.
- **Paint snapshots:** `Paint` handlers and `DrawThumbnailOverlay` render to bitmaps; compare perceptually against GDI+ (same harness as P3).
- **Smoke run of the corpus:** run each corpus script's hooks headless with a fake library. Fail on `NotSupportedException` from Tier A/B members.
- **Human acceptance on a Mac:** the top 10 plugins by usage, checklist per plugin.

#### 5.9.11 Effort and ownership

- Size: the facade object model + layout + threading + backend peers for Tier A is a substantial component, but it is bounded and mostly mechanical once the generated API surface and the conformance harness exist. **Estimate: 25–40 agent tasks, ~5–8 weeks calendar**, partly in parallel with P5 (it shares the Avalonia app shell).
- Prior art to reuse, not reinvent:
  - dotnet/winforms (MIT): layout engines, control state logic, event ordering.
  - Mono's managed WinForms "MWF" (MIT): a complete managed implementation over a backend driver interface. Useful as a reference for peer-less control logic.
  - GenAPI: surface generation.
  - Avalonia headless: tests.
- Long-term ownership: this layer is maintained by this fork. Its scope only grows when the scanner shows real scripts need more. It is never used by the Windows build, where scripts keep using real WinForms.

---

## 6. macOS v1 scope

### 6.1 In scope (D4)

| Feature | Core reuse | New macOS work |
|---|---|---|
| Library: add folders, scan, watch (FSEvents via `FileSystemWatcher`), missing-file handling | ✅ Core scanner/queues | Library sidebar (lists/folders), progress UI |
| Browser: thumbnails/tiles/details, grouping, stacks, sorting, search, quick filters | ✅ matchers, groupers, `ItemViewConfig` | Virtualized grid + detail table; column chooser |
| Smart lists (create/edit, query language) | ✅ matchers + parser | Smart-list editor UI (rule builder + text query) |
| Reader | ✅ navigator, page cache, image processing | Skia reader control; full screen; trackpad |
| Metadata editing (single + multiple books), ComicInfo.xml write-back | ✅ ComicBook, providers, `ComicInfo` writers | Info dialog/inspector; 7zz or managed writer |
| Formats CBZ/CBR(RAR5)/CB7/CBT/PDF/WebP/HEIF/AVIF/JXL/J2K | ✅ providers | Native libs + resolver (§5.5) |
| Export/convert (CBZ, PDF, image formats) | ✅ `ComicExporter`, storage providers | Export sheet UI; sharpPDF via Drawing shim (or PdfSharpCore if it fails) |
| Python scripts/plugins: **all hooks, including WinForms dialogs, UI panels, thumbnail overlays and HTML panels (D6)** | ✅ Plugins via IronPython 2.7.12 | Script UI Compatibility Layer (§5.9), Scripts menu, preferences pane with per-script compatibility report, `.crplugin` install via drag-drop/Finder |
| Preferences (subset), localization (19 languages via TR XML) | ✅ TR | Preferences window. New TR XML files only for the new macOS views. |
| Backup manager | ✅ Core backup | Small UI |

### 6.2 Windows forms → macOS v1 mapping (UI work list)

| Windows (WinForms) | macOS v1 | Notes |
|---|---|---|
| `MainForm` + `MainView` + `ComicBrowserControl` + `ComicListLibraryBrowser` | Main window: sidebar + browser + optional preview | NativeMenu menu bar with standard macOS menus (App, File, Edit, View, Book, Window, Help) |
| `ComicDisplayControl` | Reader window/tab | Native full screen |
| `ComicBookDialog`, `MultipleComicBooksDialog` | Info inspector (sheet or panel) | |
| `SmartListDialog`, `SmartListQueryDialog`, `MatcherEditor`, `MatcherGroupEditor` | Smart list editor | |
| `PreferencesDialog` (subset) | Settings window (⌘,) | Only v1-relevant panes |
| `ExportComicsDialog` | Export sheet | |
| `ComicDataPasteDialog`, `ScriptConsole`, `TasksDialog`, `BackupManager` dialogs | Equivalent small windows | |
| `NewsDialog`, `AboutDialog`, `SplashForm` | About panel; no splash | News as plain text |
| Remote library, device sync, server settings | **Not in v1** | |

### 6.3 Deferred (post-v1 backlog, in suggested order)

1. **Wi-Fi device sync** with ComicRack Android/iOS apps (`Engine/Sync/WirelessSyncProvider.cs`: portable sockets, UDP multicast 224.34.123.90:7615, TCP 7614/7620+). It needs the macOS **Local Network privacy** prompt (`NSLocalNetworkUsageDescription`, Bonjour/multicast entitlement `com.apple.developer.networking.multicast`, which must be requested from Apple).
2. **DjVu** via djvulibre.
3. **Script UI compatibility Tier B long tail** (§5.9.5), where the scanner shows low usage.
4. **Library sharing:** WCF NetTcp with `SecurityMode.Message` is **not supported** by CoreWCF or the .NET WCF client. So macOS↔Windows sharing needs a **new protocol** (HTTP+JSON or gRPC over TLS) implemented on **both** sides, or a CoreWCF Transport-security endpoint added to the Windows server. Owner decision required (§10).
5. **Removable-drive sync** (`/Volumes`).
6. **USB/MTP sync** (libmtp; low value, high cost; Android phones in MTP mode don't mount on macOS).

### 6.4 Explicitly dropped on macOS

OpenGL/Tao renderer, Windows7.Multitouch, jump lists/taskbar overlays, registry file associations, UAC elevation, IE WebBrowser, NTFS ADS (unless xattr is added), `-rf` register-formats switch, Ghostscript registry lookup, x86 support.

---

## 7. Typical challenges of this kind of migration

These are the things the brief didn't ask about but that typically sink or delay ports like this one. Each has a mitigation that is already built into the plan.

### 7.1 No test safety net
There are **zero automated tests**, and the code is decompiled (names are sometimes synthetic, intent undocumented). Any refactor risks silent behaviour changes on Windows.
→ **P0 builds characterization ("golden") tests first**, running on net48/Windows and net10/macOS:
- DB load → save round-trip byte equality
- smart-list results on a fixture DB
- archive page enumeration + page hashes
- ComicInfo write → read
- thumbnail perceptual hashes
- filename parsing (`ComicNameInfo`)
- script hook outputs

### 7.2 File system semantics
- **Separators and roots:** `\` vs `/`; there are no drive letters, so `DriveChecker`/`GetPathRoot().ToLower()`/`DriveInfo` logic must treat `/Volumes/X` as a "drive".
- **Case sensitivity:** APFS is case-insensitive by default but *can* be case-sensitive. The Core's `OrdinalIgnoreCase` dictionaries are right for the default and wrong on case-sensitive volumes. Document it and detect it per volume later.
- **Unicode normalization:** macOS file APIs may hand back **NFD** names while Windows uses **NFC**. APFS is normalization-insensitive for lookup but preserves the stored form. Paths compared as strings (DB keys, duplicate detection, Windows-DB import) must be normalized (`string.Normalize(NormalizationForm.FormC)`) at the boundaries.
- **Network volumes** (SMB/NAS libraries, which are very common for comic collections): slower enumeration, `FileSystemWatcher` gaps on SMB, and permission prompts.
- **Hidden files and junk in archives:** `__MACOSX/`, `.DS_Store` and AppleDouble `._*` files. CE already ignores some (`Changes.txt:115`); extend this to folder scans.

### 7.3 Privacy permissions (TCC) and Gatekeeper
- Scanning `~/Documents`, `~/Downloads`, `~/Desktop`, external and network volumes triggers **TCC prompts** per location. "Full Disk Access" may be needed for some setups. The UX must explain this. Use security-scoped bookmarks only if sandboxing is ever adopted (not in D5).
- **Quarantine:** comics or `.crplugin` files downloaded from the web carry `com.apple.quarantine`. Extracting them is fine, but running bundled helper tools from quarantined locations is not, so always run tools from inside the signed app bundle.
- **Notarization:** every Mach-O in the bundle (dotnet host, all `.dylib`, `7zz`, SkiaSharp/HarfBuzz natives) must be signed with hardened runtime and a secure timestamp. NuGet-delivered dylibs are often unsigned or ad-hoc signed and must be re-signed.

### 7.4 Rendering fidelity and performance
- GDI+ and Skia differ in anti-aliasing, text metrics, interpolation kernels (`HighQualityBicubic` ≠ Skia's cubic/mipmaps) and premultiplied-alpha behaviour. Thumbnails and textures will look *slightly* different. Accept perceptual equivalence, not pixel equality.
- **Retina:** draw at device pixel ratio 2. Thumbnail caches keyed by logical size should store at 2× (cache-size and memory impact).
- The current pipeline decodes non-GDI formats → re-encodes to JPEG → decodes again (`ImageProvider.RetrieveSourceByteImage`). That is wasteful and also lossy. On macOS, decode directly to `SKBitmap` behind the shim. Measure the page-turn latency budget (target < 50 ms for cached pages on an M1).

### 7.5 Threading model
- The WinForms app disables cross-thread checks (`Program.cs:1147`) and invalidates UI from worker threads. Win32 tolerates this; **AppKit does not** (crashes or undefined behaviour).
- In the macOS app, *every* Core event handler that touches UI state must go through `IDispatcher`/`Dispatcher.UIThread`.
- `Thread.Abort` is unavailable on .NET 10 (§3.1). The splash-on-second-UI-thread pattern is impossible on macOS (and unnecessary).

### 7.6 Keyboard, menus and Mac conventions
- ⌘ instead of Ctrl, standard shortcuts (⌘, Settings, ⌘W, ⌘Q, ⌘F, ⌘⌥F), the app menu, Services, the Window menu, and native full screen.
- The 3×3 "gesture zones" and mouse-button customisations from Windows don't map 1:1 to a trackpad. Design trackpad defaults (swipe = page turn, pinch = zoom, force click = magnifier?).
- Keymaps are persisted as `CommandKey` values with Ctrl. Keep a separate macOS keymap set; don't translate in place.
- Fonts: Segoe UI and Tahoma are absent, so use the system font. The Drawing shim must map unknown family names to SF.

### 7.7 Licensing and provenance
- The GPLv2 decompiled-codebase provenance is unchanged by the port, but **distribution to Mac users increases visibility**. Keep the README notice and attribution.
- RAR: reading via SharpCompress is fine. Never offer RAR *creation*.
- Bundle `THIRD_PARTY_NOTICES` with all codec and toolkit licences (§2.5) inside `Contents/Resources`.
- An Apple Developer ID (99 USD/yr) is needed for notarization, and signing credentials go into CI secrets.

### 7.8 Upstream divergence
- The fork must keep merging `maforget/ComicRackCE`. The shadow-project technique keeps upstream files untouched; `mac/EDITS.md` lists the few unavoidable edits.
- Offer the PAL seam, the `#if NETFRAMEWORK` guards and the BinaryFormatter removal upstream as small PRs, which is what upstream's CONTRIBUTING asks for ("small, focused pull requests").
- New upstream features land in Core automatically through the source links. CI on macOS fails fast when upstream adds a Windows-only API to a linked file. **Make that a scheduled CI job** (weekly upstream-merge dry run + net10 build).

### 7.9 Runtime and packaging choices
- Self-contained .NET 10 `osx-arm64`, not trimmed, no NativeAOT (IronPython/DLR and `XmlSerializer` reflection). App size is ~120–180 MB; acceptable for a DMG.
- `ReadyToRun` for startup time.
- `Info.plist`:
  - `CFBundleDocumentTypes` for cbz/cbr/cb7/cbt/cbw/cbl/crplugin/pdf (viewer role)
  - `UTImportedTypeDeclarations` for `com.comicrack.cbz` etc. (some UTIs exist in the wild; avoid conflicts)
  - `LSMinimumSystemVersion` 14.0
  - `NSHighResolutionCapable`
  - Usage strings for local network (post-v1)
- Updates: Sparkle (via a .NET wrapper, or a simple "check for updates" that opens the release page, matching the current News behaviour). Plus a Homebrew cask.

### 7.10 Data migration UX
- Users switching from Windows will want their library: an **import wizard** (§5.8) with path remapping, plus copying custom thumbnails and smart lists.
- Settings location changes, so document `~/Library/Application Support/ComicRack Community Edition`.

### 7.11 Accessibility, localization, help
- Avalonia 12 exposes accessibility on macOS, so label all custom controls (the reader and the grid).
- TR XML localization is reusable. New macOS views need new TR keys. Translators' workflow is unchanged (wiki).
- Help links (`ComicRack/Output/Help/*.ini`) point to web pages and are reusable. F1 → Help menu search.

### 7.12 Crash diagnostics
- `CrashWatchDog` relies on `Application.ThreadException` (WinForms).
- On macOS: `AppDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException` + Avalonia dispatcher exception handler.
- Write logs to `~/Library/Logs/ComicRack Community Edition/`.

### 7.13 CI cost and environment
- macOS GitHub runners (`macos-15`/arm64) cost more minutes; cache NuGet and native builds.
- Windows CI must keep building net48 unchanged. Add `dotnet build mac/…` on macOS **and** a net10 build on Linux (cheapest early compile gate for agents: the cloud agents run on Linux).

---

## 8. Risk register

| # | Risk | Prob. | Impact | Mitigation | Owner/phase |
|---|---|---|---|---|---|
| R1 | Drawing shim fidelity/coverage is larger than estimated | M | H | P1.3 usage inventory; spike S2 (GeneXus.Drawing vs custom); perceptual tests; fall back to targeted `#if` refactors of the worst files | P1/P3 |
| R2 | Hidden Windows coupling in linked Core files (static constructors touching Win32, WinForms types deep in models) | H | M | Compile-driven triage; headless CLI smoke runs on macOS each phase | P1/P2 |
| R3 | IronPython 2.7.12 issues on .NET 10/arm64 or under hardened runtime | M | H | Spike S4 on day 1; fallback: IronPython 3.4 with a compatibility notice | P0/P4 |
| R4 | Native codec packaging/signing friction (rpaths, notarization rejects) | M | M | vcpkg-based reproducible build; ImageIO decode route (S3) to minimize dylibs | P2/P6 |
| R5 | Main-thread violations cause random crashes on macOS | M | H | `IDispatcher` discipline; Avalonia's thread checks; stress tests during scans | P5 |
| R6 | Upstream changes break the net10 link set frequently | M | M | Weekly upstream-merge CI; exclusion list; upstream the PAL | ongoing |
| R7 | UI scope creep (users expect Windows parity) | H | M | Strict v1 list (§6); public roadmap of deferred items | Owner |
| R8 | Performance regressions (large libraries of 100k+ books, SMB) | M | M | Benchmarks in CLI harness (scan, load DB, filter); virtualization | P2/P5 |
| R9 | Legal/provenance challenge increases with Mac distribution | L | H | Keep the README stance; no App Store; GPL compliance (source link in About) | Owner |
| R10 | Agent-produced code that compiles but is subtly wrong (decompiled code, few comments) | M | H | Characterization tests as gates; Opus reviews of Sonnet diffs; human on-device acceptance checklist per phase | all |
| R11 | Script UI facade coverage is never "complete"; long-tail plugins hit unimplemented members | H | M | Usage scanner drives the backlog (§5.9.9); generated full API surface gives clear `NotSupportedException` messages; compatibility report before running; `.compat` overlays for important inactive plugins | P4 |
| R12 | Blocking `ShowDialog`/`MessageBox` semantics (nested dispatcher frames) misbehave on macOS (re-entrancy, sheets, app deactivation) | M | H | Spike S6 first; conformance tests for nested modals, modal-from-background-thread and closing during a nested frame | P0/P4 |
| R13 | Scripts relying on unmarshalled cross-thread control access crash or race on macOS | M | H | Auto-marshalling of facade setters; facade state is thread-safe; stress tests with corpus plugins that use worker threads | P4 |
| R14 | Layout differences (fonts/metrics) make dialogs clip or overlap | M | M | Layout computed by ported WinForms algorithms; AutoSize grow-to-fit; control-tree snapshot diffs vs Windows | P4 |

---

## 9. Agentic execution plan

This section is written for **Claude planning sessions** (to expand into task-level plans) and **Claude implementation sessions** (to execute single tasks).

### 9.1 Roles and model usage

| Role | Model tier | Use for |
|---|---|---|
| **Architect/Planner** | Claude Opus | Expanding a phase into tasks; spikes with uncertain outcomes; designing PAL interfaces and the Drawing shim API; reviewing every PR touching existing (upstream) files; diagnosing cross-platform bugs; threading/concurrency work; reader-control design |
| **Implementer** | Claude Sonnet | Well-specified tasks: link-set setup, compile-error triage batches, shim members, PAL implementations, Avalonia views from a spec, tests, CI YAML, packaging scripts, TR XML |
| **Reviewer** | Claude Opus (or a 2nd Sonnet pass for mechanical diffs) | Adversarial review against the guardrails below + the test gate |
| **Human (owner)** | — | Decisions in §10; on-Mac acceptance testing (agents run on Linux cloud containers and cannot click through a macOS GUI); signing credentials; upstream relations |

Parallelism: use separate git worktrees/branches per task. Phases P2/P3/P4 can run in parallel after P1. P5 views can be split per screen.

### 9.2 Guardrails (every session must follow)

1. **Never change behaviour of the net48 Windows build.** Every PR must build `ComicRack.sln` for net48 (Windows CI) and pass the characterization tests on net48.
2. **Prefer adding over editing.** Edits to files under the original 7 project folders must be minimal, mechanical, justified in the PR description and logged in `mac/EDITS.md`.
3. Allowed edit patterns in existing files:
   - `#if NETFRAMEWORK`/`#if !NETFRAMEWORK` guards
   - replacing a direct Win32/WinForms call with a `Platform.Current.X` call whose Windows implementation calls the original code
   - making a class `partial`
   - replacing hard-coded `"\\"` path joins with `Path.Combine`

   Anything else needs Architect approval in the PR.
4. Do not rename public types/namespaces in Core. Python scripts import them by name (`cYo.Projects.ComicRack.Engine.*`, `cYo.Common.Windows.Forms.FormUtility`).
5. Enum names/values in compat shims must match WinForms exactly (XML persistence).
6. Never introduce BinaryFormatter, `Thread.Abort`, CodeDom or private reflection on the net10 path.
7. Never commit secrets (Apple signing). Use CI secrets.
8. No model identifiers or session links in code, comments or docs.
9. The script compatibility facades (`cYo.Compat.Forms`, `cYo.Compat.Drawing`) are **net10/macOS only**. They must never be referenced by the net48 build, where scripts keep using real WinForms/GDI+. Never change a hook signature or plugin-facing type to make the facade easier; the facade adapts to the scripts (D6).
10. Keep this document current: when a decision or a fact here changes, update the relevant section in the same PR.

### 9.3 Phases

Each phase lists **goal → tasks → exit criteria**. IDs are stable so planning sessions can expand them (e.g. `P1.3a`, `P1.3b`).

#### P0: Baseline, spikes and safety net (Architect-heavy)
- **P0.1** Add `mac/` skeleton, `mac/Directory.Build.props` (net10.0), `mac/EDITS.md`, and a Linux + macOS CI workflow that only builds `mac/**` (it must not touch the Windows workflows).
- **P0.2** Create `tests/ComicRack.Characterization` (xUnit, multi-target `net48;net10.0`) with fixture data under `tests/fixtures/`. Generate small CBZ/CBR/CB7/PDF/WebP/AVIF/JXL samples with an MIT/CC0 provenance note.
- **P0.3** Write golden tests (§7.1) against the **net48** build on Windows CI. Record the baselines.
- **Spikes** (time-boxed, each ends with a short write-up appended to §11.4):
  - **S1:** compile `ComicRack.Engine` linked sources on net10 with an empty shim and categorize errors (counts per bucket: Drawing, WinForms types, Win32, WCF, other).
  - **S2:** GeneXus.Drawing.Common vs a custom Skia shim: coverage of the S1 Drawing errors, licence, perf.
  - **S3:** ImageIO decode (via `CGImageSource` P/Invoke) vs bundled dylibs for WebP/AVIF/HEIF/JXL on an M-series Mac.
  - **S4:** IronPython 2.7.12 on .NET 10 osx-arm64 inside a hardened-runtime, notarized hello-world `.app`: run `Sample.py` headless hooks.
  - **S5:** Avalonia 12 virtualized grid with 100k items + thumbnails; reader Skia control at 60 fps on a 4K page.
  - **S6:** Script UI layer feasibility:
    - `clr.AddReference('System.Windows.Forms')` resolving to a generated facade (identity mapping, §5.9.2)
    - a Python subclass of `Control` overriding `OnPaint`
    - blocking `ShowDialog()` via a nested Avalonia dispatcher frame, including from a background thread
    - `Sample.py` + `Autonumber.py` dialogs shown on a Mac
    - Output: go/no-go and the identity-mapping choice.
- **Exit:** CI green; S1–S5 reports merged; baseline tests green on net48.

#### P1: Core compiles on .NET 10 (Sonnet-heavy, Opus review)
- **P1.1** Shadow projects with link sets + exclusion lists (§5.2): `cYo.Common.Core`, `ComicRack.Engine.Core`, `ComicRack.Plugins.Core`.
- **P1.2** `cYo.Compat.Forms`, first slice (types only: enums/structs + `Application` facade); link `ItemViewConfig`, `ItemViewColumnInfo`, `ItemViewMode` (and dependents) into Core. Generate the slice from the reference assemblies so it later grows into the full facade (§5.9.2).
- **P1.3** API-usage inventory of System.Drawing in the link set (Roslyn-based or compiler-error-based), then implement `cYo.Compat.Drawing` (or adopt the S2 winner) member by member.
- **P1.4** Minimal edits:
  - `#if` around BinaryFormatter (`DiskCache.cs`)
  - `Thread.Abort` call sites in linked files
  - `WebComic.cs` reflection
  - hard-coded `Resources\\` joins
- **P1.5** Stub/replacement files for excluded-but-referenced types (`NetworkManager`, `DeviceSyncFactory`, `SingleInstance`), net10 only.
- **Exit:** `dotnet build mac/ComicRack.Engine.Core` green on Linux and macOS with 0 errors; net48 Windows build unchanged; `EDITS.md` lists every edit.

#### P2: PAL + native libs + headless harness
- **P2.1** `cYo.Platform` interfaces (§5.4). The Windows implementation wraps existing code. Wire `Platform.Current` into the WinForms app startup (a one-line edit in `Program.cs`) **only if** needed for Core calls. Otherwise the net48 Core keeps calling Win32 directly via `#if`.
- **P2.2** `cYo.Platform.MacOS` implementations.
- **P2.3** Native library resolver + `mac/native/build-natives.sh` (vcpkg arm64-osx-dynamic) or the S3 ImageIO route; PDFium macOS package.
- **P2.4** `ComicRack.Cli` commands: `scan <folder>`, `db load|save|stats`, `open <book> --page n --out x.png`, `thumb`, `smartlist run <xml>`, `script run <py> --hook Books`, `export`, `info write`.
- **P2.5** Run the characterization tests on net10 (Linux + macOS) via the Cli/tests. Fix parity gaps.
- **Exit:** all golden tests pass on net10/macOS within agreed tolerances (perceptual for images); Cli benchmarks recorded.

#### P3: Imaging fidelity and performance
- **P3.1** Perceptual-diff suite for the Drawing shim vs GDI+ baselines (thumbnail renderers, `ImageProcessing` adjustments, page compositing).
- **P3.2** Direct-decode path on net10, removing the JPEG round-trip in `RetrieveSourceByteImage` behind `#if !NETFRAMEWORK`.
- **P3.3** Memory and cache tuning for Retina (2× thumbnails).
- **Exit:** visual diffs below threshold; page-decode p95 within budget.

#### P4: Scripting on macOS, including the Script UI Compatibility Layer (§5.9)
- **P4.1** IronPython 2.7.12 for net10 in `ComicRack.Plugins.Core`; bundle the StdLib; `.crplugin` install flow; fix the `CommitInstallPackage` `SafeDelete` bare-filename bug (upstreamable).
- **P4.2** Script compatibility scanner (`ComicRack.Cli script-scan`) + corpus collection, producing the ranked `mac/scripting/compat-usage.md` that drives P4.4–P4.6.
- **P4.3** Generate the full `System.Windows.Forms` and `System.Drawing` public surface into `cYo.Compat.Forms`/`cYo.Compat.Drawing` as stubs that throw `NotSupportedException`. Add the assembly-identity mapping in the net10 plugin host.
- **P4.4** Facade core:
  - control/form object model and event ordering
  - layout engines ported from dotnet/winforms
  - threading (auto-marshal, `Invoke`)
  - modal loop (`ShowDialog`, `MessageBox`)
  - `IFormsBackend` interface plus a null/headless backend
- **P4.5** `ComicRack.Mac.FormsBackend`: Avalonia peers for Tier A controls, native file dialogs, owner-draw surface → `Graphics`. One task per 2–4 controls, ordered by the usage ranking.
- **P4.6** ComicRack hook hosts: `ComicInfoUI`/`QuickOpenUI` panel host, `DrawThumbnailOverlay` in the browser grid, HTML panels (WKWebView + `window.external` bridge), `IPluginEnvironment.MainWindow`/`Theme`.
- **P4.7** Conformance harness:
  - control-tree snapshot + event-trace recorder on Windows (net48, real WinForms) and macOS/Linux (facade, Avalonia headless)
  - CI diff
  - corpus smoke run
- **P4.8** Tier B controls by usage rank; user-facing compatibility report in Scripts preferences.
- **Exit:**
  - All bundled sample scripts run unmodified on macOS, including the `Sample.py` UI panel, thumbnail overlay and HTML panel, `Autonumber.py`/`NewComics.py` dialogs, and `SaveFileDialog`.
  - The top-10 community plugins by usage pass the owner's on-Mac checklist.
  - Smart-list Python expressions give identical results to net48 on the fixture DB.
  - Control-tree and event-trace diffs are within tolerance for the corpus dialogs.

#### P5: Avalonia macOS app v1 (largest phase; split per screen)
- **P5.0** App shell:
  - MVVM structure, NativeMenu, ⌘ keymap, `IDispatcher`
  - DI composition root (`Platform.Current = new MacPlatform()`)
  - Settings persistence (own file + shared portable sections)
  - Localization via TR
- **P5.1** Library sidebar + scanning/progress.
- **P5.2** Browser grid (thumbnails/tiles/details), grouping/stacks, sort, search, `ItemViewConfig` round-trip.
- **P5.3** Reader control (§5.6) + full screen + trackpad.
- **P5.4** Info inspector (single + multi-edit) + ComicInfo write-back.
- **P5.5** Smart-list editor.
- **P5.6** Export, backup, script console/scripts menu, preferences subset.
- **P5.7** Windows-library import wizard (§5.8).
- Each screen task ships with: a view spec derived from the WinForms form (fields, behaviours, validation), view-model unit tests, and a human acceptance checklist.
- **Exit:** owner completes the acceptance checklist on a Mac for every v1 feature in §6.1.

#### P6: macOS integration, packaging and release
- **P6.1** `.app` bundle layout, `Info.plist` (doc types, UTIs, min OS), icons (`.icns` from existing assets).
- **P6.2** Codesign (hardened runtime + entitlements: allow-jit, allow-unsigned-executable-memory, disable-library-validation **only if** required by plugins), notarize, staple, DMG (`create-dmg`), optional Homebrew cask.
- **P6.3** macOS release workflow (tag-triggered, separate from the Windows workflows); nightly macOS artifact.
- **P6.4** `THIRD_PARTY_NOTICES`, README "macOS" section, wiki page.
- **Exit:** a notarized DMG installs and runs on a clean macOS 14 and the current macOS release with Gatekeeper on.

#### P7: Beta hardening
Crash/log collection, SMB/large-library testing, TCC/permissions UX, performance tuning, accessibility pass, translations of new strings.

#### Post-v1 backlog
§6.3 items in order, plus optional upstreaming of the shadow projects into real multi-targeting.

### 9.4 Dependency graph

```
P0 ──► P1 ──┬─► P2 ──┬─► P3
            │        ├─► P4.1–P4.4 (headless; can start right after P1)
            │        └─► P5.0 ─┬─► P5.1..P5.7 (parallel per screen)
            │                  └─► P4.5–P4.8 (needs the Avalonia shell)
            └────────────────────────────────► P6 (can start after P5.0)
P5 + P6 ──► P7 ──► v1 release
```

### 9.5 Task template for planning sessions

```markdown
### <ID> <Title>
- Phase / depends on: <P#> / <IDs>
- Model tier: Opus | Sonnet
- Goal (one sentence):
- Files to add: ...
- Existing files to edit (must justify each; log in mac/EDITS.md): ...
- Out of scope: ...
- Acceptance: build gates (net48 Windows + net10 Linux/macOS), tests to add/pass, CLI command to run, human check (if UI)
- Rollback: how to revert without affecting Windows
```

Prompt seed for a planning session:

> "Read `MACOS_NATIVE_PATH.md` §0, §5 and §9. Expand phase `<P#>` into 5–15 tasks using the §9.5 template. Respect §9.2 guardrails. For each task, list the exact files (verify they exist in the repo), the build/test gates and the model tier. Flag any task that requires editing existing files, and propose the least-invasive pattern."

### 9.6 Sizing (low confidence; refine after P0 spikes)

| Phase | Agent tasks | Calendar (with human review) | Main uncertainty |
|---|---|---|---|
| P0 | 8–12 | 1–2 wks | Fixture creation, spike outcomes |
| P1 | 10–20 | 2–3 wks | Drawing shim size (S1/S2) |
| P2 | 10–15 | 2–3 wks | Hidden coupling, native builds |
| P3 | 4–8 | 1–2 wks | Fidelity thresholds |
| P4 (incl. Script UI layer) | 30–45 | 5–8 wks (parallel with P5) | Facade coverage long tail, modal-loop semantics (S6) |
| P5 | 20–35 | 5–8 wks | UI polish, reader control |
| P6 | 5–8 | 1–2 wks | Notarization of all natives |
| P7 | 5–10 | 2–3 wks | Real-world libraries |
| **Total** | **~90–155** | **~15–24 wks** (P2–P5 overlap) | |

Human time is dominated by: decisions (§10), PR review of edits to existing files, and on-Mac acceptance testing (budget ~1–2 h per P5 screen task).

---

## 10. Open questions for later decisions

| # | Question | Default if unanswered | Needed by |
|---|---|---|---|
| Q1 | Drawing shim: adopt GeneXus.Drawing.Common or build a custom shim? | Decide from spike S2 | P1.3 |
| Q2 | Codec route on macOS: Apple ImageIO decode vs bundled libwebp/libjxl/libheif dylibs? | ImageIO for decode, dylibs only where encode is required | P2.3 |
| Q3 | Archive writing on macOS: bundle `7zz` vs managed writers only (CBZ via SharpZipLib, no CB7 write)? | Managed CBZ write + `7zz` for CB7 | P2.3 |
| Q4 | Library sharing future: new cross-platform protocol on both OSes, or CoreWCF transport endpoint on Windows, or never? | Not in v1; revisit after v1 | post-v1 |
| Q5 | IronPython 3.4 migration (breaks Python 2 scripts) — ever? | Stay on 2.7.12 | post-v1 |
| Q6 | Upstream strategy: propose the PAL/shadow approach to `maforget/ComicRackCE` early, or keep it in this fork until v1? | Upstream small, neutral fixes (BinaryFormatter `#if`, path joins, the plugin bug) early; the rest after v1 | P1 |
| Q7 | Should the Avalonia UI eventually also replace the WinForms UI on Windows? | No (out of scope); keep the option open | post-v1 |
| Q8 | Branding/bundle id (`org.comicrackce.ComicRack`?), app name on macOS, icon | "ComicRack CE", id chosen by owner | P6.1 |
| Q9 | Minimum macOS: 14 (D5) — raise to 15 if Avalonia/ImageIO features need it? | 14 | P6 |
| Q10 | Script corpus: which community plugins form the "top-10 must work" acceptance list, and may their code be stored (privately) for CI smoke tests? | Owner provides the list; scan results only in the repo | P4.2 |
| Q11 | `.compat` overlay patches for inactive plugins: acceptable, and who maintains them? | Acceptable as a last resort, maintained in this fork | P4.8 |
| Q12 | HTML panel bridge: build a small WKWebView wrapper, or adopt a WebView package (check licence)? | Small in-house wrapper | P4.6 |

---

## 11. Appendices

### 11.1 Key file index (for agents)

| Topic | File(s) |
|---|---|
| Startup, native path, single instance, cross-thread flag | `ComicRack/Program.cs:1122-1158` (`Main`), `738-972` (`StartNew`), `57-80` (`MouseWheelDelegater`) |
| App data paths | `ComicRack.Engine/SystemPaths.cs:46-83` |
| DB load/save | `ComicRack.Engine/DatabaseManager.cs`, `Database/ComicDatabase.cs:286-364`, `cYo.Common/Xml/XmlUtility.cs` |
| Engine configuration / ini | `ComicRack.Engine/EngineConfiguration.cs`, `cYo.Common/Runtime/IniFile.cs` |
| Provider factory / readers / writers | `ComicRack.Engine/IO/Provider/{Providers,ProviderFactory,ImageProviderFactory,ImageProvider}.cs`, `Readers/*`, `Writers/*` |
| Archive engines | `Readers/Archive/{SevenZipEngine,SharpCompressEngine,FileBasedAccessor}.cs`, `cYo.Common/Compression/SevenZip/*` |
| Codecs | `IO/Provider/{WebpImage,JpegXLImage,HeifAvifImage,Jpeg2000Image,DjVuImage}.cs`, `Readers/Pdf/PdfiumReaderEngine.cs`, `IO/Provider/Pdfium.cs` |
| Imaging core | `cYo.Common/Drawing/{BitmapExtensions,ImageProcessing,FastBitmap,FastBitmapLock,GraphicsExtensions}.cs`, `ComicRack.Engine/IO/{MemoryOptimizedImage,PageImage,ThumbnailImage}.cs`, `IO/Cache/ImagePool.cs`, `Drawing/*` |
| Disk cache (BinaryFormatter) | `cYo.Common/IO/DiskCache.cs:226-325` |
| NTFS ADS | `ComicRack.Engine/IO/Provider/NtfsInfoStorage.cs`, `cYo.Common/Win32/AlternateDataStreamFile.cs` |
| Renderer seam | `cYo.Common.Presentation/{IBitmapRenderer,IHardwareRenderer,IControlRenderer,BitmapGdiRenderer}.cs`, `Tao/ControlOpenGlRenderer.cs`, `ComicRack.Engine.Display.Forms/ImageDisplayControl.cs:1830-1882` |
| Persisted UI config in model | `cYo.Common.Windows/Forms/{ItemViewConfig,ItemViewColumnInfo,ItemViewMode}.cs`, `ComicRack.Engine/Database/{StacksConfig,DisplayListConfig}.cs`, `Metadata/ComicBook/ComicBookMetadata.cs` |
| Scripting | `ComicRack.Plugins/{PythonCommand,PluginEngine,PluginEnvironment,IPluginEnvironment,Command}.cs`, `Automation/*`, `ComicRack/ScriptUtility.cs` (UI hook hosting: `CreatePagesUI`, `CreatePagesHtml`), `ComicRack/MainForm.cs:793` (`DrawThumbnailOverlay`), `ComicRack.Plugins/Controls/{HtmlComicPageControl,UIComicPageControl}.cs` (Windows hosts, `window.external`), `ComicRack.Engine/PackageManager.cs`, `ComicRack/Output/Scripts/*` (samples: `Sample.py` covers every UI hook type) |
| Networking (deferred) | `ComicRack.Engine/IO/Network/*`, `cYo.Common/Net/Broadcaster.cs`, `cYo.Common/Runtime/SingleInstance.cs` |
| Sync (deferred) | `ComicRack.Engine/Sync/*`, `cYo.Common/Win32/PortableDevices/*` |
| Thread.Abort sites | `cYo.Common/Threading/{ThreadUtility.cs:247,BackgroundRunner.cs:77}`, `ComicRack.Engine/ComicScanner.cs:97`, UI: `AutomaticProgressDialog.cs:88`, `RemoteConnectionView.cs:249` |
| Localization | `cYo.Common/Localize/{TR,TRDictionary}.cs`, `ComicRack.Engine/PackedLocalize.cs`, `cYo.Common.Windows/LocalizeUtility.cs`, `ComicRack/Output/Languages/*` |
| Build/CI | `.github/workflows/*.yml`, `ComicRack/ComicRack.csproj` (PreBuild/PostBuild/CompressFolders), `Installer.iss`, `BuildInstaller.ps1`, `CompressFolders.ps1`, `compile_res_file.bat` |

### 11.2 P/Invoke families (cYo.Common + UI), grouped by PAL target

| Family | Files | macOS action |
|---|---|---|
| kernel32 LoadLibrary/GetProcAddress (7z), ole32 PropVariantClear | `Compression/SevenZip/*` | Not used (SharpCompress / 7zz) |
| kernel32 file/ADS/FindFirstFile/GetShortPathName/GetOEMCP | `Win32/{AlternateDataStreamFile,FileSystemEnumerable,FileMethods,ExecuteProcess}.cs` | PAL / exclude |
| shell32/shlwapi (SHFileOperation, IFileOperation, PIDLs, icons, SHOpenFolderAndSelectItems, SHChangeNotify, PathGetCharType) | `Win32/{ShellFile,ShellFolder,ShellPidl,ShellIcon,FileExplorer,ShellRegister,FileOperations/*}.cs`, `IO/FileUtility.cs` | `IShellServices`, `IFileAssociations` |
| Power (PowerCreateRequest, SetThreadExecutionState, RegisterApplicationRestart) | `Win32/PowerRequest.cs`, `Threading/ThreadUtility.cs` | `IPowerManagement` |
| Memory/WMI | `Win32/MemoryInfo.cs` | `ISystemInfo` |
| credui | `Win32/UserCredentialsDialog.cs` | `ICredentialsPrompt` |
| user32/gdi32/uxtheme/dwmapi (cursors, DPI, dark mode, layered windows, messages) | `Win32/{BitmapCursor,FontDC,UXTheme,TrueVisibility,Keyboard}.cs`, `cYo.Common.Windows/**` | UI-only, not in Core |
| WPD COM (~3.1k lines) | `Win32/PortableDevices/*` | Deferred / exclude |
| Codec P/Invokes (libwebp, jxl, libheif) | `ComicRack.Engine/IO/Provider/*Image.cs` | `INativeLibraryResolver` |

### 11.3 Build commands (reference)

```bash
# Windows (unchanged)
msbuild ComicRack\ComicRack.csproj -t:rebuild -p:Configuration=Release

# Core on Linux/macOS (after P1)
dotnet build mac/ComicRack.Engine.Core -c Release
dotnet test  tests/ComicRack.Characterization -f net10.0

# macOS app (after P5/P6)
dotnet publish mac/ComicRack.Mac -c Release -r osx-arm64 --self-contained -p:PublishReadyToRun=true
```

### 11.4 Spike results log
_(Empty. Append S1–S5 write-ups here: date, question, method, result, decision.)_

### 11.5 Sources (prior art and ecosystem facts)

- Microsoft: [System.Drawing.Common only supported on Windows (.NET 6+ breaking change)](https://learn.microsoft.com/en-in/dotnet/core/compatibility/core-libraries/6.0/system-drawing-common-windows-only)
- Avalonia: [Avalonia for macOS](https://avaloniaui.net/platforms/macos), [Avalonia macOS platform guide](https://docs.avaloniaui.net/docs/platform-specific-guides/macos), [v12 supported platforms (Metal status)](https://v12.docs.avaloniaui.net/docs/supported-platforms), [v12 NativeMenu](https://v12.docs.avaloniaui.net/controls/menus/nativemenu), [What's new in Avalonia 12](https://avaloniaui.net/avalonia), [Pricing](https://avaloniaui.net/pricing)
- Avalonia migration case studies: [Devolutions](https://avaloniaui.net/success/devolutions), [DiskDigger + Avalonia](https://dmitrybrant.com/?p=2486), [WPF→Avalonia porting guide](https://avaloniaui.net/blog/the-expert-guide-to-porting-wpf-applications-to-avalonia), [Host Avalonia in NSView discussion #15719](https://de.github.com/AvaloniaUI/Avalonia/discussions/15719), [.NET cross-platform UI history (LeXtudio)](https://docs.lextudio.com/blog/the-story-about-net-cross-platform-ui-frameworks-dd4a9433d0ea)
- Mono/Wine: [Framework Mono 6.14 release (WineHQ)](https://winehq.org/news/2025030801), [heise: Mono 6.14 arm64 macOS](https://heise.de/-10312438), [Mono donated to WineHQ (DevClass)](https://www.devclass.com/development/2024/08/28/microsoft-quietly-donates-mono-project-to-winehq-but-why/1628359), [Mono WinForms Carbon64/Cocoa (InfoQ 2018)](https://www.infoq.com/news/2018/02/mono-forms-carbon64/), [Miguel de Icaza on WinForms macOS backends](https://tirania.org/blog/archive/2018/Feb-20-1.html), [BizHawk on Mono WinForms limits](https://github.com/TASEmulators/BizHawk/issues/2261), [TASVideos discussion](https://tasvideos.org/Forum/Posts/459666)
- ComicRack under Wine: [Wine bug 59820](https://list.winehq.org/hyperkitty/list/wine-bugs@list.winehq.org/thread/44KERWVD7BTMMN53DHWRYN7FOOKYQT2H/), [Wine bug 60013](https://list.winehq.org/hyperkitty/list/wine-bugs@list.winehq.org/thread/KUNNA36YVUQXJQQCWIHYLZ76ARV25VGP/), [CodeWeavers ComicRack compatibility](https://www.codeweavers.com/compatibility/crossover/comicrack)
- Rosetta 2 timeline: [MacRumors: macOS 26.4 Rosetta warnings](https://www.macrumors.com/2026/02/16/macos-tahoe-26-4-rosetta-2-warnings/), [heise: Intel software will no longer run](https://heise.de/-10460014), [macerkopf: macOS 27 last with full Rosetta 2](https://www.macerkopf.de/2026/06/10/macos27-golden-gate-letzte-version/), [Gigazine: WWDC25 announcement](https://wbgsv0a.gigazine.net/gsc_news/en/20250611-apple-macos-tahoe-26-intel-mac-support)
- Libraries: [GeneXus.Drawing.Common (System.Drawing on SkiaSharp)](https://www.nuget.org/packages/GeneXus.Drawing.Common/), [bblanchon.PDFium.macOS](https://www.nuget.org/packages/bblanchon.PDFium.macOS), [IronPython 2.7.12 (netstandard2.0/netcoreapp3.1)](https://www-1.nuget.org/packages/IronPython/2.7.12), [IronPython 3.4.2](https://www.nuget.org/packages/IronPython/latest), [CoreWCF 1.0 announcement](https://devblogs.microsoft.com/dotnet/corewcf-v1-released/), [Eto.Platform.macOS](https://www.nuget.org/packages/Eto.Platform.macOS), [Modern.Forms](https://github.com/jsuarezruiz/modern.forms), [ModernFormsNext](https://www.nuget.org/packages/ModernFormsNext)
- Apple image formats: [WWDC23 session 10122 (HEIC/AVIF/JPEG XL in Safari 17)](https://developer.apple.com/videos/play/wwdc2023/10122), [MacGeneration on JPEG XL support since macOS Sonoma](https://www.macg.co/ailleurs/2026/01/chrome-va-reprendre-en-charge-le-jpeg-xl-apres-quelques-annees-de-pause-306157)
- Script ecosystem (§5.9): [maforget/ComicRack_AmazonScrapper (.NET-assembly plugin with a one-line Python loader)](https://github.com/maforget/ComicRack_AmazonScrapper), [FrederikBaerentsen ComicRack_Scripts / CVIssueCount](https://gitea.baerentsen.space/FrederikBaerentsen/ComicRack_Scripts/releases/tag/CVIssueCount.v3.0), [ComicRack scripts documentation site](https://comicrack.baerentsen.space/Introduction/), [dotnet/winforms (MIT; layout engines and control logic to port)](https://github.com/dotnet/winforms), [WPF `Dispatcher.PushFrame` (the nested-frame model Avalonia's dispatcher mirrors)](https://learn.microsoft.com/ru-ru/dotnet/api/system.windows.threading.dispatcher.pushframe)
- Domain alternatives: [AlternativeTo: ComicRack alternatives](https://alternativeto.net/software/comicrack/), [YACReader alternatives](https://alternativeto.net/software/yacreader), [How-To Geek: comic readers for Windows/Mac/Linux](https://www.howtogeek.com/321394/the-best-comic-book-readers-for-windows-mac-and-linux/)

> Facts marked "verify" in the text, and every version number above, should be re-checked by the session that depends on them. Ecosystem details (Avalonia Metal default, ImageIO format coverage per macOS version, package contents) move quickly.
