# ComicBookDialog (ComicRack/Dialogs/ComicBookDialog.cs, .Designer.cs)

Mac view: `Views/ComicBookDialog` (frame `CrDialogWindow`), pages `Views/InfoSummaryPage.axaml`, `Views/InfoDetailsPage.axaml`, model `Editor/BookEditorModel`, P5.7a. Ledger DL.01, IN.01, IN.02, IN.08, ME.01 to ME.03, BT.12, BC.04, PC.01, PB.38; decisions C-DLG-2, C-DLG-5, C-CTRL-1, C-CTRL-3, C-INFO-1. P5.7b fills Plot & Notes, Catalog and Custom, P5.7c Pages and Colors; the tab pages exist (empty) so the tab set is the original's.

## 1. Sources
`ComicBookDialog.Designer.cs` (positions, anchors, TR keys), `ComicBookDialog.cs`: constructor 125-244 (lists, spin buttons, number keys, size), `SetCurrentBook` 246-258, `SetComicToEditor` 260-307 (what is shown and enabled for a book), `SetDataToEditor` 324-410, `SaveBook` 458-591, Previous/Next 1063-1081, `Show` 1151-1176 (last tab, colour controls), `ThumbTileRenderer.cs` (the cover tile and the 3D book), `ComicRack.Engine/Drawing/ComicBox3D.cs`, `ComicRack/MainForm.cs:2072-2095` (`ShowInfo`: undo marker "Show Info", multi edit for several books), `EditControlUtility.cs`, `ComicRack/Config/DefaultLists.cs`, `cYo.Common.Windows/Forms/SpinButton.cs`, `TextBoxEx.cs`, `ListSelectorControl.cs`, `LanguageComboBox.cs`, `ComboBoxSkinner.cs`.

## 2. Layout
```
form  client 593 x 517 (min), resizable, centred on the owner, accept = OK, cancel = Cancel; title = Caption of the book
tabControl (8,9) 574 x 468 anchor all; tab pages 566 x 442: Summary, Details, Plot & Notes, Catalog, Custom, Pages, Colors
btPrev (8,483) btNext (92,483) 80 x 24 (hidden for one book)      btOK (329,483) btCancel (415,483) btApply (501,483) 80 x 24, 12 from the right, 10 from the bottom
```
Summary (anchors as the Designer): coverThumbnail (21,19) 520 x 243 TBLR; panel1 line (23,268) 520 x 1; labelType (19,285) 68 x 20 right; lblType (93,272) 160 x 41; labelPages (21,313) 66 x 20; lblPages (93,313) 105 x 20; labelMyRating (219,285); txRating (372,285) 152 x 21; labelCommunityRating (217,313); txCommunityRating (372,312) 152 x 21; whereSeparator (19,360) 520 x 1; labelWhere (19,373) 68 x 17; lblPath (93,373) 431 x 35; btThumbnail (372,347) 152 x 23 (split button); btLinkFile (372,373) 152 x 23; linkLabel (3,416) 560 x 23 centred.
Details: row 1 Series (11,28) 201, Volume (216,28) 57, Number (278,28) 65, Count "of:" (349,28) 55, Format (416,27) 139; row 2 Title, Year, Month, Day, Publisher; row 3 Alternate Series (11,104) 262, Alternate Number (278,104), Alternate Count "of:" (349,104), Imprint (416,103); table at (11,129) 543 x 210 with columns 50 % / 50 % / 12 / 139 and rows 12, 20, 26, 12, 26, 12, 26, 12, 26, 12, 26 (Story Arc, Series Group, Series complete; Writer, Penciller, Age Rating; Inker, Colorist, Manga; Letterer, Cover Artist, Language; Editor, Translator, Black and White); Genre (10,372) 392; Include in Updates (416,371) 139; Tags (11,411) 392; Proposed Values (416,410) 139. The number boxes are 11 narrower than the Designer's width and have the spin buttons (11 wide) 1 to the right of the box; the boxes with a list have the 16 wide selector button.

## 3. Controls
| Control | TR key (section ComicBookDialog) | Mac |
|---|---|---|
| labels | `label*` (the control's name) | `Label` with the translated text, the Windows mnemonic dropped |
| tabs | `tabSummary`, `tabDetails`, `tabPlot`, `tabCatalog`, `tabCustom`, `tabPages`, `tabColors` | `TabItem`; the text is kept as it is ("Plot & Notes": the Win32 tab control draws the ampersand) |
| `btPrev`, `btNext`, `btOK`, `btCancel`, `btApply` | same | Previous and Next in the footer of the button row, the others the frame's buttons |
| `coverThumbnail` | | `CrBookTile` (C-INFO-1): 3D book or flat cover with the text lines of `ComicTextElements` CaptionWithoutTitle, Title, ArtistInfo, Summary, Opened, Added, Released, NoEmptyDates; icons of the book at the bottom |
| `txRating`, `txCommunityRating` | | `CrRatingStars` 152 x 21 |
| `txSeries`, `txTitle`, `txAlternateSeries`, `txStoryArc`, `txSeriesGroup`, credits, `txGenre`, `txTags` | | `AutoCompleteBox` over the values of the library (`EditorValueLists`); the credits, genre and tags have the list selector button |
| `txVolume`, `txNumber`, `txCount`, `txYear`, `txMonth`, `txDay`, `txAlternateNumber`, `txAlternateCount` | | `TextBox` + `CrSpinButton`; Volume, Count, Year, Month, Day and Alternate Count take digits, point and comma only |
| `cbFormat`, `cbPublisher`, `cbImprint`, `cbAgeRating` | | `CrValueComboBox`, editable, with the pictures of the icon packages |
| `cbSeriesComplete`, `cbManga`, `cbBlackAndWhite`, `cbEnableProposed`, `cbEnableDynamicUpdate` | | `CrValueComboBox` as a list (Yes/No, Manga with "Yes (Right to Left)") |
| `cbLanguage` | | `CrValueComboBox` as a list: no language, the languages the library uses, a line, every language |
| `btThumbnail`, `miResetThumbnail` | `btThumbnail`, `miResetThumbnail` | `SplitButton` with a `MenuFlyout`: Reset, a line, the recent picture files |
| `btLinkFile`, `linkLabel` | `btLinkFile` | `Button`; `TextBlock` with class `cr-link` that opens the address |

## 5. Icons
`Route` (the list selector button), the icon packages (`Publishers`, `AgeRatings`, `Formats`, `Special`) of `Resources/Icons`.

## 6. Tokens
`Cr.Control`, `Cr.ControlDark`, `Cr.ComicBookPanelBack`, `Cr.ComicBookWhereSeparator`, `Cr.ComicBookLink`, `Cr.ThumbTileEmboss`, `Cr.ThumbTileTitle`, `Cr.ThumbTileBody`, `Cr.Thumb.*` (shadow, frame), `Cr.ButtonBack`, `Cr.ButtonBorder`, `Cr.ButtonHoverBack`, `Cr.ButtonPressedBack`.

## 7. Behaviour
- `ShowInfo` (Edit > Info..., Cmd+I; the context menu; the page menu of the reader; the Tools menu): the first selected book, the shown list as its Previous and Next books. Several selected books open the multi edit dialog (P5.8). An undo marker "Show Info" is set first, so the changes of the dialog are one undo step.
- The dialog edits the library's own book. Nothing is written while fields change. OK, Apply, Previous and Next write every field (`SaveBook`); Cancel and Escape write nothing. Apply also shows the book again as the book holds it (`RefreshInfoFromFile`). Previous and Next select the book in the browser (`IComicBrowser.SelectComic`) and keep the box that was being edited focused; the mouse wheel over the cover does the same.
- Per book (`SetComicToEditor`): type, pages and where rows for a book with a file; thumbnail and link buttons for a book without a file; Details and Plot & Notes are disabled when the book cannot be edited (`UpdateComicFiles`, in the container, `EditMode`); the Pages and Colors tabs only for a book with a file; the Catalog tab unless `CatalogOnlyForFileless` hides it for a book with a file; the Custom tab with `ShowCustomBookFields`; "Proposed Values" only for a book with a file; "Include in Updates" for a book from a dynamic source.
- Proposed values (`EnableProposed`): the empty Series, Title, Number, Count, Year, Volume and Format boxes show the proposed value as a prompt and take it when they get the focus (`TextBoxEx`).
- Spin buttons: the number in the box, or the prompt, plus or minus 1; an empty box starts at 1 (Count and Alternate Count at 1 from 0 up, Year at this year, Month at this month from 1 to 12, Day at this month from 1 to 31 as the original has it).
- Cover: 3D book of the Core's `ComicBox3D` when `InformationCover3D` (default on) else the flat cover; a click flips it and the setting follows. Custom thumbnails: Thumbnail... opens a picture, the menu resets it or takes a recent file; only for a book without a file.
- Link to File...: the book of the chosen file with the fields of this book; refuses a file the library has ("This file is already used in the Library"); OK writes the file path.
- The last tab chosen and the size above the minimum are kept (`lastActivePage`, `SafeSize` saved by OK and Apply).

## 8. Planned differences
- The buttons are 6 below the tabs and 10 from the bottom as the Designer has them; the frame's margins are set by `CrDialogWindow.ButtonBarMargin`.
- The list selector is a flyout of 308 x 278 that cannot be resized (the original's popup grows to twice its size) and has no search links; it keeps the three views (Lists, Check, Text) and the last view.
- Drag and drop of a picture on the cover and of a file on the dialog (the Link to File shortcut), the Editor script button and the page and colour controls of the Pages and Colors tabs are not in P5.7a (`full-port.md` section 9).
- A flat cover has the frame and the shadow but not the ribbons, the state icons and the dog-ear of a grid cover.
- macOS: Cmd+Z and Cmd+Y belong to the text fields while the dialog is open (the Edit menu's Undo, Redo and Info are off then).
