# TabBar (cYo.Common.Windows/Forms/TabBar.cs)

Mac view: `ComicRack.Mac.Controls.CrTabBar` (P5.T4). Decision C-TAB-1 (original look).

## 1. Sources
`cYo.Common.Windows/Forms/TabBar.cs`: fields 598-608 (padding 2 above, 4 below, `drawBaseLine` true), `LayoutTabs` 1770-1840, `OnPaint` 1410-1470, `DrawTab` 1843-1875, `DrawTabItem` 1280-1320, hit testing 1320-1350 and 1580-1610. Instance: `ComicRack/MainForm.Designer.cs:2577-2589` (`fileTabs`: left indent 8, close image `Close`, 744 x 31, hosts `mainToolStrip`), `ComicRack/Views/MainView.Designer.cs:52-79` (`tabStrip`).

## 2. Layout
```
bar 31 high, full width, background TabBarBack
  y 2..27   tab area (content rect: top padding 2, bottom padding 4)
  y 26..30  base line frame (outline of a rectangle, colour TabPageBorder)
  tabs      from x = 8 (left indent), overlap by 1, width = text + 6 + image + (close box 16 + 4), clamped to 60..250, shrunk in proportion when they do not fit
            selected: y 2, height 26 (content height + 1); others: y 4, height 22
  close box 16 x 16 at (right - 18, top + 2), only on the hot and the selected tab, dimmed until the pointer is over it
  toolbar slot at the right, 25 high (bar height - bottom padding - 1)
```

## 3. Controls
Tab items: text, close box (`CanClose`), optional 16 px image (resource name). Events: selected tab changed, close requested. Not in this foundation: scroll arrows and the drop-down list of overflowing tabs (P5.32a), drag to reorder, tab tooltips.

## 5. Icons
`<Close>` (ComicRack), `<SimpleArrowLeft>`, `<SimpleArrowRight>`, `<SimpleArrowDown>` (arrows, P5.32a).

## 6. Tokens and metrics
Tokens: Cr.TabBarBack (transparent in light, Window in dark), Cr.TabBarSelectedBack, Cr.TabPageBorder (the visual-style border colour of the original, light value assumed per chrome-guidelines 2.5), Cr.TabPageOther (unselected tab fill), Cr.ButtonHoverBack and Cr.ButtonHoverBorder (hot tab), Cr.ControlText. Metrics: chrome-guidelines section 4, row "Book tab bar".

## 8. Planned differences
The unselected and hot tab fills come from Windows 10 visual styles, which no session can read; the values are the assumed ones of chrome-guidelines 2.5 and are confirmed on the contact sheet (C2).
