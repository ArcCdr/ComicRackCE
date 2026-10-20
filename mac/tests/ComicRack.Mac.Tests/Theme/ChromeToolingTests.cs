using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ComicRack.Mac.Themes;
using ComicRack.Mac.Views;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// P5.T3: the hidden options <c>--show</c>, <c>--fixture-library</c> and <c>--list-views</c> that the contact sheets use, the view registry
	/// behind them, and the placeholder gallery window (<c>--show ChromeGallery</c>).
	/// </summary>
	public class ChromeToolingTests
	{
		[Fact]
		public void Show_accepts_registered_views_in_any_case_and_names_the_known_ones_when_it_does_not_know_one()
		{
			Assert.Null(AppOptions.Parse(new string[0]).Show);
			Assert.Equal("ChromeGallery", AppOptions.Parse(new[] { "--show", "ChromeGallery" }).Show);
			Assert.Equal("ChromeGallery", AppOptions.Parse(new[] { "--show=chromegallery" }).Show);
			Assert.Equal("MainWindow", AppOptions.Parse(new[] { "--show", "mainwindow" }).Show);
			AppOptions bad = AppOptions.Parse(new[] { "--show", "NoSuchView" });
			Assert.NotNull(bad.Error);
			Assert.Contains("ChromeGallery", bad.Error);
			Assert.Contains("MainWindow", bad.Error);
		}

		[Fact]
		public void A_fixture_library_needs_an_existing_folder_and_a_private_data_folder()
		{
			using (var fixtures = new TempDir())
			using (var data = new TempDir())
			{
				AppOptions ok = AppOptions.Parse(new[] { "--fixture-library", fixtures.Path, "--data-dir", data.Path });
				Assert.Null(ok.Error);
				Assert.Equal(Path.GetFullPath(fixtures.Path), ok.FixtureLibrary);
				Assert.Null(AppOptions.Parse(new[] { "--fixture-library=" + fixtures.Path, "--smoke-test" }).Error); // a smoke test gets a temporary data folder

				AppOptions shared = AppOptions.Parse(new[] { "--fixture-library", fixtures.Path });
				Assert.NotNull(shared.Error);
				Assert.Contains("--data-dir", shared.Error);

				AppOptions missing = AppOptions.Parse(new[] { "--fixture-library", Path.Combine(fixtures.Path, "nope"), "--data-dir", data.Path });
				Assert.NotNull(missing.Error);
				Assert.Contains("no such folder", missing.Error);
			}
		}

		[Fact]
		public void List_views_is_an_option_and_the_registry_lists_every_view_sorted()
		{
			Assert.True(AppOptions.Parse(new[] { "--list-views" }).ListViews);
			Assert.False(AppOptions.Parse(new string[0]).ListViews);
			Assert.Contains("MainWindow", ViewRegistry.Names);
			Assert.Contains("ChromeGallery", ViewRegistry.Names);
			Assert.Equal(ViewRegistry.Names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), ViewRegistry.Names.ToList());
		}

		[Fact]
		public void The_registry_adds_replaces_and_refuses_unknown_names()
		{
			const string name = "ToolingTestView";
			Assert.False(ViewRegistry.IsKnown(name));
			Assert.Throws<ArgumentException>(() => ViewRegistry.Show(name, null));
			Assert.Throws<ArgumentException>(() => ViewRegistry.Register(" ", () => null));
			Assert.Throws<ArgumentNullException>(() => ViewRegistry.Register(name, null));
			ViewRegistry.Register(name, () => new Window());
			try
			{
				Assert.True(ViewRegistry.IsKnown(name.ToLowerInvariant()));
				Assert.Equal(name, ViewRegistry.Canonical(name.ToUpperInvariant()));
				Assert.Contains(name, ViewRegistry.Names);
				// a registered name is accepted by the option parser
				Assert.Equal(name, AppOptions.Parse(new[] { "--show", name }).Show);
			}
			finally
			{
				Assert.True(ViewRegistry.Unregister(name));
				Assert.False(ViewRegistry.IsKnown(name));
				Assert.False(ViewRegistry.Unregister("MainWindow"));
			}
		}

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task The_gallery_window_shows_one_swatch_per_token_of_the_appearance_in_the_token_colour(string variantName)
		{
			ThemeVariant variant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
			await Ui.Run(() =>
			{
				Application app = Application.Current;
				ThemeVariant before = app.RequestedThemeVariant;
				try
				{
					app.RequestedThemeVariant = variant;
					var window = (ChromeGalleryWindow)ViewRegistry.Show("ChromeGallery", null);
					try
					{
						Assert.Equal("ComicRack Chrome Gallery", window.Title);
						var swatches = window.GetVisualDescendants().OfType<WrapPanel>().Single();
						int expected = typeof(ThemeTokens.Names).GetFields().Count(f => f.IsLiteral && ThemeTokens.TryGetColor((string)f.GetRawConstantValue(), out _));
						Assert.True(expected >= 170, "tokens in the dictionary: " + expected);
						Assert.Equal(expected, swatches.Children.Count);
						// dark has the dark-only tokens, light has not
						bool hasSidePanel = swatches.Children.OfType<StackPanel>().Any(c => c.Children.OfType<TextBlock>().Any(t => t.Text == ThemeTokens.Names.SidePanel));
						Assert.Equal(variantName == "Dark", hasSidePanel);

						// the swatch of Highlight has the token's colour in the frame the window drew
						StackPanel cell = swatches.Children.OfType<StackPanel>().Single(c => c.Children.OfType<TextBlock>().Any(t => t.Text == ThemeTokens.Names.Highlight));
						Border swatch = cell.Children.OfType<Border>().Single();
						var raster = HiDpi.Dpi.Capture(window, "chrome-gallery-" + variantName.ToLowerInvariant());
						Rect area = HiDpi.Dpi.Area(swatch, window);
						Avalonia.Media.Color token = ThemeTokens.Get(ThemeTokens.Names.Highlight, variant);
						SKColor pixel = raster[(int)(area.X + area.Width / 2), (int)(area.Y + area.Height / 2)];
						Assert.True(HiDpi.Raster.Near(pixel, new SKColor(token.R, token.G, token.B)), "swatch pixel " + pixel + " against token " + token);
					}
					finally
					{
						window.Close();
					}
				}
				finally
				{
					app.RequestedThemeVariant = before;
				}
				return Task.CompletedTask;
			});
		}
	}
}
