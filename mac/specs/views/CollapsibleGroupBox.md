# CollapsibleGroupBox (cYo.Common.Windows/Forms/CollapsibleGroupBox.cs)

Mac view: `ComicRack.Mac.Controls.CrCollapsibleGroup` (P5.T4). Decision C-GRP-1 (original look). Used by the Preferences pages and `MultipleComicBooksDialog`.

## 1. Sources
`CollapsibleGroupBox.cs`: header height 41-62, `HeaderRectangle` 123-145, `ToggleRectange` 146-160, toggle and double click 225-250, `DrawBox` 275-299, toggle images 301-318 (`Resources.SimpleArrowDown` and `SimpleArrowRight`).

## 2. Layout
```
header 24 high, full width, horizontal gradient CollapsibleGroupBoxHeaderStart to CollapsibleGroupBoxHeaderEnd
  toggle square 24 x 24 at the left holding the arrow image (down when expanded, right when collapsed)
  text in bold (header font style), starting after the toggle square, vertically centred, colour CollapsibleGroupBoxHeaderText
content below the header; collapsed: the control is only the header high
body background CollapsibleGroupBoxBack (transparent in light, #202020 in dark)
```

## 3. Behaviour
Mouse up on the toggle square, or a double click on the header, flips collapsed and raises `CollapseClicked`; `CollapsedChanged` fires on every change.

## 5. Icons
`<SimpleArrowDown>`, `<SimpleArrowRight>`.

## 6. Tokens and metrics
Cr.CollapsibleGroupBoxBack, Cr.CollapsibleGroupBoxHeaderStart, Cr.CollapsibleGroupBoxHeaderEnd, Cr.CollapsibleGroupBoxHeaderText. Header 24.

## 8. Planned differences
The Windows visual-style tab body drawn behind the group in light mode is not reproduced; the group is transparent over the dialog colour.
