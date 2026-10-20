using System;
using Avalonia;

namespace ComicRack.Mac.Controls
{
	/// <summary>How the rating of a thumbnail is shown (<c>ThumbnailRatingMode</c>, chosen from <c>NumericRatingThumbnails</c> and <c>RatingStarsBelowThumbnails</c>).</summary>
	public enum ThumbnailRatingMode
	{
		/// <summary>Numeric tags in the bottom right corner of the cover, the user's next to the community's (the default).</summary>
		Tags,

		/// <summary>Five stars over the left edge of the cover.</summary>
		StarsOverlay,

		/// <summary>Five stars in a strip under the cover.</summary>
		StarsBelow,
	}

	/// <summary>
	/// The numbers of the thumbnail renderers (<c>ThumbRenderer</c>, <c>ThumbIconRenderer</c>, <c>RatingRenderer</c>, <c>CoverViewItem.MeasureItem</c>)
	/// in device independent units, chrome-guidelines section 4: item border 4, shadow 4, three caption lines, caption font scale, overlay heights.
	/// </summary>
	public static class ThumbnailMetrics
	{
		/// <summary><c>ThumbnailViewItem.border</c>: the space around the cover inside an item.</summary>
		public const double ItemBorder = 4;

		/// <summary><c>ThumbRenderer.DrawShadow(..., 4)</c>: the blur depth, on the right and bottom side.</summary>
		public const double ShadowDepth = 4;

		/// <summary><c>CoverViewItem.labelLines</c>.</summary>
		public const int LabelLines = 3;

		/// <summary><c>ThumbRenderer.DefaultThumbnailAspect</c>.</summary>
		public const double CoverAspect = 2.0 / 3.0;

		/// <summary>Space between the cover and the rating strip under it (<c>ratingRenderer.Y = thumbnailBounds.Bottom + 4</c>).</summary>
		public const double StripGap = 4;

		/// <summary>The side of a rating star picture (<c>StarYellow.png</c>, <c>StarBlue.png</c>).</summary>
		public const double StarSize = 32;

		/// <summary>A star strip shows five stars (<c>RatingRenderer.MaximumRating</c>).</summary>
		public const int StarCount = 5;

		/// <summary><c>FadeInTime</c> of <c>ThumbnailViewItem</c>.</summary>
		public const int FadeMilliseconds = 300;

		private static double itemFontHeight;

		/// <summary>The height of one line of the item font (<c>CurrentFontHeight</c> of the item view), measured once.</summary>
		public static double ItemFontHeight => itemFontHeight > 0 ? itemFontHeight : (itemFontHeight = Math.Ceiling(ChromeFonts.LineHeight(ChromeFonts.ItemSize)));

		/// <summary><c>(defaultSize.Height / 192).Clamp(0.7, 1)</c>.</summary>
		public static double LabelFontScale(double coverHeight) => Math.Clamp(coverHeight / 192.0, 0.7, 1.0);

		/// <summary>The caption font: the item font times <see cref="LabelFontScale"/>.</summary>
		public static double LabelFontSize(double coverHeight) => ChromeFonts.ItemSize * LabelFontScale(coverHeight);

		/// <summary><c>(int)(LabelLines * (FontHeight * scale + 2))</c>.</summary>
		public static int LabelHeight(double coverHeight) => (int)(LabelLines * (ItemFontHeight * LabelFontScale(coverHeight) + 2));

		/// <summary>An item: the cover (2:3) plus the border on both sides. The grid is uniform, so a cover of another proportion sits inside this box.</summary>
		public static double CellWidth(double coverHeight) => Math.Round(coverHeight * CoverAspect) + 2 * ItemBorder;

		/// <summary>An item: the cover, the border on both sides and the caption.</summary>
		public static double CellHeight(double coverHeight) => Math.Round(coverHeight) + 2 * ItemBorder + LabelHeight(coverHeight);

		/// <summary><c>ThumbRenderer.GetOverlaysHeight</c>: a tenth of the cover, 16 to 32 (state icons, star strip, ribbon band).</summary>
		public static int OverlaysHeight(double thumbnailHeight) => (int)Math.Clamp((int)thumbnailHeight / 10, 16, 32);

		/// <summary><c>ThumbRenderer.GetTagHeight</c>: a fifth of the cover, 16 to 64 (the numeric rating tags).</summary>
		public static int TagHeight(double thumbnailHeight) => (int)Math.Clamp((int)thumbnailHeight / 5, 16, 64);
	}

	/// <summary>The rectangles of one thumbnail item, in the coordinates of the item.</summary>
	public readonly struct ThumbnailGeometry
	{
		public ThumbnailGeometry(Rect item, Rect inner, Rect area, Rect box, Rect picture, Rect label, Rect strip)
		{
			Item = item;
			Inner = inner;
			Area = area;
			Box = box;
			Picture = picture;
			Label = label;
			Strip = strip;
		}

		/// <summary>The whole item.</summary>
		public Rect Item { get; }

		/// <summary>The item inside its border.</summary>
		public Rect Inner { get; }

		/// <summary>The part of the inner rectangle above the caption: the cover, its shadow and, with stars below, the strip.</summary>
		public Rect Area { get; }

		/// <summary>Where the cover picture goes (the area without the shadow, and without the strip): the box of the <see cref="CoverView"/>.</summary>
		public Rect Box { get; }

		/// <summary>The cover itself inside the box (aspect kept, or the whole box when all covers have the same size).</summary>
		public Rect Picture { get; }

		/// <summary>The caption, inside the inner rectangle's bottom part, two units in from each side.</summary>
		public Rect Label { get; }

		/// <summary>The star strip under the cover, or an empty rectangle.</summary>
		public Rect Strip { get; }
	}

	/// <summary>The arithmetic of <c>ThumbIconRenderer.Draw</c> and <c>ThumbRenderer.DrawThumbnail</c>.</summary>
	public static class ThumbnailLayout
	{
		/// <summary>
		/// <paramref name="cell"/> is the size of the item, <paramref name="coverHeight"/> the item size of the browser (the cover height the caption
		/// scales with), <paramref name="image"/> the size of the picture in pixels or default for none (a cover is then 2:3).
		/// </summary>
		public static ThumbnailGeometry Compute(Size cell, double coverHeight, bool starsBelow, bool sameSize, Size image)
		{
			double border = ThumbnailMetrics.ItemBorder;
			var item = new Rect(0, 0, cell.Width, cell.Height);
			var inner = new Rect(border, border, Math.Max(0, cell.Width - 2 * border), Math.Max(0, cell.Height - 2 * border));
			double labelHeight = Math.Min(inner.Height, ThumbnailMetrics.LabelHeight(coverHeight));
			var area = new Rect(inner.X, inner.Y, inner.Width, inner.Height - labelHeight);
			Rect cover = area;
			Rect strip = default;
			if (starsBelow)
			{
				// RatingRenderer bounds (0, 0, width - 4, overlays - 4) hold five stars of 32 units; the strip is the largest that fits, whole units high.
				double overlays = ThumbnailMetrics.OverlaysHeight(area.Height);
				double boundsWidth = Math.Max(0, area.Width - 4), boundsHeight = Math.Max(0, overlays - 4);
				double scale = Math.Min(boundsWidth / (ThumbnailMetrics.StarCount * ThumbnailMetrics.StarSize), boundsHeight / ThumbnailMetrics.StarSize);
				double stripHeight = Math.Floor(ThumbnailMetrics.StarSize * scale);
				double stripWidth = ThumbnailMetrics.StarCount * stripHeight;
				cover = new Rect(area.X, area.Y, area.Width, Math.Max(0, area.Height - (stripHeight + ThumbnailMetrics.StripGap)));
				double top = cover.Y + Math.Max(0, cover.Height - ThumbnailMetrics.ShadowDepth) + ThumbnailMetrics.StripGap;
				strip = new Rect(area.X + 2 + Math.Floor((boundsWidth - stripWidth) / 2), top, stripWidth, stripHeight);
			}
			var box = new Rect(cover.X, cover.Y, Math.Max(0, cover.Width - ThumbnailMetrics.ShadowDepth), Math.Max(0, cover.Height - ThumbnailMetrics.ShadowDepth));
			// The picture of a cover that is not loaded yet (and of a "same size" browser) fills the whole box.
			Rect picture = sameSize || image.Width <= 0 || image.Height <= 0 ? new Rect(0, 0, box.Width, box.Height) : FitInBox(box, image.Width, image.Height);
			picture = new Rect(box.X + picture.X, box.Y + picture.Y, picture.Width, picture.Height);
			var label = new Rect(inner.X + 2, area.Bottom + 2, Math.Max(0, inner.Width - 4), Math.Max(0, labelHeight - 4));
			return new ThumbnailGeometry(item, inner, area, box, picture, label, strip);
		}

		private static Rect FitInBox(Rect box, double width, double height)
		{
			if (width <= 0 || height <= 0 || box.Width <= 0 || box.Height <= 0)
			{
				return default;
			}
			double scale = Math.Min(box.Width / width, box.Height / height);
			double w = width * scale, h = height * scale;
			return new Rect((box.Width - w) / 2, (box.Height - h) / 2, w, h);
		}
	}
}
