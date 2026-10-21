using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Services;
using ComicRack.Mac.Thumbnails;
using cYo.Common.Drawing;
using cYo.Common.Text;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO;
using cYo.Projects.ComicRack.Engine.IO.Cache;

namespace ComicRack.Mac.Editor
{
	/// <summary>The columns of the pages view (<c>PagesView</c> constructor: id, English text, width, field, sorter, alignment) with their translations from the <c>Columns</c> section.</summary>
	public static class PageColumns
	{
		public const int Page = 0;
		public const int Thumbnail = 1;
		public const int Type = 2;
		public const int Size = 3;
		public const int Width = 4;
		public const int Height = 5;
		public const int Name = 6;
		public const int Bookmark = 7;
		public const int Rotation = 8;
		public const int Position = 9;

		private sealed class Spec
		{
			public int Id;
			public string Property;
			public string English;
			public int Width;
			public bool Far;
			public Comparison<PageRow> Sorter;
		}

		private static readonly Spec[] specs =
		{
			new Spec { Id = Page, Property = "Page", English = "Page", Width = 40, Far = true, Sorter = (x, y) => x.Page.CompareTo(y.Page) },
			new Spec { Id = Thumbnail, Property = "Thumbnail", English = "Thumbnail", Width = 80 },
			new Spec { Id = Type, Property = "PageTypeAsText", English = "Type", Width = 80, Sorter = Info(new ComicPageInfoTypeComparer()) },
			new Spec { Id = Size, Property = "ImageFileSizeAsText", English = "Size", Width = 80, Far = true, Sorter = Info(new ComicPageInfoImageSizeComparer()) },
			new Spec { Id = Width, Property = "ImageWidthAsText", English = "Width", Width = 60, Far = true, Sorter = Info(new ComicPageInfoImageWidthComparer()) },
			new Spec { Id = Height, Property = "ImageHeightAsText", English = "Height", Width = 60, Far = true, Sorter = Info(new ComicPageInfoImageHeightComparer()) },
			new Spec { Id = Name, Property = "Key", English = "Name", Width = 150, Sorter = (x, y) => string.Compare(x.Key, y.Key) },
			new Spec { Id = Bookmark, Property = "Bookmark", English = "Bookmark", Width = 150, Sorter = Info(new ComicPageBookmarkComparer()) },
			new Spec { Id = Rotation, Property = "RotationAsText", English = "Rotation", Width = 60, Far = true, Sorter = Info(new ComicPageRotationComparer()) },
			new Spec { Id = Position, Property = "PagePositionAsText", English = "Position", Width = 60, Sorter = Info(new ComicPagePositionComparer()) },
		};

		// PageViewItemComparer<T>: the comparer of the page infos, applied to the page info of each row as the book holds it
		private static Comparison<PageRow> Info(IComparer<ComicPageInfo> comparer) => (x, y) => comparer.Compare(x.Info, y.Info);

		/// <summary>The columns with their texts from the <c>Columns</c> section of the language files (<c>ComicListField.TranslateColumns</c>).</summary>
		public static List<PageListColumn> Create()
		{
			return specs.Select(s => new PageListColumn(s.Id, UiText.Raw("Columns", s.Property, s.English), s.Width, s.Far, s.Id == Thumbnail, s.Sorter != null)).ToList();
		}

		/// <summary>The property of <see cref="ComicPageInfo"/> (or "Page", "Key") that a column shows.</summary>
		public static string PropertyOf(int columnId) => specs.First(s => s.Id == columnId).Property;

		internal static Comparison<PageRow> SorterOf(int columnId) => specs.FirstOrDefault(s => s.Id == columnId)?.Sorter;
	}

	/// <summary>One page of the list (<c>PageViewItem</c>): the image of the book it shows, its name in the file, and the page info the book holds for it now.</summary>
	public sealed class PageRow
	{
		private readonly PageListModel owner;

		internal PageRow(PageListModel owner, int imageIndex, string key)
		{
			this.owner = owner;
			ImageIndex = imageIndex;
			Key = key ?? string.Empty;
		}

		/// <summary>The index of the image in the book's file (stays when pages are moved).</summary>
		public int ImageIndex { get; }

		/// <summary>The name of the image in the file (<c>PageViewItem.Key</c>).</summary>
		public string Key { get; internal set; }

		public ComicPageInfo Info => owner.Comic.GetPageByImageIndex(ImageIndex);

		/// <summary>The page number, counting from 0, in the order the book holds the pages in now.</summary>
		public int Page => owner.Comic.TranslateImageIndexToPage(ImageIndex);

		public string PageAsText => (Page + 1).ToString();

		/// <summary><c>PageViewItem.GetStringValue</c>.</summary>
		public string Cell(string property)
		{
			if (property == "Key")
			{
				return Key;
			}
			if (property == "Page")
			{
				return PageAsText;
			}
			return Info.GetStringValue(property);
		}

		public void SetPageType(ComicPageType type) => owner.Comic.UpdatePageType(Page, type);

		public void SetPageRotation(ImageRotation rotation) => owner.Comic.UpdatePageRotation(Page, rotation);

		public void SetPagePosition(ComicPagePosition position) => owner.Comic.UpdatePagePosition(Page, position);
	}

	/// <summary>What the page list needs next to the book: the pictures, the Core's pool and the way to reach the UI thread.</summary>
	public sealed class PageListServices
	{
		/// <summary>Makes the pictures of the rows; null: rows have no picture.</summary>
		public ThumbnailService Thumbnails { get; set; }

		/// <summary>The Core's image pool (Refresh drops the page and thumbnail of a row from it); null: only the service's pictures are dropped.</summary>
		public ImagePool Pool { get; set; }

		/// <summary>Runs an action on the UI thread later; the default is <see cref="UiDispatch.Post(Action, Avalonia.Threading.DispatcherPriority)"/>.</summary>
		public Action<Action> Post { get; set; }
	}

	/// <summary>
	/// The list of pages of one book (<c>PagesView</c>): a row for each page of the working copy of the book in the order the copy holds them, the sorting, the selection and the commands of
	/// the page menu. The Info dialog's Pages tab edits a copy (<see cref="Comic"/>, a <c>new ComicBook(book)</c> with its own navigator); OK writes its pages into the library's book
	/// (<see cref="SavePagesTo"/>), Cancel drops them. The main window's pages view (P5.23b) uses the same model on the book that is open.
	/// <para>The list is <see cref="PageFilter"/> filtered (the dialog shows every page, deleted ones included), starts sorted by page number (the original sorts by the Page column), and
	/// refills itself when the navigator learns of more pages and when the copy's pages are moved or reset.</para>
	/// </summary>
	public sealed class PageListModel : IPageListSource, IDisposable
	{
		private readonly ComicBook comic;
		private readonly ComicBookNavigator navigator;
		private readonly PageListServices services;
		private readonly Func<int, string> nameOf;
		private readonly Action<Action> post;
		private readonly bool ownsNavigator;
		private readonly List<PageListColumn> columns = PageColumns.Create();
		private readonly List<PageRow> natural = new List<PageRow>();
		private List<PageRow> shown = new List<PageRow>();
		private readonly HashSet<int> selected = new HashSet<int>(); // image indexes
		private readonly PageRowThumbnails pictures;
		private int sortColumn = PageColumns.Page;
		private bool sortAscending = true;
		private ComicPageType filter = ComicPageType.AllWithDeleted;
		private int refillPending;
		private bool disposed;
		private bool started;
		private bool listedLogged;

		/// <param name="comic">The book whose pages are listed (a working copy: the model changes its pages).</param>
		/// <param name="navigator">The navigator of <paramref name="comic"/> that reads the file's image names and page count; null: <paramref name="nameOf"/> and the book's page count are used (tests).</param>
		/// <param name="services">Pictures, pool and UI thread.</param>
		/// <param name="nameOf">The name of an image in the file when there is no navigator.</param>
		/// <param name="ownsNavigator">The model disposes the navigator with itself (the dialog's list; the navigator is made for it).</param>
		public PageListModel(ComicBook comic, ComicBookNavigator navigator, PageListServices services = null, Func<int, string> nameOf = null, bool ownsNavigator = false)
		{
			this.ownsNavigator = ownsNavigator;
			this.comic = comic ?? throw new ArgumentNullException(nameof(comic));
			this.navigator = navigator;
			this.services = services ?? new PageListServices();
			this.nameOf = nameOf;
			post = this.services.Post ?? (action => UiDispatch.Post(action));
			pictures = new PageRowThumbnails(this.services.Thumbnails, comic);
			pictures.Ready += (sender, e) => PicturesChanged?.Invoke(this, EventArgs.Empty);
			comic.BookChanged += OnComicChanged;
			if (navigator != null)
			{
				navigator.IndexOfPageReady += OnIndexReady;
				navigator.IndexRetrievalCompleted += OnIndexCompleted;
			}
		}

		/// <summary>The working copy.</summary>
		public ComicBook Comic => comic;

		public ComicBookNavigator Navigator => navigator;

		/// <summary>True once the navigator has read the whole file index (<c>IsIndexRetrievalCompleted</c>), or when there is no navigator.</summary>
		public bool IsIndexComplete => navigator == null || navigator.IsIndexRetrievalCompleted;

		/// <summary>The number of images in the file as far as known.</summary>
		public int ImageCount => navigator?.Count ?? comic.PageCount;

		/// <summary>The kinds of pages listed (<c>PagesView.PageFilter</c>); changing it refills the list.</summary>
		public ComicPageType PageFilter
		{
			get => filter;
			set
			{
				if (filter != value)
				{
					filter = value;
					Refill(keepSelection: true);
				}
			}
		}

		public IReadOnlyList<PageRow> Rows => shown;

		/// <summary>The rows selected, as rows.</summary>
		public IReadOnlyList<PageRow> SelectedRows => shown.Where(r => selected.Contains(r.ImageIndex)).ToList();

		/// <summary>The page infos of the selected rows in row order (<c>GetSelectedPages</c>).</summary>
		public IEnumerable<ComicPageInfo> SelectedPages => SelectedRows.Select(r => r.Info);

		/// <summary>Opens the navigator (reading the file's index in the background) and fills the list with what is known.</summary>
		public void Start()
		{
			if (started)
			{
				return;
			}
			started = true;
			if (navigator != null)
			{
				navigator.Open(async: true, 0);
			}
			Refill(keepSelection: false);
		}

		// ---- IPageListSource -----------------------------------------------------------------------------------------

		public int Count => shown.Count;

		public IReadOnlyList<PageListColumn> Columns => columns;

		public int SortColumn => sortColumn;

		public bool SortAscending => sortAscending;

		public string Text(int row, int columnId) => shown[row].Cell(PageColumns.PropertyOf(columnId));

		public PageRowState State(int row)
		{
			PageRow r = shown[row];
			ComicPageInfo info = r.Info;
			return new PageRowState(r.PageAsText, info.IsDeleted, info.IsBookmark, false);
		}

		public string ToolTip(int row) => string.Format("{0} #{1}", UiText.Default("Page", "Page"), shown[row].PageAsText);

		public IReadOnlyList<TileTextLine> Lines(int row)
		{
			PageRow r = shown[row];
			return PageTileText.Build(r.Info, r.Page);
		}

		public CoverImage Picture(int row, int height) => row >= 0 && row < shown.Count ? pictures.Get(shown[row], height) : null;

		public IReadOnlyList<int> Selection
		{
			get
			{
				var rows = new List<int>();
				for (int i = 0; i < shown.Count; i++)
				{
					if (selected.Contains(shown[i].ImageIndex))
					{
						rows.Add(i);
					}
				}
				return rows;
			}
		}

		public void SetSelection(IEnumerable<int> rows)
		{
			var next = new HashSet<int>();
			foreach (int row in rows ?? Array.Empty<int>())
			{
				if (row >= 0 && row < shown.Count)
				{
					next.Add(shown[row].ImageIndex);
				}
			}
			if (!next.SetEquals(selected))
			{
				selected.Clear();
				selected.UnionWith(next);
				SelectionChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		public event EventHandler Changed;

		public event EventHandler RowsChanged;

		public event EventHandler SelectionChanged;

		public event EventHandler PicturesChanged;

		/// <summary>Raised after a command changed the copy's pages (type, rotation, position, bookmark, order): the dialog's pages differ from the book's now.</summary>
		public event EventHandler PagesEdited;

		// ---- sorting -------------------------------------------------------------------------------------------------

		/// <summary>
		/// The header of a column was clicked or the column was chosen in the Arrange by menu (<c>OnHeaderClick</c>, <c>ArrangeMenuItemClicked</c>): the sorted column flips its order, another
		/// one sorts ascending. A column without a sorter (the thumbnail) does nothing.
		/// </summary>
		public void SortBy(int columnId)
		{
			if (PageColumns.SorterOf(columnId) == null)
			{
				return;
			}
			if (sortColumn == columnId)
			{
				sortAscending = !sortAscending;
			}
			else
			{
				sortColumn = columnId;
				sortAscending = true;
			}
			Resort();
		}

		/// <summary>Sets the sort without raising anything (the dialog restores what the user chose last time before the list is shown).</summary>
		public void SortRestore(int columnId, bool ascending)
		{
			if (columnId < 0 || PageColumns.SorterOf(columnId) != null)
			{
				sortColumn = columnId;
				sortAscending = ascending;
				shown = Sorted(natural);
			}
		}

		/// <summary>"Not Sorted" of the Arrange by menu: the rows are in the order the book holds the pages in.</summary>
		public void SortNone()
		{
			sortColumn = -1;
			sortAscending = true;
			Resort();
		}

		/// <summary>True if the rows are in page order, ascending and not grouped: only then can pages be moved by dragging (<c>IsPageSorted</c>).</summary>
		public bool IsPageSorted => sortColumn == PageColumns.Page && sortAscending;

		private void Resort()
		{
			shown = Sorted(natural);
			Changed?.Invoke(this, EventArgs.Empty);
		}

		private List<PageRow> Sorted(List<PageRow> rows)
		{
			Comparison<PageRow> sorter = sortColumn < 0 ? null : PageColumns.SorterOf(sortColumn);
			if (sorter == null)
			{
				return new List<PageRow>(rows);
			}
			// a stable sort, like the item view's: rows that compare equal stay in the order the book holds them in
			var indexed = rows.Select((r, i) => (Row: r, Index: i)).ToList();
			indexed.Sort((a, b) =>
			{
				int result = sorter(a.Row, b.Row);
				if (!sortAscending)
				{
					result = -result;
				}
				return result != 0 ? result : a.Index.CompareTo(b.Index);
			});
			return indexed.Select(t => t.Row).ToList();
		}

		// ---- filling -------------------------------------------------------------------------------------------------

		private string NameOfImage(int imageIndex)
		{
			try
			{
				return nameOf != null ? nameOf(imageIndex) : navigator?.GetImageName(imageIndex) ?? string.Empty;
			}
			catch (Exception)
			{
				return string.Empty;
			}
		}

		/// <summary>Builds the rows again from the copy's pages (<c>FillList</c>); the selection of the pages that are still there is kept if asked.</summary>
		public void Refill(bool keepSelection)
		{
			if (disposed)
			{
				return;
			}
			var before = new HashSet<int>(selected);
			HashSet<int> keep = keepSelection ? new HashSet<int>(selected) : new HashSet<int>();
			int count;
			try
			{
				count = IsIndexComplete ? Math.Max(ImageCount, comic.PageCount) : ImageCount;
			}
			catch (ObjectDisposedException)
			{
				return;
			}
			natural.Clear();
			for (int i = 0; i < count; i++)
			{
				ComicPageInfo info = comic.GetPage(i);
				if (info.IsTypeOf(filter))
				{
					natural.Add(new PageRow(this, info.ImageIndex, NameOfImage(info.ImageIndex)));
				}
			}
			shown = Sorted(natural);
			selected.Clear();
			foreach (PageRow row in shown)
			{
				if (keep.Contains(row.ImageIndex))
				{
					selected.Add(row.ImageIndex);
				}
			}
			Changed?.Invoke(this, EventArgs.Empty);
			if (!before.SetEquals(selected))
			{
				SelectionChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		private void RefillSoon()
		{
			// the navigator reports every page from its reader thread: one refill for all that arrive together
			if (Interlocked.Exchange(ref refillPending, 1) == 0)
			{
				post(() =>
				{
					Interlocked.Exchange(ref refillPending, 0);
					Refill(keepSelection: true);
				});
			}
		}

		private void OnIndexReady(object sender, BookPageEventArgs e) => RefillSoon();

		private void OnIndexCompleted(object sender, EventArgs e)
		{
			// the whole file index is known: the last refill, and the line the end to end case waits for
			post(() =>
			{
				if (disposed)
				{
					return;
				}
				Refill(keepSelection: true);
				if (!listedLogged && shown.Count > 0)
				{
					listedLogged = true;
					AppLog.Info("Page list: " + shown.Count + " pages listed.");
				}
			});
		}

		private void OnComicChanged(object sender, BookChangedEventArgs e)
		{
			if (disposed)
			{
				return;
			}
			if (e.Page >= 0)
			{
				post(() =>
				{
					if (!disposed)
					{
						RowsChanged?.Invoke(this, EventArgs.Empty);
					}
				});
			}
		}

		// ---- the page menu -------------------------------------------------------------------------------------------

		/// <summary>True if the copy's pages may be edited (<c>EditMode.CanEditPages</c>): the page menu hides its editing items otherwise.</summary>
		public bool CanEditPages => comic.EditMode.CanEditPages();

		private void Edited()
		{
			PagesEdited?.Invoke(this, EventArgs.Empty);
			// a list sorted by a column that the command changed (the type, the rotation, the bookmark ...) puts the row where it belongs now
			if (sortColumn >= 0 && sortColumn != PageColumns.Page)
			{
				Resort();
			}
			RowsChanged?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>The page type that all selected pages have, or null when they differ or nothing is selected (<c>contextPages_Opening</c>).</summary>
		public ComicPageType? CommonPageType => Common(p => p.PageType);

		public ImageRotation? CommonRotation => Common(p => p.Rotation);

		public ComicPagePosition? CommonPosition => Common(p => p.PagePosition);

		private T? Common<T>(Func<ComicPageInfo, T> read) where T : struct
		{
			T? value = null;
			foreach (ComicPageInfo page in SelectedPages)
			{
				if (value == null)
				{
					value = read(page);
				}
				else if (!EqualityComparer<T>.Default.Equals(value.Value, read(page)))
				{
					return null;
				}
			}
			return value;
		}

		/// <summary>Page Type: sets the type of the selected pages (<c>PageMenuValueChanged</c>).</summary>
		public void SetPageType(ComicPageType type)
		{
			foreach (PageRow row in SelectedRows)
			{
				row.SetPageType(type);
			}
			Edited();
		}

		/// <summary>Page Rotation (<c>RotateMenuValueChanged</c>).</summary>
		public void SetRotation(ImageRotation rotation)
		{
			foreach (PageRow row in SelectedRows)
			{
				row.SetPageRotation(rotation);
			}
			Edited();
		}

		/// <summary>Page Position: Default, Near or Far (<c>SetPagePosition</c>).</summary>
		public void SetPosition(ComicPagePosition position)
		{
			foreach (PageRow row in SelectedRows)
			{
				row.SetPagePosition(position);
			}
			Edited();
		}

		/// <summary>Mark as Deleted (Delete): a deleted page becomes a story page again, any other one is deleted (<c>MarkAsDeleted</c>).</summary>
		public void MarkAsDeleted()
		{
			foreach (PageRow row in SelectedRows)
			{
				row.SetPageType(row.Info.PageType == ComicPageType.Deleted ? ComicPageType.Story : ComicPageType.Deleted);
			}
			Edited();
		}

		// -- bookmarks

		/// <summary>One page selected in a book whose pages can be edited (<c>CanBookmark</c>).</summary>
		public bool CanBookmark => CanEditPages && selected.Count == 1 && SelectedRows.Count == 1;

		/// <summary>The bookmark of the selected page, or null.</summary>
		public string Bookmark => CanBookmark ? SelectedRows[0].Info.Bookmark : null;

		/// <summary>The name that Set Bookmark... offers: the page's bookmark or "Page N" (<c>BookmarkProposal</c>).</summary>
		public string BookmarkProposal
		{
			get
			{
				if (!CanBookmark)
				{
					return null;
				}
				return !string.IsNullOrEmpty(Bookmark) ? Bookmark : string.Format("{0} {1}", UiText.Default("Page", "Page"), SelectedRows[0].PageAsText);
			}
		}

		/// <summary>Sets (or with an empty text removes) the bookmark of the selected page.</summary>
		public void SetBookmark(string name)
		{
			if (!CanBookmark)
			{
				return;
			}
			comic.UpdateBookmark(SelectedRows[0].Page, name ?? string.Empty);
			Edited();
		}

		public bool CanRemoveBookmark => CanBookmark && !string.IsNullOrEmpty(Bookmark);

		public void RemoveBookmark() => SetBookmark(string.Empty);

		// -- order

		/// <summary>Move to Top (<c>MoveSelectedPageStart</c>).</summary>
		public void MoveSelectedToTop() => MovePages(0, SelectedPages.ToList());

		/// <summary>Move to Bottom (<c>MoveSelectedPageEnd</c>): front cover pages that are moved away stop being the cover, the page that takes their place becomes one.</summary>
		public void MoveSelectedToBottom()
		{
			ComicPageInfo[] pages = SelectedPages.ToArray();
			if (pages.Length > 0 && pages.All(p => p.PageType == ComicPageType.FrontCover))
			{
				foreach (ComicPageInfo page in pages)
				{
					comic.UpdatePageType(page, ComicPageType.Other);
				}
				if (pages.Length < comic.Pages.Count)
				{
					comic.UpdatePageType(pages.Length, ComicPageType.FrontCover);
				}
			}
			MovePages(comic.PageCount, SelectedPages.ToList());
		}

		/// <summary>Moves pages to a position (drag and drop, Move to Top and Bottom); the moved pages stay selected (<c>MovePages</c>).</summary>
		public void MovePages(int position, IReadOnlyList<ComicPageInfo> pages)
		{
			try
			{
				comic.MovePages(position, pages);
				Refill(keepSelection: false);
				Select(pages.Select(p => p.ImageIndex));
			}
			catch (Exception)
			{
			}
			Edited();
		}

		/// <summary>Reset original Order (<c>ResetPageOrder</c>): the selection is kept.</summary>
		public void ResetOriginalOrder()
		{
			try
			{
				IEnumerable<int> keep = SelectedPages.Select(p => p.ImageIndex).ToList();
				comic.ResetPageSequence();
				Refill(keepSelection: false);
				Select(keep);
			}
			catch (Exception)
			{
			}
			Edited();
		}

		/// <summary>The Reset button of the tab (<c>btResetPages_Click</c>): the original page order, no selection.</summary>
		public void ResetPages()
		{
			comic.ResetPageSequence();
			Refill(keepSelection: false);
			Edited();
		}

		/// <summary>True if the Reset button's menu can order by name (<c>btResetPages_ShowContextMenu</c>: the file's index is complete).</summary>
		public bool CanOrderByName => navigator != null ? navigator.IsIndexRetrievalCompleted : nameOf != null;

		/// <summary>Order by Name (ordinal comparison of the image names).</summary>
		public void OrderByName() => OrderBy((a, b) => string.CompareOrdinal(a, b));

		/// <summary>Order by Name (numeric): numbers inside the names compare as numbers (<c>ExtendedStringComparer</c>).</summary>
		public void OrderByNameNumeric() => OrderBy(ExtendedStringComparer.Compare);

		private void OrderBy(Comparison<string> names)
		{
			Dictionary<int, PageRow> map = natural.ToDictionary(r => r.ImageIndex);
			try
			{
				comic.SortPages((a, b) => names(map[a.ImageIndex].Key, map[b.ImageIndex].Key));
			}
			catch (Exception)
			{
				comic.ResetPageSequence();
			}
			Refill(keepSelection: false);
			Edited();
		}

		/// <summary>Refresh: drops the page and the thumbnail of the selected rows from the Core's pool and the pictures of the list and asks for them again (<c>RefreshImage</c>).</summary>
		public void RefreshSelected()
		{
			foreach (PageRow row in SelectedRows)
			{
				try
				{
					var key = comic.GetThumbnailKey(row.Page);
					services.Pool?.Pages.RefreshImage(new PageKey(key));
					services.Pool?.Thumbs.RefreshImage(key);
				}
				catch (Exception)
				{
				}
			}
			services.Thumbnails?.Invalidate(comic);
			pictures.Clear();
			RowsChanged?.Invoke(this, EventArgs.Empty);
			PicturesChanged?.Invoke(this, EventArgs.Empty);
		}


		// -- Merge Pages

		/// <summary>Merge Pages shows for exactly two selected pages in a book whose pages can be edited (<c>contextPages_Opening</c>).</summary>
		public bool CanMergePages => CanEditPages && navigator != null && SelectedRows.Count == 2;

		/// <summary>
		/// Merge Pages (<c>MergePages</c>): the two selected pages become one picture, first and second in reading order, put into the Core's image pool under the key of the first page
		/// (so a later export finds it), and the second page is marked as deleted. The original checks "not two pages and one of them deleted" before it starts; so does this.
		/// </summary>
		public bool MergePages()
		{
			List<PageRow> selectedPages = SelectedRows.ToList();
			if (navigator == null || selectedPages.Count != 2 && selectedPages.Any(p => p.Info.PageType == ComicPageType.Deleted))
			{
				return false;
			}
			// the reading direction decides which page is on the left; a list sorted descending is reversed already
			bool reversed = navigator.RightToLeftReading == YesNo.Yes || comic.Manga == MangaYesNo.YesAndRightToLeft;
			reversed = reversed && sortColumn >= 0 && !sortAscending ? false : reversed;
			PageRow first = reversed ? selectedPages[selectedPages.Count - 1] : selectedPages[0];
			PageRow second = reversed ? selectedPages[0] : selectedPages[selectedPages.Count - 1];
			Bitmap firstImage = navigator.GetImage(first.ImageIndex);
			Bitmap secondImage = navigator.GetImage(second.ImageIndex);
			Bitmap merged = BitmapExtensions.Merge(firstImage, secondImage);
			if (merged == null)
			{
				return false;
			}
			PageKey pageKey = navigator.GetPageKey(first.ImageIndex);
			ThumbnailKey thumbKey = navigator.GetThumbnailKey(first.ImageIndex);
			ImagePool pool = services.Pool;
			if (pool != null)
			{
				pool.Pages.RefreshImage(pageKey);
				pool.Thumbs.RefreshImage(thumbKey);
				using (pool.Pages.AddImage(pageKey, p => PageImage.CreateFromMerged(merged)))
				using (pool.Thumbs.AddImage(thumbKey, p => ThumbnailImage.CreateFrom(merged, merged.Size)))
				{
				}
			}
			second.SetPageType(ComicPageType.Deleted);
			services.Thumbnails?.Invalidate(comic);
			pictures.Clear();
			Refill(keepSelection: false);
			Edited();
			PicturesChanged?.Invoke(this, EventArgs.Empty);
			return true;
		}

		public void SelectAll() => SetSelection(Enumerable.Range(0, shown.Count));

		public void InvertSelection() => SetSelection(Enumerable.Range(0, shown.Count).Except(Selection));

		private void Select(IEnumerable<int> imageIndexes)
		{
			HashSet<int> wanted = new HashSet<int>(imageIndexes);
			SetSelection(Enumerable.Range(0, shown.Count).Where(i => wanted.Contains(shown[i].ImageIndex)));
		}

		/// <summary>The page (counting from 0) of the first selected row, or <paramref name="fallback"/> (<c>PagesViewSelectedIndexChanged</c>).</summary>
		public int FirstSelectedPage(int fallback)
		{
			PageRow first = SelectedRows.FirstOrDefault();
			return first != null ? comic.TranslateImageIndexToPage(first.ImageIndex) : fallback;
		}

		// ---- writing back --------------------------------------------------------------------------------------------

		/// <summary>
		/// What the dialog's Save does with the pages (<c>SaveBook</c>): once the file's index is complete and the copy has pages, the library book gets the copy's pages and the page
		/// count of the file, and its last page read is kept inside the book. Returns true if the book's pages differ from before.
		/// </summary>
		public bool SavePagesTo(ComicBook target) => SavePagesTo(target, out _);

		/// <summary>
		/// <see cref="SavePagesTo(ComicBook)"/>, and whether the picture of the book's cover is another one afterwards (another front cover page, a turned cover): the grid then asks for
		/// the cover again. Learning the sizes of the pages changes the pages but not the cover.
		/// </summary>
		public bool SavePagesTo(ComicBook target, out bool coverChanged)
		{
			if (target == null)
			{
				throw new ArgumentNullException(nameof(target));
			}
			coverChanged = false;
			if (!IsIndexComplete || comic.Pages.Count == 0)
			{
				return false;
			}
			bool changed = !target.Pages.SequenceEqual(comic.Pages);
			ThumbnailKey before = target.GetFrontCoverThumbnailKey();
			target.SetPages(comic.Pages);
			// ComicPageInfo.Equals does not look at the rotation, so SetPages sees pages that differ in nothing else as equal and keeps the old rotations: they are applied here
			for (int i = 0; i < comic.Pages.Count; i++)
			{
				ComicPageInfo page = comic.Pages[i];
				if (target.GetPage(i).Rotation != page.Rotation)
				{
					target.UpdatePageRotation(i, page.Rotation);
					changed = true;
				}
			}
			target.PageCount = ImageCount;
			target.LastPageRead = Math.Min(target.PageCount - 1, target.LastPageRead);
			coverChanged = changed && !before.Equals(target.GetFrontCoverThumbnailKey());
			return changed;
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			comic.BookChanged -= OnComicChanged;
			if (navigator != null)
			{
				navigator.IndexOfPageReady -= OnIndexReady;
				navigator.IndexRetrievalCompleted -= OnIndexCompleted;
				if (ownsNavigator)
				{
					navigator.Dispose();
				}
			}
			pictures.Dispose();
		}
	}

	/// <summary>The lines of text beside a page's picture in tile mode (<c>ComicTextBuilder.GetTextBlocks(ComicPageInfo ...)</c>), with the texts of the <c>ComicBook</c> section.</summary>
	public static class PageTileText
	{
		private static string T(string key, string english) => UiText.Raw("ComicBook", key, english);

		public static List<TileTextLine> Build(ComicPageInfo info, int page)
		{
			var lines = new List<TileTextLine>();
			if (info.IsEmpty)
			{
				lines.Add(new TileTextLine(T("NoInformation", "No information available"), TileTextStyle.Caption));
				return lines;
			}
			lines.Add(new TileTextLine(string.Format(T("PageText", "Page #{0}"), page + 1), TileTextStyle.Caption));
			lines.Add(new TileTextLine(info.PageTypeAsText, TileTextStyle.Small));
			lines.Add(new TileTextLine(10));
			lines.Add(new TileTextLine(info.ImageFileSize != 0 ? StringUtility.Format(T("PageSize", "Size:\t{0}"), info.ImageFileSizeAsText) : T("UnknownSize", "Unknown Size"), TileTextStyle.Small));
			lines.Add(new TileTextLine(StringUtility.Format(T("Resolution", "Resolution:\t{0} x {1}"), info.ImageWidthAsText, info.ImageHeightAsText), TileTextStyle.Small));
			if (info.Rotation != 0)
			{
				lines.Add(new TileTextLine(StringUtility.Format(T("Rotation", "Rotation:\t{0}"), info.RotationAsText), TileTextStyle.Small));
			}
			lines.Add(new TileTextLine(6));
			if (info.IsBookmark)
			{
				lines.Add(new TileTextLine(StringUtility.Format(T("Bookmark", "Bookmark:\t{0}"), info.Bookmark), TileTextStyle.Small));
			}
			return lines;
		}
	}
}
