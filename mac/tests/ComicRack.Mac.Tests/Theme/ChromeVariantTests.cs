using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Themes;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// RV5: one element of the chrome flips from Original to Native through <see cref="ChromeVariants"/> and nothing else changes: the same view tree,
	/// the same view model, the same controls, only the control theme of that element's controls (and so its pixels). The proof element is the status bar.
	/// </summary>
	public class ChromeVariantTests
	{
		private static (MainWindow window, MainViewModel vm) NewWindow()
		{
			var service = new HostService(AppOptions.Parse(new[] { "--data-dir", Path.Combine(Path.GetTempPath(), "crmac-never-started") }));
			var vm = new MainViewModel(new NullShell(), service, new FakeDialogs());
			return (new MainWindow { DataContext = vm }, vm);
		}

		/// <summary>Type and name of every visual of the window, in tree order: the shape of the view tree.</summary>
		private static List<string> Shape(Window window)
		{
			return window.GetVisualDescendants().Select(v => v.GetType().FullName + ":" + ((v as Control)?.Name ?? string.Empty)).ToList();
		}

		[Fact]
		public void Every_element_has_a_default_and_the_default_is_the_original_look()
		{
			foreach (ChromeElement element in Enum.GetValues(typeof(ChromeElement)))
			{
				Assert.True(ChromeVariants.Defaults.ContainsKey(element), element + " has no default");
				Assert.Equal(ChromeVariant.Original, ChromeVariants.Defaults[element]);
			}
			// RV5b: every element has a Native variant
			Assert.Equal(Enum.GetValues(typeof(ChromeElement)).Cast<ChromeElement>().OrderBy(e => e).ToArray(), ChromeVariants.NativeAvailable.OrderBy(e => e).ToArray());
		}

		[Fact]
		public async Task A_specification_sets_elements_and_wrong_ones_are_refused_with_the_valid_names()
		{
			await Ui.Run(() =>
			{
				try
				{
					Assert.Null(ChromeVariants.Configure("statusbar=native"));
					Assert.Equal(ChromeVariant.Native, ChromeVariants.Get(ChromeElement.StatusBar));
					Assert.Null(ChromeVariants.Configure("StatusBar=Original; Toolbar=Original"));
					Assert.Equal(ChromeVariant.Original, ChromeVariants.Get(ChromeElement.StatusBar));
					Assert.Null(ChromeVariants.Configure("native"));
					foreach (ChromeElement element in Enum.GetValues(typeof(ChromeElement)))
					{
						Assert.Equal(ChromeVariant.Native, ChromeVariants.Get(element));
						Assert.True(ChromeVariants.IsNative(element));
					}
					Assert.Null(ChromeVariants.Configure("original"));
					foreach (ChromeElement element in Enum.GetValues(typeof(ChromeElement)))
					{
						Assert.Equal(ChromeVariant.Original, ChromeVariants.Get(element));
						Assert.False(ChromeVariants.IsNative(element));
					}
					Assert.False(ChromeVariants.AnyNative);
					Assert.Null(ChromeVariants.Configure(null));
					Assert.Null(ChromeVariants.Configure("  "));
					foreach (string bad in new[] { "Bogus=Native", "StatusBar", "StatusBar=Pink", "StatusBar=Native=Original", "99=1" })
					{
						string error = ChromeVariants.Configure(bad);
						Assert.NotNull(error);
						Assert.Contains("StatusBar", error);
						Assert.Contains("Native", error);
					}
					Assert.Equal(ChromeVariant.Original, ChromeVariants.Get(ChromeElement.StatusBar));
				}
				finally
				{
					ChromeVariants.Reset();
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Every_element_can_be_set_to_native_and_back_and_reset_restores_the_defaults()
		{
			await Ui.Run(() =>
			{
				try
				{
					foreach (ChromeElement element in Enum.GetValues(typeof(ChromeElement)))
					{
						Assert.True(ChromeVariants.Set(element, ChromeVariant.Native), element + " has no Native variant");
						Assert.Equal(ChromeVariant.Native, ChromeVariants.Get(element));
						Assert.True(ChromeVariants.Set(element, ChromeVariant.Original));
						Assert.Equal(ChromeVariant.Original, ChromeVariants.Get(element));
					}
					Assert.True(ChromeVariants.Set(ChromeElement.StatusBar, ChromeVariant.Native));
					Assert.Equal(ChromeVariant.Native, ChromeVariants.Get(ChromeElement.StatusBar));
					ChromeVariants.Reset();
					Assert.Equal(ChromeVariant.Original, ChromeVariants.Get(ChromeElement.StatusBar));
					Assert.False(ChromeVariants.AnyNative);
				}
				finally
				{
					ChromeVariants.Reset();
				}
				return Task.CompletedTask;
			});
		}

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task Flipping_the_status_bar_changes_its_theme_and_pixels_and_nothing_in_the_view_tree(string variantName)
		{
			await ChromeTestSupport.InAppearance(variantName, () =>
			{
				var (window, vm) = NewWindow();
				var changed = new List<(ChromeElement, ChromeVariant)>();
				Action<ChromeElement, ChromeVariant> onChanged = (e, v) => changed.Add((e, v));
				ChromeVariants.Changed += onChanged;
				try
				{
					window.Show();
					Dpi.Settle(window);
					var status = window.FindControl<CrStatusBar>("StatusBar");
					var toolbar = window.FindControl<CrToolBar>("BrowserToolBar");
					var slot = window.FindControl<CrStatusSlot>("SizeSlot");
					Assert.Same(ChromeVariants.FindTheme(typeof(CrStatusBar), ChromeVariant.Original), status.Theme);
					Assert.NotNull(status.Theme);
					Assert.False(status.IsNative);
					ControlTheme toolbarTheme = toolbar.Theme;
					List<string> shape = Shape(window);
					Rect area = Dpi.Area(status, window);
					int x = (int)(area.X + area.Width / 2), y = (int)(area.Y + area.Height / 2);
					Raster before = Dpi.Capture(window, "chrome-variant-original-" + variantName.ToLowerInvariant());
					SKColor originalPixel = before[x, y];
					SKColor originalTop = before[x, (int)area.Y];

					Assert.True(ChromeVariants.Set(ChromeElement.StatusBar, ChromeVariant.Native));
					Dpi.Settle(window);
					Raster native = Dpi.Capture(window, "chrome-variant-native-" + variantName.ToLowerInvariant());

					// the same controls and the same tree: only the theme (and what it paints) differs
					Assert.Same(status, window.FindControl<CrStatusBar>("StatusBar"));
					Assert.Same(vm, window.DataContext);
					Assert.Equal(shape, Shape(window));
					Assert.Same(ChromeVariants.FindTheme(typeof(CrStatusBar), ChromeVariant.Native), status.Theme);
					Assert.NotSame(ChromeVariants.FindTheme(typeof(CrStatusBar), ChromeVariant.Original), status.Theme);
					Assert.True(status.IsNative);
					Assert.Same(ChromeVariants.FindTheme(typeof(CrStatusSlot), ChromeVariant.Native), slot.Theme);
					Assert.Same(toolbarTheme, toolbar.Theme); // another element is untouched
					Assert.Equal(new[] { (ChromeElement.StatusBar, ChromeVariant.Native) }, changed.ToArray());
					Assert.Equal(25, Dpi.Area(toolbar, window).Height);
					Assert.Equal(24, status.Bounds.Height); // the layout is the same, so the flip needs no view change

					// the native bar is flat in the stock theme's chrome colour
					Assert.True(window.TryFindResource("SystemControlBackgroundChromeMediumLowBrush", window.ActualThemeVariant, out object brush), "the stock brush exists");
					Color expected = ((ISolidColorBrush)brush).Color;
					SKColor nativePixel = native[x, y];
					Assert.True(Raster.Near(nativePixel, new SKColor(expected.R, expected.G, expected.B)), "native bar pixel " + nativePixel + " against " + expected);
					Assert.False(Raster.Near(nativePixel, originalPixel, 1) && Raster.Near(native[x, (int)area.Y], originalTop, 1), "the native bar does not look like the original");
					// above the bar nothing moved: the toolbar rows of both frames are equal
					Rect bar = Dpi.Area(toolbar, window);
					Assert.True(before.Crop(new RectInt(0, (int)bar.Y, before.Width, (int)bar.Bottom)).Compare(native.Crop(new RectInt(0, (int)bar.Y, native.Width, (int)bar.Bottom))).Max <= 2, "the toolbar changed");

					// and back: the first frame again, pixel for pixel
					Assert.True(ChromeVariants.Set(ChromeElement.StatusBar, ChromeVariant.Original));
					Dpi.Settle(window);
					Raster again = Dpi.Capture(window, "chrome-variant-restored-" + variantName.ToLowerInvariant());
					Assert.Equal(0, before.Compare(again, 0).Max);
					Assert.Same(ChromeVariants.FindTheme(typeof(CrStatusBar), ChromeVariant.Original), status.Theme);
					Assert.False(status.IsNative);
					window.Close();
				}
				finally
				{
					ChromeVariants.Changed -= onChanged;
					ChromeVariants.Reset();
				}
			});
		}

		[Fact]
		public async Task A_control_made_after_the_flip_has_the_native_theme_and_a_native_slot_is_flat()
		{
			await Ui.Run(() =>
			{
				try
				{
					var before = new CrStatusSlot();
					var window = new Window { Content = before };
					window.Show();
					Dpi.Settle(window);
					Assert.True(before.Sunken);
					ChromeVariants.Configure("StatusBar=Native");
					Dpi.Settle(window);
					var after = new CrStatusBar();
					Assert.True(after.IsNative);
					Assert.Same(ChromeVariants.FindTheme(typeof(CrStatusBar), ChromeVariant.Native), after.Theme);
					// an existing slot follows the flip too; its Sunken value now comes from the theme
					Assert.False(before.Sunken);
					ChromeVariants.Configure("StatusBar=Original");
					Dpi.Settle(window);
					Assert.True(before.Sunken);
					window.Close();
				}
				finally
				{
					ChromeVariants.Reset();
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_closed_window_does_not_stay_alive_through_the_registry()
		{
			await Ui.Run(() =>
			{
				WeakReference weak = Make();
				for (int i = 0; i < 4 && weak.IsAlive; i++)
				{
					GC.Collect();
					GC.WaitForPendingFinalizers();
				}
				Assert.False(weak.IsAlive, "a status bar that nothing references is still held");
				return Task.CompletedTask;
			});

			static WeakReference Make() => new WeakReference(new CrStatusBar());
		}
	}
}
