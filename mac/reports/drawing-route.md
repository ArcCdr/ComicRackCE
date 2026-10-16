# Drawing route data (spike S2, task M0.4)

Input for decision Q1 (G0): adopt `GeneXus.Drawing.Common` as the System.Drawing replacement, or build a custom shim in the literal `System.Drawing` namespaces. This report gives data and a recommendation; the owner decides.

- **Date:** 2026-10-17; **SDK:** 10.0.402 on macOS 26.6 (Apple M4); **repository commit:** `da44c36`.
- **Inventory:** `mac/artifacts/api-inventory/inventory.json` (same data as `mac/reports/api-inventory.md`, generated at `53d9f7a`): 47 `System.Drawing.Common` types and 126 members (19 ctors, 63 methods, 42 properties, 2 fields; 714 call sites), plus 10 `System.Drawing.Primitives` types.
- **Spike projects (throwaway, not in `mac/ComicRack.Mac.slnx`):** `mac/spikes/S2-coverage/` (reflection coverage tool) and `mac/spikes/S2-genexus/` (6 xUnit checks).

## 1. The package

| | |
|---|---|
| Package | `GeneXus.Drawing.Common` **2.2.3** (latest stable; published 2025-09-11; 15 stable versions since 1.0.0) |
| **Licence** | **Apache-2.0** (NuGet licence expression). The plan text calls it MIT; that is wrong. See section 5 |
| Repository | github.com/genexuslabs/GeneXus.Drawing-DotNet (commit `47a0a7c`) |
| Target | `netstandard2.0` only; assembly version 2.0.0.0; 121 KB package |
| Dependencies | `SkiaSharp` **2.88.8**, `SkiaSharp.Extended` 2.0.0, `SkiaSharp.Svg` 1.60.0, `SkiaSharp.NativeAssets.Linux.NoDependencies` 2.88.7 (`SkiaSharp` 2.88.8 brings `NativeAssets.macOS` itself) |
| Namespaces | **`GeneXus.Drawing`**, `GeneXus.Drawing.Drawing2D`, `.Imaging`, `.Text`. Nothing is declared in `System.Drawing.*` |
| Own copies of the primitives | `Color`, `Point`, `PointF`, `Size`, `SizeF`, `Rectangle`, `RectangleF`, `KnownColor`, `ColorTranslator` are **new types** in `GeneXus.Drawing`, distinct from the BCL `System.Drawing.*` structs. Their only conversions are to SkiaSharp types and between GeneXus types; none to or from `System.Drawing.*` (checked by reflection) |

## 2. Coverage

Method: `dotnet run --project mac/spikes/S2-coverage` maps each inventory entry `System.Drawing.X` to `GeneXus.Drawing.X` and compares by reflection. A member **matches** when name, parameter types and return type are equal by simple type name; this ignores the namespace difference, so it is the generous reading. A member is **convertible** when only an implicit numeric widening (`int` to `float`) or extra optional parameters differ, which keeps call sites compiling. Methods whose body is only `new Exception; throw` are flagged as stubs. Enums are compared name by name and value by value against `System.Drawing.Common` 10.0.0.

### 2.1 Headline (`System.Drawing.Common` scope: 47 types, 126 members, 714 call sites)

| Measure | Result |
|---|---|
| Types present in GeneXus | **35 of 47 = 74.5 %** (34 = 72.3 % with the same kind; `ImageFormat` is an enum there, a class in GDI+) |
| Members with an exact signature | 83 of 126 = **65.9 %** |
| Members with a source-compatible signature (exact + convertible) | 94 of 126 = **74.6 %** |
| ... of which not stubs | 90 of 126 = **71.4 %** (stubs: `Bitmap.GetHicon`, `Font.ToHfont`, `Graphics.GetHdc`, `Icon.FromHandle`) |
| Call-site weighted, source-compatible and not a stub | 517 of 714 = **72.4 %** |
| Enums | 17 of 18 present, and all 17 match in names, values and `[Flags]`; `ContentAlignment` is absent |
| Threshold for adopting the package | 90 % (not met by any measure) |

By kind: ctors 14 of 19 match (9 exact), methods 53 of 63 (47 exact), properties 27 of 42, fields 0 of 2.

A matching member is not always usable as it stands: 22 of the 94 mention `Color`, `Point`, `Size`, `Rectangle` or their `F` variants, which in GeneXus are its own types (section 1). Only 72 match without a type bridge.

### 2.2 What is missing, ordered by weight

| Missing or broken in GeneXus | Core uses | Why it matters |
|---|---|---|
| **`BitmapData`**; `Bitmap.LockBits`/`UnlockBits` exist but take and return `object` and **`LockBits` throws `NotImplementedException`** | 107 uses of `BitmapData`; `LockBits` 28, `UnlockBits` 29, `Scan0` 32, `Stride` 43 call sites | The pixel loops in `ImageProcessing`, `WebpImage`, `HeifAvifImage`, `FastBitmap` |
| **`ImageAttributes`, `ColorMatrix`** (the `DrawImage` overloads take `object imageAttrs` and ignore it) | 7 + 27 uses; 16 `ColorMatrix[,]` call sites | Grayscale, brightness, contrast in `ImageProcessing`, `GraphicsExtensions` |
| **`Encoder`, `EncoderParameter(s)`, `ImageCodecInfo`** | 5 + 3 + 5 + 10 uses | JPEG quality in `BitmapExtensions` (GeneXus has `Save(Stream, ImageFormat, int quality)` instead) |
| `ImageFormat` as a class (`Guid`, `Tiff`, static properties) | 24 uses | GeneXus has an enum, so `ImageFormat.Jpeg.Guid` does not compile |
| `ContentAlignment`, `IDeviceContext`, `SystemBrushes`, `SystemPens`, `SystemFonts` | 23 + 1 + 1 + 1 + 2 uses | Small, but the inventory lists them |
| Near misses: `Image.FromStream(Stream, bool[, bool])`, `Save(Stream, ImageCodecInfo, EncoderParameters)`, `Graphics.SetClip(RectangleF, CombineMode)`, `Bitmap.Clone(Rectangle, PixelFormat)` (returns `object`), `DrawImage(..., ImageAttributes)` | 4 + 1 + 2 + 2 + 2 | Few call sites |

The first three rows are the hot path of the Core's image code. What GeneXus does cover is the cold or cosmetic part.

### 2.3 Per type

Sorted by total uses in the Core (member references, type tokens and declarations). "Signature match" counts exact plus convertible members.

| Type | Kind | In GeneXus | Uses | Members used | Signature match | Notes |
|---|---|---|---|---|---|---|
| `Bitmap` | class | class | 643 | 9 | 6 | 1 of the matches is a stub (`GetHicon`) |
| `Image` | class | class | 430 | 16 | 13 | |
| `Graphics` | class | class | 141 | 34 | 32 | 1 of the matches is a stub (`GetHdc`) |
| `Imaging.BitmapData` | class | **absent** | 107 | 3 | 0 | |
| `Brush` | class | class | 92 | 1 | 1 | |
| `Pen` | class | class | 83 | 2 | 2 | |
| `Font` | class | class | 74 | 7 | 7 | 1 of the matches is a stub (`ToHfont`) |
| `Drawing2D.GraphicsPath` | class | class | 35 | 6 | 6 | |
| `StringFormat` | class | class | 34 | 8 | 8 | |
| `Imaging.ColorMatrix` | class | **absent** | 27 | 4 | 0 | |
| `Imaging.PixelFormat` | enum | enum | 27 | - | - | 23 names, 0 missing, 0 value differences |
| `Imaging.ImageFormat` | class | enum (kind differs) | 24 | 6 | 0 | |
| `StringAlignment` | enum | enum | 24 | - | - | 3 names, all equal |
| `ContentAlignment` | enum | **absent** | 23 | - | - | |
| `Icon` | class | class | 22 | 6 | 6 | 1 of the matches is a stub (`FromHandle`) |
| `Drawing2D.Matrix` | class | class | 11 | 4 | 4 | |
| `FontStyle` | enum | enum | 11 | - | - | 5 names, all equal |
| `Imaging.ImageCodecInfo` | class | **absent** | 10 | 2 | 0 | |
| `Drawing2D.CoordinateSpace` | enum | enum | 8 | - | - | 3 names, all equal |
| `SolidBrush` | class | class | 8 | 1 | 1 | |
| `Imaging.ImageAttributes` | class | **absent** | 7 | 2 | 0 | |
| `Imaging.Encoder` | class | **absent** | 5 | 2 | 0 | |
| `Imaging.EncoderParameters` | class | **absent** | 5 | 2 | 0 | |
| `Drawing2D.PathGradientBrush` | class | class | 4 | 3 | 3 | |
| `GraphicsUnit` | enum | enum | 4 | - | - | 7 names, all equal |
| `StringFormatFlags` | enum | enum | 4 | - | - | 9 names, all equal |
| `Drawing2D.GraphicsState` | class | class | 3 | - | - | |
| `Drawing2D.InterpolationMode` | enum | enum | 3 | - | - | 9 names, all equal |
| `Drawing2D.LinearGradientBrush` | class | class | 3 | 2 | 2 | |
| `Drawing2D.SmoothingMode` | enum | enum | 3 | - | - | 6 names, all equal |
| `Imaging.EncoderParameter` | class | **absent** | 3 | 1 | 0 | |
| `Text.TextRenderingHint` | enum | enum | 3 | - | - | 6 names, all equal |
| `Drawing2D.CombineMode` | enum | enum | 2 | - | - | 6 names, all equal |
| `Drawing2D.MatrixOrder` | enum | enum | 2 | - | - | 2 names, all equal |
| `FontFamily` | class | class | 2 | 1 | 1 | |
| `Region` | class | class | 2 | 1 | 1 | |
| `SystemFonts` | static class | **absent** | 2 | 2 | 0 | |
| `Brushes` | static class | static class | 1 | 1 | 1 | |
| `Drawing2D.CompositingMode` | enum | enum | 1 | - | - | 2 names, all equal |
| `Drawing2D.CompositingQuality` | enum | enum | 1 | - | - | 6 names, all equal |
| `Drawing2D.PixelOffsetMode` | enum | enum | 1 | - | - | 6 names, all equal |
| `IDeviceContext` | interface | **absent** | 1 | - | - | |
| `Imaging.ImageLockMode` | enum | enum | 1 | - | - | 4 names, all equal |
| `RotateFlipType` | enum | enum | 1 | - | - | 16 names, all equal |
| `StringTrimming` | enum | enum | 1 | - | - | 6 names, all equal |
| `SystemBrushes` | static class | **absent** | 1 | - | - | |
| `SystemPens` | static class | **absent** | 1 | - | - | |

The 126 members split as: 83 exact, 11 convertible, 18 on absent types, 4 `ImageFormat` static properties that are fields in GeneXus, 2 `ImageFormat` members that are missing (`Guid`, `Tiff`), 2 whose return type is `object` (`Bitmap.Clone(Rectangle, PixelFormat)`, `Bitmap.LockBits`), and 6 other near misses (`UnlockBits`, two `FromStream` overloads, `Save(Stream, ImageCodecInfo, EncoderParameters)`, `SetClip(RectangleF, ...)`, `DrawImage(..., ImageAttributes)`).

### 2.4 `System.Drawing.Primitives` (10 types, already in .NET 10)

GeneXus has 9 of the 10 (all but `SystemColors`), and 88 of the 123 inventory members match by name and signature. That does not count towards coverage, because the BCL already provides these types and the shim must not redeclare them (M1.2a). It matters for the opposite reason: GeneXus's copies are separate types (section 1), so a GeneXus `Graphics.FillRectangle(Brush, Rectangle)` takes `GeneXus.Drawing.Rectangle`, not the `System.Drawing.Rectangle` that the Core passes.

## 3. The six checks (xUnit, on this Mac)

`dotnet test mac/spikes/S2-genexus --logger "console;verbosity=detailed"` gives **2 passed, 4 failed**. The same split occurs with the SkiaSharp that `Avalonia.Skia` 12.1.4 depends on (`-p:SkiaOverride=3.119.4`); there was no `MissingMethodException`, though six checks cover only a small part of the API. A failed check is a result, not something to fix here.

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | Decode a JPEG from a stream | **PASS** | `Image.FromStream` gives a `GeneXus.Drawing.Bitmap` of 64x48; the left half reads 254,0,0 and the right half 0,0,254. It reports `Format24bppRgb` although it stores RGBA (see 6) |
| 2 | `LockBits` on `Format32bppArgb` (`Scan0`, `Stride`, BGRA) | **FAIL** | `System.NotImplementedException` from `Bitmap.LockBits(Rectangle, ImageLockMode, PixelFormat, object)`. The public signature returns `object` and there is no `BitmapData` type, so nothing could be read even without the exception |
| 3 | `DrawImage` downscale with `HighQualityBicubic` | **FAIL** | `InterpolationMode` round-trips as a property but **has no effect**: a 256 to 64 reduction of a 1-pixel checkerboard is identical for `HighQualityBicubic` and `NearestNeighbor` (worst deviation from mid grey is 128, pixels stay 0 or 255; a proper reduction gives about 127). A side probe tried six modes and three `DrawImage` overloads with the same result |
| 4 | Save a JPEG with an `EncoderParameter` quality | **FAIL** on API shape; the capability works | `Encoder`, `EncoderParameter`, `EncoderParameters` and `ImageCodecInfo` do not exist. `Save(Stream, ImageFormat.Jpeg, int quality)` does work: on a 192x192 noise image it gives 3,957 bytes at quality 20 and 32,357 bytes at quality 95, both with the JPEG magic |
| 5 | `ImageAttributes` + `ColorMatrix` grayscale | **FAIL** | Neither type exists (no type with `ColorMatrix`, `ImageAttributes` or `ColorFilter` in its name). `DrawImage(..., object imageAttrs, object callback)` accepts any object without throwing, so attributes would be silently ignored |
| 6 | Underlying `SKBitmap` without a copy | **PASS** | A public explicit conversion operator exists: `(SKBitmap)bitmap` (also `(SKImage)image` and `(SKCanvas)graphics`). Repeated conversions return the same instance and pixel pointer, and a write through the `SKBitmap` is visible through `GetPixel`. Caveat: the bitmap is **`Rgba8888` premultiplied**, not BGRA, so it cannot back a BGRA `LockBits` without a conversion |

Not tested: performance, text drawing, the many other GeneXus members, the `object`-typed overloads beyond the probes above, and IronPython (S4 is the place for that).

## 4. Shim size (what a custom `System.Drawing` shim has to declare)

From the inventory, `System.Drawing.Common` scope only (the primitives come from the BCL):

| Kind | Count |
|---|---|
| Types | **47** (18 enum, 24 class, 4 static class, 1 interface) |
| Constructors | **19** |
| Methods | **63** |
| Properties | **42** |
| Fields | **2** |
| **Members in total** | **126** |

By role, and how much of it needs real behaviour in the MVP (M1.2b/c) versus a throwing skeleton (M1.2a):

| Group | Types | Ctors | Methods | Props | Fields | Call sites | MVP |
|---|---|---|---|---|---|---|---|
| Image, Bitmap, BitmapData, formats, encoders | 11 | 5 | 18 | 16 | 2 | 513 | real (M1.2b) |
| Graphics core, modes, ImageAttributes, ColorMatrix, Matrix, Region | 16 | 5 | 30 | 10 | 0 | 121 | the subset M1.2c lists; the rest throws |
| Brushes, pens, paths | 9 | 5 | 8 | 3 | 0 | 36 | `Brush`, `SolidBrush`, `Brushes` real or trivial; the rest throws |
| Text and fonts | 10 | 3 | 4 | 11 | 0 | 33 | throws (text drawing is outside the MVP) |
| Icon and handle APIs | 1 | 1 | 3 | 2 | 0 | 11 | throws |

Roughly 55 members need real behaviour in the MVP and about 70 can stay skeleton. Low-confidence estimate of code size: about 1,000 lines for the skeleton (one file per type plus enums) and 1,500 to 2,500 lines for the real `Image`/`Bitmap`/`BitmapData`/`Graphics` bodies. The M1.2a to M1.2c task split already matches this.

## 5. Recommendation

**Build the custom shim** (`cYo.Compat.Drawing`, namespaces `System.Drawing`, `.Drawing2D`, `.Imaging`, `.Text`), as the default rule says. Do not adopt GeneXus as a package. None of the three adoption conditions holds:

1. **Coverage is 72 to 75 %, not 90 %**, and the gap is in the hot path (`BitmapData`/`LockBits`, `ColorMatrix`/`ImageAttributes`, `Encoder*`).
2. **4 of 6 checks fail.** Two of them (`LockBits`, `InterpolationMode`) are functional gaps in the library, not just missing names.
3. **Namespace aliasing cannot work for scripts (D6).** This is reasoning, not a test. A `global using Bitmap = GeneXus.Drawing.Bitmap;` exists only at compile time. An IronPython script runs `clr.AddReference('System.Drawing')` and `from System.Drawing import Bitmap`, and IronPython finds types by their CLR namespace in loaded assemblies, so the types must really live in `System.Drawing.*`. Aliases would also fail inside the Core: it passes BCL `System.Drawing.Color`/`Point`/`Rectangle` values around, and GeneXus's same-named structs are different types with no conversion between them.

Two further reasons: GeneXus pins SkiaSharp 2.88.x while `Avalonia.Skia` 12.1.4 depends on SkiaSharp 3.119.4 (the checks still ran with 3.119.4, but an old pinned dependency is a standing risk), and its pixel storage is `Rgba8888` premultiplied, while the Core's `LockBits` code needs BGRA (M1.2b maps to `Bgra8888`).

**Copying GeneXus source: nothing is worth copying.** The rule allows copying per member where GeneXus passed the checks. Only checks 1 and 6 passed, and both are a few lines of SkiaSharp (`SKBitmap.Decode`; the `SkiaInterop.GetSKBitmap` accessor that M1.2b adds). Copying would also raise a licence question: GeneXus is **Apache-2.0**, not MIT as the plan says, and this repository ships the GPLv2 text. The FSF considers Apache-2.0 incompatible with GPLv2-only (it is compatible with GPLv3). Whether the repository is "GPLv2 only" or "or later" was not checked here. If the owner ever wants GeneXus code, settle that first and carry its licence and NOTICE.

**Findings to reuse in M1.2b and M1.2c:**
- Back `Bitmap` with `SKColorType.Bgra8888` as planned. GeneXus's `Rgba8888` default is what makes a BGRA `LockBits` impossible for it.
- Apply `InterpolationMode` for real through `SKSamplingOptions`; GeneXus ignores it. The check-3 test (1-pixel checkerboard, 4:1 reduction, mid grey expected) is a good model for the M1.2c test.
- JPEG quality works through `SKImage.Encode(Jpeg, quality)` (check 4: file size ratio 8.2 between quality 95 and 20).
- `ImageFormat` must be a class with `Guid` and static properties, and `ImageCodecInfo.GetImageEncoders()` must return entries whose `FormatID` equals `ImageFormat.X.Guid`.

## 6. Reproduce

```bash
dotnet run --project mac/tools/api-inventory                 # only if mac/artifacts/api-inventory/inventory.json is absent
dotnet run --project mac/spikes/S2-coverage -- mac/artifacts/api-inventory/inventory.json mac/artifacts/s2/coverage.json
dotnet test mac/spikes/S2-genexus --logger "console;verbosity=detailed"
dotnet test mac/spikes/S2-genexus -p:SkiaOverride=3.119.4     # same checks with the SkiaSharp that Avalonia.Skia 12.1.4 depends on
```

`mac/artifacts/s2/coverage.json` (git-ignored) holds the per-member result with its level (`exact`, `convertible`, `arity`, `name`, `ret-differs`, `field-for-property`, `type-missing`, `missing`), the candidate GeneXus signatures and the stub flag. Neither spike project is in the solution. Delete them after G0, or keep `S2-coverage` to measure the custom shim (see `mvp-track.md` section 9).
