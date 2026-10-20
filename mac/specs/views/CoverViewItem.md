# CoverViewItem, thumbnail mode (ComicRack/Controls/CoverViewItem.cs, ComicRack.Engine/Drawing/Thumb*Renderer.cs)

Mac view: `ComicRack.Mac.Views.BookCell` (data and thumbnail requests) on `ComicRack.Mac.Controls.CrThumbnailItem` (the look), `CoverView` (the picture) and `CrBookToolTip` (P5.R2). Decision C-LIST-1 (original look, everything of section 4). The tile mode (`ThumbTileRenderer`) is P5.12, the details mode P5.11a.

## 1. Sources
`CoverViewItem.cs:475-560` (`OnDraw`: the state icons, the rating mode, the options of the renderer), `:386-403` (`MeasureItem`: item size and caption height), `:1060-1100` (`CreateThumbnailLines`, `GetBackThumbnail`), `ThumbnailViewItem.cs:21-358` (border 4, fade-in 300 ms), `ThumbRenderer.cs` (`DrawThumbnail`, `DrawShadow`, `DrawBookmarkV`, `DrawPageCurlOverlay`, `DrawMissingThumbnail`, `DrawPageNumber`, `DrawImageList`), `ThumbIconRenderer.cs` (`Draw`), `RatingRenderer.cs`, `ThumbnailDrawingOptions.cs` (`Default` = 0x5CF), `GraphicsExtensions.cs:199-290` (`DrawShadow`, `CreateShadowBitmap`), `ComicBrowserControl.cs:1250-1260` (Ctrl+wheel), `:3246-3290` (tool tip), `Settings.cs:270-282` (the options).

## 2. Layout (one item, 96 DPI units)
```
item                         cover height H (the item size, 80 to 360), width round(H * 2/3) + 8, height H + 8 + label
  border 4 on every side
  area = item inside the border, minus the label
    cover box = area minus 4 on the right and at the bottom (the shadow)       StarsBelow: the box also gives up the strip and 4
    cover picture = the picture fitted into the box (a "same size" browser: the whole box, a landscape picture cropped to its right part)
    frame: 1 unit black, on the picture's edge and one unit outside it on the right and at the bottom
    shadow: 4 units, black 0.33 fading to nothing, right edge, bottom edge and the three corners that touch them
  label = 3 lines of (item font x scale + 2), scale = clamp(H / 192, 0.7, 1), item font 12; the caption is centred in it, 2 units in from each side
```
The Mac grid is uniform (every item has the box above); the original sizes each item by its cover (section 8).

## 3. Parts, in drawing order
| Part | Drawn by | Rule |
|---|---|---|
| Shadow | `CrThumbnailItem` backdrop | `Cr.Thumb.ShadowBrush` (black alpha 84); linear strips on the right and bottom edge, radial quarters at the corners |
| Empty tile | backdrop | `Cr.Thumb.MissingBackBrush` white over the whole box while there is no picture or it is below 0.95 opacity; text "Loading Thumbnail..." (`TR.Messages["loadingThumbnail"]`) Arial 6 pt in `Cr.Thumb.MissingTextBrush`, centred, 4 units in |
| Caption selection | backdrop | the style of chrome-guidelines 2.4 (hot alpha 92, selected 164, selected and hot or focused 255, fill a quarter, `Cr.SelectionFocused` while the grid has the keyboard focus else `Cr.SelectionUnfocused`), behind the text rectangle plus 2 units, inside the label area; the selected book is the focused one |
| Cover | `CoverView` | fade-in 300 ms (`FadeInThumbnails`) for a cover that arrives later, not for one from the memory cache |
| Frame | overlay | `Cr.Thumb.BorderBrush` |
| Dog-ear | overlay | selected: 0.9 of the shorter side of the visible part, hot: 0.5; the page the book is at (or its first page that is not a cover) shows through the triangle, `PageCurlShadow` then `PageCurl` over it; only when `DogEarThumbnails`, the book is linked, has 2 pages or more and its file is there |
| Page number badge | overlay | black alpha 192, white Arial 7 pt number, at least 20 wide, radius 3, at the top right (the browser does not turn it on) |
| Ribbons | overlay | two flags 16 x 8 at the right edge at current page and last page read (orange, green), a black shadow one unit down and right, black outline, fill fades to half of each channel; none for a fileless entry; a page 0 is at the top |
| State icons | overlay | bottom left, band height = H / 10 clamped 16 to 32, each icon over the one before by 80 %: `UpdateBig` (unsaved changes and the update setting), `OpenMarker`, `LastMarker`, `NewPages1` to `NewPages5Plus`, `FilelessMarker`, `Deleted` (file missing) |
| Rating | overlay | `NumericRatingThumbnails` (default on): tags 128 square images `RatingBlue` (community, in the corner) and `RatingYellow` (user, to its left), height H / 5 clamped 16 to 64, the number with one decimal in Arial 12 pt bold italic `Cr.Thumb.RatingTextBrush`; off and `RatingStarsBelowThumbnails` (default on): five stars in a strip under the cover, the user's `StarYellow` in front of the community's `StarBlue` two units lower, the rated part at alpha 1.0 and the rest at 0.25 (an unrated book shows five faint yellow stars); off and not below: a column of stars over the left edge |
| Caption | `CrThumbnailItem` | `ComicBook.Caption` (`ComicCaptionFormat`), at most 3 lines, item font x scale, `Cr.WindowText` |

## 4. Menus and shortcuts
Cmd+wheel (original Ctrl+wheel) changes the item size by 16 per notch (80 to 360); a pinch on the trackpad scales it (BV.04, mac-only gesture); the status-bar slider is the same value.

## 5. Icons
`<StarYellow>`, `<StarBlue>`, `<RatingYellow>`, `<RatingBlue>`, `<PageCurl>`, `<PageCurlShadow>`, `<OpenMarker>`, `<LastMarker>`, `<FilelessMarker>`, `<UpdateBig>`, `<Deleted>`, `<NewPages1>`, `<NewPages5Plus>` (and `NewPages2` to `NewPages5`).

## 6. Tokens and metrics
`Cr.WindowText`, `Cr.SelectionFocused`, `Cr.SelectionUnfocused`, `Cr.ItemViewMainBack` (the paper), `Cr.Info`, `Cr.InfoText`, `Cr.WindowFrame` (tool tip); the literals of the renderers are `Cr.Thumb.*` in `CrThumbnailLook.axaml`. Item border 4, shadow 4, label lines 3, item padding 1 (grid margin 1, spacing 2).

## 7. Behaviour
- Options (`ThumbnailOptions`, defaults of `Settings.cs`): `ShowToolTips` false, `FadeInThumbnails` true, `DogEarThumbnails` true, `NumericRatingThumbnails` true, `CoverThumbnailsSameSize` false; `RatingStarsBelowThumbnails` true (`EngineConfiguration`). A change draws the cells again.
- Tool tip (`ShowToolTips`): 360 x 120, the system tool tip colours, the cover at the left with its frame, the lines of `ComicTextElements.DefaultComic` (caption without title, title, artists, summary with its tabs removed, size and pages, format) at the right, the first two bold.
- Thumbnails are requested at the item size in device pixels and kept while the size changes within a cache step (`ThumbnailSteps`); the page behind the dog-ear only while the cell is selected or hot.

## 8. Planned differences
- Uniform cells, not one width per cover (`ItemView` lays out items of the width of their cover, the Mac grid keeps the `UniformGridLayout` of the MVP): a landscape cover is small inside its 2:3 box. Section 9 of `full-port.md` has the item for a variable width layout.
- Clicking the stars under a cover to set the rating (`GetRatingFromStrip`) comes with My Rating (P5.5). The open and last markers (`MarkerType`) are filled by P5.2 from the reader state; the renderer draws them already.
- The front cover button ("- 1/2 +") of a selected book with several front covers is not drawn yet (section 9).
- The frame, shadow and ribbons are 1 unit wide on every display; the original draws 1 device pixel at 200 %.
