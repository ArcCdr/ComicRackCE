# NavigationOverlay (ComicRack.Engine.Display.Forms/NavigationOverlay.cs)

Mac view: `ComicRack.Mac.Controls.CrNavigationOverlay` with `NavigationLayout` (P5.R3). Decision C-READ-1 (original look).

## 1. Sources
`NavigationOverlay.cs:26-290` (children, sizes), `:300-470` (mouse), `:470-560` (`OnPaint`), `:560-630` (thumbnails), `ComicDisplayControl.cs:2423`, `:3124-3170` (`UpdateNavigationOverlay`, `MoveAnimator`), `SimpleButtonPanel.cs`, `SimpleScrollbarPanel.cs`, `LabelPanel.cs`, `PanelRenderer.cs`.

## 2. Layout
```
overlay W x H: W = max(200, 0.9 x reader width), H = 200 (at least 120)
  glass BlackGlassPanel at opacity 0.7; content area D = overlay minus margins 5, 5, 10, 10
  name label   (W - 80) x 15 at D top left, 1 above;  time label 50 x 15 at D top right (right aligned, "t" format)
  strip frame  D less 15 at the top and 32 at the bottom: GradientFramedBackground (margins 0, 3, 0, 3); pictures in it less 4 at the sides, 2 at the top
  buttons 28 x 28 at D bottom: First, Previous at the left (32 apart), Next, Last at the right; GrayGlassButton opacity 0.9 (1.0 and +0.4 brightness hot), black icon 16
  slider (W - 151) x 24 centred at D bottom, 2 above it: BlackGlassScrollbarBack inflated by -(24 / 2 - 3), knob GrayGlassButton 23 x 23
```
Pages in the strip: the displayed page centred in full strength, neighbours at 0.3 spaced by 6, up to the strip edges; a page without its picture is a 3:4 box with its number (Times 32 pt); with a picture: the picture at the strip height, one unit black frame, page number badge.

## 3. Behaviour
Shown (`UpdateNavigationOverlay`) when there is a page and it was toggled, or `ShowNavigationOverlay` is on and the pointer is over the overlay or, with no button down, in the 500 x 50 zone in the middle of the bottom (top) edge. Slides in over 300 ms after 500 ms, out over 200 ms (sinus rise). A click on the page puts a toggled overlay away. Buttons: first, previous (-1), next (+1), last; slider drag, release goes to the page; a click on a picture goes to its page; dragging beyond a picture scrolls the strip.

## 4. Planned differences
The battery indicator (laptops) is not drawn; the thumbnails come from the thumbnail service (the browser's cache) instead of the page thumbnail pool.
