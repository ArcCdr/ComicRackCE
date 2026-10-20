# S4: IronPython 2.7.12 on .NET 10 arm64 (spike P4.1)

Date: 2026-10-20. Runtime: .NET 10.0.13 arm64, IronPython 2.7.12 (`IronPython` NuGet, `IronPython.StdLib` 2.7.12), `cYo.Compat.Forms` / `cYo.Compat.Drawing` stand-ins.

## Verdict

**GO, with one binary patch of `IronPython.dll` (build step).** Stock IronPython 2.7.12 is **NO-GO on .NET 9 and later**: every new-style Python class fails to be created, so the standard library (`import os`), `class X(Control)` in a script and almost any real plugin do not run. A four-instruction IL patch fixes it completely (checked in the dev build and in the hardened, ad-hoc signed bundle). D6 is not weakened: scripts are not changed. The patch needs no owner decision; C3 is not raised by this spike. Two other gaps decide whether D6 holds and belong to later tasks: the assembly identity of `System.Windows.Forms` / `System.Drawing` (P4.2, section "Resolution table") and the breadth of the Forms stand-in (P4.10).

| Check | Stock 2.7.12 | Patched | Notes |
|---|---|---|---|
| `PythonCommand.CompileExpression` with a smart-list expression | PASS | PASS | boolean expression, Python 2 integer division (`7 / 2 = 3`), syntax error gives `null` |
| `ComicBookExpressionMatcher` (`{Series} == 'Batman' and {Year} > 2000`) over 3 books | PASS | PASS | exactly 1 match |
| `import os, re, datetime` (StdLib) | FAIL (`ValueError` / `Exception: Error`) | PASS | 45 of 45 probed modules import with the patch (os, os.path, re, datetime, json, csv, codecs, glob, shutil, urllib, urllib2, xml.dom.minidom, xml.etree.ElementTree, socket, subprocess, threading, sqlite3, zipfile, hashlib, ...) |
| `class UIControl(Control)` at the top level (as in `Sample.py`) | FAIL | PASS | also `OnPaint` override, instance creation |
| `Sample.py` headless hooks: ParseComicPath, CreateBookList (`GetBooksWith`), NetSearch, ComicInfoHtml (2), QuickOpenHtml | not reached | PASS (6 of 6) | only when the reference lines resolve (see below) |
| `Sample.py` as shipped (`clr.AddReferenceByPartialName("System.Windows.Forms")`) | FAIL | FAIL | `IOException: Could not add reference to assembly System.Windows.Forms`: identity gap |
| `Sample.py` DrawThumbnailOverlay (`SolidBrush`, `FillRectangle` through the Drawing stand-in) | not reached | PASS | |
| Same checks inside the signed bundle (hardened runtime) | same as dev | same as dev | see "Bundle" |

## Root cause and patch

`IronPython.Runtime.Types.NewTypeMaker.ImplementCustomTypeDescriptor` loops over `typeof(ICustomTypeDescriptor).GetMethods()` and, for each method, emits a call to the static helper of the same name in `CustomTypeDescHelpers` (`ImplementCTDOverride` -> `ILGen.EmitCall(type, name, paramTypes)`). .NET 9 added four members with a default implementation to `ICustomTypeDescriptor` (`GetConverterFromRegisteredType`, `GetEventsFromRegisteredType`, `GetPropertiesFromRegisteredType`, `RequireRegisteredTypes`). The helpers class has no such methods, `EmitCall` throws `ArgumentException: Type doesn't have a method with a given name and signature. (Parameter 'type')`. `NewTypeMaker` runs for every new-style class (`class X(object)`, every class that derives from a .NET type, exceptions), only old-style classes (`class X:`) are unaffected. Python modules fail on import; the exception in the host appears as `ValueError`, `Exception: Error`, `TargetInvocationException`.

Patch (`mac/spikes/S4-patch`, Mono.Cecil 0.11.6, MIT): at the start of `NewTypeMaker.ImplementCTDOverride(MethodInfo)` insert `if (!m.IsAbstract) return;`. The generated type then keeps the default implementation of those four members, which is what the runtime does for any other implementer. All other interface methods are abstract and are overridden as before. IronPython is Apache-2.0: modification is allowed; the build step must keep the license file in the bundle (P6). The patched assembly keeps its name and version; .NET does not validate the strong-name signature. `mac/spikes/S4-ironpython/run.sh orig|patched` shows both behaviours.

Not tried and not needed: a runtime detour (MonoMod, Harmony; needs more entitlements and is fragile), building IronPython from source (a fork to maintain), IronPython 3.4 (Python 3, breaks D6).

## Resolution table (which assembly loads)

Observed with the patched engine; the unpatched engine gives the same rows. "Stand-ins" are `cYo.Compat.Forms` and `cYo.Compat.Drawing` (in the output folder next to the host). Each scenario is a fresh IronPython engine (`mac/spikes/S4-ironpython/selftest.py`).

| Script text | Result |
|---|---|
| `clr.AddReferenceByPartialName("System.Windows.Forms")` / `clr.AddReference("System.Windows.Forms")` | **IOException**: no assembly of that name exists on .NET 10. The script stops at its first lines. Five of the 7 bundled scripts do this in their first lines |
| `clr.AddReferenceByPartialName("System.Drawing")` / `AddReference("System.Drawing")` | ok: the framework facade `System.Drawing.dll`. `Point`, `Size`, `Rectangle`, `Color` resolve to `System.Drawing.Primitives 10.0.0.0` (the same types the stand-in uses). `Bitmap`, `Graphics`, `SolidBrush`, `Font`, `Pen`, `Image` resolve to the **real** `System.Drawing.Common 4.0.2.0` (pulled in by IronPython's package dependencies, lies in the output folder), and `Bitmap(10, 10)` throws `The type initializer for 'Gdip' threw an exception`. So the unmodified reference line gives scripts a Drawing that cannot work |
| `clr.AddReference("System.Drawing.Common")` | ok, same real assembly; `Point`/`Color` are not visible then |
| `clr.AddReference("cYo.Compat.Drawing")` | ok: `Bitmap`, `Graphics`, `SolidBrush`, `Font`, `Pen`, `Image` from the stand-in (1.0.0.0), `Bitmap(10, 10)` works; `Point`, `Size`, `Rectangle`, `Color` are **not** in it (they come from the framework primitives, so `System.Drawing` must be referenced as well) |
| Both `System.Drawing.Common` and `cYo.Compat.Drawing` referenced | **the one referenced last wins** for `Bitmap`/`Graphics` (Common then Compat: stand-in; Compat then Common: real Common) |
| `clr.AddReference("cYo.Compat.Forms")` | `Form`, `Control`, `DialogResult`, `Keys` resolve (1.0.0.0); `Label`, `NumericUpDown`, `ListBox`, `MessageBox`, `DockStyle`, `Button`, `TextBox`, `ComboBox`, `CheckBox` do not exist yet (types-only slice, P4.10) |
| No `clr.AddReference` at all | `from System.Drawing import *` and `from System.Windows.Forms import *` raise `ImportError: No module named Drawing / Windows` (as on Windows: scripts must add the references) |
| `class UIControl(Control)` after referencing the Forms stand-in | works (patched); `UIControl()` is an `IronPython.NewTypes.System.Windows.Forms.Control_14$14` |
| `clr.AddReferenceByPartialName("ComicRack.Engine")`, `"ComicRack.Plugins"`, `"cYo.Common"` | **IOException**: the shadow assemblies are named `ComicRack.Engine.Core`, `ComicRack.Plugins.Core`, `cYo.Common.Core` (these names load). `RefreshView.py` uses the first form. Namespaces are unchanged: `from cYo.Projects.ComicRack.Engine import ComicBook` works without any reference (`ComicRack.Engine.Core` is preloaded by `PythonCommand`) |
| `from cYo.Common.Windows.Forms import FormUtility` (after `AddReferenceByPartialName('cYo.Common.Windows')`) | fails: `cYo.Common.Windows` is not linked (UI layer, `RefreshView.py` and any script that uses `FormUtility`) |
| `from cYo.Common.Reflection import DuckTyping` | needs `AddReference("cYo.Common.Core")` first; resolves. `DuckTyping.Implement<T>` throws `NotSupportedException` (CodeDom generator excluded). Python cannot call the generic overloads anyway (`Multiple targets could match`); no bundled script and no Core code uses it |

### Experiment for the identity choice (feeds P4.2)

- **`AssemblyLoadContext.Default.Resolving` hook that answers `System.Windows.Forms` with `cYo.Compat.Forms`: does not work.** The runtime rejects it: `FileLoadException ... InvalidOperationException: Resolved assembly's simple name should be the same as the requested name`. A hook cannot return an assembly with another simple name.
- **Rewriting the two reference lines of every bundled script** (`AddReferenceByPartialName("System.Windows.Forms")` -> `AddReference("cYo.Compat.Forms")`, and `System.Drawing` -> `AddReferenceByPartialName("System.Drawing"); clr.AddReference("cYo.Compat.Drawing")`) makes `Sample.py`, `Autonumber.py`, `CommitProposed.py`, `NewComics.py`, `OtherScripts.py` and `SearchAndReplace.py` run their top level (imports, class definitions); `RefreshView.py` still fails on `ComicRack.Engine`. This is a simulation of a solution (scripts must not be changed, D6), not a proposal.
- Options left for P4.2 (not tested here): (a) name the stand-in assemblies `System.Windows.Forms` and `System.Drawing.Common` (and keep `System.Drawing.Common` 4.7.0 out of the output so only the stand-in exists; the `System.Drawing` facade forwards `Bitmap` to that name); (b) a `clr` module wrapper that is put in `sys.modules` before a script runs and maps the names in `AddReference*`. Option (a) also needs `ComicRack.Engine`, `ComicRack.Plugins` and `cYo.Common` as assembly names of the shadow projects (rename `AssemblyName`; the namespaces and the types stay) or the same wrapper.

## StdLib

`IronPython.StdLib` 2.7.12 (NuGet, 8 MB, 1043 files, 11 MB on disk) copies a `Lib/` folder next to the host assembly; IronPython finds it there by itself (`sys.path = ['.', '<exe folder>/Lib', <script folder>]`). In the bundle that is `Contents/MacOS/Lib`; `codesign --force --deep` and `codesign --verify --deep --strict` accept it. `sys.platform` is `cli`, `os.name` is `posix`, `os.sep` is `/`.

## Bundle (hardened runtime)

`mac/tools/make-app.sh` bundle (ad-hoc signature, `runtime` flag, entitlements of `mac/tools/mvp.entitlements`), run through its own process (since RV4b a release build needs `COMICRACK_ENABLE_SELFTEST=1`): `Contents/MacOS/ComicRack.Mac --script-selftest mac/spikes/S4-ironpython/selftest.py` (no window; prints the lines of the table and exits 0 or 1).

| Bundle | Result |
|---|---|
| as built (stock IronPython, no `Lib`) | same 4 failures as the dev build (StdLib, `Control` subclass x2, `Sample.py` reference line) |
| patched `IronPython.dll` and `Lib/` copied in, re-signed with the same command | `all checks passed` (with the reference rewrite; without it only the `Sample.py` reference line fails) |
| entitlements `allow-jit` + `disable-library-validation` only (no `allow-unsigned-executable-memory`) | still passes: IronPython's dynamic code needs `allow-jit`, not `allow-unsigned-executable-memory` |
| `disable-library-validation` only (no `allow-jit`) | the runtime does not start (`Failed to create CoreCLR`, exit 137) |

So the hardened runtime is not an obstacle; `allow-unsigned-executable-memory` could be dropped later (P6 decides, after notarization).

## Bundled scripts, top level (information)

With the patch and the rewritten reference lines: `Sample.py` (12 commands from comments plus 2 in `Sample.xml`), `Autonumber.py`, `CommitProposed.py`, `NewComics.py`, `OtherScripts.py`, `SearchAndReplace.py` load. UI hooks of `Sample.py` fail with `NameError` for names the Forms stand-in lacks (`ListBox`, `MessageBox`), as expected before P4.10.

## Reproduce

```
mac/spikes/S4-ironpython/run.sh orig             # stock IronPython: 4 failures
mac/spikes/S4-ironpython/run.sh patched          # patched: 1 failure (Sample.py reference line)
mac/spikes/S4-ironpython/run.sh patched-rewrite  # patched, references rewritten in a temp copy of the scripts: all pass
mac/spikes/S4-ironpython/run.sh patched-resolver # the Resolving hook experiment
```
`run.sh` builds `mac/spikes/S4-ironpython` (console, not in the solution; it compiles `mac/ComicRack.Mac/ScriptSelfTest.cs`, the same code as the hidden `--script-selftest FILE` option of the app), copies the output to `mac/artifacts/s4/run-<variant>` and runs `selftest.py`. Output lines: `PASS`/`FAIL` (checks), `INFO ... RES` (observations), exit code 0 when no `FAIL`.
