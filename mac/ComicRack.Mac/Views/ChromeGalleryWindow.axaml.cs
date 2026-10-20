using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// <c>--show ChromeGallery</c>: today one swatch per theme token of the current appearance (name below, colour from the dictionary
	/// through a resource reference, so it follows the variant). P5.T4 turns it into the gallery of every control and state.
	/// </summary>
	public partial class ChromeGalleryWindow : Window
	{
		public ChromeGalleryWindow()
		{
			InitializeComponent();
			foreach (string name in TokenNames())
			{
				var swatch = new Border
				{
					Width = 140,
					Height = 40,
					BorderThickness = new Thickness(1),
				};
				swatch.Bind(Border.BackgroundProperty, swatch.GetResourceObservable(ThemeTokens.BrushKey(name)));
				swatch.Bind(Border.BorderBrushProperty, swatch.GetResourceObservable(ThemeTokens.BrushKey(ThemeTokens.Names.ControlDark)));
				var caption = new TextBlock { Text = name, FontSize = 10, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, Width = 140 };
				caption.Bind(TextBlock.ForegroundProperty, caption.GetResourceObservable(ThemeTokens.BrushKey(ThemeTokens.Names.WindowText)));
				var cell = new StackPanel { Margin = new Thickness(4), Spacing = 2 };
				cell.Children.Add(swatch);
				cell.Children.Add(caption);
				Swatches.Children.Add(cell);
			}
		}

		/// <summary>The token names that exist in the dictionary of the current appearance, in the order of <see cref="ThemeTokens.Names"/>.</summary>
		private static string[] TokenNames()
		{
			return typeof(ThemeTokens.Names)
				.GetFields(BindingFlags.Public | BindingFlags.Static)
				.Where(f => f.IsLiteral && f.FieldType == typeof(string))
				.Select(f => (string)f.GetRawConstantValue())
				.Where(n => ThemeTokens.TryGetColor(n, out _))
				.ToArray();
		}
	}
}
