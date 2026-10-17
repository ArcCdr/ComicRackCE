using System;
using System.Collections.Generic;
using Avalonia;
using cYo.Projects.ComicRack.Engine.Display;

namespace ComicRack.Mac.Reader
{
	/// <summary>Where the pages of one reading position go in the window, and how far the content can be scrolled.</summary>
	public sealed class PageArrangement
	{
		public static readonly PageArrangement Empty = new PageArrangement(new Rect[0], 0, 0);

		public PageArrangement(Rect[] pages, double scroll, double maxScroll)
		{
			Pages = pages;
			Scroll = scroll;
			MaxScroll = maxScroll;
		}

		/// <summary>One rectangle per page, in device independent pixels, relative to the window's reading area (may extend beyond it when scrolled).</summary>
		public Rect[] Pages { get; }

		/// <summary>The vertical scroll that was applied, clamped to 0..<see cref="MaxScroll"/>.</summary>
		public double Scroll { get; }

		/// <summary>How far the content can be scrolled down: 0 if everything fits.</summary>
		public double MaxScroll { get; }
	}

	/// <summary>
	/// How pages are grouped and placed: the abstraction that lets M2.3b add the two-page spread, right-to-left reading and zoom without
	/// touching the view model or the view. A <i>position</i> is the number of the first page of what is on screen. The layout knows
	/// which pages a position shows and how positions follow each other; the view model only moves between positions, asks the page
	/// service for <see cref="PagesAt"/> and the pages around (<see cref="LayoutExtensions.Window"/>), and draws the rectangles from
	/// <see cref="Arrange"/>.
	/// </summary>
	public interface IPageLayout
	{
		string Name { get; }

		/// <summary>The pages shown at <paramref name="position"/> (empty if the book has no pages).</summary>
		int[] PagesAt(int position, int pageCount);

		/// <summary>The valid position nearest to <paramref name="position"/>.</summary>
		int Normalize(int position, int pageCount);

		int First(int pageCount);

		int Last(int pageCount);

		/// <summary>The position after <paramref name="position"/>, or -1 at the end.</summary>
		int Next(int position, int pageCount);

		/// <summary>The position before <paramref name="position"/>, or -1 at the start.</summary>
		int Previous(int position, int pageCount);

		/// <summary>
		/// Places the pages (<paramref name="pageSizes"/> in image pixels, in the order of <see cref="PagesAt"/>) in a viewport of the given size.
		/// <paramref name="scroll"/> is the vertical offset of the content in the same units as the result.
		/// </summary>
		PageArrangement Arrange(Size viewport, IReadOnlyList<Size> pageSizes, ImageFitMode fit, double scroll);
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
	/// <see cref="ImageFitMode.FitWidth"/> the width of the window (a taller page scrolls), <see cref="ImageFitMode.FitHeight"/> the height
	/// (never wider than the window, so nothing is cropped: M2.3b adds panning). The other values of the Core's enum behave as Fit until
	/// M2.3b implements them. The page is centred on the axis where it is smaller than the window; pixel snapping is the job of
	/// <see cref="ReaderPainter"/>.
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

		public PageArrangement Arrange(Size viewport, IReadOnlyList<Size> pageSizes, ImageFitMode fit, double scroll)
		{
			if (pageSizes == null || pageSizes.Count == 0)
			{
				return PageArrangement.Empty;
			}
			Size page = pageSizes[0];
			if (page.Width <= 0 || page.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0)
			{
				return new PageArrangement(new[] { default(Rect) }, 0, 0);
			}
			double scaleWidth = viewport.Width / page.Width;
			double scaleHeight = viewport.Height / page.Height;
			double scale;
			switch (fit)
			{
				case ImageFitMode.FitWidth:
					scale = scaleWidth;
					break;
				case ImageFitMode.FitHeight:
					scale = Math.Min(scaleHeight, scaleWidth);
					break;
				default:
					scale = Math.Min(scaleWidth, scaleHeight);
					break;
			}
			double width = page.Width * scale, height = page.Height * scale;
			double maxScroll = Math.Max(0, height - viewport.Height);
			scroll = Math.Max(0, Math.Min(scroll, maxScroll));
			double x = (viewport.Width - width) / 2;
			double y = height <= viewport.Height ? (viewport.Height - height) / 2 : -scroll;
			return new PageArrangement(new[] { new Rect(x, y, width, height) }, scroll, maxScroll);
		}
	}
}
