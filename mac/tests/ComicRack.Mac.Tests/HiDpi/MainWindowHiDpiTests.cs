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
	}
}
