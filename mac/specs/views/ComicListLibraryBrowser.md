# ComicListLibraryBrowser (ComicRack/Views/ComicListLibraryBrowser.Designer.cs, ComicRack/Controls/LibraryTreeSkin.cs)

Mac view: the library tree in the side panel of `ComicRack.Mac.Views.MainWindow` (P5.R1 first version: look, icons, counters; editing and the tree toolbar are P5.16a to P5.16c).

## 1. Sources
`ComicListLibraryBrowser.Designer.cs` (`toolStrip`, `tvQueries`, `favContainer`, `quickSearchPanel`, `treeImages`), `ComicListLibraryBrowser.cs:313-317` (image list), `:398` (`ImageKey`), `ComicRack/Controls/LibraryTreeSkin.cs:20-140` (counters), `cYo.Common.Windows/Forms/TreeViewSkinner.cs`, `NiceTreeSkin.cs`.

## 2. Layout
```
toolStrip 25 high: tbNewFolder tbNewList tbNewSmartList | tbOpenWindow tbOpenTab | tbExpandCollapseAll tbRefresh tbFavorites tsQuickSearch   (P5.16a, P5.18)
favContainer 160 (favorites, hidden by default, P5.18)
quickSearchPanel 28 (hidden)
tvQueries (fill): rows font + 8 high, 16 px image, label, counters right-aligned
```
Mac in P5.R1: the tree only (no toolbar yet), window colour, no frame.

## 3. Controls
| Control | Mac in P5.R1 | Task |
|---|---|---|
| `tvQueries` | `TreeView` with the P5.T4 tree item theme (row = font 12 + 8, bold selected label, chevron images, `StyledSelection`) | P5.R1 |
| `treeImages` | `CrIcon` by the list's `ImageKey`: Library = `<Library>`, Folder = `<SearchFolder>`, Search = `<SearchDocument>`, List = `<List>`, TempFolder = `<TempFolder>` | P5.R1 |
| counters | `CrGaugePill`: total in `GaugeTotal` (green, white text, 0.75 x font, alpha 192 to 128 gradient), right aligned; none when the list has no books. Unread (orange) and new (red) counters need the read state (P5.2) | P5.R1, P5.2 |
| `toolStrip`, `favContainer`, `quickSearchPanel` | not yet | P5.16a, P5.18 |

## 4. Menus and shortcuts
Tree menu (LT.03 to LT.15) and GK.03 are later tasks; selecting a row shows its books.

## 5. Icons
`<Library>`, `<SearchFolder>`, `<SearchDocument>`, `<List>`, `<TempFolder>`; arrows `<SmallArrowRight>`, `<SmallArrowDown>` of the tree theme.

## 6. Tokens and metrics
Cr.Window, Cr.WindowText, Cr.SelectionFocused / Cr.SelectionUnfocused (frame alpha 164, fill 41), Cr.GaugeTotal, Cr.GaugeTotalText; item font 12, counter font 9.

## 7. Behaviour
A selected row shows its list in the browser; counts are computed in the background and appear as they arrive. A list that needs scripts has no counter and a tool tip ("Requires scripts (not in MVP)"); its grid shows the notice.

## 8. Planned differences
The MVP's second line badge for script lists became a tool tip, because the original rows are single line. The device-sync marker on a row (`LibraryTreeSkin.DrawNodeIcon`) is deferred with device sync (D4).
