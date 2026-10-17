using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Display;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>
	/// Frames of the two-page reader on the headless Skia platform (with CRMAC_FRAMES_DIR set they are saved as PNG): pages carry their
	/// number, size and a colour of their own, and the tests read pixels at the corners of the rectangles the layout reports.
	/// </summary>
	public class ReaderSpreadWindowTests : IDisposable
	{
		private static readonly SKColor Paper = new SKColor(0x1E, 0x1E, 0x1E);
		private readonly long liveBefore;

		public ReaderSpreadWindowTests()
		{
			Gc.Flush();
			liveBefore = PixelBlock.LiveCount;
		}

		public void Dispose()
		{
			Gc.Flush();
			Assert.Equal(liveBefore, PixelBlock.LiveCount);
		}

		private static (int, int) Sizes(int page) => page == 4 ? (2400, 1200) : (1200, 1800);

		private static Task<ReaderHarness> Open(PageLayoutMode mode, int resume = 0, bool rightToLeft = false, int pages = 9, double width = 900, double height = 700, Func<int, (int, int)> size = null)
		{
			ReaderOptions options = ReaderHarness.DefaultOptions();
			options.Mode = mode;
			var decoder = new FakePageDecoder { PageCount = pages, Size = size ?? Sizes, DrawNumbers = true };
			var session = new FakeSession(decoder) { ResumePage = resume, RightToLeft = rightToLeft };
			return ReaderHarness.Open(session, width, height, options);
		}

		private static SKColor InsideTopLeft(WriteableBitmap frame, Rect r) => ReaderHarness.ColorAt(frame, (int)r.X + 12, (int)r.Y + 12);

		private static SKColor InsideBottomRight(WriteableBitmap frame, Rect r) => ReaderHarness.ColorAt(frame, (int)r.Right - 13, (int)r.Bottom - 13);

		private static void AssertPaper(WriteableBitmap frame, double x, double y, string where)
		{
			Assert.True(PageImages.Near(ReaderHarness.ColorAt(frame, (int)x, (int)y), Paper, 1), "paper at " + where);
		}

		private static void AssertPage(WriteableBitmap frame, Rect r, int page, string where)
		{
			SKColor expected = FakePageDecoder.ColorFor(page);
			// the corners are inside the page's black outline, which is thin; read just inside it
			Assert.True(PageImages.Near(InsideTopLeft(frame, r), expected, 3), where + " top left of page " + (page + 1));
			Assert.True(PageImages.Near(InsideBottomRight(frame, r), expected, 3), where + " bottom right of page " + (page + 1));
		}

		[Fact]
		public async Task Frame_single_page()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(PageLayoutMode.Single, resume: 1))
				{
					WriteableBitmap frame = h.Capture("m23b-01-single");
					Rect[] r = h.PageRects();
					Assert.Single(r);
					AssertPage(frame, r[0], 1, "single");
					Assert.Equal("2 / 9", h.Window.FindControl<TextBlock>("PageLabel").Text);
					Assert.Equal("One page", h.Window.FindControl<TextBlock>("ModeLabel").Text);
				}
			});
		}

		[Fact]
		public async Task Frame_spread_with_the_cover_alone_on_the_right_half()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(PageLayoutMode.Double))
				{
					WriteableBitmap frame = h.Capture("m23b-02-spread-cover-alone");
					Rect[] r = h.PageRects();
					Assert.Single(r);
					Rect area = h.ReadingArea;
					Assert.True(r[0].X >= area.X + area.Width / 2 - 1, "the cover starts at the middle of the spread");
					AssertPage(frame, r[0], 0, "cover");
					AssertPaper(frame, area.X + 20, area.Y + area.Height / 2, "left half");
					Assert.Equal("1 / 9", h.Window.FindControl<TextBlock>("PageLabel").Text);
					Assert.Equal("Two pages", h.Window.FindControl<TextBlock>("ModeLabel").Text);
				}
			});
		}

		[Fact]
		public async Task Frame_spread_of_two_pages_next_to_each_other()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(PageLayoutMode.Double))
				{
					h.Press(Key.Right);
					await h.Settled();
					WriteableBitmap frame = h.Capture("m23b-03-spread-2-3");
					Rect[] r = h.PageRects();
					Assert.Equal(2, r.Length);
					Assert.True(r[0].X < r[1].X);
					Assert.Equal(r[0].Right, r[1].X, 1);
					AssertPage(frame, r[0], 1, "left");
					AssertPage(frame, r[1], 2, "right");
					Assert.Equal("2-3 / 9", h.Window.FindControl<TextBlock>("PageLabel").Text);
				}
			});
		}

		[Fact]
		public async Task Frame_spread_with_a_double_page_in_the_middle_of_the_book()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(PageLayoutMode.Double, resume: 3))
				{
					// page 4 (index 3) is alone because page 5 is a double page
					WriteableBitmap frame = h.Capture("m23b-04-before-double");
					Rect[] r = h.PageRects();
					Assert.Single(r);
					Assert.Equal(new[] { 3 }, h.Vm.Scene.Pages.Select(p => p.Index).ToArray());
					AssertPage(frame, r[0], 3, "lone page");
					Assert.True(r[0].X >= h.ReadingArea.X + h.ReadingArea.Width / 2 - 1);
					h.Press(Key.Right);
					await h.Settled();
					frame = h.Capture("m23b-05-double-page");
					r = h.PageRects();
					Assert.Single(r);
					Rect area = h.ReadingArea;
					Assert.Equal(area.Width, r[0].Width, 1); // the double page fills the width
					Assert.True(r[0].Width > r[0].Height);
					AssertPage(frame, r[0], 4, "double page");
					h.Press(Key.Right);
					await h.Settled();
					frame = h.Capture("m23b-06-after-double");
					r = h.PageRects();
					Assert.Equal(2, r.Length);
					AssertPage(frame, r[0], 5, "left");
					AssertPage(frame, r[1], 6, "right");
					// and back again by the same spreads
					h.Press(Key.Left);
					await h.Settled();
					Assert.Equal(new[] { 4 }, h.Vm.Scene.Pages.Select(p => p.Index).ToArray());
					h.Press(Key.Left);
					await h.Settled();
					Assert.Equal(new[] { 3 }, h.Vm.Scene.Pages.Select(p => p.Index).ToArray());
				}
			});
		}

		[Fact]
		public async Task Frame_right_to_left_spread_and_cover()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(PageLayoutMode.Double, rightToLeft: true))
				{
					WriteableBitmap frame = h.Capture("m23b-07-rtl-cover");
					Rect[] r = h.PageRects();
					Assert.Single(r);
					Assert.True(r[0].X >= h.ReadingArea.X + h.ReadingArea.Width / 2 - 1, "the cover stays on the right");
					AssertPage(frame, r[0], 0, "cover");
					Assert.Equal("Two pages · Right to left", h.Window.FindControl<TextBlock>("ModeLabel").Text);
					h.Press(Key.Left); // forward
					await h.Settled();
					frame = h.Capture("m23b-08-rtl-spread");
					r = h.PageRects();
					Assert.Equal(2, r.Length);
					Assert.True(r[0].X > r[1].X, "the first page is on the right");
					AssertPage(frame, r[0], 1, "right");
					AssertPage(frame, r[1], 2, "left");
					h.Press(Key.Left);
					await h.Settled();
					Assert.Equal(new[] { 3, 4 }.Take(1).Concat(new[] { 3 }).Distinct().ToArray(), new[] { h.Vm.CurrentPage });
					h.Press(Key.Right); // back
					await h.Settled();
					Assert.Equal(1, h.Vm.CurrentPage);
				}
			});
		}

		[Fact]
		public async Task Frame_spread_for_each_fit_mode()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(PageLayoutMode.Double, resume: 1, width: 900, height: 500))
				{
					Rect area = h.ReadingArea;
					WriteableBitmap frame = h.Capture("m23b-09-spread-best-fit");
					Rect[] best = h.PageRects();
					Assert.True(best[1].Right - best[0].X <= area.Width + 1 && best[0].Height <= area.Height + 1);
					AssertPage(frame, best[0], 1, "best fit left");
					AssertPage(frame, best[1], 2, "best fit right");

					h.Press(Key.D3, KeyModifiers.Meta);
					frame = h.Capture("m23b-10-spread-fit-width");
					Rect[] width = h.PageRects();
					Assert.Equal(area.Width, width[1].Right - width[0].X, 1);
					Assert.True(width[0].Height > area.Height, "the spread is taller than the area and scrolls");
					Assert.True(h.Vm.CanScrollDown);

					h.Window.Width = 560;
					h.Window.Height = 700;
					h.Tick();
					area = h.ReadingArea;
					h.Press(Key.D5, KeyModifiers.Meta);
					frame = h.Capture("m23b-11-spread-fit-height");
					Rect[] height = h.PageRects();
					Assert.Equal(area.Height, height[0].Height, 1);
					Assert.True(height[1].Right - height[0].X > area.Width, "the spread is wider than the area and pans sideways");
					AssertPage(frame, new Rect(height[0].X, height[0].Y, Math.Min(height[0].Width, area.Right - height[0].X), height[0].Height), 1, "fit height left page (clipped)");
				}
			});
		}

		[Fact]
		public async Task Frame_zoomed_and_panned_by_keys_wheel_and_drag()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(PageLayoutMode.Double, resume: 1))
				{
					Rect area = h.ReadingArea;
					h.Press(Key.OemPlus, KeyModifiers.Meta);
					h.Press(Key.OemPlus, KeyModifiers.Meta);
					h.Press(Key.OemPlus, KeyModifiers.Meta);
					Assert.Equal(Math.Pow(1.25, 3), h.Vm.Zoom, 9);
					Assert.Equal("Two pages · 195 %", h.Window.FindControl<TextBlock>("ModeLabel").Text);
					h.Capture("m23b-12-zoomed-centre");
					// the wheel pans: first to the top left corner of the content ...
					h.Window.MouseWheel(new Point(300, 300), new Vector(1000, 1000));
					h.Tick();
					Assert.Equal(0, h.Vm.ScrollX);
					Assert.Equal(0, h.Vm.ScrollY);
					WriteableBitmap corner = h.Capture("m23b-13-zoomed-top-left");
					Rect[] r = h.PageRects();
					Assert.Equal(area.X, r[0].X, 1);
					Assert.Equal(area.Y, r[0].Y, 1);
					AssertPage(corner, new Rect(r[0].X, r[0].Y, Math.Min(r[0].Width, area.Width), Math.Min(r[0].Height, area.Height)), 1, "top left");
					// ... then 4 notches down and 2 right
					h.Window.MouseWheel(new Point(300, 300), new Vector(-2, -4));
					h.Tick();
					Assert.Equal(100, h.Vm.ScrollX, 3);
					Assert.Equal(200, h.Vm.ScrollY, 3);
					// a drag moves the content with the pointer, within its edges
					Point from = new Point(area.X + 300, area.Y + 300);
					h.Window.MouseDown(from, MouseButton.Left);
					h.Window.MouseMove(new Point(from.X - 60, from.Y - 40));
					h.Window.MouseMove(new Point(from.X - 120, from.Y - 90));
					h.Window.MouseUp(new Point(from.X - 120, from.Y - 90), MouseButton.Left);
					h.Tick();
					Assert.Equal(220, h.Vm.ScrollX, 3);
					Assert.Equal(290, h.Vm.ScrollY, 3);
					h.Capture("m23b-14-zoomed-panned");
					// dragging far past the edge stops at the edge
					h.Window.MouseDown(from, MouseButton.Left);
					h.Window.MouseMove(new Point(from.X - 3000, from.Y - 3000));
					h.Window.MouseUp(new Point(from.X - 3000, from.Y - 3000), MouseButton.Left);
					h.Tick();
					Assert.Equal(h.Vm.Arrangement.MaxScrollX, h.Vm.ScrollX, 3);
					Assert.Equal(h.Vm.Arrangement.MaxScroll, h.Vm.ScrollY, 3);
					WriteableBitmap far = h.Capture("m23b-15-zoomed-bottom-right");
					Rect[] rf = h.PageRects();
					Assert.Equal(area.Right, rf[1].Right, 1);
					Assert.Equal(area.Bottom, rf[1].Bottom, 1);
					AssertPage(far, new Rect(Math.Max(rf[1].X, area.X), Math.Max(rf[1].Y, area.Y), Math.Min(rf[1].Right, area.Right) - Math.Max(rf[1].X, area.X), Math.Min(rf[1].Bottom, area.Bottom) - Math.Max(rf[1].Y, area.Y)), 2, "bottom right");
					// Cmd+0 resets
					h.Press(Key.D0, KeyModifiers.Meta);
					Assert.Equal(1.0, h.Vm.Zoom);
					Assert.Equal(0, h.Vm.ScrollX);
					Assert.False(h.Vm.CanPan);
				}
			});
		}

		[Fact]
		public async Task A_drag_does_nothing_when_the_page_fits_and_the_cursor_follows_the_state()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(PageLayoutMode.Single))
				{
					Assert.False(h.Vm.CanPan);
					Point from = new Point(300, 300);
					h.Window.MouseDown(from, MouseButton.Left);
					h.Window.MouseMove(new Point(200, 200));
					h.Window.MouseUp(new Point(200, 200), MouseButton.Left);
					h.Tick();
					Assert.Equal(0, h.Vm.ScrollX);
					Assert.Equal(0, h.Vm.ScrollY);
					Assert.Null(h.Window.ReadingArea.Cursor);
					h.Vm.SetZoom(2);
					h.Window.MouseDown(from, MouseButton.Left);
					Assert.NotNull(h.Window.ReadingArea.Cursor);
					h.Window.MouseUp(from, MouseButton.Left);
					h.Tick();
					Assert.Null(h.Window.ReadingArea.Cursor);
				}
			});
		}

		[Fact]
		public async Task A_magnify_gesture_zooms_about_the_pointer()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(PageLayoutMode.Double, resume: 1))
				{
					ReaderView view = h.Window.ReadingArea;
					var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
					var args = new PointerDeltaEventArgs(InputElement.PointerTouchPadGestureMagnifyEvent, view, pointer, h.Window, new Point(450, 300), 0,
						new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None, new Vector(0.5, 0));
					view.RaiseEvent(args);
					Assert.True(args.Handled);
					Assert.Equal(1.5, h.Vm.Zoom, 9);
					args = new PointerDeltaEventArgs(InputElement.PointerTouchPadGestureMagnifyEvent, view, pointer, h.Window, new Point(450, 300), 8,
						new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None, new Vector(-0.2, 0));
					view.RaiseEvent(args);
					Assert.Equal(1.2, h.Vm.Zoom, 9);
					h.Capture("m23b-16-pinched");
				}
			});
		}

		[Fact]
		public async Task Full_screen_follows_ctrl_cmd_f_hides_the_status_strip_and_escape_leaves_it()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(PageLayoutMode.Single))
				{
					Border strip = h.Window.FindControl<Border>("StatusStrip");
					Assert.True(strip.IsVisible);
					Assert.NotEqual(WindowState.FullScreen, h.Window.WindowState);
					h.Press(Key.F, KeyModifiers.Meta | KeyModifiers.Control);
					Assert.True(h.Vm.IsFullScreen);
					Assert.Equal(WindowState.FullScreen, h.Window.WindowState);
					Assert.False(strip.IsVisible);
					h.Capture("m23b-17-full-screen");
					bool closed = false;
					h.Window.Closed += (s, e) => closed = true;
					h.Press(Key.Escape);
					Assert.False(h.Vm.IsFullScreen);
					Assert.NotEqual(WindowState.FullScreen, h.Window.WindowState);
					Assert.True(strip.IsVisible);
					Assert.False(closed);
					// the user (or macOS) leaves full screen: the view model follows
					h.Window.WindowState = WindowState.FullScreen;
					h.Tick();
					Assert.True(h.Vm.IsFullScreen);
					h.Window.WindowState = WindowState.Normal;
					h.Tick();
					Assert.False(h.Vm.IsFullScreen);
				}
			});
		}

		[Fact]
		public async Task Frame_adaptive_layout_shows_the_cover_as_a_single_page()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(PageLayoutMode.DoubleAdaptive))
				{
					WriteableBitmap frame = h.Capture("m23b-18-adaptive-cover");
					Rect[] r = h.PageRects();
					Rect area = h.ReadingArea;
					Assert.Single(r);
					Assert.Equal(area.X + area.Width / 2, r[0].X + r[0].Width / 2, 1);
					AssertPage(frame, r[0], 0, "cover");
				}
			});
		}

		[Fact]
		public async Task The_view_menu_has_the_layout_zoom_direction_and_full_screen_items_with_their_shortcuts()
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(PageLayoutMode.Single))
				{
					NativeMenu menu = NativeMenu.GetMenu(h.Window);
					NativeMenuItem view = menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "View");
					var items = view.Menu.Items.OfType<NativeMenuItem>().Where(i => !(i is NativeMenuItemSeparator)).ToDictionary(i => i.Header, i => i);
					Assert.Equal(new KeyGesture(Key.OemPlus, KeyModifiers.Meta), items["Zoom In"].Gesture);
					Assert.Equal(new KeyGesture(Key.OemMinus, KeyModifiers.Meta), items["Zoom Out"].Gesture);
					Assert.Equal(new KeyGesture(Key.D0, KeyModifiers.Meta), items["Actual Fit"].Gesture);
					Assert.Equal(new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Control), items["Enter Full Screen"].Gesture);
					Assert.True(items["One Page"].IsChecked);
					Assert.False(items["Two Pages"].IsChecked);
					items["Two Pages"].Command.Execute(null);
					await h.Settled();
					Assert.True(items["Two Pages"].IsChecked);
					Assert.False(items["One Page"].IsChecked);
					Assert.False(items["Read Right to Left"].IsChecked);
					items["Read Right to Left"].Command.Execute(null);
					Assert.True(items["Read Right to Left"].IsChecked);
					items["Enter Full Screen"].Command.Execute(null);
					Assert.Equal("Exit Full Screen", items["Enter Full Screen"].Header);
					items["Zoom In"].Command.Execute(null);
					Assert.Equal(1.25, h.Vm.Zoom, 9);
				}
			});
		}
	}
}
