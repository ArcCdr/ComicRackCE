using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using ComicRack.Mac.Browser;
using SkiaSharp;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// Draws a cover with Skia directly: the cell's <see cref="SKImage"/> goes to the canvas of the render thread with one
	/// <c>DrawImageRect</c>, scaled to the cell, with no intermediate bitmap and no copy of pixels (Avalonia's own <c>Bitmap</c> would
	/// need one per image). The operation lives until the render thread is done with the frame and holds a reference of the
	/// <see cref="CoverImage"/> meanwhile. It can draw a part of the picture (<c>source</c>: the "same size" covers crop a landscape picture),
	/// with an opacity (the fade-in of a freshly loaded cover) and clipped to a triangle (the page behind the dog-ear).
	/// </summary>
	public sealed class CoverDrawOperation : ICustomDrawOperation
	{
		private readonly CoverImage image;
		private readonly Rect destination;
		private readonly Rect? source;
		private readonly double opacity;
		private readonly Point[] triangle;

		/// <summary>Takes a reference of <paramref name="image"/>, released by <see cref="Dispose"/>.</summary>
		public CoverDrawOperation(CoverImage image, Rect destination, Rect? source = null, double opacity = 1, Point[] triangle = null)
		{
			this.image = image.Retain();
			this.destination = destination;
			this.source = source;
			this.opacity = opacity;
			this.triangle = triangle;
		}

		public Rect Bounds => destination;

		public bool HitTest(Point p) => destination.Contains(p);

		public bool Equals(ICustomDrawOperation other)
		{
			return other is CoverDrawOperation o && ReferenceEquals(o.image, image) && o.destination == destination && o.source == source && o.opacity == opacity
				&& (o.triangle == null && triangle == null || o.triangle != null && triangle != null && o.triangle.AsSpan().SequenceEqual(triangle));
		}

		public void Render(ImmediateDrawingContext context)
		{
			ISkiaSharpApiLeaseFeature feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
			if (feature == null)
			{
				return; // not a Skia renderer (the headless platform without drawing): nothing to draw on
			}
			using (ISkiaSharpApiLease lease = feature.Lease())
			{
				SKCanvas canvas = lease.SkCanvas;
				SKImage skImage = image.Image;
				if (canvas == null || skImage == null)
				{
					return;
				}
				var target = new SKRect((float)destination.Left, (float)destination.Top, (float)destination.Right, (float)destination.Bottom);
				// Linear with mipmaps: a cover that is scaled down by more than 2:1 (a thumbnail of a step above the cell) must not alias.
				var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
				int restore = canvas.Save();
				try
				{
					if (triangle != null)
					{
						using (var path = new SKPath())
						{
							path.MoveTo((float)triangle[0].X, (float)triangle[0].Y);
							path.LineTo((float)triangle[1].X, (float)triangle[1].Y);
							path.LineTo((float)triangle[2].X, (float)triangle[2].Y);
							path.Close();
							canvas.ClipPath(path, SKClipOperation.Intersect, true);
						}
					}
					if (source == null && opacity >= 1)
					{
						canvas.DrawImage(skImage, target, sampling);
					}
					else
					{
						Rect s = source ?? new Rect(0, 0, skImage.Width, skImage.Height);
						using (var paint = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255)) })
						{
							canvas.DrawImage(skImage, new SKRect((float)s.Left, (float)s.Top, (float)s.Right, (float)s.Bottom), target, sampling, paint);
						}
					}
				}
				finally
				{
					canvas.RestoreToCount(restore);
				}
			}
		}

		public void Dispose()
		{
			image.Release();
		}
	}
}
