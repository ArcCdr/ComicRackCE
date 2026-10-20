using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Themes;
using SkiaSharp;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>What the chrome tests share: a window with a control in the top left corner in a chosen appearance, token colours as pixels, blending.</summary>
	internal static class ChromeTestSupport
	{
		public static readonly ThemeVariant[] Variants = { ThemeVariant.Light, ThemeVariant.Dark };

		public static ThemeVariant Variant(string name) => name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

		/// <summary>Runs on the UI thread with the application showing the appearance, and restores it.</summary>
		public static Task InAppearance(string variantName, Action work)
		{
			return Ui.Run(() =>
			{
				Application app = Application.Current;
				ThemeVariant before = app.RequestedThemeVariant;
				try
				{
					app.RequestedThemeVariant = Variant(variantName);
					work();
				}
				finally
				{
					app.RequestedThemeVariant = before;
				}
				return Task.CompletedTask;
			});
		}

		/// <summary>A shown window whose surface is the dialog colour and that holds the control at its top left.</summary>
		public static Window ShowIn(Control control, double width, double height, double scale = 1.0)
		{
			control.HorizontalAlignment = HorizontalAlignment.Left;
			control.VerticalAlignment = VerticalAlignment.Top;
			var surface = new Border { Child = control };
			surface.Bind(Border.BackgroundProperty, surface.GetResourceObservable(ThemeTokens.BrushKey(ThemeTokens.Names.Control)));
			var window = new Window { Width = width, Height = height, Content = surface };
			window.Show();
			if (scale != 1.0)
			{
				Dpi.Apply(window, scale);
			}
			Dpi.Settle(window);
			return window;
		}

		public static SKColor Token(string name, string variantName, int alpha = 255)
		{
			Avalonia.Media.Color c = ThemeTokens.Get(name, Variant(variantName));
			return new SKColor(c.R, c.G, c.B, (byte)alpha);
		}

		/// <summary>The colour of <paramref name="top"/> drawn with <paramref name="alpha"/> (0 to 255) over <paramref name="under"/>.</summary>
		public static SKColor Over(SKColor top, int alpha, SKColor under)
		{
			double a = alpha / 255.0;
			return new SKColor((byte)Math.Round(top.Red * a + under.Red * (1 - a)), (byte)Math.Round(top.Green * a + under.Green * (1 - a)), (byte)Math.Round(top.Blue * a + under.Blue * (1 - a)), 255);
		}

		public static SKColor Lerp(SKColor a, SKColor b, double t)
		{
			return new SKColor((byte)Math.Round(a.Red + (b.Red - a.Red) * t), (byte)Math.Round(a.Green + (b.Green - a.Green) * t), (byte)Math.Round(a.Blue + (b.Blue - a.Blue) * t), 255);
		}

		/// <summary>Pixel of a point given in units (device pixel = Device(units, scale)).</summary>
		public static SKColor At(Raster frame, double x, double y, double scale = 1.0)
		{
			return frame[Scales.Device(x, scale), Scales.Device(y, scale)];
		}

		public static string Describe(SKColor c) => "#" + c.Red.ToString("X2") + c.Green.ToString("X2") + c.Blue.ToString("X2");
	}
}
