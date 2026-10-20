# StatusStrip of the main window (ComicRack/MainForm.Designer.cs:1799-1818)

Mac view: `ComicRack.Mac.Controls.CrStatusBar` and `CrStatusSlot` (P5.T4). Decision C-SB-1 (original look). The segments themselves (SB.01 to SB.05) are filled by P5.R1.

## 1. Sources
`MainForm.Designer.cs:1799-1818`, the status items declared around it; renderer colours `StatusStripGradientBegin`, `StatusStripGradientEnd`, `StatusStripBorder` (chrome-guidelines 2.3).

## 2. Layout
```
24 high: top to bottom gradient StatusStripGradientBegin to StatusStripGradientEnd, top border StatusStripBorder
text segment (spring): takes the remaining width, 4 margin left and right, vertically centred
icon slots: 20 x 19, 3 above and 2 below, sunken border (ButtonShadow on top and left, ButtonHighlight on bottom and right), image centred
slider slot: the size slider, no border
```

## 5. Icons
By resource name in a slot (`<Refresh>`, `<Search>`, `<UpdatePages>`, ...).

## 6. Tokens and metrics
Cr.ToolStatusStripGradientBegin, Cr.ToolStatusStripGradientEnd, Cr.ToolStatusStripBorder, Cr.ButtonShadow, Cr.ButtonHighlight, Cr.ControlText. Status strip 24 (chrome-guidelines section 4).
