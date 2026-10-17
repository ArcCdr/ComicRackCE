using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ComicRack.Mac.Browser
{
	/// <summary>
	/// The picture area of a cell. <see cref="Render"/> runs only when the picture or the size changes (Avalonia keeps the
	/// recorded frame otherwise), so scrolling does not redraw covers on the UI thread; the pixels are drawn by the render thread
	/// through <see cref="CoverDrawOperation"/>.
	/// </summary>
	public sealed class CoverView : Control
	{
		private static readonly IBrush PlaceholderFill = new SolidColorBrush(Color.FromArgb(0x26, 0x80, 0x80, 0x80));
		private static readonly IPen PlaceholderPen = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)), 1);
		private CoverImage image;

		/// <summary>True when a picture is set.</summary>
		public bool HasImage => image != null;

		/// <summary>The current picture (for tests); do not keep it without <see cref="CoverImage.Retain"/>.</summary>
		public CoverImage Image => image;

		/// <summary>Replaces the picture; takes its own reference. Null shows the empty tile.</summary>
		public void SetImage(CoverImage value)
		{
			if (ReferenceEquals(value, image))
			{
				return;
			}
			CoverImage old = image;
			image = value?.Retain();
			old?.Release();
			InvalidateVisual();
		}

		/// <summary>The rectangle a picture of the given size takes inside a box: as large as fits, aspect kept, centred.</summary>
		public static Rect Fit(double boxWidth, double boxHeight, double imageWidth, double imageHeight)
		{
			if (imageWidth <= 0 || imageHeight <= 0 || boxWidth <= 0 || boxHeight <= 0)
			{
				return default;
			}
			double scale = Math.Min(boxWidth / imageWidth, boxHeight / imageHeight);
			double w = imageWidth * scale, h = imageHeight * scale;
			return new Rect((boxWidth - w) / 2, (boxHeight - h) / 2, w, h);
		}

		public override void Render(DrawingContext context)
		{
			double w = Bounds.Width, h = Bounds.Height;
			CoverImage current = image;
			if (current == null)
			{
				// Not loaded yet: a quiet tile of cover proportions.
				Rect tile = Fit(w, h, 2, 3);
				if (tile.Width > 0)
				{
					context.DrawRectangle(PlaceholderFill, PlaceholderPen, tile, 3, 3);
				}
				return;
			}
			Rect target = Fit(w, h, current.Width, current.Height);
			if (target.Width > 0)
			{
				context.Custom(new CoverDrawOperation(current, target));
			}
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
		}
	}
}
