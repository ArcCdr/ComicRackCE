using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using ComicRack.Mac.Reader;
using ComicRack.Mac.ViewModels;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Display;
using Xunit;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>The reader view model in two-page mode and with zoom: paging across spreads, resume, wide pages found while loading, prefetch, right to left, zoom limits and pan clamping, full screen.</summary>
	public class ReaderSpreadTests
	{
		private static ReaderOptions Options(PageLayoutMode mode = PageLayoutMode.Double, bool rightToLeft = false)
		{
			return new ReaderOptions
			{
				Mode = mode,
				RightToLeft = rightToLeft,
				LoadingDelay = TimeSpan.FromMilliseconds(20),
				Log = m => { },
				Swipe = new SwipeRecognizer(),
			};
		}

		private static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 10000)
		{
			var watch = Stopwatch.StartNew();
			while (!condition())
			{
				if (watch.ElapsedMilliseconds > timeoutMs)
				{
					throw new TimeoutException("Not reached: " + what);
				}
				await Task.Delay(3);
			}
		}

		private static (int, int) Portrait(int page) => (1200, 1800);

		private static async Task<(ReaderViewModel vm, FakeSession session)> Open(int pages = 8, int resume = 0, ReaderOptions options = null, Func<int, (int, int)> size = null, bool? rightToLeft = null, Func<int, PageTraits> traits = null)
		{
			var decoder = new FakePageDecoder { PageCount = pages, Size = size ?? Portrait };
			var session = new FakeSession(decoder) { ResumePage = resume, RightToLeft = rightToLeft, Traits = traits };
			var vm = new ReaderViewModel(session, options ?? Options());
			vm.ViewportSize = new Size(1000, 800);
			await vm.OpenAsync();
			await vm.WhenSettled;
			if (vm.IsOpen)
			{
				int expected = vm.Layout.Window(vm.CurrentPage, pages).Count;
				await WaitFor(() => vm.Pages.GetStats().CachedCount >= expected, "first window cached");
			}
			return (vm, session);
		}

		private sealed class Held : IDisposable
		{
			public Held(ComicRack.Mac.Browser.CoverImage image) { Image = image; }

			public ComicRack.Mac.Browser.CoverImage Image { get; }

			public void Dispose() { Image?.Release(); }
		}

		private static int[] Shown(ReaderViewModel vm) => vm.Scene.Pages.Select(p => p.Index).ToArray();

		// ---- Paging

		[Fact]
		public async Task Paging_walks_the_spreads_forward_and_back_and_records_the_first_page_and_the_furthest()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(8);
				try
				{
					Assert.Equal(new[] { 0 }, Shown(vm));
					Assert.Equal("1 / 8", vm.PageText);
					Assert.Empty(session.Furthest);
					var seen = new List<string>();
					for (int i = 0; i < 4; i++)
					{
						vm.NextPage();
						await vm.WhenSettled;
						seen.Add(string.Join(",", Shown(vm)) + "@" + vm.CurrentPage + " " + vm.PageText);
					}
					Assert.Equal(new[] { "1,2@1 2-3 / 8", "3,4@3 4-5 / 8", "5,6@5 6-7 / 8", "7@7 8 / 8" }, seen);
					Assert.Equal(new[] { 0, 1, 3, 5, 7 }, session.Reported.ToArray());
					Assert.Equal(new[] { 2, 4, 6 }, session.Furthest.ToArray());
					vm.NextPage(); // at the end: stays
					await vm.WhenSettled;
					Assert.Equal(7, vm.CurrentPage);
					Assert.Equal(1, session.EndReached);
					seen.Clear();
					for (int i = 0; i < 4; i++)
					{
						vm.PreviousPage();
						await vm.WhenSettled;
						seen.Add(string.Join(",", Shown(vm)) + "@" + vm.CurrentPage);
					}
					Assert.Equal(new[] { "5,6@5", "3,4@3", "1,2@1", "0@0" }, seen);
					vm.PreviousPage();
					Assert.Equal(0, vm.CurrentPage);
					vm.LastPage();
					await vm.WhenSettled;
					Assert.Equal(7, vm.CurrentPage);
					vm.FirstPage();
					await vm.WhenSettled;
					Assert.Equal(0, vm.CurrentPage);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Theory]
		[InlineData(0, 0, "1 / 9")]
		[InlineData(1, 1, "2-3 / 9")]
		[InlineData(2, 1, "2-3 / 9")]
		[InlineData(4, 3, "4-5 / 9")]
		[InlineData(8, 7, "8-9 / 9")]
		public async Task Resuming_in_the_middle_of_a_spread_starts_at_its_first_page(int resume, int expectedPosition, string expectedText)
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(9, resume);
				try
				{
					Assert.Equal(expectedPosition, vm.CurrentPage);
					Assert.Equal(expectedText, vm.PageText);
					Assert.Equal(expectedPosition, session.LastReported);
					vm.Dispose();
					Assert.Equal(expectedPosition, session.LastReported); // and that is what the book remembers when the reader closes
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task A_wide_page_found_while_loading_is_shown_alone_and_the_pairing_after_it_follows_from_it()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(9, resume: 3, size: p => p == 4 ? (2400, 1200) : (1200, 1800));
				try
				{
					// page 3 was asked for together with page 4; the decoded page 4 turned out to be wide
					Assert.Equal(new[] { 3 }, Shown(vm));
					Assert.Equal("4 / 9", vm.PageText);
					Assert.Equal(3, vm.CurrentPage);
					vm.NextPage();
					await vm.WhenSettled;
					Assert.Equal(new[] { 4 }, Shown(vm));
					Assert.Equal("5 / 9", vm.PageText);
					vm.NextPage();
					await vm.WhenSettled;
					Assert.Equal(new[] { 5, 6 }, Shown(vm));
					vm.PreviousPage();
					await vm.WhenSettled;
					Assert.Equal(new[] { 4 }, Shown(vm));
					vm.PreviousPage();
					await vm.WhenSettled;
					Assert.Equal(new[] { 3 }, Shown(vm));
					Assert.Equal(1, session.FakeDecoder.Order.Count(p => p == 4)); // decoded once, not again for the correction
					Assert.DoesNotContain(1, session.Furthest.Where(p => p == 4)); // 4 was never the second page of a spread
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task A_wide_first_page_leaves_the_decoded_second_page_for_the_next_spread()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(9, resume: 3, size: p => p == 3 ? (2400, 1200) : (1200, 1800));
				try
				{
					Assert.Equal(new[] { 3 }, Shown(vm));
					vm.NextPage();
					await vm.WhenSettled;
					Assert.Equal(new[] { 4, 5 }, Shown(vm));
					using (var cached = new Held(vm.Pages.TryGetCached(4)))
					{
						Assert.NotNull(cached.Image);
					}
					Assert.Equal(1, session.FakeDecoder.Order.Count(p => p == 4));
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task Pages_that_cannot_be_loaded_give_the_notice_of_the_first_page_of_the_spread()
		{
			await Ui.Run(async () =>
			{
				var decoder = new FakePageDecoder { PageCount = 6, Size = Portrait, FailWith = p => p == 2 ? new System.IO.InvalidDataException("Damaged") : null };
				var session = new FakeSession(decoder);
				var vm = new ReaderViewModel(session, Options());
				try
				{
					await vm.OpenAsync();
					vm.NextPage();
					await vm.WhenSettled;
					Assert.Null(vm.Scene);
					Assert.Equal("Page 2 could not be loaded: Damaged", vm.Notice);
					vm.NextPage(); // the broken page is part of this spread only
					await vm.WhenSettled;
					Assert.Equal(new[] { 3, 4 }, Shown(vm));
					Assert.False(vm.HasNotice);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task Prefetch_covers_the_pages_of_the_next_two_spreads_and_the_previous_one()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(20, resume: 3);
				try
				{
					Assert.Equal(new[] { 3, 4 }, Shown(vm));
					await WaitFor(() => vm.Pages.GetStats().CachedCount >= 8, "window cached");
					Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, session.FakeDecoder.Order.OrderBy(p => p).ToArray());
					vm.NextPage(); // the next spread is already cached: shown without waiting
					Assert.Equal(new[] { 5, 6 }, Shown(vm));
					Assert.True(vm.WhenSettled.IsCompleted);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task Switching_layout_keeps_the_reader_on_the_spread_that_holds_its_page()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(10, resume: 4, options: Options(PageLayoutMode.Single));
				try
				{
					Assert.Equal(PageLayoutMode.Single, vm.LayoutMode);
					Assert.Equal(new[] { 4 }, Shown(vm));
					int changes = 0;
					vm.LayoutChanged += (s, e) => changes++;
					vm.SetLayoutMode(PageLayoutMode.Double);
					await vm.WhenSettled;
					Assert.Equal(new[] { 3, 4 }, Shown(vm));
					Assert.Equal(3, vm.CurrentPage);
					Assert.Equal("4-5 / 10", vm.PageText);
					Assert.Equal("Two pages", vm.ModeText);
					vm.SetLayoutMode(PageLayoutMode.Double);
					Assert.Equal(1, changes);
					vm.SetLayoutMode(PageLayoutMode.Single);
					await vm.WhenSettled;
					Assert.Equal(new[] { 3 }, Shown(vm));
					vm.SetLayoutMode(PageLayoutMode.Continuous); // not supported: one page
					Assert.Equal(PageLayoutMode.Single, vm.LayoutMode);
					vm.SetLayoutMode(PageLayoutMode.DoubleAdaptive);
					await vm.WhenSettled;
					Assert.Equal(new[] { 3, 4 }, Shown(vm));
					Assert.Equal("Two pages (adaptive)", vm.ModeText);
					Assert.Equal(3, changes);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task The_cover_of_a_two_page_reader_is_alone_and_the_adaptive_layout_places_it_as_a_single_page()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(6, options: Options(PageLayoutMode.Double));
				try
				{
					PageArrangement half = vm.Arrangement;
					Assert.Equal(vm.ViewportSize.Width, half.Content.Width, 3);
					Assert.True(half.Pages[0].X > vm.ViewportSize.Width / 2 - 1, "the cover is on the right half");
					vm.SetLayoutMode(PageLayoutMode.DoubleAdaptive);
					await vm.WhenSettled;
					PageArrangement adaptive = vm.Arrangement;
					Assert.Equal(adaptive.Content, adaptive.Pages[0]);
					Assert.Equal((vm.ViewportSize.Width - adaptive.Pages[0].Width) / 2, adaptive.Pages[0].X, 3);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task Fit_modes_apply_to_the_whole_spread()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(8, resume: 1);
				try
				{
					vm.ViewportSize = new Size(1000, 600);
					PageArrangement best = vm.Arrangement;
					Assert.Equal(600, best.Content.Height, 3);
					Assert.Equal(800, best.Content.Width, 3);
					vm.HandleKey(Key.D3, KeyModifiers.Meta);
					PageArrangement width = vm.Arrangement;
					Assert.Equal(1000, width.Content.Width, 3);
					Assert.Equal(750, width.Content.Height, 3);
					Assert.Equal(150, width.MaxScroll, 3);
					Assert.True(vm.CanScrollDown);
					vm.HandleKey(Key.D5, KeyModifiers.Meta);
					vm.ViewportSize = new Size(500, 600);
					PageArrangement height = vm.Arrangement;
					Assert.Equal(600, height.Content.Height, 3);
					Assert.Equal(800, height.Content.Width, 3);
					Assert.Equal(300, height.MaxScrollX, 3);
					Assert.True(vm.CanPan);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		// ---- Right to left

		[Fact]
		public async Task A_book_that_says_it_reads_right_to_left_turns_with_the_left_arrow_and_mirrors_its_spreads()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(8, rightToLeft: true);
				try
				{
					Assert.True(vm.RightToLeft);
					Assert.Equal("Two pages · Right to left", vm.ModeText);
					vm.HandleKey(Key.Left, KeyModifiers.None);
					await vm.WhenSettled;
					Assert.Equal(new[] { 1, 2 }, Shown(vm));
					PageArrangement a = vm.Arrangement;
					Assert.True(a.Pages[0].X > a.Pages[1].X, "the first page of the spread is on the right");
					vm.HandleKey(Key.Left, KeyModifiers.None);
					await vm.WhenSettled;
					Assert.Equal(new[] { 3, 4 }, Shown(vm));
					vm.HandleKey(Key.Right, KeyModifiers.None);
					await vm.WhenSettled;
					Assert.Equal(new[] { 1, 2 }, Shown(vm));
					vm.HandleKey(Key.PageDown, KeyModifiers.None); // these do not depend on the direction
					await vm.WhenSettled;
					Assert.Equal(new[] { 3, 4 }, Shown(vm));
					vm.HandleKey(Key.PageUp, KeyModifiers.None);
					await vm.WhenSettled;
					Assert.Equal(new[] { 1, 2 }, Shown(vm));
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task The_book_decides_the_direction_and_a_book_that_does_not_say_takes_the_default_and_the_toggle_changes_it()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel left, FakeSession s1) = await Open(6, rightToLeft: false, options: Options(rightToLeft: true));
				(ReaderViewModel dflt, FakeSession s2) = await Open(6, rightToLeft: null, options: Options(rightToLeft: true));
				(ReaderViewModel none, FakeSession s3) = await Open(6, rightToLeft: null);
				try
				{
					Assert.False(left.RightToLeft); // the book says no
					Assert.True(dflt.RightToLeft); // the book says nothing: the default
					Assert.False(none.RightToLeft);
					int raised = 0;
					none.PropertyChanged += (s, e) => raised += e.PropertyName == nameof(ReaderViewModel.RightToLeft) ? 1 : 0;
					none.HandleKey(Key.R, KeyModifiers.Meta | KeyModifiers.Alt);
					Assert.True(none.RightToLeft);
					Assert.Equal(1, raised);
					Assert.Equal("Two pages · Right to left", none.ModeText);
					none.ToggleRightToLeft();
					Assert.False(none.RightToLeft);
					none.SetRightToLeft(false);
					Assert.Equal(2, raised);
				}
				finally
				{
					left.Dispose();
					dflt.Dispose();
					none.Dispose();
				}
			});
		}

		[Fact]
		public async Task Toggling_the_direction_mirrors_the_spread_on_screen_and_a_swipe_turns_the_other_way()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(10, resume: 3);
				try
				{
					Assert.True(vm.Arrangement.Pages[0].X < vm.Arrangement.Pages[1].X);
					vm.ToggleRightToLeft();
					Assert.True(vm.Arrangement.Pages[0].X > vm.Arrangement.Pages[1].X);
					// fingers moving left: forward in a left-to-right book, back in a right-to-left one
					long t = 0;
					for (int i = 0; i < 5; i++, t += 8)
					{
						vm.HandleWheel(new Vector(-0.4, 0), t);
					}
					await vm.WhenSettled;
					Assert.Equal(1, vm.CurrentPage);
					t += 1000;
					for (int i = 0; i < 5; i++, t += 8)
					{
						vm.HandleWheel(new Vector(0.4, 0), t);
					}
					await vm.WhenSettled;
					Assert.Equal(3, vm.CurrentPage);
					vm.ToggleRightToLeft();
					t += 1000;
					for (int i = 0; i < 5; i++, t += 8)
					{
						vm.HandleWheel(new Vector(-0.4, 0), t);
					}
					await vm.WhenSettled;
					Assert.Equal(5, vm.CurrentPage);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task Reading_right_to_left_a_page_wider_than_the_window_starts_at_its_right_edge()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(8, resume: 1, rightToLeft: true);
				try
				{
					vm.ViewportSize = new Size(500, 600);
					vm.SetFit(ImageFitMode.FitHeight);
					Assert.True(vm.Arrangement.MaxScrollX > 100);
					Assert.Equal(vm.Arrangement.MaxScrollX, vm.ScrollX, 3);
					vm.SetRightToLeft(false);
					Assert.Equal(0, vm.ScrollX);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		// ---- Zoom and pan

		[Fact]
		public async Task Zoom_steps_up_and_down_between_the_fit_and_the_limit_and_resets()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(6, options: Options(PageLayoutMode.Single));
				try
				{
					Assert.Equal(1.0, vm.Zoom);
					vm.HandleKey(Key.OemPlus, KeyModifiers.Meta);
					Assert.Equal(1.25, vm.Zoom, 9);
					Assert.Equal("One page · 125 %", vm.ModeText);
					vm.HandleKey(Key.OemPlus, KeyModifiers.Meta | KeyModifiers.Shift);
					Assert.Equal(1.5625, vm.Zoom, 9);
					vm.HandleKey(Key.OemMinus, KeyModifiers.Meta);
					Assert.Equal(1.25, vm.Zoom, 9);
					vm.HandleKey(Key.OemMinus, KeyModifiers.Meta);
					vm.HandleKey(Key.OemMinus, KeyModifiers.Meta); // not below the fit
					Assert.Equal(1.0, vm.Zoom, 9);
					for (int i = 0; i < 30; i++)
					{
						vm.ZoomIn();
					}
					Assert.Equal(8.0, vm.Zoom, 9); // the limit
					vm.HandleKey(Key.D0, KeyModifiers.Meta);
					Assert.Equal(1.0, vm.Zoom);
					Assert.Equal("One page", vm.ModeText);
					vm.SetZoom(100);
					Assert.Equal(8.0, vm.Zoom);
					vm.SetZoom(0.2);
					Assert.Equal(1.0, vm.Zoom);
					vm.ZoomBy(0);
					vm.ZoomBy(double.NaN);
					vm.ZoomBy(-3);
					vm.ZoomBy(double.PositiveInfinity);
					Assert.Equal(1.0, vm.Zoom);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task A_custom_zoom_range_is_respected()
		{
			await Ui.Run(async () =>
			{
				ReaderOptions o = Options(PageLayoutMode.Single);
				o.ZoomMax = 3;
				o.ZoomStep = 2;
				(ReaderViewModel vm, FakeSession session) = await Open(3, options: o);
				try
				{
					vm.ZoomIn();
					vm.ZoomIn();
					Assert.Equal(3.0, vm.Zoom);
					vm.ZoomOut();
					Assert.Equal(1.5, vm.Zoom, 9);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task Zooming_keeps_the_point_under_the_pointer_in_place_and_the_zoom_survives_a_page_turn()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(8, resume: 1, options: Options(PageLayoutMode.Double));
				try
				{
					vm.ViewportSize = new Size(800, 700);
					PageArrangement before = vm.Arrangement;
					var focus = new Point(400, 350);
					double ux = (focus.X - before.Content.X) / before.Content.Width;
					double uy = (focus.Y - before.Content.Y) / before.Content.Height;
					vm.ZoomBy(3, focus);
					PageArrangement after = vm.Arrangement;
					Assert.Equal(3, after.Scale / before.Scale, 6);
					Assert.Equal(ux, (focus.X - after.Content.X) / after.Content.Width, 6);
					Assert.Equal(uy, (focus.Y - after.Content.Y) / after.Content.Height, 6);
					Assert.InRange(vm.ScrollX, 0, after.MaxScrollX);
					Assert.InRange(vm.ScrollY, 0, after.MaxScroll);
					// a focus point in a corner cannot be held (the content may not leave the window): the scroll stays inside the content
					vm.ZoomReset();
					vm.ZoomBy(8, new Point(0, 0));
					Assert.Equal(0, vm.ScrollX, 6);
					Assert.Equal(0, vm.ScrollY, 6);
					vm.ZoomReset();
					vm.ZoomBy(8, new Point(800, 700));
					Assert.Equal(vm.Arrangement.MaxScrollX, vm.ScrollX, 6);
					Assert.Equal(vm.Arrangement.MaxScroll, vm.ScrollY, 6);
					// the zoom stays when the page turns, the scroll goes back to the start of the new spread
					vm.PanBy(new Vector(-1e6, -1e6));
					vm.NextPage();
					await vm.WhenSettled;
					Assert.Equal(8.0, vm.Zoom);
					Assert.Equal(0, vm.ScrollX);
					Assert.Equal(0, vm.ScrollY);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task Panning_is_clamped_to_the_content_and_does_nothing_when_the_page_fits()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(6, options: Options(PageLayoutMode.Single));
				try
				{
					vm.ViewportSize = new Size(800, 700);
					Assert.False(vm.CanPan);
					Assert.False(vm.PanBy(new Vector(50, 50)));
					Assert.Equal(0, vm.ScrollX);
					vm.SetZoom(2);
					Assert.True(vm.CanPan);
					PageArrangement a = vm.Arrangement;
					Assert.True(a.MaxScrollX > 0 && a.MaxScroll > 0);
					vm.PanBy(new Vector(-1e9, -1e9)); // zooming about the centre scrolled to the middle: go to the start
					Assert.True(vm.PanBy(new Vector(30, 20)));
					Assert.Equal(30, vm.ScrollX, 6);
					Assert.Equal(20, vm.ScrollY, 6);
					Assert.True(vm.PanBy(new Vector(1e6, 1e6)));
					Assert.Equal(a.MaxScrollX, vm.ScrollX, 6);
					Assert.Equal(a.MaxScroll, vm.ScrollY, 6);
					Assert.False(vm.PanBy(new Vector(10, 10))); // already at the end
					Assert.True(vm.PanBy(new Vector(-1e9, -1e9)));
					Assert.Equal(0, vm.ScrollX);
					Assert.Equal(0, vm.ScrollY);
					Assert.False(vm.PanBy(new Vector(-5, -5)));
					// the window shrinks: the scroll stays inside the new content
					vm.PanBy(new Vector(1e6, 1e6));
					vm.ViewportSize = new Size(500, 400);
					Assert.Equal(vm.Arrangement.MaxScrollX, vm.ScrollX, 6);
					Assert.Equal(vm.Arrangement.MaxScroll, vm.ScrollY, 6);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task A_zoomed_page_is_panned_by_the_wheel_and_a_swipe_still_turns_a_page_that_fits()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(10, resume: 3, options: Options(PageLayoutMode.Double));
				try
				{
					vm.ViewportSize = new Size(800, 700);
					vm.SetZoom(2);
					vm.PanBy(new Vector(-1e9, -1e9));
					long t = 0;
					Assert.True(vm.HandleWheel(new Vector(-1, -2), t)); // fingers moving left and up: the content moves left and up
					Assert.Equal(50, vm.ScrollX, 6);
					Assert.Equal(100, vm.ScrollY, 6);
					Assert.True(vm.HandleWheel(new Vector(2, 1), t + 8));
					Assert.Equal(0, vm.ScrollX, 6);
					Assert.Equal(50, vm.ScrollY, 6);
					for (int i = 0; i < 6; i++, t += 8)
					{
						vm.HandleWheel(new Vector(-0.5, 0), t); // a horizontal swipe pans, it does not turn the page
					}
					Assert.Equal(3, vm.CurrentPage);
					Assert.True(vm.ScrollX > 100);
					vm.ZoomReset();
					t += 1000;
					for (int i = 0; i < 6; i++, t += 8)
					{
						vm.HandleWheel(new Vector(-0.5, 0), t);
					}
					await vm.WhenSettled;
					Assert.Equal(5, vm.CurrentPage); // fitting again: the swipe turns the page
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		// ---- Layout keys and full screen

		[Fact]
		public async Task The_option_command_keys_choose_the_layout()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(8, resume: 3, options: Options(PageLayoutMode.Single));
				try
				{
					var cmdAlt = KeyModifiers.Meta | KeyModifiers.Alt;
					Assert.True(vm.HandleKey(Key.D2, cmdAlt));
					await vm.WhenSettled;
					Assert.Equal(PageLayoutMode.Double, vm.LayoutMode);
					Assert.Equal(new[] { 3, 4 }, Shown(vm));
					Assert.True(vm.HandleKey(Key.D3, cmdAlt));
					Assert.Equal(PageLayoutMode.DoubleAdaptive, vm.LayoutMode);
					Assert.True(vm.HandleKey(Key.D1, cmdAlt));
					await vm.WhenSettled;
					Assert.Equal(PageLayoutMode.Single, vm.LayoutMode);
					Assert.Equal(new[] { 3 }, Shown(vm));
					Assert.False(vm.HandleKey(Key.D4, cmdAlt));
					Assert.False(vm.HandleKey(Key.D2, KeyModifiers.Alt)); // an option key alone is not a command
					Assert.Equal(ImageFitMode.Fit, vm.Fit);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task Full_screen_toggles_with_ctrl_cmd_f_and_escape_leaves_it_before_it_closes_the_reader()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(4);
				try
				{
					int closes = 0;
					vm.CloseRequested += (s, e) => closes++;
					Assert.False(vm.IsFullScreen);
					Assert.True(vm.ShowStatus);
					Assert.True(vm.HandleKey(Key.F, KeyModifiers.Meta | KeyModifiers.Control));
					Assert.True(vm.IsFullScreen);
					Assert.False(vm.ShowStatus);
					Assert.True(vm.HandleKey(Key.Escape, KeyModifiers.None));
					Assert.False(vm.IsFullScreen);
					Assert.Equal(0, closes);
					Assert.True(vm.HandleKey(Key.F, KeyModifiers.Meta | KeyModifiers.Control));
					Assert.True(vm.HandleKey(Key.F, KeyModifiers.Meta | KeyModifiers.Control));
					Assert.False(vm.IsFullScreen);
					vm.HandleKey(Key.Escape, KeyModifiers.None);
					Assert.Equal(1, closes);
					Assert.False(vm.HandleKey(Key.F, KeyModifiers.Control)); // not a reader command without Cmd
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task A_book_of_one_page_in_two_page_mode_is_shown_as_a_single_page_and_marked_read_at_its_end()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(1);
				try
				{
					Assert.Equal(new[] { 0 }, Shown(vm));
					Assert.Equal(vm.Arrangement.Content, vm.Arrangement.Pages[0]);
					vm.NextPage();
					Assert.Equal(1, session.EndReached);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}
	}
}
