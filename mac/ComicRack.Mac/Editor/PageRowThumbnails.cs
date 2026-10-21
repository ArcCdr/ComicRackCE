using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Services;
using ComicRack.Mac.Thumbnails;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Editor
{
	/// <summary>
	/// The pictures of the rows of the page list (<c>PageViewItem.GetThumbnail</c>): the thumbnail of a page from the thumbnail service, the same cache and decoder as the browser's covers.
	/// A picture is kept for the image of the file, its rotation and its step (not for the position of the page, which moves when pages are moved), so moving pages asks for nothing
	/// again. A row whose picture is not there yet is asked for and <see cref="Ready"/> follows on the UI thread. The last <see cref="Capacity"/> pictures are kept; the owner releases
	/// the ones it gets.
	/// </summary>
	public sealed class PageRowThumbnails : IDisposable
	{
		public const int Capacity = 96;

		private readonly ThumbnailService service;
		private readonly ComicBook book;
		private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
		private readonly Dictionary<(int Image, int Rotation, int Step), CoverImage> pictures = new Dictionary<(int, int, int), CoverImage>();
		private readonly LinkedList<(int Image, int Rotation, int Step)> order = new LinkedList<(int, int, int)>();
		private readonly HashSet<(int Image, int Rotation, int Step)> asked = new HashSet<(int, int, int)>();
		private bool disposed;

		public PageRowThumbnails(ThumbnailService service, ComicBook book)
		{
			this.service = service;
			this.book = book ?? throw new ArgumentNullException(nameof(book));
		}

		/// <summary>A picture arrived.</summary>
		public event EventHandler Ready;

		/// <summary>The picture of a row at about <paramref name="height"/> pixels with a reference for the caller, or null while it is made (or when it cannot be).</summary>
		public CoverImage Get(PageRow row, int height)
		{
			if (disposed || service == null || row == null || height <= 0)
			{
				return null;
			}
			int step = ThumbnailSteps.Snap(height);
			int page = row.Page;
			var key = (row.ImageIndex, (int)row.Info.Rotation, step);
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

		private async Task Load(int page, int step, (int, int, int) key)
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
					Ready?.Invoke(this, EventArgs.Empty);
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
				AppLog.Error("A page picture of the Info dialog could not be made.", ex);
				Store(key, null);
			}
		}

		private void Store((int Image, int Rotation, int Step) key, CoverImage picture)
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
				(int Image, int Rotation, int Step) oldest = order.First.Value;
				order.RemoveFirst();
				if (pictures.TryGetValue(oldest, out CoverImage evicted))
				{
					evicted?.Release();
					pictures.Remove(oldest);
					asked.Remove(oldest);
				}
			}
		}

		/// <summary>Lets go of every picture (Refresh): they are asked for again when drawn.</summary>
		public void Clear()
		{
			foreach (CoverImage picture in pictures.Values)
			{
				picture?.Release();
			}
			pictures.Clear();
			order.Clear();
			asked.Clear();
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			lifetime.Cancel();
			Clear();
		}
	}
}
