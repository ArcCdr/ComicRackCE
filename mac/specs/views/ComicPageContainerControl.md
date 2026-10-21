# ComicPageContainerControl (ComicRack.Engine/Controls/ComicPageContainerControl.cs, ComicPageControl.cs) and its host pluginContainer (ComicRack/Views/ComicExplorerView.cs, .Designer.cs)

Mac view: `Views/InfoPanelPane` (tab strip `CrTabBar`, page host `Border`), view model `ViewModels/InfoPanelViewModel`, page interface `Info/IInfoPage`, hosted by `CrSizableContainer` `InfoPanelDock` at the bottom of the browser area in `Views/MainWindow.axaml`. P5.6b. Ledger MB.08, IP.01, PB.22. Decision C-SPLIT-1.

## 1. Sources
`ComicPageContainerControl.cs` (tab per page, `OnControlAdded`, `OnControlRemoved`, `ShowInfo`), `ComicPageControl.cs` (`ShowInfo`, `MarkAsDirty`, pending update when hidden, `Icon`), `ComicExplorerView.Designer.cs:35-146` (`pluginContainer`, `comicInfo`, `pluginPlaceholder`), `ComicExplorerView.cs:195-370` (`ISidebar.Info`, `HasInfoPanels`, `InfoBrowserSize`, `UpdatePreviewPadding`, `UpdatePreview`, `previewBookChanged`, `AddInfo`), `ComicExplorerViewSettings.cs` (`ShowInfo`, `InfoBrowserSize`), `MainForm.cs:1478`, `:2387`, `:3307`, `:3317` (Browse > Info Panel), `SearchContextMenuBuilder.cs`, `WikiSearch.cs`, `CoverViewItem.cs:1228` (search link button).

The original has **no built-in page**: pages come only from scripts (`ComicInfoHtml`, `ComicInfoUI` hooks, P4.11a).

## 2. Layout (96 DPI units)
```
browser area (right of the side panel)
  comicBrowser            Fill
  pluginContainer         Dock Bottom, 162 high (InfoBrowserSize), grip 6 on top, bottom padding 6, Visible=false until a page is added, Expanded=false (hidden by default)
    comicInfo             Fill
      tabBar              Dock Top, 31 high, MinimumTabWidth 100, visible with two pages or more
      page                Fill (the selected page; the others are hidden)
  pluginPlaceholder       Dock Bottom, 6 high, shown while the container is not visible
```
Collapsed (not Expanded) the container is its 6 unit grip.

## 3. Controls
| Control | Mac |
|---|---|
| `pluginContainer` | `CrSizableContainer` `InfoPanelDock` (Grip Top, `KeepGripVisible`), child `Border` with bottom padding 6 |
| `pluginPlaceholder` | `Border` `InfoPlaceholder`, 6 high |
| `comicInfo` | `InfoPanelPane` |
| `tabBar` | `CrTabBar` `InfoTabBar`, tabs `CanClose` false, `MinimumWidth` 100, text = page title, icon = page icon name |
| `ComicPageControl` | `IInfoPage` (`InfoPageItem` holds the pending books) |

No texts of its own (the tabs show the pages' titles). Browse > Info Panel is `MainForm` key `miInfoPanel`.

## 4. Behaviour
- Browse > Info Panel (Shift+F9) is a check item (`CheckInfoPanelEnabled` = `ISidebar.Info`), enabled only when the panel has a page (`CheckInfoPanelAvailable` = `HasInfoPanels`). `ShowInfo` (off) and `InfoBrowserSize` (162) are kept in `Settings.xml`.
- The first page added is the selected tab and makes the container visible; later pages do not take the selection. A tab click shows its page and hides the others.
- While the panel is open the selected books go to the pages 500 ms after the selection settled (`previewTimer`), at once when the panel opens, and again when a selected book changes (`BookChanged`). The page that shows gets them; a page that does not show keeps them and gets them when its tab is selected (`MarkAsDirty` gives an empty list).
- Search links (`ShowSearchLinks`, default on): the details view cell shows an arrow while the list has the focus (text not empty, field `IsSearchable`); it opens a menu with the engine's results (up to 10; none for the whole text: the parts between commas, 2 each), `No Results` (disabled) when empty, `Search...` for the engine's own search page, and the book's web address after a separator; every entry opens in the default browser. One engine (Wikipedia): its items directly; several: a sub menu each.

## 5. Planned differences
- The sharing of one timer between preview and info panel is two timers with the same delay.
- The panel docked at the right (`InfoBrowserRight`) is not ported yet (full-port.md section 9).
- The search link arrow waits for the details view (P5.11a); its menu logic is `Info/SearchLinkMenu`.
