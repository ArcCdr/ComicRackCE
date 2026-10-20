# MainForm (ComicRack/MainForm.Designer.cs, MainForm.cs)

Mac view: `ComicRack.Mac.Views.MainWindow` (the library window) and `ComicRack.Mac.MainMenu` (P5.R1, first version). The reader area, book tabs and Quick Open of this form are P5.32a, the reader toolbar P5.R3; this spec records the whole form so later tasks extend it.

## 1. Sources
`MainForm.Designer.cs:436-445` (`mainMenuStrip`: `fileMenu`, `editMenu`, `browseMenu`, `readMenu`, `displayMenu`, `helpMenu`), `:1799-1818` (`statusStrip`), `:2577-2615` (`fileTabs`, `mainToolStrip`), `:3443-3454` (`mainViewContainer`); `MainForm.cs:963` (the size slider is inserted into the status strip), `:1261-1558` (command bindings), `:3895-3925` (`OnUpdateGui`: window title, `tsBook`, `tsCurrentPage`, `tsPageCount`, `tsText` = browser `SelectionInfo`). Texts: `Languages/<culture>/MainForm.xml`.

## 2. Layout
```
form "ComicRack" (TR MainForm/MainForm), client 744 x 662 in the designer
  mainMenuStrip           24, the macOS menu bar on the Mac (C-MENU-1)
  viewContainer (fill)    reader area: fileTabs (31 high, reader toolbar at the right, P5.R3), Quick Open (P5.29)
  mainViewContainer       docked bottom, 250 high, 6 grip: the browser (ComicExplorerView). In the Mac library window the browser fills the window.
  statusStrip             24 high: tsText (spring) | activity icons 20 x 19 | tsBook | tsCurrentPage | tsPageCount | size slider
```
Mac library window (P5.R1): side panel 252 (grip right) | browser toolbar 25 above the book list; status bar 24 at the bottom.

## 3. Controls (status strip, tab order)
| Control | TR key | English | Mac | Rule |
|---|---|---|---|---|
| `tsText` | `tsText` | Ready | status text | the browser's `SelectionInfo` (`ComicBrowserControl` spec) while a library is open, else the app's message ("Starting the library engine...") |
| `tsBackupActivity` ... `tsScanActivity` | | | not yet | SB.02, P5.26 |
| `tsBook` | `tsBook` (+ `.Tooltip`) | Book | segment, text "None" (`TR.Default` key None) | shows the open book's caption; the library window shows no book until P5.32a |
| `tsCurrentPage` | `tsCurrentPage` | Page | segment, text "NA" (`NotAvailable`) | page number of the open book |
| `tsPageCount` | `tsPageCount` | 0 Pages | segment, text "NA" | `TotalPageInformation` |
| size slider (`ToolStripThumbSize`) | | | slider slot 120 x 22, range 80 to 360 (MVP) | `SetItemSize` of the active browser; thumb 6 x 14 in the original |

## 4. Menus and shortcuts (ledger rows)
File (MF.*), Edit (ME.*), Browse (MB.*), Read (MR.*), Display (MD.*), Help (MH.*) in the ledger's order and with the ledger's shortcuts (DD20). In P5.R1 the menus hold what the app can do: File > Add Folder to Library... (MF.05, Cmd+Shift+A), Browse > Sidebar (MB.05, Shift+F6, checked), Display > Full Screen (MD.25, F11, alias Ctrl+Cmd+F, checked while full screen), Help > ComicRack Documentation... (MH.02, F1, alias Cmd+?; not implemented before P5.31). Edit and Read are present and empty. Mac-only: File > Import Windows Library... (MA.04), the Window menu with Minimize, Zoom and Show Performance Overlay (Ctrl+Option+Cmd+P, MA.05), the app menu (MA.01).

## 5. Icons
Window title: none. Status slots by resource name (`<Backup>`, `<ExportAnimation>`, `<ReadInfoAnimation>`, `<UpdateInfoAnimation>`, `<ReadPagesAnimation>`, `<ScanAnimation>`).

## 6. Tokens and metrics
Cr.Control (window), Cr.ControlText, Cr.ToolStatusStripGradientBegin/End, Cr.ToolStatusStripBorder, Cr.ButtonShadow, Cr.ButtonHighlight. Status strip 24, slots 20 x 19, font 11.

## 7. Behaviour
Title: "ComicRack" (plus " - <book>" with a book open, P5.32a). Closing the library window keeps the app running (DD7, P5.10b). The status text is rewritten from the browser on every change of the list, the search or the selection.

## 8. Planned differences
Menu strip becomes the native menu bar (C-MENU-1); Exit and About live in the app menu, Preferences as Settings... (ledger MF.18, MH.10, ME.16). Reader area and book tabs are not in the window until P5.32a (separate reader windows, MV.01). Segments tsBook, tsCurrentPage, tsPageCount are shown but not bound to a book (SB.04 stays todo until P5.32a).
