# Chrome guidelines: making the Mac app look like ComicRack

> **Binding by R9 (b) / D9** (`mac/plans/full-port.md` §1.1 and §2.11, `MACOS_NATIVE_PATH.md` §0). The Mac app keeps ComicRack's own chrome (colours, toolbars, icons, browser and reader look, dialog layouts, status bar, font proportions) and uses macOS natively only where the table in §7 says so. When in doubt, the original look wins; every native-over-original choice is a row in §7.
> **Facts below were extracted from the original source on 2026-10-20** (planning session). Paths are relative to the repository root; line numbers refer to the base commit `7fd9fd3`. Values marked *assumed* could not be read from this repository (they come from Windows itself) and are confirmed or corrected by P5.T1.

Contents: 1 sources and references · 2 colour tokens · 3 fonts · 4 metrics · 5 icons and images · 6 shortcut rule · 7 chrome decisions (control by control) · 8 view specs · 9 visual parity check procedure.

---

## 1. Sources of truth and reference images

- **Themes:** the original has two themes, `Default` (the Windows system colours) and an experimental `Dark` (`cYo.Common.Windows/Forms/Theme/Themes.cs:5-9`; switched only by the command line `-dark` / `-theme`, `ComicRack/Config/ExtendedSettings.cs:453-474`; marked "EXPERIMENTAL" in `ComicRack/Output/ComicRack.ini:28-29`). No Preferences option, no "follow system".
- **Renderers:** menus and toolstrips use `ThemeToolStripProRenderer` (`cYo.Common.Windows/Forms/Theme/Resources/ThemeToolStripProRenderer.cs`) with `OptimizedProfessionalColorTable` in light mode (`ComicRack/OptimizedProfessionalColorTable.cs:8-12`, `ComicRack/Program.cs:844-867`) and `ProfessionalColorTableEx` with the dark colours in dark mode. Lists, trees, thumbnails and headers draw selection with `StyledRenderer` (`cYo.Common.Windows/Forms/StyledRenderer.cs:110-233`). Scrollbars, buttons and tabs are native Windows controls (dark mode themes them).
- **Custom-drawn pieces:** `ItemView` (`cYo.Common.Windows/Forms/ItemView.cs`), `HeaderControl`, `TabBar`, `SizableContainer`, `CollapsibleGroupBox`, `CaptionControl`, `SplitButton`, `NiceTreeSkin`/`TreeViewSkinner` (all in `cYo.Common.Windows/Forms/`), `ThumbRenderer`, `ThumbTileRenderer`, `ThumbIconRenderer`, `RatingRenderer` (`ComicRack.Engine/Drawing/`), `CoverViewItem` (`ComicRack/Controls/CoverViewItem.cs`), `LibraryTreeSkin` (`ComicRack/Controls/LibraryTreeSkin.cs`), the reader `ComicDisplayControl`, `NavigationOverlay`, `PanelRenderer`, `TextOverlay` (`ComicRack.Engine.Display.Forms/`).
- **Reference images:** the repository contains **no screenshots** of the app (the README hero image is a remote GitHub asset, `README.md:25`; `ComicRack/Output/Help/ComicRack Introduction.djvu` has 19 illustrated pages but needs a DjVu decoder, deferred by D4). No session has a Windows machine. So the reference for a view is, in this order:
  1. an owner-supplied screenshot in `mac/fixtures/owner/reference/<View>.png` (optional request written by P5.T3 into `OWNER-ACTIONS.md`, checkpoint C1/C2);
  2. a screenshot from the optional Windows CI job (P5.T5), stored by the owner in `mac/fixtures/owner/reference/ci/`;
  3. the README hero image, downloaded by P5.T3 into `mac/artifacts/chrome/reference/readme-hero.png` (not committed). It turned out to be a 597 x 299 brand banner (a painted landscape with the title), not a screenshot, so it only shows at the top of `sheet.html` and gives no layout reference;
  4. otherwise the **view spec** (§8) plus the original source: layout, sizes, tokens and icons rendered as a reference card on the contact sheet (§9).

## 2. Colour tokens

The Mac theme is one Avalonia resource dictionary per variant, `mac/ComicRack.Mac/Themes/ComicRackTheme.Light.axaml` and `.Dark.axaml` (P5.T1), with exactly the token names below (`Cr.<Token>`). Light = the original `Default` theme on Windows 10 system colours; Dark = the original `Dark` theme. The app follows the macOS appearance (decision C-APP-1). A test (P5.T1) parses the original source files named here and fails if a token value differs, so an upstream colour change is caught by the sync harness.

### 2.1 System colours (`cYo.Common/Drawing/ExtendedColors/KnownColorTableEx.cs:33-71`; light = the Windows 10 defaults written in that file's comments, dark = the array the file uses)

| Token | Light | Dark | Token | Light | Dark |
|---|---|---|---|---|---|
| Control / ButtonFace | #F0F0F0 | #202020 | Window | #FFFFFF | #323232 |
| ControlDark / ButtonShadow | #A0A0A0 | #4A4A4A / #464646 | WindowText | #000000 | #F0F0F0 |
| ControlDarkDark | #696969 | #5A5A5A | Highlight | #0078D7 | #2864B4 |
| ControlLight | #E3E3E3 | #2E2E2E | HighlightText | #FFFFFF | #FFFFFF |
| ControlLightLight | #FFFFFF | #1F1F1F | GrayText | #6D6D6D | #969696 |
| ControlText | #000000 | #FFFFFF | HotTrack | #0066CC | #2D5FAF |
| Menu / MenuBar | #F0F0F0 | #373737 | MenuHighlight | #3399FF | #2A80D2 |
| MenuText | #000000 | #F0F0F0 | ScrollBar | #C8C8C8 | #505050 |
| AppWorkspace | #ABABAB | #3C3C3C | InactiveCaptionText | #000000 | #BEBEBE |
| ActiveBorder | #B4B4B4 | #464646 | Info / InfoText | #FFFFE1 / #000000 | #50503C / #BEBEBE |
| ButtonHighlight | #FFFFFF | #101010 | WindowFrame | #646464 | #282828 |
| WhiteSmoke (remapped in dark, `Theme/ThemeManager.cs:76-88`) | #F5F5F5 | #303030 | | | |

### 2.2 App tokens (light `cYo.Common.Windows/Forms/Theme/Internal/ThemeColorTable.cs:20-136`; dark overrides `.../Theme/DarkMode/DarkThemeColorTable.cs:17-127`; an empty dark cell = the light token resolved through the dark system colours)

| Token | Light | Dark | Used by |
|---|---|---|---|
| ItemViewMainBack | Window #FFFFFF | Control #202020 | browser background |
| ItemViewGroupText | DarkBlue #00008B | LightSkyBlue #87CEFA | group headers |
| ItemViewGroupSeparator | ControlDark | InactiveCaptionText #BEBEBE | group header lines |
| ItemViewMarker | Black | White | drop marker |
| DetailRowHighlight | LightGray #D3D3D3 (drawn at alpha 96, `ThemeColors.cs:101`) | #484848 | details zebra rows |
| ThumbnailViewItemBack | LightGray | #484848 | tile background |
| ThumbnailViewItemBorder | ControlDark at alpha 48 | InactiveCaptionText at alpha 64 | tile border |
| SelectionFocused (ThumbRendererSelectionBack, StyledSelectionFocused) | Highlight | Highlight | selection in a focused view |
| SelectionUnfocused (StyledSelectionBack) | Gray #808080 | Gray | selection in an unfocused view |
| ThumbTileEmboss / Title / Body | White / Black / Gray | #303030 / White / LightGray | tile text |
| CollapsibleGroupBoxBack | Transparent | #202020 | preference groups |
| CollapsibleGroupBoxHeader | gradient Control → ControlLight | Control → ControlDark | preference group headers |
| FavViewBack, SmallPreviewPageViewerBack | Window | SidePanel #191919 | favorites, small preview |
| SmallPreviewPageViewerText | LightGray | #303030 | "Nothing Selected" |
| GaugeTotal / GaugeUnread / GaugeNew (white text) | Green #008000 / Orange #FFA500 / Red #FF0000 | #00C000 / Orange / Red | library tree counters |
| TabBarBack | Transparent | Window #323232 | tab bars |
| TabBarDefaultBorder | AppWorkspace | Black | tab bars |
| TabBarSelectedBack | ControlLightLight | #323232 | selected tab |
| CaptionBack | White | Black | Quick Open caption |
| HeaderBack | White | Control #202020 | column headers |
| HeaderSeparator | ControlDark | #636363 | column headers |
| RatingUnrated / RatingRated | LightGray / ControlText | GrayText / ControlText | rating stars text mode |
| StackFill / StackBorder | Control / Black | LightGray / Black | stacked covers |
| SmartQueryKeyword / Qualifier / Negation / String / Exception | Green / Blue / DarkRed / Red / LightGray | #FAC600 / #4CA3FF / Red / #FF7D7D / #7D1F1F | smart-list query editor |
| ComicBookLink | SteelBlue #4682B4 | LightBlue #ADD8E6 | search links |
| PrefsOverlayPanelBack | WhiteSmoke | #303030 | Preferences > Reader > Overlays |
| VistaColor | #99DEFD | #99DEFD | caption frame |
| ReaderBackground (default) | WhiteSmoke (`ComicRack/Config/DisplayWorkspace.cs:252-260`) | WhiteSmoke remapped #303030 | reader |

Dark-only colours (`cYo.Common.Windows/Forms/Theme/DarkMode/Resources/DarkColors.cs:23-163`): SidePanel #191919, Content #202020, Window #323232, ListBack #282828, EditBack #2E2E2E (hover #222222, focused #1A1A1A), Button #323232 (pressed #666666, hover #464646, border #9B9B9B), CheckBoxAccent #60CDFF, borders #101010 / #333333 / #5D5D5D / #9B9B9B, SelectionHighlight #344356, Focus #2864B4.

Mac token names (P5.T1): `Cr.<Token>` is the name in the tables above; the system colours keep their `SystemColors` names (`Cr.Control`, `Cr.Window`, ... all 32 of `KnownColorTableEx`); an app token that merges several original properties is checked against all of them (`SelectionFocused` = `StyledSelectionFocusedBack` and `ThumbRendererSelectionBack`; `FavViewBack` = the two `FavViewBack` properties); the others are `ThemeColorTable` properties with the class prefix dropped (`GaugeTotal` = `LibraryTreeTotalBookCountBack`, `TreeDragSeparator` = `NiceTreeSkinDragSeparator`, `MagnifySetupBack`, `PrefsServerEditBack`). Control tokens (`Button*`, `CheckBox*`, `Edit*`, `TrackBar*`, `TabPage*`) take the light values of §2.5 (assumed) and the dark values of `DarkColors.cs` where it has one; the dark values without an original source are marked in the dictionary. The dark-only colours exist in the dark dictionary only. Not mapped: `PreferencesLabelOverlays` (the dark value `ThemeColors.Lossboro` depends on class initialisation order; P5.22c decides).

### 2.3 Toolbar and menu-strip colours

- **Dark**: `Program.cs:844-847` gives the dark theme a plain `ThemeToolStripProRenderer()`, which builds a `ProfessionalColorTableEx` (`cYo.Common/Drawing/ExtendedColors/ProfessionalColorTableEx.cs`: the same formulas as the stock table, started from the dark system colours of §2.1, without the Aero exception). Item text and arrows are white (`ThemeToolStripProRenderer.cs:22-86`). `DarkProfessionalColors.cs` (MenuItemPressed #606060, MenuStrip #1F1F1F, StatusStrip #323232, ...) **is not used by any code**; the dark column below is therefore the computed one, and `ThemeTokenTests.The_dark_professional_color_table_of_the_original_is_still_unused` fails when upstream starts using that class.
- **Light** (`ComicRack/Program.cs:853-866`, `ComicRack/OptimizedProfessionalColorTable.cs`): the stock .NET `ProfessionalColorTable` on Windows 10, whose theme file is `aero.msstyles`: `InitSystemColors` and `InitCommonColors` applied to the §2.1 light colours, plus the Aero exception (the menu item and check background use the "selected highlight" blend) and the three changes of `OptimizedProfessionalColorTable` (MenuStrip gradient end = begin, MenuItemSelected and ButtonSelected gradient end = begin at alpha 128). **Computed by P5.T1** (`mac/ComicRack.Mac/Themes/ProfessionalColors.cs`, a port of the dotnet/winforms formulas, MIT; the tokens are `Cr.Tool<Property>`; values `#RRGGBB`, `#AARRGGBB` where the alpha is not 255):

| Property | Light | Dark |
|---|---|---|
| ButtonSelectedHighlight | #B5D7F3 | #2E3F56 |
| ButtonSelectedHighlightBorder | #0078D7 | #2864B4 |
| ButtonPressedHighlight | #7EBAEA | #2C4A73 |
| ButtonPressedHighlightBorder | #0078D7 | #2864B4 |
| ButtonCheckedHighlight | #B5D7F3 | #2E3F56 |
| ButtonCheckedHighlightBorder | #0078D7 | #2864B4 |
| ButtonPressedBorder | #0078D7 | #2864B4 |
| ButtonSelectedBorder | #0078D7 | #2864B4 |
| ButtonCheckedGradientBegin | #00000000 | #00000000 |
| ButtonCheckedGradientMiddle | #00000000 | #00000000 |
| ButtonCheckedGradientEnd | #00000000 | #00000000 |
| ButtonSelectedGradientBegin | #B3D7F3 | #2F4159 |
| ButtonSelectedGradientMiddle | #B3D7F3 | #2F4159 |
| ButtonSelectedGradientEnd | #80B3D7F3 | #2F4159 |
| ButtonPressedGradientBegin | #80BCEB | #2D4B73 |
| ButtonPressedGradientMiddle | #80BCEB | #2D4B73 |
| ButtonPressedGradientEnd | #80BCEB | #2D4B73 |
| CheckBackground | #B5D7F3 | #2864B4 |
| CheckSelectedBackground | #80BCEB | #2D4B73 |
| CheckPressedBackground | #80BCEB | #2D4B73 |
| GripDark | #B8B8B8 | #414141 |
| GripLight | #FFFFFF | #323232 |
| ImageMarginGradientBegin | #FCFCFC | #2E2E2E |
| ImageMarginGradientMiddle | #F8F8F8 | #292929 |
| ImageMarginGradientEnd | #F0F0F0 | #202020 |
| ImageMarginRevealedGradientBegin | #F9F9F9 | #2B2B2B |
| ImageMarginRevealedGradientMiddle | #F5F5F5 | #252525 |
| ImageMarginRevealedGradientEnd | #F2F2F2 | #222222 |
| MenuStripGradientBegin | #F0F0F0 | #202020 |
| MenuStripGradientEnd | #F0F0F0 | #2E2E2E |
| MenuItemSelected | #B5D7F3 | #323232 |
| MenuItemBorder | #0078D7 | #2864B4 |
| MenuBorder | #808080 | #6B6B6B |
| MenuItemSelectedGradientBegin | #B3D7F3 | #2F4159 |
| MenuItemSelectedGradientEnd | #80B3D7F3 | #2F4159 |
| MenuItemPressedGradientBegin | #FCFCFC | #2E2E2E |
| MenuItemPressedGradientMiddle | #F5F5F5 | #252525 |
| MenuItemPressedGradientEnd | #F8F8F8 | #292929 |
| RaftingContainerGradientBegin | #F0F0F0 | #202020 |
| RaftingContainerGradientEnd | #FCFCFC | #2E2E2E |
| SeparatorDark | #BDBDBD | #404040 |
| SeparatorLight | #FFFFFF | #101010 |
| StatusStripBorder | #FFFFFF | #101010 |
| StatusStripGradientBegin | #F0F0F0 | #202020 |
| StatusStripGradientEnd | #FCFCFC | #2E2E2E |
| ToolStripBorder | #F2F2F2 | #232323 |
| ToolStripDropDownBackground | #FDFDFD | #2F2F2F |
| ToolStripGradientBegin | #FCFCFC | #2E2E2E |
| ToolStripGradientMiddle | #F8F8F8 | #292929 |
| ToolStripGradientEnd | #F0F0F0 | #202020 |
| ToolStripContentPanelGradientBegin | #F0F0F0 | #202020 |
| ToolStripContentPanelGradientEnd | #FCFCFC | #2E2E2E |
| ToolStripPanelGradientBegin | #F0F0F0 | #202020 |
| ToolStripPanelGradientEnd | #FCFCFC | #2E2E2E |
| OverflowButtonGradientBegin | #F5F5F5 | #252525 |
| OverflowButtonGradientMiddle | #F2F2F2 | #222222 |
| OverflowButtonGradientEnd | #A0A0A0 | #464646 |

- `-ftcs` tan scheme (`cYo.Common.Windows/Forms/TanColorTable.cs:676-710`): not ported (Windows XP fallback; deviation `native`, never reachable on macOS).

### 2.4 Selection style (`StyledRenderer`, Windows 8+ "Windows8" style, `StyledRenderer.cs:110-233`)

Square corners, 1 px frame, no gradient. Frame alpha: hot 92, selected 164, selected+hot 255; fill alpha = 0.25 × frame alpha (selected fill about 41). Colour = `SelectionFocused` when the view has focus, else `SelectionUnfocused`. A focused-only item gets the frame without fill. Lists, trees, thumbnails, tiles, details rows and column headers (hot/pressed) use it.

### 2.5 Native Windows controls in light mode (*assumed*, Windows 10 visual styles; confirm with an owner or CI reference)

Button: fill #E1E1E1, border #ADADAD; hover fill #E5F1FB, border #0078D7; pressed fill #CCE4F7, border #005499; default button border #0078D7. CheckBox/RadioButton: 13 px box, border #333333, check glyph #000000; hover border #0078D7. ComboBox/TextBox: border #7A7A7A, hover #171717, focus #0078D7. TrackBar: track #E7EAEA with border #D6D6D6, thumb #007AD9. Tab pages: native Windows tab look (selected tab white, others #F0F0F0, border #D9D9D9).

## 3. Fonts (decision C-FONT-1)

| Original | Where | Mac |
|---|---|---|
| Microsoft Sans Serif 8.25 pt (net48 default control font; `AutoScaleMode` mostly Font, MainForm `None`) | dialogs, labels, buttons, menus in toolstrips | system font (SF Pro) **11 px** (8.25 pt × 96/72) |
| Microsoft Sans Serif 6.75 pt bold (141 uses: field captions in the Info dialog and others) | small bold captions | system font bold **9 px**; if the contact sheet shows it unreadable, 10 px (recorded in §7) |
| SystemFonts.IconTitleFont = Segoe UI 9 pt (`ComicRack/Views/ComicBrowserControl.cs:859`, `ComicRack/Controls/PagesView.cs:269`, `ComicRack/Views/ComicListLibraryBrowser.cs:273`) | item views, trees, tiles | system font **12 px** |
| Thumbnail caption: thumbHeight/192 clamped 0.7 to 1, later lines 0.8 × (`CoverViewItem.cs:397-398, 1071, 1099-1102`) | thumbnail text | same formula on 12 px |
| Group header text 1.15 × font (`ItemView.cs:3175-3231`) | group headers | 13.8 px |
| Tree counters 0.75 × font (`LibraryTreeSkin.cs:43-134`) | gauges | 9 px |
| SmallCaptionFont | Quick Open caption | system font 11 px bold |
| Segoe UI 12 pt (`MainForm.cs:4366-4376`) | reader messages and overlays | system font 16 px |
| Arial 6 / 7 / 12 bold italic; "Times" 32 | "Loading Thumbnail...", page-number badge, rating tag; navigation page numbers | Arial and Times (installed on macOS), same sizes in px (pt × 4/3) |

DPI: the original scales everything by LOGPIXELS/96, including bitmaps (`cYo.Common.Windows/Forms/FormUtility.cs:80-99, 846-859`). On macOS one Avalonia DIP = one 96-DPI pixel, so every pixel size in this file is used as DIPs unchanged.

## 4. Metrics (pixels at 96 DPI = Avalonia DIPs)

| Element | Value | Source |
|---|---|---|
| Main menu strip | 24 (on macOS: the menu bar, C-MENU-1) | `MainForm.Designer.cs:436-445` |
| Book tab bar (`fileTabs`) | 31 high, left indent 8, tab height 24 when it hosts a toolstrip, min tab width 60, max 250, top padding 2, bottom 4, base line | `MainForm.Designer.cs:2577-2589`, `TabBar.cs:258, 487-495, 720-790` |
| Reader toolbar | in the tab bar, right-aligned, transparent, no grip; groups: Prev, Next | Page Layout, Fit, Zoom ("100 %"), Rotate ("0°") | Magnifier, Full Screen | Tools | `MainForm.Designer.cs:2593-2615` |
| Browser dock (`mainViewContainer`) | docked bottom, 250 high, grip 6 (MozillaGrip, hot ControlLight, pressed Highlight) | `MainForm.Designer.cs:3443-3454`, `SizableContainer.cs:126, 290-306, 455-507` |
| Browser tab strip (`MainView`) | 25 high, docking split button at the right | `MainView.Designer.cs:52-79` |
| Side panel | left, 252 wide, grip right; small preview 207 high at the bottom with a 25 px toolbar | `ComicExplorerView.Designer.cs:35-146` |
| Browser toolbar | 25 high; quick search box 130 × 24 | `ComicBrowserControl.Designer.cs:976-1392` |
| Open-stack bar, display-option bar | 39 high each | same |
| Search browser | 160 high, grip at the bottom, three columns with type combo boxes | same |
| Info panel | 162 high, bottom | `ComicExplorerView.Designer.cs` |
| Status strip | 24 high; activity icons 20 × 19 with sunken borders | `MainForm.Designer.cs:1799-1818` |
| ItemView | column header 20, item padding 1, thumbnail 128 × 128, tile 192 × 96, row 16, group header 40, column offset 8, marker 2 | `ItemView.cs:398-400, 736-964` |
| Thumbnail | shadow 4 px blur black 0.33; border 1 px black; aspect 2:3; caption height 45; stack cover inset 8, back covers rotated −4° and +2° | `ThumbRenderer.cs:32, 517-598, 651-654`, `ThumbIconRenderer.cs:17-47` |
| Badges | page-number badge Arial 7, min width 20, radius 3, black alpha 192, white text; bookmark ribbons 16 × 8 (Orange, Green, Red, Blue gradient to half); overlay height H/10 (16 to 32); tag H/5 (16 to 64); state icons bottom-left, 0.8 overlap | `ThumbRenderer.cs:18-40, 353-472, 687-712` |
| Dog-ear | page curl at 0.5 width on hover, 0.9 on selection (PageCurl.png, PageCurlShadow.png 500 px) | `ThumbRenderer.cs:634-644, 787-846` |
| Ratings | rated part alpha 1.0, rest 0.25; StarYellow (user), StarBlue (community) 32 px; tag images 128 px | `RatingRenderer.cs:113-199`, `MainForm.cs:965-968` |
| Tiles | content inset 4, thumbnail at most half the width, 4 px gap; selected uses the selection style; unselected fill ThumbnailViewItemBack alpha 128 | `ThumbTileRenderer.cs:54-170` |
| Tree | row height = font height + 8; selected label bold; arrow glyph images; drop separator 2 | `TreeViewSkinner.cs:71`, `NiceTreeSkin.cs:25-91` |
| Column header | fill HeaderBack, separator StyledRectangle 1 px; sort arrow top centre at 0.7 opacity; text padding 2 × 3 | `HeaderControl.cs:112-160` |
| Collapsible group box | header 24, bold, horizontal gradient | `CollapsibleGroupBox.cs:41, 62, 275-299` |
| Split button | push part 14 wide | `SplitButton.cs:14` |
| Quick Open card | 616 × 289 (min 300 × 250), single border, caption "Quick Open", 128 px thumbnails | `MainForm.Designer.cs:2551-2575` |
| Reader | padding 16; magnifier 400 × 300; page shadow 1 % of the smaller page side, opacity 0.6; blind-out black alpha 192 | `MainForm.cs:4366-4376`, `ComicDisplayControl.cs:244, 1331-1342` |
| Reader overlays | page info 150 × 25 top right (fade 100/2000/100 ms); visible part 200 × 200 bottom right; loading 200 × 30 centre; message 300 × 30 centre; navigation 500 × 200 bottom; glass panels 9-slice, margins 5,5,10,10, white text | `ComicDisplayControl.cs:206-214, 1086-1168`, `PanelRenderer.cs:30-34` |
| Navigation overlay | four 28 px GrayGlassButtons (first, previous, next, last), slider on BlackGlassScrollbarBack with a 32 px knob, title and clock 9 pt, battery indicator, page thumbnail strip, page numbers Times 32 | `NavigationOverlay.cs:30-50, 212-280, 614-616` |

## 5. Icons and images (decision C-ICON-1)

| Folder | Content | Use on macOS |
|---|---|---|
| `ComicRack/Resources/` | 183 PNG, 17 GIF, 1 ICO: 167 at 16 × 16 (menus, toolbars), 20 at 32 × 32 (preference tabs, stars, markers), NumberFrame 40 × 16, RatingYellow/RatingBlue/RedExclamation 128, ComicRackApp256, Splash 619 × 317 | toolbar, tab, status and dialog icons; About/splash |
| `ComicRack/Resources/Dark/` | 29 PNG with the same names (dark variants, swapped by `ComicRack/Properties/ResourceManagerEx.cs:19-69`) | used in dark appearance, same name lookup |
| `cYo.Common.Windows/Resources/` (+ `Dark/`) | 8 PNG + 1 GIF (+6): sort and arrow glyphs 16 px, 6 × 5 arrows, MozillaGrip | headers, tree arrows, grips |
| `ComicRack.Engine/Resources/` | 11 PNG, 2 JPG: NewPages*, PageCurl, PageCurlShadow, ErrorPage, Unknown | thumbnails |
| `ComicRack.Engine.Display.Forms/Resources/` | 14 PNG: glass panels and buttons, Magnifier 650 × 400, TouchPad 256, navigation arrows | reader overlays |
| `ComicRack/Output/Resources/Icons/` | 735 PNG + 15 JPG (publisher logos, formats, age ratings, special) | info panel, tiles (already shipped as payload by P2.6) |
| `ComicRack/Output/Resources/Textures/` | 14 backgrounds, 4 papers | reader background and paper (P2.6, P5.23c) |
| `ComicRack/Icons/icon1-4.ico`, `ComicRack/Resources/ComicRackAppSmall.ico` | app icon (icon1 has a 256 px PNG frame), window/tray icon 16/32 | `.icns` (P6.1), status item (P5.33) |

Rules:
- **Never copy an original image into a mac/ source folder and never edit one.** P5.T2 links them at build time (`ComicRack.Mac.csproj`: `<AvaloniaResource Include="..\..\ComicRack\Resources\**" Link="Assets\Original\ComicRack\Resources\%(RecursiveDir)%(Filename)%(Extension)" />`, the same for the other three folders and `ComicRack/Icons/*.ico`; the asset path is `avares://ComicRack.Mac/Assets/Original/<repository path>`) and generates `mac/ComicRack.Mac/Assets/icon-map.json` with `mac/tools/icon-map/icon-map.py` (per project: resource name from `Properties/Resources.resx` -> file, and the dark variant) so code asks for `Icons.Get("FitWidth")`, the same name the WinForms code uses (`Resources.FitWidth`). A name in two projects (`GoLast` in the app and the reader project) resolves to the main app; `Icons.Get("ComicRack.Engine.Display.Forms:GoLast")` names the project. `Icons.CreateImage(name)` gives an `Image` of the picture's own size in units with high-quality filtering.
- Magenta (#FF00FF) is the transparent colour of toolbar images (`ImageTransparentColor`); the loader makes it transparent for GIF/BMP-style sources.
- There are no high-DPI variants; on Retina the 16 px images are scaled ×2 with high-quality filtering, the same as the original on a 200 % Windows display. Replacements drawn for macOS are not allowed (they would be new chrome).
- Dark appearance uses `Dark/<name>` when it exists, else the light image.
- An icon mentioned in a ledger row or a view spec is named by its resource name (`<FitWidth>`).

## 6. Shortcut rule (DD20; the ledger holds every value)

Ctrl → Cmd, Alt → Option, Shift and function keys unchanged, single keys of the reader unchanged. When the mapped chord is reserved by macOS (Cmd+Tab, Cmd+Space, Cmd+Shift+3/4/5, Cmd+Shift+Q, Option+Cmd+Shift+Q, Option+Cmd+Esc, Option+Cmd+D, Ctrl+Cmd+Q, Cmd+F1, Cmd+F3) or is a standard Mac command with a different meaning (Cmd+H, Cmd+M, Cmd+W, Cmd+X, Cmd+Z, Cmd+Shift+Z, Cmd+,, Cmd+Q, Ctrl+Cmd+F, Cmd+`, Cmd+?), add Option; if that is also taken, use Ctrl+Cmd. Mac aliases are added for commands that have a Mac standard: Settings Cmd+, ; Full Screen Ctrl+Cmd+F; Redo Cmd+Shift+Z; Help Cmd+? ; Close (book tab) Cmd+W; Remove Cmd+Backspace; Quit Cmd+Q. Function keys need fn on Mac laptops unless "Use F1, F2, etc. keys as standard function keys" is on; that is documented in Help, not changed.

## 7. Chrome decisions (control by control)

Default = what tasks implement. **O** = original look, **N** = native macOS. "Flip cost" says how expensive it is if the owner reverses the decision at C2 or C6 after looking at the contact sheet.

| ID | Element | Original look | Native macOS | Default | Rationale | Flip cost |
|---|---|---|---|---|---|---|
| C-APP-1 | Appearance (light/dark) | Default theme, Dark only by `-dark` | follows System Settings | **N switching, O palettes**: light = Default tokens, dark = Dark tokens; `--theme light|dark` app option (mirrors `-dark`, used by contact sheets) | both palettes are original; system appearance is a Mac feature that clearly belongs to the OS | low (one setting) |
| C-WIN-1 | Window frame, title bar, traffic lights, resizing | Windows caption | standard macOS title bar | **N** (plain title bar, not unified or transparent; title text as the original: "ComicRack" plus the open book) | owner-listed native element | — |
| C-WIN-2 | Full screen | borderless maximized window | full-screen Space | **N** (F11 and Ctrl+Cmd+F; reader behaviour inside as the original: hidden cursor, optional minimal UI) | owner-listed | — |
| C-WIN-3 | Window tabbing | — | automatic NSWindow tabs | **off** (the app has its own tab bars) | avoids two tab systems | low |
| C-MENU-1 | Menu bar | menu strip in the window | NativeMenu | **N** with the original menus, items, order, separators, check marks and shortcuts; About, Settings…, Quit move to the app menu; no icons in the menu bar | owner-listed; Mac apps need the global menu | — |
| C-MENU-2 | Window menu | — | standard Window menu | **N**, added (Minimize, Zoom, Library window Option+Cmd+0, Bring All to Front) | Mac standard; DD7 | — |
| C-MENU-3 | Context menus and toolbar drop-downs | Office-style toolstrip drop-downs with 16 px icons and an image margin | NSMenu look | **N look, O content**: Mac-style popup (system font 13, rounded, Mac selection), original items, order, shortcuts, check marks and the original 16 px icons | owner-listed native rendering; the icons carry meaning for fit and layout modes | medium (one style) |
| C-SCROLL-1 | Scrollbars | Windows scrollbars | overlay scrollers | **N** (Avalonia overlay scroll bars that honour System Settings "Show scroll bars") | owner-listed | — |
| C-DLG-1 | Standard dialogs: open, save, choose folder, message boxes, questions with "do not ask again" | Windows common dialogs, MessageBox, `QuestionDialog` | NSOpenPanel, NSSavePanel, NSAlert (suppression check box) | **N** (StorageProvider panels; alerts through `NSAlert` interop, P5.T4; the MVP `MessageWindow` restyled is the fallback) | owner-listed | — |
| C-DLG-2 | ComicRack's own dialogs (Info, Preferences, Export, Smart List, Book Display Settings, ...) | fixed-size Windows dialogs | sheets | **O**: separate modal windows centred on the owner, original layout, fields, labels, tab order, group boxes; buttons OK, Cancel, Apply in the original order and position; Return = default button, Esc = Cancel | owner: "dialog layouts" stay | low per dialog (button order is one style) |
| C-TEXT-1 | Text editing in fields | Windows edit control | NSTextField behaviour | **N behaviour, O frame**: Cmd/Option editing keys, system text selection colour, Mac context menu of text fields; size and border as the original | owner-listed | — |
| C-FOCUS-1 | Keyboard focus | dotted focus rectangle | accent focus ring | **N** | owner-listed | — |
| C-DOCK-1 | Dock icon and Dock menu, app menu | taskbar, jump lists (dropped, §6.4) | Dock | **N** | owner-listed | — |
| C-TRAY-1 | Notification-area icon (MinimizeToTray, CloseMinimizesToTray) | tray icon with Restore, Exit | menu bar status item | **N host, O menu** (NSStatusItem with the original icon and menu) | the macOS equivalent of the notification area | low |
| C-TB-1 | Toolbars (reader toolbar, browser toolbar, tree toolbar, small preview, pages view) | ToolStrip, professional renderer, 16 px icons, split buttons, separators | NSToolbar / unified title bar | **O** (Avalonia toolbar styled with `Cr.Tool*` tokens, 25 px, flat buttons with hover/pressed fills, split buttons with a 14 px arrow part, text on Zoom and Rotate buttons) | ComicRack-specific chrome | medium |
| C-TAB-1 | Tab bars (book tabs, browser tabs) | `TabBar` custom-drawn | — | **O** | ComicRack-specific | — |
| C-SB-1 | Status bar | StatusStrip with segments, sunken activity icons, size slider | — | **O** | ComicRack-specific | — |
| C-LIST-1 | Browser views (thumbnails, tiles, details), pages view, Quick Open | `ItemView` + renderers (§4) | — | **O** (all of §4: shadows, borders, dog-ear, badges, ribbons, stars, group headers, zebra rows, selection style §2.4) | ComicRack-specific | — |
| C-TREE-1 | Library and folder trees | `NiceTreeSkin`, gauges | NSOutlineView source list | **O** (row height, bold selected label, arrow images, gauge pills; not the Mac source-list look) | ComicRack-specific | medium |
| C-HDR-1 | Column headers | `HeaderControl` | NSTableHeaderView | **O** | ComicRack-specific | low |
| C-SPLIT-1 | Docked panels and splitters | `SizableContainer` grips (6 px, MozillaGrip), collapsible with animation | thin NSSplitView dividers | **O** | ComicRack-specific | low |
| C-GRP-1 | Preference group boxes | `CollapsibleGroupBox` (24 px bold header, gradient, collapse) | — | **O** | ComicRack-specific | — |
| C-PREF-1 | Preferences window | dialog with a left column of five 32 px icon tabs, OK/Cancel/Apply | Settings window with a toolbar of panes, live apply | **O** (opened by Cmd+, from the app menu; title "Settings" per macOS 14; apply semantics as the original: OK/Apply commit, Cancel reverts) | owner: dialog layouts stay | medium |
| C-CTRL-1 | Buttons, check boxes, radio buttons, combo boxes, numeric up/down, sliders, list boxes in dialogs | Windows 10 visual styles (§2.5) | AppKit controls | **O** (Avalonia control themes drawn from §2.1/§2.5 tokens; in dark mode the original dark colours of §2.2) | "when in doubt prefer the original"; shown on the C2 contact sheet for the owner's verdict | medium (one theme file) |
| C-READ-1 | Reader chrome (background, page shadow, overlays, navigation overlay, magnifier frame, page curl) | `ComicDisplayControl` with glass images | — | **O** | ComicRack-specific | — |
| C-FONT-1 | Fonts | MS Sans Serif / Segoe UI | SF Pro | **N family, O sizes** (§3; Arial and Times kept where the original names them) | Windows fonts are not on macOS; proportions kept | low |
| C-ICON-1 | Icons and images | `Resources` PNG/GIF | SF Symbols | **O** (§5) | owner: "icons from the existing resources" | — |
| C-CURSOR-1 | Cursors (hand, magnifier, wait) | Windows cursors and custom cursor resources | macOS cursors | **N** for standard cursors, **O** for ComicRack's custom cursors (if a resource exists) | standard cursors are part of the OS | low |
| C-SPLASH-1 | Splash and About | `Splash` form (619 × 317 image, fade, version text) | standard About panel | **O** (splash at startup when `ShowSplash`; About shows the same panel) | original feature (PB.01) | low |
| C-DLG-3 | Alert where the system alert cannot run (tests, headless platform) | `QuestionDialog` | NSAlert | **O** (the window `AlertWindow` on the `CrDialogWindow` frame: bold message, description at most 350 wide, check box, 80 x 24 buttons, first button default, Esc = cancel button); only a fallback, the app uses NSAlert | keeps tests and the headless smoke run independent of AppKit's modal loop | — |
| C-CTRL-2 | Text size of standard controls | 8.25 pt dialog font | system font | **O sizes**: every control reads 11 px (`ControlContentThemeFontSize`), item views and trees 12, menus 13 (C-MENU-3); the MVP windows follow until P5.R1 to P5.R3 restyle them | one rule for every control, the proportions of the original | low (one resource) |

New decisions are added here by the task that meets them (same columns) and listed in its report; the owner sees them on the next contact sheet.

## 8. View specs (written by every UI task before coding)

- File: `mac/specs/views/<OriginalTypeName>.md` (one per WinForms form, user control or custom-drawn view; created by the first task that needs it, extended by later ones). P5.T3 adds `mac/tools/designer-dump/designer-dump.py` (python3 standard library) that prints the skeleton from a `*.Designer.cs`: control tree in z-order with type, name, text, location, size, anchor/dock, image resource, shortcut, plus the TR section and key of each text (section = form type name, key = control name, as `ComicRack/Output/Languages/<culture>/<Form>.xml`; `&` mnemonics are dropped on macOS).
- Content: (1) source files and line ranges; (2) layout tree with sizes at 96 DPI and resize behaviour (anchor/dock, minimum size); (3) every control in tab order: type, name, TR key, English text, default value, bound setting or `ComicBook` property, enable/visibility rules; (4) menus and shortcuts (ledger row IDs); (5) icons by resource name; (6) colour tokens and metrics used (§2, §4); (7) behaviours: what OK, Cancel, Apply, Previous/Next and each event do; (8) planned differences, each pointing to a §7 decision or a ledger deviation.
- The Mac view reproduces the tree: same groups and order, same labels through the original TR keys (never retyped English strings), same defaults. Mac-only additions go in a separate, clearly labelled group and get a `mac-only` ledger row.

## 9. Visual parity check (every UI task; owner review at C2 and C6)

1. **Unit level:** headless frame tests (`CRMAC_FRAMES_DIR`) assert token colours at known points (toolbar background, selection frame, status bar) and metrics (toolbar height 25, tab bar 31, status bar 24) in DIPs.
2. **Real window:** `mac/tools/ui-drive/ui-drive.sh launch --dev --data-dir <tmp> -- --theme light --show <ViewName> --fixture-library <dir>` (the hidden options of P5.T3: `--show` opens a view registered in `Views/ViewRegistry.cs`, `--list-views` prints the names, `--fixture-library` scans a `crcli fixtures make` folder into the private library), `shot <view>-light`, same with `--theme dark`; `probe` checks two or three token pixels.
3. **Contact sheet:** `mac/tools/chrome-sheet.sh <label> <ViewName>...` (P5.T3) writes `mac/artifacts/chrome/<label>/sheet.html` and `sheet.png`: one row per view with the reference (owner or CI screenshot if present, else a reference card rendered from the view spec: layout outline with sizes, token swatches, the icons used), the Mac light shot and the Mac dark shot, plus the §7 decisions that apply. `crcli image sheet` composes the PNG (no browser needed).
4. **Report:** the task lists the sheet path in its report and in `full-port.md` §3; differences it cannot fix become ledger notes or §9 items; new native-over-original choices become §7 rows.
5. **Owner checkpoints:** at C2 (after the restyle tasks and wave B) and C6 (after P7.A4) the orchestrator runs `mac/tools/chrome-sheet.sh C2 --all` (C6), copies `sheet.png` and `sheet.html` to `mac/artifacts/chrome/C2/`, and the checkpoint block asks the owner to open one file and answer per row "ok" or what to change, and to confirm or flip the §7 defaults marked with a flip cost. Answers become `R<n>` tasks.
