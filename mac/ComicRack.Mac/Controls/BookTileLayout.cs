using System;
using System.Collections.Generic;
using Avalonia;

namespace ComicRack.Mac.Controls
{
	/// <summary>The weight and size of a line of tile text (<c>ComicTextBuilder.GetCaptionFont</c>, <c>GetNormalFont</c>, <c>GetSmallFont</c>, <c>GetSmallSmallFont</c>).</summary>
	public enum TileTextStyle
	{
		/// <summary>Bold, the tile's size.</summary>
		Caption,

		/// <summary>The tile's size.</summary>
		Normal,

		/// <summary>0.95 of the tile's size.</summary>
		Small,

		/// <summary>0.9 of the tile's size.</summary>
		SmallSmall,
	}

	/// <summary>One line of the text beside a cover (<c>TextLine</c>): the text, its style, and how it fits (<c>StringFormatFlags.NoWrap</c> or word ellipsis), or a gap.</summary>
	public sealed class TileTextLine
	{
		public TileTextLine(string text, TileTextStyle style, bool noWrap = false, bool ellipsisWord = false)
		{
			Text = text ?? string.Empty;
			Style = style;
			NoWrap = noWrap;
			EllipsisWord = ellipsisWord;
		}

		/// <summary>A gap of <paramref name="spacing"/> units before the next line of text (<c>new TextLine(spacing)</c>; the last gap before a line is the one that counts).</summary>
		public TileTextLine(int spacing)
		{
			Separator = true;
			Spacing = spacing;
			Text = string.Empty;
		}

		public string Text { get; }

		public TileTextStyle Style { get; }

		public bool NoWrap { get; }

		public bool EllipsisWord { get; }

		public bool Separator { get; }

		public int Spacing { get; }

		public bool IsBold => Style == TileTextStyle.Caption;

		/// <summary>The size of the line relative to the tile's text size.</summary>
		public double Scale
		{
			get
			{
				switch (Style)
				{
					case TileTextStyle.Small: return 0.95;
					case TileTextStyle.SmallSmall: return 0.9;
					default: return 1;
				}
			}
		}
	}

	/// <summary>Where the parts of a tile go.</summary>
	public readonly struct BookTileGeometry
	{
		public BookTileGeometry(Rect image, Rect text, Rect textTo, Rect icons)
		{
			Image = image;
			Text = text;
			TextArea = textTo;
			Icons = icons;
		}

		/// <summary>The flat cover (or the 3D picture), from the top left corner.</summary>
		public Rect Image { get; }

		/// <summary>The room of the text and the icon strip together.</summary>
		public Rect Text { get; }

		/// <summary>The room of the text alone: <see cref="Text"/> less the icon strip when there are icons.</summary>
		public Rect TextArea { get; }

		/// <summary>The strip along the bottom of <see cref="Text"/> that holds the icons, or empty.</summary>
		public Rect Icons { get; }
	}

	/// <summary>
	/// The arithmetic of <c>ThumbTileRenderer.DrawTile</c> and <c>ThumbRenderer.DrawImageList</c> in whole units, as the original computes it: a flat cover takes half of the
	/// width (as large as fits, from the top left corner) and the text starts 4 units behind it; the 3D picture takes the room from the top left corner as large as fits and
	/// the text lies over the right two thirds; icons sit in a strip of a fifth of the height (16 to 64) along the bottom of the text room, the text room is that much
	/// lower (1.2 times the strip).
	/// </summary>
	public static class BookTileLayout
	{
		/// <summary><c>ThumbRenderer.DefaultThumbnailAspect</c>: the shape of a cover that has not arrived yet.</summary>
		public const double DefaultAspect = 2.0 / 3.0;

		/// <summary><c>ThumbRenderer.GetTagHeight</c>.</summary>
		public static int TagHeight(double tileHeight) => Math.Clamp((int)(tileHeight / 5), 16, 64);

		/// <summary>
		/// The geometry of a tile of the given size. <paramref name="imageWidth"/> and <paramref name="imageHeight"/> are the size of the flat cover (0 while it has not arrived);
		/// <paramref name="boxWidth"/> and <paramref name="boxHeight"/> the size of the 3D picture.
		/// </summary>
		public static BookTileGeometry Compute(double width, double height, double imageWidth, double imageHeight, bool threeD, bool hasIcons, double boxWidth = 0, double boxHeight = 0)
		{
			int w = (int)width, h = (int)height;
			Rect image, text;
			if (threeD)
			{
				text = new Rect(w / 3, 0, w - w / 3, h);
				if (boxWidth > 0 && boxHeight > 0)
				{
					double scale = Math.Min(w / boxWidth, h / boxHeight);
					image = new Rect(0, 0, (int)(boxWidth * scale), (int)(boxHeight * scale));
				}
				else
				{
					image = default;
				}
			}
			else
			{
				Size safe = SafeScaledSize(imageWidth, imageHeight, w / 2, h);
				image = new Rect(0, 0, safe.Width, safe.Height);
				text = new Rect(safe.Width + 4, 0, w - safe.Width - 4, h);
			}
			Rect area = text;
			Rect icons = default;
			if (hasIcons)
			{
				int tag = TagHeight(h);
				area = new Rect(text.X, text.Y, text.Width, Math.Max(0, text.Height - (int)(tag * 1.2f)));
				int top = (int)text.Height - tag;
				icons = new Rect(text.X, text.Y + top, text.Width, Math.Max(0, text.Height - top));
			}
			return new BookTileGeometry(image, text, area, icons);
		}

		/// <summary><c>ThumbRenderer.GetSafeScaledImageSize</c>: the picture fitted into the canvas with its shape kept; a missing picture has the shape of a cover.</summary>
		public static Size SafeScaledSize(double imageWidth, double imageHeight, double canvasWidth, double canvasHeight)
		{
			if (imageWidth > 0 && imageHeight > 0)
			{
				double scale = Math.Min(canvasWidth / imageWidth, canvasHeight / imageHeight);
				return new Size((int)(imageWidth * scale), (int)(imageHeight * scale));
			}
			return new Size((int)(canvasHeight * DefaultAspect), (int)canvasHeight);
		}

		/// <summary>
		/// <c>ThumbRenderer.DrawImageList</c>: the rectangles of the icons in a row, each fitted into the strip, the row shrunk to the strip's width when it is too long,
		/// aligned to the left or the right and in the middle of the strip's height, consecutive icons a tenth of their width apart (the original passes -0.1 as the overlap).
		/// </summary>
		public static Rect[] IconRects(Rect strip, IReadOnlyList<Size> sizes, bool alignRight, double overlap = -0.1)
		{
			if (sizes.Count == 0)
			{
				return Array.Empty<Rect>();
			}
			int[] widths = new int[sizes.Count], heights = new int[sizes.Count];
			for (int i = 0; i < sizes.Count; i++)
			{
				double scale = Math.Min(strip.Width / sizes[i].Width, strip.Height / sizes[i].Height);
				widths[i] = (int)(sizes[i].Width * scale);
				heights[i] = (int)(sizes[i].Height * scale);
			}
			int Step(int w) => w - (int)(overlap * w);
			int tallest = 0;
			foreach (int hh in heights)
			{
				tallest = Math.Max(tallest, hh);
			}
			int total = widths[widths.Length - 1];
			for (int i = 0; i < widths.Length - 1; i++)
			{
				total += Step(widths[i]);
			}
			double shrink = Math.Min(1.0, strip.Width / total);
			int rowWidth = (int)(shrink * total), rowHeight = (int)(shrink * tallest);
			double left = alignRight ? strip.X + strip.Width - rowWidth : strip.X;
			double top = strip.Y + strip.Height - rowHeight; // bottom aligned
			var rects = new Rect[sizes.Count];
			double x = left;
			for (int i = 0; i < sizes.Count; i++)
			{
				int iw = (int)(widths[i] * shrink), ih = (int)(heights[i] * shrink);
				rects[i] = new Rect(x, top + (rowHeight - ih) / 2, iw, ih);
				x += Step(iw);
			}
			return rects;
		}
	}
}
