# Chrome architecture: one isolated, swappable layer (RV5, rule R10)

Owner requirement (2026-10-20): everything that makes the app look like ComicRack rather than stock Avalonia or macOS (the chrome) lives in **one layer** that can be changed or replaced **per element** without touching a view, a view model or any logic, so individual elements can later move to a native macOS look. This file is the map of that layer. The decisions about *how* each element looks are in `chrome-guidelines.md`; the policy for UI tasks is `full-port.md` section 2.11.

## 1. The rule (R10)

Views and view models never contain chrome. They use **semantic parts only**: a `Cr*` control, a `cr-*` style class, a token or an icon by name. A view never says how something looks (a colour, a font, a corner radius, an opacity, a border width, a template, an inline style, a picture path); it says what the thing *is*. The look is decided in the chrome layer, and per element it is a variant (`Original` or `Native`).

## 2. The layers

```
tokens ──► control themes and styles ──► Cr controls ──► views
(colours,     (templates, per-variant        (Controls/,      (Views/, ViewModels/,
 metrics,      themes, cr-* classes,          IChromeElement)   Browser/, Reader/ ...)
 fonts)        stock-control styles)
```

| Layer | Files | What it holds |
|---|---|---|
| Tokens | `Themes/ComicRackTheme.Light.axaml`, `.Dark.axaml`, `Themes/ThemeTokens*.cs`, `Themes/ProfessionalColors.cs`, `Themes/Controls/CrMetrics.axaml`, `Controls/ChromeMetrics.cs` (`ChromeMetrics`, `ChromeFonts`) | the `Cr.*` colours of the original (tested against the original tables), font sizes, metrics of chrome-guidelines sections 3 and 4 |
| Control themes and styles | `Themes/Controls/CrChromeThemes.axaml` (one `ControlTheme` per Cr control and variant, key `<ControlTypeName>.<Variant>`), `CrControlThemes.axaml` (type-keyed themes of stock controls: tree item, check box, radio button), `CrControls.axaml` (styles of stock controls: buttons, list boxes, menus, tabs, focus ring), `CrControlResources.Light/Dark.axaml` (the base theme's own brush keys set from tokens), `CrParts.axaml` (the semantic classes), `CrThumbnailLook.axaml` and `CrReaderLook.axaml` (the colours the original's renderers write as literals: `Cr.Thumb.*`, `Cr.Reader.*`, `Cr.Perf.*`) | templates, setters, per-variant looks |
| Cr controls | `Controls/*.cs`, `Platform/*.cs` (native pieces: `NSAlert`, window tabbing, scroller style) | `CrToolBar`, `CrToolButton`, `CrSplitButton`, `CrToolSeparator`, `CrSearchBox`, `CrTabBar`, `CrStatusBar`, `CrStatusSlot`, `CrTreeView`, `StyledSelectionVisual`, `CrTreeRow`, `CrGaugeStrip`, `CrHeader`, `CrCollapsibleGroup`, `CrSizableContainer`, `CrIcon`, `CrDialogWindow`, `AlertWindow`, `CrThumbnailItem`, `CoverView`, `CrBookToolTip`, `CrTextOverlay`, `CrPartOverlay`, `CrNavigationOverlay`; custom drawing through `ChromePaint` |
| Views | `Views/`, `ViewModels/`, `Browser/`, `Reader/`, `Services/`, `Diagnostics/`, top level files | structure, bindings, behaviour; no chrome |

The chrome layer is `Themes/`, `Controls/` and `Platform/`. Nothing else in the app project may contain chrome (the tests of section 6 enforce it). `Views/ChromeGalleryWindow` is the one view that is chrome on purpose: it is the test card that shows every control in every state.

## 3. What a view may use (the public surface)

- `Cr*` controls from `ComicRack.Mac.Controls`, with their documented properties. A control that takes its content from a model names the members (`CrTreeView.IconMember`, `TextMember`, `CountMember`, `CountVisibleMember`, `ToolTipMember`, `ChildrenMember`, `ExpandedMember`): the view says which member carries what, the control decides how a row looks.
- Style classes of `CrParts.axaml`, named for the role: `cr-window`, `cr-panel`, `cr-card`, `cr-display`, `cr-title`, `cr-heading`, `cr-bold`, `cr-small`, `cr-secondary`, `cr-hint`, `cr-error`, `cr-grid`, `cr-grid-line` and `cr-grid-cell` (the Custom tab's grid, P5.7b), `cr-caption` (the captions of the Colors tab's sliders, P5.7c), and the MVP surfaces `cr-item-view` (the paper of the browser, P5.R2), `cr-overlay*` (the performance overlay). A new role gets a new class in `CrParts.axaml`, never an attribute in a view.
- Tokens: `{DynamicResource Cr.<Token>Brush}` and `ThemeTokens.Get(...)` in code (a token is a colour of the original, not a look decision of a variant).
- Pictures: `CrIcon IconName="<resx name>"` or `Icons.Get(name)`. `Icons` is the only way to load an image of the original.
- Custom-drawn views take brushes by key: `ChromePaint.ResourceBrush(control, "Cr.Reader...")` or `ChromePaint.Brush(control, token)`.
- Layout (`Margin`, `Padding`, `Width`, `Height`, `Spacing`, panels, `Grid.Row`) stays in views. Standard controls (`Button`, `TextBox`, `ComboBox`, `ListBox`, `Slider`, ...) are used as they are; their look is the stock-control styles of the chrome layer.

## 4. The swap mechanism

**Elements and variants.** `Themes/ChromeVariants.cs` defines `ChromeElement` (Toolbar, TabBar, StatusBar, Tree, ListHeader, GroupBox, SidePanel, Icons, Dialogs, Buttons, ComboBox, Menus, ItemView, ReaderOverlay) and `ChromeVariant` (`Original`, `Native`). `ChromeVariants.Defaults` is the one table with the shipped look of every element (all `Original`: the default look is the WinForms look). `ChromeVariants.NativeAvailable` lists the elements that have a Native variant (since RV5b: all of them).

**Where it is set.** The table in `ChromeVariants.cs` is the default. `--chrome SPEC` (or the environment variable `COMICRACK_MAC_CHROME`) overrides it per run: `--chrome StatusBar=Native`, `--chrome "StatusBar=Native,Toolbar=Original"`, `--chrome native` (every element that has a Native variant) or `--chrome original`. An invalid specification is a usage error (exit 2) that lists the valid names. A preference file is not needed yet; `ChromeVariants.Configure(string)` is the one call a settings dialog would make.

**How it resolves.** Every chrome control implements `IChromeElement` (`Element`, optional `OnChromeVariantChanged`) and calls `ChromeVariants.Bind(this)` in its constructor. `Bind` gives the control the `ControlTheme` that the application resources hold under `<ControlTypeName>.<Variant>` (the nearest base type that has one) and keeps the control (weakly) so that `ChromeVariants.Set(element, variant)` can move it later. A flip therefore changes the `Theme` of the controls of that element and nothing else: the view tree, the view model, the control instances and every other element stay as they are (`ChromeVariantTests` proves it by comparing the visual tree, the controls, the themes of another element and the pixels).

**What a variant is.** A `ControlTheme` per control type: `Original` is the WinForms look (for a control that draws itself in `Render`, the theme is empty and the look is C# from the tokens); `Native` is built on the `Original` theme (`BasedOn`) and overrides what differs, preferably with the stock theme's own brushes (`SystemControl*`) and macOS conventions. A control whose `Render` or `ApplyTokens` sets values by hand branches on `OnChromeVariantChanged` and clears those values for Native so that the theme's setters win (`CrStatusBar.IsNative`). A control that is not yet backed by a Native theme resolves the `Original` one; `ChromeVariants.Set(element, Native)` for an element without a Native variant keeps `Original`, logs it and returns false.

**Native variants (RV5b).** Self-drawn controls: `ChromePaint.Brush` (and the gradients) answer with the stock `SystemControl*` brush of the palette when the control's element is Native, so a control needs no code for colour; shapes (rounded hot buttons and tabs, header, selection, overlay panel, dialog button order, grey icons) branch on `ChromePaint.IsNative(this)` or `ChromeVariants.IsNative(element)`. Stock controls: the pack of the variant (section 5). Every Cr control has a `<Control>.Native` theme (`ChromeArchitectureTests`).

**The status bar** is the first Native element and keeps its theme-based variant. `CrStatusBar.Native` is a flat bar in the stock theme's chrome colour (`SystemControlBackgroundChromeMediumLowBrush`) with a one unit top line (`SystemControlForegroundBaseLowBrush`) instead of the toolbar-colour gradient and the `Cr.ToolStatusStripBorder` line; `CrStatusSlot.Native` has no sunken frame. Try it: `ComicRack.Mac --chrome StatusBar=Native`.

## 5. Elements and what is behind them

| Element | Controls and styles | Native variant |
|---|---|---|
| Toolbar | `CrToolBar` (items planned by `ToolBarLayout`: `CrToolBar.Alignment`, `CrToolBar.Overflow`, the overflow button and its drop-down, RV8), `CrToolButton`, `CrSplitButton`, `CrDropDownButton`, `CrToolSeparator`, `CrSearchBox` (self-drawn from `Cr.Tool*`) | **yes** (RV5b) |
| TabBar | `CrTabBar` (self-drawn: tabs with their own minimum width, the New Tab button as a tab that is not selectable, scroll arrows and the drop-down button, the right click menu hook) | **yes** (RV5b) |
| StatusBar | `CrStatusBar`, `CrStatusSlot` | **yes** (proof) |
| Tree | `CrTreeView`, `CrTreeRow`, `CrGaugeStrip`, `StyledSelectionVisual`, tree item theme (`CrControlThemes.axaml`) | **yes** (RV5b) |
| ListHeader | `CrHeader` | **yes** (RV5b) |
| GroupBox | `CrCollapsibleGroup` | **yes** (RV5b) |
| SidePanel | `CrSizableContainer` (grip, slide, a click on the grip toggles it, `Fill` takes the whole room: the side panel and the browser dock of the main window) | **yes** (RV5b) |
| Icons | `CrIcon`, `Icons` | **yes** (RV5b) |
| Dialogs | `CrDialogWindow`, `AlertWindow` (the system alert is native already: `MacAlert`) | **yes** (RV5b) |
| ItemView | `CrThumbnailItem` (shadow, frame, badges, caption, selection; its backdrop and overlay are inner parts), `CrBookTile` (the cover with the book's text, the 3D book and the icons of the Info dialog, P5.7a; geometry `BookTileLayout`, shadow `ThumbnailShadow`), `CrPageList` (the list of pages of the Info dialog and later of the pages view: the detail view with its header, the thumbnail and tile views, selection, scrolling; rows through `IPageListSource`; header drawn by `CrHeader.Draw`, tile text by `TileTextPainter`, page number badge and bookmark flag by `ThumbnailAdornments`, P5.7c), `CrPageViewer` (the page of the Colors tab, fitted to the width, P5.7c), `CoverView` (the picture, fade-in), `CrBookToolTip`, `CrSelectionRectangle` (the rubber band, RV6); the colours of the original's renderers are `Cr.Thumb.*` in `Themes/Controls/CrThumbnailLook.axaml`, the numbers are `ThumbnailMetrics` and `ThumbnailLayout` | **yes** (RV5b) |
| ReaderOverlay | `CrTextOverlay` (page info, loading, message), `CrPartOverlay` (visible part), `CrNavigationOverlay` (glass buttons, slider, page strip), `CrReaderPage` (the reading surface: paper, shadow, outlines and the pages through `ReaderDrawOperation`, fed by `IReaderPageSource`; `Views/ReaderView` is this control plus the pointer, wheel, gesture and cursor behaviour, RV8), `GlassPainter` (nine-part glass pictures), `PageFrame` (outline and shadow of the pages), `ReaderOverlayClock` (fades and slide); the colours are `Cr.Reader.*` in `Themes/Controls/CrReaderLook.axaml` | **yes** (RV5b) |
| Buttons, ComboBox, Menus | `CrRatingStars` (the rating editor of the rating menu and the Quick Rating dialog, P5.5, self-drawn from the tokens and `StarYellow`), `CrSpinButton` (the up and down buttons of the number boxes of the Info dialog, P5.7a, self-drawn), `CrValueComboBox` (the editors' combo boxes with pictures and separator lines, P5.7a) and `CrCheckList` (a list with check boxes, P5.7a; its items are the `cr-check-list` style of `CrControls.axaml`) and the **stock controls**: `CrControls.axaml`, `CrControlThemes.axaml` (check box, radio button), `CrControlResources.*` (the base theme's brush keys), `CrMetrics.axaml` | **yes** (RV5b): packs (section 4) |

Buttons, ComboBox, Menus and the tree item are **packs** (`Themes/StockPacks.cs`, files `Themes/Controls/Stock/Cr<Element>.<Variant>.axaml`): the styles, control themes and brush resources of one variant; `ChromeVariants` keeps the pack of the current variant in `Application.Styles`. The self-drawn controls take their Native colours from `Themes/ChromeNativePalette.cs` through `ChromePaint` and branch on `ChromePaint.IsNative` for shapes.

## 6. Enforcement (tests in `mac/tests/ComicRack.Mac.Tests/Theme/`)

- `ChromeArchitectureTests` (scanner: `ChromeLint.cs`): the view layer (every `.cs` and `.axaml` outside `Themes/`, `Controls/`, `Platform/`, `Assets/`, `App.axaml*`, `Program.cs`) contains no font attribute or `FontFamily`/`FontWeight`, no `CornerRadius`/`BoxShadow`/`Opacity`/`BorderThickness` assignment, no inline `<Style>`, `<ControlTemplate>`, `<DataTemplate>`, `<TreeDataTemplate>`, `<ControlTheme>`, `<X.Styles>` or `new Style/ControlTemplate/Setter/FuncTreeDataTemplate`, no `AssetLoader`, `avares://`, `new Bitmap` or image file name; files of `Views/` do not use `Avalonia.Styling`, `Avalonia.Markup.*`, `Avalonia.Skia`, `Avalonia.Media.Imaging`, `Avalonia.Rendering.*`, `Avalonia.Platform`, `SkiaSharp` or `ComicRack.Mac.Platform`; files of `ViewModels/` use no `Avalonia.Controls`, `Avalonia.Media`, styling, rendering or chrome namespace. `ChromeLint.Allowed` (one entry: the grid's `ItemsRepeater` element factory) and `ChromeLint.Showcases` (the gallery) are the only exceptions, each with a reason, and a test fails when an exception is no longer needed.
- `NoLiteralColorsTests`: no colour literal in any source or `.axaml` of the app outside `Themes/` and `Platform/` (every folder since RV8: `Thumbnails/`, `Reader/`, `Browser/`, `Services/` ...; no exceptions left since RV5). Outside `Controls/` the Skia and short forms count too (`new SKColor`, `SKColors.X`, `Color.FromUInt32`, `"#rgb"`): code that needs a colour takes it from a token or from a look object of the chrome layer (`ThumbnailPlaceholderLook`). Views may not name `Avalonia.Media` at all (`ChromeLint.ForbiddenInViews`).
- `ChromeArchitectureTests`: every public control of `ComicRack.Mac.Controls` implements `IChromeElement`, resolves a `ControlTheme` keyed `<Type>.Original` that it wears, and a `<Type>.Native` for every element in `NativeAvailable`; `CrChromeThemes.axaml` defines only keys of that form for existing controls; only `Themes/Icons.cs` touches the asset store.
- `ChromeVariantTests`: parsing and defaults, the flip (same tree, same controls, other elements untouched, pixels of the Native bar equal the stock brush, flip back restores the frame pixel for pixel, light and dark), controls made after a flip, no leak through the registry.
- `ChromeSnapshotTests`: 14 frames (library window, grid with a selected cell, gallery, About, Import, reader page, reader notice; light and dark) compared with a baseline taken before a change (`CRMAC_CHROME_BASELINE_DIR`, save with `CRMAC_CHROME_SNAPSHOT_DIR`), and the values of the surfaces that moved into `CrMvpLook.axaml (deleted by P5.R3: the values are tokens and `Cr.Reader.*`/`Cr.Perf.*` now)`.

## 7. Audit of RV5 (what leaked and where it went)

| Where | Leak | Now |
|---|---|---|
| `MainWindow.axaml` | an inline `<Style>` binding `IsExpanded`, a `TreeDataTemplate` with the row layout, font size 11 and surface tokens on the window, font 28 semibold, token foregrounds, a card with its own border | `CrTreeView` (member names only), classes `cr-window`, `cr-panel`, `cr-card`, `cr-display`, `cr-hint`, `cr-bold` |
| `AboutWindow.axaml`, `ImportWindow.axaml` | font sizes and weights, opacities, a token foreground | `cr-title`, `cr-heading`, `cr-secondary`, `cr-small`, `cr-error` |
| `ReaderWindow.axaml` | seven colour literals, font sizes, a corner radius, the dark theme variant | classes `cr-reader`, `cr-reader-status*`, `cr-reader-notice*` (values in `CrMvpLook.axaml (deleted by P5.R3: the values are tokens and `Cr.Reader.*`/`Cr.Perf.*` now)`, replaced by tokens in P5.R3) |
| `BookCell.cs`, `CoverView.cs`, `ReaderView.cs`, `PerfOverlay.cs` | colour literals, fonts, opacity, padding, frame, radius, a monospace family | `BookCell` derives from `CrThumbnailItem` and `CoverView` moved to `Controls/` (P5.R2, the `cr-book-*` classes and `Cr.Mvp.Grid*`/`Cover*` entries are gone); `cr-overlay*` and `ChromePaint.ResourceBrush` for the reader views (values in `CrMvpLook.axaml (deleted by P5.R3: the values are tokens and `Cr.Reader.*`/`Cr.Perf.*` now)`, replaced in P5.R3) |
| `Views/AlertWindow.cs` | a chrome dialog living in the view layer | moved to `Controls/` (namespace `ComicRack.Mac.Controls`) |
| the Cr controls | no common contract, no element, no variant | `IChromeElement`, `ChromeVariants`, a theme per control and variant |

| RV8 | `Views/ReaderView.cs` painted the page (paper, shadow, outlines, pages) | `Controls/CrReaderPage`; the view derives from it and keeps the behaviour |
| RV8 | `Thumbnails/ThumbnailPixels.cs` had the grey of the placeholder tile as literals | `Controls/ThumbnailPlaceholderLook` and the `Cr.Thumb.Placeholder*Brush` entries, per appearance |
| RV8 | `Views/PerfOverlay.cs` named `Avalonia.Media` for nothing | unused using removed |

## 8. Adding to the layer

- **A new chrome control**: put it in `Controls/`, implement `IChromeElement` (pick its element), call `ChromeVariants.Bind(this)` in the constructor, add `<Control>.Original` to `CrChromeThemes.axaml`. `ChromeArchitectureTests` fails until all three exist.
- **A new semantic role** (a kind of text, a surface): a `cr-*` class in `CrParts.axaml`, used by name in the views.
- **A Native variant of an element**: `chrome-guidelines.md` section 10.
- **A restyle task** (P5.R2, P5.R3, P5.x): builds its looks in the chrome layer and gives the views classes and Cr controls; it replaces literals with tokens or `Cr.<Surface>.*` look entries.
