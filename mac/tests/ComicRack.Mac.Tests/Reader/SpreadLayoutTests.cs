using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using ComicRack.Mac.Reader;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Display;
using Xunit;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>The two-page layout: which pages pair up, how positions follow each other both ways, where pages go for each fit mode, right to left, zoom and pan limits.</summary>
	public class SpreadLayoutTests
	{
		private static PageCatalog Catalog(int count, Func<int, PageTraits> traits = null) => new PageCatalog(count, traits);

		private static PageTraits Story(ComicPagePosition position = ComicPagePosition.Default) => new PageTraits(false, false, position, 0, 0);

		private static PageTraits Back() => new PageTraits(false, true, ComicPagePosition.Default, 0, 0);

		private static int[][] Spreads(IPageLayout layout, int count)
		{
			var list = new List<int[]>();
			for (int p = layout.First(count); p >= 0; p = layout.Next(p, count))
			{
				list.Add(layout.PagesAt(p, count));
			}
			return list.ToArray();
		}

		private static string Show(int[][] spreads) => string.Join(" | ", spreads.Select(s => string.Join(",", s)));

		// ---- Pairing

		[Fact]
		public void The_cover_is_alone_and_the_other_pages_pair_up()
		{
			var layout = new SpreadLayout(Catalog(8), adaptive: false);
			Assert.Equal("0 | 1,2 | 3,4 | 5,6 | 7", Show(Spreads(layout, 8)));
			layout = new SpreadLayout(Catalog(7), adaptive: false);
			Assert.Equal("0 | 1,2 | 3,4 | 5,6", Show(Spreads(layout, 7)));
			Assert.Equal(5, layout.Last(7));
		}

		[Fact]
		public void Going_back_visits_the_same_spreads_as_going_forward()
		{
			foreach (int count in new[] { 1, 2, 3, 8, 9 })
			{
				var layout = new SpreadLayout(Catalog(count), adaptive: false);
				var forward = new List<int>();
				for (int p = layout.First(count); p >= 0; p = layout.Next(p, count))
				{
					forward.Add(p);
				}
				var backward = new List<int>();
				for (int p = layout.Last(count); p >= 0; p = layout.Previous(p, count))
				{
					backward.Add(p);
				}
				backward.Reverse();
				Assert.Equal(forward, backward);
			}
		}

		[Fact]
		public void Any_page_is_normalized_to_the_start_of_the_spread_that_holds_it()
		{
			var layout = new SpreadLayout(Catalog(8), adaptive: false);
			Assert.Equal(new[] { 0, 1, 1, 3, 3, 5, 5, 7 }, Enumerable.Range(0, 8).Select(p => layout.Normalize(p, 8)).ToArray());
			Assert.Equal(7, layout.Normalize(99, 8));
			Assert.Equal(0, layout.Normalize(-4, 8));
			Assert.Equal(1, layout.Previous(2, 8)); // inside a spread: back to its start
			Assert.Equal(0, layout.Previous(1, 8));
			Assert.Equal(-1, layout.Previous(0, 8));
			Assert.Equal(-1, layout.Next(7, 8));
		}

		[Fact]
		public void A_wide_page_is_alone_and_moves_the_pairing_of_the_pages_after_it()
		{
			var catalog = Catalog(9);
			var layout = new SpreadLayout(catalog, adaptive: false);
			Assert.Equal("0 | 1,2 | 3,4 | 5,6 | 7,8", Show(Spreads(layout, 9)));
			Assert.True(catalog.Learn(4, 2400, 1200)); // decoded: wider than tall
			Assert.Equal("0 | 1,2 | 3 | 4 | 5,6 | 7,8", Show(Spreads(layout, 9)));
			Assert.True(catalog.Learn(5, 1200, 1800));
			Assert.Equal("0 | 1,2 | 3 | 4 | 5,6 | 7,8", Show(Spreads(layout, 9)));
			Assert.True(catalog.Learn(1, 2400, 1200)); // a wide page early moves everything after it
			Assert.Equal("0 | 1 | 2,3 | 4 | 5,6 | 7,8", Show(Spreads(layout, 9)));
			Assert.Equal(new[] { 0, 1, 2, 4, 5, 7 }, Enumerable.Range(0, 6).Select(i => Spreads(layout, 9)[i][0]).ToArray());
		}

		[Fact]
		public void The_size_the_core_already_knows_counts_and_a_decoded_size_replaces_it()
		{
			var catalog = Catalog(6, p => p == 0 ? PageTraits.Default(0) : p == 3 ? new PageTraits(false, false, ComicPagePosition.Default, 2000, 1000) : Story());
			var layout = new SpreadLayout(catalog, adaptive: false);
			Assert.Equal("0 | 1,2 | 3 | 4,5", Show(Spreads(layout, 6)));
			Assert.True(catalog.Learn(3, 1000, 2000)); // the file changed: it is a portrait page now
			Assert.Equal("0 | 1,2 | 3,4 | 5", Show(Spreads(layout, 6)));
			Assert.False(catalog.Learn(3, 1000, 2000)); // nothing new
			Assert.False(catalog.Learn(3, 0, 0));
			Assert.False(catalog.Learn(99, 10, 10));
		}

		[Fact]
		public void A_size_the_core_learned_behind_the_layouts_back_is_noticed_when_the_reader_decodes_the_page()
		{
			bool wide = false;
			var catalog = Catalog(6, p => p == 0 ? PageTraits.Default(0) : new PageTraits(false, false, ComicPagePosition.Default, p == 3 && wide ? 2000 : 1000, 1500));
			var layout = new SpreadLayout(catalog, adaptive: false);
			Assert.Equal("0 | 1,2 | 3,4 | 5", Show(Spreads(layout, 6)));
			wide = true; // the Core read page 3 and recorded its size; nothing told the catalog
			Assert.Equal("0 | 1,2 | 3,4 | 5", Show(Spreads(layout, 6))); // the pairing in use is still the old one
			Assert.True(catalog.Learn(3, 2000, 1500)); // the reader decodes the page and tells the catalog
			Assert.Equal("0 | 1,2 | 3 | 4,5", Show(Spreads(layout, 6)));
		}

		[Fact]
		public void A_size_that_does_not_change_wide_or_narrow_does_not_invalidate_the_pairing()
		{
			var catalog = Catalog(6);
			long start = catalog.Version;
			catalog.Learn(2, 1000, 1500);
			catalog.Learn(2, 900, 1400);
			Assert.Equal(start, catalog.Version);
			catalog.Learn(2, 1500, 1000);
			Assert.NotEqual(start, catalog.Version);
		}

		[Fact]
		public void Front_and_back_cover_types_are_alone_and_near_pages_start_a_spread()
		{
			// a back cover in the middle splits the pairing around itself
			var layout = new SpreadLayout(Catalog(8, p => p == 0 ? PageTraits.Default(0) : p == 6 ? Back() : Story()), adaptive: false);
			Assert.Equal("0 | 1,2 | 3,4 | 5 | 6 | 7", Show(Spreads(layout, 8)));
			// the page after the cover is typed Story here, so a book whose first page is not typed as a cover pairs from its first page
			layout = new SpreadLayout(Catalog(5, p => Story()), adaptive: false);
			Assert.Equal("0,1 | 2,3 | 4", Show(Spreads(layout, 5)));
			// Near on the second page of a would-be pair: it starts the next spread instead
			layout = new SpreadLayout(Catalog(7, p => p == 0 ? PageTraits.Default(0) : Story(p == 2 ? ComicPagePosition.Near : ComicPagePosition.Default)), adaptive: false);
			Assert.Equal("0 | 1 | 2,3 | 4,5 | 6", Show(Spreads(layout, 7)));
		}

		[Fact]
		public void A_book_of_one_page_or_none_has_one_position_or_none()
		{
			var layout = new SpreadLayout(Catalog(1), adaptive: false);
			Assert.Equal(new[] { 0 }, layout.PagesAt(0, 1));
			Assert.Equal(-1, layout.Next(0, 1));
			Assert.Equal(-1, layout.Previous(0, 1));
			Assert.Equal(0, layout.Last(1));
			layout = new SpreadLayout(Catalog(0), adaptive: false);
			Assert.Empty(layout.PagesAt(0, 0));
			Assert.Equal(-1, layout.Next(0, 0));
			Assert.Equal(-1, layout.Previous(0, 0));
			Assert.Equal(0, layout.Normalize(5, 0));
			Assert.Empty(layout.Window(0, 0));
		}

		[Fact]
		public void The_window_for_a_spread_holds_its_pages_the_next_two_spreads_and_the_previous_one()
		{
			var layout = new SpreadLayout(Catalog(12), adaptive: false);
			Assert.Equal(new[] { 5, 6, 7, 8, 9, 10, 3, 4 }, layout.Window(5, 12));
			Assert.Equal(new[] { 0, 1, 2, 3, 4 }, layout.Window(0, 12));
			Assert.Equal(new[] { 9, 10, 11, 7, 8 }, layout.Window(9, 12));
			Assert.Equal(new[] { 1, 2, 3, 4, 0 }, layout.Window(1, 12, ahead: 1));
			Assert.Equal(new[] { 3, 4, 5, 6 }, layout.Window(4, 12, ahead: 1, behind: 0)); // any page of a spread: its position
		}

		[Fact]
		public void A_changed_page_count_rebuilds_the_pairing()
		{
			var layout = new SpreadLayout(Catalog(8), adaptive: false);
			Assert.Equal(7, layout.Last(8));
			Assert.Equal(5, layout.Last(7));
			Assert.Equal(7, layout.Last(8));
		}

		// ---- Placement

		private static readonly Size Portrait = new Size(1200, 1800);

		private static void AssertRect(Rect expected, Rect actual)
		{
			Assert.Equal(expected.X, actual.X, 6);
			Assert.Equal(expected.Y, actual.Y, 6);
			Assert.Equal(expected.Width, actual.Width, 6);
			Assert.Equal(expected.Height, actual.Height, 6);
		}

		[Fact]
		public void A_spread_is_fitted_as_one_image_with_the_pages_side_by_side_at_the_same_height()
		{
			var layout = new SpreadLayout(Catalog(8), adaptive: false);
			PageArrangement a = layout.Arrange(new Size(1000, 800), 1, new[] { Portrait, Portrait }, ImageFitMode.Fit, 1, default(Vector));
			double scale = 1000.0 / 2400; // the width limits it
			Assert.Equal(scale, a.Scale, 9);
			AssertRect(new Rect(0, (800 - 1800 * scale) / 2, 1200 * scale, 1800 * scale), a.Pages[0]);
			AssertRect(new Rect(1200 * scale, (800 - 1800 * scale) / 2, 1200 * scale, 1800 * scale), a.Pages[1]);
			Assert.Equal(0, a.MaxScroll);
			Assert.Equal(0, a.MaxScrollX);
			Assert.Equal(1000, a.Content.Width, 6);
		}

		[Fact]
		public void Pages_of_different_heights_are_scaled_to_the_taller_one()
		{
			var layout = new SpreadLayout(Catalog(8), adaptive: false);
			PageArrangement a = layout.Arrange(new Size(1400, 1400), 1, new[] { new Size(1000, 1500), new Size(1200, 2000) }, ImageFitMode.Fit, 1, default(Vector));
			Assert.Equal(a.Pages[0].Height, a.Pages[1].Height, 6);
			Assert.Equal(1000.0 / 1500, a.Pages[0].Width / a.Pages[0].Height, 6);
			Assert.Equal(1200.0 / 2000, a.Pages[1].Width / a.Pages[1].Height, 6);
			Assert.Equal(a.Pages[0].Right, a.Pages[1].Left, 6);
		}

		[Fact]
		public void Reading_right_to_left_mirrors_a_spread()
		{
			var layout = new SpreadLayout(Catalog(8), adaptive: false, rightToLeft: true);
			PageArrangement a = layout.Arrange(new Size(1000, 800), 1, new[] { Portrait, Portrait }, ImageFitMode.Fit, 1, default(Vector));
			Assert.True(a.Pages[0].X > a.Pages[1].X, "the first page is on the right");
			Assert.Equal(a.Pages[1].Right, a.Pages[0].Left, 6);
			layout.RightToLeft = false;
			a = layout.Arrange(new Size(1000, 800), 1, new[] { Portrait, Portrait }, ImageFitMode.Fit, 1, default(Vector));
			Assert.True(a.Pages[0].X < a.Pages[1].X);
		}

		[Fact]
		public void Fit_width_and_fit_height_apply_to_the_whole_spread()
		{
			var layout = new SpreadLayout(Catalog(8), adaptive: false);
			var sizes = new[] { Portrait, Portrait };
			// fit width: the spread is as wide as the window and a tall spread scrolls
			PageArrangement w = layout.Arrange(new Size(1000, 600), 1, sizes, ImageFitMode.FitWidth, 1, new Vector(0, 100));
			Assert.Equal(1000, w.Content.Width, 6);
			Assert.Equal(1000, w.Pages[0].Width + w.Pages[1].Width, 6);
			Assert.Equal(750 - 600, w.MaxScroll, 6);
			Assert.Equal(100, w.Scroll, 6);
			Assert.Equal(-100, w.Pages[0].Y, 6);
			Assert.Equal(0, w.MaxScrollX);
			// fit height: the spread is as high as the window and overflows sideways, which pans
			PageArrangement h = layout.Arrange(new Size(500, 600), 1, sizes, ImageFitMode.FitHeight, 1, new Vector(200, 0));
			Assert.Equal(600, h.Content.Height, 6);
			Assert.Equal(800, h.Content.Width, 6); // 2400 x 1800 at 1/3
			Assert.Equal(300, h.MaxScrollX, 6);
			Assert.Equal(200, h.ScrollX, 6);
			Assert.Equal(-200, h.Pages[0].X, 6);
			Assert.Equal(0, h.MaxScroll);
			Assert.Equal(0, h.Pages[0].Y, 6);
			// best fit: both fit, the whole spread is visible and centred
			PageArrangement b = layout.Arrange(new Size(1000, 600), 1, sizes, ImageFitMode.Fit, 1, default(Vector));
			Assert.Equal(600, b.Content.Height, 6);
			Assert.Equal(800, b.Content.Width, 6);
			Assert.Equal(100, b.Content.X, 6);
		}

		[Fact]
		public void A_lone_cover_takes_the_right_half_of_a_spread_a_closing_back_cover_the_left_and_the_cover_stays_right_when_reading_right_to_left()
		{
			var catalog = Catalog(8, p => p == 0 ? PageTraits.Default(0) : p == 7 ? Back() : Story());
			var layout = new SpreadLayout(catalog, adaptive: false);
			var one = new[] { Portrait };
			PageArrangement cover = layout.Arrange(new Size(1000, 800), 0, one, ImageFitMode.Fit, 1, default(Vector));
			Assert.Equal(1000.0 / 2400, cover.Scale, 9); // as wide as the two pages together
			Assert.Equal(cover.Content.X + cover.Content.Width / 2, cover.Pages[0].X, 6); // the right half
			Assert.Equal(cover.Content.Right, cover.Pages[0].Right, 6);
			PageArrangement back = layout.Arrange(new Size(1000, 800), 7, one, ImageFitMode.Fit, 1, default(Vector));
			Assert.Equal(back.Content.X, back.Pages[0].X, 6); // the left half
			layout.RightToLeft = true;
			cover = layout.Arrange(new Size(1000, 800), 0, one, ImageFitMode.Fit, 1, default(Vector));
			Assert.Equal(cover.Content.Right, cover.Pages[0].Right, 6);
			back = layout.Arrange(new Size(1000, 800), 7, one, ImageFitMode.Fit, 1, default(Vector));
			Assert.Equal(back.Content.X, back.Pages[0].X, 6);
		}

		[Fact]
		public void Any_other_lone_page_is_on_the_right_or_on_the_left_when_reading_right_to_left()
		{
			var catalog = Catalog(8);
			catalog.Learn(4, 2400, 1200);
			var layout = new SpreadLayout(catalog, adaptive: false);
			Assert.Equal(new[] { 3 }, layout.PagesAt(3, 8)); // alone, before the double page
			PageArrangement ltr = layout.Arrange(new Size(1000, 800), 3, new[] { Portrait }, ImageFitMode.Fit, 1, default(Vector));
			Assert.Equal(ltr.Content.Right, ltr.Pages[0].Right, 6);
			layout.RightToLeft = true;
			PageArrangement rtl = layout.Arrange(new Size(1000, 800), 3, new[] { Portrait }, ImageFitMode.Fit, 1, default(Vector));
			Assert.Equal(rtl.Content.X, rtl.Pages[0].X, 6);
		}

		[Fact]
		public void A_wide_page_fills_the_spread_and_the_adaptive_layout_shows_lone_portrait_pages_as_single_pages()
		{
			var catalog = Catalog(8);
			var layout = new SpreadLayout(catalog, adaptive: false);
			var wide = new Size(2400, 1200);
			PageArrangement w = layout.Arrange(new Size(1000, 800), 4, new[] { wide }, ImageFitMode.Fit, 1, default(Vector));
			Assert.Equal(1000, w.Pages[0].Width, 6);
			Assert.Equal(w.Content, w.Pages[0]);

			var adaptive = new SpreadLayout(catalog, adaptive: true);
			PageArrangement c = adaptive.Arrange(new Size(1000, 800), 0, new[] { Portrait }, ImageFitMode.Fit, 1, default(Vector));
			Assert.Equal(800.0 / 1800, c.Scale, 9); // one page, centred
			Assert.Equal((1000 - c.Pages[0].Width) / 2, c.Pages[0].X, 6);
			Assert.Equal(c.Content, c.Pages[0]);
			// a book of one page is never shown as half a spread
			var single = new SpreadLayout(Catalog(1), adaptive: false);
			PageArrangement one = single.Arrange(new Size(1000, 800), 0, new[] { Portrait }, ImageFitMode.Fit, 1, default(Vector));
			Assert.Equal(one.Content, one.Pages[0]);
		}

		[Fact]
		public void Degenerate_input_gives_empty_rectangles_and_does_not_throw()
		{
			var layout = new SpreadLayout(Catalog(8), adaptive: false);
			Assert.Empty(layout.Arrange(new Size(100, 100), 1, new Size[0], ImageFitMode.Fit, 1, default(Vector)).Pages);
			Assert.Equal(default(Rect), layout.Arrange(new Size(100, 100), 1, new[] { new Size(0, 10), Portrait }, ImageFitMode.Fit, 1, default(Vector)).Pages[0]);
			Assert.Equal(default(Rect), layout.Arrange(new Size(0, 0), 1, new[] { Portrait, Portrait }, ImageFitMode.Fit, 1, default(Vector)).Pages[1]);
		}

		// ---- Zoom and pan

		[Fact]
		public void Zoom_enlarges_the_fit_and_the_scroll_is_clamped_to_the_content()
		{
			var layout = new SpreadLayout(Catalog(8), adaptive: false);
			var sizes = new[] { Portrait, Portrait };
			PageArrangement fit = layout.Arrange(new Size(1000, 800), 1, sizes, ImageFitMode.Fit, 1, default(Vector));
			PageArrangement zoomed = layout.Arrange(new Size(1000, 800), 1, sizes, ImageFitMode.Fit, 2, default(Vector));
			Assert.Equal(fit.Scale * 2, zoomed.Scale, 9);
			Assert.Equal(2000, zoomed.Content.Width, 6);
			Assert.Equal(1500, zoomed.Content.Height, 6);
			Assert.Equal(1000, zoomed.MaxScrollX, 6);
			Assert.Equal(700, zoomed.MaxScroll, 6);
			PageArrangement far = layout.Arrange(new Size(1000, 800), 1, sizes, ImageFitMode.Fit, 2, new Vector(99999, 99999));
			Assert.Equal(1000, far.ScrollX, 6);
			Assert.Equal(700, far.Scroll, 6);
			Assert.Equal(1000, far.Content.Right, 6); // the right edge of the content is at the right edge of the window
			Assert.Equal(800, far.Content.Bottom, 6);
			PageArrangement before = layout.Arrange(new Size(1000, 800), 1, sizes, ImageFitMode.Fit, 2, new Vector(-50, -20));
			Assert.Equal(0, before.ScrollX);
			Assert.Equal(0, before.Scroll);
			Assert.Equal(0, before.Content.X, 6);
			Assert.Equal(0, before.Content.Y, 6);
		}

		[Fact]
		public void A_small_zoom_that_still_fits_keeps_the_content_centred_with_nothing_to_pan()
		{
			var layout = new SpreadLayout(Catalog(8), adaptive: false);
			PageArrangement a = layout.Arrange(new Size(1000, 1000), 1, new[] { Portrait, Portrait }, ImageFitMode.Fit, 1, new Vector(40, 40));
			Assert.Equal(0, a.ScrollX);
			Assert.Equal(0, a.MaxScrollX);
			Assert.Equal((1000 - a.Content.Height) / 2, a.Content.Y, 6);
		}

		[Theory]
		[InlineData(0)]
		[InlineData(-2)]
		[InlineData(double.NaN)]
		[InlineData(double.PositiveInfinity)]
		public void A_zoom_that_makes_no_sense_counts_as_one(double zoom)
		{
			var layout = SinglePageLayout.Instance;
			PageArrangement a = layout.Arrange(new Size(1000, 800), 0, new[] { Portrait }, ImageFitMode.Fit, zoom, default(Vector));
			PageArrangement one = layout.Arrange(new Size(1000, 800), 0, new[] { Portrait }, ImageFitMode.Fit, 1, default(Vector));
			Assert.Equal(one.Pages[0], a.Pages[0]);
		}

		[Fact]
		public void The_single_page_zooms_and_pans_like_a_spread()
		{
			PageArrangement a = SinglePageLayout.Instance.Arrange(new Size(1000, 800), 0, new[] { Portrait }, ImageFitMode.FitWidth, 2, new Vector(100, 5000));
			Assert.Equal(2000, a.Content.Width, 6);
			Assert.Equal(3000, a.Content.Height, 6);
			Assert.Equal(1000, a.MaxScrollX, 6);
			Assert.Equal(100, a.ScrollX, 6);
			Assert.Equal(2200, a.MaxScroll, 6);
			Assert.Equal(2200, a.Scroll, 6);
		}
	}
}
