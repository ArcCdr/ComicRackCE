using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using cYo.Common;
using cYo.Common.Localize;
using cYo.Common.Text;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO.Cache;

namespace ComicRack.Mac.Editor
{
	/// <summary>The value lists of the editor's text and combo boxes (<c>DefaultLists</c> calls in <c>ComicBookDialog</c>'s constructor).</summary>
	public enum EditorList
	{
		Title,
		Series,
		Writer,
		Genre,
		Tags,
		AlternateSeries,
		StoryArc,
		SeriesGroup,
		Penciller,
		Colorist,
		Inker,
		Letterer,
		CoverArtist,
		Editor,
		Translator,
		Characters,
		Teams,
		MainCharacterOrTeam,
		Locations,
		ScanInformation,
		AgeRating,
		Format,
		Publisher,
		Imprint,
		BookAge,
		BookStore,
		BookOwner,
		BookCondition,
		BookPrice,
		BookLocation,
		CollectionStatus,
	}

	/// <summary>One entry of the language box (<c>LanguageComboBox.LanguageItem</c>): the culture name that is stored, the name shown, a separator line before it.</summary>
	public sealed class LanguageItem
	{
		public LanguageItem(string name, string display, bool separatorBefore = false)
		{
			Name = name;
			Display = display;
			SeparatorBefore = separatorBefore;
		}

		/// <summary>The culture name ("en", "fr", ...) or the empty string for "no language".</summary>
		public string Name { get; }

		public string Display { get; }

		public bool SeparatorBefore { get; }

		public override string ToString() => Display;
	}

	/// <summary>
	/// What the Info dialog edits and how it moves between books: the behaviour of <c>ComicRack/Dialogs/ComicBookDialog.cs</c> without a window. The dialog edits
	/// the library's own book (<see cref="Current"/>): nothing is written while fields change, <see cref="Save"/> (OK, Apply, Previous, Next) writes every
	/// field into the book, Cancel writes nothing. Book properties that change go through the Core's change events, so the browser shows them and the
	/// Core's undo (<c>Database.Undo</c>, marker "Show Info" set by the caller) can take them back as one step.
	///
	/// P5.7a holds the Summary and Details fields; the fields of the other tabs are added by the tasks that build them (P5.7b, P5.7c) next to these, in
	/// <see cref="Load"/> and <see cref="Save"/>. A field that the model does not know is never touched.
	/// </summary>
	public sealed partial class BookEditorModel : ObservableObject
	{
		private readonly AppPreferences preferences;
		private readonly EditorValueLists lists;
		private readonly Func<ComicBook, bool> selectComic;
		private readonly Func<string, ComicBook> createBook;
		private ComicBook current;
		private ComicBook displayComic;
		private string customThumbnailKey;
		private bool loading;

		/// <param name="current">The book to edit.</param>
		/// <param name="allBooks">The books the Previous and Next buttons walk through (the browser's list); null or empty: just <paramref name="current"/>.</param>
		/// <param name="preferences">The settings the dialog reads (<c>UpdateComicFiles</c>, <c>UpdateComicBookFiles</c>, <c>CatalogOnlyForFileless</c>, <c>ShowCustomBookFields</c>, <c>InformationCover3D</c>); null: the defaults.</param>
		/// <param name="lists">The values the combo boxes and the auto-complete offer; null: none.</param>
		/// <param name="selectComic">Called with the book that becomes current so the browser can select it (the original's <c>IComicBrowser.SelectComic</c>).</param>
		/// <param name="createBook">Makes a book object for a file that is not in the library (link to file); null: linking is not available.</param>
		public BookEditorModel(ComicBook current, IReadOnlyList<ComicBook> allBooks, AppPreferences preferences = null, EditorValueLists lists = null,
			Func<ComicBook, bool> selectComic = null, Func<string, ComicBook> createBook = null)
		{
			this.current = current ?? throw new ArgumentNullException(nameof(current));
			this.preferences = preferences ?? new AppPreferences();
			this.lists = lists;
			this.selectComic = selectComic;
			this.createBook = createBook;
			AllBooks = allBooks != null && allBooks.Count > 0 ? allBooks.ToArray() : new[] { current };
			Languages = Array.Empty<LanguageItem>();
			SetCurrentBook(current);
		}

		/// <summary>True while the fields are being filled from a book (the dialog skips what it would do for a change of the user).</summary>
		public bool IsLoading => loading;

		// ---- the books

		/// <summary>The books the dialog walks through, in the browser's order.</summary>
		public ComicBook[] AllBooks { get; }

		/// <summary>The library's book that Save writes to.</summary>
		public ComicBook Current => current;

		/// <summary>The book the dialog shows: <see cref="Current"/>, or a book made from a file the user chose in "Link to File..." (its file path is what Save writes).</summary>
		public ComicBook Display => displayComic;

		/// <summary>The title of the dialog: the caption of the shown book.</summary>
		public string DialogTitle => displayComic.Caption;

		/// <summary>True if there is more than one book (the Previous and Next buttons are shown).</summary>
		public bool HasNavigation => AllBooks.Length > 1;

		public bool CanGoPrevious => Array.IndexOf(AllBooks, current) > 0;

		public bool CanGoNext => Array.IndexOf(AllBooks, current) < AllBooks.Length - 1;

		/// <summary>Raised after the editor has been filled from another book (the first book, Previous, Next, Apply, a script, Link to File).</summary>
		public event EventHandler BookLoaded;

		/// <summary>Raised when the cover has to be drawn again: another book, or another custom thumbnail. The argument is the book that holds the thumbnail key to show.</summary>
		public event EventHandler<ComicBook> CoverChanged;

		// ---- Pages and Colors (P5.7c)

		/// <summary>The Colors tab: the sliders, the white point and the page the viewer shows. Filled with every book (<c>SetDataToEditor</c>), written by <see cref="ApplyTo"/>.</summary>
		public ColorTabModel ColorTab { get; } = new ColorTabModel();

		/// <summary>What the Pages tab needs besides the book: the picture service, the Core's image pool, the UI thread. Set by the owner of the dialog; without it the pages have no pictures.</summary>
		public PageListServices PageServices { get; set; }

		/// <summary>The pages of the shown book while the Pages tab has been used for it (<see cref="EnsurePages"/>), else null.</summary>
		public PageListModel Pages => pages;

		/// <summary>Raised when <see cref="Pages"/> was dropped because another book is shown (the tab clears its list), and when it was made.</summary>
		public event EventHandler PagesChanged;

		/// <summary>True after a save changed what the grid shows of a book's pictures: the pages of the book (the cover can be another page or turned) or its custom thumbnail. The owner refreshes the covers.</summary>
		public bool ThumbnailsChanged => thumbnailBooks.Count > 0;

		/// <summary>The books whose pictures a save changed.</summary>
		public IReadOnlyCollection<ComicBook> ThumbnailBooks => thumbnailBooks;

		private readonly HashSet<ComicBook> thumbnailBooks = new HashSet<ComicBook>();

		private PageListModel pages;

		/// <summary>
		/// The list of pages of the shown book, made on first use (<c>SetComicToEditor</c> makes it for every book; here it waits for the tab, so moving through books does not open each file).
		/// The list works on a copy of the book with a navigator of its own; Save writes its pages into the library's book. Null for a book without a file or one that cannot be opened.
		/// </summary>
		public PageListModel EnsurePages()
		{
			if (pages != null)
			{
				return pages;
			}
			if (!displayComic.IsLinked)
			{
				return null;
			}
			var copy = new ComicBook(displayComic);
			ComicBookNavigator navigator = null;
			try
			{
				navigator = copy.CreateNavigator();
			}
			catch (Exception)
			{
			}
			if (navigator == null)
			{
				return null;
			}
			if (copy.IsDynamicSource)
			{
				PageServices?.Pool?.RefreshLastImage(copy.FilePath);
			}
			pages = new PageListModel(copy, navigator, PageServices, ownsNavigator: true);
			// PagesViewSelectedIndexChanged: the viewer of the Colors tab shows the first selected page, else the front cover of the book
			PageListModel created = pages;
			created.SelectionChanged += (sender, e) =>
			{
				if (ReferenceEquals(pages, created))
				{
					ColorTab.ShowPage(created.FirstSelectedPage(current.FrontCoverPageIndex));
				}
			};
			pages.Start();
			PagesChanged?.Invoke(this, EventArgs.Empty);
			return pages;
		}

		private void DropPages()
		{
			if (pages == null)
			{
				return;
			}
			PageListModel old = pages;
			pages = null;
			old.Dispose();
			PagesChanged?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>Lets go of the pages and their navigator (the dialog closes).</summary>
		public void Close() => DropPages();

		// ---- state that follows the book (SetComicToEditor)

		[ObservableProperty]
		public partial bool IsLinked { get; set; }

		/// <summary>The type, pages and "where" rows of the Summary tab show only for a book with a file.</summary>
		[ObservableProperty]
		public partial bool ShowFileInfo { get; set; }

		/// <summary>Thumbnail... and Link to File... (a book without a file that is a local book) and dropping a picture or a file on the dialog.</summary>
		[ObservableProperty]
		public partial bool ShowThumbnailButtons { get; set; }

		[ObservableProperty]
		public partial bool ShowWhere { get; set; }

		[ObservableProperty]
		public partial bool DetailsEnabled { get; set; }

		[ObservableProperty]
		public partial bool PlotEnabled { get; set; }

		[ObservableProperty]
		public partial bool ColorsEnabled { get; set; }

		[ObservableProperty]
		public partial bool PagesEnabled { get; set; }

		[ObservableProperty]
		public partial bool CatalogEnabled { get; set; }

		[ObservableProperty]
		public partial bool CustomEnabled { get; set; }

		[ObservableProperty]
		public partial bool ShowPagesTab { get; set; }

		[ObservableProperty]
		public partial bool ShowColorsTab { get; set; }

		[ObservableProperty]
		public partial bool ShowCatalogTab { get; set; }

		[ObservableProperty]
		public partial bool ShowCustomTab { get; set; }

		[ObservableProperty]
		public partial bool RatingEnabled { get; set; }

		[ObservableProperty]
		public partial bool CommunityRatingEnabled { get; set; }

		[ObservableProperty]
		public partial bool ProposedEnabled { get; set; }

		[ObservableProperty]
		public partial bool SeriesCompleteEnabled { get; set; }

		/// <summary>Only a book with a file has "Proposed Values" and "Scan Information".</summary>
		[ObservableProperty]
		public partial bool ShowProposedValues { get; set; }

		[ObservableProperty]
		public partial bool ShowDynamicUpdate { get; set; }

		// ---- Summary

		[ObservableProperty]
		public partial double Rating { get; set; }

		[ObservableProperty]
		public partial double CommunityRating { get; set; }

		/// <summary>"CBZ/54,5 MB" (the file format, with the real one on a second line when it differs, and the size).</summary>
		[ObservableProperty]
		public partial string TypeText { get; set; } = string.Empty;

		/// <summary>"3/32 Pages" (the last page read and the page count).</summary>
		[ObservableProperty]
		public partial string PagesText { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string PathText { get; set; } = string.Empty;

		/// <summary>The web address of the book (the link under the Summary tab).</summary>
		[ObservableProperty]
		public partial string WebLink { get; set; } = string.Empty;

		// ---- Details

		[ObservableProperty]
		public partial string Series { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Title { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Volume { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Number { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Count { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Year { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Month { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Day { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Publisher { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Imprint { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Format { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string AlternateSeries { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string AlternateNumber { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string AlternateCount { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string StoryArc { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string SeriesGroup { get; set; } = string.Empty;

		/// <summary>Yes, No or empty (unknown).</summary>
		[ObservableProperty]
		public partial string SeriesComplete { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Writer { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Penciller { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Inker { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Colorist { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string CoverArtist { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Letterer { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Editor { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Translator { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string AgeRating { get; set; } = string.Empty;

		/// <summary>Yes, Yes (Right to Left), No or empty.</summary>
		[ObservableProperty]
		public partial string Manga { get; set; } = string.Empty;

		/// <summary>The culture name of the language, or empty.</summary>
		[ObservableProperty]
		public partial string Language { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string BlackAndWhite { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Genre { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Tags { get; set; } = string.Empty;

		/// <summary>Yes or No (shown for a book that comes from a dynamic source only).</summary>
		[ObservableProperty]
		public partial string EnableDynamicUpdate { get; set; } = string.Empty;

		/// <summary>Yes or No: the proposed values of the file name are shown as prompts of the empty fields.</summary>
		[ObservableProperty]
		public partial string EnableProposed { get; set; } = string.Empty;

		// ---- Plot & Notes (P5.7b)

		/// <summary>The Summary sub-tab (<c>txSummary</c>), shown with the line endings of the Windows edit box.</summary>
		[ObservableProperty]
		public partial string Summary { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Notes { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Review { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Characters { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Teams { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string MainCharacterOrTeam { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Locations { get; set; } = string.Empty;

		/// <summary>Shown for a book with a file only (<see cref="ShowProposedValues"/>).</summary>
		[ObservableProperty]
		public partial string ScanInformation { get; set; } = string.Empty;

		/// <summary>The editable web address (<c>txWeblink</c>); <see cref="WebLink"/> is the link under the Summary tab, which keeps the address the book had when it was shown.</summary>
		[ObservableProperty]
		public partial string Web { get; set; } = string.Empty;

		// ---- Catalog (P5.7b)

		/// <summary>Opened/Read and Pages are the catalog fields of a book without a file (<c>labelOpenedTime</c>, <c>labelPagesAsTextSimple</c>).</summary>
		[ObservableProperty]
		public partial bool ShowFilelessCatalogFields { get; set; }

		[ObservableProperty]
		public partial string BookStore { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string BookPrice { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string BookAge { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string BookCondition { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string BookOwner { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string BookLocation { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string BookCollectionStatus { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string BookNotes { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string Isbn { get; set; } = string.Empty;

		/// <summary>The page count as the catalog edits it (digits; a book without a file only).</summary>
		[ObservableProperty]
		public partial string PagesAsTextSimple { get; set; } = string.Empty;

		/// <summary>When the book was added or bought; <see cref="DateTime.MinValue"/> is "not set" (the box shows nothing).</summary>
		[ObservableProperty]
		public partial DateTime AddedTime { get; set; }

		[ObservableProperty]
		public partial DateTime ReleasedTime { get; set; }

		[ObservableProperty]
		public partial DateTime OpenedTime { get; set; }

		/// <summary>What a <c>DateTimePicker</c> can hold (<c>MinDate</c> and <c>MaxDate</c>); a date outside shows as "not set" and is saved as such.</summary>
		public static DateTime PickerValue(DateTime value)
		{
			return value < new DateTime(1753, 1, 1) || value > new DateTime(9998, 12, 31) ? DateTime.MinValue : value;
		}

		// ---- Custom (P5.7b)

		/// <summary>
		/// The rows of the Custom tab (<c>customValuesData</c>): a row for every custom value key the library knows, in the order of the keys, then an empty row for a new key. Script
		/// values (keys with a point) are kept but hidden unless <see cref="ShowScriptValues"/> (<c>ExtendedSettings.ShowCustomScriptValues</c>, false by default).
		/// </summary>
		public ObservableCollection<CustomValueRow> CustomRows { get; } = new ObservableCollection<CustomValueRow>();

		/// <summary><c>ExtendedSettings.ShowCustomScriptValues</c>: the Custom tab lists the values scripts keep under a key with a point.</summary>
		public bool ShowScriptValues { get; set; }

		private HashSet<string> customKeys;

		// ---- prompt texts (SetPromptTexts)

		public string ProposedSeries { get; private set; } = string.Empty;

		public string ProposedTitle { get; private set; } = string.Empty;

		public string ProposedNumber { get; private set; } = string.Empty;

		public string ProposedCount { get; private set; } = string.Empty;

		public string ProposedYear { get; private set; } = string.Empty;

		public string ProposedVolume { get; private set; } = string.Empty;

		public string ProposedFormat { get; private set; } = string.Empty;

		/// <summary>Raised when the prompt texts changed (the book changed, or the proposed values were switched on or off).</summary>
		public event EventHandler PromptsChanged;

		partial void OnEnableProposedChanged(string value)
		{
			if (!loading)
			{
				SetPromptTexts(current, EditControls.GetYesNo(value) == YesNo.Yes);
			}
		}

		// ---- items of the combo boxes

		/// <summary>Empty, Yes, No (<c>EditControlUtility.InitializeYesNo</c>).</summary>
		public static string[] YesNoItems => new[] { string.Empty, ComicInfo.YesText, ComicInfo.NoText };

		/// <summary>Yes, No (<c>InitializeYesNo(withEmpty: false)</c>).</summary>
		public static string[] YesNoOnlyItems => new[] { ComicInfo.YesText, ComicInfo.NoText };

		/// <summary>Empty, Yes, Yes (Right to Left), No (<c>InitializeMangaYesNo</c>).</summary>
		public static string[] MangaItems => new[] { string.Empty, ComicInfo.YesText, ComicInfo.YesRightToLeftText, ComicInfo.NoText };

		/// <summary>The language box: no language, the languages the library uses, a separator, then every language by name.</summary>
		public IReadOnlyList<LanguageItem> Languages { get; private set; }

		/// <summary>The values a field offers (the drop-down of a combo box, the completions of a text box), from the books of the library and the defaults.</summary>
		public List<string> Values(EditorList list) => lists == null ? new List<string>() : EditorListValues.Get(lists, list);

		// ---- the cover

		/// <summary>The setting "3D display of covers in the Book Info Dialog" (<c>InformationCover3D</c>, PB.38); a click on the cover flips it, closing the dialog saves it.</summary>
		public bool ThreeD
		{
			get => preferences.InformationCover3D;
			set
			{
				if (preferences.InformationCover3D != value)
				{
					preferences.InformationCover3D = value;
					OnPropertyChanged();
				}
			}
		}

		/// <summary>The key of the custom thumbnail of the book, or empty/null for the cover of the book itself.</summary>
		public string CustomThumbnailKey
		{
			get => customThumbnailKey;
			set
			{
				// a book with a file takes its cover from the file
				if (displayComic.IsLinked || value == customThumbnailKey)
				{
					return;
				}
				var book = new ComicBook(displayComic);
				customThumbnailKey = book.CustomThumbnailKey = value;
				OnPropertyChanged();
				CoverChanged?.Invoke(this, book);
			}
		}

		/// <summary>The Reset item of the thumbnail menu.</summary>
		public void ResetThumbnail() => CustomThumbnailKey = null;

		// ---- load

		private void SetCurrentBook(ComicBook comic, bool withRefresh = true)
		{
			selectComic?.Invoke(comic);
			if (withRefresh)
			{
				comic.RefreshInfoFromFile();
			}
			current = comic;
			SetComicToEditor(comic);
			OnPropertyChanged(nameof(CanGoPrevious));
			OnPropertyChanged(nameof(CanGoNext));
			OnPropertyChanged(nameof(HasNavigation));
		}

		private void SetComicToEditor(ComicBook comic)
		{
			loading = true;
			try
			{
				displayComic = comic;
				DropPages();
				bool isLocal = comic.EditMode.IsLocalComic();
				IsLinked = comic.IsLinked;
				ShowFileInfo = comic.IsLinked;
				ShowThumbnailButtons = !comic.IsLinked && isLocal;
				ShowWhere = comic.IsLinked && isLocal;
				customThumbnailKey = comic.CustomThumbnailKey;
				Load(comic);
				bool filelessOrInContainer = !comic.IsLinked || comic.IsInContainer;
				bool canEdit = filelessOrInContainer || preferences.UpdateComicFiles;
				bool canEditProperties = comic.EditMode.CanEditProperties();
				DetailsEnabled = PlotEnabled = ColorsEnabled = canEdit && canEditProperties;
				CommunityRatingEnabled = PagesEnabled = canEdit && comic.EditMode.CanEditPages();
				CatalogEnabled = CustomEnabled = (filelessOrInContainer || preferences.UpdateComicBookFiles) && canEditProperties;
				ShowPagesTab = comic.IsLinked;
				ShowColorsTab = comic.IsLinked;
				ShowCatalogTab = (!comic.IsLinked || !preferences.CatalogOnlyForFileless) && (filelessOrInContainer || preferences.UpdateComicBookFiles);
				ShowCustomTab = preferences.ShowCustomBookFields && (filelessOrInContainer || preferences.UpdateComicBookFiles);
				ShowProposedValues = comic.IsLinked;
				ShowFilelessCatalogFields = !comic.IsLinked;
				RatingEnabled = ProposedEnabled = SeriesCompleteEnabled = filelessOrInContainer || preferences.UpdateComicBookFiles;
				if (!canEditProperties)
				{
					CommunityRatingEnabled = RatingEnabled = false;
				}
				WebLink = comic.Web ?? string.Empty;
			}
			finally
			{
				loading = false;
			}
			OnPropertyChanged(nameof(DialogTitle));
			BookLoaded?.Invoke(this, EventArgs.Empty);
			CoverChanged?.Invoke(this, comic);
		}

		/// <summary>Fills every field from <paramref name="comic"/> (<c>SetDataToEditor</c>).</summary>
		private void Load(ComicBook comic)
		{
			string pages = comic.PagesAsText;
			if (comic.LastPageRead > 0)
			{
				// a book of one page shows "1/1 Pages", not "2/1 Pages"
				pages = $"{comic.LastPageRead + (comic.PageCount == 1 ? 0 : 1)}/{pages}";
			}
			string fileFormat = comic.ActualFileFormat != comic.FileFormat ? $"{comic.ActualFileFormat} ({TR.Default["Actual"]}){Environment.NewLine}{comic.FileFormat}" : comic.FileFormat;
			PagesText = EditControls.Label(pages);
			TypeText = EditControls.Label($"{fileFormat}/{comic.FileSizeAsText}");
			PathText = EditControls.Label(comic.FilePath);
			Rating = comic.Rating;
			CommunityRating = comic.CommunityRating;
			Title = comic.Title ?? string.Empty;
			Series = comic.Series ?? string.Empty;
			Volume = EditControls.Number(comic.Volume);
			Year = EditControls.Number(comic.Year);
			Month = EditControls.Number(comic.Month);
			Day = EditControls.Number(comic.Day);
			Writer = comic.Writer ?? string.Empty;
			Number = comic.Number ?? string.Empty;
			Count = EditControls.Number(comic.Count);
			Genre = comic.Genre ?? string.Empty;
			SeriesComplete = ComicInfo.GetYesNoAsText(comic.SeriesComplete);
			Tags = comic.Tags ?? string.Empty;
			Manga = ComicInfo.GetYesNoAsText(comic.Manga);
			BlackAndWhite = ComicInfo.GetYesNoAsText(comic.BlackAndWhite);
			EnableProposed = ComicInfo.GetYesNoAsText(comic.EnableProposed ? YesNo.Yes : YesNo.No);
			ShowDynamicUpdate = comic.IsDynamicSource;
			EnableDynamicUpdate = ComicInfo.GetYesNoAsText(comic.EnableDynamicUpdate ? YesNo.Yes : YesNo.No);
			AlternateSeries = comic.AlternateSeries ?? string.Empty;
			AlternateNumber = comic.AlternateNumber ?? string.Empty;
			AlternateCount = EditControls.Number(comic.AlternateCount);
			StoryArc = comic.StoryArc ?? string.Empty;
			SeriesGroup = comic.SeriesGroup ?? string.Empty;
			Penciller = comic.Penciller ?? string.Empty;
			Colorist = comic.Colorist ?? string.Empty;
			Inker = comic.Inker ?? string.Empty;
			Letterer = comic.Letterer ?? string.Empty;
			CoverArtist = comic.CoverArtist ?? string.Empty;
			Editor = comic.Editor ?? string.Empty;
			Translator = comic.Translator ?? string.Empty;
			Format = comic.Format ?? string.Empty;
			AgeRating = comic.AgeRating ?? string.Empty;
			Publisher = comic.Publisher ?? string.Empty;
			Imprint = comic.Imprint ?? string.Empty;
			Languages = BuildLanguages(lists?.GetComicFieldList(b => b.LanguageISO) ?? new List<string>());
			OnPropertyChanged(nameof(Languages));
			Language = Languages.Any(l => l.Name == comic.LanguageISO) ? comic.LanguageISO : string.Empty;
			// Plot & Notes, Catalog and Custom (P5.7b); the multi-line boxes get the line endings of the Windows edit box (StringUtility.MakeEditBoxMultiline)
			Summary = StringUtility.MakeEditBoxMultiline(comic.Summary) ?? string.Empty;
			Notes = StringUtility.MakeEditBoxMultiline(comic.Notes) ?? string.Empty;
			Review = StringUtility.MakeEditBoxMultiline(comic.Review) ?? string.Empty;
			Characters = comic.Characters ?? string.Empty;
			Teams = comic.Teams ?? string.Empty;
			MainCharacterOrTeam = comic.MainCharacterOrTeam ?? string.Empty;
			Locations = comic.Locations ?? string.Empty;
			ScanInformation = comic.ScanInformation ?? string.Empty;
			Web = comic.Web ?? string.Empty;
			BookAge = comic.BookAge ?? string.Empty;
			BookStore = comic.BookStore ?? string.Empty;
			BookOwner = comic.BookOwner ?? string.Empty;
			BookCondition = comic.BookCondition ?? string.Empty;
			BookPrice = comic.BookPriceAsText ?? string.Empty;
			BookLocation = comic.BookLocation ?? string.Empty;
			BookCollectionStatus = comic.BookCollectionStatus ?? string.Empty;
			BookNotes = StringUtility.MakeEditBoxMultiline(comic.BookNotes) ?? string.Empty;
			Isbn = comic.ISBN ?? string.Empty;
			PagesAsTextSimple = comic.PagesAsTextSimple ?? string.Empty;
			AddedTime = PickerValue(comic.AddedTime);
			ReleasedTime = PickerValue(comic.ReleasedTime);
			OpenedTime = PickerValue(comic.OpenedTime);
			LoadCustomRows(comic);
			ColorTab.Load(comic);
			SetPromptTexts(comic, comic.EnableProposed);
		}

		private void LoadCustomRows(ComicBook comic)
		{
			CustomRows.Clear();
			foreach (string key in CustomKeys(comic).OrderBy(k => k))
			{
				CustomRows.Add(new CustomValueRow(key, comic.GetCustomValue(key) ?? string.Empty, hidden: !ShowScriptValues && key.Contains('.')));
			}
			CustomRows.Add(new CustomValueRow(string.Empty, string.Empty, hidden: false));
		}

		// Database.CustomValues: the keys of every book of the library (kept up to date as books change), here read once and extended by what this dialog saves
		private IEnumerable<string> CustomKeys(ComicBook comic)
		{
			if (customKeys == null)
			{
				customKeys = new HashSet<string>(lists?.GetCustomValueKeys() ?? Enumerable.Empty<string>());
			}
			foreach (ComicBook book in new[] { comic, current })
			{
				foreach (StringPair pair in book.GetCustomValues())
				{
					customKeys.Add(pair.Key);
				}
			}
			return customKeys;
		}

		/// <summary>The values the library holds under a custom key, offered as completions while a value is typed (<c>customValuesData_EditingControlShowing</c>).</summary>
		public List<string> CustomValueSuggestions(string key)
		{
			return string.IsNullOrEmpty(key) || lists == null ? new List<string>() : lists.GetCustomValues(key);
		}

		/// <summary>Adds the empty row for a new key when the last row has been used (the grid's "new row" moves down when its first cell is edited).</summary>
		public void EnsureNewCustomRow()
		{
			CustomValueRow last = CustomRows.LastOrDefault();
			if (last == null || !last.IsEmpty)
			{
				CustomRows.Add(new CustomValueRow(string.Empty, string.Empty, hidden: false));
			}
		}

		private void SetPromptTexts(ComicBook comic, bool enabled)
		{
			if (enabled)
			{
				ProposedSeries = comic.ProposedSeries ?? string.Empty;
				ProposedTitle = comic.ProposedTitle ?? string.Empty;
				ProposedNumber = comic.ProposedNumber ?? string.Empty;
				ProposedCount = comic.ProposedCountAsText ?? string.Empty;
				ProposedYear = comic.ProposedYearAsText ?? string.Empty;
				ProposedVolume = comic.ProposedNakedVolumeAsText ?? string.Empty;
				ProposedFormat = comic.ProposedFormat ?? string.Empty;
			}
			else
			{
				ProposedSeries = ProposedTitle = ProposedNumber = ProposedCount = ProposedYear = ProposedVolume = ProposedFormat = string.Empty;
			}
			PromptsChanged?.Invoke(this, EventArgs.Empty);
		}

		// LanguageComboBox.FillList: "no language", the languages the books use (sorted by name), a separator, then every neutral culture by name
		internal static IReadOnlyList<LanguageItem> BuildLanguages(IEnumerable<string> usedIso)
		{
			var items = new List<LanguageItem> { new LanguageItem(string.Empty, string.Empty) };
			LanguageItem[] top = usedIso.Where(iso => !string.IsNullOrEmpty(iso)).Distinct().Select(Culture).Where(c => c != null).Select(c => new LanguageItem(c.Name, c.DisplayName)).OrderBy(i => i.Display, StringComparer.CurrentCulture).ToArray();
			bool hasTop = top.Length > 0;
			items.AddRange(top);
			LanguageItem[] all = CultureInfo.GetCultures(CultureTypes.NeutralCultures).Select(c => new LanguageItem(c.Name, string.IsNullOrEmpty(c.Name) ? string.Empty : c.DisplayName))
				.Where(i => !hasTop || !string.IsNullOrEmpty(i.Display)).OrderBy(i => i.Display, StringComparer.CurrentCulture).ToArray();
			if (all.Length > 0)
			{
				// the first entry of the full list starts below the used languages, with a separator line
				items.AddRange(hasTop ? all.Select((i, n) => n == 0 ? new LanguageItem(i.Name, i.Display, separatorBefore: true) : i) : all);
			}
			return items;
		}

		private static CultureInfo Culture(string iso)
		{
			try
			{
				return new CultureInfo(iso);
			}
			catch (CultureNotFoundException)
			{
				return null;
			}
		}

		// ---- save

		/// <summary>Writes every field into the library book (<c>SaveBook()</c>).</summary>
		public void Save() => ApplyTo(current);

		/// <summary>Writes every field into <paramref name="comic"/> (<c>SaveBook(ComicBook)</c>).</summary>
		public void ApplyTo(ComicBook comic)
		{
			comic.Series = Series.Trim();
			comic.Number = Number.Trim();
			comic.Title = Title.Trim();
			comic.AlternateSeries = AlternateSeries.Trim();
			comic.AlternateNumber = AlternateNumber.Trim();
			comic.StoryArc = StoryArc.Trim();
			comic.SeriesGroup = SeriesGroup.Trim();
			comic.Writer = Writer.Trim();
			comic.Summary = Summary.Trim();
			comic.Penciller = Penciller.Trim();
			comic.Inker = Inker.Trim();
			comic.Letterer = Letterer.Trim();
			comic.CoverArtist = CoverArtist.Trim();
			comic.Editor = Editor.Trim();
			comic.Translator = Translator.Trim();
			comic.Colorist = Colorist.Trim();
			comic.Genre = Genre.Trim();
			comic.Characters = Characters.Trim();
			comic.Teams = Teams.Trim();
			comic.MainCharacterOrTeam = MainCharacterOrTeam.Trim();
			comic.Locations = Locations.Trim();
			comic.Notes = Notes.Trim();
			comic.Review = Review.Trim();
			comic.ScanInformation = ScanInformation.Trim();
			comic.Web = Web.Trim();
			comic.Tags = Tags.Trim();
			comic.SeriesComplete = EditControls.GetYesNo(SeriesComplete);
			comic.Count = EditControls.GetNumber(Count);
			comic.Volume = EditControls.GetNumber(Volume);
			comic.Month = EditControls.GetNumber(Month);
			comic.Day = EditControls.GetNumber(Day);
			comic.Year = EditControls.GetNumber(Year);
			comic.AlternateCount = EditControls.GetNumber(AlternateCount);
			comic.FilePath = displayComic.FilePath;
			if (comic.CustomThumbnailKey != CustomThumbnailKey)
			{
				thumbnailBooks.Add(comic);
			}
			comic.CustomThumbnailKey = CustomThumbnailKey;
			string publisher = Publisher.Trim();
			if (comic.Publisher != publisher)
			{
				comic.Publisher = publisher;
			}
			string imprint = Imprint.Trim();
			if (comic.Imprint != imprint)
			{
				comic.Imprint = imprint;
			}
			string format = Format.Trim();
			if (comic.Format != format)
			{
				comic.Format = format;
			}
			string ageRating = AgeRating.Trim();
			if (comic.AgeRating != ageRating)
			{
				comic.AgeRating = ageRating;
			}
			string bookAge = BookAge.Trim();
			if (comic.BookAge != bookAge)
			{
				comic.BookAge = bookAge;
			}
			string bookStore = BookStore.Trim();
			if (comic.BookStore != bookStore)
			{
				comic.BookStore = bookStore;
			}
			string bookOwner = BookOwner.Trim();
			if (comic.BookOwner != bookOwner)
			{
				comic.BookOwner = bookOwner;
			}
			string bookPrice = BookPrice.Trim();
			if (comic.BookPriceAsText != bookPrice)
			{
				comic.BookPrice = EditControls.GetRealNumber(bookPrice);
			}
			string bookCondition = BookCondition.Trim();
			if (comic.BookCondition != bookCondition)
			{
				comic.BookCondition = bookCondition;
			}
			string bookLocation = BookLocation.Trim();
			if (comic.BookLocation != bookLocation)
			{
				comic.BookLocation = bookLocation;
			}
			comic.BookNotes = BookNotes.Trim();
			comic.BookCollectionStatus = BookCollectionStatus.Trim();
			comic.ISBN = Isbn.Trim();
			if (!comic.IsLinked)
			{
				comic.PagesAsTextSimple = PagesAsTextSimple;
				comic.OpenedTime = OpenedTime;
			}
			comic.AddedTime = AddedTime;
			comic.ReleasedTime = ReleasedTime;
			comic.LanguageISO = Language ?? string.Empty;
			comic.Rating = (float)Rating;
			comic.CommunityRating = (float)CommunityRating;
			comic.Manga = EditControls.GetMangaYesNo(Manga);
			comic.BlackAndWhite = EditControls.GetYesNo(BlackAndWhite);
			comic.EnableProposed = EditControls.GetYesNo(EnableProposed) == YesNo.Yes;
			comic.EnableDynamicUpdate = EditControls.GetYesNo(EnableDynamicUpdate) == YesNo.Yes;
			ColorTab.ApplyTo(comic);
			if (pages != null)
			{
				pages.SavePagesTo(comic, out bool coverChanged);
				if (coverChanged)
				{
					thumbnailBooks.Add(comic);
				}
			}
			comic.CustomValuesStore = CustomValuesStore();
		}

		// the rows with a name and a value, in the order of the grid (hidden script values included); not set one by one, so that the book is not marked changed in between
		private string CustomValuesStore()
		{
			string store = string.Empty;
			foreach (CustomValueRow row in CustomRows)
			{
				if (!string.IsNullOrEmpty(row.Name?.Trim()) && !string.IsNullOrEmpty(row.Value))
				{
					store = ComicBook.SetCustomValueInStore(row.Name, row.Value, store);
					customKeys?.Add(row.Name);
				}
			}
			return store;
		}

		// ---- the buttons

		/// <summary>Apply: write the book, then show it again as the file and the Core hold it (<c>btApply_Click</c>).</summary>
		public void Apply()
		{
			Save();
			SetCurrentBook(current);
		}

		/// <summary>OK: write the book (<c>btOK_Click</c>); the dialog closes.</summary>
		public void Ok() => Save();

		/// <summary>Previous: write the book, show the one before it (<c>SelectPreviousBook</c>). False at the first book.</summary>
		public bool GoPrevious()
		{
			int index = Array.IndexOf(AllBooks, current) - 1;
			if (index < 0)
			{
				return false;
			}
			Save();
			SetCurrentBook(AllBooks[index]);
			return true;
		}

		/// <summary>Next: write the book, show the one after it (<c>SelectNextBook</c>). False at the last book.</summary>
		public bool GoNext()
		{
			int index = Array.IndexOf(AllBooks, current) + 1;
			if (index >= AllBooks.Length)
			{
				return false;
			}
			Save();
			SetCurrentBook(AllBooks[index]);
			return true;
		}

		/// <summary>True if "Link to File..." can make a book object (the host is open).</summary>
		public bool CanLinkFile => createBook != null;

		/// <summary>
		/// Link to File... (<c>LinkFile</c>): the shown book becomes a copy of this one with the chosen file behind it; Save writes the file path into the book.
		/// Returns false with <paramref name="problem"/> set when the file cannot be used; nothing happens for a book that has a file.
		/// </summary>
		public bool LinkFile(string file, Func<string, ComicBook> inLibrary, out string problem)
		{
			problem = null;
			if (current.IsLinked || createBook == null)
			{
				return false;
			}
			if (inLibrary != null && inLibrary(file) != null)
			{
				problem = UiTextHooks.FileUsed;
				return false;
			}
			ComicBook linked = createBook(file);
			if (linked == null)
			{
				return false;
			}
			linked.SetInfo(current.GetInfo());
			SetComicToEditor(linked);
			return true;
		}

		/// <summary>
		/// Runs a script (<c>ExecuteScript</c>): the script gets a copy of the book with the fields written into it and changes it; the editor shows the result.
		/// </summary>
		public void RunScript(Action<ComicBook> script)
		{
			var copy = new ComicBook(current);
			ApplyTo(copy);
			script(copy);
			loading = true;
			try
			{
				Load(copy);
			}
			finally
			{
				loading = false;
			}
		}
	}

	/// <summary>One row of the Custom tab: the key of a custom value and its value for the book.</summary>
	public sealed partial class CustomValueRow : ObservableObject
	{
		public CustomValueRow(string name, string value, bool hidden)
		{
			Name = name ?? string.Empty;
			Value = value ?? string.Empty;
			Hidden = hidden;
		}

		[ObservableProperty]
		public partial string Name { get; set; }

		[ObservableProperty]
		public partial string Value { get; set; }

		/// <summary>A script value (key with a point) while the Custom tab does not list those: the row stays in the list and is saved with it.</summary>
		public bool Hidden { get; }

		public bool IsEmpty => string.IsNullOrEmpty(Name) && string.IsNullOrEmpty(Value);
	}

	/// <summary>The texts the model needs that come from the shared message section.</summary>
	internal static class UiTextHooks
	{
		public static string FileUsed => UiText.Message("ErrorFileUsed", "This file is already used in the Library");
	}

	/// <summary><c>EditControlUtility</c>: how the fields turn numbers and Yes/No into text and back.</summary>
	public static class EditControls
	{
		/// <summary>A number as the editor shows it: empty for "unknown" (-1).</summary>
		public static string Number(int value) => value == -1 ? string.Empty : value.ToString();

		/// <summary>The number in a field: negative and anything that is not a number is -1 ("unknown").</summary>
		public static int GetNumber(string text)
		{
			if (int.TryParse(text, out int result))
			{
				return result >= 0 ? result : -1;
			}
			return -1;
		}

		/// <summary>A price: negative and anything that is not a number is -1.</summary>
		public static float GetRealNumber(string text)
		{
			if (float.TryParse(text, out float result))
			{
				return result < 0f ? -1f : result;
			}
			return -1f;
		}

		/// <summary>A label shows "Unknown" for nothing (<c>EditControlUtility.SetLabel</c>).</summary>
		public static string Label(string text) => string.IsNullOrEmpty(text) ? "Unknown" : text;

		public static YesNo GetYesNo(string text)
		{
			if (string.Equals(text, ComicInfo.YesText, StringComparison.OrdinalIgnoreCase))
			{
				return YesNo.Yes;
			}
			return string.Equals(text, ComicInfo.NoText, StringComparison.OrdinalIgnoreCase) ? YesNo.No : YesNo.Unknown;
		}

		public static MangaYesNo GetMangaYesNo(string text)
		{
			if (string.Equals(text, ComicInfo.YesText, StringComparison.OrdinalIgnoreCase))
			{
				return MangaYesNo.Yes;
			}
			if (string.Equals(text, ComicInfo.YesRightToLeftText, StringComparison.OrdinalIgnoreCase))
			{
				return MangaYesNo.YesAndRightToLeft;
			}
			return string.Equals(text, ComicInfo.NoText, StringComparison.OrdinalIgnoreCase) ? MangaYesNo.No : MangaYesNo.Unknown;
		}
	}
}
