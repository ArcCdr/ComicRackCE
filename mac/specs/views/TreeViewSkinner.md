# NiceTreeSkin and TreeViewSkinner (cYo.Common.Windows/Forms/NiceTreeSkin.cs, TreeViewSkinner.cs)

Mac view: the `TreeViewItem` control theme in `Themes/Controls/CrControlThemes.axaml` with `StyledSelectionVisual` and `CrIcon` (P5.T4). Decision C-TREE-1 (original look). The library tree (`LibraryTreeSkin`: gauges, list icons) is P5.R1.

## 1. Sources
`TreeViewSkinner.cs:55-110` (row height = font height + 8), `NiceTreeSkin.cs:25-91` (selection with `StyledRenderer`, drop separator 2, arrow images `SimpleArrowDown` and `SimpleArrowRight`, bold selected label), `StyledRenderer.cs:110-233` (selection style).

## 2. Layout
```
row height = one text line of the item font (12) + 8
indent 16 per level; arrow image in a 16 wide column (a leaf keeps the column, the arrow is hidden)
label from the column's right edge + 2, bold when selected
selection: StyledSelectionVisual over the whole row (square, 1 unit frame at alpha 164 and fill 41, 255 when hot and selected, focused or unfocused colour)
```

## 5. Icons
`<SimpleArrowDown>`, `<SimpleArrowRight>`.

## 6. Tokens and metrics
Cr.SelectionFocused, Cr.SelectionUnfocused, Cr.WindowText, Cr.GrayText, Cr.TreeDragSeparator (drop marker, P5.R1). Item font 12 (chrome-guidelines section 3).
