# MainForm (ComicRack/MainForm.Designer.cs, MainForm.cs)

Mac view: `ComicRack.Mac.Views.MainWindow` (the main window: tab strip, reader area, browser dock, status bar), `ComicRack.Mac.Views.ReaderArea` (the reader area: page and overlays), `ComicRack.Mac.Views.ReaderWindow` (the reader in its own window, `ReaderForm`) and `ComicRack.Mac.MainMenu` (P5.R1 first version; P5.32a added the tab, Read and Display items). Quick Open is P5.29, the other docking modes P5.32b, workspaces P5.32c; this spec records the whole form so later tasks extend it.

## 1. Sources
`MainForm.Designer.cs:436-445` (`mainMenuStrip`: `fileMenu`, `editMenu`, `browseMenu`, `readMenu`, `displayMenu`, `helpMenu`), `:1799-1818` (`statusStrip`), `:2577-2615` (`fileTabs`, `mainToolStrip`), `:3443-3454` (`mainViewContainer`); `MainForm.cs:963` (the size slider is inserted into the status strip), `:1261-1558` (command bindings), `:3895-3925` (`OnUpdateGui`: window title, `tsBook`, `tsCurrentPage`, `tsPageCount`, `tsText` = browser `SelectionInfo`). Texts: `Languages/<culture>/MainForm.xml`.

## 2. Layout
```
form "ComicRack" (TR MainForm/MainForm), client 744 x 662 in the designer
  mainMenuStrip           24, the macOS menu bar on the Mac (C-MENU-1)
  viewContainer (fill)    reader area: fileTabs (31 high, reader toolbar at the right, P5.R3), Quick Open (P5.29)
  mainViewContainer       docked bottom, 250 high, 6 grip: the browser (ComicExplorerView)
  statusStrip             24 high: tsText (spring) | activity icons 20 x 19 | tsBook | tsCurrentPage | tsPageCount | size slider
```
Mac main window (P5.32a), a `DockPanel` in this order: status bar 24 (bottom) | tab strip `TabBar` 31 with the reader toolbar in its slot (top) | `BrowserDock` (`CrSizableContainer`, grip on top, 250 by default, bottom: side panel 252 with its grip, browser toolbar 25, book list) | `ReaderHost` (fill: `ReaderArea` while a book is open, the Quick Open pane `QuickOpenPane` on the reader background otherwise). `BrowserDock.Fill` (no grip, takes everything) while there is no reader host (a window without a reader, tests) and while the reader is in its own window (`ReaderUndocked`: the original sets `BrowserDock = Fill` then).

## 3. Controls (status strip, tab order)
| Control | TR key | English | Mac | Rule |
|---|---|---|---|---|
| `tsText` | `tsText` | Ready | status text | the browser's `SelectionInfo` (`ComicBrowserControl` spec) while a library is open, else the app's message ("Starting the library engine...") |
| `tsBackupActivity` ... `tsScanActivity` | | | not yet | SB.02, P5.26 |
| `tsBook` | `tsBook` (+ `.Tooltip`) | Book | segment, text "None" (`TR.Default` key None) | the caption of the book of the current tab (60 characters at most) |
| `tsCurrentPage` | `tsCurrentPage` | Page | segment, text "NA" (`NotAvailable`), padlock `Locked` while Track current Page is off | page number of the book of the current tab |
| `tsPageCount` | `tsPageCount` | 0 Pages | segment, text "NA" | `TotalPageInformation` |
| size slider (`ToolStripThumbSize`) | | | slider slot 120 x 22, range 80 to 360 (MVP) | `SetItemSize` of the active browser; thumb 6 x 14 in the original |

## 4. Menus and shortcuts (ledger rows)
File (MF.*), Edit (ME.*), Browse (MB.*), Read (MR.*), Display (MD.*), Help (MH.*) in the ledger's order and with the ledger's shortcuts (DD20). The menus hold what the app can do: File > Close (MF.02, Option+Cmd+X, alias Cmd+W), Close All (MF.03, Cmd+Shift+X), New Tab (MF.04, Cmd+T), Add Folder to Library... (MF.05, Cmd+Shift+A), Open Books (MF.15, Ctrl+Cmd+F1 to F12); Browse > Browser (MB.01, F3, checked while the dock is open), Sidebar (MB.05, Shift+F6, checked); Read > First, Previous, Next, Last Page (MR.01 to MR.04), Show in Browser (MR.08, Option+Cmd+F3), Previous Tab, Next Tab (MR.09, MR.10, Cmd+Shift+J, K), Track current Page (MR.13, Option+Shift+T, checked); Display > Page Layout, Zoom (MD.02 to MD.17 as far as they exist), Full Screen (MD.25, F11, alias Ctrl+Cmd+F, checked while full screen), Reader in own Window (MD.26, F12, checked); Help > ComicRack Documentation... (MH.02, F1, alias Cmd+?; not implemented before P5.31). The Read and Display items work on the book of the current tab and are off without one. Key aliases of the window: Cmd+W (Close), Cmd+Shift+O (Open in new Tab, BC.02), Ctrl+Cmd+F, Cmd+?, Cmd+F9.

### Tab strip, tab menu, tool bar (`fileTabs`, `tabContextMenu`, `mainToolStrip`)
One tab per open book (text = caption, `MinimumWidth` 100, close box), then the New Tab button (`AddTab`, 32 wide, no text). `NavigatorManager.Open` rules: a book that is open is shown; otherwise it takes the place of the book of the current tab (also an empty tab), unless it is opened in a new tab (Open in new Tab, the setting `OpenInNewTab` reversed by a held Cmd or Shift), which is inserted at the **left end** (`slots.Insert(0, ...)`); a restored session appends at the right end. Closing the current tab shows the tab to its right (the previous one when it was the last). Tab menu (right click, the tab is selected first): Close, Close All But This, Close All to the Right, -, Show in Browser, -, Reveal in Finder (Cmd+G) and Copy Full Path to Clipboard (both only for a local file). The reader toolbar (Previous/Next Page, Page Layout, Fit, Zoom | Full Screen | Tools) works on the book of the current tab; without a book only Full Screen and Tools are shown. Mac-only: File > Import Windows Library... (MA.04), the Window menu with Minimize, Zoom and Show Performance Overlay (Ctrl+Option+Cmd+P, MA.05), the app menu (MA.01).

## 5. Icons
Window title: none. Status slots by resource name (`<Backup>`, `<ExportAnimation>`, `<ReadInfoAnimation>`, `<UpdateInfoAnimation>`, `<ReadPagesAnimation>`, `<ScanAnimation>`).

## 6. Tokens and metrics
Cr.Control (window), Cr.ControlText, Cr.ToolStatusStripGradientBegin/End, Cr.ToolStatusStripBorder, Cr.ButtonShadow, Cr.ButtonHighlight. Status strip 24, slots 20 x 19, font 11.

## 7. Behaviour
Title: "ComicRack" plus " - <caption of the book>" with a book open (the file name for a book that is not in a container). Closing the main window keeps the app running (DD7, P5.10b). The status text is rewritten from the browser on every change of the list, the search or the selection. Esc in the reader leaves full screen, else opens or closes the browser dock (`ToggleBrowserFromReader`; the click on the page is P5.32b). F12 or D moves the reader area into a window of its own (the main window keeps the tab strip, the status bar and the browser, which fills it) and back; closing that window docks the reader again. Full screen with a book in the window hides the tab strip, the status bar and the browser (the minimal GUI of the original; a native-over-original choice of the Mac, ledger MD.25). The open books are saved at quit and reopened at the next start (`OpenLastFile`); the dock height, whether it is open and whether the reader is in its own window are saved.

## 8. Planned differences
Menu strip becomes the native menu bar (C-MENU-1); Exit and About live in the app menu, Preferences as Settings... (ledger MF.18, MH.10, ME.16). Not done yet: the other docking modes, the browser tab strip and the list history (P5.32b), Quick Open (P5.29), workspaces (P5.32c), the cover thumbnail on a tab (the `Original` image stands in), dragging a tab to a new place, dropping files on the tab strip, the tab tool tip card (the Mac shows the caption and the shortcut as text); the open books keep one `PageService` each (section 9 of `full-port.md`).
