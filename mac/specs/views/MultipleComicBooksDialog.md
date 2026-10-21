# MultipleComicBooksDialog (ComicRack/Dialogs/MultipleComicBooksDialog.cs, .Designer.cs)

Mac view: `Views/MultipleComicBooksDialog` (frame `CrDialogWindow`), body `Views/MultipleBooksBody.axaml` (generated from the Designer's coordinates), model `Editor/MultiBookEditorModel`, P5.8. Ledger DL.02 (and ME.01, the Edit > Info... item for several books); decisions C-DLG-2, C-DLG-6, C-GRP-1, C-CTRL-1, C-CTRL-3. The group box is specified in `CollapsibleGroupBox.md`.

## 1. Sources
`MultipleComicBooksDialog.Designer.cs` (positions, sizes, TR keys), `MultipleComicBooksDialog.cs`: constructor 56-132 (lists, number keys, spin buttons, enable rules, custom fields), `AddCustomFields` 134-171, `OnLoad`/`Init` 173-286 (the `SetText` calls: property and list of each box), `Store` 288-391 (OK), `GetSameValue` 393-407, `MakeCheckBox` 409-430, `SetGrayText` 432-452, `SetText` 454-554 (shown values, list boxes, the check box rules), `ListSelectorControl.Register` (`cYo.Common.Windows/Forms/ListSelectorControl.cs:377-442`), `SpinButton.AddUpDown` (`cYo.Common.Windows/Forms/SpinButton.cs:319-408`), `TextBoxEx.cs` (prompt text), `FormUtility.cs:600-790` (`RestorePanelStates`, `PanelToTab`, `RegisterPanelToTabToggle`), `ComicRack/MainForm.cs:2074-2096` and `4340-4366` (`ShowInfo`, `ShowComicInfo`: the undo marker "Edit multiple Books", the dialog for more than one editable book), `ComicRack/Config/TabLayouts.cs`, `EditControlUtility.cs`.

## 2. Layout
```
form  client 525 x 460, fixed dialog, centred on the owner, accept = OK, cancel = Cancel; title "Multiple Book Information (N)" (N = the number of books)
pageData (12,12) 497 x 407, a one unit frame (BorderStyle.FixedSingle), scrolls (AutoScroll); inside it the groups are stacked (Dock = Top), 478 wide (beside the 17 wide scroll bar):
  grpMain      "Main"                        400 high  (header 24)
  grpArtists   "Artists / People Involved"   204
  grpPlotNotes "Plot & Notes"                265
  grpCatalog   "Catalog"                     392        (hidden: books with files and CatalogOnlyForFileless)
  grpCustom    "Custom Fields"               last box bottom + 32 under the 24 high header   (hidden: ShowCustomBookFields off, or no custom key)
btOK (346,427) 77 x 24   btCancel (429,427) 80 x 24
```
Every box of a group is at the Designer's position (measured from the top of the group, header included). `MakeCheckBox` puts a check box (16 x 17, left edge at the box's left, vertically centred on the box) in front of every box and moves the box 18 to the right and makes it 18 narrower; the labels stay where the Designer has them (the original's search for the label finds nothing, so they never move). `ListSelectorControl.Register` makes the box 16 narrower and puts the 16 wide selector button at its right (Writer, Penciller, Inker, Colorist, Editor, Cover Artist, Translator, Letterer, Genre, Tags, Characters, Teams, Locations, Collection Status, Main Character/Team); `SpinButton.AddUpDown` makes a number box 11 narrower and puts the 11 wide spin button 1 to the right of it (Volume, Number, Count, Year, Month, Day, Alternate Number, Alternate Count, Pages). The check boxes and boxes are listed by the test `Every_box_its_check_box_selector_and_spin_button_is_where_the_original_puts_it`, which reads the Designer file.

Custom Fields (`AddCustomFields`): for every key without a point of `Database.CustomValues` a label "Key:" at (10, y), a check box at (11, y + 15 + 1) and a box 451 - 18 wide at (11 + 18, y + 15), y = 8 for the first and the bottom of the previous box + 8 for the next (label 12 high, 4 between label and box); the panel has a one unit frame.

## 3. Controls
| Box | Property of the book | Kind | Mac |
|---|---|---|---|
| `txSeries`, `txTitle`, `txAlternateSeries`, `txSeriesGroup`, `txStoryArc`, `txScanInformation` | Series, Title, AlternateSeries, SeriesGroup, StoryArc, ScanInformation | text | `AutoCompleteBox` over the library's values (`EditorValueLists`; Title is the raw title here) |
| `txNumber`, `txAlternateNumber`, `txISBN`, `txWeb` | Number, AlternateNumber, ISBN, Web | text | `TextBox` |
| `txCount`, `txYear`, `txVolume`, `txMonth`, `txDay`, `txAlternateCount`, `txPagesAsTextSimple` | the numbers | int (a text that is not a number, or is negative, is the property's default) | `TextBox` + `CrSpinButton`; Count, Year, Volume, Month, Alternate Count and Pages take digits, point and comma only |
| `txWriter` ... `txMainCharacterOrTeam` (15 boxes) | the credit, genre, tag, character, team, location and collection status fields | list | `AutoCompleteBox` + selector button + indeterminate check box when the books differ |
| `txSummary`, `txNotes`, `txReview`, `txBookNotes` | Summary, Notes, Review, BookNotes | text | `TextBox`, multi-line, wraps |
| `cbPublisher`, `cbImprint`, `cbFormat`, `cbAgeRating`, `cbBookStore`, `cbBookOwner`, `cbBookLocation`, `cbBookPrice` (BookPriceAsText), `cbBookAge`, `cbBookCondition` | the same names | text | `CrValueComboBox`, editable, with the library's values and the pictures of the icon packages |
| `cbSeriesComplete`, `cbBlackAndWhite`, `cbManga`, `cbEnableProposed` | SeriesComplete, BlackAndWhite (Yes/No), Manga (Yes, Yes (Right to Left), No), EnableProposed (bool: "Yes" in any case is true) | `InitializeYesNo`, `InitializeMangaYesNo` (the empty entry first) | `CrValueComboBox` as a list |
| `cbLanguage` | LanguageISO | text | `CrValueComboBox` as a list: no language, the languages the library uses, a line, every language |
| `txRating`, `txCommunityRating` | Rating (yellow stars), CommunityRating (blue stars) | float | `CrRatingStars` with the number (the original shows the number in a frame, also for 0) |
| `dtpOpenedTime`, `dtpAddedTime`, `dtpReleasedTime` | OpenedTime, AddedTime, ReleasedTime | date | `CalendarDatePicker`, Long format, blank when not set (C-INFO-2) |

## 4. Menus and shortcuts
Edit > Info... (Cmd+I, ME.01) and the browser context menu's Info... open it when more than one book is selected and every selected book can be edited (`EditMode.CanEditProperties`); with one book, or a book that cannot be edited, the Info dialog opens (DL.01). The script interface's `ShowComicInfo(books)` (P4.3) does the same for its books.

## 5. Icons
`Route` (the selector buttons), `StarYellow`, `StarBlue` (the ratings), `SimpleArrowDown`, `SimpleArrowRight` (the group headers), the icon packages.

## 6. Tokens
`Cr.Control`, `Cr.WindowFrame` (the frame of the panel and of the custom fields' panel), `Cr.CollapsibleGroupBoxHeaderStart`, `...HeaderEnd`, `...HeaderText`, `Cr.ButtonBack`, `Cr.ButtonBorder`, `Cr.ControlDark`.

## 7. Behaviour
- Before the dialog: the undo marker "Edit multiple Books" (`UndoEditMultipleComics`), then the information of every book is read from its file (`RefreshInfoFromFile(None)`, here off the UI thread; the original shows a progress dialog after one second, DL.17 of P5.26).
- Every box shows the value the books have in common; when they differ it is empty (a date: nothing) and the check box is unchecked. When they agree, the check box is checked and the value is shown (an unknown number, -1, shows nothing but is still checked). A list box shows the entries every book has (intersection); when the lists differ the check box is indeterminate.
- Typing in a box (any change to a text with something in it, or a date) checks it. A click on the check box: unchecked becomes checked, checked and indeterminate become unchecked; unchecking an indeterminate list box also empties the box.
- Prompts (Series, Number, Count, Year, Volume, Format): the value all the books propose (`ProposedSeries`, `ProposedNumber`, `ProposedCountAsText`, `ProposedYearAsText`, `ProposedNakedVolumeAsText`, `ProposedFormat`) is shown gray in the empty box and goes into it when the box gets the focus (`TextBoxEx`) and when the check box is checked; unchecking removes it while it is still the prompt.
- Enable rules: Opened/Read and Pages (boxes, check boxes, labels, spin button) only for books without a file; Scan Information only when some book has a file; My Rating, Proposed Values and Series complete only when every book is in a library (the check box is disabled with the box); the Catalog group is hidden for books with files when `CatalogOnlyForFileless` is on; the Custom Fields group needs `ShowCustomBookFields`.
- OK (`Store`): every checked or indeterminate, enabled box is written into every book as its text says: Yes/No boxes through `GetYesNo`/`GetMangaYesNo`, numbers through `int.TryParse` (not a number or negative: the property's default), ratings through `float.TryParse`, dates as the date, custom boxes through `SetCustomValue`. An indeterminate list box removes the entries that every book had and adds the entries of the box, keeping the other entries of each book (the order a book ends up with is that of the set, as in the original). A box whose books agree is checked, so OK writes it again with the same value (no change). Cancel and Escape write nothing. All the writes are one undo step.
- The dialog keeps, for the next one in the same run (`RestorePanelStates`, `RestorePosition`): which groups are collapsed and the scroll position of the panel. A group collapses by its toggle square or a double click on its header.
- A double click on the empty part of a group (not on a box or label) turns the groups into the pages of a tab control (`RegisterPanelToTabToggle`, the page titles are the group titles, hidden groups have no page) and back; the choice is the `Multiple` flag of the setting `TabLayouts` (default off).

## 8. Planned differences
- The group body is transparent over the dialog colour (the Windows tab-body drawing behind a group is not reproduced: `CollapsibleGroupBox.md`).
- The scroll bar is the system one (overlay), so the groups keep their 478 width and the 17 units at the right stay empty; the panel scrolls vertically only.
- The check box is the Windows 10 look from the Buttons pack (C-CTRL-1), 16 x 17 with the 13 unit box at its left, which is what an empty auto-sized `CheckBox` measures in the original.
- The progress dialog of the refresh is not shown (DL.17, P5.26); the dialog opens when the books are read.
- The dialog has no `RestorePosition` on the Mac (the system places a modal window over its owner); the group state and scroll position are kept.
- The list selector is the flyout of C-CTRL-3; the text menus have no search entries (`TextBoxContextMenu.AddSearchLinks`, section 9 of the plan).
