# ComicBrowserControl (ComicRack/Views/ComicBrowserControl.Designer.cs, ComicBrowserControl.cs)

Mac view: the browser toolbar and book area of `ComicRack.Mac.Views.MainWindow` (P5.R1 first version; the thumbnails are P5.R2).

## 1. Sources
`ComicBrowserControl.Designer.cs:976-1392` (`toolStrip` and its items), `ComicBrowserControl.cs:635-690` (`SelectionInfo`), `:745-748` (texts), `:1311-1363` (quick search), `:1528-1538` (quick search width), `cYo.Common.Windows/Forms/SearchTextBox.cs`.

## 2. Layout
```
toolStrip 25 high, left to right:
  tbSidebar | btBrowsePrev btBrowseNext | --- | tbbView(split) tbbGroup(split) tbbStack(split) tbbSort "Arrange"(split) | tsListLayouts | --- | tbbDuplicateList | --- | tbUndo tbRedo | ... | tsQuickSearch 130 x 24 (Alignment Right, Overflow Never, margin 0,1,1,0) | overflow button (16 wide, only when the strip is too narrow; LayoutStyle HorizontalStackWithOverflow)
openStackPanel 39 (hidden), searchBrowserContainer 160 (P5.15b), displayOptionPanel 39 (hidden)
browserContainer: itemView (the book list) with lvGroupHeaders
```
Mac since P5.4: the whole strip (Sidebar and Arrange work, quick search filters; the other buttons are present and disabled until their task); the book grid below. Corrected by RV8: the quick search is at the right end and the strip overflows (`CrToolBar` with `CrToolBar.Alignment` and `CrToolBar.Overflow`, `ToolBarLayout`); the Arrange button flips the order on a click (`tbbSort_ButtonClick`) and its arrow opens the menu.

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
Toolbar BT.* rows; Cmd+F (focus quick search, GK.01) works since P5.4; the scope menu is P5.15a.

## 5. Icons
`<Sidebar>`, `<SortUp>`, `<Search>`, `<SmallCloseGray>`, `<SmallArrowDown>` (split arrow).

## 6. Tokens and metrics
Toolbar 25 (Cr.ToolToolStripGradientBegin/Middle/End, Cr.ToolToolStripBorder); button 23 x 22, hover Cr.ToolButtonSelected*, pressed Cr.ToolButtonPressed*, checked Cr.ToolButtonChecked*; search box Cr.Window with a Cr.ControlDark frame; split arrow 14.

## 7. Behaviour
Status text = `SelectionInfo`: `[list name: ]N Book(s)[ (M filtered)][ / total size][ - path of the selected book | K selected][ / selected size]`, texts `ComicSingle`, `ComicMulti`, `ComicFiltered`, `ComicSelected`, sizes by `FileLengthFormat`. Typing in the quick search filters after a short delay; the clear button and Esc empty it.

## 8. Planned differences
Only the MVP's single selection exists, so "K selected" appears with one book as its path. The quick search keeps the MVP's title and series filter until the scopes of P5.15a. Sizes in the status text use the current culture's decimal separator, as the original's `FileLengthFormat` does.


## 9. The book context menu (`contextMenuItems`, P5.5)
Mac: `Views/BrowserContextMenu` (a `MenuFlyout` on the grid, popup style of C-MENU-3). All items of the original in its order (the Designer's `AddRange` list is a test): Open (Cmd+O), Open in new Tab (Cmd+Shift+O), Open With, Info... (Cmd+I), Web... (Cmd+R), Edit (F2), line, My Rating (None, line, 1 to 5 stars, line, the five stars editor, line, Quick Rating and Review...; Option+Shift+0 to 5 and Q), Mark as (Unread, Read, line, Checked, Unchecked; Option+Shift+U, R, M, V), Edit List (Move to Top Option+Cmd+T, Move to Bottom Option+Cmd+B, Apply current Order), Add to List, line, Add to Library, Browse Books, Show in List, Export Books (Export Books... Cmd+Shift+E, Export with Previous Option+Cmd+E), Automation, Update Book Files, Reveal in Finder (Cmd+G), line, Copy Data (Cmd+C), Paste Data... (Cmd+V), Clear Data, line, Select All (Cmd+A), Invert Selection, Refresh, line, the stack items, Set as List Background, line, Remove... (Delete, Cmd+Backspace). Texts by TR key = item name; pictures Open, GetInfo, WebBlog, UpdateSmall, RevealExplorer, EditCopy, EditPaste, RefreshThumbnail, EditDelete, Save.
Done in P5.5: Open, Open in new Tab, Info... (the command; the dialog is P5.7a), My Rating, Mark as, Reveal in Finder, Select All, Invert Selection, Remove...; the other items are there and disabled until their tasks (P5.9, P5.11a, P5.13b, P5.16c, P5.20, P5.21, P5.24a, P4.5). Items the original shows only in some states are hidden here too: Edit (Details view), Web... (book with a Web field), the stack items (a stack). Separators are fixed as `FixSeparators` does. The menu opens only for a selection (a right click on the empty area is the view's own menu, P5.13a).
Differences: the Edit menu of the menu bar has My Rating without the stars editor (C-MENU-5).
