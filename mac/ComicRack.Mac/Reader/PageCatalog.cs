using System;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Reader
{
	/// <summary>
	/// What the layout needs to know about one page besides its pixels: the page types and position that decide whether it may share a
	/// spread (<c>ComicPageInfo.IsSinglePageType</c>, <c>PagePosition</c>), and its size if it is known (<c>ImageWidth</c> and
	/// <c>ImageHeight</c> of the Core's page info, which the Core fills in when it first reads a page).
	/// </summary>
	public readonly struct PageTraits
	{
		public PageTraits(bool isFrontCover, bool isSingleType, ComicPagePosition position, int width, int height)
		{
			IsFrontCover = isFrontCover;
			IsSingleType = isSingleType;
			Position = position;
			Width = width;
			Height = height;
		}

		/// <summary>The page is typed FrontCover (<c>IsSingleRightPageType</c>): shown alone, on the right.</summary>
		public bool IsFrontCover { get; }

		/// <summary>The page is typed FrontCover or BackCover (<c>IsSinglePageType</c>): never shares a spread.</summary>
		public bool IsSingleType { get; }

		/// <summary>Near = must start a spread; Far = belongs on the far side of one.</summary>
		public ComicPagePosition Position { get; }

		/// <summary>The size in pixels, or 0 when it is not known yet.</summary>
		public int Width { get; }

		public int Height { get; }

		public bool HasSize => Width > 0 && Height > 0;

		/// <summary>A page wider than tall is a double page (<c>ComicPageInfo.IsDoublePage</c>).</summary>
		public bool IsWide => HasSize && Width > Height;

		/// <summary>What the Core gives a book without page info: the first page is the front cover, the rest is story.</summary>
		public static PageTraits Default(int page)
		{
			return new PageTraits(page == 0, page == 0, ComicPagePosition.Default, 0, 0);
		}

		public PageTraits WithSize(int width, int height)
		{
			return new PageTraits(IsFrontCover, IsSingleType, Position, width, height);
		}
	}

	/// <summary>
	/// The pages of one open book as the layout sees them: the traits the book's page info gives, plus the sizes that decoding revealed.
	/// The Core knows a page's size only after it has read the page once (and keeps it in the database afterwards), and the WinForms
	/// display decides a pairing from the decoded image itself; so the reader learns every size it decodes (<see cref="Learn"/>, which
	/// <see cref="PageService"/> calls from its decoding threads) and a layout asks again when <see cref="Version"/> changed.
	/// Thread safe.
	/// </summary>
	public sealed class PageCatalog
	{
		private readonly object gate = new object();
		private readonly Func<int, PageTraits> source;
		private readonly int[] widths;
		private readonly int[] heights;
		private readonly bool[] seenWide; // what <see cref="Get"/> last told a caller about each page: what a pairing in use is built on
		private long version;

		public PageCatalog(int count, Func<int, PageTraits> source = null)
		{
			Count = Math.Max(0, count);
			this.source = source ?? PageTraits.Default;
			widths = new int[Count];
			heights = new int[Count];
			seenWide = new bool[Count];
		}

		public int Count { get; }

		/// <summary>Changes whenever <see cref="Learn"/> reveals something new about a page.</summary>
		public long Version
		{
			get
			{
				lock (gate)
				{
					return version;
				}
			}
		}

		public PageTraits Get(int page)
		{
			if (page < 0 || page >= Count)
			{
				return default(PageTraits);
			}
			PageTraits traits;
			try
			{
				traits = source(page);
			}
			catch (Exception)
			{
				traits = PageTraits.Default(page);
			}
			lock (gate)
			{
				if (widths[page] > 0 && heights[page] > 0)
				{
					traits = traits.WithSize(widths[page], heights[page]);
				}
				seenWide[page] = traits.IsWide;
			}
			return traits;
		}

		/// <summary>Records the decoded size of a page. Returns true if it differs from what was known (the pairing may change).</summary>
		public bool Learn(int page, int width, int height)
		{
			if (page < 0 || page >= Count || width <= 0 || height <= 0)
			{
				return false;
			}
			lock (gate)
			{
				if (widths[page] == width && heights[page] == height)
				{
					return false;
				}
				widths[page] = width;
				heights[page] = height;
				// Only a change of the wide/narrow property matters for pairing, and it matters against what the pairing in use was
				// built on (the Core may have learned the size meanwhile, which nobody tells us); the sizes themselves matter for placing.
				if (seenWide[page] != width > height)
				{
					seenWide[page] = width > height;
					version++;
				}
				return true;
			}
		}
	}
}
