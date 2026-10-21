# SelectItemDialog (cYo.Common.Windows/Forms/SelectItemDialog.cs, .Designer.cs)

Mac view: `Views/SelectItemWindow` (frame `CrDialogWindow`, `TrSection` `SelectItemDialog`). Ledger DL.31 (the text and list variants are done by P5.7c for the bookmark name; P5.16a adds the check box and the Browse button). Spec by P5.7c.

## 1. Sources
`SelectItemDialog.Designer.cs` (positions), `SelectItemDialog.cs` (`GetName`, `NameTextChanged`).

## 2. Layout
Client 344 x 130, not resizable, title = the caller's caption. lblName (12,24) "Name:" (or the caller's caption and a colon); txtName or cbName (12,40) 320 x 21 (the combo box when the caller gives items, else the text box); chkOption (12,67), btBrowse (244,39) 88 x 23 (both hidden unless the caller asks); btOK (166,94) and btCancel (252,94) 80 x 24, 12 from the right and from the bottom.

## 3. Behaviour
OK is off while the text is empty. OK returns the trimmed text, Cancel and Escape return nothing. The text is selected when the dialog opens. Set Bookmark... of the page list uses it with the caption `Bookmark` (shared text) and the proposed bookmark.

## 4. Planned differences
The check box (`CheckOptionText`) and the Browse button (`BrowseAction`) are not built (P5.16a). The combo box is the stock editable one.
