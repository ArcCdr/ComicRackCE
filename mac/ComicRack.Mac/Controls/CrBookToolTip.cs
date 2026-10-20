using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// The tool tip of a book (<c>ComicBrowserControl.toolTip_Draw</c>): 360 x 120, the system tool tip colours, the cover at the left with the frame
	/// and shadow of a thumbnail and the text lines of <see cref="BookToolTipData"/> at its right (the first two in bold). Give it its content with
	/// <see cref="Show"/>; it lets go of the cover when it leaves the screen.
	/// </summary>
	public class CrBookToolTip : Border, IChromeElement
	{
		/// <summary><c>toolTip_Popup</c>: <c>e.ToolTipSize = new Size(360, 120)</c>.</summary>
		public const double CardWidth = 360, CardHeight = 120;

		private readonly CoverView cover = new CoverView { SnapToUnits = true };
		private readonly StackPanel text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Top };
		private readonly Grid layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
		private readonly Border coverFrame = new Border { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 10, 0), BorderThickness = new Thickness(1) };

		public CrBookToolTip()
		{
			Width = CardWidth;
			Height = CardHeight;
			Padding = new Thickness(10);
			BorderThickness = new Thickness(1);
			coverFrame.Child = cover;
			layout.Children.Add(coverFrame);
			Grid.SetColumn(text, 1);
			layout.Children.Add(text);
			Child = layout;
			ActualThemeVariantChanged += (sender, e) => ApplyTokens();
			AttachedToVisualTree += (sender, e) => ApplyTokens();
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.ItemView;

		/// <summary>The picture in the card (for tests).</summary>
		public CoverView Cover => cover;

		/// <summary>The lines of text in the card (for tests).</summary>
		public Avalonia.Controls.Controls Lines => text.Children;

		/// <summary>Fills the card. The picture may be null (the cover is not loaded); the card takes its own reference.</summary>
		public void Show(BookToolTipData data, CoverImage picture)
		{
			cover.SetImage(picture);
			double height = CardHeight - 2 * 10 - 2;
			double width = picture == null ? height * ThumbnailMetrics.CoverAspect : Math.Min(CardWidth / 2, height * picture.Width / Math.Max(1, picture.Height));
			cover.Width = Math.Round(width);
			cover.Height = height;
			text.Children.Clear();
			string[] lines = data?.Lines ?? Array.Empty<string>();
			for (int i = 0; i < lines.Length; i++)
			{
				text.Children.Add(new TextBlock
				{
					Text = lines[i],
					FontSize = ChromeFonts.ItemSize * (i < 2 ? 1.2 : 1.0),
					FontWeight = i < 2 ? FontWeight.Bold : FontWeight.Normal,
					TextWrapping = TextWrapping.Wrap,
					TextTrimming = TextTrimming.CharacterEllipsis,
					MaxLines = i < 2 ? 2 : 3,
					Foreground = ChromePaint.Brush(this, ThemeTokens.Names.InfoText),
				});
			}
			ApplyTokens();
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			cover.SetImage(null);
		}

		private void ApplyTokens()
		{
			Background = ChromePaint.Brush(this, ThemeTokens.Names.Info);
			BorderBrush = ChromePaint.Brush(this, ThemeTokens.Names.WindowFrame);
			coverFrame.BorderBrush = ChromePaint.ResourceBrush(this, "Cr.Thumb.BorderBrush");
			foreach (Control line in text.Children)
			{
				if (line is TextBlock block)
				{
					block.Foreground = ChromePaint.Brush(this, ThemeTokens.Names.InfoText);
				}
			}
		}
	}
}
