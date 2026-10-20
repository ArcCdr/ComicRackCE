# SizableContainer (cYo.Common.Windows/Forms/SizableContainer.cs)

Mac view: `ComicRack.Mac.Controls.CrSizableContainer` (P5.T4). Decision C-SPLIT-1 (original look).

## 1. Sources
`SizableContainer.cs`: grip width 126, `GripRectangle` 368-388, `DrawGrip` 455-507, state and cursor 389-420, `UpdateExpanded` and `AnimateWindow` 754-815, defaults 36 (`EnableAnimation`), 277-290 (`AnimateExpand`, `SlideTime` 100, `HotColor` ControlLight, `PressedColor` Highlight). Instances: `MainForm.Designer.cs:3443-3454` (browser dock, bottom, 250 high), `ComicExplorerView.Designer.cs:35-146` (side panel, 252 wide).

## 2. Layout
```
panel = child + grip strip of 6 on one side (Top, Left, Right, Bottom; the side faces the rest of the window)
extent along the grip axis: expanded value (drag sets it), 0 collapsed (grip width when the grip is kept visible), animated in 100 ms
grip image MozillaGrip (120 x 5), centred in the strip, rotated 180 when expanded, -90 for a left grip, +90 right, +180 bottom, clipped to the strip
fill: Control normally, ControlLight hot, Highlight pressed
```

## 3. Behaviour
Press and drag on the grip resizes (never below the grip plus 20); the cursor is the split cursor over the grip. Collapse and expand slide. Not in this foundation: the double click on the grip and the persistence of the extent (P5.1).

## 5. Icons
`<MozillaGrip>` (cYo.Common.Windows).

## 6. Tokens and metrics
Cr.Control, Cr.ControlLight, Cr.Highlight. Grip 6, slide 100 ms (chrome-guidelines section 4, "Browser dock").
