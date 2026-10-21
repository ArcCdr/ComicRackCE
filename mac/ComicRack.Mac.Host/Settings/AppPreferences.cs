using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using cYo.Common.Mathematics;

namespace ComicRack.Mac.Host
{
	/// <summary>
	/// Marks a property of <see cref="AppPreferences"/> that has no counterpart in <c>ComicRack/Config/Settings.cs</c> (window and view state of the
	/// Mac app). Every other public property mirrors a property of the original with the same name, type and default; <c>SettingsDefaultsTests</c>
	/// parses the original and compares them.
	/// </summary>
	[AttributeUsage(AttributeTargets.Property)]
	public sealed class MacOnlyAttribute : Attribute
	{
	}

	/// <summary>
	/// The frame of a window as it is saved: the position of its top left corner in screen pixels, the size of its client area in device independent
	/// pixels and whether it was zoomed. <see cref="X"/> and <see cref="Y"/> are those of <c>Window.Position</c>, <see cref="Width"/> and <see cref="Height"/> those of
	/// <c>Window.ClientSize</c>. For a zoomed window they are the frame it returns to.
	/// </summary>
	[Serializable]
	public sealed class WindowFrame : IEquatable<WindowFrame>
	{
		[XmlAttribute]
		public int X { get; set; }

		[XmlAttribute]
		public int Y { get; set; }

		[XmlAttribute]
		public double Width { get; set; }

		[XmlAttribute]
		public double Height { get; set; }

		[XmlAttribute]
		[DefaultValue(false)]
		public bool Maximized { get; set; }

		public WindowFrame()
		{
		}

		public WindowFrame(int x, int y, double width, double height, bool maximized = false)
		{
			X = x;
			Y = y;
			Width = width;
			Height = height;
			Maximized = maximized;
		}

		/// <summary>True if the size can be used for a window (finite and positive).</summary>
		public bool IsUsable => Width > 0 && Height > 0 && !double.IsNaN(Width) && !double.IsNaN(Height) && !double.IsInfinity(Width) && !double.IsInfinity(Height);

		public bool Equals(WindowFrame other)
		{
			return other != null && X == other.X && Y == other.Y && Width == other.Width && Height == other.Height && Maximized == other.Maximized;
		}

		public override bool Equals(object obj) => Equals(obj as WindowFrame);

		public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height, Maximized);

		public override string ToString() => $"{X},{Y} {Width}x{Height}{(Maximized ? " zoomed" : string.Empty)}";
	}

	/// <summary>
	/// What the Mac app remembers between runs, saved by <see cref="SettingsStore"/> as <c>&lt;data&gt;/Settings.xml</c> (DD3: the Mac's own file; the portable
	/// parts of a Windows <c>Config.xml</c> are imported by P5.28b). It has two kinds of properties:
	/// <list type="bullet">
	/// <item>the options that mirror <c>ComicRack/Config/Settings.cs</c> (R9): the same name, type and default as the original (every Preferences > Behavior
	/// option, the reader overlays, the caches, the language). The field citations name the line of the original's field initializer (or of the property when
	/// it is an automatic one); <c>SettingsDefaultsTests</c> compares the defaults with the file. An option that does not work yet is kept and saved all the same,
	/// its ledger row (PB.01 to PB.40) stays <c>todo</c> until the task that owns it makes it work;</item>
	/// <item>the Mac's window and view state (<see cref="MacOnlyAttribute"/>): window frames, the side panel, the grid, the reader.</item>
	/// </list>
	/// A value equal to its <c>DefaultValue</c> is not written, so a new default of a later version reaches a file that never changed it. A setter raises
	/// <see cref="PropertyChanged"/> only when the value changes. The clamps of the original are in the setters, so a value read from a file is clamped too.
	/// </summary>
	[Serializable]
	[XmlRoot("Settings")]
	public sealed class AppPreferences : INotifyPropertyChanged
	{
		// Settings.cs:110-122
		public const int MinimumMemoryPageCacheCount = 20;
		public const int MaximumMemoryPageCacheCount = 100;
		public const int DefaultMemoryPageCacheCount = 25;
		public const int MinimumMemoryThumbnailCacheMB = 5;
		public const int MaximumMemoryThumbnailCacheMB = 500;
		public const int DefaultMemoryThumbnailCacheMB = 50;
		public const int DefaultInternetCacheSizeMB = 1000;

		/// <summary>Raised (on the thread of the setter) after a property changed.</summary>
		public event PropertyChangedEventHandler PropertyChanged;

		private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
		{
			if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
			{
				field = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
			}
		}

		// ---- Starting ComicRack (PB.01 to PB.05)

		private bool showSplash = true; // Settings.cs:214

		[DefaultValue(true)]
		public bool ShowSplash
		{
			get { return showSplash; }
			set { Set(ref showSplash, value); }
		}

		private bool openLastFile = true; // Settings.cs:216

		[DefaultValue(true)]
		public bool OpenLastFile
		{
			get { return openLastFile; }
			set { Set(ref openLastFile, value); }
		}

		private bool scanStartup; // Settings.cs:218

		[DefaultValue(false)]
		public bool ScanStartup
		{
			get { return scanStartup; }
			set { Set(ref scanStartup, value); }
		}

		private bool updateWebComicsStartup; // Settings.cs:220

		[DefaultValue(false)]
		public bool UpdateWebComicsStartup
		{
			get { return updateWebComicsStartup; }
			set { Set(ref updateWebComicsStartup, value); }
		}

		private bool newsStartup = true; // Settings.cs:222

		[DefaultValue(true)]
		public bool NewsStartup
		{
			get { return newsStartup; }
			set { Set(ref newsStartup, value); }
		}

		// ---- Opening a Book (PB.06 to PB.09)

		private bool openLastPage = true; // Settings.cs:226

		[DefaultValue(true)]
		public bool OpenLastPage
		{
			get { return openLastPage; }
			set { Set(ref openLastPage, value); }
		}

		private bool closeBrowserOnOpen; // Settings.cs:228

		[DefaultValue(false)]
		public bool CloseBrowserOnOpen
		{
			get { return closeBrowserOnOpen; }
			set { Set(ref closeBrowserOnOpen, value); }
		}

		private bool addToLibraryOnOpen; // Settings.cs:230

		[DefaultValue(false)]
		public bool AddToLibraryOnOpen
		{
			get { return addToLibraryOnOpen; }
			set { Set(ref addToLibraryOnOpen, value); }
		}

		private bool openInNewTab; // Settings.cs:232

		[DefaultValue(false)]
		public bool OpenInNewTab
		{
			get { return openInNewTab; }
			set { Set(ref openInNewTab, value); }
		}

		// ---- Reading (PB.10 to PB.18)

		private bool hideCursorFullScreen = true; // Settings.cs:234

		[DefaultValue(true)]
		public bool HideCursorFullScreen
		{
			get { return hideCursorFullScreen; }
			set { Set(ref hideCursorFullScreen, value); }
		}

		private bool autoNavigateComics = true; // Settings.cs:236

		[DefaultValue(true)]
		public bool AutoNavigateComics
		{
			get { return autoNavigateComics; }
			set { Set(ref autoNavigateComics, value); }
		}

		private bool pageChangeDelay = true; // Settings.cs:252

		[DefaultValue(true)]
		public bool PageChangeDelay
		{
			get { return pageChangeDelay; }
			set { Set(ref pageChangeDelay, value); }
		}

		private bool scrollingDoesBrowse = true; // Settings.cs:254

		[DefaultValue(true)]
		public bool ScrollingDoesBrowse
		{
			get { return scrollingDoesBrowse; }
			set { Set(ref scrollingDoesBrowse, value); }
		}

		private bool resetZoomOnPageChange; // Settings.cs:256

		[DefaultValue(false)]
		public bool ResetZoomOnPageChange
		{
			get { return resetZoomOnPageChange; }
			set { Set(ref resetZoomOnPageChange, value); }
		}

		private bool zoomInOutOnPageChange = true; // Settings.cs:258

		[DefaultValue(true)]
		public bool ZoomInOutOnPageChange
		{
			get { return zoomInOutOnPageChange; }
			set { Set(ref zoomInOutOnPageChange, value); }
		}

		private bool blendWhilePaging; // Settings.cs:262

		[DefaultValue(false)]
		public bool BlendWhilePaging
		{
			get { return blendWhilePaging; }
			set { Set(ref blendWhilePaging, value); }
		}

		private bool autoMinimalGui; // Settings.cs:296

		[DefaultValue(false)]
		public bool AutoMinimalGui
		{
			get { return autoMinimalGui; }
			set { Set(ref autoMinimalGui, value); }
		}

		private bool autoShowQuickReview; // Settings.cs:2414

		[DefaultValue(false)]
		public bool AutoShowQuickReview
		{
			get { return autoShowQuickReview; }
			set { Set(ref autoShowQuickReview, value); }
		}

		// ---- Remove, and the questions that stay off (BC.23, DL.30)

		private bool moveFilesToRecycleBin; // Settings.cs:535 (automatic property)

		[DefaultValue(false)]
		public bool MoveFilesToRecycleBin
		{
			get { return moveFilesToRecycleBin; }
			set { Set(ref moveFilesToRecycleBin, value); }
		}

		private bool alsoRemoveFromLibrary; // Settings.cs:543

		[DefaultValue(false)]
		public bool AlsoRemoveFromLibrary
		{
			get { return alsoRemoveFromLibrary; }
			set { Set(ref alsoRemoveFromLibrary, value); }
		}

		private bool alsoRemoveFromLibraryFiltered; // Settings.cs:551

		[DefaultValue(false)]
		public bool AlsoRemoveFromLibraryFiltered
		{
			get { return alsoRemoveFromLibraryFiltered; }
			set { Set(ref alsoRemoveFromLibraryFiltered, value); }
		}

		private bool removeFilesfromDatabase; // Settings.cs:559

		[DefaultValue(false)]
		public bool RemoveFilesfromDatabase
		{
			get { return removeFilesfromDatabase; }
			set { Set(ref removeFilesfromDatabase, value); }
		}

		private HiddenMessageBoxes hiddenMessageBoxes; // Settings.cs:200

		[DefaultValue(HiddenMessageBoxes.None)]
		public HiddenMessageBoxes HiddenMessageBoxes
		{
			get { return hiddenMessageBoxes; }
			set { Set(ref hiddenMessageBoxes, value); }
		}

		// ---- Right to Left (PB.19, PB.20)

		private bool trueRightToLeftReading; // Settings.cs:1601

		[DefaultValue(false)]
		public bool TrueRightToLeftReading
		{
			get { return trueRightToLeftReading; }
			set { Set(ref trueRightToLeftReading, value); }
		}

		private bool leftRightMovementReversed; // Settings.cs:268

		[DefaultValue(false)]
		public bool LeftRightMovementReversed
		{
			get { return leftRightMovementReversed; }
			set { Set(ref leftRightMovementReversed, value); }
		}

		// ---- Browser (PB.21 to PB.30)

		private bool showToolTips; // Settings.cs:270

		[DefaultValue(false)]
		public bool ShowToolTips
		{
			get { return showToolTips; }
			set { Set(ref showToolTips, value); }
		}

		private bool showSearchLinks = true; // Settings.cs:272

		[DefaultValue(true)]
		public bool ShowSearchLinks
		{
			get { return showSearchLinks; }
			set { Set(ref showSearchLinks, value); }
		}

		private bool fadeInThumbnails = true; // Settings.cs:274

		[DefaultValue(true)]
		public bool FadeInThumbnails
		{
			get { return fadeInThumbnails; }
			set { Set(ref fadeInThumbnails, value); }
		}

		private bool dogEarThumbnails = true; // Settings.cs:276

		[DefaultValue(true)]
		public bool DogEarThumbnails
		{
			get { return dogEarThumbnails; }
			set { Set(ref dogEarThumbnails, value); }
		}

		private bool numericRatingThumbnails = true; // Settings.cs:278

		[DefaultValue(true)]
		public bool NumericRatingThumbnails
		{
			get { return numericRatingThumbnails; }
			set { Set(ref numericRatingThumbnails, value); }
		}

		private bool localQuickSearch = true; // Settings.cs:280

		[DefaultValue(true)]
		public bool LocalQuickSearch
		{
			get { return localQuickSearch; }
			set { Set(ref localQuickSearch, value); }
		}

		private bool coverThumbnailsSameSize; // Settings.cs:282

		[DefaultValue(false)]
		public bool CoverThumbnailsSameSize
		{
			get { return coverThumbnailsSameSize; }
			set { Set(ref coverThumbnailsSameSize, value); }
		}

		private bool commonListStackLayout; // Settings.cs:284

		[DefaultValue(false)]
		public bool CommonListStackLayout
		{
			get { return commonListStackLayout; }
			set { Set(ref commonListStackLayout, value); }
		}

		private bool alwaysDisplayBrowserDockingGrip; // Settings.cs:300

		[DefaultValue(false)]
		public bool AlwaysDisplayBrowserDockingGrip
		{
			get { return alwaysDisplayBrowserDockingGrip; }
			set { Set(ref alwaysDisplayBrowserDockingGrip, value); }
		}

		private bool disableDragDrop; // Settings.cs:304

		[DefaultValue(false)]
		public bool DisableDragDrop
		{
			get { return disableDragDrop; }
			set { Set(ref disableDragDrop, value); }
		}

		// ---- Application (PB.31 to PB.39)

		private bool showQuickOpen = true; // Settings.cs:286

		[DefaultValue(true)]
		public bool ShowQuickOpen
		{
			get { return showQuickOpen; }
			set { Set(ref showQuickOpen, value); }
		}

		private bool catalogOnlyForFileless = true; // Settings.cs:288

		[DefaultValue(true)]
		public bool CatalogOnlyForFileless
		{
			get { return catalogOnlyForFileless; }
			set { Set(ref catalogOnlyForFileless, value); }
		}

		private bool showCustomBookFields; // Settings.cs:290

		[DefaultValue(false)]
		public bool ShowCustomBookFields
		{
			get { return showCustomBookFields; }
			set { Set(ref showCustomBookFields, value); }
		}

		private bool minimizeToTray; // Settings.cs:292

		[DefaultValue(false)]
		public bool MinimizeToTray
		{
			get { return minimizeToTray; }
			set { Set(ref minimizeToTray, value); }
		}

		private bool closeMinimizeToTray = true; // Settings.cs:294

		[DefaultValue(true)]
		public bool CloseMinimizesToTray
		{
			get { return closeMinimizeToTray; }
			set { Set(ref closeMinimizeToTray, value); }
		}

		private bool animatePanels = true; // Settings.cs:298

		[DefaultValue(true)]
		public bool AnimatePanels
		{
			get { return animatePanels; }
			set { Set(ref animatePanels, value); }
		}

		private bool showMainMenuNoComicOpen = true; // Settings.cs:306

		[DefaultValue(true)]
		public bool ShowMainMenuNoComicOpen
		{
			get { return showMainMenuNoComicOpen; }
			set { Set(ref showMainMenuNoComicOpen, value); }
		}

		private bool informationCover3D = true; // Settings.cs:308

		[DefaultValue(true)]
		public bool InformationCover3D
		{
			get { return informationCover3D; }
			set { Set(ref informationCover3D, value); }
		}

		private bool newBooksChecked = true; // Settings.cs:314

		[DefaultValue(true)]
		public bool NewBooksChecked
		{
			get { return newBooksChecked; }
			set { Set(ref newBooksChecked, value); }
		}

		// ---- Import and Export (PB.40)

		private bool exportedListsContainFilenames; // Settings.cs:2345

		[DefaultValue(false)]
		public bool ExportedListsContainFilenames
		{
			get { return exportedListsContainFilenames; }
			set { Set(ref exportedListsContainFilenames, value); }
		}

		// ---- Reader overlays (Preferences > Reader > Overlays, PR.04)

		private bool showCurrentPageOverlay = true; // Settings.cs:238

		[DefaultValue(true)]
		public bool ShowCurrentPageOverlay
		{
			get { return showCurrentPageOverlay; }
			set { Set(ref showCurrentPageOverlay, value); }
		}

		private bool showVisiblePagePartOverlay = true; // Settings.cs:240

		[DefaultValue(true)]
		public bool ShowVisiblePagePartOverlay
		{
			get { return showVisiblePagePartOverlay; }
			set { Set(ref showVisiblePagePartOverlay, value); }
		}

		private bool showStatusOverlay = true; // Settings.cs:242

		[DefaultValue(true)]
		public bool ShowStatusOverlay
		{
			get { return showStatusOverlay; }
			set { Set(ref showStatusOverlay, value); }
		}

		private bool showNavigationOverlay = true; // Settings.cs:244

		[DefaultValue(true)]
		public bool ShowNavigationOverlay
		{
			get { return showNavigationOverlay; }
			set { Set(ref showNavigationOverlay, value); }
		}

		private bool navigationOverlayOnTop; // Settings.cs:246

		[DefaultValue(false)]
		public bool NavigationOverlayOnTop
		{
			get { return navigationOverlayOnTop; }
			set { Set(ref navigationOverlayOnTop, value); }
		}

		private bool currentPageShowsName; // Settings.cs:248

		[DefaultValue(false)]
		public bool CurrentPageShowsName
		{
			get { return currentPageShowsName; }
			set { Set(ref currentPageShowsName, value); }
		}

		// ---- Caches (the Core reads them through ICacheSettings; Preferences > Advanced > Caches, PR.21)

		private bool thumbCacheEnabled = true; // Settings.cs:316

		[DefaultValue(true)]
		public bool ThumbCacheEnabled
		{
			get { return thumbCacheEnabled; }
			set { Set(ref thumbCacheEnabled, value); }
		}

		private int thumbCacheSizeMB = 500; // Settings.cs:318

		[DefaultValue(500)]
		public int ThumbCacheSizeMB
		{
			get { return thumbCacheSizeMB; }
			set { Set(ref thumbCacheSizeMB, value); }
		}

		private bool pageCacheEnabled = true; // Settings.cs:320

		[DefaultValue(true)]
		public bool PageCacheEnabled
		{
			get { return pageCacheEnabled; }
			set { Set(ref pageCacheEnabled, value); }
		}

		private int pageCacheSizeMB = 500; // Settings.cs:322

		[DefaultValue(500)]
		public int PageCacheSizeMB
		{
			get { return pageCacheSizeMB; }
			set { Set(ref pageCacheSizeMB, value); }
		}

		private bool internetCacheEnabled = true; // Settings.cs:324

		[DefaultValue(true)]
		public bool InternetCacheEnabled
		{
			get { return internetCacheEnabled; }
			set { Set(ref internetCacheEnabled, value); }
		}

		private int internetCacheSizeMB = DefaultInternetCacheSizeMB; // Settings.cs:326

		[DefaultValue(DefaultInternetCacheSizeMB)]
		public int InternetCacheSizeMB
		{
			get { return internetCacheSizeMB; }
			set { Set(ref internetCacheSizeMB, value); }
		}

		private int memoryThumbCacheSizeMB = DefaultMemoryThumbnailCacheMB; // Settings.cs:328

		[DefaultValue(DefaultMemoryThumbnailCacheMB)]
		public int MemoryThumbCacheSizeMB
		{
			get { return memoryThumbCacheSizeMB; }
			set { Set(ref memoryThumbCacheSizeMB, value.Clamp(MinimumMemoryThumbnailCacheMB, MaximumMemoryThumbnailCacheMB)); }
		}

		private int memoryPageCacheCount = DefaultMemoryPageCacheCount; // Settings.cs:330

		[DefaultValue(DefaultMemoryPageCacheCount)]
		public int MemoryPageCacheCount
		{
			get { return memoryPageCacheCount; }
			set { Set(ref memoryPageCacheCount, value.Clamp(MinimumMemoryPageCacheCount, MaximumMemoryPageCacheCount)); }
		}

		private bool memoryThumbCacheOptimized = true; // Settings.cs:332

		[DefaultValue(true)]
		public bool MemoryThumbCacheOptimized
		{
			get { return memoryThumbCacheOptimized; }
			set { Set(ref memoryThumbCacheOptimized, value); }
		}

		private bool memoryPageCacheOptimized = true; // Settings.cs:334

		[DefaultValue(true)]
		public bool MemoryPageCacheOptimized
		{
			get { return memoryPageCacheOptimized; }
			set { Set(ref memoryPageCacheOptimized, value); }
		}

		// ---- Updating book files (IComicUpdateSettings)

		private bool updateComicFiles; // Settings.cs:202

		[DefaultValue(false)]
		public bool UpdateComicFiles
		{
			get { return updateComicFiles; }
			set { Set(ref updateComicFiles, value); }
		}

		private bool updateComicBookFiles; // Settings.cs:204

		[DefaultValue(false)]
		public bool UpdateComicBookFiles
		{
			get { return updateComicBookFiles; }
			set { Set(ref updateComicBookFiles, value); }
		}

		private bool autoUpdateComicsFiles; // Settings.cs:206

		[DefaultValue(false)]
		public bool AutoUpdateComicsFiles
		{
			get { return autoUpdateComicsFiles; }
			set { Set(ref autoUpdateComicsFiles, value); }
		}

		// ---- Other

		private bool lookForShared = true; // Settings.cs:158

		[DefaultValue(true)]
		public bool LookForShared
		{
			get { return lookForShared; }
			set { Set(ref lookForShared, value); }
		}

		private string cultureName; // Settings.cs:2336

		[DefaultValue(null)]
		public string CultureName
		{
			get { return cultureName; }
			set { Set(ref cultureName, value); }
		}

		private bool trackCurrentPage = true; // Settings.cs:264

		/// <summary>Read &gt; Track current Page (MR.13): when off, turning pages does not move the current page and the last page read of the book (<c>ComicBookNavigator.TrackCurrentPage</c>).</summary>
		[DefaultValue(true)]
		public bool TrackCurrentPage
		{
			get { return trackCurrentPage; }
			set { Set(ref trackCurrentPage, value); }
		}

		// ---- Library tree counters (LT.01, PR.10)

		private bool displayLibraryGauges = true; // Settings.cs:310

		[DefaultValue(true)]
		public bool DisplayLibraryGauges
		{
			get { return displayLibraryGauges; }
			set { Set(ref displayLibraryGauges, value); }
		}

		private LibraryGauges libraryGaugesFormat = LibraryGauges.Default; // Settings.cs:312

		[DefaultValue(LibraryGauges.Default)]
		public LibraryGauges LibraryGaugesFormat
		{
			get { return libraryGaugesFormat; }
			set { Set(ref libraryGaugesFormat, value); }
		}

		// ---- Mac only: window and view state

		private WindowFrame libraryWindow;
		private WindowFrame readerWindow;
		private double sidebarWidth = DefaultSidebarWidth;
		private bool sidebarVisible = true;
		private double thumbnailSize = DefaultThumbnailSize;
		private string sortBy = DefaultSortBy;
		private bool sortDescending;
		private string readerFit = DefaultReaderFit;
		private string readerLayout = DefaultReaderLayout;
		private bool rightToLeftDefault;
		private string lastFolder;
		private string[] lastOpenBooks;
		private double browserDockHeight = DefaultBrowserDockHeight;
		private bool browserDockExpanded = true;
		private bool readerUndocked;

		/// <summary>The width of the side panel when it is open (<c>ComicExplorerView</c>'s <c>SizableContainer</c>, 252).</summary>
		public const double DefaultSidebarWidth = 252;

		/// <summary>The height of a cover in the grid, in device independent pixels (the status bar slider, 80 to 360).</summary>
		public const double DefaultThumbnailSize = 200;

		/// <summary>The name of the sort of the grid (<c>BookSort</c>).</summary>
		public const string DefaultSortBy = "Series";

		/// <summary>The height of the browser docked at the bottom of the main window when it is open (<c>MainForm.mainViewContainer</c>, 250 high, grip included).</summary>
		public const double DefaultBrowserDockHeight = 250;

		/// <summary>The name of the fit mode a reader starts with (<c>ImageFitMode</c>; Fit = "Fit All").</summary>
		public const string DefaultReaderFit = "Fit";

		/// <summary>The name of the page layout a reader starts with (<c>PageLayoutMode</c>).</summary>
		public const string DefaultReaderLayout = "Single";

		/// <summary>Where the library window was, or null before it was ever moved.</summary>
		[MacOnly]
		public WindowFrame LibraryWindow
		{
			get { return libraryWindow; }
			set { Set(ref libraryWindow, value); }
		}

		/// <summary>Where the last reader window was, or null. A new reader opens there (moved so that windows do not cover each other).</summary>
		[MacOnly]
		public WindowFrame ReaderWindow
		{
			get { return readerWindow; }
			set { Set(ref readerWindow, value); }
		}

		[MacOnly]
		[DefaultValue(DefaultSidebarWidth)]
		public double SidebarWidth
		{
			get { return sidebarWidth; }
			set { Set(ref sidebarWidth, value); }
		}

		[MacOnly]
		[DefaultValue(true)]
		public bool SidebarVisible
		{
			get { return sidebarVisible; }
			set { Set(ref sidebarVisible, value); }
		}

		[MacOnly]
		[DefaultValue(DefaultThumbnailSize)]
		public double ThumbnailSize
		{
			get { return thumbnailSize; }
			set { Set(ref thumbnailSize, value); }
		}

		[MacOnly]
		[DefaultValue(DefaultSortBy)]
		public string SortBy
		{
			get { return sortBy; }
			set { Set(ref sortBy, value); }
		}

		/// <summary>The sort of the grid runs from the end (the Arrange button flips it, <c>ItemSortOrder</c> Descending).</summary>
		[MacOnly]
		[DefaultValue(false)]
		public bool SortDescending
		{
			get { return sortDescending; }
			set { Set(ref sortDescending, value); }
		}

		[MacOnly]
		[DefaultValue(DefaultReaderFit)]
		public string ReaderFit
		{
			get { return readerFit; }
			set { Set(ref readerFit, value); }
		}

		[MacOnly]
		[DefaultValue(DefaultReaderLayout)]
		public string ReaderLayout
		{
			get { return readerLayout; }
			set { Set(ref readerLayout, value); }
		}

		/// <summary>The reading direction of a book that does not say (its manga setting is unknown); the last one the reader chose for such a book.</summary>
		[MacOnly]
		[DefaultValue(false)]
		public bool RightToLeftDefault
		{
			get { return rightToLeftDefault; }
			set { Set(ref rightToLeftDefault, value); }
		}

		/// <summary>The last folder that was added to the library; the folder picker starts there.</summary>
		[MacOnly]
		[DefaultValue(null)]
		public string LastFolder
		{
			get { return lastFolder; }
			set { Set(ref lastFolder, value); }
		}

		/// <summary>
		/// The files of the books that were open at the last quit, in tab order (<c>Settings.LastOpenFiles</c>, saved at close). With Reopen Books from last session
		/// (<c>OpenLastFile</c>, PB.02) the next start opens them again. Named differently from the original's list, which has no default value to compare.
		/// </summary>
		[MacOnly]
		[DefaultValue(null)]
		public string[] LastOpenBooks
		{
			get { return lastOpenBooks; }
			set { Set(ref lastOpenBooks, value != null && value.Length == 0 ? null : value); }
		}

		/// <summary>The height of the browser dock (a drag of its grip sets it); the browser is docked at the bottom until P5.32b adds the other docking modes.</summary>
		[MacOnly]
		[DefaultValue(DefaultBrowserDockHeight)]
		public double BrowserDockHeight
		{
			get { return browserDockHeight; }
			set { Set(ref browserDockHeight, value); }
		}

		/// <summary>False when the browser dock was collapsed (<c>BrowserVisible</c>; a click on the grip, Esc in the reader).</summary>
		[MacOnly]
		[DefaultValue(true)]
		public bool BrowserDockExpanded
		{
			get { return browserDockExpanded; }
			set { Set(ref browserDockExpanded, value); }
		}

		/// <summary>True when the reader was in its own window at the last quit (<c>ReaderUndocked</c>; Display &gt; Reader in own Window).</summary>
		[MacOnly]
		[DefaultValue(false)]
		public bool ReaderUndocked
		{
			get { return readerUndocked; }
			set { Set(ref readerUndocked, value); }
		}
	}
}
