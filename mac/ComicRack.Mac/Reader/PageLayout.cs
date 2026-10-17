using System;
using System.Collections.Generic;
using Avalonia;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Display;

namespace ComicRack.Mac.Reader
{
	/// <summary>Where the pages of one reading position go in the window, and how far the content can be scrolled.</summary>
	public sealed class PageArrangement
	{
		public static readonly PageArrangement Empty = new PageArrangement(new Rect[0], 0, 0);

		public PageArrangement(Rect[] pages, double scroll, double maxScroll)
			: this(pages, scroll, maxScroll, 0, 0, 1, default(Rect))
		{
		}

		public PageArrangement(Rect[] pages, double scroll, double maxScroll, double scrollX, double maxScrollX, double scale, Rect content)
		{
			Pages = pages;
			Scroll = scroll;
			MaxScroll = maxScroll;
			ScrollX = scrollX;
			MaxScrollX = maxScrollX;
			Scale = scale;
			Content = content;
		}

		/// <summary>One rectangle per page, in device independent pixels, relative to the window's reading area (may extend beyond it when scrolled).</summary>
		public Rect[] Pages { get; }

		/// <summary>The vertical scroll that was applied, clamped to 0..<see cref="MaxScroll"/>.</summary>
		public double Scroll { get; }

		/// <summary>How far the content can be scrolled down: 0 if everything fits.</summary>
		public double MaxScroll { get; }

		/// <summary>The horizontal scroll that was applied (how far the content is moved to the left), clamped to 0..<see cref="MaxScrollX"/>.</summary>
		public double ScrollX { get; }

		/// <summary>How far the content can be scrolled to the right: 0 if everything fits.</summary>
		public double MaxScrollX { get; }

		/// <summary>Device independent pixels per image pixel of the content (the fit scale times the zoom; a spread is scaled as one image).</summary>
		public double Scale { get; }

		/// <summary>The whole content (all pages, with the blank half of a lone page in a spread), in the same coordinates as <see cref="Pages"/>.</summary>
		public Rect Content { get; }
	}

	/// <summary>
	/// How pages are grouped and placed: the seam through which the single page, the two-page spread, right-to-left reading and zoom
	/// reach the view model and the view. A <i>position</i> is the number of the first page of what is on screen. The layout knows
	/// which pages a position shows and how positions follow each other; the view model only moves between positions, asks the page
	/// service for <see cref="PagesAt"/> and the pages around (<see cref="LayoutExtensions.Window"/>), and draws the rectangles from
	/// <see cref="Arrange"/>.
	/// </summary>
	public interface IPageLayout
	{
		string Name { get; }

		/// <summary>The pages shown at <paramref name="position"/>, in book order (empty if the book has no pages).</summary>
		int[] PagesAt(int position, int pageCount);

		/// <summary>The valid position nearest to <paramref name="position"/>: for a spread, the first page of the spread that contains it.</summary>
		int Normalize(int position, int pageCount);

		int First(int pageCount);

		int Last(int pageCount);

		/// <summary>The position after <paramref name="position"/>, or -1 at the end.</summary>
		int Next(int position, int pageCount);

		/// <summary>The position before <paramref name="position"/>, or -1 at the start.</summary>
		int Previous(int position, int pageCount);

		/// <summary>
		/// Places the pages of <paramref name="position"/> (<paramref name="pageSizes"/> in image pixels, in the order of <see cref="PagesAt"/>)
		/// in a viewport of the given size. The content (one page, or the whole spread as one image) is scaled by the fit mode and then
		/// by <paramref name="zoom"/> (1 = the fit, larger = enlarged); <paramref name="scroll"/> says how far the content is moved to the
		/// left and up (the result is clamped, and an axis on which the content is smaller than the viewport is centred instead).
		/// </summary>
		PageArrangement Arrange(Size viewport, int position, IReadOnlyList<Size> pageSizes, ImageFitMode fit, double zoom, Vector scroll);
	}

	/// <summary>The scale and clamping every layout shares: one image (a page or a whole spread) in a viewport.</summary>
	public static class ContentPlacement
	{
		private const double Epsilon = 1e-6;

		/// <summary>
		/// The scale of the fit mode, following the WinForms display (<c>ImageDisplayControl.GetScale</c>): <see cref="ImageFitMode.FitWidth"/>
		/// the width of the viewport, <see cref="ImageFitMode.FitHeight"/> its height (a wider content is panned sideways), everything else
		/// the whole content (the smaller of the two; the WinForms default, called Best Fit in the Mac menu).
		/// </summary>
		public static double FitScale(Size viewport, Size content, ImageFitMode fit)
		{
			double scaleWidth = viewport.Width / content.Width;
			double scaleHeight = viewport.Height / content.Height;
			switch (fit)
			{
				case ImageFitMode.FitWidth:
					return scaleWidth;
				case ImageFitMode.FitHeight:
					return scaleHeight;
				default:
					return Math.Min(scaleWidth, scaleHeight);
			}
		}

		/// <summary>
		/// Places <paramref name="content"/> (in image pixels). Returns the rectangle of the whole content and the scroll that was
		/// applied, with how far each axis can scroll. A degenerate size gives an empty rectangle.
		/// </summary>
		public static PageArrangement Place(Size viewport, Size content, ImageFitMode fit, double zoom, Vector scroll, Rect[] pagesInContent, out Rect rect)
		{
			rect = default(Rect);
			if (content.Width <= 0 || content.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0)
			{
				return new PageArrangement(new Rect[pagesInContent.Length], 0, 0);
			}
			if (!(zoom > 0) || double.IsInfinity(zoom))
			{
				zoom = 1;
			}
			double scale = FitScale(viewport, content, fit) * zoom;
			double width = content.Width * scale, height = content.Height * scale;
			double maxX = width - viewport.Width > Epsilon ? width - viewport.Width : 0;
			double maxY = height - viewport.Height > Epsilon ? height - viewport.Height : 0;
			double scrollX = double.IsNaN(scroll.X) ? 0 : Math.Max(0, Math.Min(scroll.X, maxX));
			double scrollY = double.IsNaN(scroll.Y) ? 0 : Math.Max(0, Math.Min(scroll.Y, maxY));
			double x = maxX > 0 ? -scrollX : (viewport.Width - width) / 2;
			double y = maxY > 0 ? -scrollY : (viewport.Height - height) / 2;
			rect = new Rect(x, y, width, height);
			var pages = new Rect[pagesInContent.Length];
			for (int i = 0; i < pages.Length; i++)
			{
				Rect p = pagesInContent[i];
				pages[i] = new Rect(x + p.X * scale, y + p.Y * scale, p.Width * scale, p.Height * scale);
			}
			return new PageArrangement(pages, scrollY, maxY, scrollX, maxX, scale, rect);
		}
	}

	public static class LayoutExtensions
	{
		/// <summary>
		/// The pages worth having decoded around <paramref name="position"/>, most important first: the ones on screen, those of the next
		/// <paramref name="ahead"/> positions, then those of the previous <paramref name="behind"/> positions.
		/// </summary>
		public static IReadOnlyList<int> Window(this IPageLayout layout, int position, int pageCount, int ahead = 2, int behind = 1)
		{
			var pages = new List<int>();
			if (pageCount <= 0)
			{
				return pages;
			}
			position = layout.Normalize(position, pageCount);
			pages.AddRange(layout.PagesAt(position, pageCount));
			int p = position;
			for (int i = 0; i < ahead; i++)
			{
				p = layout.Next(p, pageCount);
				if (p < 0)
				{
					break;
				}
				AddAll(pages, layout.PagesAt(p, pageCount));
			}
			p = position;
			for (int i = 0; i < behind; i++)
			{
				p = layout.Previous(p, pageCount);
				if (p < 0)
				{
					break;
				}
				AddAll(pages, layout.PagesAt(p, pageCount));
			}
			return pages;
		}

		private static void AddAll(List<int> into, int[] pages)
		{
			foreach (int page in pages)
			{
				if (!into.Contains(page))
				{
					into.Add(page);
				}
			}
		}
	}

	/// <summary>
	/// One page at a time. The fit modes follow the WinForms display (<c>ImageDisplayControl.GetScale</c>): <see cref="ImageFitMode.Fit"/>
	/// shows the whole page (the smaller of the two scales; the Mac menu calls it Best Fit and it is the WinForms default),
	/// <see cref="ImageFitMode.FitWidth"/> the width of the window (a taller page scrolls), <see cref="ImageFitMode.FitHeight"/> its height
	/// (a wider page is panned sideways). The other values of the Core's enum behave as Fit. The page is centred on an axis where it
	/// is smaller than the window; pixel snapping is the job of <see cref="ReaderPainter"/>.
	/// </summary>
	public sealed class SinglePageLayout : IPageLayout
	{
		public static readonly SinglePageLayout Instance = new SinglePageLayout();

		private SinglePageLayout()
		{
		}

		public string Name => "Single page";

		public int[] PagesAt(int position, int pageCount)
		{
			return pageCount <= 0 ? new int[0] : new[] { Normalize(position, pageCount) };
		}

		public int Normalize(int position, int pageCount)
		{
			return pageCount <= 0 ? 0 : Math.Max(0, Math.Min(position, pageCount - 1));
		}

		public int First(int pageCount) => 0;

		public int Last(int pageCount) => Math.Max(0, pageCount - 1);

		public int Next(int position, int pageCount)
		{
			return position + 1 < pageCount ? position + 1 : -1;
		}

		public int Previous(int position, int pageCount)
		{
			return position > 0 && pageCount > 0 ? Math.Min(position, pageCount) - 1 : -1;
		}

		public PageArrangement Arrange(Size viewport, int position, IReadOnlyList<Size> pageSizes, ImageFitMode fit, double zoom, Vector scroll)
		{
			if (pageSizes == null || pageSizes.Count == 0)
			{
				return PageArrangement.Empty;
			}
			Size page = pageSizes[0];
			return ContentPlacement.Place(viewport, page, fit, zoom, scroll, new[] { new Rect(0, 0, page.Width, page.Height) }, out Rect _);
		}
	}

	/// <summary>
	/// Two pages side by side, as the WinForms display's Double and DoubleAdaptive layouts do
	/// (<c>ComicDisplayControl.GetImageInfo</c>, <c>DisplayNextPage</c>, <c>DisplayPreviousPage</c>, <c>DrawImage</c>), with one difference:
	/// the WinForms display pairs from the page it is on, so going back can land on a different pairing than going forward; this layout
	/// pairs the book once from its first page (greedily) and every position is the first page of one group of that pairing, so the
	/// same spreads come up in both directions.
	/// <list type="bullet">
	/// <item>A page pairs with the next one unless either is typed front or back cover (a cover is alone), either is wide (a double
	/// page is alone), or the next one is positioned Near (it starts a spread). Wide means width greater than height, from the
	/// Core's page info or, once decoded, from the picture: <see cref="PageCatalog.Learn"/> changes the pairing for the pages that
	/// follow.</item>
	/// <item>The two pages are scaled to the height of the taller one and the pair is fitted, zoomed and scrolled as one image.</item>
	/// <item>A page that is alone and not wide: in the adaptive layout it is shown as a single page; in the other it takes half a spread,
	/// the other half stays blank: the front cover on the right, a closing back cover on the left, any other on the right (on the
	/// left when reading right to left). A wide page always fills the spread.</item>
	/// <item>Right to left: the pair is mirrored (the first page on the right), the front cover stays on the right.</item>
	/// </list>
	/// A book of one page is shown as a single page. The layout holds the pairing between calls and rebuilds it when the catalog learns
	/// something or the page count changes; use it from the UI thread.
	/// </summary>
	public sealed class SpreadLayout : IPageLayout
	{
		private readonly PageCatalog catalog;
		private readonly bool adaptive;
		private long builtVersion = -1;
		private int builtCount = -1;
		private int[] starts = new int[0];
		private int[] groupOf = new int[0];

		public SpreadLayout(PageCatalog catalog, bool adaptive, bool rightToLeft = false)
		{
			this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
			this.adaptive = adaptive;
			RightToLeft = rightToLeft;
		}

		public string Name => adaptive ? "Two pages (adaptive)" : "Two pages";

		public bool Adaptive => adaptive;

		/// <summary>The order of the pages in a spread: the first page goes on the right.</summary>
		public bool RightToLeft { get; set; }

		public int[] PagesAt(int position, int pageCount)
		{
			if (pageCount <= 0)
			{
				return new int[0];
			}
			Ensure(pageCount);
			int group = groupOf[Clamp(position, pageCount)];
			int first = starts[group];
			int end = group + 1 < starts.Length ? starts[group + 1] : pageCount;
			var pages = new int[end - first];
			for (int i = 0; i < pages.Length; i++)
			{
				pages[i] = first + i;
			}
			return pages;
		}

		public int Normalize(int position, int pageCount)
		{
			if (pageCount <= 0)
			{
				return 0;
			}
			Ensure(pageCount);
			return starts[groupOf[Clamp(position, pageCount)]];
		}

		public int First(int pageCount) => 0;

		public int Last(int pageCount)
		{
			if (pageCount <= 0)
			{
				return 0;
			}
			Ensure(pageCount);
			return starts[starts.Length - 1];
		}

		public int Next(int position, int pageCount)
		{
			if (pageCount <= 0)
			{
				return -1;
			}
			Ensure(pageCount);
			int group = groupOf[Clamp(position, pageCount)];
			return group + 1 < starts.Length ? starts[group + 1] : -1;
		}

		public int Previous(int position, int pageCount)
		{
			if (pageCount <= 0)
			{
				return -1;
			}
			Ensure(pageCount);
			int group = groupOf[Clamp(position, pageCount)];
			if (position > starts[group] && position < pageCount)
			{
				return starts[group]; // inside a spread: back to its start
			}
			return group > 0 ? starts[group - 1] : -1;
		}

		private static int Clamp(int position, int pageCount)
		{
			return Math.Max(0, Math.Min(position, pageCount - 1));
		}

		/// <summary>True if pages <paramref name="a"/> and <paramref name="a"/> + 1 are shown together.</summary>
		public bool CanPair(int a, int pageCount)
		{
			int b = a + 1;
			if (a < 0 || b >= pageCount)
			{
				return false;
			}
			PageTraits first = catalog.Get(a), second = catalog.Get(b);
			return !first.IsSingleType && !second.IsSingleType && !first.IsWide && !second.IsWide && second.Position != ComicPagePosition.Near;
		}

		private void Ensure(int pageCount)
		{
			long version = catalog.Version;
			if (version == builtVersion && pageCount == builtCount)
			{
				return;
			}
			var list = new System.Collections.Generic.List<int>();
			var map = new int[pageCount];
			int page = 0;
			while (page < pageCount)
			{
				bool pair = CanPair(page, pageCount);
				map[page] = list.Count;
				if (pair)
				{
					map[page + 1] = list.Count;
				}
				list.Add(page);
				page += pair ? 2 : 1;
			}
			starts = list.ToArray();
			groupOf = map;
			builtVersion = version;
			builtCount = pageCount;
		}

		/// <summary>True if a page that is alone takes half a spread (and so is placed on one side).</summary>
		private bool HalfSpread(Size size, int pageCount)
		{
			return !adaptive && pageCount > 1 && size.Height > size.Width;
		}

		private bool LoneOnRight(int page, int pageCount)
		{
			PageTraits traits = catalog.Get(page);
			if (traits.IsFrontCover)
			{
				return true;
			}
			if (traits.IsSingleType && page == pageCount - 1)
			{
				return false;
			}
			return !RightToLeft;
		}

		public PageArrangement Arrange(Size viewport, int position, IReadOnlyList<Size> pageSizes, ImageFitMode fit, double zoom, Vector scroll)
		{
			if (pageSizes == null || pageSizes.Count == 0)
			{
				return PageArrangement.Empty;
			}
			int count = catalog.Count;
			foreach (Size size in pageSizes)
			{
				if (size.Width <= 0 || size.Height <= 0)
				{
					return new PageArrangement(new Rect[pageSizes.Count], 0, 0);
				}
			}
			int[] pages = PagesAt(position, count);
			Size content;
			Rect[] inContent;
			if (pageSizes.Count == 1)
			{
				Size s = pageSizes[0];
				if (HalfSpread(s, count))
				{
					content = new Size(s.Width * 2, s.Height);
					bool right = LoneOnRight(pages.Length > 0 ? pages[0] : 0, count);
					inContent = new[] { new Rect(right ? s.Width : 0, 0, s.Width, s.Height) };
				}
				else
				{
					content = s;
					inContent = new[] { new Rect(0, 0, s.Width, s.Height) };
				}
			}
			else
			{
				Size a = pageSizes[0], b = pageSizes[1];
				double height = Math.Max(a.Height, b.Height);
				double widthA = a.Width * height / a.Height, widthB = b.Width * height / b.Height;
				content = new Size(widthA + widthB, height);
				inContent = RightToLeft
					? new[] { new Rect(widthB, 0, widthA, height), new Rect(0, 0, widthB, height) }
					: new[] { new Rect(0, 0, widthA, height), new Rect(widthA, 0, widthB, height) };
			}
			return ContentPlacement.Place(viewport, content, fit, zoom, scroll, inContent, out Rect _);
		}
	}
}
