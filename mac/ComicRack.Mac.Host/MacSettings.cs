using System;
using System.Collections.Generic;
using cYo.Common.Collections;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO.Network;

namespace ComicRack.Mac.Host
{
	/// <summary>
	/// The settings the Core reads, with the defaults of the WinForms class (ComicRack/Config/Settings.cs). The values live in
	/// <see cref="AppPreferences"/> (which keeps the citations of the original's lines and is saved by <see cref="SettingsStore"/>); this class is the
	/// view of them that the Core's interfaces ask for. Setters raise the same change event and clamp like the original.
	/// </summary>
	public sealed class MacSettings : ICacheSettings, IComicUpdateSettings, ISharesSettings, IVirtualTagSettings
	{
		public const int MinimumMemoryPageCacheCount = AppPreferences.MinimumMemoryPageCacheCount;
		public const int MaximumMemoryPageCacheCount = AppPreferences.MaximumMemoryPageCacheCount;
		public const int DefaultMemoryPageCacheCount = AppPreferences.DefaultMemoryPageCacheCount;
		public const int MinimumMemoryThumbnailCacheMB = AppPreferences.MinimumMemoryThumbnailCacheMB;
		public const int MaximumMemoryThumbnailCacheMB = AppPreferences.MaximumMemoryThumbnailCacheMB;
		public const int DefaultMemoryThumbnailCacheMB = AppPreferences.DefaultMemoryThumbnailCacheMB;
		public const int DefaultInternetCacheSizeMB = AppPreferences.DefaultInternetCacheSizeMB;

		private static readonly HashSet<string> cacheProperties = new HashSet<string>
		{
			nameof(AppPreferences.ThumbCacheEnabled), nameof(AppPreferences.ThumbCacheSizeMB), nameof(AppPreferences.PageCacheEnabled), nameof(AppPreferences.PageCacheSizeMB),
			nameof(AppPreferences.InternetCacheEnabled), nameof(AppPreferences.InternetCacheSizeMB), nameof(AppPreferences.MemoryThumbCacheSizeMB), nameof(AppPreferences.MemoryPageCacheCount),
			nameof(AppPreferences.MemoryThumbCacheOptimized), nameof(AppPreferences.MemoryPageCacheOptimized),
		};

		private readonly AppPreferences preferences;
		private readonly SmartList<ComicLibraryServerConfig> shares = new SmartList<ComicLibraryServerConfig>(); // Settings.cs:156
		private readonly List<VirtualTag> virtualTags = new List<VirtualTag>(); // Settings.cs:2420

		/// <summary>The defaults of the original, in preferences of its own (tests, tools).</summary>
		public MacSettings()
			: this(new AppPreferences())
		{
		}

		/// <summary>The Core's view of <paramref name="preferences"/>: what the user changes there reaches the Core, and the other way round.</summary>
		public MacSettings(AppPreferences preferences)
		{
			this.preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
			preferences.PropertyChanged += (sender, e) =>
			{
				if (cacheProperties.Contains(e.PropertyName))
				{
					CacheSettingsChanged?.Invoke(this, EventArgs.Empty);
				}
			};
		}

		/// <summary>The preferences behind these settings.</summary>
		public AppPreferences Preferences => preferences;

		public event EventHandler CacheSettingsChanged;

		public bool ThumbCacheEnabled
		{
			get { return preferences.ThumbCacheEnabled; }
			set { preferences.ThumbCacheEnabled = value; }
		}

		public int ThumbCacheSizeMB
		{
			get { return preferences.ThumbCacheSizeMB; }
			set { preferences.ThumbCacheSizeMB = value; }
		}

		public bool PageCacheEnabled
		{
			get { return preferences.PageCacheEnabled; }
			set { preferences.PageCacheEnabled = value; }
		}

		public int PageCacheSizeMB
		{
			get { return preferences.PageCacheSizeMB; }
			set { preferences.PageCacheSizeMB = value; }
		}

		public bool InternetCacheEnabled
		{
			get { return preferences.InternetCacheEnabled; }
			set { preferences.InternetCacheEnabled = value; }
		}

		public int InternetCacheSizeMB
		{
			get { return preferences.InternetCacheSizeMB; }
			set { preferences.InternetCacheSizeMB = value; }
		}

		/// <summary>Clamped to 5..500, as in Settings.cs:2196.</summary>
		public int MemoryThumbCacheSizeMB
		{
			get { return preferences.MemoryThumbCacheSizeMB; }
			set { preferences.MemoryThumbCacheSizeMB = value; }
		}

		/// <summary>Clamped to 20..100, as in Settings.cs:2217.</summary>
		public int MemoryPageCacheCount
		{
			get { return preferences.MemoryPageCacheCount; }
			set { preferences.MemoryPageCacheCount = value; }
		}

		public bool MemoryThumbCacheOptimized
		{
			get { return preferences.MemoryThumbCacheOptimized; }
			set { preferences.MemoryThumbCacheOptimized = value; }
		}

		public bool MemoryPageCacheOptimized
		{
			get { return preferences.MemoryPageCacheOptimized; }
			set { preferences.MemoryPageCacheOptimized = value; }
		}

		// IComicUpdateSettings: in the WinForms class each of the three also has its own change event, which nothing in the Core subscribes to.
		public bool UpdateComicFiles
		{
			get { return preferences.UpdateComicFiles; }
			set { preferences.UpdateComicFiles = value; }
		}

		public bool UpdateComicBookFiles
		{
			get { return preferences.UpdateComicBookFiles; }
			set { preferences.UpdateComicBookFiles = value; }
		}

		public bool AutoUpdateComicsFiles
		{
			get { return preferences.AutoUpdateComicsFiles; }
			set { preferences.AutoUpdateComicsFiles = value; }
		}

		// ISharesSettings: the MVP has no library sharing, so the list stays empty.
		public bool LookForShared
		{
			get { return preferences.LookForShared; }
			set { preferences.LookForShared = value; }
		}

		public SmartList<ComicLibraryServerConfig> Shares => shares;

		/// <summary>Settings.cs:2662 (constructor): empty.</summary>
		public string PrivateListingPassword => string.Empty;

		/// <summary>Settings.cs:2661 (constructor): empty.</summary>
		public string ExternalServerAddress => string.Empty;

		// IVirtualTagSettings
		public List<VirtualTag> VirtualTags => virtualTags;
	}
}
