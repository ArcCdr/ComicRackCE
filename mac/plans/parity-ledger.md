# Parity ledger: Windows ComicRack CE features on macOS

> **Binding by R9 / D9** (`mac/plans/full-port.md` §1.1, `MACOS_NATIVE_PATH.md` §0). Every feature of the WinForms app is listed here once. A row is closed only as `done` (the Mac app does exactly what the original does, verified) or `deviation` (macOS makes the original impossible, or a native-over-original chrome rule of `mac/plans/chrome-guidelines.md` applies; the reason is written in the row). This ledger is the acceptance backbone: the parity audits P7.A1 to P7.A4 walk it, and v1 (C6) needs zero `todo` rows outside `deferred (D4)`.
> **Inventory source** (2026-10-20, planning session): extracted mechanically from the original Designer files and code, not from memory: `ComicRack/MainForm.Designer.cs` (menus, reader toolbar, tools menu, tab and page context menus, status strip), `ComicRack/MainForm.cs:1261-1558` (command bindings) and `:1563-1800` (reader keyboard map), `ComicRack/Views/*.Designer.cs`, `ComicRack/Controls/PagesView.Designer.cs`, `ComicRack/Dialogs/*.Designer.cs`, `ComicRack/Config/Settings.cs` (Behavior options with their defaults), `ComicRack.Plugins/PluginEngine.cs` (script hooks). P5.T3 adds `mac/tools/ledger-check`, which re-extracts the menu and toolbar items from the Designer files and fails when one has no row here (also after an upstream merge).

## How to use it

- **Every UI task** reads the rows whose "Mac task" column names it (`grep -n "P5.20" mac/plans/parity-ledger.md`), implements them exactly, and in its own commit sets `Status` to `done <date>` or `deviation` (with the reason) or leaves `todo` with a note naming the task that will finish it. A task may split a row into sub-rows (`BC.05a`, `BC.05b`) when one original item becomes several Mac pieces, and adds field-level sub-rows for dialogs when it writes the view spec (`IN.02.f01` ...).
- **Statuses:** `todo` · `done YYYY-MM-DD` · `deviation` (reason mandatory: `impossible: ...` or `native: <chrome-guidelines rule>`) · `deferred (D4)` (out of v1 by the binding scope decision D4; not a parity failure, revisited post-v1) · `mac-only` (an addition macOS needs; never replaces an original row).
- **Shortcuts** ("Win → Mac" column): the mapping rule DD20 (`full-port.md` §1.2): Ctrl becomes Cmd, Alt becomes Option, Shift and function keys stay, single keys of the reader stay; a chord that macOS reserves or that is a standard Mac command with another meaning gets Option added (then Ctrl+Cmd if that is taken too); Mac-standard aliases are added where the same command has one. The value in this column is the decision; tasks implement it and P7.A1 checks the menus against it. "—" = no shortcut in the original.
- **Location changes** are deviations of type `native` only for the items the chrome rules move (app menu: About, Settings, Quit; Window menu). Everything else keeps its original menu and position.

## Main menu (`ComicRack/MainForm.Designer.cs`, `mainMenuStrip`: File, Edit, Browse, Read, Display, Help)

| ID | Feature | Original location | Win → Mac | Mac task | Status | Deviation / note |
|---|---|---|---|---|---|---|
| MF.00 | Main menu bar: File, Edit, Browse, Read, Display, Help (in this order) | `mainMenuStrip`: `fileMenu`, `editMenu`, `browseMenu`, `readMenu`, `displayMenu`, `helpMenu` | — | P5.R1 | done 2026-10-20 | becomes the macOS menu bar (C-MENU-1); the six menus keep their names, texts (TR) and order, the Mac Window menu sits before Help; Edit and Read are empty until their tasks land |
| MF.01 | Open File... | File `miOpenComic` | Ctrl+O → Cmd+O | P5.10a | todo | MVP has Open Folder on Cmd+O; P5.10a moves it |
| MF.02 | Close (current book tab) | File `miCloseComic` | Ctrl+X → Option+Cmd+X, alias Cmd+W | P5.32a | todo | Cmd+X is Cut on macOS |
| MF.03 | Close All | File `miCloseAllComics` | Ctrl+Shift+X → Cmd+Shift+X | P5.32a | todo | |
| MF.04 | New Tab (empty reader slot) | File `miAddTab` (`OpenBooks.AddSlot`) | Ctrl+T → Cmd+T | P5.32a | todo | |
| MF.05 | Add Folder to Library... | File `miAddFolderToLibrary` | Ctrl+Shift+A → Cmd+Shift+A | P5.10a | todo | label and shortcut applied by P5.R1 (the MVP's Open Folder with the native folder picker); P5.10a adds the name-or-path dialog and the original's background scan |
| MF.06 | Scan Book Folders | File `miScan` | Ctrl+Shift+S → Cmd+Shift+S | P5.19b | todo | |
| MF.07 | Update all Book Files | File `miUpdateAllComicFiles` | Ctrl+Shift+U → Cmd+Shift+U | P5.9 | todo | |
| MF.08 | Update Web Comics | File `miUpdateWebComics` | Ctrl+Shift+W → Cmd+Shift+W | P5.19b | todo | Core feature; DD18 overridden by R9 |
| MF.09 | Generate Cover Thumbnails | File `miCacheThumbnails` | — | P5.19b | todo | |
| MF.10 | Synchronize Devices | File `miSynchronizeDevices` | — | — | deferred (D4) | device sync |
| MF.11 | Tasks... | File `miTasks` | Ctrl+Shift+T → Cmd+Shift+T | P5.26 | todo | |
| MF.12 | Automation (script commands) | File `miFileAutomation` (visible when scripts add items) | — | P4.5 | todo | |
| MF.13 | New fileless Book Entry... | File `miNewComic` | Ctrl+Shift+N → Cmd+Shift+N | P5.20 | todo | original shares Ctrl+Shift+N with Next Bookmark (ME.11); keep both as the original does |
| MF.14 | Open Remote Library... | File `miOpenRemoteLibrary` | Ctrl+Shift+R | — | deferred (D4) | library sharing |
| MF.15 | Open Books (list of open books) | File `miOpenNow` | — | P5.32a | todo | |
| MF.16 | Recent Books | File `miOpenRecent` | — | P5.10a | todo | label stays "Recent Books" |
| MF.17 | Restart | File `miRestart` | Ctrl+Shift+Q → Ctrl+Cmd+Shift+Q | P5.10b | todo | Cmd+Shift+Q and Option+Cmd+Shift+Q are system Log Out |
| MF.18 | Exit | File `miExit` | Ctrl+Q → Cmd+Q | P5.10b | deviation | native: Quit lives in the app menu (chrome rule C-MENU-1) |
| ME.01 | Info... | Edit `miShowInfo` | Ctrl+I → Cmd+I | P5.7a | todo | |
| ME.02 | Undo | Edit `miUndo` (`Database.Undo`) | Ctrl+Z → Cmd+Z | P5.7a | todo | |
| ME.03 | Redo | Edit `miRedo` | Ctrl+Y → Cmd+Y, alias Cmd+Shift+Z | P5.7a | todo | |
| ME.04 | My Rating (None, 1 to 5 stars, Quick Rating and Review...) | Edit `miRating` → `contextRating`, `miRate0`, `miRate1`, `miRate2`, `miRate3`, `miRate4`, `miRate5`, `miQuickRating` | — | P5.5 | todo | |
| ME.05 | Page Type | Edit `miPageType` | — | P5.23b | todo | |
| ME.06 | Page Rotation | Edit `miPageRotate` | — | P5.23b | todo | |
| ME.07 | Bookmarks > Set Bookmark... | Edit `miSetBookmark`, `miBookmarks` | Ctrl+Shift+B → Cmd+Shift+B | P5.23a | todo | |
| ME.08 | Bookmarks > Remove Bookmark | Edit `miRemoveBookmark` | Ctrl+Shift+D → Cmd+Shift+D | P5.23a | todo | |
| ME.09 | Bookmarks > Previous Bookmark | Edit `miPrevBookmark` | Ctrl+Shift+P → Cmd+Shift+P | P5.23a | todo | |
| ME.10 | Bookmarks > Next Bookmark | Edit `miNextBookmark` | Ctrl+Shift+N → Cmd+Shift+N | P5.23a | todo | |
| ME.11 | Bookmarks > Last Page Read | Edit `miLastPageRead` | Ctrl+Shift+L → Cmd+Shift+L | P5.23a | todo | |
| ME.12 | Copy Page | Edit `miCopyPage` | Ctrl+C → Cmd+C | P5.23a | todo | |
| ME.13 | Export Page... | Edit `miExportPage` | Ctrl+Shift+C → Cmd+Shift+C | P5.23a | todo | |
| ME.14 | Refresh | Edit `miViewRefresh` | F5 | P5.20 | todo | |
| ME.15 | Devices... | Edit `miDevices` | — | — | deferred (D4) | |
| ME.16 | Preferences... | Edit `miPreferences` | Ctrl+F9 → Cmd+F9, alias Cmd+, | P5.22a | deviation | native: lives in the app menu as "Settings…" (C-MENU-1); Cmd+F9 kept |
| MB.01 | Browser (show/hide browser) | Browse `miToggleBrowser` | F3 | P5.32b | todo | |
| MB.02 | Library | Browse `miViewLibrary` | F6 | P5.32b | todo | |
| MB.03 | Folders | Browse `miViewFolders` | F7 | P5.30 | todo | |
| MB.04 | Pages | Browse `miViewPages` | F8 | P5.23b | todo | |
| MB.05 | Sidebar | Browse `miSidebar` | Shift+F6 | P5.R1 | done 2026-10-20 | check item that follows the side panel; the toolbar button `tbSidebar` (BT.01) does the same |
| MB.06 | Small Preview | Browse `miSmallPreview` | Shift+F7 | P5.6 | todo | |
| MB.07 | Search Browser | Browse `miSearchBrowser` | Shift+F8 | P5.15b | todo | |
| MB.08 | Info Panel | Browse `miInfoPanel` | Shift+F9 | P5.6 | todo | |
| MB.09 | Previous List | Browse `miPreviousList` | Ctrl+J → Cmd+J | P5.32b | todo | |
| MB.10 | Next List | Browse `miNextList` | Ctrl+K → Cmd+K | P5.32b | todo | |
| MB.11 | Workspaces > Save Workspace... | Browse `miSaveWorkspace` | Ctrl+W → Option+Cmd+W | P5.32c | todo | Cmd+W is Close (MF.02) |
| MB.12 | Workspaces > Edit Workspaces... | Browse `miEditWorkspaces` | Ctrl+Alt+W → Ctrl+Cmd+W | P5.32c | todo | Option+Cmd+W taken by MB.11 |
| MB.13 | Workspaces > (saved workspaces list, apply) | Browse `miWorkspaces` (filled in `MainForm.cs`) | — | P5.32c | todo | DD16 overridden by R9 |
| MB.14 | List Layout > Edit List Layout... | Browse `miEditListLayout` | Ctrl+L → Cmd+L | P5.11b | todo | |
| MB.15 | List Layout > Save List Layout... | Browse `miSaveListLayout` | — | P5.11b | todo | |
| MB.16 | List Layout > Edit Layouts... | Browse `miEditLayouts` | Ctrl+Alt+L → Option+Cmd+L | P5.11b | todo | |
| MB.17 | List Layout > Set all Lists to current Layout | Browse `miSetAllListsSame` | — | P5.11b | todo | |
| MB.18 | List Layout > (saved layouts list, apply) | Browse `miListLayouts` | — | P5.11b | todo | |
| MR.01 | First Page | Read `miFirstPage` | Ctrl+B → Cmd+B | P5.R3 | todo | |
| MR.02 | Previous Page | Read `miPrevPage` | Ctrl+P → Cmd+P | P5.R3 | todo | no Print in the app, so Cmd+P stays |
| MR.03 | Next Page | Read `miNextPage` | Ctrl+N → Cmd+N | P5.R3 | todo | |
| MR.04 | Last Page | Read `miLastPage` | Ctrl+E → Cmd+E | P5.R3 | todo | |
| MR.05 | Previous Book (from list) | Read `miPrevFromList` | Ctrl+Alt+P → Option+Cmd+P | P5.23a | todo | MVP perf overlay must leave Option+Cmd+P (P5.R3) |
| MR.06 | Next Book (from list) | Read `miNextFromList` | Ctrl+Alt+N → Option+Cmd+N | P5.23a | todo | |
| MR.07 | Random Book | Read `miRandomFromList` | Ctrl+Alt+O → Option+Cmd+O | P5.23a | todo | |
| MR.08 | Show in Browser | Read `miSyncBrowser` | Ctrl+F3 → Option+Cmd+F3 | P5.32a | todo | Cmd+F3 is Show Desktop |
| MR.09 | Previous Tab | Read `miPrevTab` | Ctrl+Shift+J → Cmd+Shift+J | P5.32a | todo | |
| MR.10 | Next Tab | Read `miNextTab` | Ctrl+Shift+K → Cmd+Shift+K | P5.32a | todo | |
| MR.11 | Auto Scrolling | Read `miAutoScroll` | Ctrl+S → Cmd+S | P5.23d | todo | |
| MR.12 | Double Page Auto Scrolling | Read `miDoublePageAutoScroll` | Alt+Shift+S → Option+Shift+S | P5.23d | todo | |
| MR.13 | Track current Page | Read `miTrackCurrentPage` | Alt+Shift+T → Option+Shift+T | P5.32a | todo | |
| MD.01 | Book Display Settings... | Display `miComicDisplaySettings` | F9 | P5.23c | todo | |
| MD.02 | Page Layout > Original Size | Display `miOriginal`, `miPageLayout` | Ctrl+1 → Cmd+1 | P5.23e | todo | MVP fit shortcuts are realigned by P5.R3 |
| MD.03 | Page Layout > Fit All | Display `miFitAll` | Ctrl+2 → Cmd+2 | P5.R3 | todo | |
| MD.04 | Page Layout > Fit Width | Display `miFitWidth` | Ctrl+3 → Cmd+3 | P5.R3 | todo | |
| MD.05 | Page Layout > Fit Width (adaptive) | Display `miFitWidthAdaptive` | Ctrl+4 → Cmd+4 | P5.23e | todo | |
| MD.06 | Page Layout > Fit Height | Display `miFitHeight` | Ctrl+5 → Cmd+5 | P5.R3 | todo | |
| MD.07 | Page Layout > Fit Best | Display `miBestFit` | Ctrl+6 → Cmd+6 | P5.23e | todo | |
| MD.08 | Page Layout > Single Page | Display `miSinglePage` | Ctrl+7 → Cmd+7 | P5.R3 | todo | |
| MD.09 | Page Layout > Two Pages | Display `miTwoPages` | Ctrl+8 → Cmd+8 | P5.R3 | todo | |
| MD.10 | Page Layout > Two Pages (adaptive) | Display `miTwoPagesAdaptive` | Ctrl+9 → Cmd+9 | P5.R3 | todo | |
| MD.11 | Page Layout > Continuous | Display `miContinuous` | — | P5.23f | todo | |
| MD.12 | Page Layout > Right to Left | Display `miRightToLeft` | Ctrl+0 → Cmd+0 | P5.R3 | todo | DD7's Cmd+0 for Window > Library moves (C-MENU-2) |
| MD.13 | Page Layout > Only fit if oversized | Display `miOnlyFitOversized` | Ctrl+Shift+0 → Cmd+Shift+0 | P5.23e | todo | original shares the chord with MD.22 |
| MD.14 | Zoom > Zoom In | Display `miZoomIn`, `miZoom` | Ctrl+Plus → Cmd+= | P5.23e | todo | |
| MD.15 | Zoom > Zoom Out | Display `miZoomOut` | Ctrl+Minus → Cmd+- | P5.23e | todo | |
| MD.16 | Zoom > Toggle Zoom | Display `miToggleZoom` | Ctrl+Alt+Z → Option+Cmd+Z | P5.23e | todo | |
| MD.17 | Zoom > 100% 125% 150% 200% 400% | Display `miZoom100..400`, `miZoom125`, `miZoom150`, `miZoom200`, `miZoom400` | — | P5.23e | todo | |
| MD.18 | Zoom > Custom... (Custom Zoom dialog) | Display `miZoomCustom` | Ctrl+Shift+Z → Option+Cmd+Shift+Z | P5.23e | todo | Cmd+Shift+Z is Redo on macOS |
| MD.19 | Rotation > Rotate Left / Rotate Right | Display `miRotateLeft/Right`, `miRotation`, `miRotateRight` | Ctrl+Shift+Minus/Plus → Cmd+Shift+-/= | P5.23e | todo | |
| MD.20 | Rotation > No Rotation | Display `miRotate0` | Ctrl+Shift+7 → Cmd+Shift+7 | P5.23e | todo | |
| MD.21 | Rotation > 90° / 180° | Display `miRotate90/180`, `miRotate180` | Ctrl+Shift+8/9 → Cmd+Shift+8/9 | P5.23e | todo | |
| MD.22 | Rotation > 270° | Display `miRotate270` | Ctrl+Shift+0 → Cmd+Shift+0 | P5.23e | todo | |
| MD.23 | Rotation > Autorotate Double Pages | Display `miAutoRotate` | — | P5.23e | todo | |
| MD.24 | Minimal User Interface | Display `miMinimalGui` | F10 | P5.23f | todo | |
| MD.25 | Full Screen | Display `miFullScreen` | F11, alias Ctrl+Cmd+F | P5.R3 | todo | native full screen (C-WIN-2); the item is in the library window's Display menu since P5.R1 (checked while full screen); the reader's menu is P5.R3 |
| MD.26 | Reader in own Window | Display `miReaderUndocked` | F12 | P5.32a | todo | |
| MD.27 | Magnifier | Display `miMagnify` | Ctrl+M → Option+Cmd+M | P5.23d | todo | Cmd+M is Minimize |
| MH.01 | Help (internal, hidden) | Help `miHelp` | — | — | deviation | native: hidden in the original too; nothing to port |
| MH.02 | ComicRack Documentation... | Help `miWebHelp` | F1, alias Cmd+? | P5.31 | todo | item and shortcuts in place since P5.R1; the action reports "not available yet" until P5.31 |
| MH.03 | Plugins (script help entries) | Help `miHelpPlugins` (`MainForm.cs:3241`) | — | P5.31 | todo | |
| MH.04 | Choose Help System | Help `miChooseHelpSystem` (`Output/Help/*.ini`) | — | P5.31 | todo | |
| MH.05 | Quick Introduction | Help `miHelpQuickIntro` (DjVu file) | — | — | deferred (D4) | DjVu |
| MH.06 | ComicRack Homepage... | Help `miWebHome` | Shift+F1 | P5.31 | todo | |
| MH.07 | ComicRack User Forum... | Help `miWebUserForum` | Ctrl+F1 → Option+Cmd+F1 | P5.31 | todo | Cmd+F1 toggles display mirroring |
| MH.08 | News... | Help `miNews` | — | P5.31 | todo | |
| MH.09 | Check For Update... | Help `miCheckUpdate` | — | P5.31 | todo | |
| MH.10 | About... (splash panel) | Help `miAbout` | Alt+F1 → Option+F1 | P5.31 | deviation | native: About lives in the app menu (C-MENU-1); the panel itself is the original splash |
| MA.01 | App menu: About, Settings…, Services, Hide, Hide Others, Show All, Quit | macOS | Cmd+, / Cmd+H / Option+Cmd+H / Cmd+Q | P5.R1 | mac-only | C-MENU-1; done 2026-10-20: About with the app name, Settings… (disabled until P5.22a), Hide Others fixed to Option+Cmd+H |
| MA.02 | Window menu: Minimize, Zoom, Library window, Bring All to Front | macOS | Cmd+M; Library: Option+Cmd+0 | P5.10b | mac-only | C-MENU-2 |
| MA.03 | Text editing commands in text fields (Cut, Copy, Paste, Select All, Undo) | macOS | Cmd+X/C/V/A/Z | P5.R1 | mac-only | C-TEXT-1; done 2026-10-20: the text boxes (quick search) handle the Command keys themselves, the Edit menu holds no text items so that Cmd+Z stays free for the original's Undo (ME.02) |
| MA.04 | File > Import Windows Library… (copy a ComicDb.xml with path mappings) | none in the original | — | P5.R1 | mac-only | Mac-only addition from the MVP (M2.1) |
| MA.05 | Window > Show Performance Overlay | none in the original | Ctrl+Option+Cmd+P | P5.R1 | mac-only | developer aid of the MVP; moved from Option+Cmd+P, which is Previous Book (MR.05) |

## Reader toolbar, tools menu, tabs, page context menu, tray (`MainForm.Designer.cs`)

| ID | Feature | Original location | Win → Mac | Mac task | Status | Deviation / note |
|---|---|---|---|---|---|---|
| TB.01 | Previous Page split button (First Page, Previous Bookmark, Previous Book from List) | `mainToolStrip.tbPrevPage`, `tbFirstPage`, `tbPrevBookmark`, `tbPrevFromList` | as MR/ME rows | P5.R3 | todo | |
| TB.02 | Next Page split button (Last Page, Next Bookmark, Last Page Read, Next Book from List, Random Book) | `tbNextPage`, `tbLastPage`, `tbNextBookmark`, `tbLastPageRead`, `tbNextFromList`, `tbRandomFromList` | as MR/ME rows | P5.R3 | todo | |
| TB.03 | Page Layout split button (Single, Two Pages, Two Pages adaptive, Continuous, Right to Left) | `tbPageLayout`, `tbSinglePage`, `tbTwoPages`, `tbTwoPagesAdaptive`, `tbContinuous`, `tbRightToLeft` | as MD rows | P5.R3 | todo | button cycles the layout (`ComicDisplay.TogglePageLayout`) |
| TB.04 | Fit split button (Original, Fit All, Fit Width, adaptive, Fit Height, Fit Best, Only fit if oversized) | `tbFit`, `tbOriginal`, `tbFitAll`, `tbFitWidth`, `tbFitWidthAdaptive`, `tbFitHeight`, `tbBestFit`, `tbOnlyFitOversized` | as MD rows | P5.R3 | todo | button cycles fit (`TogglePageFit`) |
| TB.05 | Zoom split button showing "100 %" (Zoom In/Out, presets, Custom...) | `tbZoom`, `tbZoomIn`, `tbZoomOut`, `tbZoom100`, `tbZoom125`, `tbZoom150`, `tbZoom200`, `tbZoom400`, `tbZoomCustom` | as MD rows | P5.R3 | todo | |
| TB.06 | Rotate split button showing "0°" (rotation items, Autorotate) | `tbRotate`, `tbRotateLeft`, `tbRotateRight`, `tbRotate0`, `tbRotate90`, `tbRotate180`, `tbRotate270`, `tbAutoRotate` | as MD rows | P5.R3 | todo | |
| TB.07 | Magnifier split button | `tbMagnify` | — | P5.23d | todo | drop-down holds `MagnifySetupControl` |
| TB.08 | Full Screen button | `tbFullScreen` | — | P5.R3 | todo | |
| TB.09 | Tools drop-down (`toolsContextMenu`: Open Book..., Open Remote Library..., Info..., Workspaces, Bookmarks, Auto Scrolling, Minimal UI, Reader in own Window, Scan, Update all Book Files, Update Web Comics, Generate Cover Thumbnails, Synchronize Devices, Book Display Settings..., Preferences..., About..., Show Main Menu, Exit) | `tbTools`, `tbOpenComic`, `tbOpenRemoteLibrary`, `tbShowInfo`, `tsWorkspaces`, `tsSaveWorkspace`, `tsEditWorkspaces`, `tbBookmarks`, `tbSetBookmark`, `tbRemoveBookmark`, `tbAutoScroll`, `tbMinimalGui`, `tbReaderUndocked`, `tbScan`, `tbUpdateAllComicFiles`, `tbUpdateWebComics`, `tbCacheThumbnails`, `tsSynchronizeDevices`, `tbComicDisplaySettings`, `tbPreferences`, `tbAbout`, `tbExit` | items as their menu rows | P5.R3 | todo | remote and device items deferred (D4); Show Main Menu: TB.10 |
| TB.10 | Show Main Menu (toggle menu strip) | `tbShowMainMenu` | Shift+F10 | P5.23f | deviation | impossible: the macOS menu bar cannot be removed from a window; in full screen it auto-hides natively |
| TC.01 | Book tabs bar (one tab per open book, current tab, close box) | `fileTabs` (`TabBar`) | — | P5.32a | todo | original TabBar look (C-TAB-1) |
| TC.02 | Tab menu: Close | `cmClose` | as MF.02 | P5.32a | todo | |
| TC.03 | Tab menu: Close All But This | `cmCloseAllButThis` | — | P5.32a | todo | |
| TC.04 | Tab menu: Close All to the Right | `cmCloseAllToTheRight` | — | P5.32a | todo | |
| TC.05 | Tab menu: Show in Browser | `cmSyncBrowser` | as MR.08 | P5.32a | todo | |
| TC.06 | Tab menu: Reveal in Explorer | `cmRevealInExplorer` | Ctrl+G → Cmd+G | P5.32a | deviation | native: label "Reveal in Finder" (platform name) |
| TC.07 | Tab menu: Copy Full Path to Clipboard | `cmCopyPath` | — | P5.32a | todo | |
| PC.01 | Page menu: Info... | `pageContextMenu.cmShowInfo` | Cmd+I | P5.R3 | todo | |
| PC.02 | Page menu: My Rating | `cmRating` → `contextRating2`, `cmRate0`, `cmRate1`, `cmRate2`, `cmRate3`, `cmRate4`, `cmRate5`, `cmQuickRating` | — | P5.R3 | todo | |
| PC.03 | Page menu: Page Type, Page Rotation | `cmPageType`, `cmPageRotate` | — | P5.23b | todo | |
| PC.04 | Page menu: Bookmarks (Set, Remove, Previous, Next, Last Page Read) | `cmBookmarks`, `cmSetBookmark`, `cmRemoveBookmark`, `cmPrevBookmark`, `cmNextBookmark`, `cmLastPageRead` | as ME rows | P5.23a | todo | |
| PC.05 | Page menu: Books (Open File..., Close, Previous/Next/Random Book) | `cmComics`, `cmOpenComic`, `cmCloseComic`, `cmPrevFromList`, `cmNextFromList`, `cmRandomFromList` | as MF/MR rows | P5.23a | todo | |
| PC.06 | Page menu: Page Layout (fit, layout, rotation, oversized) | `cmPageLayout`, `cmOriginal`, `cmFitAll`, `cmFitWidth`, `cmFitWidthAdaptive`, `cmFitHeight`, `cmFitBest`, `cmSinglePage`, `cmTwoPages`, `cmTwoPagesAdaptive`, `cmContinuous`, `cmRightToLeft`, `cmRotate0`, `cmRotate90`, `cmRotate180`, `cmRotate270`, `cmOnlyFitOversized` | as MD rows | P5.R3 | todo | |
| PC.07 | Page menu: Magnifier, Copy Page, Export Page..., Refresh, Minimal User Interface | `cmMagnify`, `cmCopyPage`, `cmExportPage`, `cmRefreshPage`, `cmMinimalGui` | as their rows | P5.23a | todo | |
| NT.01 | Notification-area icon with menu (Restore, Exit); Minimize/Close to tray settings | `notifyIcon`, `notfifyContextMenu`, Settings `MinimizeToTray`, `CloseMinimizesToTray`, `cmNotifyRestore`, `cmNotifyExit` | — | P5.33 | todo | macOS equivalent: a menu bar status item (NSStatusItem) with the same menu |

## Status bar (`statusStrip`)

| ID | Feature | Original location | Win → Mac | Mac task | Status | Deviation / note |
|---|---|---|---|---|---|---|
| SB.01 | Status text ("Ready", messages) | `tsText` | — | P5.R1 | done 2026-10-20 | the browser's `SelectionInfo` (list name, books, filtered, size, selection) while a library is open, else the app's message |
| SB.02 | Activity icons: backup, export, read info, write info, page reading, scanning (animated) | `tsBackupActivity`, `tsExportActivity`, `tsReadInfoActivity`, `tsWriteInfoActivity`, `tsPageActivity`, `tsScanActivity` | — | P5.26 | todo | original animated images |
| SB.03 | Activity icons: device sync, server, data source state | `tsDeviceSyncActivity`, `tsServerActivity`, `tsDataSourceState` | — | — | deferred (D4) | |
| SB.04 | Book / Page / page count segments | `tsBook`, `tsCurrentPage`, `tsPageCount` | — | P5.32a | todo | segments and texts (None, NA, NA) in the library window since P5.R1; they follow the open book when the main window hosts the reader (P5.32a) |
| SB.05 | Thumbnail size slider (visible when the browser is shown) | `ToolStripThumbSize` inserted at `MainForm.cs:963` | — | P5.R1 | done 2026-10-20 | the slider is the last segment of the status bar |

## Main view: browser docking and browser tabs (`ComicRack/Views/MainView.Designer.cs`, `MainForm.cs`)

| ID | Feature | Original location | Win → Mac | Mac task | Status | Deviation / note |
|---|---|---|---|---|---|---|
| MV.01 | Main window = reader area (book tabs, Quick Open) plus a dockable browser panel | `MainForm` (`mainViewContainer`, `panelReader`) | — | P5.32a | todo | MVP separate reader windows become "Reader in own Window" |
| MV.02 | Browser tab strip (Library, Folders, Pages, lists opened in tabs) | `MainView.tabStrip` | — | P5.32b | todo | |
| MV.03 | Docking Mode: Dock Bottom / Left / Right / Fill / Info Panel Right | `tsbAlignBottom..tsbInfoPanelLeft`, `tsbAlignment`, `tsbAlignLeft`, `tsbAlignRight`, `tsbAlignFill` | Ctrl+Shift+1..5 → Option+Cmd+Shift+1..5 | P5.32b | todo | Cmd+Shift+3/4/5 are screenshots; the group stays consistent |
| MV.04 | Browser docking grip, animated expand/collapse (`AnimatePanels`) | `SizableContainer` | — | P5.32b | todo | |

## Browser (`ComicRack/Views/ComicBrowserControl.Designer.cs`, `ComicExplorerView`)

| ID | Feature | Original location | Win → Mac | Mac task | Status | Deviation / note |
|---|---|---|---|---|---|---|
| BT.01 | Toolbar: Sidebar toggle | `toolStrip.tbSidebar` | — | P5.4 | todo | |
| BT.02 | Toolbar: Previous / Next (list history) | `btBrowsePrev`, `btBrowseNext` | Mouse 4/5 | P5.32b | todo | |
| BT.03 | Toolbar: Views (Thumbnails, Tiles, Details) | `tbbView`, `miViewThumbnails`, `miViewTiles`, `miViewDetails` | — | P5.12 | todo | |
| BT.04 | Views: Collapse/Expand all Groups, Show Group Headers | `miExpandAllGroups`, `miShowGroupHeaders` | Ctrl+Shift+G → Cmd+Shift+G | P5.13a | todo | |
| BT.05 | Views: Show All / not Read / Reading / Read / only Books / only fileless Entries / Duplicates | `miShowOnly*` | — | P5.15a | todo | |
| BT.06 | Toolbar: Group (grouper list) | `tbbGroup`, `dummyToolStripMenuItem` | — | P5.13a | todo | |
| BT.07 | Toolbar: Stack (stacker list) | `tbbStack`, `dummyToolStripMenuItem1` | — | P5.13b | todo | |
| BT.08 | Toolbar: Arrange (sort keys, ascending/descending) | `tbbSort`, `dummyToolStripMenuItem2` | — | P5.14 | todo | |
| BT.09 | Toolbar: Quick search box with scope menu (All, Series, Writer, Artists, Descriptive, Catalog, Filename) | `tsQuickSearch`, `contextQuickSearch`, `miSearchAll`, `miSearchSeries`, `miSearchWriter`, `miSearchArtists`, `miSearchDescriptive`, `miSearchCatalog`, `miSearchFile` | Ctrl+F → Cmd+F (focus) | P5.15a | todo | |
| BT.10 | Toolbar: List Layouts (Edit, Save, Reset List Background, Edit Layouts, saved layouts) | `tsListLayouts`, `tsEditListLayout`, `tsSaveListLayout`, `tsEditLayouts` | Ctrl+L, Ctrl+Alt+L as MB rows | P5.11b | todo | |
| BT.11 | Toolbar: Duplicate list (into a new list or smart list) | `tbbDuplicateList`, `dummyEntryToolStripMenuItem2` | — | P5.16a | todo | |
| BT.12 | Toolbar: Undo / Redo | `tbUndo`, `tbRedo` | as ME.02/03 | P5.7a | todo | |
| BV.01 | Thumbnails view (cover, title lines, rating, badges) | `ItemView` + `ComicBookViewItem` | — | P5.R2 | done 2026-10-20 | thumbnail items drawn as `ThumbRenderer` and `ThumbIconRenderer` draw them (`CrThumbnailItem`, spec `CoverViewItem`): shadow, frame, empty tile, caption of three lines with the selection style, dog-ear, ribbons, state icons, ratings. Uniform item width (the grid is the MVP's `UniformGridLayout`): a variable width per cover is a section 9 item; the open and last markers are drawn but filled by P5.2; clicking the stars to rate is P5.5 |
| BV.02 | Tiles view | `ItemView` tiles | — | P5.12 | todo | |
| BV.03 | Details view (columns, sort by header, column chooser) | `ItemView` details | — | P5.11a | todo | |
| BV.04 | Item size by Ctrl+mouse wheel and by the status-bar slider | `ComicBrowserControl.cs:1255` | Ctrl+wheel → Cmd+wheel, also pinch | P5.R2 | done 2026-10-20 | Cmd+wheel (16 per notch, 80 to 360), a trackpad pinch, and the status-bar slider all set the same item size; the caption font and the cell follow it |
| BV.05 | Open stack panel (Close Stack, Previous, Next, stack caption) | `openStackPanel` | — | P5.13b | todo | |
| BV.06 | "Not all Books are displayed" bar with Display All | `displayOptionPanel` | — | P5.15a | todo | |
| BV.07 | List background image | `miSetListBackground`, `miResetListBackground` | — | P5.20 | todo | |
| BV.08 | Book tooltips (`ShowToolTips`), fade-in, dog-ear, numeric ratings, same-size covers | Settings (PB rows) | — | P5.R2 | done 2026-10-20 | tool tip card (`CrBookToolTip`, 360 x 120), fade-in of new covers, dog-ear, numeric rating tags, same size covers; the options are `ThumbnailOptions` with the defaults of `Settings.cs` |
| BV.09 | Multi-selection, Select All, Invert Selection | `miSelectAll`, `miInvertSelection` | Ctrl+A → Cmd+A | P5.3 | todo | |
| BV.10 | Keyboard in the browser: arrows, Return opens, Delete removes | `ItemView` | Delete, alias Cmd+Backspace | P5.3 | todo | |
| BC.01 | Context: Open | `contextMenuItems.miRead` | Ctrl+O → Cmd+O | P5.5 | todo | |
| BC.02 | Context: Open in new Tab | `miReadTab` | Ctrl+Shift+O → Cmd+Shift+O | P5.32a | todo | |
| BC.03 | Context: Open With (programs, Manage Programs...) | `miOpenWith`, `contextOpenWith`, `miOpenWithManager` | — | P5.21 | todo | |
| BC.04 | Context: Info... | `miProperties` | Cmd+I | P5.5 | todo | |
| BC.05 | Context: Web... (open the book's Web field) | `miShowWeb` | Ctrl+R → Cmd+R | P5.20 | todo | |
| BC.06 | Context: Edit (in-place edit of the focused field) | `miEdit` | F2 | P5.11a | todo | |
| BC.07 | Context: My Rating (None, 1 to 5, Quick Rating and Review...) | `miRateMenu`, `contextRating`, `miRating0`, `miRating1`, `miRating2`, `miRating3`, `miRating4`, `miRating5` | Alt+Shift+0..5, Alt+Shift+Q → Option+Shift+0..5, Option+Shift+Q | P5.5 | todo | |
| BC.08 | Context: Mark as (Unread, Read, Checked, Unchecked) | `miMarkAs`, `contextMarkAs`, `miMarkUnread`, `miMarkRead`, `miMarkChecked`, `miMarkUnchecked` | Alt+Shift+U/R/M/V → Option+Shift+U/R/M/V | P5.5 | todo | |
| BC.09 | Context: Edit List (Move to Top, Move to Bottom, Apply current Order) | `miEditList`, `miEditListMoveToTop`, `miEditListMoveToBottom`, `miEditListApplyOrder` | Ctrl+Alt+T/B → Option+Cmd+T/B | P5.16c | todo | |
| BC.10 | Context: Add to List (list of lists) | `miAddList`, `dummyEntryToolStripMenuItem` | — | P5.16c | todo | |
| BC.11 | Context: Add to Library | `miAddLibrary` | — | P5.20 | todo | |
| BC.12 | Context: Browse Books (show only) | `miShowOnly` | — | P5.20 | todo | |
| BC.13 | Context: Show in List (lists containing the book) | `miShowInList`, `dummyEntryToolStripMenuItem1` | — | P5.20 | todo | |
| BC.14 | Context: Export Books (Export Books..., Export with Previous) | `miExportComics`, `contextExport`, `miExportComicsAs`, `miExportComicsWithPrevious` | Ctrl+Shift+E, Ctrl+Alt+E → Cmd+Shift+E, Option+Cmd+E | P5.24a | todo | |
| BC.15 | Context: Automation (script commands) | `miAutomation` | — | P4.5 | todo | |
| BC.16 | Context: Update Book Files | `miUpdateComicFiles` | — | P5.9 | todo | |
| BC.17 | Context: Reveal Book in Explorer | `miRevealBrowser` | Ctrl+G → Cmd+G | P5.5 | deviation | native: label "Reveal in Finder" |
| BC.18 | Context: Copy Data / Paste Data... / Clear Data | `miCopyData`, `miPasteData`, `miClearData` | Ctrl+C/V → Cmd+C/V | P5.20 | todo | |
| BC.19 | Context: Select All / Invert Selection | `miSelectAll`, `miInvertSelection` | as BV.09 | P5.3 | todo | |
| BC.20 | Context: Refresh (re-read info and thumbnail) | `miRefreshInformation` | — | P5.20 | todo | |
| BC.21 | Context: Reset Top of Stack / Set as Top of Stack / Set custom Stack Thumbnail... / Remove custom Stack Thumbnail | `miResetTopOfStack`, `miSetTopOfStack`, `miSetStackThumbnail`, `miRemoveStackThumbnail` | — | P5.13b | todo | |
| BC.22 | Context: Set as List Background | `miSetListBackground` | — | P5.20 | todo | |
| BC.23 | Context: Remove... (Delete Item dialog: from list, library, to Trash) | `miRemove` | Delete, alias Cmd+Backspace | P5.5 | todo | |

## Sidebar: library tree, favorites, folders, pages, search browser, previews (`ComicRack/Views`, `ComicRack/Controls`, `ComicRack.Engine/Controls`)

| ID | Feature | Original location | Win → Mac | Mac task | Status | Deviation / note |
|---|---|---|---|---|---|---|
| LT.01 | Library tree with list icons, counts and live counters (gauges) | `ComicListLibraryBrowser`, Settings `LibraryGauges*` | — | P5.2 | todo | look done by P5.R1 (original images by list type, row height, bold selection, total counter pill); P5.2 adds the unread and new counters and the live update |
| LT.02 | Tree toolbar: New Folder, New List, New Smart List, New Window, New Tab, Expand/Collapse all, Refresh, Show Favorites, Quick Search | `ComicListLibraryBrowser.toolStrip`, `tbNewFolder`, `tbNewList`, `tbNewSmartList`, `tbExpandCollapseAll` | Quick Search Ctrl+Alt+F → Option+Cmd+F | P5.16a | todo | New Tab: P5.32b |
| LT.03 | Tree menu: Edit... (smart list editor) | `treeContextMenu.miEditSmartList` | — | P5.17a | todo | |
| LT.04 | Tree menu: Rename | `miQueryRename` | F2 | P5.16a | todo | |
| LT.05 | Tree menu: Sort | `miNodeSort` | — | P5.16a | todo | |
| LT.06 | Tree menu: New Folder... / New List... / New Smart List... | `miNewFolder`, `miNewList`, `miNewSmartList` | — | P5.16a | todo | |
| LT.07 | Tree menu: Copy List / Paste List | `miCopyList`, `miPasteList` | Ctrl+C/V → Cmd+C/V | P5.16a | todo | |
| LT.08 | Tree menu: Export Reading List... / Import Reading List... | `miExportReadingList`, `miImportReadingList` | Ctrl+Shift+C/V → Cmd+Shift+C/V | P5.16c | todo | |
| LT.09 | Tree menu: Open in New Window | `miOpenWindow` | — | P5.16a | todo | `ComicBrowserForm` |
| LT.10 | Tree menu: Open in New Tab | `miOpenTab` | — | P5.32b | todo | |
| LT.11 | Tree menu: Sync with Devices | `cmEditDevices` | — | — | deferred (D4) | |
| LT.12 | Tree menu: Add to Favorites | `miAddToFavorites` | — | P5.16b | todo | |
| LT.13 | Tree menu: Refresh | `miRefresh` | — | P5.16a | todo | |
| LT.14 | Tree menu: Remove... | `miRemoveListOrFolder` | Delete, alias Cmd+Backspace | P5.16a | todo | |
| LT.15 | Favorites panel (Refresh, Remove...) | `contextMenuFavorites`, `miFavRemove` | — | P5.16b | todo | |
| LT.16 | Drag and drop of lists (reorder, nest) and books onto lists | `ComicListLibraryBrowser.cs` | — | P5.16b, P5.18 | todo | |
| FB.01 | Folders browser: folder tree and books of the folder | `ComicListFolderFilesBrowser` | — | P5.30 | todo | |
| FB.02 | Folders toolbar: Show Favorites, Include all Subfolders, New Window, New Tab, Refresh | `toolStrip`, `tbFavorites`, `tbIncludeSubFolders`, `tbOpenWindow`, `tbOpenTab`, `tbRefresh` | — | P5.30 | todo | |
| FB.03 | Folders menu: Open in New Window, Open in New Tab, Add Folder to Favorites, Add Folder to Library, Refresh | `contextMenuFolders`, `miAddFolderLibrary` | — | P5.30 | todo | |
| FB.04 | Folder favorites (Refresh, Remove...) | `contextMenuFavorites`, `miFavRefresh` | — | P5.30 | todo | |
| PV.01 | Pages view of the open book (thumbnails, tiles, details; group, arrange, page filter) | `ComicPagesView` toolbar, `tbFilter` | — | P5.23b | todo | |
| PV.02 | Pages menu: Page Type, Page Rotation, Page Position (Default, Near, Far) | `PagesView.contextPages`, `miPagePosition`, `miPagePositionDefault`, `miPagePositionNear`, `miPagePositionFar` | — | P5.23b | todo | |
| PV.03 | Pages menu: Set Bookmark... / Remove Bookmark | `miSetBookmark`, `miRemoveBookmark` | Ctrl+Shift+B/D → Cmd+Shift+B/D | P5.23b | todo | |
| PV.04 | Pages menu: Move to Top / Move to Bottom / Reset original Order | `miMoveToTop`, `miMoveToBottom`, `miResetOriginalOrder` | Ctrl+Alt+T/B → Option+Cmd+T/B | P5.23b | todo | |
| PV.05 | Pages menu: Copy Page, Merge Pages, Select All, Invert Selection, Refresh, Mark as Deleted | `miCopy`, `miMergePages`, `miSelectAll`, `miInvertSelection`, `miRefreshThumbnail`, `miMarkDeleted` | Ctrl+C, Ctrl+A, Delete → Cmd+C, Cmd+A, Delete | P5.23b | todo | |
| SR.01 | Search browser (value columns with counts, multi-select narrowing) | `ComicRack.Engine/Controls/SearchBrowserControl.cs` | — | P5.15b | todo | |
| SP.01 | Small preview (page viewer, toolbar: Open, First, Previous, Next, Last, Two Pages, Refresh, Close) | `SmallComicPreview`, `tsbOpen`, `tsbFirst`, `tsbPrev`, `tsbNext`, `tsbLast`, `tsbTwoPages`, `tsbRefresh`, `tbClose` | — | P5.6 | todo | |
| IP.01 | Info panel (book info page with search links, script panels) | `ComicExplorerView.pluginContainer` (`ComicPageContainerControl`) | — | P5.6, P4.11a | todo | |
| QO.01 | Quick Open view when no book is open (recent books, Open, Browser, Open File...) | `QuickOpenView`, Settings `ShowQuickOpen` | — | P5.29 | todo | |

## Reader keyboard and mouse map (`ComicRack/MainForm.cs:1563-1800`; editable in Preferences > Reader > Keyboard)

Single keys and their mouse or gesture alternatives stay exactly as in the original. Windows touch gestures (`Gesture*`, `Flick*`, `Touch*`) map to trackpad gestures where macOS has one (swipe, pinch, double tap, two-finger tap); the others stay unbound (deviation per row).

| ID | Command (id) | Original keys | Mac keys | Mac task | Status | Deviation / note |
|---|---|---|---|---|---|---|
| RK.01 | Next Book (`NextComic`) | N | N | P5.23a | todo | |
| RK.02 | Previous Book (`PrevComic`) | P | P | P5.23a | todo | |
| RK.03 | Random Book (`RandomComic`) | L | L | P5.23a | todo | |
| RK.04 | Show Browser (`ShowBrowser`) | Mouse left (on the browser grip), Escape | same | P5.32b | todo | |
| RK.05 | First Page / Last Page (`MoveToFirstPage`, `MoveToLastPage`) | Ctrl+Home, Ctrl+End; double gestures 1/3 | Cmd+Home/End (fn+Cmd+Left/Right on laptops) | P5.R3 | todo | |
| RK.06 | Previous / Next Page (`MoveToPreviousPage`, `MoveToNextPage`) | PageUp/PageDown, Alt+Left/Right, gestures 1/3, flicks | PageUp/PageDown, Option+Left/Right, two-finger swipe | P5.R3 | todo | MVP has Left/Right; realign |
| RK.07 | Previous / Next Bookmark | Ctrl+PageUp/PageDown | Cmd+PageUp/PageDown | P5.23a | todo | |
| RK.08 | Previous / Next Tab | Shift+Tab, Tab | same | P5.32a | todo | |
| RK.09 | Single Page Back / Forward | Shift+PageUp/PageDown | same | P5.R3 | todo | |
| RK.10 | Previous Part / Next Part | Shift+Space, Space (gestures 7/9) | same | P5.R3 | todo | |
| RK.11 | Page Start / Page End | Home, End | same | P5.R3 | todo | |
| RK.12 | Move Part 10% down / up | V, Ctrl+Down; B, Ctrl+Up | V, Cmd+Down; B, Cmd+Up | P5.23d | todo | |
| RK.13 | Toggle Auto Scrolling / Double Page Auto Scroll | S; Shift+S | same | P5.23d | todo | |
| RK.14 | Up / Down / Left / Right (scroll) | arrows, mouse wheel, tilt | arrows, scroll | P5.R3 | todo | |
| RK.15 | Toggle Undock Reader | D | D | P5.32a | todo | |
| RK.16 | Toggle Full Screen | F, mouse double click, gesture 2 | F, double click | P5.R3 | todo | |
| RK.17 | Toggle Two Pages | T | T | P5.R3 | todo | |
| RK.18 | Toggle Realistic Display (page ornaments) | Shift+D | Shift+D | P5.23f | todo | §8 item 132 reinstated by R9 |
| RK.19 | Toggle Magnifier | M, touch press-and-tap | M | P5.23d | todo | |
| RK.20 | Toggle Menu (minimal UI menu) | K | K | P5.23f | todo | |
| RK.21 | Toggle Navigation Overlay | gesture 8, two-finger tap | two-finger tap | P5.R3 | todo | |
| RK.22 | Original / Fit All / Fit Width / Fit Width adaptive / Fit Height / Best Fit | 1 to 6 | 1 to 6 | P5.23e | todo | |
| RK.23 | Single Page / Two Pages / Two Pages adaptive / Continuous / Right to Left | 7, 8, 9, (none), 0 | same | P5.R3 | todo | |
| RK.24 | Only Fit if oversized | O | O | P5.23e | todo | |
| RK.25 | Rotate Right / Rotate Left / Autorotate Double Pages | R, Shift+R, A | same | P5.23e | todo | |
| RK.26 | Zoom In / Zoom Out | Ctrl+wheel | Cmd+scroll, pinch | P5.R3 | todo | |
| RK.27 | Step Zoom In / Out | Z, Shift+Z | same | P5.23e | todo | |
| RK.28 | Toggle Zoom | touch double tap | trackpad smart zoom (two-finger double tap) | P5.23e | todo | |
| RK.29 | Rotate Page Right / Left (permanent, page info) | Y, Shift+Y | same | P5.23b | todo | |
| RK.30 | Exit | Q | Q | P5.R3 | todo | quits like Cmd+Q |
| RK.31 | Windows touch gestures without a trackpad equivalent (gestures 4, 6, flick up/down) | `CommandKey.Gesture*` | — | P5.23d | deviation | impossible: no such trackpad gesture on macOS; the commands keep their keys |
| GK.01 | Focus Quick Search (`FocusQuickSearch`) | Ctrl+F | Cmd+F | P5.15a | todo | |
| GK.02 | Previous / Next List (`BrowsePrevious`, `BrowseNext`) | mouse buttons 4/5 | same | P5.32b | todo | |
| GK.03 | Library tree quick search (`ToggleQuickSearch`) | Ctrl+Alt+F | Option+Cmd+F | P5.16a | todo | |

## Dialogs and windows (`ComicRack/Dialogs`, `ComicRack/*.cs`, `cYo.Common.Windows/Forms`)

| ID | Dialog | Original | Mac task | Status | Deviation / note |
|---|---|---|---|---|---|
| DL.01 | Info (book) | `ComicBookDialog` (tabs: IN rows) | P5.7a | todo | |
| DL.02 | Multiple Book Information | `MultipleComicBooksDialog` | P5.8 | todo | |
| DL.03 | Paste Data to {0} Books | `ComicDataPasteDialog` | P5.20 | todo | |
| DL.04 | Book Display Settings | `ComicDisplaySettingsDialog` | P5.23c | todo | |
| DL.05 | Crash dialog ("ComicRack encountered a Problem...") | `CrashDialog` | P7.1 | todo | |
| DL.06 | Delete Item {0} | `DeleteItemDialog` | P5.5 | todo | |
| DL.07 | Devices, Select Device, device and server edit controls | `DevicesEditDialog`, `DeviceSelectDialog`, `DeviceEditControl`, `ServerEditControl`, `miDeviceCopy`, `miDevicePaste`, `miDeviceCopyToAll`, `miDeviceRename`, `miDeviceUnpair` | — | deferred (D4) | |
| DL.08 | Edit List | `EditListDialog` | P5.16a | todo | |
| DL.09 | Export Books | `ExportComicsDialog` | P5.24a | todo | |
| DL.10 | List editor (Edit Workspaces, Edit Layouts) | `ListEditorDialog` | P5.32c, P5.11b | todo | |
| DL.11 | List Options (tabs Details, Thumbnails, Tiles) | `ListLayoutDialog` | P5.11b | todo | |
| DL.12 | Smart list rule editors | `MatcherEditor`, `MatcherGroupEditor` (menu: New Rule Ctrl+R, New Group Ctrl+G, Delete, Cut, Copy, Paste, Move Up Ctrl+U, Move Down Ctrl+D), `miNewRule`, `miNewGroup`, `miDelete`, `miCut`, `miPaste`, `miMoveUp`, `miMoveDown` | P5.17b | todo | shortcuts Ctrl→Cmd; Cut/Copy/Paste keep their meaning |
| DL.13 | Latest ComicRack News | `NewsDialog` | P5.31 | todo | |
| DL.14 | Open Remote Library, Password | `OpenRemoteDialog`, `PasswordDialog` | — | deferred (D4) | |
| DL.15 | Open With (Manage Programs) | `OpenWithDialog` | P5.21 | todo | |
| DL.16 | Preferences | `PreferencesDialog` (PR rows) | P5.22a | todo | |
| DL.17 | Progress dialogs | `ProgressDialog`, `AutomaticProgressDialog` | P5.26 | todo | |
| DL.18 | Quick Rating | `QuickRatingDialog` | P5.5 | todo | |
| DL.19 | Save Workspace | `SaveWorkspaceDialog` | P5.32c | todo | |
| DL.20 | Errors | `ShowErrorsDialog` | P5.26 | todo | |
| DL.21 | Edit Smart List (rule builder) | `SmartListDialog` | P5.17b | todo | |
| DL.22 | Edit Smart List (query text; menu: Undo, Redo, Cut, Copy, Paste, Select All, Quick Format Ctrl+F, Insert Match, Insert Value) | `SmartListQueryDialog`, `miQuickFormat`, `miInsertMatch`, `miInsertValue` | P5.17a | todo | |
| DL.23 | Splash screen at startup (`ShowSplash`) and About panel | `Splash` | P5.31 | todo | MACOS_NATIVE_PATH §6.2 "no splash" superseded by D9 |
| DL.24 | Tasks (Background Tasks; Server Statistics tab) | `TasksDialog`, `dummyToolStripMenuItem` | P5.26 | todo | Server Statistics tab deferred (D4) |
| DL.25 | Match Value | `ValueEditorDialog` | P5.17c | todo | |
| DL.26 | Custom Zoom | `ZoomDialog` | P5.23e | todo | |
| DL.27 | Script Output | `ScriptOutputForm` | P4.5 | todo | |
| DL.28 | Reader in own window | `ReaderForm` | P5.32a | todo | |
| DL.29 | Browser in own window (Open in New Window) | `ComicBrowserForm` | P5.16a | todo | |
| DL.30 | Question dialogs with "do not show again" (`HiddenMessageBoxes`) | `QuestionDialog` | P5.5 | todo | reset in Preferences > Advanced (PR.27) |
| DL.31 | Select item / text input dialogs | `SelectItemDialog` | P5.16a | todo | used by bookmarks, names, external programs |
| DL.32 | Key input (shortcut capture) | `KeyInputForm` | P5.22c | todo | |
| DL.33 | Magnifier setup | `ComicRack.Engine/Controls/MagnifySetupControl` | P5.23d | todo | |
| DL.34 | Standard dialogs: open, save, folder, message boxes | `OpenFileDialog`, `SaveFileDialog`, `FolderBrowserDialog`, `MessageBox` | P5.R1 | deviation | native: macOS panels and alerts (C-DLG-1) |
| IN.01 | Info: Summary tab | `ComicBookDialog` | P5.7a | todo | 3D cover (`InformationCover3D`) |
| IN.02 | Info: Details tab | `ComicBookDialog` | P5.7a | todo | |
| IN.03 | Info: Plot & Notes tab (Summary, Notes, Review sub-tabs) | `ComicBookDialog` | P5.7b | todo | |
| IN.04 | Info: Catalog tab (`CatalogOnlyForFileless`) | `ComicBookDialog` | P5.7b | todo | |
| IN.05 | Info: Custom tab (`ShowCustomBookFields`) | `ComicBookDialog` | P5.7b | todo | |
| IN.06 | Info: Pages tab (menu: Order by Name, Order by Name numeric) | `ComicBookDialog`, `cmResetPages`, `miOrderByName`, `miOrderByNameNumeric` | P5.7c | todo | |
| IN.07 | Info: Colors tab ("Double Click on Color to set White Point") | `ComicBookDialog` | P5.7c | todo | |
| IN.08 | Info: thumbnail menu (Reset) and Previous/Next book buttons | `cmThumbnail`, buttons, `miResetThumbnail` | P5.7a | todo | |

## Preferences (`ComicRack/Dialogs/PreferencesDialog.Designer.cs`; tabs Reader, Libraries, Behavior, Scripts, Advanced)

| ID | Pane > group | Contents | Mac task | Status | Deviation / note |
|---|---|---|---|---|---|
| PR.01 | Window frame | left tab column (Reader, Libraries, Behavior, Scripts, Advanced), collapsible groups, OK / Cancel / Apply | P5.22a | todo | original layout (C-PREF-1) |
| PR.02 | Reader > Display | contrast, brightness, saturation, gamma, sharpening sliders, Automatic Contrast Enhancement, High Quality, Anamorphic Scaling, Reset | P5.22a | todo | |
| PR.03 | Reader > Keyboard | shortcut editor, Import... (with Default layout), Export... (`*.xml`), `miDefaultKeyboardLayout` | P5.22c | todo | DD19 overridden by R9 |
| PR.04 | Reader > Overlays | Show Navigation automatically, visible Page Part, Messages and Status, current Page, page names; sizing; navigation position | P5.22a | todo | |
| PR.05 | Reader > Mouse Scrolling | wheel speed slow/fast, Inertial Mouse scrolling, Smooth Auto Scrolling | P5.22a | todo | |
| PR.06 | Reader > Hardware Acceleration | Animation of Page Display changes, Hardware Acceleration, Software Filters, Hardware Filters | P5.22a | deviation | impossible: partly (no OpenGL/Tao path, §6.4): rendering is always Metal through Skia and "Enable Hardware Acceleration" has no off switch on macOS; animation and filters keep their meaning |
| PR.07 | Reader > macOS group (swipe at edge, pinch speed, trackpad) | Mac-only additions (DD8) | P5.22a | mac-only | |
| PR.08 | Libraries > Book Folders | folder list with watch check boxes, Add..., Remove, Change..., Open | P5.22b | todo | |
| PR.09 | Libraries > Scanning | Scan, auto-remove missing, do not re-add removed files | P5.22b | todo | |
| PR.10 | Libraries > Display | Live Counters (New, Unread, Total, numbers) | P5.22b | todo | |
| PR.11 | Libraries > Sharing, Server Settings | shares, public address, private list password | — | deferred (D4) | |
| PR.12 | Libraries > Virtual Tags | tag selector, enable, name, description, caption prefix/field/suffix/format, Insert, Choose Value, help | P5.22b | todo | |
| PR.13 | Behavior | generated from Settings booleans by category (PB rows) | P5.22a | todo | |
| PR.14 | Scripts > Script Packages | list, Install..., Remove | P4.11e | todo | |
| PR.15 | Scripts > Available Scripts | list, Configure..., Hide sample Scripts | P4.11e | todo | |
| PR.16 | Scripts > Script Settings | library paths, Add Folder..., Disable all Scripts | P4.11e | todo | |
| PR.17 | Advanced > Languages | language list, Help localizing... | P5.27b | todo | |
| PR.18 | Advanced > Books | cover filter, Allow writing of Book info, Library info, files updated automatically | P5.22b | todo | |
| PR.19 | Advanced > Database Backup | Backup Database..., Restore Database... | P5.25 | todo | |
| PR.20 | Advanced > Backup Manager | location, items, number kept, alternate configs, backup on startup / shut down | P5.25 | todo | |
| PR.21 | Advanced > Caches & Memory Usage | disk caches (thumbnails, pages, internet) with sizes and Clear; memory caches; maximum memory | P5.22b | todo | |
| PR.22 | Advanced > Messages and Social | reset hidden messages | P5.22b | todo | |
| PR.23 | Advanced > Explorer Integration | formats to associate, overwrite associations, Change... | P5.34 | deviation | native: label "Finder Integration"; associations through Launch Services (same effect) |
| PR.24 | Advanced > Wireless Setup | device addresses, Test | — | deferred (D4) | |

### Behavior options (`ComicRack/Config/Settings.cs`, generated into the Behavior pane by `FormUtility.FillPanelWithOptions`)

Defaults must equal the original's (a test in P5.1 compares them). "Mac task" = the task that makes the option *work*; P5.22a shows them all.

| ID | Category | Option (property) | Default | Mac task | Status | Deviation / note |
|---|---|---|---|---|---|---|
| PB.01 | Starting ComicRack | Show Splash Screen (`ShowSplash`) | true | P5.31 | todo | |
| PB.02 | Starting ComicRack | Reopen Books from last session (`OpenLastFile`) | true | P5.32a | todo | |
| PB.03 | Starting ComicRack | Rescan the Book Folders for new Books (`ScanStartup`) | false | P5.19b | todo | |
| PB.04 | Starting ComicRack | Update Web Comics (`UpdateWebComicsStartup`) | false | P5.19b | todo | |
| PB.05 | Starting ComicRack | Check for latest news (`NewsStartup`) | true | P5.31 | todo | |
| PB.06 | Opening a Book | Open the Book at the page where it was closed (`OpenLastPage`) | true | P5.23a | todo | |
| PB.07 | Opening a Book | Close the Browser when a new Book is opened (`CloseBrowserOnOpen`) | false | P5.32b | todo | |
| PB.08 | Opening a Book | Opened Files are added to the Library (`AddToLibraryOnOpen`) | false | P5.10a | todo | |
| PB.09 | Opening a Book | Open in new Tab (`OpenInNewTab`) | false | P5.32a | todo | |
| PB.10 | Reading | Hide the mouse cursor in Full Screen (`HideCursorFullScreen`) | true | P5.R3 | todo | |
| PB.11 | Reading | Reading beyond start or end opens the next Book (`AutoNavigateComics`) | true | P5.23a | todo | |
| PB.12 | Reading | Wheel and cursor key delay on page transitions (`PageChangeDelay`) | true | P5.23d | todo | |
| PB.13 | Reading | Scrolling to page margin browses to new pages (`ScrollingDoesBrowse`) | true | P5.23d | todo | |
| PB.14 | Reading | Zoom is reset to 100% on page change (`ResetZoomOnPageChange`) | false | P5.23e | todo | |
| PB.15 | Reading | Zoom out during page change (`ZoomInOutOnPageChange`) | true | P5.23e | todo | |
| PB.16 | Reading | Blend animation while fast paging (`BlendWhilePaging`) | false | P5.23c | todo | |
| PB.17 | Reading | Fullscreen also toggles Minimal User Interface (`AutoMinimalGui`) | false | P5.23f | todo | |
| PB.18 | Reading | Show Quick Review Dialog after finishing Book (`AutoShowQuickReview`) | false | P5.5 | todo | |
| PB.19 | Right to Left | True right to left reading (`TrueRightToLeftReading`) | false | P5.23e | todo | |
| PB.20 | Right to Left | Left/right movement is also reversed (`LeftRightMovementReversed`) | false | P5.23e | todo | |
| PB.21 | Browser | Show Tooltips for Books (`ShowToolTips`) | false | P5.R2 | done 2026-10-20 | the option works (`ThumbnailOptions.ShowToolTips`); the check box is P5.22a, saving it P5.1 |
| PB.22 | Browser | Show Search Links (`ShowSearchLinks`) | true | P5.6 | todo | |
| PB.23 | Browser | New loaded Thumbnails fade in (`FadeInThumbnails`) | true | P5.R2 | done 2026-10-20 | the option works (`CoverView` fade, 300 ms); the check box is P5.22a, saving it P5.1 |
| PB.24 | Browser | Selected Thumbnails have a dog-ear (`DogEarThumbnails`) | true | P5.R2 | done 2026-10-20 | the option works (dog-ear on the selected and the hot cover); the check box is P5.22a, saving it P5.1 |
| PB.25 | Browser | Thumbnails display numeric ratings (`NumericRatingThumbnails`) | true | P5.R2 | done 2026-10-20 | the option works (tags, else stars below or over the cover); the check box is P5.22a, saving it P5.1 |
| PB.26 | Browser | Each List has own Quick Search settings (`LocalQuickSearch`) | true | P5.15a | todo | |
| PB.27 | Browser | All Cover Thumbnails have the same Size (`CoverThumbnailsSameSize`) | false | P5.R2 | done 2026-10-20 | the option works (every cover fills the 2:3 box, a landscape one shows its right part); the check box is P5.22a, saving it P5.1 |
| PB.28 | Browser | All Stacks in a List have the same Layout (`CommonListStackLayout`) | false | P5.13b | todo | |
| PB.29 | Browser | Always display Browser Docking Grip (`AlwaysDisplayBrowserDockingGrip`) | false | P5.32b | todo | |
| PB.30 | Browser | Disable opening files via drag-and-drop (`DisableDragDrop`) | false | P5.18 | todo | |
| PB.31 | Application | Show Quick Open when no book is open (`ShowQuickOpen`) | true | P5.29 | todo | |
| PB.32 | Application | Show Catalog fields only for fileless Books (`CatalogOnlyForFileless`) | true | P5.7b | todo | |
| PB.33 | Application | Show custom Book fields (`ShowCustomBookFields`) | false | P5.7b | todo | |
| PB.34 | Application | Minimize moves ComicRack into the Notification Area (`MinimizeToTray`) | false | P5.33 | todo | menu bar status item |
| PB.35 | Application | Close moves ComicRack into the Notification Area (`CloseMinimizesToTray`) | true | P5.33 | todo | |
| PB.36 | Application | Animate expanding and collapsing Panels (`AnimatePanels`) | true | P5.32b | todo | |
| PB.37 | Application | Show Main Menu if no Book is open (`ShowMainMenuNoComicOpen`) | true | P5.23f | deviation | impossible: macOS menu bar is always present (TB.10) |
| PB.38 | Application | 3D display of covers in Book Info Dialog (`InformationCover3D`) | true | P5.7a | todo | |
| PB.39 | Application | Newly added Books are checked (`NewBooksChecked`) | true | P5.19a | todo | |
| PB.40 | Import & Export | Exported Book Lists contain filenames (`ExportedListsContainFilenames`) | false | P5.16c | todo | |

## Scripting hooks (`ComicRack.Plugins/PluginEngine.cs`)

| ID | Hook | Description (original) | Mac task | Status | Deviation / note |
|---|---|---|---|---|---|
| SC.01 | `Books` | Edit/Update Books Commands | P4.3, P4.5 | todo | |
| SC.02 | `NewBooks` | Create New Books Commands | P4.3, P4.5 | todo | |
| SC.03 | `Library` | Library commands | P4.3, P4.5 | todo | |
| SC.04 | `Editor` | Editor commands (Info dialog) | P4.5 | todo | added to P4.5 by R9 |
| SC.05 | `ParseComicPath` | Book Path Parsers | P4.3 | todo | |
| SC.06 | `CreateBookList` | Script lists | P4.3 | todo | |
| SC.07 | `BookOpened` | Actions when Books are opened | P4.5 | todo | added to P4.5 by R9 |
| SC.08 | `ReaderResized` | Actions when Reader is resized | P4.5 | todo | added to P4.5 by R9 |
| SC.09 | `NetSearch` | Additional Search Providers | P4.3 | todo | |
| SC.10 | `Startup` | Actions when ComicRack starts | P4.3 | todo | |
| SC.11 | `Shutdown` | Actions when ComicRack shuts down | P4.3 | todo | |
| SC.12 | `ConfigScript` | Script configuration | P4.11e | todo | |
| SC.13 | `ComicInfoHtml` | Book Information Panels (HTML) | P4.11d | todo | |
| SC.14 | `ComicInfoUI` | Book Information Panels (WinForms) | P4.11a | todo | |
| SC.15 | `QuickOpenHtml` | Quick Open Panels (HTML) | P4.11d | todo | |
| SC.16 | `QuickOpenUI` | Quick Open Panels (WinForms) | P4.11a | todo | |
| SC.17 | `DrawThumbnailOverlay` | Custom Book Thumbnail Overlays | P4.11b | todo | |
| SC.18 | Python smart-list expressions and `ComicBookPluginMatcher` | | P4.3 | todo | |

## Files and formats (must stay byte-compatible in meaning)

| ID | File | Original | Mac task | Status | Deviation / note |
|---|---|---|---|---|---|
| FF.01 | Library `ComicDb.xml` (schema, lists, smart lists, view configs) | Core | P0.7a, P5.11b | todo | DD3 |
| FF.02 | Settings (`Config.xml` portable parts: reader, library, lists, workspaces, layouts, external programs, keyboard) | `ComicRack/Config/Settings.cs` | P5.28b | todo | Mac file is its own XML (DD3) |
| FF.03 | Reading lists `.cbl` | `ComicReadingListItem` | P5.16c | todo | |
| FF.04 | Plugins `.crplugin` / script `.zip` | `PackageManager` | P4.4 | todo | |
| FF.05 | Keyboard layout export `*.xml` | `PreferencesDialog.cs:741` | P5.22c | todo | |
| FF.06 | Backups `*.zip` | `BackupManager` | P5.25 | todo | |
| FF.07 | ComicInfo.xml inside archives | Core | P2.4b | todo | |
| FF.08 | Export formats (CBZ, CB7, CBT, PDF, image folders) | `ExportComicsDialog`, `ComicExporter` | P5.24a, P5.24b | todo | |
