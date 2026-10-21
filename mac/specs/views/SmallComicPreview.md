# SmallComicPreview (ComicRack/Views/SmallComicPreview.cs, .Designer.cs) and its host previewPane (ComicRack/Views/ComicExplorerView.cs, .Designer.cs)

Mac view: `Views/SmallPreviewPane` (toolbar `CrToolBar`, page viewer `ReaderView` with `Look="SmallPreview"`), view model `ViewModels/SmallPreviewViewModel`, hosted by a `CrSizableContainer` `PreviewPanel` (flat border) at the bottom of the side panel in `Views/MainWindow.axaml`. P5.6a. Ledger MB.06, SP.01. Decisions C-SPLIT-1, C-PREV-1.

## 1. Sources
`SmallComicPreview.Designer.cs:30-200` (layout, buttons, images), `SmallComicPreview.cs:20-140` (keyboard map, commands, `ShowPreview`, `TwoPageDisplay`), `ComicExplorerView.Designer.cs:35-146` (`previewPane`, `previewTimer` 500 ms), `ComicExplorerView.cs:277-340` (`UpdatePreview`, the timer, `smallComicPreview_CloseClicked`), `ComicExplorerViewSettings.cs` (`ShowPreview`, `PreviewSplit`, `TwoPagePreview`), `MainForm.cs:1476`, `:2378`, `:3322` (Browse > Small Preview), `SizableContainer.cs` and `BorderUtility.cs` (flat border), `OpenComicOptions.DisableAll`, `NavigatorManager.OpenComic`, `ThemeColors.SmallComicPreview`.

## 2. Layout (96 DPI units)
```
side panel (252 wide, grip right)
  treePanel (Fill)
  previewPane   Dock Bottom, 207 high (PreviewSplit), grip 6 on top (kept visible while closed), BorderStyle Flat
    border: one unit frame, one unit of panel colour inside it, so the child is 2 units in: 242 x 197 in a 246 x 207 pane
    SmallComicPreview
      toolStripPreview  25 high, no grip: Open | First Previous Next Last | Two Pages | Refresh ........ Close (Alignment Right, Overflow Never)
      pageViewer        Fill, paper SmallPreviewPageViewerBack, text SmallPreviewPageViewerText, Arial 15.75 pt italic, centred
```
Closed (the default) the pane is its 6 unit grip. The pane lives in the side panel, so it is not visible while the side panel is closed.

## 3. Controls
| Control | TR key (`SmallComicPreview`) | English | Image | Mac |
|---|---|---|---|---|
| `tsbOpen` | `tsbOpen` | Open | `Open` | `CrToolButton` `PreviewOpenButton` |
| `tsbFirst` | `tsbFirst`, `tsbFirst.Tooltip` | First, Go to first page | `GoFirst` | `PreviewFirstButton` |
| `tsbPrev` | `tsbPrev`, `.Tooltip` | Previous, Go to previous page | `GoPrevious` | `PreviewPreviousButton` |
| `tsbNext` | `tsbNext`, `.Tooltip` | Next, Go to next page | `GoNext` | `PreviewNextButton` |
| `tsbLast` | `tsbLast`, `.Tooltip` | Last, Go to last page | `GoLast` | `PreviewLastButton` |
| `tsbTwoPages` | `tsbTwoPages`, `.Tooltip` | Two Pages, Show one or two pages | `TwoPage` | `PreviewTwoPagesButton` (checked while on) |
| `tsbRefresh` | `tsbRefresh` | Refresh | `Refresh` | `PreviewRefreshButton` |
| `tbClose` | `tbClose` | Close | `SmallClose` | `PreviewCloseButton` |
| `pageViewer` | `pageViewer`, `PreviewOnlyForComics` | Nothing Selected, Preview is only available for Books | | `ReaderView.PlaceholderText` |
The button images show only the image; the tool tip is the tool tip key when there is one, else the text.

## 5. Icons
`<Open>`, `<GoFirst>`, `<GoPrevious>`, `<GoNext>`, `<GoLast>`, `<TwoPage>`, `<Refresh>`, `<SmallClose>`, `<MozillaGrip>`.

## 6. Tokens and metrics
`Cr.SmallPreviewPageViewerBack` (white, dark #191919), `Cr.SmallPreviewPageViewerText` (light gray #D3D3D3, dark #303030), `Cr.Control`, `Cr.ControlDark` (the flat border in the light theme), `Cr.BorderDefault` (dark), `Cr.Tool*` (toolbar). Toolbar 25, grip 6, border inset 2, preview 207.

## 7. Behaviour
- Browse > Small Preview (Shift+F7, `miSmallPreview`) is a check item; Close does the same as unchecking it. `ShowPreview` (off), `PreviewSplit` (207), `TwoPagePreview` (off) are kept in `Settings.xml`.
- The first selected book of the browser is shown 500 ms after the selection stopped changing (`previewTimer`), also when the pane or the side panel opens (no wait). The book is a copy (`new ComicBook(first)`) opened as `OpenComicOptions.DisableAll`: from the first page, nothing written back (no opened count, opened time, current page or last page read). The same file stays open. No book: `Nothing Selected`; a book without a file: `Preview is only available for Books`.
- Previous and Next move by a page, by a spread in the two page layout; First and Last; Two Pages switches between a single page and the adaptive double page; Refresh shows the page again; Open puts the book in a new tab; Up, Down and the wheel scroll the page (a sideways swipe or Cmd+wheel do nothing); a double click opens the book; no context menu.
- Button states: Open, Two Pages and Refresh need a book; First and Previous need an earlier page; Next and Last a later one; Close always.

## 8. Planned differences
- The flat border is drawn as a one unit `ControlDark` frame with a unit of the panel colour inside it; the original's GDI `Border3DStyle.Flat` colours are not verified on Windows (OA-5 reference screenshots).
- The message uses the system font (italic, 21 units) instead of Arial (C-FONT-1).
- `ScrollingDoesBrowse` is off in the preview, as in the original, so the wheel never turns the page.
