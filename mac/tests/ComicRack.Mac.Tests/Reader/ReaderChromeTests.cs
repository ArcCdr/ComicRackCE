using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using ComicRack.Mac.Themes;
using ComicRack.Mac.Views;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>
	/// P5.R3: the chrome of the reader window as the original draws it (tab strip with the toolbar, status bar, paper, page frame, overlays), the
	/// reader keys and the settings that belong to them. Frames are read pixel by pixel; the overlays are moved with <see cref="ReaderOverlayClock"/> by hand.
	/// </summary>
	public class ReaderChromeTests : IDisposable
	{
		private readonly long liveBefore;

		public ReaderChromeTests()
		{
			liveBefore = Gc.SettleLive();
			Ui.BeginTest();
		}

		public void Dispose()
		{
			long expected = Ui.LiveBaseline(liveBefore);
			Gc.WaitForLive(expected);
			Assert.Equal(expected, PixelBlock.LiveCount);
		}

		private static Task<ReaderHarness> Open(int pages = 12, int resume = 3, Func<int, (int, int)> size = null, ReaderOptions options = null, double width = 900, double height = 700)
		{
			var session = new FakeSession(new FakePageDecoder { PageCount = pages, Size = size ?? (p => (1200, 1800)) }) { ResumePage = resume };
			return ReaderHarness.Open(session, width, height, options);
		}

		/// <summary>A source of page pictures that hands out solid pictures of its own and can say that one arrived.</summary>
		private sealed class FakeThumbnails : INavigationThumbnailSource
		{
			private readonly HashSet<int> have = new HashSet<int>();
			public List<(int Page, int Height)> Asked { get; } = new List<(int, int)>();

			public event EventHandler<int> Ready;

			public void Provide(int page)
			{
				have.Add(page);
				Ready?.Invoke(this, page);
			}

			public CoverImage Get(int page, int height)
			{
				Asked.Add((page, height));
				if (!have.Contains(page))
				{
					return null;
				}
				var bitmap = new SKBitmap(80, 120);
				bitmap.Erase(FakePageDecoder.ColorFor(page));
				return new CoverImage(SKImage.FromBitmap(bitmap));
			}
		}

		[Fact]
		public async Task The_window_has_the_tab_strip_with_one_tab_the_toolbar_at_its_right_and_the_status_bar_segments()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open())
				{
					CrTabBar tabs = h.Window.Tabs;
					Assert.Equal(31, tabs.Bounds.Height);
					Assert.Single(tabs.Tabs);
					Assert.Equal("Fake Book", tabs.Tabs[0].Text);
					Assert.True(tabs.Tabs[0].CanClose);
					Assert.Same(h.Window.ReaderToolbar, tabs.ToolbarSlot);
					string[] names = { "PreviousPageButton", "NextPageButton", "PageLayoutButton", "FitButton", "ZoomButton", "FullScreenButton", "ToolsButton" };
					foreach (string name in names)
					{
						Assert.NotNull(h.Window.FindControl<CrToolButton>(name));
					}
					Assert.Equal("100%", h.Window.FindControl<CrSplitButton>("ZoomButton").Text);
					Assert.Equal("SinglePage", h.Window.FindControl<CrSplitButton>("PageLayoutButton").IconName);
					Assert.Equal("FitAll", h.Window.FindControl<CrSplitButton>("FitButton").IconName);
					Assert.Equal(24, h.Window.FindControl<CrStatusBar>("StatusBar").Bounds.Height);
					Assert.Equal("Fake Book", h.Window.FindControl<TextBlock>("BookSegment").Text);
					Assert.Equal("4", h.Window.FindControl<TextBlock>("PageSegment").Text);
					Assert.Equal("12", h.Window.FindControl<TextBlock>("PageCountSegment").Text);
					// the toolbar follows the state: the layout and fit pictures, the zoom text
					h.Press(Key.D8, KeyModifiers.Meta);
					await h.Settled();
					h.Press(Key.D3, KeyModifiers.Meta);
					h.Press(Key.OemPlus, KeyModifiers.Meta);
					Assert.Equal("TwoPageForced", h.Window.FindControl<CrSplitButton>("PageLayoutButton").IconName);
					Assert.Equal("FitWidth", h.Window.FindControl<CrSplitButton>("FitButton").IconName);
					Assert.Equal("125%", h.Window.FindControl<CrSplitButton>("ZoomButton").Text);
					// the arrow parts have the menus of the original with the items the reader has
					Assert.Equal(1, ((Avalonia.Controls.MenuFlyout)h.Window.FindControl<CrSplitButton>("PreviousPageButton").DropDown).Items.Count);
					Assert.Equal(5, ((Avalonia.Controls.MenuFlyout)h.Window.FindControl<CrSplitButton>("PageLayoutButton").DropDown).Items.Count);
					Assert.Equal(3, ((Avalonia.Controls.MenuFlyout)h.Window.FindControl<CrSplitButton>("FitButton").DropDown).Items.Count);
					// a click on the tab's close box closes the reader
					bool closed = false;
					h.Window.Closed += (s, e) => closed = true;
					tabs.Tabs[0].GetType(); // keep the tab alive
					h.Vm.Close();
					Assert.True(closed);
				}
			});
		}

		[Fact]
		public async Task The_buttons_of_the_toolbar_run_the_commands_of_the_original()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(resume: 2))
				{
					h.Window.FindControl<CrToolButton>("NextPageButton").Command.Execute(null);
					await h.Settled();
					Assert.Equal(3, h.Vm.CurrentPage);
					h.Window.FindControl<CrToolButton>("PreviousPageButton").Command.Execute(null);
					await h.Settled();
					Assert.Equal(2, h.Vm.CurrentPage);
					// the layout button cycles one page, two pages, two pages (adaptive); the fit button Fit All, Fit Width, Fit Height; the zoom button 100, 125, 150, 200
					var layout = h.Window.FindControl<CrToolButton>("PageLayoutButton");
					layout.Command.Execute(null);
					await h.Settled();
					Assert.Equal(cYo.Projects.ComicRack.Engine.Display.PageLayoutMode.Double, h.Vm.LayoutMode);
					layout.Command.Execute(null);
					await h.Settled();
					Assert.Equal(cYo.Projects.ComicRack.Engine.Display.PageLayoutMode.DoubleAdaptive, h.Vm.LayoutMode);
					layout.Command.Execute(null);
					await h.Settled();
					Assert.Equal(cYo.Projects.ComicRack.Engine.Display.PageLayoutMode.Single, h.Vm.LayoutMode);
					var fit = h.Window.FindControl<CrToolButton>("FitButton");
					fit.Command.Execute(null);
					Assert.Equal(cYo.Projects.ComicRack.Engine.Display.ImageFitMode.FitWidth, h.Vm.Fit);
					fit.Command.Execute(null);
					Assert.Equal(cYo.Projects.ComicRack.Engine.Display.ImageFitMode.FitHeight, h.Vm.Fit);
					fit.Command.Execute(null);
					Assert.Equal(cYo.Projects.ComicRack.Engine.Display.ImageFitMode.Fit, h.Vm.Fit);
					var zoom = h.Window.FindControl<CrToolButton>("ZoomButton");
					var seen = new List<string>();
					for (int i = 0; i < 5; i++)
					{
						zoom.Command.Execute(null);
						seen.Add(h.Vm.ZoomText);
					}
					Assert.Equal(new[] { "125%", "150%", "200%", "100%", "125%" }, seen);
					h.Window.FindControl<CrToolButton>("FullScreenButton").Command.Execute(null);
					Assert.True(h.Vm.IsFullScreen);
					Assert.True(h.Window.FindControl<CrToolButton>("FullScreenButton").IsChecked);
				}
			});
		}

		[Fact]
		public async Task The_paper_is_the_reader_background_and_each_page_has_a_black_outline_and_a_shadow_that_the_option_turns_off()
		{
			await Ui.Run(async () =>
			{
				Func<ReaderHarness, (SKColor paper, SKColor outline, SKColor shadow, SKColor far)> sample = h =>
				{
					WriteableBitmap frame = h.Capture("reader-frame-ornaments");
					Rect page = h.PageRects()[0];
					int y = (int)(page.Y + page.Height / 2);
					return (ReaderHarness.ColorAt(frame, (int)(page.X - 40), y), ReaderHarness.ColorAt(frame, (int)page.X, y),
						ReaderHarness.ColorAt(frame, (int)page.X - 2, y), ReaderHarness.ColorAt(frame, (int)(page.X - 20), y));
				};
				SKColor whiteSmoke = new SKColor(0xF5, 0xF5, 0xF5);
				using (ReaderHarness h = await Open(size: p => (1200, 1800)))
				{
					var (paper, outline, shadow, far) = sample(h);
					Assert.True(PageImages.Near(paper, whiteSmoke, 1), "paper " + paper);
					Assert.True(outline.Red < 40 && outline.Green < 40 && outline.Blue < 40, "outline " + outline);
					Assert.True(shadow.Red < whiteSmoke.Red - 20, "the shadow darkens the paper next to the page: " + shadow);
					Assert.True(far.Red >= shadow.Red, "and fades away from it");
					Assert.Equal(4, (int)PageFrame.ShadowDepth(h.PageRects()[0])); // 1 percent of the smaller side (about 430 wide)
				}
				ReaderOptions plain = ReaderHarness.DefaultOptions();
				plain.RealisticPages = false;
				using (ReaderHarness h = await Open(options: plain))
				{
					var (paper, outline, shadow, _) = sample(h);
					Assert.True(PageImages.Near(outline, FakePageDecoder.ColorFor(3), 3), "no outline: " + outline);
					Assert.True(PageImages.Near(shadow, whiteSmoke, 1), "no shadow: " + shadow);
				}
			});
		}

		[Fact]
		public async Task The_page_info_fades_in_stays_two_seconds_and_fades_out_at_each_new_page_and_the_option_turns_it_off()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open())
				{
					CrTextOverlay info = h.Window.PageInfo;
					Assert.Equal("4 (of 12)", info.Text);
					Assert.Equal(150, info.Bounds.Width); // 150 x 25 at least
					Assert.InRange(info.Bounds.Height, 25, 40); // 25 at least: the 16 unit text and the 15 units of glass make it about 31
					Rect where = Dpi.Area(info, h.Window);
					Rect reading = h.ReadingArea;
					Assert.Equal(reading.Right - 16, where.Right, 1); // top right inside the 16 unit padding
					Assert.Equal(reading.Top + 16, where.Top, 1);
					Assert.Equal(0, info.Opacity); // the clock is off in a test: nothing moved yet
					Assert.True(info.IsFading);
					ReaderOverlayClock.Advance(50);
					Assert.Equal(0.5, info.Opacity, 3);
					ReaderOverlayClock.Advance(50);
					Assert.Equal(1.0, info.Opacity, 3);
					ReaderOverlayClock.Advance(2000);
					Assert.Equal(1.0, info.Opacity, 3);
					ReaderOverlayClock.Advance(50);
					Assert.Equal(0.5, info.Opacity, 3);
					ReaderOverlayClock.Advance(100);
					Assert.Equal(0, info.Opacity);
					Assert.False(info.IsFading);
					// a page turn starts it again, with the new number
					h.Press(Key.PageDown);
					await h.Settled();
					Assert.Equal("5 (of 12)", info.Text);
					Assert.True(info.IsFading);
					ReaderOverlayClock.Advance(100);
					Assert.Equal(1.0, info.Opacity, 3);
					WriteableBitmap frame = h.Capture("reader-page-info");
					Rect box = where;
					Assert.True(ReaderHarness.DistinctColors(frame, new Rect(box.X, box.Y, box.Width, box.Height)) > 5, "the glass and the text are drawn");
					ReaderOverlayClock.Advance(5000);
					// the option: no overlay
					h.Vm.Options.ShowCurrentPageOverlay = false;
					h.Press(Key.PageDown);
					await h.Settled();
					Assert.False(info.IsFading);
					Assert.Equal(0, info.Opacity);
				}
			});
		}

		[Fact]
		public async Task Loading_and_the_message_are_text_overlays_in_the_middle_and_the_message_wins()
		{
			await Ui.Run(async () =>
			{
				var session = new FakeSession(new FakePageDecoder { PageCount = 5 }, openImmediately: false);
				var vm = new ComicRack.Mac.ViewModels.ReaderViewModel(session, ReaderHarness.DefaultOptions());
				using (var h = new ReaderHarness(vm, 800, 600))
				{
					h.Tick();
					Assert.True(h.Window.Message.IsVisible);
					Assert.Equal("Opening the book 'Fake Book'...", h.Window.Message.Text);
					Assert.Equal(300, h.Window.Message.Bounds.Width, 0);
					Assert.False(h.Window.Loading.IsVisible);
					Rect m = Dpi.Area(h.Window.Message, h.Window);
					Rect area = h.ReadingArea;
					Assert.Equal(area.Center.X, m.Center.X, 1);
					Assert.Equal(area.Center.Y, m.Center.Y, 1);
					vm.LoadingText = "Loading Page 2...";
					h.Tick();
					Assert.False(h.Window.Loading.IsVisible); // the message hides it
					session.CompleteOpen();
					await vm.OpenAsync();
					await vm.WhenSettled;
					h.Tick();
					Assert.False(h.Window.Message.IsVisible);
					vm.LoadingText = "Loading Page 3...";
					h.Tick();
					Assert.True(h.Window.Loading.IsVisible);
					Assert.Equal(200, h.Window.Loading.Bounds.Width, 0);
					vm.Options.ShowStatusOverlay = false;
					vm.LoadingText = "Loading Page 4...";
					h.Tick();
					Assert.False(h.Window.Loading.IsVisible);
				}
			});
		}

		[Fact]
		public async Task The_visible_part_overlay_shows_the_page_in_miniature_when_it_is_larger_than_the_window_and_has_a_close_button()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(size: p => (1200, 1800)))
				{
					CrPartOverlay part = h.Window.Part;
					Assert.Equal(0, part.Opacity);
					Assert.False(part.IsFading); // the page fits: no part to show
					h.Press(Key.D3, KeyModifiers.Meta); // Fit Width: taller than the window
					h.Tick();
					Assert.True(h.Vm.CanPan);
					Assert.True(part.IsFading);
					ReaderOverlayClock.Advance(100);
					Assert.Equal(1.0, part.Opacity, 3);
					// 200 x 200 box: a 2:3 page is 133 x 200 plus 16
					Size panel = CrPartOverlay.PanelSizeFor(new Size(1200, 1800));
					Assert.Equal(133 + 16, panel.Width, 0);
					Assert.Equal(200 + 16, panel.Height, 0);
					Assert.Equal(panel.Width, part.Bounds.Width, 0);
					Rect box = Dpi.Area(part, h.Window);
					Assert.Equal(h.ReadingArea.Right - 16, box.Right, 1);
					Assert.Equal(h.ReadingArea.Bottom - 16, box.Bottom, 1);
					Rect visible = part.VisibleRect, picture = part.PictureRect;
					Assert.True(visible.Width > 0 && visible.Height > 0 && visible.Height < picture.Height, "the window shows the top part of the page");
					Assert.Equal(picture.Top, visible.Top, 1);
					WriteableBitmap frame = h.Capture("reader-part-overlay");
					Assert.True(ReaderHarness.DistinctColors(frame, box) > 5);
					// scrolling to the bottom moves the visible part down and starts the fade again
					ReaderOverlayClock.Advance(5000);
					Assert.False(part.IsFading);
					h.Vm.ScrollBy(10000);
					Assert.True(part.IsFading);
					Assert.Equal(picture.Bottom, part.VisibleRect.Bottom, 1);
					// the close button turns the overlay off for good
					ReaderOverlayClock.Advance(100);
					Rect close = part.CloseRect;
					Assert.Equal(16, close.Width);
					Point onIt = new Point(box.X + close.Center.X, box.Y + close.Center.Y);
					h.Window.MouseMove(onIt);
					h.Window.MouseDown(onIt, MouseButton.Left);
					h.Window.MouseUp(onIt, MouseButton.Left);
					Assert.False(h.Vm.Options.ShowVisiblePagePartOverlay);
					Assert.False(part.IsFading);
					h.Vm.ScrollBy(-10000);
					Assert.False(part.IsFading);
				}
			});
		}

		[Fact]
		public async Task The_navigation_overlay_slides_in_at_the_edge_has_the_original_layout_and_browses()
		{
			await Ui.Run(async () =>
			{
				var thumbs = new FakeThumbnails();
				var session = new FakeSession(new FakePageDecoder { PageCount = 12, Size = p => (1200, 1800) }) { ResumePage = 5 };
				var vm = new ComicRack.Mac.ViewModels.ReaderViewModel(session, ReaderHarness.DefaultOptions());
				using (var h = new ReaderHarness(vm, 900, 700))
				{
					h.Window.Navigation.Thumbnails = thumbs;
					await vm.OpenAsync();
					await vm.WhenSettled;
					h.Tick();
					CrNavigationOverlay nav = h.Window.Navigation;
					Assert.Equal(12, nav.PageCount);
					Assert.Equal(5, nav.DisplayedPage);
					Assert.Equal("Fake Book", nav.Caption);
					Assert.False(nav.IsShown);
					Assert.Equal(1, nav.SlidePosition);
					// 0.9 of the reading area wide, 200 high, centred at the bottom
					Rect area = h.ReadingArea;
					Assert.Equal(Math.Floor(area.Width * 0.9), nav.Bounds.Width, 1);
					Assert.Equal(200, nav.Bounds.Height);
					// the pointer in the 500 x 50 zone at the bottom: it slides in after a delay of 500 ms in 300 ms
					Point inZone = new Point(area.Center.X, area.Bottom - 20);
					h.Window.MouseMove(inZone);
					h.Tick();
					Assert.True(nav.IsShown);
					ReaderOverlayClock.Advance(400);
					Assert.Equal(1, nav.SlidePosition); // still waiting for the delay
					ReaderOverlayClock.Advance(100 + 300);
					Assert.Equal(0, nav.SlidePosition, 3);
					WriteableBitmap frame = h.Capture("reader-navigation-overlay");
					Rect box = Dpi.Area(nav, h.Window);
					Assert.True(ReaderHarness.DistinctColors(frame, box) > 10);
					Assert.Equal(area.Bottom - 3, box.Bottom, 1);
					// the layout of the original
					NavigationLayout l = nav.Layout;
					Assert.Equal(28, l.First.Width);
					Assert.Equal(l.First.Left + 32, l.Previous.Left);
					Assert.Equal(l.Last.Right, l.Display.Right);
					Assert.Equal(l.Next.Left, l.Last.Left - 32);
					Assert.Equal(24, l.Slider.Height);
					Assert.Equal(nav.Bounds.Width - 151, l.Slider.Width, 0);
					// the strip shows the displayed page in full and its neighbours; pictures come when the source has them
					Assert.Contains(nav.PageAreas, a => a.Page == 5);
					Assert.Contains(nav.PageAreas, a => a.Page == 4);
					Assert.Contains(nav.PageAreas, a => a.Page == 6);
					Assert.Contains(thumbs.Asked, a => a.Page == 5);
					thumbs.Provide(5);
					h.Tick();
					Assert.True(nav.PageAreas.First(a => a.Page == 5).Bounds.Width < nav.PageAreas.First(a => a.Page == 5).Bounds.Height);
					// the buttons browse
					var browsed = new List<NavigationBrowseEventArgs>();
					nav.Browse += (s, e) => browsed.Add(e);
					void Click(Rect r)
					{
						Point p = new Point(box.X + r.Center.X, box.Y + r.Center.Y);
						h.Window.MouseMove(p);
						h.Window.MouseDown(p, MouseButton.Left);
						h.Window.MouseUp(p, MouseButton.Left);
						h.Tick();
					}
					Click(l.Next);
					Click(l.Previous);
					Click(l.First);
					Click(l.Last);
					Assert.Equal(new[] { (NavigationOrigin.Current, 1), (NavigationOrigin.Current, -1), (NavigationOrigin.Beginning, 0), (NavigationOrigin.End, 0) },
						browsed.Select(b => (b.Origin, b.Offset)).ToArray());
					// the slider: a click at the middle goes to the middle page; a page picture goes to its page
					browsed.Clear();
					Click(new Rect(l.Slider.X + l.Slider.Width / 2, l.Slider.Y + 12, 1, 1));
					Assert.Single(browsed);
					Assert.Equal(NavigationOrigin.Absolute, browsed[0].Origin);
					Assert.InRange(browsed[0].Offset, 5, 7);
					browsed.Clear();
					Rect other = nav.PageAreas.First(a => a.Page == 6).Bounds;
					Click(new Rect(other.Center.X, other.Center.Y, 1, 1));
					Assert.Equal(6, browsed.Single().Offset);
					// the pointer leaves: the overlay slides out in 200 ms
					h.Window.MouseMove(new Point(area.Center.X, area.Top + 50));
					h.Tick();
					Assert.False(nav.IsShown);
					ReaderOverlayClock.Advance(200);
					Assert.Equal(1, nav.SlidePosition, 3);
				}
			});
		}

		[Fact]
		public async Task The_navigation_overlay_follows_its_options_and_the_toggle_and_the_browse_requests_move_the_reader()
		{
			await Ui.Run(async () =>
			{
				ReaderOptions options = ReaderHarness.DefaultOptions();
				options.ShowNavigationOverlay = false;
				using (ReaderHarness h = await Open(options: options, resume: 5))
				{
					CrNavigationOverlay nav = h.Window.Navigation;
					Rect area = h.ReadingArea;
					h.Window.MouseMove(new Point(area.Center.X, area.Bottom - 20));
					h.Tick();
					Assert.False(nav.IsShown); // auto-show is off
					h.Vm.ToggleNavigationOverlayCommand.Execute(null);
					h.Tick();
					Assert.True(nav.IsShown);
					ReaderOverlayClock.Advance(1000);
					Assert.Equal(0, nav.SlidePosition, 3);
					// a click on the page puts a toggled overlay away again
					Point onPage = new Point(area.Center.X, area.Top + 100);
					h.Window.MouseMove(onPage);
					h.Window.MouseDown(onPage, MouseButton.Left);
					h.Window.MouseUp(onPage, MouseButton.Left);
					h.Tick();
					Assert.False(h.Vm.NavigationOverlayVisible);
					Assert.False(nav.IsShown);
				}
				ReaderOptions top = ReaderHarness.DefaultOptions();
				top.NavigationOverlayOnTop = true;
				using (ReaderHarness h = await Open(options: top))
				{
					CrNavigationOverlay nav = h.Window.Navigation;
					Assert.True(nav.OnTop);
					Rect area = h.ReadingArea;
					Assert.Equal(new Rect((area.Width - 500) / 2 - 0.5, 0, 500, 50).Width, nav.HotRect(area.Size).Width);
					Assert.Equal(0, nav.HotRect(area.Size).Top);
				}
			});
		}

		[Fact]
		public async Task Command_wheel_zooms_by_a_tenth_a_double_click_toggles_full_screen_and_the_cursor_hides_in_full_screen()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open())
				{
					Point middle = h.ReadingArea.Center;
					h.Window.MouseMove(middle);
					h.Window.MouseWheel(middle, new Vector(0, 3), RawInputModifiers.Meta);
					h.Tick();
					Assert.Equal(1.3, h.Vm.Zoom, 6);
					h.Window.MouseWheel(middle, new Vector(0, -10), RawInputModifiers.Meta);
					h.Tick();
					Assert.Equal(1.0, h.Vm.Zoom, 6); // never below the fit
					// a double click
					h.Window.MouseDown(middle, MouseButton.Left);
					h.Window.MouseUp(middle, MouseButton.Left);
					h.Window.MouseDown(middle, MouseButton.Left);
					h.Window.MouseUp(middle, MouseButton.Left);
					h.Tick();
					Assert.True(h.Vm.IsFullScreen);
					ReaderView view = h.Window.ReadingArea;
					Assert.True(view.AutoHideCursor);
					Assert.False(view.CursorHidden);
					view.HideCursorNow();
					Assert.True(view.CursorHidden);
					Assert.NotNull(view.Cursor);
					h.Window.MouseMove(new Point(middle.X + 5, middle.Y + 5)); // movement shows it again
					Assert.False(view.CursorHidden);
					// the option off: the cursor stays
					h.Press(Key.F);
					Assert.False(h.Vm.IsFullScreen);
					Assert.False(view.AutoHideCursor);
					h.Vm.Options.HideCursorFullScreen = false;
					h.Press(Key.F);
					Assert.True(h.Vm.IsFullScreen);
					Assert.False(view.AutoHideCursor);
				}
			});
		}

		[Fact]
		public async Task The_page_menu_has_info_my_rating_and_page_layout_and_the_rating_is_written_to_the_book()
		{
			await Ui.Run(async () =>
			{
				var session = new FakeSession(new FakePageDecoder { PageCount = 4 }) { RatingValue = 3 };
				using (ReaderHarness h = await ReaderHarness.Open(session, 800, 600))
				{
					var flyout = (MenuFlyout)h.Window.ReadingArea.ContextFlyout;
					Assert.Equal(3, flyout.Items.Count);
					var items = flyout.Items.OfType<MenuItem>().ToArray();
					Assert.Equal("Info...", items[0].Header);
					Assert.Equal(new KeyGesture(Key.I, KeyModifiers.Meta), items[0].InputGesture);
					Assert.Equal("My Rating", items[1].Header);
					var stars = items[1].Items.OfType<MenuItem>().ToArray();
					Assert.Equal(6, stars.Length);
					Assert.Equal("None", stars[0].Header);
					Assert.Equal("*** (3 Stars)", stars[3].Header);
					Assert.True(stars[3].IsChecked);
					Assert.Equal(new KeyGesture(Key.D3, KeyModifiers.Alt | KeyModifiers.Shift), stars[3].InputGesture);
					stars[5].Command.Execute(null);
					Assert.Equal(5, session.RatingValue);
					Assert.True(stars[5].IsChecked);
					Assert.Equal("Page Layout", items[2].Header);
					var layout = items[2].Items.OfType<MenuItem>().ToArray();
					Assert.Equal(new[] { "Fit All", "Fit Width", "Fit Height", "Single Page", "Two Pages", "Two Pages (adaptive)", "Right to Left" }, layout.Select(m => m.Header).ToArray());
					layout[1].Command.Execute(null);
					Assert.True(layout[1].IsChecked);
					Assert.False(layout[0].IsChecked);
					bool info = false;
					h.Vm.InfoRequested += (s, e) => info = true;
					items[0].Command.Execute(null);
					Assert.True(info);
				}
			});
		}

		[Fact]
		public async Task The_tools_menu_has_about_and_exit_and_q_and_the_keys_of_the_original_do_their_work()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(resume: 5))
				{
					var tools = (MenuFlyout)h.Window.FindControl<CrToolButton>("ToolsButton").Flyout;
					Assert.Equal(new[] { "About...", "Exit" }, tools.Items.OfType<MenuItem>().Select(m => (string)m.Header).ToArray());
					// Left and Right scroll sideways, the arrows never turn the page
					h.Vm.SetFit(cYo.Projects.ComicRack.Engine.Display.ImageFitMode.FitHeight);
					h.Vm.SetZoom(4);
					h.Tick();
					double x = h.Vm.ScrollX;
					Assert.True(h.Vm.HandleKey(Key.Right, KeyModifiers.None));
					Assert.True(h.Vm.ScrollX > x, $"x {x} -> {h.Vm.ScrollX}, max {h.Vm.Arrangement.MaxScrollX}, zoom {h.Vm.Zoom}, fit {h.Vm.Fit}");
					Assert.True(h.Vm.HandleKey(Key.Left, KeyModifiers.None));
					Assert.Equal(x, h.Vm.ScrollX, 3);
					Assert.Equal(5, h.Vm.CurrentPage);
					// Home and End go to the start and the end of the page; Cmd+Home and Cmd+End to the first and last page
					Assert.True(h.Vm.HandleKey(Key.End, KeyModifiers.None));
					Assert.Equal(h.Vm.Arrangement.MaxScroll, h.Vm.ScrollY, 3);
					Assert.True(h.Vm.HandleKey(Key.Home, KeyModifiers.None));
					Assert.Equal(0, h.Vm.ScrollY, 3);
					Assert.Equal(5, h.Vm.CurrentPage);
					// Q quits the application (the window asks the lifetime; without one it closes), About is the Tools item
					bool exit = false, about = false;
					h.Vm.ExitRequested += (s, e) => exit = true;
					h.Vm.AboutRequested += (s, e) => about = true;
					h.Vm.ShowAboutCommand.Execute(null);
					Assert.True(about);
					Assert.True(h.Vm.HandleKey(Key.Q, KeyModifiers.None));
					Assert.True(exit);
				}
			});
		}

		[Fact]
		public async Task The_overlays_and_the_window_follow_the_dark_appearance()
		{
			await Ui.Run(async () =>
			{
				Application app = Application.Current;
				var before = app.RequestedThemeVariant;
				try
				{
					app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
					using (ReaderHarness h = await Open())
					{
						WriteableBitmap frame = h.Capture("reader-dark");
						Color(frame, h.ReadingArea.X + 8, h.ReadingArea.Y + 100, ThemeTokens.Get(ThemeTokens.Names.ReaderBackground, Avalonia.Styling.ThemeVariant.Dark));
					}
				}
				finally
				{
					app.RequestedThemeVariant = before;
				}
			});
		}

		private static void Color(WriteableBitmap frame, double x, double y, Avalonia.Media.Color expected)
		{
			SKColor c = ReaderHarness.ColorAt(frame, (int)x, (int)y);
			Assert.True(PageImages.Near(c, new SKColor(expected.R, expected.G, expected.B), 1), "paper " + c + " expected " + expected);
		}
	}

	/// <summary>The new custom-drawn parts of the reader at 1x and 2x (full-port.md section 2.5).</summary>
	[Trait("Category", "HiDpi")]
	public class ReaderChromeHiDpiTests
	{
		private static async Task<ReaderHarness> Open(double scale, bool zoomed = false)
		{
			var session = new FakeSession(new FakePageDecoder { PageCount = 12, Size = p => (1200, 1800) }) { ResumePage = 3 };
			ReaderHarness h = await ReaderHarness.Open(session, 900, 700);
			Dpi.Apply(h.Window, scale);
			await h.Settled();
			return h;
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_page_outline_is_one_unit_of_whole_device_pixels_and_the_shadow_fades_with_the_scale(double scale)
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(scale))
				{
					Raster frame = Dpi.Capture(h.Window, "reader-outline");
					RectInt page = RectInt.Snapped(h.PageRects().Single(), scale);
					int y = (page.Top + page.Bottom) / 2;
					int unit = Scales.Device(1, scale);
					// the left outline is `unit` black pixels starting at the page edge
					for (int i = 0; i < unit; i++)
					{
						SKColor c = frame[page.Left + i, y];
						Assert.True(c.Red < 30 && c.Green < 30 && c.Blue < 30, $"outline pixel {i} at {scale}x: {c}");
					}
					Assert.False(frame[page.Left + unit, y].Red < 30 && frame[page.Left + unit, y].Green < 30 && frame[page.Left + unit, y].Blue < 30 && unit > 0 && frame[page.Left + unit + 1, y].Red < 30);
					// the shadow: darker than the paper next to the page, paper again beyond its depth
					int depth = Scales.Device(PageFrame.ShadowDepth(h.PageRects().Single()), scale);
					SKColor next = frame[page.Left - 2, y], beyond = frame[page.Left - depth - 4, y];
					Assert.True(next.Red < 0xF5 - 20, "shadow next to the page " + next);
					Assert.True(Raster.Near(beyond, new SKColor(0xF5, 0xF5, 0xF5), 1), "paper beyond the shadow " + beyond);
				}
			});
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_text_overlay_and_the_navigation_overlay_have_the_same_logical_boxes_at_every_scale_and_draw_their_glass(double scale)
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(scale))
				{
					ReaderOverlayClock.Advance(100); // the page info is in
					Rect info = Dpi.Area(h.Window.PageInfo, h.Window);
					Assert.Equal(150, info.Width, 0);
					Assert.InRange(info.Height, 25, 40);
					h.Window.Navigation.SnapShown(true);
					Dpi.Settle(h.Window);
					Rect nav = Dpi.Area(h.Window.Navigation, h.Window);
					Assert.Equal(200, nav.Height, 0);
					Raster frame = Dpi.Capture(h.Window, "reader-overlays");
					RectInt infoBox = RectInt.Snapped(info, scale), navBox = RectInt.Snapped(nav, scale);
					Raster a = frame.Crop(infoBox), b = frame.Crop(navBox);
					Assert.True(b.CountInRow(b.Height - 40, 0, b.Width, c => c.Red < 120) > b.Width / 4, "the black glass of the navigation overlay");
					Assert.True(a.CountInRow(a.Height / 2, 0, a.Width, c => c.Red < 120) > a.Width / 2, "the black glass of the page info");
					h.Window.Navigation.SnapShown(false);
				}
			});
		}
	}
}
