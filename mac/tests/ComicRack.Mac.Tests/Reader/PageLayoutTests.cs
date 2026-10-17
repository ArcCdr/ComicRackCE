using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using ComicRack.Mac.Reader;
using cYo.Projects.ComicRack.Engine.Display;
using Xunit;

namespace ComicRack.Mac.Tests.Reader
{
	public static class SingleLayoutTestExtensions
	{
		/// <summary>The arrangement of a position-independent layout without zoom and without horizontal scroll (the M2.3a form).</summary>
		public static PageArrangement Arrange(this IPageLayout layout, Size viewport, IReadOnlyList<Size> sizes, ImageFitMode fit, double scrollY)
		{
			return layout.Arrange(viewport, 0, sizes, fit, 1.0, new Vector(0, scrollY));
		}
	}

	public class PageLayoutTests
	{
		private static readonly IPageLayout Layout = SinglePageLayout.Instance;

		[Fact]
		public void Positions_follow_each_other_and_stop_at_both_ends()
		{
			Assert.Equal(new[] { 0 }, Layout.PagesAt(0, 5));
			Assert.Equal(1, Layout.Next(0, 5));
			Assert.Equal(4, Layout.Next(3, 5));
			Assert.Equal(-1, Layout.Next(4, 5));
			Assert.Equal(-1, Layout.Previous(0, 5));
			Assert.Equal(3, Layout.Previous(4, 5));
			Assert.Equal(0, Layout.First(5));
			Assert.Equal(4, Layout.Last(5));
		}

		[Fact]
		public void A_book_of_one_page_and_a_book_without_pages_have_no_neighbours()
		{
			Assert.Equal(-1, Layout.Next(0, 1));
			Assert.Equal(-1, Layout.Previous(0, 1));
			Assert.Equal(new[] { 0 }, Layout.PagesAt(0, 1));
			Assert.Empty(Layout.PagesAt(0, 0));
			Assert.Equal(-1, Layout.Next(0, 0));
			Assert.Equal(-1, Layout.Previous(0, 0));
			Assert.Equal(0, Layout.Last(0));
			Assert.Empty(Layout.Window(0, 0));
		}

		[Theory]
		[InlineData(-3, 0)]
		[InlineData(0, 0)]
		[InlineData(7, 7)]
		[InlineData(9, 9)]
		[InlineData(40, 9)]
		public void Normalize_clamps_to_the_book(int position, int expected)
		{
			Assert.Equal(expected, Layout.Normalize(position, 10));
		}

		[Fact]
		public void The_window_is_the_page_then_the_next_two_then_the_previous_one()
		{
			Assert.Equal(new[] { 5, 6, 7, 4 }, Layout.Window(5, 10));
			Assert.Equal(new[] { 0, 1, 2 }, Layout.Window(0, 10)); // nothing before the first page
			Assert.Equal(new[] { 9, 8 }, Layout.Window(9, 10)); // nothing after the last
			Assert.Equal(new[] { 8, 9, 7 }, Layout.Window(8, 10)); // only one page after
			Assert.Equal(new[] { 0 }, Layout.Window(0, 1));
			Assert.Equal(new[] { 3, 4, 2, 1 }, Layout.Window(3, 5, ahead: 1, behind: 2));
		}

		[Fact]
		public void Best_fit_shows_the_whole_page_centred_and_keeps_its_proportions()
		{
			// a 1200 x 1800 page in a 1000 x 800 window: limited by the height
			PageArrangement a = Layout.Arrange(new Size(1000, 800), new[] { new Size(1200, 1800) }, ImageFitMode.Fit, 0);
			Rect r = a.Pages[0];
			Assert.Equal(800, r.Height, 6);
			Assert.Equal(1200.0 / 1800 * 800, r.Width, 6);
			Assert.Equal((1000 - r.Width) / 2, r.X, 6);
			Assert.Equal(0, r.Y, 6);
			Assert.Equal(0, a.MaxScroll);
			// a wide page in a narrow window: limited by the width, centred vertically
			a = Layout.Arrange(new Size(600, 900), new[] { new Size(1800, 1200) }, ImageFitMode.Fit, 0);
			r = a.Pages[0];
			Assert.Equal(600, r.Width, 6);
			Assert.Equal(400, r.Height, 6);
			Assert.Equal(250, r.Y, 6);
			Assert.Equal(0, r.X, 6);
		}

		[Fact]
		public void Fit_width_fills_the_width_and_a_taller_page_scrolls()
		{
			PageArrangement a = Layout.Arrange(new Size(1000, 800), new[] { new Size(1200, 1800) }, ImageFitMode.FitWidth, 0);
			Rect r = a.Pages[0];
			Assert.Equal(1000, r.Width, 6);
			Assert.Equal(1500, r.Height, 6);
			Assert.Equal(0, r.X, 6);
			Assert.Equal(0, r.Y, 6);
			Assert.Equal(700, a.MaxScroll, 6);

			a = Layout.Arrange(new Size(1000, 800), new[] { new Size(1200, 1800) }, ImageFitMode.FitWidth, 300);
			Assert.Equal(300, a.Scroll, 6);
			Assert.Equal(-300, a.Pages[0].Y, 6);

			a = Layout.Arrange(new Size(1000, 800), new[] { new Size(1200, 1800) }, ImageFitMode.FitWidth, 5000); // clamped to the end of the page
			Assert.Equal(700, a.Scroll, 6);
			Assert.Equal(-700, a.Pages[0].Y, 6);
			Assert.Equal(800, a.Pages[0].Bottom, 6);

			a = Layout.Arrange(new Size(1000, 800), new[] { new Size(1200, 1800) }, ImageFitMode.FitWidth, -50);
			Assert.Equal(0, a.Scroll);
		}

		[Fact]
		public void Fit_width_of_a_page_that_is_shorter_than_the_window_is_centred_vertically_and_does_not_scroll()
		{
			PageArrangement a = Layout.Arrange(new Size(1000, 800), new[] { new Size(1800, 1200) }, ImageFitMode.FitWidth, 100);
			Assert.Equal(0, a.MaxScroll);
			Assert.Equal(0, a.Scroll);
			Assert.Equal(1000, a.Pages[0].Width, 6);
			Assert.Equal(666.6666, a.Pages[0].Height, 3);
			Assert.Equal((800 - a.Pages[0].Height) / 2, a.Pages[0].Y, 6);
		}

		[Fact]
		public void Fit_height_fills_the_height_and_a_wider_page_is_panned_sideways()
		{
			PageArrangement a = Layout.Arrange(new Size(1000, 800), new[] { new Size(1200, 1800) }, ImageFitMode.FitHeight, 0);
			Assert.Equal(800, a.Pages[0].Height, 6);
			Assert.True(a.Pages[0].Width < 1000);
			Assert.Equal(0, a.MaxScrollX);
			// a very wide page overflows the window: it keeps the full height and scrolls sideways
			a = Layout.Arrange(new Size(1000, 800), 0, new[] { new Size(3000, 1000) }, ImageFitMode.FitHeight, 1.0, new Vector(250, 0));
			Assert.Equal(800, a.Pages[0].Height, 6);
			Assert.Equal(2400, a.Pages[0].Width, 6);
			Assert.Equal(1400, a.MaxScrollX, 6);
			Assert.Equal(250, a.ScrollX, 6);
			Assert.Equal(-250, a.Pages[0].X, 6);
			Assert.Equal(0, a.MaxScroll);
		}

		[Fact]
		public void The_other_fit_modes_of_the_core_behave_as_best_fit_for_now()
		{
			PageArrangement best = Layout.Arrange(new Size(1000, 800), new[] { new Size(1200, 1800) }, ImageFitMode.Fit, 0);
			foreach (ImageFitMode mode in new[] { ImageFitMode.Original, ImageFitMode.BestFit, ImageFitMode.FitWidthAdaptive })
			{
				Assert.Equal(best.Pages[0], Layout.Arrange(new Size(1000, 800), new[] { new Size(1200, 1800) }, mode, 0).Pages[0]);
			}
		}

		[Fact]
		public void Empty_or_degenerate_input_gives_an_empty_arrangement_without_throwing()
		{
			Assert.Empty(Layout.Arrange(new Size(100, 100), new Size[0], ImageFitMode.Fit, 0).Pages);
			Assert.Equal(default(Rect), Layout.Arrange(new Size(100, 100), new[] { new Size(0, 10) }, ImageFitMode.Fit, 0).Pages[0]);
			Assert.Equal(default(Rect), Layout.Arrange(new Size(0, 0), new[] { new Size(10, 10) }, ImageFitMode.Fit, 0).Pages[0]);
		}

		[Fact]
		public void Enlarging_a_small_page_is_allowed()
		{
			PageArrangement a = Layout.Arrange(new Size(1000, 1000), new[] { new Size(100, 200) }, ImageFitMode.Fit, 0);
			Assert.Equal(1000, a.Pages[0].Height, 6);
			Assert.Equal(500, a.Pages[0].Width, 6);
		}
	}
}
