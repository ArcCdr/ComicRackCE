# QuickRatingDialog (ComicRack/Dialogs/QuickRatingDialog.cs, .Designer.cs)

Mac view: `Views/QuickRatingWindow` (frame `CrDialogWindow`, body `Views/QuickRatingBody.axaml`), P5.5. Ledger DL.18; decisions C-DLG-2.

## 1. Sources
`QuickRatingDialog.Designer.cs` (positions, sizes), `QuickRatingDialog.cs:56-85` (`Show`: title, values, result), `ComicRack/MainForm.cs:112-165` (`RatingEditor.QuickRatingAndReview`: undo marker "Quick Rating", the first book), `ComicRack/MainForm.cs:887-893` (`OnBookClosing`: shown after a book that was read and has no rating when `AutoShowQuickReview`), `ComicRack.Engine/Controls/RatingControl.cs` (the editor).

## 2. Layout
```
form 564 x 304, fixed dialog, centred on the owner, accept = OK, cancel = Cancel
coverThumbnail  (12,12)   170 x 227   front cover of the book
txRating        (12,245)  170 x 21    RatingControl (border, no number, stars centred)
txReview        (188,12)  369 x 254   multi line text
chkShow         (12,277)  "Show when Book read"
btOK (391,272) btCancel (477,272)  80 x 24
```

## 3. Controls
| Control | TR key | English | Mac |
|---|---|---|---|
| form | `QuickRatingDialog/QuickRatingDialog` | Quick Rating | title "Quick Rating - <CaptionWithoutTitle>" |
| `chkShow` | `chkShow` | Show when Book read | `CheckBox` in the footer of the button row |
| `btOK`, `btCancel` | `QuickRatingDialog/btOK`, `btCancel` | OK, Cancel | `CrDialogWindow` buttons |
| `txRating` | | | `CrRatingStars` 170 x 21 |
| `txReview` | | | `TextBox`, `AcceptsReturn`, wrapping |
| `coverThumbnail` | | | `CoverView` fed by `ThumbnailService` (front cover) |

## 5. Icons
`<StarYellow>` (rating points).

## 6. Tokens
`Cr.Window`, `Cr.ControlDark` (frame of the editor), `Cr.RatingRated`, `Cr.RatingUnrated`.

## 7. Behaviour
Opens with the review and rating of the first selected book (the Edit menu's Quick Rating and Review..., Option+Shift+Q; the context menu's item; after a closed book). OK writes `Review`, `Rating` and `AutoShowQuickReview`; Cancel changes nothing. An undo marker "Quick Rating" is set before the dialog.

## 8. Planned differences
The buttons are the frame's (15 from the right, 12 from the bottom: C-DLG-2), so the form is 10 units taller than the original's and the buttons 8 units further left; the check box sits at the left of the button row at x 15.
