# ComicRack CE for macOS (work in progress)

Plans: `mac/plans/mvp-track.md` and `MACOS_NATIVE_PATH.md`.

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

`api-inventory` builds pass A clean and lists the System.Drawing, System.Windows.Forms and P/Invoke members the Core uses. Pass A and B share `obj/` (the restore result is per pass): `dotnet build` restores by itself, but a bare `dotnet msbuild` query needs `-restore` after the other pass ran.

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
- Exit codes: 0 ok, 1 smoke test failed, 2 bad arguments, 4 no usable display.

Example (real window, needs an unlocked screen): `dotnet run --project mac/ComicRack.Mac -- --smoke-test=5 --data-dir "$TMPDIR/crmac"; echo $?`

### Rules for app code

- Every subscription to a Core event goes through `UiDispatch` (`UiDispatch.Wrap(handler)` or `UiDispatch.Post` inside the handler). Review checklist item for every M2 task.
- Log through `AppLog` (it writes to the host log once the host is open).
