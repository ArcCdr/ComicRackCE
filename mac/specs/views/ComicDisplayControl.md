# ComicDisplayControl, the reader (ComicRack.Engine.Display.Forms/ComicDisplayControl.cs, ComicRack/MainForm.Designer.cs, ComicRack/MainForm.cs)

Mac view: `ComicRack.Mac.Views.ReaderWindow` (tab strip, toolbar, status bar, overlay layer) on `ReaderView` (the page), `CrTabBar`, `CrToolBar`, `CrStatusBar`, `CrTextOverlay`, `CrPartOverlay`, `CrNavigationOverlay` and `PageFrame` (P5.R3). Decisions C-READ-1, C-TB-1, C-TAB-1, C-WIN-2 (original look). Until P5.32a docks the reader the window shows the main form's tab strip with one tab; the original's undocked `ReaderForm` has only the display control.

## 1. Sources
`ComicDisplayControl.cs:196-240` (overlay sizes), `:1078-1170` (the overlays and their animators), `:1262-1345` (`DrawPageOrnaments`: outline and shadow), `:2594-3170` (overlay updates, `UpdateNavigationOverlay`), `MainForm.cs:4366-4376` (`CreateComicDisplay`: font Segoe UI 12 pt, padding 16), `MainForm.Designer.cs:2577-2615` (`fileTabs`, `mainToolStrip`), `:161-250` (Read, Display, page menus), `MainForm.cs:1563-1800` (reader keys), `Settings.cs` (overlay options), `EngineConfiguration.cs` (`PageShadowWidthPercentage` 1, `PageShadowOpacity` 0.6, `NavigationPanelWidth` 0.9), `PanelRenderer.cs`, `TextOverlay.cs`.

## 2. Layout (96 DPI units)
```
window
  tab strip 31 high (CrTabBar): one tab (book caption, close box), the reader toolbar in the slot at the right (25 high, transparent)
  reading area (ReaderView): paper = Cr.ReaderBackground (WhiteSmoke; dark #303030); the pages are fitted in the whole area
    overlays align inside a 16 unit padding:
      page info   150 x 25 at least, top right        fade 100 / 2000 / 100 ms at every page change (ShowCurrentPageOverlay)
      visible part 200 x 200 box + 16, bottom right    fade as above, only while the page is larger than the window (ShowVisiblePagePartOverlay), close button 16 x 16
      loading     200 x 30 at least, middle            "Loading Page N..." while a page is not there (ShowStatusOverlay)
      message     300 x 30 at least, middle            opening the book, why a book or page cannot be shown
    navigation overlay 0.9 of the width (at least 200) x 200, centred, 3 above the bottom edge (or at the top, NavigationOverlayOnTop)
  status bar 24 high: tsBook (caption, 60 characters), tsCurrentPage, tsPageCount
```
Tab strip and status bar are hidden in full screen.

## 3. Page ornaments (`DrawRealisticPages`, default true)
Each page has a one unit black outline (`Cr.Reader.PageFrameBrush`, on the left and top edge and one unit outside at the right and bottom); around all pages a shadow of 1 percent of the smaller side (at most 255) fading from 0.6 black (`Cr.Reader.PageShadowBrush`): linear along the sides, radial at the corners. Page bows and the toggle (Shift+D) come with P5.23f.

## 4. Overlays
- Glass: `BlackGlassPanel` cut into nine parts (margins 5, 5, 10, 10 of the source rectangle 5, 5, 215, 125), text white (`Cr.Reader.OverlayTextBrush`) in the system font at 16 units (Segoe UI 12 pt), size = text + 15 each way, at least the minimum above, at most 400 x 400.
- Page info text: `ComicBook.FormatNumber`: "4 (of 12)", "4/5 (of 12)" for a spread.
- Visible part: the whole content in miniature at 0.3 over the glass, the part in the window at 1.0.
- Navigation overlay: see `NavigationOverlay.md`.

## 5. Menus and shortcuts (ledger rows)
Read: First Page Cmd+B, Previous Page Cmd+P, Next Page Cmd+N, Last Page Cmd+E (MR.01 to MR.04). Display > Page Layout: Fit All Cmd+2, Fit Width Cmd+3, Fit Height Cmd+5, Single Page Cmd+7, Two Pages Cmd+8, Two Pages (adaptive) Cmd+9, Right to Left Cmd+0 (MD.03, MD.04, MD.06, MD.08 to MD.10, MD.12); Display > Zoom: Zoom In Cmd+=, Zoom Out Cmd+-, 100% to 400% (MD.14, MD.15, MD.17); Full Screen F11, alias Ctrl+Cmd+F (MD.25). Toolbar: Previous Page and Next Page split buttons, Page Layout, Fit and Zoom split buttons, Full Screen, Tools (About, Exit) (TB.01 to TB.06, TB.08, TB.09). Page menu (right click): Info..., My Rating, Page Layout (PC.01, PC.02, PC.06).

Keys (RK rows): PageDown / PageUp and Option+Right / Option+Left turn the page; Cmd+Home / Cmd+End first and last page; Space and Shift+Space part by part; Home / End start and end of the page; arrows scroll; F, double click, F11 full screen; T cycles the layout; 7, 8, 9, 0 layout and direction, 2, 3, 5 fit; Q quits; Cmd+wheel and pinch zoom; Esc leaves full screen, then closes.

## 6. Icons
`<GoPrevious>`, `<GoNext>`, `<GoFirst>`, `<GoLast>`, `<SinglePage>`, `<TwoPageForced>`, `<TwoPage>` and their `Rtl` forms, `<FitAll>`, `<FitWidth>`, `<FitHeight>`, `<ZoomIn>`, `<ZoomOut>`, `<FullScreen>`, `<Tools>`, `<About>`, `<GetInfo>`, `<Close>`; glass: `<BlackGlassPanel>`, `<GrayGlassButton>`, `<BlackGlassScrollbarBack>`, `<GradientFramedBackground>`.

## 7. Tokens and metrics
`Cr.ReaderBackground`, `Cr.Tool*` (toolbar), `Cr.TabBar*`, `Cr.ToolStatusStrip*`; literals of the original's renderers in `CrReaderLook.axaml` (`Cr.Reader.*`). Padding 16, shadow 1 %, opacity 0.6.

## 8. Planned differences
- Not yet in the reader: Original Size, Fit Width (adaptive), Fit Best, Only fit if oversized, rotation, zoom custom (P5.23e); magnifier, auto scroll (P5.23d); continuous layout, page bows, Minimal UI (P5.23f); bookmarks and book list commands (P5.23a); Book Display Settings (P5.23c). Their menu items appear with their tasks (C-MENU-4).
- Left and Right scroll; `ScrollingDoesBrowse` (turning the page at the edge) is P5.23d.
- Shift+PageUp / Shift+PageDown turn by a spread in the two-page layouts (the pairing is fixed for the book).
- The message overlay has no book picture (`ComicBox3D`); the battery indicator of the navigation overlay is not drawn.
- The two-finger tap (RK.21) is not bound: the command exists.
- The cursor hides after 5 s in full screen (HideCursorFullScreen, the original's `AutoHideCursorDelay`).
