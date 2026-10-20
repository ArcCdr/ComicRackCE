using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Services;
using ComicRack.Mac.Thumbnails;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// One cell of the grid: the cover (<see cref="CoverView"/>), the title and "Series #Number", and the selection highlight.
	/// The grid's <c>ItemsRepeater</c> creates a few dozen of these and reuses them: <see cref="Bind"/> when a cell is realized for
	/// a book, <see cref="Unbind"/> when it is recycled.
	///
	/// <b>Thumbnail rules</b> (the M2.2a wiring notes). On bind the memory cache is asked first (<c>TryGetCached</c>: no task, no
	/// flash). Otherwise the cell asks the service and keeps the <see cref="CancellationTokenSource"/>; recycling, a new book or a
	/// service that was replaced cancels it, so a book scrolled past never decodes if its turn has not come. A result that arrives for
	/// a book the cell no longer shows is disposed. The cell owns the image it gets and releases it when it lets go of it.
	/// A size change keeps the old picture until the new one arrives, and asks again only if the cached step changed.
	/// </summary>
	public sealed class BookCell : Border
	{
		private readonly CoverView cover = new CoverView();
		private readonly TextBlock title = new TextBlock { Classes = { "cr-book-title" }, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
		private readonly TextBlock subtitle = new TextBlock { Classes = { "cr-book-subtitle" }, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
		private bool selected;
		private BookGridView owner;
		private CancellationTokenSource request;
		private int generation;
		private int shownStep; // the cached step of the picture that is shown, or 0

		public BookCell()
		{
			Classes.Add("cr-book-cell");
			var text = new StackPanel { Spacing = 1, Margin = new Thickness(0, 4, 0, 0), Children = { title, subtitle } };
			var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
			grid.Children.Add(cover);
			Grid.SetRow(text, 1);
			grid.Children.Add(text);
			Child = grid;
			ClipToBounds = true;
		}

		/// <summary>The book this cell shows, or null while it is in the recycle pool.</summary>
		public BookItem Item { get; private set; }

		/// <summary>The position in the grid's list.</summary>
		public int Index { get; private set; } = -1;

		public CoverView Cover => cover;

		public string TitleText => title.Text;

		public string SubtitleText => subtitle.Text;

		/// <summary>True while a thumbnail request of this cell is waiting for its result.</summary>
		public bool IsRequestPending => request != null;

		public void Bind(BookGridView grid, BookItem item, int index, bool selected)
		{
			if (Item != null)
			{
				Unbind();
			}
			owner = grid;
			Item = item;
			Index = index;
			title.Text = item.Title;
			subtitle.Text = item.Subtitle;
			subtitle.IsVisible = item.Subtitle.Length > 0;
			SetSelected(selected);
			LoadThumbnail(keepCurrent: false);
		}

		public void Unbind()
		{
			CancelRequest();
			generation++;
			cover.SetImage(null);
			shownStep = 0;
			Item = null;
			Index = -1;
			owner = null;
			SetSelected(false);
		}

		/// <summary>True if the cell is drawn as selected.</summary>
		public bool IsSelectedForTest => selected;

		/// <summary>Sets the <c>:selected</c> state; what a selected cell looks like is the theme's (<c>CrMvpLook.axaml</c>, <c>Border.cr-book-cell:selected</c>).</summary>
		public void SetSelected(bool value)
		{
			selected = value;
			PseudoClasses.Set(":selected", value);
		}

		/// <summary>The size or the service changed: ask again if the step differs (or <paramref name="force"/>).</summary>
		public void RefreshThumbnail(bool force)
		{
			if (Item != null)
			{
				LoadThumbnail(keepCurrent: !force);
			}
		}

		private void CancelRequest()
		{
			CancellationTokenSource cts = request;
			request = null;
			if (cts != null)
			{
				try
				{
					cts.Cancel();
				}
				finally
				{
					cts.Dispose();
				}
			}
		}

		private void LoadThumbnail(bool keepCurrent)
		{
			BookItem item = Item;
			ThumbnailService service = owner?.Thumbnails;
			CancelRequest();
			if (service == null || item?.Book == null)
			{
				if (!keepCurrent)
				{
					cover.SetImage(null);
					shownStep = 0;
				}
				return;
			}
			int pixels = owner.RequestPixels;
			int step = ThumbnailSteps.Snap(pixels);
			if (keepCurrent && shownStep == step && cover.HasImage)
			{
				return;
			}
			try
			{
				if (service.TryGetCached(item.Book, pixels, out ThumbnailResult hit))
				{
					Show(hit, step);
					return;
				}
			}
			catch (ObjectDisposedException)
			{
				return; // the library is being closed or replaced; the grid is refilled when the new one is open
			}
			if (!keepCurrent)
			{
				cover.SetImage(null);
				shownStep = 0;
			}
			var cts = new CancellationTokenSource();
			request = cts;
			int mine = ++generation;
			_ = Await(service, item, pixels, step, cts, mine);
		}

		private async Task Await(ThumbnailService service, BookItem item, int pixels, int step, CancellationTokenSource cts, int mine)
		{
			try
			{
				ThumbnailResult result = await service.RequestResult(item.Book, pixels, cts.Token);
				if (mine != generation || !ReferenceEquals(request, cts))
				{
					result.Image.Dispose(); // the cell moved on while the decode ran
					return;
				}
				request = null;
				cts.Dispose();
				Show(result, step);
			}
			catch (OperationCanceledException)
			{
			}
			catch (ObjectDisposedException)
			{
			}
			catch (Exception ex)
			{
				AppLog.Error("A cover could not be shown.", ex);
			}
		}

		private void Show(ThumbnailResult result, int step)
		{
			var image = new CoverImage(result.Image);
			cover.SetImage(image);
			image.Release(); // the cover holds its own reference
			shownStep = step;
		}
	}
}
