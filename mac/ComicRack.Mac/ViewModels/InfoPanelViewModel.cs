using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComicRack.Mac.Host;
using ComicRack.Mac.Info;
using ComicRack.Mac.Services;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.ViewModels
{
	/// <summary>
	/// The info panel under the browser (<c>ComicExplorerView.pluginContainer</c>, a <c>SizableContainer</c> with a <c>ComicPageContainerControl</c>, Browse &gt; Info
	/// Panel, Shift+F9). It is only the container: the original has no built-in book page, its pages come from scripts (<see cref="IInfoPage"/>, P4.11a). It holds:
	/// <list type="bullet">
	/// <item>the pages and their tab strip (the strip shows with two pages or more, the first page added is the selected one);</item>
	/// <item><see cref="HasPages"/> (<c>ISidebar.HasInfoPanels</c>): the container exists once a page was added (<c>AddInfo</c> makes it visible), and the menu item is
	/// disabled until then (<c>CheckInfoPanelAvailable</c>);</item>
	/// <item><see cref="IsVisible"/> (<c>ISidebar.Info</c>, <c>pluginContainer.Expanded</c>, kept as <c>ShowInfo</c>) and <see cref="Extent"/> (<c>InfoBrowserSize</c>, 162);</item>
	/// <item>the selection: when the panel is open, the selected books go to the pages 500 ms after they settled (<c>previewTimer</c>), at once when the panel opens, and
	/// again when one of the books changes (<c>previewBookChanged</c>). The page that is showing gets them, the others when they show.</item>
	/// </list>
	/// </summary>
	public sealed class InfoPanelViewModel : ObservableObject, IDisposable
	{
		/// <summary><c>previewTimer.Interval</c>: how long the selection has to rest before the pages show it.</summary>
		public static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(500);

		/// <summary>The smallest height a drag of the grip can leave (<c>CrSizableContainer.MinimumExtent</c>).</summary>
		public const double MinimumExtent = 26;

		private readonly AppPreferences preferences;
		private readonly ObservableCollection<InfoPageItem> pages = new ObservableCollection<InfoPageItem>();
		private IDisposable pending;
		private ComicBook[] watched = new ComicBook[0];
		private bool isVisible;
		private double extent = AppPreferences.DefaultInfoBrowserSize;
		private int selectedIndex = -1;
		private bool disposed;

		/// <param name="preferences">Where Show and the height are kept (<c>ShowInfo</c>, <c>InfoBrowserSize</c>); null keeps nothing.</param>
		/// <param name="scheduler">Runs the 500 ms timer; a test passes one it releases by hand. The default is a thread pool timer whose action is posted to the UI thread.</param>
		public InfoPanelViewModel(AppPreferences preferences = null, ISaveScheduler scheduler = null)
		{
			this.preferences = preferences;
			Pages = new ReadOnlyObservableCollection<InfoPageItem>(pages);
			Scheduler = scheduler ?? new TimerSaveScheduler();
			if (preferences != null)
			{
				isVisible = preferences.ShowInfo;
				double size = preferences.InfoBrowserSize;
				extent = double.IsNaN(size) || double.IsInfinity(size) ? AppPreferences.DefaultInfoBrowserSize : Math.Clamp(size, MinimumExtent, 5000);
			}
			ToggleCommand = new RelayCommand(() => IsVisible = !IsVisible, () => HasPages);
			pages.CollectionChanged += (sender, e) => ToggleCommand.NotifyCanExecuteChanged();
		}

		/// <summary>Runs the 500 ms timer; a test replaces it with one it releases by hand.</summary>
		public ISaveScheduler Scheduler { get; set; }

		/// <summary>The selected books of the browser (<c>GetBookList(Selected)</c>).</summary>
		public Func<IReadOnlyList<ComicBook>> SelectedBooks { get; set; } = () => new ComicBook[0];

		/// <summary>The pages, in the order of their tabs.</summary>
		public ReadOnlyObservableCollection<InfoPageItem> Pages { get; }

		/// <summary><c>ISidebar.HasInfoPanels</c>: there is at least one page. Browse &gt; Info Panel is enabled by it.</summary>
		public bool HasPages => pages.Count > 0;

		/// <summary>The container is shown (<c>pluginContainer.Visible</c>, set by <c>AddInfo</c>): from the first page on, and open or collapsed to its grip.</summary>
		public bool ContainerVisible => HasPages;

		/// <summary>The tab strip shows (<c>tabBar.Visible = Items.Count &gt; 1</c>).</summary>
		public bool TabsVisible => pages.Count > 1;

		/// <summary>Browse &gt; Info Panel (<c>ISidebar.Info</c>): the panel is open. Kept in the preferences.</summary>
		public bool IsVisible
		{
			get => isVisible;
			set
			{
				if (SetProperty(ref isVisible, value))
				{
					if (preferences != null)
					{
						preferences.ShowInfo = value;
					}
					UpdateShowing();
					UpdateNow();
				}
			}
		}

		/// <summary>The height of the panel when it is open, grip included (<c>pluginContainer.ExpandedWidth</c>, 162). Kept in the preferences.</summary>
		public double Extent
		{
			get => extent;
			set
			{
				double clamped = double.IsNaN(value) || double.IsInfinity(value) ? AppPreferences.DefaultInfoBrowserSize : Math.Max(MinimumExtent, value);
				if (SetProperty(ref extent, clamped) && preferences != null)
				{
					preferences.InfoBrowserSize = clamped;
				}
			}
		}

		/// <summary>The index of the page that is showing, or -1.</summary>
		public int SelectedIndex
		{
			get => selectedIndex;
			set
			{
				int index = value < 0 || value >= pages.Count ? (pages.Count == 0 ? -1 : Math.Clamp(value, 0, pages.Count - 1)) : value;
				if (SetProperty(ref selectedIndex, index))
				{
					OnPropertyChanged(nameof(SelectedPage));
					UpdateShowing();
				}
			}
		}

		/// <summary>The page that is showing, or null.</summary>
		public InfoPageItem SelectedPage => selectedIndex >= 0 && selectedIndex < pages.Count ? pages[selectedIndex] : null;

		/// <summary>Browse &gt; Info Panel (<c>ToggleInfoPanel</c>); not available without a page.</summary>
		public IRelayCommand ToggleCommand { get; }

		/// <summary>True while the 500 ms timer runs.</summary>
		public bool IsPending => pending != null;

		// ---- pages

		/// <summary><c>ISidebar.AddInfo</c> (<c>OnControlAdded</c>): a page joins the tabs. The first page is the selected one and makes the container visible.</summary>
		public InfoPageItem AddPage(IInfoPage page)
		{
			if (page == null)
			{
				throw new ArgumentNullException(nameof(page));
			}
			var item = new InfoPageItem(page);
			pages.Add(item);
			OnPropertyChanged(nameof(HasPages));
			OnPropertyChanged(nameof(ContainerVisible));
			OnPropertyChanged(nameof(TabsVisible));
			if (pages.Count == 1)
			{
				SelectedIndex = 0;
			}
			UpdateShowing();
			if (isVisible)
			{
				// the page shows the books that are selected now (the original's next UpdatePreview does; there is no wait for a page that joins late)
				item.ShowInfo(Books());
			}
			return item;
		}

		/// <summary><c>OnControlRemoved</c>: the page leaves the tabs.</summary>
		public bool RemovePage(IInfoPage page)
		{
			InfoPageItem item = pages.FirstOrDefault(p => ReferenceEquals(p.Page, page));
			if (item == null)
			{
				return false;
			}
			int old = selectedIndex;
			item.SetShowing(false);
			item.Detach();
			int at = pages.IndexOf(item);
			pages.Remove(item);
			OnPropertyChanged(nameof(HasPages));
			OnPropertyChanged(nameof(ContainerVisible));
			OnPropertyChanged(nameof(TabsVisible));
			if (pages.Count == 0)
			{
				selectedIndex = -1;
			}
			else if (at < old)
			{
				selectedIndex = old - 1;
			}
			else if (old >= pages.Count)
			{
				selectedIndex = pages.Count - 1;
			}
			OnPropertyChanged(nameof(SelectedIndex));
			OnPropertyChanged(nameof(SelectedPage));
			UpdateShowing();
			return true;
		}

		// ---- the timer and the selection

		/// <summary>The selection (or a selected book) changed: the timer starts again (<c>RestartPreviewTimer</c>).</summary>
		public void SelectionChanged()
		{
			if (disposed)
			{
				return;
			}
			pending?.Dispose();
			IDisposable mine = null;
			mine = Scheduler.Schedule(ShowDelay, () => PostUpdate(() =>
			{
				if (ReferenceEquals(pending, mine))
				{
					pending = null;
					Update();
				}
			}));
			pending = mine;
		}

		// The timer fires on a pool thread. A UI thread that is already gone (the window or the test is over) refuses the post; nobody is left to show the update to.
		private static void PostUpdate(Action action)
		{
			try
			{
				UiDispatch.Post(action);
			}
			catch (NullReferenceException)
			{
			}
			catch (InvalidOperationException)
			{
			}
		}

		/// <summary>Updates the pages at once (<c>pluginContainer_ExpandedChanged</c> does not wait for the timer).</summary>
		public void UpdateNow()
		{
			pending?.Dispose();
			pending = null;
			Update();
		}

		/// <summary>
		/// <c>UpdatePreview</c> (the info part): watches the selected books for changes and gives them to the pages. Nothing happens while the panel is closed, and the
		/// books that were watched are let go.
		/// </summary>
		public void Update()
		{
			foreach (ComicBook book in watched)
			{
				book.BookChanged -= OnBookChanged;
			}
			watched = new ComicBook[0];
			if (disposed || !isVisible)
			{
				return;
			}
			IReadOnlyList<ComicBook> books = Books();
			watched = books.ToArray();
			foreach (ComicBook book in watched)
			{
				book.BookChanged += OnBookChanged;
			}
			foreach (InfoPageItem page in pages.ToArray())
			{
				page.ShowInfo(books);
			}
		}

		private IReadOnlyList<ComicBook> Books() => SelectedBooks?.Invoke() ?? new ComicBook[0];

		private void OnBookChanged(object sender, BookChangedEventArgs e) => SelectionChanged();

		// the page whose tab is selected shows while the panel is open (the other pages are hidden: Visible = item == e.NewItem)
		private void UpdateShowing()
		{
			for (int i = 0; i < pages.Count; i++)
			{
				pages[i].SetShowing(isVisible && i == selectedIndex);
			}
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			pending?.Dispose();
			pending = null;
			Update();
			foreach (InfoPageItem page in pages)
			{
				page.Detach();
			}
		}
	}
}
