using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Diagnostics;
using ComicRack.Mac.Tests.Diagnostics;
using ComicRack.Mac.Services;
using ComicRack.Mac.Themes;
using ComicRack.Mac.Tests.Browser;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Tests.Reader;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// RV5: the default look must not change while the chrome moves into its own layer. This test renders a fixed set of views (library window,
	/// grid with a selected cell, chrome gallery, About, Import, a reader page and the reader notice) in light and dark on the headless platform.
	/// <c>CRMAC_CHROME_SNAPSHOT_DIR</c> saves the frames as PNG; <c>CRMAC_CHROME_BASELINE_DIR</c> compares every frame with the PNG of the same
	/// name in that folder (frames taken before a change) and fails above the tolerance. In the fast gate, without either variable, the test only
	/// checks that every frame has content. In the full gate (<c>mac/tools/verify.sh --full</c> sets <c>CRMAC_FULL_GATE=1</c>) the comparison is
	/// required: the baseline is <c>CRMAC_CHROME_BASELINE_DIR</c> or <see cref="DefaultBaselineFolder"/>, and a missing folder or frame fails the test
	/// with the command that takes it, so the "default look unchanged" guarantee cannot silently go away.
	/// </summary>
	public class ChromeSnapshotTests
	{
		public const string SnapshotVariable = "CRMAC_CHROME_SNAPSHOT_DIR";
		public const string BaselineVariable = "CRMAC_CHROME_BASELINE_DIR";
		public const string FullGateVariable = "CRMAC_FULL_GATE";

		/// <summary>Where the full gate looks for the baseline frames when <see cref="BaselineVariable"/> is not set (git ignored: a baseline belongs to the machine that took it).</summary>
		public static string DefaultBaselineFolder => Path.Combine(OriginalColors.RepoRoot, "mac", "artifacts", "chrome", "baseline");

		/// <summary>The folder the comparison reads, or null when there is none to read (fast gate without the variable).</summary>
		public static string BaselineFolder(string variableValue, bool fullGate)
		{
			if (!string.IsNullOrEmpty(variableValue))
			{
				return variableValue;
			}
			return fullGate ? DefaultBaselineFolder : null;
		}

		/// <summary>The sentence a missing baseline frame gets in the full gate.</summary>
		public static string HowToTakeTheBaseline(string folder)
		{
			return "no baseline frame in '" + folder + "' (take them from a tree whose look is right: CRMAC_CHROME_SNAPSHOT_DIR='" + folder
				+ "' dotnet test mac/tests/ComicRack.Mac.Tests --filter ChromeSnapshotTests)";
		}

		/// <summary>The fraction of pixels that may differ by more than the threshold, and the largest mean difference.</summary>
		private const double MaxBad = 0.0005;
		private const double MaxMean = 0.05;

		private static void Settle(Window window) => Dpi.Settle(window);

		private static Raster Frame(Window window)
		{
			Settle(window);
			return Raster.From(window.CaptureRenderedFrame());
		}

		/// <summary>Renders every view in the appearance; the key is the file name without the extension.</summary>
		private static async Task<Dictionary<string, Raster>> Render(string variantName)
		{
			var frames = new Dictionary<string, Raster>();
			string suffix = variantName.ToLowerInvariant();
			await Ui.Run(async () =>
			{
				Application app = Application.Current;
				ThemeVariant before = app.RequestedThemeVariant;
				app.RequestedThemeVariant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
				try
				{
					// the library window as it starts
					var service = new HostService(AppOptions.Parse(new[] { "--data-dir", Path.Combine(Path.GetTempPath(), "crmac-never-started") }));
					var vm = new MainViewModel(new NullShell(), service, new FakeDialogs());
					var main = new MainWindow { DataContext = vm };
					main.Show();
					frames["main-" + suffix] = Frame(main);
					main.Close();

					// the grid with 24 synthetic books, the third one selected
					using (var h = new GridHarness())
					{
						await h.Show(new BookCatalog(SyntheticItems.Make(24)));
						h.Model.SelectedIndex = 2;
						h.Layout();
						frames["grid-" + suffix] = Frame(h.Window);
					}

					var gallery = (ChromeGalleryWindow)ViewRegistry.Show("ChromeGallery", null);
					gallery.Height = 1200;
					frames["gallery-" + suffix] = Frame(gallery);
					gallery.Close();

					var about = new AboutWindow();
					about.Show();
					frames["about-" + suffix] = Frame(about);
					about.Close();

					var import = new ImportWindow();
					import.Show();
					import.ViewModel.ErrorText = "Choose the ComicDb.xml file of the Windows installation.";
					frames["import-" + suffix] = Frame(import);
					import.Close();

					var session = new FakeSession(new FakePageDecoder { PageCount = 5, Size = p => (200, 300) }) { ResumePage = 2 };
					using (ReaderHarness h = await ReaderHarness.Open(session, 700, 500))
					{
						frames["reader-" + suffix] = Frame(h.Window);
					}
					using (var h = new ReaderHarness(new ReaderViewModel(new FakeSession(new FakePageDecoder { PageCount = 5 }, openImmediately: false), ReaderHarness.DefaultOptions()), 500, 300))
					{
						h.Vm.Notice = "Opening...";
						h.Tick();
						frames["reader-notice-" + suffix] = Frame(h.Window);
					}
				}
				finally
				{
					app.RequestedThemeVariant = before;
				}
			});
			return frames;
		}

		/// <summary>The values that were literals in the views and are theme entries now (<c>CrParts.axaml</c>, <c>CrReaderLook.axaml</c>), read back from live controls.</summary>
		[Fact]
		public async Task The_surfaces_that_moved_into_the_theme_keep_the_values_they_had_in_their_views()
		{
			await Ui.Run(() =>
			{
				Avalonia.Media.Color Of(object brush) => ((Avalonia.Media.ISolidColorBrush)brush).Color;
				var controller = new PerfOverlayController(new PerfSources(), new FakeTicker(), StopwatchClock.Now, m => { });
				var overlay = new PerfOverlay(controller, StopwatchClock.Now);
				var cell = new BookCell();
				var cover = new CoverView();
				var panel = new StackPanel { Children = { overlay, cell, cover } };
				var window = new Window { Width = 300, Height = 300, Content = panel };
				window.Show();
				Settle(window);

				// performance overlay: translucent block, 10 x 6 padding, radius 6, 12 unit Menlo text in pale green
				Assert.Equal(Avalonia.Media.Color.FromArgb(0xE0, 0x10, 0x10, 0x10), Of(overlay.Background));
				Assert.Equal(new Thickness(10, 6), overlay.Padding);
				Assert.Equal(new CornerRadius(6), overlay.CornerRadius);
				var text = (TextBlock)overlay.Child;
				Assert.Equal(12, text.FontSize);
				Assert.StartsWith("Menlo", text.FontFamily.Name);
				Assert.Equal(Avalonia.Media.Color.FromRgb(0xE8, 0xF4, 0xE8), Of(text.Foreground));

				// thumbnail item: the colours of the original's renderers come from CrThumbnailLook.axaml, the caption and the selection from the tokens
				Assert.Equal(Avalonia.Media.Color.FromArgb(0x54, 0, 0, 0), Of(Controls.ChromePaint.ResourceBrush(cell, "Cr.Thumb.ShadowBrush")));
				Assert.Equal(Avalonia.Media.Colors.White, Of(Controls.ChromePaint.ResourceBrush(cell, "Cr.Thumb.MissingBackBrush")));
				Assert.Equal(Avalonia.Media.Color.FromArgb(0xC0, 0, 0, 0), Of(Controls.ChromePaint.ResourceBrush(cell, "Cr.Thumb.BadgeBackBrush")));
				Assert.False(cell.IsSelectedForTest);
				cell.SetSelected(true);
				Settle(window);
				Assert.True(cell.IsSelectedForTest);
				cell.SetSelected(false);
				Settle(window);

				// the reader: the paper is the token of the original (WhiteSmoke), the glass text, the icons on the glass, the shadow and the outline of the
				// pages are the colours the original's renderers write as literals (CrReaderLook.axaml)
				Assert.Equal(Avalonia.Media.Color.FromRgb(0xF5, 0xF5, 0xF5), Of(Controls.ChromePaint.Brush(cover, ThemeTokens.Names.ReaderBackground)));
				Assert.Equal(Avalonia.Media.Colors.White, Of(Controls.ChromePaint.ResourceBrush(cover, "Cr.Reader.OverlayTextBrush")));
				Assert.Equal(Avalonia.Media.Colors.Black, Of(Controls.ChromePaint.ResourceBrush(cover, "Cr.Reader.GlassIconBrush")));
				Assert.Equal(Avalonia.Media.Colors.Black, Of(Controls.ChromePaint.ResourceBrush(cover, "Cr.Reader.PageFrameBrush")));
				Assert.Equal(Avalonia.Media.Color.FromArgb(0x99, 0, 0, 0), Of(Controls.ChromePaint.ResourceBrush(cover, "Cr.Reader.PageShadowBrush")));
				Assert.Null(Controls.ChromePaint.ResourceBrush(cover, "Cr.Reader.NoSuchBrush"));
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task The_views_render_and_match_the_baseline_frames_when_one_is_given(string variantName)
		{
			Dictionary<string, Raster> frames = await Render(variantName);
			Assert.Equal(7, frames.Count);
			string save = Environment.GetEnvironmentVariable(SnapshotVariable);
			bool fullGate = Environment.GetEnvironmentVariable(FullGateVariable) == "1";
			string baseline = BaselineFolder(Environment.GetEnvironmentVariable(BaselineVariable), fullGate);
			var problems = new List<string>();
			if (fullGate)
			{
				// one sentence for a missing baseline, not one per frame
				Assert.True(Directory.Exists(baseline) && Directory.EnumerateFiles(baseline, "*.png").Any(), "the full gate compares the default look with its baseline: " + HowToTakeTheBaseline(baseline));
			}
			foreach (KeyValuePair<string, Raster> frame in frames)
			{
				Assert.True(frame.Value.Width > 100 && frame.Value.Height > 100, frame.Key + " is empty");
				if (!string.IsNullOrEmpty(save))
				{
					Directory.CreateDirectory(save);
					frame.Value.Save(Path.Combine(save, frame.Key + ".png"));
				}
				if (!string.IsNullOrEmpty(baseline))
				{
					string path = Path.Combine(baseline, frame.Key + ".png");
					if (!File.Exists(path))
					{
						problems.Add(frame.Key + ": " + (fullGate ? HowToTakeTheBaseline(baseline) : "no baseline frame"));
						continue;
					}
					Raster expected;
					using (SKImage image = SKImage.FromEncodedData(path))
					{
						expected = Raster.From(image);
					}
					if (expected.Width != frame.Value.Width || expected.Height != frame.Value.Height)
					{
						problems.Add(frame.Key + ": size " + frame.Value.Width + " x " + frame.Value.Height + " against " + expected.Width + " x " + expected.Height);
						continue;
					}
					Difference difference = frame.Value.Compare(expected, 4);
					if (difference.Bad > MaxBad || difference.Mean > MaxMean)
					{
						problems.Add(frame.Key + ": " + difference);
					}
				}
			}
			Assert.True(problems.Count == 0, "the default look changed: " + string.Join("; ", problems));
		}

		[Fact]
		public void The_full_gate_requires_a_baseline_and_the_fast_gate_does_not()
		{
			Assert.Null(BaselineFolder(null, fullGate: false));
			Assert.Null(BaselineFolder("", fullGate: false));
			Assert.Equal(DefaultBaselineFolder, BaselineFolder(null, fullGate: true));
			Assert.Equal(DefaultBaselineFolder, BaselineFolder("", fullGate: true));
			Assert.Equal("/somewhere/else", BaselineFolder("/somewhere/else", fullGate: true));
			Assert.Equal("/somewhere/else", BaselineFolder("/somewhere/else", fullGate: false));
			string text = HowToTakeTheBaseline("/x/baseline");
			Assert.Contains("CRMAC_CHROME_SNAPSHOT_DIR='/x/baseline'", text);
			Assert.EndsWith(Path.Combine("artifacts", "chrome", "baseline"), DefaultBaselineFolder);
			// verify.sh --full is what sets the variable
			string script = File.ReadAllText(Path.Combine(OriginalColors.RepoRoot, "mac", "tools", "verify.sh"));
			Assert.Contains("export " + FullGateVariable + "=1", script);
		}
	}
}
