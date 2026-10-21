# QuestionDialog (cYo.Common.Windows/Forms/QuestionDialog.cs, .Designer.cs)

Mac view: `Controls/AlertWindow` (the window) and `Platform/MacAlert` (the system alert for questions without option boxes), P5.5. Ledger DL.30 and the Remove question (BC.23, DL.06). Decisions C-DLG-1, C-DLG-3, C-DLG-4.

## 1. Sources
`QuestionDialog.cs` (`Ask`, `AskQuestion`: the first line of the text is the question, the rest the description; `OptionText` and `Option2Text` start checked when they begin with "!"; the second box shows only while the first is checked unless independent or exclusive; result flags Cancel, Ok, Option, Option2), `QuestionDialog.Designer.cs` (369 x 198, flow of question, picture, description, two boxes, OK and Cancel), `ComicRack/Program.cs:369-388` (`AskQuestion` with a `HiddenMessageBoxes` flag), `ComicRack/Views/ComicListLibraryBrowser.cs:58-176` (`RemoveBookHandler`), `ComicRack/Config/HiddenMessageBoxes.cs`.

## 2. Layout
Message in bold, description below (at most 350 wide), the boxes (the second indented 32), a row of 80 x 24 buttons at the right: OK (the text the caller gives, "Remove"), Cancel (hidden when `ShowCancel` is false).

## 3. Controls
Texts through TR: `QuestionDialog/btOK`, `btCancel`; the questions are keys of `Messages`: `AskRemoveFromLibrary`, `AskRemoveComics`, `AlsoRemoveFromLibrary`, `MoveBin`, `Remove`, `FailedDeleteBooks`, `NotAskAgain`.

## 7. Behaviour
Remove (`miRemove`, Delete, Cmd+Backspace; Cmd+Delete skips the question like the original's Ctrl+Delete): the library or a list that is neither fixed nor smart asks "Are you sure you want to remove these books from the library?" with the option to move the files to the Trash (checked from `MoveFilesToRecycleBin`); a fixed list and a smart list ask "...from the list?" with the options to remove the books from the library as well (`AlsoRemoveFromLibrary`, `AlsoRemoveFromLibraryFiltered` for a smart list) and, while that is checked, to move the files to the Trash. The answers are saved. A smart list hides the books (filtered ids) unless the library option is checked. A file that cannot be moved keeps its book and shows `FailedDeleteBooks`. A book that leaves the library goes on the black list. "Do not ask again" questions (`HiddenMessageBoxes`) are the system alert with its suppression box; OK with the box checked sets the flag.

## 8. Planned differences
The picture of the covers (`Program.MakeBooksImage`) and the question mark icon of the original are not shown (section 9 of the plan). "Recycle Bin" reads "Trash" in the English default (the translations of `MoveBin` are kept). The original form `DeleteItemDialog` has no caller in the original; its role is the question above.
