using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Themes;
using SkiaSharp;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// The visible-part overlay (<c>visiblePartOverlay</c> of <c>ComicDisplayControl</c>) at the bottom right: when the page is larger than the window
	/// it shows the whole page in miniature on a glass panel, dimmed to 0.3, with the part that is in the window at full strength. It is the page
	/// fitted into 200 x 200 plus 16 each way, appears with a fade (100, 2000, 100 ms) whenever the visible part changes, and has a close button
	/// (the <c>Close</c> picture, 16 x 16, 3 from the right and 2 from the top of the padded panel) unless <c>HideVisiblePartOverlayClose</c> is on.
	/// </summary>
	public class CrPartOverlay : CrReaderOverlay
	{
		/// <summary><c>partInfoSize</c>: the page is fitted into this before the 16 units of glass are added.</summary>
		public static readonly Size PictureBox = new Size(200, 200);

		private const double Glass = 16;
		private const double CloseSize = 16;

		private CoverImage[] images = new CoverImage[0];
		private Rect[] pages = new Rect[0];
		private Size content;
		private Rect visible;
		private bool showClose = true;

		public CrPartOverlay()
		{
			IsHitTestVisible = true;
		}

		/// <summary>Raised when the close button is pressed (the owner turns the overlay off, <c>VisibleInfoOverlays &= ~PartInfo</c>).</summary>
		public event EventHandler CloseRequested;

		/// <summary>Whether the close button is there (the opposite of <c>EngineConfiguration.HideVisiblePartOverlayClose</c>).</summary>
		public bool ShowClose
		{
			get => showClose;
			set
			{
				showClose = value;
				InvalidateVisual();
			}
		}

		/// <summary>The size of the whole content in the units the other rectangles use.</summary>
		public Size ContentExtent => content;

		/// <summary>
		/// Gives the overlay what to draw: the pictures of the pages (retained here, the previous ones are released), the rectangle of each page in
		/// the content, the size of the content and the part of it that the window shows (all in the same units).
		/// </summary>
		public void SetContent(IReadOnlyList<CoverImage> pictures, IReadOnlyList<Rect> pageRects, Size contentSize, Rect visiblePart)
		{
			CoverImage[] old = images;
			var next = new CoverImage[pictures.Count];
			for (int i = 0; i < next.Length; i++)
			{
				next[i] = pictures[i].Retain();
			}
			images = next;
			pages = new Rect[pageRects.Count];
			for (int i = 0; i < pages.Length; i++)
			{
				pages[i] = pageRects[i];
			}
			bool resized = contentSize != content;
			content = contentSize;
			visible = visiblePart;
			foreach (CoverImage image in old)
			{
				image.Release();
			}
			if (resized)
			{
				InvalidateMeasure();
			}
			InvalidateVisual();
		}

		/// <summary>Lets go of the pictures.</summary>
		public void Clear()
		{
			SetContent(new CoverImage[0], new Rect[0], default(Size), default(Rect));
		}

		/// <summary>The size the glass has for a content of the given size (<c>visiblePartOverlay_Drawing</c>): the content fitted into 200 x 200, plus 16 each way.</summary>
		public static Size PanelSizeFor(Size contentSize)
		{
			if (contentSize.Width <= 0 || contentSize.Height <= 0)
			{
				return default(Size);
			}
			double scale = Math.Min(PictureBox.Width / contentSize.Width, PictureBox.Height / contentSize.Height);
			return new Size(Math.Round(contentSize.Width * scale) + Glass, Math.Round(contentSize.Height * scale) + Glass);
		}

		protected override Size ContentSize() => PanelSizeFor(content);

		/// <summary>The rectangle of the miniature of the whole content, in the coordinates of the overlay.</summary>
		public Rect PictureRect
		{
			get
			{
				Rect inner = GlassPainter.Inner(new Rect(Bounds.Size));
				if (content.Width <= 0 || content.Height <= 0 || inner.Width <= 0 || inner.Height <= 0)
				{
					return default(Rect);
				}
				double scale = Math.Min(inner.Width / content.Width, inner.Height / content.Height);
				double w = Math.Round(content.Width * scale), h = Math.Round(content.Height * scale);
				return new Rect(inner.X + Math.Floor((inner.Width - w) / 2), inner.Y + Math.Floor((inner.Height - h) / 2), w, h);
			}
		}

		/// <summary>The close button in the coordinates of the overlay (empty without one).</summary>
		public Rect CloseRect => showClose ? new Rect(Bounds.Width - 4 - CloseSize - 3, 4 - 2, CloseSize, CloseSize) : default(Rect);

		/// <summary>The part of the content that is in the window, in the coordinates of the overlay.</summary>
		public Rect VisibleRect
		{
			get
			{
				Rect picture = PictureRect;
				if (picture.Width <= 0 || content.Width <= 0)
				{
					return default(Rect);
				}
				double scale = picture.Width / content.Width;
				Rect part = visible.Intersect(new Rect(content));
				return new Rect(picture.X + part.X * scale, picture.Y + part.Y * scale, part.Width * scale, part.Height * scale);
			}
		}

		public override void Render(DrawingContext context)
		{
			var area = new Rect(Bounds.Size);
			if (area.Width <= 0 || area.Height <= 0)
			{
				return;
			}
			GlassPainter.Panel(context, this, area);
			Rect picture = PictureRect;
			if (picture.Width > 0 && images.Length > 0)
			{
				double scale = picture.Width / content.Width;
				var rects = new Rect[pages.Length];
				for (int i = 0; i < rects.Length; i++)
				{
					rects[i] = new Rect(picture.X + pages[i].X * scale, picture.Y + pages[i].Y * scale, pages[i].Width * scale, pages[i].Height * scale);
				}
				// the whole page at 0.3, the visible part at full strength (renderer.DrawImage(smallBitmap, r, ..., 0.3f) and (r2, ..., 1f))
				context.Custom(new PartDrawOperation(images, rects, area, null, 0.3));
				Rect part = VisibleRect;
				if (part.Width > 0 && part.Height > 0)
				{
					context.Custom(new PartDrawOperation(images, rects, area, part, 1));
				}
			}
			Rect close = CloseRect;
			if (close.Width > 0)
			{
				Avalonia.Media.Imaging.Bitmap icon = Icons.TryGet("Close", ChromePaint.VariantOf(this));
				if (icon != null)
				{
					ChromePaint.Image(context, icon, close, 0.9);
				}
			}
		}

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			Rect close = CloseRect;
			if (close.Width > 0 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && close.Contains(e.GetPosition(this)))
			{
				e.Handled = true;
				CloseRequested?.Invoke(this, EventArgs.Empty);
			}
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
		}

		/// <summary>Draws the pages of the miniature with Skia (the pictures are retained until the operation is disposed).</summary>
		private sealed class PartDrawOperation : ICustomDrawOperation
		{
			private readonly CoverImage[] pictures;
			private readonly Rect[] destinations;
			private readonly Rect bounds;
			private readonly Rect? clip;
			private readonly double opacity;

			public PartDrawOperation(CoverImage[] source, Rect[] destinations, Rect bounds, Rect? clip, double opacity)
			{
				pictures = new CoverImage[source.Length];
				for (int i = 0; i < source.Length; i++)
				{
					pictures[i] = source[i].Retain();
				}
				this.destinations = destinations;
				this.bounds = bounds;
				this.clip = clip;
				this.opacity = opacity;
			}

			public Rect Bounds => bounds;

			public bool HitTest(Point p) => false;

			public bool Equals(ICustomDrawOperation other) => false;

			public void Render(ImmediateDrawingContext context)
			{
				ISkiaSharpApiLeaseFeature feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
				if (feature == null)
				{
					return;
				}
				using (ISkiaSharpApiLease lease = feature.Lease())
				{
					SKCanvas canvas = lease.SkCanvas;
					if (canvas == null)
					{
						return;
					}
					canvas.Save();
					try
					{
						if (clip.HasValue)
						{
							canvas.ClipRect(new SKRect((float)clip.Value.Left, (float)clip.Value.Top, (float)clip.Value.Right, (float)clip.Value.Bottom));
						}
						using (var paint = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Round(opacity * 255)) })
						{
							for (int i = 0; i < pictures.Length; i++)
							{
								SKImage image = pictures[i].Image;
								if (image == null)
								{
									continue;
								}
								Rect d = destinations[i];
								double scale = d.Width / image.Width;
								canvas.DrawImage(image, new SKRect((float)d.Left, (float)d.Top, (float)d.Right, (float)d.Bottom), ReaderSampling.For(scale), paint);
							}
						}
					}
					finally
					{
						canvas.Restore();
					}
				}
			}

			public void Dispose()
			{
				foreach (CoverImage picture in pictures)
				{
					picture.Release();
				}
			}
		}
	}
}
