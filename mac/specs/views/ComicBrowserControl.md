# ComicBrowserControl (ComicRack/Views/ComicBrowserControl.Designer.cs, ComicBrowserControl.cs)

Mac view: the browser toolbar and book area of `ComicRack.Mac.Views.MainWindow` (P5.R1 first version; the thumbnails are P5.R2).

## 1. Sources
`ComicBrowserControl.Designer.cs:976-1392` (`toolStrip` and its items), `ComicBrowserControl.cs:635-690` (`SelectionInfo`), `:745-748` (texts), `:1311-1363` (quick search), `:1528-1538` (quick search width), `cYo.Common.Windows/Forms/SearchTextBox.cs`.

## 2. Layout
```
toolStrip 25 high, left to right:
  tbSidebar | btBrowsePrev btBrowseNext | --- | tbbView(split) tbbGroup(split) tbbStack(split) tbbSort "Arrange"(split) | tsQuickSearch 130 x 24 | tsListLayouts | --- | tbbDuplicateList | --- | tbUndo tbRedo
openStackPanel 39 (hidden), searchBrowserContainer 160 (P5.15b), displayOptionPanel 39 (hidden)
browserContainer: itemView (the book list) with lvGroupHeaders
```
Mac in P5.R1: Sidebar button, Arrange split button (the MVP sort: Series, Title, Date added), quick search box; the book grid below.

## 3. Controls
| Control | TR key | English | Mac in P5.R1 | Task |
|---|---|---|---|---|
| `tbSidebar` | `tbSidebar` | Sidebar | `CrToolButton` `<Sidebar>`, checked while the panel shows | P5.R1 |
| `btBrowsePrev`, `btBrowseNext` | | | not yet | P5.32b |
| `tbbView` | `tbbView` | Views | not yet | P5.11a |
| `tbbGroup`, `tbbStack` | | | not yet | P5.13a, P5.13b |
| `tbbSort` | `tbbSort` | Arrange (button text = the sort column) | `CrSplitButton` `<SortUp>`, text = sort name; a click on the button or the arrow opens the menu of sorts (the original's click flips ascending and descending: not in the MVP) | P5.R1, P5.12 |
| `tsQuickSearch` | cue "Search" + `miSearchAll` | Search All | `CrSearchBox` 130 x 24 (260 while focused), `<Search>` image, `<SmallCloseGray>` clear button; filters title and series (MVP); the scope menu is P5.15a | P5.R1, P5.15a |
| `tsListLayouts`, `tbbDuplicateList`, `tbUndo`, `tbRedo` | | | not yet | P5.11b, P5.20, P5.7a |

## 4. Menus and shortcuts
Toolbar BT.* rows; Cmd+F (focus quick search, GK.01) is P5.15a.

## 5. Icons
`<Sidebar>`, `<SortUp>`, `<Search>`, `<SmallCloseGray>`, `<SmallArrowDown>` (split arrow).

## 6. Tokens and metrics
Toolbar 25 (Cr.ToolToolStripGradientBegin/Middle/End, Cr.ToolToolStripBorder); button 23 x 22, hover Cr.ToolButtonSelected*, pressed Cr.ToolButtonPressed*, checked Cr.ToolButtonChecked*; search box Cr.Window with a Cr.ControlDark frame; split arrow 14.

## 7. Behaviour
Status text = `SelectionInfo`: `[list name: ]N Book(s)[ (M filtered)][ / total size][ - path of the selected book | K selected][ / selected size]`, texts `ComicSingle`, `ComicMulti`, `ComicFiltered`, `ComicSelected`, sizes by `FileLengthFormat`. Typing in the quick search filters after a short delay; the clear button and Esc empty it.

## 8. Planned differences
Only the MVP's single selection exists, so "K selected" appears with one book as its path. The quick search keeps the MVP's title and series filter until the scopes of P5.15a. Sizes in the status text use the current culture's decimal separator, as the original's `FileLengthFormat` does.
