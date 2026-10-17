using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using ComicRack.Mac.Browser;
using SkiaSharp;

namespace ComicRack.Mac.Reader
{
	/// <summary>
	/// Draws the pages of the reader with Skia directly: each page's <c>SKImage</c> goes to the canvas of the render thread with one
	/// <c>DrawImageRect</c> (<see cref="ReaderPainter"/>), with no intermediate bitmap and no copy of pixels. The operation holds a
	/// reference of every picture until Avalonia disposes it, so a page that the cache evicts or that the reader has turned away from
	/// stays valid for a frame that is still queued for the render thread.
	/// </summary>
	public sealed class ReaderDrawOperation : ICustomDrawOperation
	{
		private readonly CoverImage[] images;
		private readonly Rect[] destinations;
		private readonly Rect bounds;
		private readonly long turn;
		private readonly PageTurnMeter meter;

		/// <summary>Takes a reference of each image, released by <see cref="Dispose"/>.</summary>
		public ReaderDrawOperation(CoverImage[] images, Rect[] destinations, Rect bounds, long turn, PageTurnMeter meter)
		{
			this.images = new CoverImage[images.Length];
			for (int i = 0; i < images.Length; i++)
			{
				this.images[i] = images[i].Retain();
			}
			this.destinations = destinations;
			this.bounds = bounds;
			this.turn = turn;
			this.meter = meter;
		}

		public Rect Bounds => bounds;

		public bool HitTest(Point p) => bounds.Contains(p);

		public bool Equals(ICustomDrawOperation other)
		{
			if (!(other is ReaderDrawOperation o) || o.images.Length != images.Length || o.bounds != bounds || o.turn != turn)
			{
				return false;
			}
			for (int i = 0; i < images.Length; i++)
			{
				if (!ReferenceEquals(o.images[i], images[i]) || o.destinations[i] != destinations[i])
				{
					return false;
				}
			}
			return true;
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
				if (canvas == null)
				{
					return;
				}
				for (int i = 0; i < images.Length; i++)
				{
					SKImage image = images[i].Image;
					if (image != null)
					{
						ReaderPainter.DrawPage(canvas, image, destinations[i]);
					}
				}
			}
			meter?.Presented(turn);
		}

		public void Dispose()
		{
			foreach (CoverImage image in images)
			{
				image.Release();
			}
		}
	}
}
