using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using ComicRack.Mac.ViewModels;
using cYo.Projects.ComicRack.Engine.Display;
using Xunit;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>The reader view model with a generated book (no Core): paging boundaries, resume, keys, scrolling, stale page turns, failures and disposal.</summary>
	public class ReaderViewModelTests
	{
		private static ReaderOptions Options(List<string> log = null)
		{
			return new ReaderOptions
			{
				LoadingDelay = TimeSpan.FromMilliseconds(20),
				Log = log == null ? (Action<string>)(m => { }) : log.Add,
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

		/// <summary>Opens a book of <paramref name="pages"/> pages; waits until the prefetch of the first window is done.</summary>
		private static async Task<(ReaderViewModel vm, FakeSession session)> Open(int pages = 10, int resume = 0, Action<FakePageDecoder> configure = null, ReaderOptions options = null, bool waitForPrefetch = true)
		{
			var decoder = new FakePageDecoder { PageCount = pages };
			configure?.Invoke(decoder);
			var session = new FakeSession(decoder) { ResumePage = resume };
			var vm = new ReaderViewModel(session, options ?? Options());
			await vm.OpenAsync();
			if (waitForPrefetch && vm.IsOpen)
			{
				int expected = layoutWindow(vm.CurrentPage, pages).Count;
				await WaitFor(() => vm.Pages.GetStats().CachedCount >= expected, "first window cached");
			}
			return (vm, session);
		}

		private static IReadOnlyList<int> layoutWindow(int position, int pages) => SinglePageLayout.Instance.Window(position, pages);

		[Fact]
		public async Task Opening_shows_the_resume_page_and_prefetches_the_next_two_and_the_previous_one()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(resume: 4);
				try
				{
					Assert.True(vm.IsOpen);
					Assert.Equal(4, vm.CurrentPage);
					Assert.Equal("5 / 10", vm.PageText);
					Assert.Equal("Fake Book", vm.Title);
					Assert.False(vm.HasNotice);
					Assert.NotNull(vm.Scene);
					Assert.Equal(4, vm.Scene.Position);
					Assert.Equal(4, vm.Scene.Pages[0].Index);
					Assert.True(PageImages.Near(PageImages.PixelOf(vm.Scene.Pages[0].Image.Image, 50, 50), FakePageDecoder.ColorFor(4)));
					Assert.Equal(4, session.LastReported); // recorded in the book as soon as it is shown
					Assert.Equal(new[] { 4, 5, 6, 3 }, session.FakeDecoder.Order.OrderBy(p => Array.IndexOf(new[] { 4, 5, 6, 3 }, p)).ToArray());
					Assert.Equal(4, session.FakeDecoder.Started); // nothing else was decoded
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task A_book_that_cannot_be_opened_says_why_and_does_nothing_else()
		{
			await Ui.Run(async () =>
			{
				var session = new FakeSession { OpenFailure = new System.IO.InvalidDataException("The book could not be opened: x.cbz") };
				var vm = new ReaderViewModel(session, Options());
				await vm.OpenAsync();
				Assert.False(vm.IsOpen);
				Assert.Equal("The book could not be opened: x.cbz", vm.Notice);
				Assert.True(vm.HasNotice);
				Assert.Null(vm.Pages);
				vm.NextPage();
				vm.HandleKey(Key.PageDown, KeyModifiers.None);
				Assert.Empty(session.Reported);
				vm.Dispose();
				Assert.True(session.IsDisposed);
				Assert.Empty(session.Reported); // a book that never opened is not touched
			});
		}

		[Fact]
		public async Task A_book_without_pages_shows_a_notice()
		{
			await Ui.Run(async () =>
			{
				var session = new FakeSession(new FakePageDecoder { PageCount = 0 });
				var vm = new ReaderViewModel(session, Options());
				await vm.OpenAsync();
				Assert.False(vm.IsOpen);
				Assert.Equal("The book has no pages.", vm.Notice);
				vm.Dispose();
			});
		}

		[Fact]
		public async Task Paging_stops_at_both_ends_and_each_step_is_recorded()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(pages: 3);
				try
				{
					vm.PreviousPage(); // already on the first page
					Assert.Equal(0, vm.CurrentPage);
					vm.NextPage();
					Assert.Equal(1, vm.CurrentPage);
					vm.NextPage();
					Assert.Equal(2, vm.CurrentPage);
					Assert.Equal("3 / 3", vm.PageText);
					vm.NextPage(); // past the last page
					Assert.Equal(2, vm.CurrentPage);
					Assert.Equal(1, session.EndReached);
					vm.PreviousPage();
					Assert.Equal(1, vm.CurrentPage);
					vm.FirstPage();
					Assert.Equal(0, vm.CurrentPage);
					vm.LastPage();
					Assert.Equal(2, vm.CurrentPage);
					Assert.Equal(new[] { 0, 1, 2, 1, 0, 2 }, session.Reported.ToArray());
					await vm.WhenSettled;
					Assert.Equal(2, vm.Scene.Position);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task A_book_of_one_page_has_nowhere_to_go_and_reaching_its_end_is_reported()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(pages: 1);
				try
				{
					vm.NextPage();
					vm.PreviousPage();
					vm.FirstPage();
					vm.LastPage();
					Assert.Equal(0, vm.CurrentPage);
					Assert.Equal(1, session.EndReached);
					Assert.Equal("1 / 1", vm.PageText);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task The_keys_page_scroll_fit_and_close()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(pages: 20, resume: 10);
				int closes = 0, browser = 0, undock = 0, next = 0, previous = 0;
				vm.CloseRequested += (s, e) => closes++;
				vm.ToggleBrowserRequested += (s, e) => browser++;
				vm.ToggleUndockRequested += (s, e) => undock++;
				vm.NextTabRequested += (s, e) => next++;
				vm.PreviousTabRequested += (s, e) => previous++;
				try
				{
					Assert.True(vm.HandleKey(Key.PageDown, KeyModifiers.None));
					Assert.Equal(11, vm.CurrentPage);
					Assert.True(vm.HandleKey(Key.PageUp, KeyModifiers.None));
					Assert.Equal(10, vm.CurrentPage);
					Assert.True(vm.HandleKey(Key.Right, KeyModifiers.Alt)); // Alt+Right: Next Page
					Assert.Equal(11, vm.CurrentPage);
					Assert.True(vm.HandleKey(Key.Left, KeyModifiers.Alt));
					Assert.Equal(10, vm.CurrentPage);
					Assert.True(vm.HandleKey(Key.N, KeyModifiers.Meta)); // the Read menu: Cmd+N, Cmd+P, Cmd+B, Cmd+E
					Assert.Equal(11, vm.CurrentPage);
					Assert.True(vm.HandleKey(Key.P, KeyModifiers.Meta));
					Assert.Equal(10, vm.CurrentPage);
					Assert.True(vm.HandleKey(Key.Space, KeyModifiers.None)); // the page fits the window: Space turns the page
					Assert.Equal(11, vm.CurrentPage);
					Assert.True(vm.HandleKey(Key.Space, KeyModifiers.Shift));
					Assert.Equal(10, vm.CurrentPage);
					Assert.True(vm.HandleKey(Key.End, KeyModifiers.Meta));
					Assert.Equal(19, vm.CurrentPage);
					Assert.True(vm.HandleKey(Key.Home, KeyModifiers.Meta));
					Assert.Equal(0, vm.CurrentPage);
					Assert.True(vm.HandleKey(Key.E, KeyModifiers.Meta));
					Assert.Equal(19, vm.CurrentPage);
					Assert.True(vm.HandleKey(Key.B, KeyModifiers.Meta));
					Assert.Equal(0, vm.CurrentPage);

					Assert.True(vm.HandleKey(Key.D3, KeyModifiers.Meta));
					Assert.Equal(ImageFitMode.FitWidth, vm.Fit);
					Assert.True(vm.HandleKey(Key.D5, KeyModifiers.Meta));
					Assert.Equal(ImageFitMode.FitHeight, vm.Fit);
					Assert.True(vm.HandleKey(Key.D2, KeyModifiers.Meta));
					Assert.Equal(ImageFitMode.Fit, vm.Fit);
					Assert.Equal("Fit All", vm.FitText);
					Assert.True(vm.HandleKey(Key.D3, KeyModifiers.None)); // the single keys 2, 3 and 5 as well
					Assert.Equal(ImageFitMode.FitWidth, vm.Fit);
					Assert.True(vm.HandleKey(Key.D2, KeyModifiers.None));
					Assert.Equal(ImageFitMode.Fit, vm.Fit);

					Assert.False(vm.HandleKey(Key.A, KeyModifiers.None));
					Assert.False(vm.HandleKey(Key.Right, KeyModifiers.Control)); // not ours
					Assert.False(vm.HandleKey(Key.Up, KeyModifiers.Alt));
					Assert.False(vm.HandleKey(Key.Q, KeyModifiers.Meta)); // Cmd+Q belongs to the menu
					Assert.Equal(0, closes);
					// Esc is ShowBrowser, Tab and Shift+Tab are NextTab and PrevTab, D is ToggleUndockReader; only Cmd+W closes the book's tab
					Assert.True(vm.HandleKey(Key.Escape, KeyModifiers.None));
					Assert.Equal(1, browser);
					Assert.True(vm.HandleKey(Key.Tab, KeyModifiers.None));
					Assert.True(vm.HandleKey(Key.Tab, KeyModifiers.Shift));
					Assert.Equal((1, 1), (next, previous));
					Assert.True(vm.HandleKey(Key.D, KeyModifiers.None));
					Assert.Equal(1, undock);
					Assert.False(vm.HandleKey(Key.D, KeyModifiers.Shift)); // Shift+D is the realistic display (P5.23f)
					Assert.Equal(0, closes);
					Assert.True(vm.HandleKey(Key.W, KeyModifiers.Meta));
					Assert.Equal(1, closes);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task A_fit_mode_that_is_not_supported_yet_becomes_best_fit_and_the_change_is_announced_once()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open();
				int changes = 0;
				vm.FitChanged += (s, e) => changes++;
				try
				{
					vm.SetFit(ImageFitMode.FitWidth);
					vm.SetFit(ImageFitMode.FitWidth);
					Assert.Equal(1, changes);
					vm.SetFit(ImageFitMode.Original);
					Assert.Equal(ImageFitMode.Fit, vm.Fit);
					Assert.Equal(2, changes);
					Assert.Equal("Fit Width", ReaderViewModel.FitName(ImageFitMode.FitWidth));
					Assert.Equal("Fit Height", ReaderViewModel.FitName(ImageFitMode.FitHeight));
					Assert.Equal("Fit All", ReaderViewModel.FitName(ImageFitMode.Fit));
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task Space_scrolls_a_tall_page_before_it_turns_and_shift_space_goes_back_to_the_bottom_of_the_previous_page()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(pages: 6, resume: 2, configure: d => d.Size = p => (1200, 1800));
				try
				{
					vm.ViewportSize = new Size(1000, 800);
					vm.SetFit(ImageFitMode.FitWidth); // 1000 x 1500: 700 below the window
					Assert.Equal(700, vm.Arrangement.MaxScroll, 3);
					Assert.True(vm.CanScrollDown);
					Assert.False(vm.CanScrollUp);

					vm.HandleKey(Key.Space, KeyModifiers.None);
					Assert.Equal(2, vm.CurrentPage);
					Assert.Equal(700, vm.ScrollY, 3); // 90% of 800 is 720, held to the end of the page
					Assert.False(vm.CanScrollDown);

					vm.HandleKey(Key.Space, KeyModifiers.None); // at the bottom: the next page, at its top
					await vm.WhenSettled;
					Assert.Equal(3, vm.CurrentPage);
					Assert.Equal(0, vm.ScrollY);

					vm.HandleKey(Key.Down, KeyModifiers.None);
					Assert.Equal(60, vm.ScrollY, 3);
					vm.HandleKey(Key.Up, KeyModifiers.None);
					Assert.Equal(0, vm.ScrollY, 3);

					vm.HandleKey(Key.Space, KeyModifiers.Shift); // at the top: back, landing on the bottom of page 3
					await vm.WhenSettled;
					Assert.Equal(2, vm.CurrentPage);
					Assert.Equal(700, vm.ScrollY, 3);

					vm.HandleKey(Key.Space, KeyModifiers.Shift); // not at the top: scrolls up first
					Assert.Equal(2, vm.CurrentPage);
					Assert.Equal(0, vm.ScrollY, 3);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task A_fit_change_and_a_bigger_window_put_the_scroll_back_in_range()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(pages: 3, configure: d => d.Size = p => (1200, 1800));
				try
				{
					vm.ViewportSize = new Size(1000, 800);
					vm.SetFit(ImageFitMode.FitWidth);
					vm.ScrollBy(500);
					Assert.Equal(500, vm.ScrollY, 3);
					vm.ScrollBy(-9999);
					Assert.Equal(0, vm.ScrollY, 3);
					vm.ScrollBy(9999);
					Assert.Equal(700, vm.ScrollY, 3);
					vm.ViewportSize = new Size(1000, 1600); // now the whole 1500 px page fits: nothing to scroll
					Assert.Equal(0, vm.ScrollY, 3);
					vm.ViewportSize = new Size(1000, 800);
					vm.ScrollBy(300);
					vm.SetFit(ImageFitMode.Fit);
					Assert.Equal(0, vm.ScrollY);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task A_cached_page_turn_is_complete_before_the_command_returns()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(pages: 10, resume: 3);
				try
				{
					vm.NextPage(); // page 5 (index 4) was prefetched
					Assert.True(vm.WhenSettled.IsCompleted);
					Assert.Equal(4, vm.Scene.Position);
					Assert.Equal(4, vm.Scene.Pages[0].Index);
					Assert.False(vm.HasNotice);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task Paging_on_quickly_shows_only_the_last_page_and_the_passed_over_ones_are_not_decoded()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(pages: 40, configure: d => d.Delay = TimeSpan.FromMilliseconds(30));
				var shown = new List<int>();
				vm.PropertyChanged += (s, e) =>
				{
					if (e.PropertyName == nameof(ReaderViewModel.Scene) && vm.Scene != null)
					{
						shown.Add(vm.Scene.Position);
					}
				};
				try
				{
					int before = session.FakeDecoder.Started;
					for (int i = 0; i < 15; i++) // the arrow key held down
					{
						vm.NextPage();
					}
					Assert.Equal(15, vm.CurrentPage);
					await vm.WhenSettled;
					Assert.Equal(15, vm.Scene.Position);
					// Pages 1 and 2 were prefetched, so the first two steps showed at once; 3 to 14 were never shown.
					Assert.Equal(15, shown[shown.Count - 1]);
					Assert.All(shown.Take(shown.Count - 1), p => Assert.InRange(p, 1, 2));
					await WaitFor(() => vm.Pages.GetStats().InFlight == 0, "decoder idle");
					int decodedSince = session.FakeDecoder.Started - before;
					// 15 pages were passed over; the window of the last one (15, 16, 17, 14) is what had to be decoded, plus a few that were already running
					Assert.True(decodedSince < 15, "decodes since the key was pressed: " + decodedSince);
					Assert.Equal(15, session.LastReported);
					Assert.Equal(Enumerable.Range(1, 15), session.Reported.Skip(session.Reported.Count - 15));
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task The_previous_page_stays_on_screen_until_the_new_one_is_ready_and_a_slow_page_shows_a_loading_notice()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(pages: 30);
				try
				{
					session.FakeDecoder.Hold();
					vm.LastPage(); // page 30: not prefetched
					Assert.Equal(29, vm.CurrentPage);
					Assert.Equal("30 / 30", vm.PageText); // the counter is ahead, the picture is the old one
					Assert.Equal(0, vm.Scene.Position);
					Assert.False(vm.HasLoading);
					await WaitFor(() => vm.HasLoading, "loading notice");
					Assert.Equal("Loading Page 30...", vm.LoadingText);
					Assert.False(vm.HasNotice); // the message overlay is for the book, not for a page that takes its time
					Assert.Equal(0, vm.Scene.Position);

					session.FakeDecoder.Open();
					await vm.WhenSettled;
					Assert.Equal(29, vm.Scene.Position);
					Assert.False(vm.HasLoading);
					Assert.False(vm.HasNotice);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task A_page_that_cannot_be_decoded_is_explained_and_a_later_visit_tries_again()
		{
			await Ui.Run(async () =>
			{
				bool broken = true;
				var log = new List<string>();
				(ReaderViewModel vm, FakeSession session) = await Open(pages: 10, resume: 1, configure: d => d.FailWith = p => broken && p == 3 ? new System.IO.InvalidDataException("Page 4 could not be read from x.cbz.") : null, options: Options(log), waitForPrefetch: false);
				try
				{
					await WaitFor(() => vm.Pages.GetStats().Failures == 1 && vm.Pages.GetStats().CachedCount == 3, "prefetch done, page 4 failed");
					await vm.GoToAsync(3);
					Assert.Null(vm.Scene);
					Assert.Equal("Page 4 could not be loaded: Page 4 could not be read from x.cbz.", vm.Notice);
					Assert.Contains(log, m => m.Contains("Page 4 could not be loaded"));

					await vm.GoToAsync(4); // the next page is fine, and the notice goes
					Assert.False(vm.HasNotice);
					Assert.Equal(4, vm.Scene.Position);

					// Moving to page 5 put page 4 (the one behind) back into the window, so a prefetch of the page that fails may still be
					// running. A visit that joins a running decode gets that decode's result, so the repair waits for the service to be idle.
					await WaitFor(() => vm.Pages.GetStats().InFlight == 0 && vm.Pages.GetStats().Queued == 0, "the prefetch of the broken page is over");
					broken = false;
					await vm.GoToAsync(3);
					Assert.NotNull(vm.Scene);
					Assert.Equal(3, vm.Scene.Position);
					Assert.False(vm.HasNotice);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task Closing_records_the_page_stops_the_service_releases_the_pictures_and_the_book_and_can_be_repeated()
		{
			await Ui.Run(async () =>
			{
				Gc.Flush();
				long live = Gc.Live;
				(ReaderViewModel vm, FakeSession session) = await Open(pages: 10, resume: 5);
				PageService pages = vm.Pages;
				Assert.True(Gc.Live > live);
				vm.NextPage();
				vm.Dispose();
				Assert.Equal(6, session.LastReported);
				Assert.True(session.IsDisposed);
				Assert.True(pages.IsDisposed);
				Assert.Null(vm.Pages);
				Assert.Null(vm.Scene);
				Gc.Flush();
				Assert.Equal(live, Gc.Live);
				vm.Dispose();
				vm.NextPage(); // nothing happens, nothing throws
				Assert.True(vm.IsDisposed);
			});
		}

		[Fact]
		public async Task Closing_while_pages_are_still_loading_returns_promptly_and_leaks_nothing()
		{
			await Ui.Run(async () =>
			{
				Gc.Flush();
				long live = Gc.Live;
				(ReaderViewModel vm, FakeSession session) = await Open(pages: 30, waitForPrefetch: true);
				session.FakeDecoder.Hold();
				vm.LastPage();
				await WaitFor(() => session.FakeDecoder.InFlight > 0, "decode in flight");
				var watch = Stopwatch.StartNew();
				vm.Dispose();
				Assert.True(watch.ElapsedMilliseconds < 2000, "Dispose took " + watch.ElapsedMilliseconds + " ms");
				Assert.Equal(0, session.FakeDecoder.InFlight);
				await Task.Delay(30); // the pending load ends on its own
				Gc.Flush();
				Assert.Equal(live, Gc.Live);
			});
		}

		[Fact]
		public async Task Closing_while_the_book_is_still_opening_cancels_the_open()
		{
			await Ui.Run(async () =>
			{
				var session = new FakeSession(openImmediately: false);
				var vm = new ReaderViewModel(session, Options());
				Task opening = vm.OpenAsync();
				Assert.Equal("Opening the book 'Fake Book'...", vm.Notice);
				vm.Dispose();
				await opening; // returns quietly
				Assert.False(vm.IsOpen);
				Assert.True(session.IsDisposed);
				Assert.Empty(session.Reported);
			});
		}

		[Fact]
		public async Task The_scene_shares_its_pictures_with_the_cache_and_gives_them_back()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(pages: 5);
				try
				{
					CoverImage page0 = vm.Scene.Pages[0].Image;
					Assert.Equal(2, page0.References); // the cache and the scene
					ReaderScene scene = vm.Scene.Retain(); // a frame in flight
					vm.NextPage();
					Assert.Equal(2, page0.References); // the cache and the frame: the view model's scene let go
					scene.Release();
					Assert.Equal(1, page0.References);
				}
				finally
				{
					vm.Dispose();
				}
			});
		}

		[Fact]
		public async Task A_horizontal_swipe_turns_one_page_and_a_vertical_wheel_scrolls_a_tall_page()
		{
			await Ui.Run(async () =>
			{
				(ReaderViewModel vm, FakeSession session) = await Open(pages: 10, resume: 5, configure: d => d.Size = p => (1200, 1800));
				try
				{
					vm.ViewportSize = new Size(1000, 800);
					// a swipe to the left: finger events then a momentum tail
					long t = 0;
					int handled = 0;
					for (int i = 0; i < 20; i++, t += 8)
					{
						handled += vm.HandleWheel(new Vector(-0.3, 0), t) ? 1 : 0;
					}
					for (int i = 0, d = 25; i < 80; i++, t += 8, d = d * 94 / 100)
					{
						handled += vm.HandleWheel(new Vector(-d / 100.0, 0), t) ? 1 : 0;
					}
					Assert.Equal(1, handled);
					Assert.Equal(6, vm.CurrentPage);
					// and to the right: back
					t += 700;
					for (int i = 0; i < 20; i++, t += 8)
					{
						vm.HandleWheel(new Vector(0.3, 0), t);
					}
					Assert.Equal(5, vm.CurrentPage);

					vm.SetFit(ImageFitMode.FitWidth);
					t += 700;
					Assert.True(vm.HandleWheel(new Vector(0, -4), t)); // two fingers moving up: the page moves up, 4 lines = 200 points
					Assert.Equal(200, vm.ScrollY, 3);
					Assert.True(vm.HandleWheel(new Vector(0, 2), t + 8));
					Assert.Equal(100, vm.ScrollY, 3);
					vm.SetFit(ImageFitMode.Fit);
					Assert.False(vm.HandleWheel(new Vector(0, -4), t + 700)); // the page fits: nothing to scroll
				}
				finally
				{
					vm.Dispose();
				}
			});
		}
	}
}
