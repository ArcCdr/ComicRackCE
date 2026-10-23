using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Services;
using ComicRack.Mac.Themes;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using SkiaSharp;
using Xunit;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.HiDpi
{
	/// <summary>
	/// P5.R1: the library window shell at 1x and 2x. Constant in logical units: toolbar 25, status bar 24, side panel 252, quick search 130 x 24. What
	/// scales: the frame, and the bottom border of the toolbar and the top border of the status bar, which are exactly <c>scale</c> device pixels
	/// high in the pure token colour (drawn as filled rectangles).
	/// </summary>
	[Trait("Category", "HiDpi")]
	public class MainWindowHiDpiTests
	{
		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_shell_keeps_its_logical_metrics_and_its_one_unit_borders_are_crisp(double scale)
		{
			await InAppearance("Light", () =>
			{
				var service = new HostService(AppOptions.Parse(new[] { "--data-dir", Path.Combine(Path.GetTempPath(), "crmac-never-started") }));
				var vm = new MainViewModel(new NullShell(), service, new FakeDialogs());
				var window = new MainWindow { DataContext = vm, Width = 900, Height = 500 };
				window.Show();
				Dpi.Apply(window, scale);
				Dpi.Settle(window);
				var toolbar = window.FindControl<CrToolBar>("BrowserToolBar");
				var status = window.FindControl<CrStatusBar>("StatusBar");
				Assert.Equal(25, toolbar.Bounds.Height);
				Assert.Equal(24, status.Bounds.Height);
				Assert.Equal(252, window.FindControl<CrSizableContainer>("SidebarPanel").Bounds.Width);
				Assert.Equal(new Size(130, 24), window.FindControl<CrSearchBox>("SearchBox").Bounds.Size);
				Raster frame = Dpi.Capture(window, "main-window-hidpi@" + Scales.Tag(scale));
				// the toolbar's bottom border: its row of device pixels is the border token over the whole width of the browser column
				Point t = Dpi.Origin(toolbar, window);
				SKColor border = Token(ThemeTokens.Names.ToolToolStripBorder, "Light");
				int w = Scales.Device(1, scale);
				int y = Scales.Device(t.Y + 24, scale);
				int x = Scales.Device(t.X + 300, scale);
				Assert.True(Raster.Near(frame[x, y], border, 1), "toolbar border row");
				// the status bar's top border
				Point s = Dpi.Origin(status, window);
				SKColor top = Token(ThemeTokens.Names.ToolStatusStripBorder, "Light");
				Assert.True(Raster.Near(frame[Scales.Device(s.X + 300, scale), Scales.Device(s.Y, scale)], top, 1), "status border row");
				window.Close();
			});
		}

		/// <summary>
		/// P5.9: the write info and update error icons of the status bar (SB.02) are the status bar's 20 x 19 slots, left of the book segment, at the same logical size
		/// at every scale; their sunken frame is one unit of the token colours (<c>ButtonShadow</c> left, <c>ButtonHighlight</c> right) and the picture inside is drawn.
		/// </summary>
		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_activity_slots_of_the_write_queue_keep_their_size_and_a_crisp_sunken_frame(double scale)
		{
			await InAppearance("Light", () =>
			{
				var service = new HostService(AppOptions.Parse(new[] { "--data-dir", Path.Combine(Path.GetTempPath(), "crmac-never-started") }));
				var vm = new MainViewModel(new NullShell(), service, new FakeDialogs());
				var window = new MainWindow { DataContext = vm, Width = 900, Height = 500 };
				window.Show();
				vm.IsWriteInfoActive = true;
				vm.HasUpdateErrors = true;
				Dpi.Apply(window, scale);
				Dpi.Settle(window);
				var write = window.FindControl<CrStatusSlot>("WriteInfoSlot");
				var errors = window.FindControl<CrStatusSlot>("UpdateErrorsSlot");
				Assert.Equal(new Size(20, 19), write.Bounds.Size);
				Assert.Equal(new Size(20, 19), errors.Bounds.Size);
				Assert.True(errors.Bounds.X < write.Bounds.X); // the original's order: export, read info, write info
				Assert.True(write.Bounds.Right <= window.FindControl<TextBlock>("BookSegment").Bounds.X + 0.01);
				Raster frame = Dpi.Capture(window, "status-activity@" + Scales.Tag(scale));
				SKColor shadow = Token(ThemeTokens.Names.ButtonShadow, "Light");
				SKColor lightEdge = Token(ThemeTokens.Names.ButtonHighlight, "Light");
				int w = Scales.Device(1, scale);
				foreach (CrStatusSlot slot in new[] { write, errors })
				{
					Rect area = Dpi.Area(slot, window);
					int x0 = Scales.Device(area.X, scale), y = Scales.Device(area.Y + 9, scale);
					Assert.Equal(w, frame.CountInRow(y, x0, x0 + w, c => Raster.Near(c, shadow, 1)));
					int x1 = Scales.Device(area.Right - 1, scale);
					Assert.Equal(w, frame.CountInRow(y, x1, x1 + w, c => Raster.Near(c, lightEdge, 1)));
					// the picture is in the slot: some pixel inside the frame differs from the status bar's own colour
					int inside = frame.CountInRow(y, x0 + w + Scales.Device(2, scale), x1 - Scales.Device(2, scale), c => !Raster.Near(c, frame[x0 + w + 1, y], 1));
					Assert.True(inside > 0, "no picture drawn in " + slot.Name);
				}
				window.Close();
			});
		}
	}
}
