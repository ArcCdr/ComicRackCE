using System;
using System.Collections.Generic;
using cYo.Common.Collections;
using cYo.Common.Mathematics;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO.Network;

namespace ComicRack.Mac.Host
{
	/// <summary>
	/// The settings the Core reads, with the defaults of the WinForms class (ComicRack/Config/Settings.cs). It is not
	/// persisted in the MVP. Setters raise the same change events and clamp like the original.
	/// "Settings.cs:N" below is the line of the field initializer, or of the constant, in that file.
	/// </summary>
	public sealed class MacSettings : ICacheSettings, IComicUpdateSettings, ISharesSettings, IVirtualTagSettings
	{
		// Settings.cs:110-122
		public const int MinimumMemoryPageCacheCount = 20;
		public const int MaximumMemoryPageCacheCount = 100;
		public const int DefaultMemoryPageCacheCount = 25;
		public const int MinimumMemoryThumbnailCacheMB = 5;
		public const int MaximumMemoryThumbnailCacheMB = 500;
		public const int DefaultMemoryThumbnailCacheMB = 50;
		public const int DefaultInternetCacheSizeMB = 1000;

		private bool thumbCacheEnabled = true; // Settings.cs:316
		private int thumbCacheSizeMB = 500; // Settings.cs:318
		private bool pageCacheEnabled = true; // Settings.cs:320
		private int pageCacheSizeMB = 500; // Settings.cs:322
		private bool internetCacheEnabled = true; // Settings.cs:324
		private int internetCacheSizeMB = DefaultInternetCacheSizeMB; // Settings.cs:326
		private int memoryThumbCacheSizeMB = DefaultMemoryThumbnailCacheMB; // Settings.cs:328
		private int memoryPageCacheCount = DefaultMemoryPageCacheCount; // Settings.cs:330
		private bool memoryThumbCacheOptimized = true; // Settings.cs:332
		private bool memoryPageCacheOptimized = true; // Settings.cs:334
		private bool updateComicFiles; // Settings.cs:202
		private bool updateComicBookFiles; // Settings.cs:204
		private bool autoUpdateComicsFiles; // Settings.cs:206
		private bool lookForShared = true; // Settings.cs:158
		private readonly SmartList<ComicLibraryServerConfig> shares = new SmartList<ComicLibraryServerConfig>(); // Settings.cs:156
		private readonly List<VirtualTag> virtualTags = new List<VirtualTag>(); // Settings.cs:2420

		public event EventHandler CacheSettingsChanged;

		public bool ThumbCacheEnabled
		{
			get { return thumbCacheEnabled; }
			set { Set(ref thumbCacheEnabled, value); }
		}

		public int ThumbCacheSizeMB
		{
			get { return thumbCacheSizeMB; }
			set { Set(ref thumbCacheSizeMB, value); }
		}

		public bool PageCacheEnabled
		{
			get { return pageCacheEnabled; }
			set { Set(ref pageCacheEnabled, value); }
		}

		public int PageCacheSizeMB
		{
			get { return pageCacheSizeMB; }
			set { Set(ref pageCacheSizeMB, value); }
		}

		public bool InternetCacheEnabled
		{
			get { return internetCacheEnabled; }
			set { Set(ref internetCacheEnabled, value); }
		}

		public int InternetCacheSizeMB
		{
			get { return internetCacheSizeMB; }
			set { Set(ref internetCacheSizeMB, value); }
		}

		/// <summary>Clamped to 5..500, as in Settings.cs:2196.</summary>
		public int MemoryThumbCacheSizeMB
		{
			get { return memoryThumbCacheSizeMB; }
			set { Set(ref memoryThumbCacheSizeMB, value.Clamp(MinimumMemoryThumbnailCacheMB, MaximumMemoryThumbnailCacheMB)); }
		}

		/// <summary>Clamped to 20..100, as in Settings.cs:2217.</summary>
		public int MemoryPageCacheCount
		{
			get { return memoryPageCacheCount; }
			set { Set(ref memoryPageCacheCount, value.Clamp(MinimumMemoryPageCacheCount, MaximumMemoryPageCacheCount)); }
		}

		public bool MemoryThumbCacheOptimized
		{
			get { return memoryThumbCacheOptimized; }
			set { Set(ref memoryThumbCacheOptimized, value); }
		}

		public bool MemoryPageCacheOptimized
		{
			get { return memoryPageCacheOptimized; }
			set { Set(ref memoryPageCacheOptimized, value); }
		}

		// IComicUpdateSettings: in the WinForms class each of the three also has its own change event, which nothing in the Core subscribes to.
		public bool UpdateComicFiles
		{
			get { return updateComicFiles; }
			set { updateComicFiles = value; }
		}

		public bool UpdateComicBookFiles
		{
			get { return updateComicBookFiles; }
			set { updateComicBookFiles = value; }
		}

		public bool AutoUpdateComicsFiles
		{
			get { return autoUpdateComicsFiles; }
			set { autoUpdateComicsFiles = value; }
		}

		// ISharesSettings: the MVP has no library sharing, so the list stays empty.
		public bool LookForShared
		{
			get { return lookForShared; }
			set { lookForShared = value; }
		}

		public SmartList<ComicLibraryServerConfig> Shares => shares;

		/// <summary>Settings.cs:2662 (constructor): empty.</summary>
		public string PrivateListingPassword => string.Empty;

		/// <summary>Settings.cs:2661 (constructor): empty.</summary>
		public string ExternalServerAddress => string.Empty;

		// IVirtualTagSettings
		public List<VirtualTag> VirtualTags => virtualTags;

		private void Set<T>(ref T field, T value)
		{
			if (!EqualityComparer<T>.Default.Equals(field, value))
			{
				field = value;
				CacheSettingsChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}
}
