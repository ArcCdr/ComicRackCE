using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>The three counters of the library tree (<c>LibraryGauges</c>).</summary>
	public enum GaugeKind
	{
		Total,
		Unread,
		New,
	}

	/// <summary>
	/// A counter of the library tree (<c>LibraryTreeSkin.DrawNumberMarker</c>, <c>ComicRack/Controls/LibraryTreeSkin.cs</c>): the number in white
	/// on a pill that fades from alpha 192 at the top to alpha 128 at the bottom of the colour of the counter (<c>GaugeTotal</c> green,
	/// <c>GaugeUnread</c> orange, <c>GaugeNew</c> red), text at 0.75 times the item font, the width of the text plus 4. Rounded on the sides that
	/// have no neighbour (<see cref="RoundLeft"/>, <see cref="RoundRight"/>).
	/// </summary>
	public class CrGaugePill : Border
	{
		public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<CrGaugePill, string>(nameof(Text));

		public static readonly StyledProperty<GaugeKind> KindProperty = AvaloniaProperty.Register<CrGaugePill, GaugeKind>(nameof(Kind));

		private readonly TextBlock label = new TextBlock
		{
			FontSize = ChromeFonts.CounterSize,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(2, 0),
		};

		public CrGaugePill()
		{
			Child = label;
			VerticalAlignment = VerticalAlignment.Center;
			CornerRadius = new CornerRadius(3);
			IsHitTestVisible = false;
			ActualThemeVariantChanged += (sender, e) => ApplyTokens();
			AttachedToVisualTree += (sender, e) => ApplyTokens();
		}

		static CrGaugePill()
		{
			TextProperty.Changed.AddClassHandler<CrGaugePill>((pill, e) => pill.label.Text = pill.Text);
			KindProperty.Changed.AddClassHandler<CrGaugePill>((pill, e) => pill.ApplyTokens());
		}

		public string Text
		{
			get => GetValue(TextProperty);
			set => SetValue(TextProperty, value);
		}

		public GaugeKind Kind
		{
			get => GetValue(KindProperty);
			set => SetValue(KindProperty, value);
		}

		public bool RoundLeft
		{
			get => CornerRadius.TopLeft > 0;
			set => CornerRadius = new CornerRadius(value ? 3 : 0, CornerRadius.TopRight, CornerRadius.BottomRight, value ? 3 : 0);
		}

		public bool RoundRight
		{
			get => CornerRadius.TopRight > 0;
			set => CornerRadius = new CornerRadius(CornerRadius.TopLeft, value ? 3 : 0, value ? 3 : 0, CornerRadius.BottomLeft);
		}

		private static (string back, string text) Tokens(GaugeKind kind)
		{
			switch (kind)
			{
				case GaugeKind.Unread:
					return (ThemeTokens.Names.GaugeUnread, ThemeTokens.Names.GaugeUnreadText);
				case GaugeKind.New:
					return (ThemeTokens.Names.GaugeNew, ThemeTokens.Names.GaugeNewText);
				default:
					return (ThemeTokens.Names.GaugeTotal, ThemeTokens.Names.GaugeTotalText);
			}
		}

		private void ApplyTokens()
		{
			(string back, string text) = Tokens(Kind);
			Color color = ChromePaint.Color(this, back);
			var stops = new[]
			{
				new ImmutableGradientStop(0, new Color(192, color.R, color.G, color.B)),
				new ImmutableGradientStop(1, new Color(128, color.R, color.G, color.B)),
			};
			Background = new ImmutableLinearGradientBrush(stops, 1, null, null, GradientSpreadMethod.Pad, new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative));
			label.Foreground = ChromePaint.Brush(this, text);
		}
	}
}
