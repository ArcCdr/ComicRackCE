using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Services;
using ComicRack.Mac.Thumbnails;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Reader
{
	/// <summary>
	/// The pictures of the page strip of the navigation overlay (<c>NavigationOverlay.GetThumbnail</c>): the thumbnails of the pages of one book from the
	/// thumbnail service, the same cache and the same decoder as the browser's covers. A page that is not there yet is requested and
	/// <see cref="Ready"/> is raised when it arrives (on the UI thread); until then the overlay shows the page number. A page that cannot be
	/// made stays a number. The last <see cref="Capacity"/> pictures are kept.
	/// </summary>
	public sealed class CorePageThumbnails : INavigationThumbnailSource, IDisposable
	{
		/// <summary>How many pictures are kept.</summary>
		public const int Capacity = 64;

		private readonly ComicBook book;
		private readonly ThumbnailService service;
		private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
		private readonly Dictionary<(int Page, int Step), CoverImage> pictures = new Dictionary<(int, int), CoverImage>();
		private readonly LinkedList<(int Page, int Step)> order = new LinkedList<(int, int)>();
		private readonly HashSet<(int Page, int Step)> asked = new HashSet<(int, int)>();
		private bool disposed;

		public CorePageThumbnails(ComicBook book, ThumbnailService service)
		{
			this.book = book ?? throw new ArgumentNullException(nameof(book));
			this.service = service ?? throw new ArgumentNullException(nameof(service));
		}

		public event EventHandler<int> Ready;

		public CoverImage Get(int page, int height)
		{
			if (disposed || page < 0 || height <= 0)
			{
				return null;
			}
			int step = ThumbnailSteps.Snap(height);
			var key = (page, step);
			if (pictures.TryGetValue(key, out CoverImage found))
			{
				order.Remove(key);
				order.AddLast(key);
				return found?.Retain();
			}
			if (asked.Add(key))
			{
				_ = Load(page, step, key);
			}
			return null;
		}

		private async Task Load(int page, int step, (int, int) key)
		{
			try
			{
				ThumbnailResult result;
				if (!service.TryGetCached(book, page, step, out result))
				{
					result = await service.RequestResult(book, page, step, lifetime.Token);
				}
				if (disposed)
				{
					result.Image.Dispose();
					return;
				}
				CoverImage picture = result.IsPlaceholder ? null : new CoverImage(result.Image);
				if (result.IsPlaceholder)
				{
					result.Image.Dispose();
				}
				Store(key, picture);
				if (picture != null)
				{
					Ready?.Invoke(this, page);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (ObjectDisposedException)
			{
			}
			catch (Exception ex)
			{
				AppLog.Error("A page picture of the navigation overlay could not be made.", ex);
				Store(key, null);
			}
		}

		private void Store((int Page, int Step) key, CoverImage picture)
		{
			if (pictures.TryGetValue(key, out CoverImage old))
			{
				old?.Release();
				order.Remove(key);
			}
			pictures[key] = picture;
			order.AddLast(key);
			while (order.Count > Capacity)
			{
				(int Page, int Step) oldest = order.First.Value;
				order.RemoveFirst();
				if (pictures.TryGetValue(oldest, out CoverImage evicted))
				{
					evicted?.Release();
					pictures.Remove(oldest);
					asked.Remove(oldest);
				}
			}
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			lifetime.Cancel();
			foreach (CoverImage picture in pictures.Values)
			{
				picture?.Release();
			}
			pictures.Clear();
			order.Clear();
		}
	}
}
