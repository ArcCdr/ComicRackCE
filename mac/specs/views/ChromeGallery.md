# ChromeGallery (Mac only)

Mac view: `ComicRack.Mac.Views.ChromeGalleryWindow`, opened with `--show ChromeGallery` (P5.T3 placeholder, P5.T4 content). It has no WinForms original; it shows every foundation control in its states so that the contact sheet and the owner can judge the chrome. Ledger: `mac-only` is not needed (a developer view, never in a menu).

## 2. Layout
```
window 1000 x 880, Control colour, one scrolling column of sections, each with a bold caption naming its decision (C-TB-1, C-TAB-1, C-SB-1, C-GRP-1, C-SPLIT-1, C-HDR-1, C-TREE-1, C-CTRL-1, C-DLG-2, C-MENU-3, C-FONT-1)
  probe swatches: Highlight and Window, 48 x 24
  toolbar: normal, hot, pressed, checked, disabled, separator, split buttons, text button, search box
  tab bar with three tabs and a toolbar slot; status bar with a spring text, three icon slots and a slider slot
  two group boxes; four sizable containers (top, left hot, right pressed, collapsed); six column headers
  two trees; buttons, check boxes, radio buttons, text boxes, combo box, numeric up/down, slider, tab control, list box
  dialog buttons; a popup menu; a scroll viewer; font samples; one swatch for every theme token of the appearance
```

## 5. Icons
`<FitWidth>`, `<FitHeight>`, `<FitAll>`, `<SinglePage>`, `<TwoPage>`, `<Zoom>`, `<Tools>`, `<Refresh>`, `<Search>`, `<GoNext>`, `<GoPrevious>`, `<SimpleArrowDown>`.

## 6. Tokens
Cr.Control, Cr.Highlight, Cr.Window, Cr.ControlDark, Cr.ControlText, Cr.GrayText, Cr.ButtonBack, Cr.ButtonBorder, Cr.EditFrame, Cr.TabPageBorder, Cr.ToolButtonSelectedBorder, Cr.HeaderSeparator, Cr.SelectionFocused.
