# SplitButton and the toolbar buttons (cYo.Common.Windows/Forms/SplitButton.cs, ToolStrip with the professional renderer)

Mac views: `CrToolBar`, `CrToolButton`, `CrSplitButton`, `CrToolSeparator` (P5.T4). Decision C-TB-1 (original look).

## 1. Sources
`SplitButton.cs`: push part width 14 (line 14), `DrawSplitLine` 113-125, drop-down handling 140-230. Toolbars: `ComicRack/Views/ComicBrowserControl.Designer.cs:976-1392` (25 high, search box 130 x 24), `ComicRack/MainForm.Designer.cs:2593-2615` (reader toolbar in the tab bar), `cYo.Common.Windows/Forms/Theme/Resources/ThemeToolStripProRenderer.cs`, `ComicRack/OptimizedProfessionalColorTable.cs`, colours in chrome-guidelines 2.3.

## 2. Layout
```
toolbar 25 high: top to bottom gradient ToolStripGradientBegin, Middle, End; bottom line ToolStripBorder
button 23 x 22 (16 px image, padding 3), one above and two below it; text buttons add 3 and the text width
separator 6 wide: ToolSeparatorDark at x 2, ToolSeparatorLight at x 3, from 2 to height - 2
split button: button part + 14 wide arrow part (SmallArrowDown); hot or pressed: split line SplitButtonSeparatorLeft and SplitButtonSeparatorRight at the arrow part's left edge
```

## 3. States
Normal transparent; hot ToolButtonSelectedGradient* with ToolButtonSelectedBorder; pressed ToolButtonPressedGradient* with ToolButtonPressedBorder; checked ToolButtonCheckedHighlight with its border; checked and hot ToolButtonSelectedHighlight; disabled image at 0.45 and GrayText.

## 5. Icons
By resource name, e.g. `<FitWidth>`, `<Zoom>`, `<Tools>`; `<SmallArrowDown>` of cYo.Common.Windows for the arrow part.

## 6. Tokens
Cr.ToolToolStripGradientBegin/Middle/End, Cr.ToolToolStripBorder, Cr.ToolButton*, Cr.ToolSeparatorDark/Light, Cr.SplitButtonSeparatorLeft/Right, Cr.ControlText, Cr.GrayText.

## 8. Planned differences
Square button corners (the original's rounded edges depend on the renderer's `RoundedEdges`, not read here); no overflow chevron (toolbars on the Mac are wide enough; P5.R1 revisits).
