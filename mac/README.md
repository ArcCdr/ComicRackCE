# ComicRack CE for macOS (work in progress)

Plans: `mac/plans/mvp-track.md` and `MACOS_NATIVE_PATH.md`.

## Prerequisites

- Apple Silicon Mac, macOS 14 or later
- .NET SDK 10.0.x (`brew install --cask dotnet-sdk`; check with `dotnet --list-sdks`)

## Build

```bash
dotnet build mac/ComicRack.Mac.slnx
```

Files in the original project folders are not edited. `mac/EDITS.md` logs any approved exception.

## Compile census (M0.3a)

The shadow projects (`cYo.Common.Core`, `ComicRack.Engine.Core`, `ComicRack.Plugins.Core`) link the original sources and are **expected to fail** to compile until M1.3. They are not part of the solution. To measure the errors:

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
