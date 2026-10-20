# ComicExplorerView (ComicRack/Views/ComicExplorerView.Designer.cs)

Mac view: the body of `ComicRack.Mac.Views.MainWindow` (side panel and browser; P5.R1 first version).

## 1. Sources
`ComicExplorerView.Designer.cs:35-146` (`sidePanel`, `treePanel`, `previewPane`, `comicBrowser`, `pluginContainer`), `cYo.Common.Windows/Forms/SizableContainer.cs`.

## 2. Layout
```
700 x 538 in the designer
  sidePanel (SizableContainer, dock left, 252 wide, grip on the right, 6)
    treePanel (fill)          the library tree (ComicListLibraryBrowser spec)
    previewPane (dock bottom, 207 high, own grip)   small preview with a 25 px toolbar (P5.6)
  comicBrowser (fill)         ComicBrowserControl spec
  pluginContainer (dock bottom, 162 high, hidden)   Info panel (P5.6)
```

## 3. Controls
| Control | Mac in P5.R1 | Task |
|---|---|---|
| `sidePanel` | `CrSizableContainer` Name=SidebarPanel, Grip=Right, ExpandedExtent=252, `IsExpanded` bound to `Sidebar.IsVisible` | P5.R1 |
| `treePanel` | tree with the window colour, no frame | P5.R1 |
| `previewPane` | not yet | P5.6 |
| `pluginContainer` | not yet | P5.6 |
| `comicBrowser` | the toolbar and the book grid | P5.R1, P5.R2 |

## 4. Menus and shortcuts
Browse > Sidebar (MB.05, Shift+F6) and the Sidebar toolbar button collapse and expand the side panel (100 ms slide as the original).

## 5. Icons
`<Sidebar>` (toolbar button), `<MozillaGrip>` (grip, cYo.Common.Windows).

## 6. Tokens and metrics
Side panel 252, grip 6 (`Cr.ControlLight` hot, `Cr.Highlight` pressed), tree background Cr.Window.

## 7. Behaviour
The panel width and whether it is open are saved in Settings.xml (P5.1) and restored at the next start; a collapsed panel is out of reach of Tab. The grip drags the width; a collapsed panel has no visible grip (the Sidebar button and menu reopen it).

## 8. Planned differences
Small preview and Info panel are not in the window until P5.6.
