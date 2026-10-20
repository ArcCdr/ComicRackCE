# S6: Script UI modal loop and assembly identity (spike P4.2)

Date: 2026-10-20. Runtime: .NET 10.0.13 arm64, Avalonia 12.1.4 (headless platform with the Skia renderer; Avalonia.Native for the real-window runs), IronPython 2.7.12 with the S4 patch, SkiaSharp 3.119.4. Spike code: `mac/spikes/S6-modal/` (not in the solution). Raw logs and snapshots: `mac/reports/s6-modal/`.

## Verdict

**GO. The facade architecture of `MACOS_NATIVE_PATH.md` §5.9 holds; D6 is not weakened; C3 is not raised.**

1. **Modal loop: GO (headless Avalonia, all cases).** `Form.ShowDialog()` is `window.ShowDialog(owner)` followed by `Dispatcher.UIThread.PushFrame(frame)` on the UI thread; from another thread the facade marshals the whole call to the UI thread and blocks the caller. `MessageBox.Show`, closing during the frame, nested dialogs, a dialog opened from a handler of a dialog, exceptions in handlers, `Application.DoEvents`, cross-thread updates, modeless forms and shutdown during a dialog all behave as WinForms scripts expect. 78 of 78 checks pass, five runs in a row, no timing assertion.
2. **Python on the facade: GO.** A Python subclass of the facade `Control` overrides `OnPaint` and `OnResize` (IronPython 2.7.12 with the S4 patch), draws through the Drawing stand-in, and its pixels reach the frame; `Autonumber.py` and `Sample.py` run **unmodified** and show their dialogs (control trees, values typed through the peers, books renumbered).
3. **Identity: decided, literal assembly names** (details below). The stand-ins are built as `System.Windows.Forms` and `System.Drawing.Common` (AssemblyVersion 10.0.0.0, unsigned) and the shadow assemblies take the names of the originals. With that, every reference line of every bundled script resolves unmodified (27 of 27 checks on the real Core, including all of `Sample.py`), and compiled plug-ins that reference `System.Windows.Forms, Version=4.0.0.0, PublicKeyToken=b77a5c561934e089` bind to the facade. An `AssemblyLoadContext.Resolving` handler is not an alternative (it cannot answer a name with an assembly of another simple name; S4) and is not needed.
4. **Real window server: SKIPPED.** `mac/tools/ui-drive/ui-drive.sh check` printed `unlocked` when the session started and exited 4 (screen locked) from the time the real-window runs were due until the end of the session, and Avalonia.Native cannot start a render timer on a locked screen (the spike exits 4 with the same message as the app). The real-window half is written and syntax checked: `mac/spikes/S6-modal/real.sh all` (every case in a native window, Return/Escape and mouse clicks sent by ui-drive, the owner window must refuse a click during a modal dialog, native window screenshots, a pixel check of the Python-drawn rectangle, shutdown during a dialog). What it could still change: whether Avalonia.Native's `ShowDialog` blocks the owner by itself. Headless it does not, and the facade does not rely on it (the facade owns the modal logic; if the native window does not block the owner, the facade disables the owner chain itself, which is a small change in `FormPeer`). A running frame in a native window is the one unverified mechanism; it is the same `PushFrame` that Avalonia's own `Dispatcher.MainLoop` uses.

## What was built

| Path | Purpose |
|---|---|
| `Forms/` | the facade `System.Windows.Forms` (assembly name literal): `Control` (state, events, Dock layout, `Invoke`/`BeginInvoke`, `PeerPaint`), `Form` (`ShowDialog`, `Close`, `DialogResult`, closing rules, Accept/Cancel button), `MessageBox`, `Application` (`DoEvents`, `ThreadException`), `Timer`, `Button`, `Label`, `TextBox`, `CheckBox`, `ComboBox`, `ListBox`, `NumericUpDown`, `Panel`; the backend interface `IFormsBackend` and the peer interfaces |
| `App/` | Avalonia app: `AvaloniaBackend` and the peers (a `Window` with a `Canvas` per form, real Avalonia controls placed at the rectangles the facade computed, a paint surface that runs `OnPaint` into a stand-in `Bitmap`), the case runner, the Python host (one engine and scope per script, `ComicRack` variable), the scenarios for ui-drive |
| `Plugin/` | a compiled plug-in that uses WinForms types, for the identity matrix |
| `Tools/` | `s6-rebind` (Cecil): renames assemblies, versions, tokens and references in a copy of a build output, so the identity options are tried without touching a production project (a renamed `AssemblyName` gives the same binaries) |
| `scripts/` | `s6-probe.py` (Control subclass, owner draw, handlers), `s6-identity-core.py` (reference lines on the real Core) |
| `stage.sh`, `run.sh`, `identity.sh`, `real.sh` | stage the app with the patched IronPython; the whole matrix; the identity runs; the real-window runs |

## Modal loop and facade cases (headless)

| Case | What it shows | PASS | FAIL | INFO |
|---|---|---|---|---|
| M01 | `ShowDialog` on the UI thread blocks inside a nested frame, a queued job still runs, typed values (TextBox, NumericUpDown, CheckBox, ComboBox) come back, event order Load, Activated, Shown, FormClosing, FormClosed | 7 | 0 | 1 |
| M02 | `ShowDialog` from a background thread: the thread blocks, 3 timer ticks happen meanwhile, the thread continues on itself with the result, `InvokeRequired` is true | 6 | 0 | 0 |
| M03 | `MessageBox.Show` from the UI thread and from a thread; YesNo ignores Esc and the close box, Enter takes the first button, OKCancel Esc cancels, an OK-only box returns OK from its close box | 8 | 0 | 1 |
| M04 | closing during the frame: `Close()` from another thread, a cancelling `FormClosing` (user close twice), `DialogResult = Retry`, `Dispose` inside `ShowDialog`, re-showing a closed form with its old values, `ShowDialog` on a shown form throws | 7 | 0 | 0 |
| M05 | nested dialogs: one opened from the `Shown` event of another; closing the lower one first returns both, innermost first | 4 | 0 | 1 |
| M06 | an exception in a handler goes to `Application.ThreadException` (or is recorded) and the loop and dialog stay alive | 4 | 0 | 0 |
| M07 | `Application.DoEvents` inside a UI-thread loop handles a queued click; off the UI thread it returns | 2 | 0 | 0 |
| M08 | a worker writes `Text` 200 times without `Invoke`, calls `Invoke` and `BeginInvoke`, closes the dialog from its thread (progress dialog) | 4 | 0 | 0 |
| M09 | modeless `Show` returns, survives GC, a button `DialogResult` does not close it, `Close` disposes it | 4 | 0 | 0 |
| M10 | the owner window during a dialog (headless: information only, see Findings) | 0 | 0 | 2 |
| P01 | Python `class Swatch(Control)`: `OnPaint` and `OnResize` overrides run, `e.Graphics.FillRectangle` pixels are in the frame (rgb 220,30,30), `Invalidate()` repaints with the new colour, an exception in `OnPaint` is reported and rendering goes on | 7 | 0 | 1 |
| P02 | a `Paint` handler assigned from Python draws | 1 | 0 | 0 |
| P03 | a Python Click handler opens a `MessageBox` inside a modal dialog (two nested frames), a Python handler that raises, a .NET `Thread` started from Python shows a dialog | 6 | 0 | 0 |
| P04 | cost of the Python `OnPaint` path (information): 4 to 9 microseconds per call on a reused bitmap, 8 to 16 with a new bitmap and `Graphics` per frame | 1 | 0 | 1 |
| S01 | `Autonumber.py` unmodified: its dialog (243 x 154, Accept/Cancel buttons, defaults), OK renumbers 10, 11, 12 with total 12 (alternate series), Escape leaves the books, a worker thread blocks in `ShowDialog` and Enter accepts | 8 | 0 | 3 |
| S02 | `Sample.py` unmodified: `BookHasBeenOpened`, `ConfigureBookHasBeenOpened` and `ShowShutdownMessage` message boxes (Yes gives True, No gives False), `DummyUIInfoPanel` returns the Python `UIControl(Control)`, hosted, `ShowInfo(books)` fills its ListBox | 8 | 0 | 1 |
| | no dialog left open at the end | 1 | 0 | 0 |
| | **total** | **78** | **0** | |

Five consecutive runs: 78 PASS each. Further runs of `run.sh`:

| Run | Result |
|---|---|
| shutdown while a dialog is open (`--shutdown-case`, headless) | `desktop.Shutdown()` closes the dialog, `ShowDialog` returns Cancel, the process exits with code 0 |
| the interactive scenario answered by its autopilot (`--interactive --headless --auto`) | A (Autonumber on the UI thread, Return): books 7, 8, 9; B (worker thread, Escape): unchanged; C (Yes/No box from a worker, Return): True; D (owner-draw panel) opens and closes |
| an exception that escapes a job on the UI thread (`--unhandled-case`) | **the process aborts (exit 134)** unless `Dispatcher.UIThread.UnhandledException` marks it handled; then the app goes on |

Snapshots (headless, window content without title bar): `mac/reports/s6-modal/headless-S01-autonumber.png`, `headless-S02-info-panel.png`, `headless-M01-dialog.png`, `headless-M03-messagebox.png`, `headless-P01-swatch.png` (the Python-drawn rectangle), `headless-P01-swatch-blue.png`, `headless-P02-paint-event.png`.

## Real window server

SKIPPED (screen locked, ui-drive exit 4). Run `mac/spikes/S6-modal/real.sh all` from the VS Code terminal with an unlocked screen (a few minutes): `cases` runs every case above against Avalonia.Native, `interactive` drives the app through ui-drive (`UI_DRIVE_EXE`, a new override of `launch`), `shutdown` quits during a dialog. Expected: the same PASS lines, `PASS A: a click on the owner window during the modal dialog was not handled`, Return and Escape accepted and cancelled by real key events, the red rectangle of the Python `OnPaint` in the real window, exit code 0. The screenshots go to `mac/artifacts/s6/ui-drive/shots/`; copy them to `mac/reports/s6-modal/` when run. Anything that fails there becomes a task for P4.9 (the peers); the facade logic is covered above.

## Assembly identity

Two layers, both without a window server. Layer 1 (`identity.sh forms`): the S6 facade under the literal name, probed with each spelling of a reference line and with a compiled plug-in, in processes of their own. Layer 2 (`identity.sh core`): the **real Core** and the **unmodified bundled scripts** through `PythonCommand`/`PythonPluginInitializer` (the S4 console), in a copy of its output whose assemblies were renamed by `s6-rebind`.

### Layer 1: the facade `System.Windows.Forms` (unsigned)

| Facade version, token | `AddReference('System.Windows.Forms')` and `AddReferenceByPartialName` | strong name `4.0.0.0, b77a5c561934e089` (`AddReference`, `AddReferenceByName`), partial name with `Version=4.0.0.0` | compiled plug-in built against the facade (Python `AddReferenceToFileAndPath`, `Assembly.LoadFrom`) | compiled plug-in that references `System.Windows.Forms 4.0.0.0, b77a5c561934e089` (both ways) |
|---|---|---|---|---|
| 4.0.0.0, none | PASS | PASS | PASS | PASS |
| 10.0.0.0, none | PASS | PASS | PASS | PASS |
| 4.0.0.0, token b77a5c561934e089 | PASS | PASS | PASS | PASS |
| 4.0.0.0, another token | PASS | PASS | PASS | PASS |
| 1.0.0.0, none | PASS | **FAIL** (IOException, FileNotFoundException) | PASS | **FAIL** (FileNotFoundException, both ways) |
| 4.0.0.0 outside the app folder, found by an `AssemblyLoadContext.Default.Resolving` handler by simple name | PASS | PASS | PASS | PASS |

- **The public key token is ignored by the binder** (a reference with the framework's token binds to an unsigned assembly, or to one with another token). **The version is not**: a facade version below the requested one fails. A plug-in built for .NET Framework asks for 4.0.0.0, one built for .NET 8 asks for 8.0.0.0, so the facade must be at least as high as anything asked: 10.0.0.0.
- The `Resolving` handler only helps to load a facade from a folder that is not on the probing path; the assembly still needs the literal simple name. `clr.AddReference('System.Windows.Forms.dll')` and `AddReferenceToFile` with the file name fail in every variant (they need the folder on `sys.path`; same IronPython behaviour as on Windows, not used by scripts).
- With the facade named `cYo.Compat.Forms` (today, the Layer 2 baseline) the first reference line already fails; the S4 experiment showed that a handler cannot map one simple name to another.

### Layer 2: the real Core and the bundled scripts

| Variant (what `s6-rebind` changed) | PASS | FAIL | Notes |
|---|---|---|---|
| baseline (nothing; the state of P4.1) | 8 | 12 | `AddReferenceByPartialName("System.Windows.Forms")` IOException (first lines of 5 of 7 scripts), `System.Drawing` gives the real `Bitmap` that throws, `ComicRack.Engine` / `Plugins` / `cYo.Common` not found, `Sample.py` does not compile |
| `cYo.Compat.Forms` -> `System.Windows.Forms` 4.0.0.0 only | 20 | 7 | Forms names resolve; Drawing (real `System.Drawing.Common`) and the Core names still fail |
| **everything renamed** (Forms 4.0.0.0 or 10.0.0.0, `cYo.Compat.Drawing` -> `System.Drawing.Common` 4.0.2.0 or 10.0.0.0, `ComicRack.Engine.Core` -> `ComicRack.Engine`, `ComicRack.Plugins.Core` -> `ComicRack.Plugins`, `cYo.Common.Core` -> `cYo.Common`, the real `System.Drawing.Common.dll` dropped) | **27** | **0** | the two `AddReferenceByPartialName` lines, both star imports, `Point`/`Size`/`Rectangle`/`Color`/`SizeF` from `System.Drawing.Primitives`, `Bitmap`/`Graphics`/`SolidBrush` from the stand-in (and working), `class UIControl(Control)` with an `OnPaint` override, strong-name spellings of `System.Windows.Forms` and `System.Drawing`, the three Core names, `ComicBook` creation, `Sample.py` complete (12 commands, top level, ParseComicPath, GetBooksWith, DummySearch, both HTML panels, QuickOpenHtml); information lines: the top levels of `Autonumber.py`, `CommitProposed.py`, `NewComics.py`, `OtherScripts.py` and `SearchAndReplace.py` run |
| the same with the Drawing stand-in carrying the token `cc7b13ffcd2ddd51` | 27 | 0 | no difference: the framework's `System.Drawing.dll` forwards to `System.Drawing.Common, Version=0.0.0.0, PublicKeyToken=cc7b13ffcd2ddd51` and an unsigned stand-in satisfies it |

Left over in the renamed run (information): `RefreshView.py` needs an assembly named `cYo.Common.Windows` (`FormUtility.FindActiveService[IRefreshDisplay]()`; the UI layer is not linked); the UI hooks of `Sample.py` fail with `NameError` for `ListBox` / `MessageBox` against the types-only `cYo.Compat.Forms` (P4.9 and P4.10 add the controls; the S6 facade shows they work).

### Decision

**Literal names.** Rename by `AssemblyName` in the project files (no change to any original file, no handler, no wrapper, no change to a script):

| Project | `AssemblyName` | `AssemblyVersion` |
|---|---|---|
| `mac/cYo.Compat.Forms` | `System.Windows.Forms` | 10.0.0.0, unsigned |
| `mac/cYo.Compat.Drawing` | `System.Drawing.Common` | 10.0.0.0, unsigned |
| `mac/ComicRack.Engine.Core` | `ComicRack.Engine` | as now |
| `mac/ComicRack.Plugins.Core` | `ComicRack.Plugins` | as now |
| `mac/cYo.Common.Core` | `cYo.Common` | as now |

Why this and not the others: a `Resolving` handler cannot map names (S4); the stand-ins have to be found by the binder for compiled plug-ins too (a `clr` wrapper or a custom platform adaptation layer would only serve `clr.AddReference` calls in Python; neither was needed or tried); one rename covers Python scripts, compiled plug-ins and the `System.Drawing` forwarders. No NuGet package of the Core may bring the real `System.Drawing.Common` into the output (it comes in transitively through IronPython; an explicit `PackageReference` with `ExcludeAssets="all"` in the Core projects removes it).

For P4.7 and P4.8, places that name the assemblies and must follow the rename: `mac/tools/api-inventory/ShimCoverage.cs` (`Newest(macDir, "cYo.Compat.Drawing")`, `"cYo.Compat.Forms"`) and `Program.cs` (`Assemblies`), `mac/tests/Compat.Forms.Tests/ReferencePack.cs` and `Compat.Drawing.Tests/ReferencePack.cs` (a `MetadataLoadContext` that now holds the reference `System.Windows.Forms` and the shim under one name: tell them apart by path), `mac/tests/ComicRack.Mac.Host.Tests/WindowsTripwireTests.cs` and `NativeLibraryResolver.CoreAssemblies()` (assembly names), `mac/ComicRack.Mac/ScriptSelfTest.cs` (the `RewriteRefs` and `Resolver` experiments of S4 become obsolete), and the file names that scripts and packaging copy (`make-app.sh`).

## Findings and rules for P4.7 to P4.13

1. **State in the facade, peers built from it at every show.** A closed modal form is shown again with new peers and its old values (M04). Property writes from any thread are stored at once and posted to the peer in order; 200 writes in a loop were fine, but a script that writes in a tight loop (a progress counter) would queue a job per write: coalesce (one pending update per control and property) in P4.9.
2. **Modal = `window.ShowDialog(owner)` (its task ignored) + `Dispatcher.UIThread.PushFrame`, exited by `frame.Continue = false` from the facade's close path.** An exit requested before the frame runs is remembered. `ShowDialog` from another thread: `Dispatcher.UIThread.Invoke` of the whole call. Closing an owner closes its open dialogs (Avalonia's owned-window rule); both `ShowDialog` calls return, innermost first (M05). `DialogResult` assigned or set by a button closes only a modal form; a form closed without a button returns `DefaultCloseResult` (Cancel; a native-like message box with only OK returns OK).
3. **A click whose handler opens a dialog runs the nested frame inside the input dispatch.** A test driver must not wait for such a click to return (`ClickAsync` in the spike), and a script that shows a `MessageBox` from a `Click` handler of a dialog works (P03).
4. **Every call from a peer into a script is guarded** (`FormsRuntime.Guard`: `Application.ThreadException`, else recorded). Avalonia aborts the process (exit 134) when an exception escapes a UI-thread job, so the host must also install `Dispatcher.UIThread.UnhandledException` with `Handled = true` as a net (P4.5 or the app start).
5. **Owner draw works as designed.** The peer paints the facade control into a `cYo.Compat.Drawing.Bitmap` at the render scaling, copies it (`SKBitmap.Copy`) into an `ICustomDrawOperation` and the Python override sees `e.Graphics` and `e.ClipRectangle`; 4 to 16 microseconds per call. The stand-in `Graphics` still throws `NotSupportedException` for text, ellipses, clips and `Save`/`Restore` (P4.10 needs them for owner-draw scripts).
6. **IronPython facts.** A new-style subclass of the facade `Control` is created without calling the base `__init__` (as `Sample.py` does); the override can call `Control.OnPaint(self, e)`; `System.Threading.Thread(ThreadStart(f))` works without the StdLib; Python exceptions in handlers arrive in `ThreadException` as `ValueErrorException`/`...Exception` objects; `print` goes to the engine's output stream (give it a stream, the host decides where it goes); Python `Decimal` to `int` property assignment (`book.Count = nud.Value`) works.
7. **Fidelity items seen in the snapshots (P4.9).** An Avalonia `NumericUpDown` at the original 65 pixel width hides its value behind the spinner buttons (`Autonumber.py`); an AutoSize `CheckBox` must add the glyph width to the measured text; labels wrap at the measured width; `Form.AutoSize` and `AutoScaleMode.Font` are ignored in the spike (client size and absolute positions are honoured); window `Width`/`Height` is the client size in Avalonia, so `ClientSize` maps 1:1.
8. **The owner window during a modal dialog (M10).** The headless platform does not block the owner (a click is handled); the real-window run (`real.sh interactive`, step A) decides whether Avalonia.Native does. If it does not, the facade disables the owner chain while its frame runs.
9. **Not covered (stays with the later tasks):** layout engines (Anchor, AutoSize, Flow/Table), ListView/TreeView/TabControl and the rest of tier A, file dialogs, HTML panels, `Application.Idle`, keyboard modifier mapping, Retina text metrics.

## Reproduce

```
mac/spikes/S6-modal/run.sh --no-real      # stage, headless cases (78 PASS), shutdown, autopilot scenario, unhandled-exception run, identity runs: about 30 seconds
mac/spikes/S6-modal/real.sh all           # real windows through ui-drive; exit 4 when the screen is locked
mac/spikes/S6-modal/identity.sh core      # the Core with renamed assemblies (S4 console, unmodified bundled scripts)
mac/spikes/S6-modal/stage.sh && mac/artifacts/s6/app/S6-modal --headless --cases M01,S01 --out /tmp/s6   # single cases (--list shows them)
```

Results go to `mac/artifacts/s6/` (git-ignored); the committed copies are in `mac/reports/s6-modal/`.
