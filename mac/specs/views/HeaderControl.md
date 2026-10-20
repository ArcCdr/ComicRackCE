# HeaderControl (cYo.Common.Windows/Forms/HeaderControl.cs)

Mac view: `ComicRack.Mac.Controls.CrHeader` (P5.T4). Decision C-HDR-1 (original look). The details list and pages list of the browser draw the same header through `ItemView` (P5.R2 and later).

## 1. Sources
`HeaderControl.cs`: `Draw` 98-160 (fill, frame style by state, drop-down, sort arrow, text), `GetDropDownBounds` 161-166, images `SortUp`, `SortDown`, `SmallArrowDown` (cYo.Common.Windows resources, lines 20-24). Header height of the item view: `ItemView.cs:398-400` (20).

## 2. Layout
```
20 high, fill HeaderBack
frame: one unit, colour HeaderSeparator, selection style alpha by state: Pressed = selected and hot (255), Hot = selected (164), Active and Normal = hot (92); fill alpha a quarter of that
sort arrow: SortDown or SortUp image at the top centre of the text area, opacity 0.7
drop-down part (optional): image width + 4 at the right, a ControlDark line and a ControlLight line at its left (4 from top and bottom), SmallArrowDown centred
text: system font 12, centred, 3 above and below, ellipsis
```

## 3. Behaviour
Click raises `Clicked`; a click on the drop-down part raises `DropDownClicked`. Column resizing, reordering and the column chooser belong to the list views.

## 5. Icons
`<SortUp>`, `<SortDown>`, `<SmallArrowDown>` (all of cYo.Common.Windows: `cYo.Common.Windows:SortDown`).

## 6. Tokens and metrics
Cr.HeaderBack, Cr.HeaderSeparator, Cr.ControlDark, Cr.ControlLight, Cr.ControlText.
