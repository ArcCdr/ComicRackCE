using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// Two pieces of the original's <c>ThumbRenderer</c> that the grid's cover cell and the page list both draw over a picture: the page number badge and the bookmark flag. Their colours are the
	/// <c>Cr.Thumb.*</c> entries of <c>CrThumbnailLook.axaml</c>, read through the owning control.
	/// </summary>
	internal static class ThumbnailAdornments
	{
		private static readonly Typeface Arial = new Typeface("Arial");

		/// <summary>ThumbRenderer.DrawPageNumber: a black badge (alpha 192) with a white number, Arial 7 pt, at least 20 wide, radius 3, in the top right corner of the picture.</summary>
		public static void DrawPageNumber(DrawingContext context, Control owner, Rect t, int number)
		{
			var text = new FormattedText(number.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Arial, 7 * 4 / 3.0, ChromePaint.ResourceBrush(owner, "Cr.Thumb.BadgeTextBrush"));
			Rect inside = t.Deflate(2);
			double w = Math.Max(Math.Ceiling(text.Width), 20) + 4, h = Math.Ceiling(text.Height) + 4;
			var badge = new Rect(inside.Right - w, inside.Y, w, h);
			var pen = new Pen(ChromePaint.ResourceBrush(owner, "Cr.Thumb.BadgeFrameBrush"), 1);
			context.DrawRectangle(ChromePaint.ResourceBrush(owner, "Cr.Thumb.BadgeBackBrush"), pen, badge, 3, 3);
			context.DrawText(text, new Point(badge.X + (badge.Width - text.Width) / 2, badge.Y + (badge.Height - text.Height) / 2));
		}

		/// <summary>
		/// ThumbRenderer.DrawBookmarkV: a flag with a notch, at most 16 x 8, sliding down the right edge with the page; a black shadow one unit below and to the right of it, a black outline, a fill
		/// that fades to half of each channel.
		/// </summary>
		public static void DrawBookmark(DrawingContext context, Control owner, Rect t, double percent, int index)
		{
			IBrush colour = ChromePaint.ResourceBrush(owner, "Cr.Thumb.Bookmark" + (index % 4) + "Brush");
			if (!(colour is ISolidColorBrush solid))
			{
				return;
			}
			double height = Math.Min(8, t.Height - 2), width = Math.Min(16, t.Width - 2);
			double top = Math.Truncate(t.Y + (t.Height - height) * percent);
			var rect = new Rect(t.Right - width, top, width, height);
			Color c1 = solid.Color, c2 = new Color(255, (byte)(c1.R / 2), (byte)(c1.G / 2), (byte)(c1.B / 2)); // half of each channel
			StreamGeometry Flag(double dx, double dy)
			{
				var g = new StreamGeometry();
				using (StreamGeometryContext s = g.Open())
				{
					s.BeginFigure(new Point(rect.Left + dx, rect.Top + dy), true);
					s.LineTo(new Point(rect.Right + dx, rect.Top + dy));
					s.LineTo(new Point(rect.Right + dx, rect.Bottom + dy));
					s.LineTo(new Point(rect.Left + dx, rect.Bottom + dy));
					s.LineTo(new Point(rect.Left + height + dx, rect.Top + height / 2 + dy));
					s.EndFigure(true);
				}
				return g;
			}
			context.DrawGeometry(ChromePaint.ResourceBrush(owner, "Cr.Thumb.BookmarkShadowBrush"), null, Flag(1, 1));
			var fill = new ImmutableLinearGradientBrush(new[] { new ImmutableGradientStop(0, c1), new ImmutableGradientStop(1, c2) }, 1, null, null, GradientSpreadMethod.Pad,
				new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative));
			context.DrawGeometry(fill, new Pen(ChromePaint.ResourceBrush(owner, "Cr.Thumb.BookmarkFrameBrush"), 1), Flag(0, 0));
		}
	}
}
