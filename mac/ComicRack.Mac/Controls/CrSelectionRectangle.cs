using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// The rubber band of the item view (<c>ItemView.OnPaint</c>): while the user drags a rectangle over the empty area, the rectangle (two units
	/// smaller on every side) is filled with the <c>Highlight</c> colour at alpha 128 and framed by a one unit <c>Highlight</c> line, which, as GDI+
	/// draws a rectangle, is one unit wider and higher than the fill. The control only draws; the view says where the rectangle is
	/// (<see cref="Rectangle"/>, in the coordinates of this control, which covers the item view) and what it selects.
	/// </summary>
	public class CrSelectionRectangle : Control, IChromeElement
	{
		/// <summary>The alpha of the fill (<c>Color.FromArgb(128, SystemColors.Highlight)</c>).</summary>
		public const byte FillAlpha = 128;

		/// <summary>The rectangle is drawn this much smaller on every side (<c>Rectangle.Inflate(-2, -2)</c>).</summary>
		public const double Inset = 2;

		public static readonly StyledProperty<Rect> RectangleProperty = AvaloniaProperty.Register<CrSelectionRectangle, Rect>(nameof(Rectangle));

		static CrSelectionRectangle()
		{
			AffectsRender<CrSelectionRectangle>(RectangleProperty);
		}

		public CrSelectionRectangle()
		{
			IsHitTestVisible = false;
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.ItemView;

		/// <summary>The band as the view tracks it, in the coordinates of this control; an empty rectangle draws nothing.</summary>
		public Rect Rectangle
		{
			get => GetValue(RectangleProperty);
			set => SetValue(RectangleProperty, value);
		}

		/// <summary>The rectangle that is filled (the band deflated by <see cref="Inset"/>, not below zero) and the one that is framed (one unit wider and higher).</summary>
		public static void Shapes(Rect band, out Rect fill, out Rect frame)
		{
			double width = Math.Max(0, band.Width - 2 * Inset), height = Math.Max(0, band.Height - 2 * Inset);
			fill = new Rect(band.X + Inset, band.Y + Inset, width, height);
			frame = new Rect(fill.X, fill.Y, width + 1, height + 1);
		}

		public override void Render(DrawingContext context)
		{
			base.Render(context);
			Rect band = Rectangle;
			if (band.Width <= 0 && band.Height <= 0)
			{
				return;
			}
			Shapes(band, out Rect fill, out Rect frame);
			if (fill.Width > 0 && fill.Height > 0)
			{
				context.DrawRectangle(ChromePaint.Brush(this, ThemeTokens.Names.Highlight, FillAlpha), null, fill);
			}
			ChromePaint.Frame(context, ChromePaint.Brush(this, ThemeTokens.Names.Highlight), frame);
		}
	}
}
