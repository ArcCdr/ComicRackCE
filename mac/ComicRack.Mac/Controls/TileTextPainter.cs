using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// <c>SimpleTextRenderer.DrawText</c> for the lines of text beside a picture (<see cref="TileTextLine"/>): every line in the style's size, the first tab stop at the widest text before a tab
	/// plus 8 (not for right aligned text), gaps between lines, nothing drawn once the room is used up. The tile of the Info dialog and the tile mode of the page list draw their text with it.
	/// </summary>
	internal static class TileTextPainter
	{
		public static FormattedText Format(string text, TileTextLine line, IBrush brush, double fontSize, double width, double height, bool far)
		{
			var weight = line.IsBold ? FontWeight.Bold : FontWeight.Normal;
			var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight), fontSize * line.Scale, brush)
			{
				TextAlignment = far ? TextAlignment.Right : TextAlignment.Left,
			};
			if (width > 0)
			{
				formatted.MaxTextWidth = width;
			}
			if (height > 0)
			{
				formatted.MaxTextHeight = height;
			}
			if (line.NoWrap)
			{
				formatted.MaxLineCount = 1;
				formatted.Trimming = TextTrimming.None;
			}
			else
			{
				formatted.Trimming = line.EllipsisWord ? TextTrimming.WordEllipsis : TextTrimming.None;
			}
			return formatted;
		}

		public static void Draw(DrawingContext context, IReadOnlyList<TileTextLine> lines, Rect area, bool far, double fontSize, Func<TileTextLine, IBrush> brushOf)
		{
			if (area.Width <= 0 || area.Height <= 0 || lines.Count == 0)
			{
				return;
			}
			using (context.PushClip(area))
			{
				// the first tab stop: the widest text before a tab, plus 8 (not for the right aligned text)
				double tab = 0;
				if (!far)
				{
					foreach (TileTextLine line in lines.Where(l => !l.Separator && l.Text.Contains('\t')))
					{
						tab = Math.Max(tab, Format(line.Text.Substring(0, line.Text.IndexOf('\t')), line, Brushes.Transparent, fontSize, 0, 0, false).WidthIncludingTrailingWhitespace);
					}
					tab += 8;
				}
				double y = area.Y;
				double remaining = area.Height;
				double gap = 0;
				foreach (TileTextLine line in lines)
				{
					if (line.Separator)
					{
						gap = line.Spacing;
						continue;
					}
					if (gap != 0)
					{
						Space(ref y, ref remaining, gap);
						gap = 0;
					}
					if (string.IsNullOrEmpty(line.Text))
					{
						continue;
					}
					if (remaining <= 0)
					{
						break;
					}
					IBrush brush = brushOf(line);
					string text = far ? line.Text.Replace("\t", "    ") : line.Text;
					double height;
					int at = far ? -1 : text.IndexOf('\t');
					if (at >= 0)
					{
						FormattedText label = Format(text.Substring(0, at), line, brush, fontSize, 0, 0, false);
						FormattedText value = Format(text.Substring(at + 1), line, brush, fontSize, Math.Max(1, area.Width - tab), remaining, false);
						context.DrawText(label, new Point(area.X, y));
						context.DrawText(value, new Point(area.X + tab, y));
						height = Math.Min(remaining, Math.Max(label.Height, value.Height));
					}
					else
					{
						FormattedText formatted = Format(text, line, brush, fontSize, area.Width, remaining, far);
						context.DrawText(formatted, new Point(area.X, y));
						height = Math.Min(remaining, formatted.Height);
					}
					Space(ref y, ref remaining, height);
				}
			}
		}

		private static void Space(ref double y, ref double remaining, double dy)
		{
			if (remaining > 0)
			{
				y += dy;
				remaining = Math.Max(0, remaining - dy);
			}
		}
	}
}
