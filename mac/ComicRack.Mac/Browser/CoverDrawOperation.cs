using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace ComicRack.Mac.Browser
{
	/// <summary>
	/// Draws a cover with Skia directly: the cell's <see cref="SKImage"/> goes to the canvas of the render thread with one
	/// <c>DrawImageRect</c>, scaled to the cell, with no intermediate bitmap and no copy of pixels (Avalonia's own <c>Bitmap</c> would
	/// need one per image). The operation lives until the render thread is done with the frame and holds a reference of the
	/// <see cref="CoverImage"/> meanwhile.
	/// </summary>
	public sealed class CoverDrawOperation : ICustomDrawOperation
	{
		private readonly CoverImage image;
		private readonly Rect destination;

		/// <summary>Takes a reference of <paramref name="image"/>, released by <see cref="Dispose"/>.</summary>
		public CoverDrawOperation(CoverImage image, Rect destination)
		{
			this.image = image.Retain();
			this.destination = destination;
		}

		public Rect Bounds => destination;

		public bool HitTest(Point p) => destination.Contains(p);

		public bool Equals(ICustomDrawOperation other)
		{
			return other is CoverDrawOperation o && ReferenceEquals(o.image, image) && o.destination == destination;
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
				canvas.DrawImage(skImage, target, sampling);
			}
		}

		public void Dispose()
		{
			image.Release();
		}
	}
}
