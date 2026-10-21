using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Editor;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The Pages tab of the Info dialog (<c>tabPages</c>, IN.06): the list of the book's pages (<see cref="CrPageList"/>) with the context menu of <c>PagesView</c>, the Pages button
	/// (<c>btPageViews_Click</c>: the item view's menu to choose Thumbnails, Tiles or Details and the column to arrange by) and the Reset button (the original page order; its menu
	/// orders the pages by name, plain or numeric). The list works on a copy of the book that <see cref="BookEditorModel.EnsurePages"/> makes the first time the tab is shown; OK writes the
	/// copy's pages into the book. The view mode, the sizes and the column widths are kept for the next dialog (the original keeps them in <c>ComicBookDialog.PagesConfig</c>).
	/// </summary>
	public partial class InfoPagesPage : UserControl
	{
		// ComicBookDialog.PagesConfig: the item view's mode, sizes, widths and the sort, kept while the application runs
		private static PageListMode lastMode = PageListMode.Detail;
		private static double lastRowHeight, lastThumbnailHeight, lastTileHeight;
		private static int lastSortColumn = PageColumns.Page;
		private static bool lastSortAscending = true;
		private static readonly Dictionary<int, double> lastWidths = new Dictionary<int, double>();

		/// <summary>Forgets what the user chose for the list (view, sizes, widths, sort) as a new application would: for the tests, which share the process.</summary>
		public static void ResetViewState()
		{
			lastMode = PageListMode.Detail;
			lastRowHeight = lastThumbnailHeight = lastTileHeight = 0;
			lastSortColumn = PageColumns.Page;
			lastSortAscending = true;
			lastWidths.Clear();
		}

		private BookEditorModel model;
		private PageListModel pages;
		private PagesContextMenu menu;
		private Func<string, string, Task<string>> askName;
		private bool wired;
		private bool attached;

		public InfoPagesPage()
		{
			InitializeComponent();
			labelPagesInfo.Content = InfoPageText.Raw("labelPagesInfo", "Change the page order with drag & drop or use the context menu to change page types:"); // UseMnemonic is off: the ampersand is text
			btPageViewText.Text = InfoPageText.Get("btPageView", "Pages");
			btResetPages.Content = InfoPageText.Get("btResetPages", "Reset");
			if (lastRowHeight > 0)
			{
				pagesView.RowHeight = lastRowHeight;
				pagesView.ThumbnailHeight = lastThumbnailHeight;
				pagesView.TileHeight = lastTileHeight;
			}
			pagesView.Mode = lastMode;
			pagesView.ViewChanged += (sender, e) => RememberView();
			pagesView.HeaderClicked += (sender, id) =>
			{
				pages?.SortBy(id);
				RememberView();
			};
			pagesView.ItemActivated += (sender, row) => PageActivated?.Invoke(this, row);
			btPageView.Click += (sender, e) => ShowViewMenu();
			btResetPages.Click += (sender, e) => pages?.ResetPages();
			btResetPages.Flyout = OrderMenu();
			AddHandler(KeyDownEvent, OnPageKeyDown, RoutingStrategies.Bubble);
		}

		public CrPageList List => pagesView;

		public Button PageViewButton => btPageView;

		public SplitButton ResetButton => btResetPages;

		public Label InfoLabel => labelPagesInfo;

		/// <summary>The menu of the list; created with <see cref="Initialize"/>.</summary>
		public PagesContextMenu Menu => menu;

		/// <summary>The list model of the shown book, or null until the tab was shown for it.</summary>
		public PageListModel Pages => pages;

		/// <summary>A page was double clicked or Return was pressed on it (<c>PagesViewItemActivate</c>: the dialog shows the Colors tab).</summary>
		public event EventHandler<int> PageActivated;

		/// <param name="editor">The model of the dialog.</param>
		/// <param name="ask">Asks for a name (the bookmark): the caption and the proposed text; null after Cancel.</param>
		public void Initialize(BookEditorModel editor, Func<string, string, Task<string>> ask)
		{
			model = editor;
			askName = ask;
			DataContext = editor;
			menu = new PagesContextMenu(() => pages, askName);
			pagesView.ContextFlyout = menu.Flyout;
			model.PagesChanged += (sender, e) => Bind();
			// another book is shown while the tab is open: its list is made at once
			model.BookLoaded += (sender, e) =>
			{
				if (attached)
				{
					Activate();
				}
			};
			wired = true;
		}

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);
			attached = true;
			Activate();
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			attached = false;
		}

		/// <summary>The tab is shown: the list of the book is made (once) and shown.</summary>
		public void Activate()
		{
			if (wired)
			{
				model.EnsurePages();
				Bind();
			}
		}

		private void Bind()
		{
			if (!wired || ReferenceEquals(model.Pages, pages))
			{
				return;
			}
			pages = model.Pages;
			if (pages != null)
			{
				pages.SortRestore(lastSortColumn, lastSortAscending);
				foreach (PageListColumn column in pages.Columns)
				{
					if (lastWidths.TryGetValue(column.Id, out double width))
					{
						column.Width = width;
					}
				}
			}
			pagesView.Source = pages;
			btResetPages.IsEnabled = pages != null;
		}

		private void RememberView()
		{
			lastMode = pagesView.Mode;
			lastRowHeight = pagesView.RowHeight;
			lastThumbnailHeight = pagesView.ThumbnailHeight;
			lastTileHeight = pagesView.TileHeight;
			if (pages != null)
			{
				foreach (PageListColumn column in pages.Columns)
				{
					lastWidths[column.Id] = column.Width;
				}
				lastSortColumn = pages.SortColumn;
				lastSortAscending = pages.SortAscending;
			}
		}

		private void OnPageKeyDown(object sender, KeyEventArgs e)
		{
			if (!e.Handled && menu != null && pages != null && menu.TryRun(e.Key, e.KeyModifiers))
			{
				e.Handled = true;
			}
		}

		// ---- the Reset button's menu (cmResetPages)

		private MenuFlyout OrderMenu()
		{
			var flyout = new MenuFlyout();
			var byName = new MenuItem { Name = "miOrderByName", Header = InfoPageText.Get("miOrderByName", "Order by Name") };
			byName.Click += (sender, e) => pages?.OrderByName();
			var numeric = new MenuItem { Name = "miOrderByNameNumeric", Header = InfoPageText.Get("miOrderByNameNumeric", "Order by Name (numeric)") };
			numeric.Click += (sender, e) => pages?.OrderByNameNumeric();
			flyout.Items.Add(byName);
			flyout.Items.Add(numeric);
			// btResetPages_ShowContextMenu: both items need the file's index to be complete
			flyout.Opening += (sender, e) => byName.IsEnabled = numeric.IsEnabled = pages != null && pages.CanOrderByName;
			OrderItems = new[] { byName, numeric };
			return flyout;
		}

		/// <summary>Order by Name and Order by Name (numeric).</summary>
		public IReadOnlyList<MenuItem> OrderItems { get; private set; }

		// ---- the Pages button's menu (ItemView.GetViewMenu)

		private static string ItemViewText(string key, string english) => UiText.Get("ItemView", key, english);

		/// <summary>The menu of the Pages button: View (Thumbnails, Tiles, Details) and Arrange by (Not Sorted and the sortable columns).</summary>
		public MenuFlyout BuildViewMenu()
		{
			var flyout = new MenuFlyout();
			var view = new MenuItem { Name = "miViewMode", Header = ItemViewText("miViewMode", "View") };
			foreach ((string key, string english, PageListMode mode) in new[]
			{
				("miViewThumbs", "Thumbnails", PageListMode.Thumbnail),
				("miViewTiles", "Tiles", PageListMode.Tile),
				("miViewDetails", "Details", PageListMode.Detail),
			})
			{
				PageListMode target = mode;
				var item = new MenuItem { Name = key, Header = ItemViewText(key, english), ToggleType = MenuItemToggleType.CheckBox, IsChecked = pagesView.Mode == mode };
				item.Click += (sender, e) => pagesView.Mode = target;
				view.Items.Add(item);
			}
			flyout.Items.Add(view);
			var arrange = new MenuItem { Name = "miArrange", Header = ItemViewText("miArrange", "Arrange by") };
			var unsorted = new MenuItem { Name = "NotSorted", Header = ItemViewText("NotSorted", "Not Sorted"), ToggleType = MenuItemToggleType.CheckBox, IsChecked = pages != null && pages.SortColumn < 0 };
			unsorted.Click += (sender, e) =>
			{
				pages?.SortNone();
				RememberView();
			};
			arrange.Items.Add(unsorted);
			arrange.Items.Add(new Separator());
			if (pages != null)
			{
				foreach (PageListColumn column in pages.Columns)
				{
					if (!column.CanSort)
					{
						continue;
					}
					int id = column.Id;
					var item = new MenuItem { Name = "arrange" + id, Header = column.Title, ToggleType = MenuItemToggleType.CheckBox, IsChecked = pages.SortColumn == id };
					item.Click += (sender, e) =>
					{
						pages.SortBy(id);
						RememberView();
					};
					arrange.Items.Add(item);
				}
			}
			flyout.Items.Add(arrange);
			return flyout;
		}

		private void ShowViewMenu()
		{
			MenuFlyout flyout = BuildViewMenu();
			flyout.ShowAt(btPageView);
		}
	}
}
