using System;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Services;
using ComicRack.Mac.Thumbnails;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// One cell of the grid: a <see cref="CrThumbnailItem"/> (cover, badges, caption, selection) bound to a book. The grid's
	/// <c>ItemsRepeater</c> creates a few dozen of these and reuses them: <see cref="Bind"/> when a cell is realized for
	/// a book, <see cref="Unbind"/> when it is recycled. How a cell looks is the item's business (<c>CrThumbnailItem</c> in the chrome layer);
	/// this class decides what it shows and when it asks for pictures.
	///
	/// <b>Thumbnail rules</b> (the M2.2a wiring notes). On bind the memory cache is asked first (<c>TryGetCached</c>: no task, no
	/// flash). Otherwise the cell asks the service and keeps the <see cref="CancellationTokenSource"/>; recycling, a new book or a
	/// service that was replaced cancels it, so a book scrolled past never decodes if its turn has not come. A result that arrives for
	/// a book the cell no longer shows is disposed. The cell owns the image it gets and releases it when it lets go of it.
	/// A size change keeps the old picture until the new one arrives, and asks again only if the cached step changed.
	/// A cover that arrives from the service fades in (<c>FadeInThumbnails</c>); one that was cached is there at once.
	/// The page behind the dog-ear is asked for only while the cell is selected or hot and the option is on (<c>CoverViewItem.GetBackThumbnail</c>).
	/// </summary>
	public sealed class BookCell : CrThumbnailItem
	{
		private BookGridView owner;
		private CancellationTokenSource request;
		private CancellationTokenSource backRequest;
		private int generation;
		private int backGeneration;
		private int shownStep; // the cached step of the picture that is shown, or 0
		private bool hasTip;

		/// <summary>The book this cell shows, or null while it is in the recycle pool.</summary>
		public BookItem Item { get; private set; }

		/// <summary>The position in the grid's list.</summary>
		public int Index { get; private set; } = -1;

		/// <summary>The title of the book (the first line of the older two line cell; the caption is <see cref="CrThumbnailItem.CaptionText"/>).</summary>
		public string TitleText => Item?.Title;

		/// <summary>"Series #Number" of the book.</summary>
		public string SubtitleText => Item?.Subtitle;

		/// <summary>True while a thumbnail request of this cell is waiting for its result.</summary>
		public bool IsRequestPending => request != null;

		/// <summary>True while the request for the page behind the dog-ear is waiting.</summary>
		public bool IsBackRequestPending => backRequest != null;

		public void Bind(BookGridView grid, BookItem item, int index, bool selected, bool focused = false)
		{
			if (Item != null)
			{
				Unbind();
			}
			owner = grid;
			Item = item;
			Index = index;
			ThumbnailOptions options = grid.Options;
			CoverHeight = grid.CoverHeight;
			ApplyOptions(options);
			CaptionText = ThumbnailBadgeBuilder.Caption(item);
			Badges = ThumbnailBadgeBuilder.From(item, options);
			LoadingText = UiText.Message("loadingThumbnail", "Loading Thumbnail...");
			ViewFocused = grid.IsKeyboardFocusWithin;
			IsFocusedItem = focused;
			SetSelected(selected);
			LoadThumbnail(keepCurrent: false);
		}

		public void Unbind()
		{
			CancelRequest();
			CancelBackRequest();
			generation++;
			Cover.SetImage(null);
			BackImage = null;
			shownStep = 0;
			Item = null;
			Index = -1;
			owner = null;
			SetHot(false);
			IsFocusedItem = false;
			SetSelected(false);
			UpdateToolTip();
		}

		/// <summary>True if the cell is drawn as selected.</summary>
		public bool IsSelectedForTest => IsSelected;

		/// <summary>Sets the selection (the focus is <see cref="CrThumbnailItem.IsFocusedItem"/>), and the dog-ear page is asked for or let go.</summary>
		public override void SetSelected(bool value)
		{
			base.SetSelected(value);
			UpdateBackImage();
		}

		/// <summary>Sets the selection and the focus together (the grid draws the model's state on the cells it realizes).</summary>
		public void SetState(bool selected, bool focused)
		{
			if (selected != IsSelected || focused != IsFocusedItem)
			{
				IsFocusedItem = focused;
				SetSelected(selected);
			}
		}

		protected override void OnHotChanged()
		{
			UpdateBackImage();
			UpdateToolTip();
		}

		// ShowToolTips: the card of the book (cover and the first lines of its data), made when the pointer comes and let go of when it leaves.
		private void UpdateToolTip()
		{
			bool wanted = IsHot && Item != null && owner != null && owner.Options.ShowToolTips;
			if (!wanted)
			{
				if (hasTip)
				{
					if (Avalonia.Controls.ToolTip.GetTip(this) is CrBookToolTip old)
					{
						old.Show(null, null);
					}
					Avalonia.Controls.ToolTip.SetTip(this, null);
					hasTip = false;
				}
				return;
			}
			var tip = new CrBookToolTip();
			tip.Show(BookToolTipData.From(Item.Book), Cover.Image);
			Avalonia.Controls.ToolTip.SetTip(this, tip);
			hasTip = true;
		}

		/// <summary>The size, the options or the data changed: draw this cell again with them (the cover picture stays).</summary>
		public void RefreshLook()
		{
			if (Item == null || owner == null)
			{
				return;
			}
			CoverHeight = owner.CoverHeight;
			ApplyOptions(owner.Options);
			CaptionText = ThumbnailBadgeBuilder.Caption(Item);
			Badges = ThumbnailBadgeBuilder.From(Item, owner.Options);
			UpdateBackImage();
		}

		private void ApplyOptions(ThumbnailOptions options)
		{
			RatingMode = options.RatingMode;
			SameSizeCovers = options.CoverThumbnailsSameSize;
			DogEarEnabled = options.DogEarThumbnails;
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
			Cancel(cts);
		}

		private void CancelBackRequest()
		{
			CancellationTokenSource cts = backRequest;
			backRequest = null;
			Cancel(cts);
		}

		private static void Cancel(CancellationTokenSource cts)
		{
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
					Cover.SetImage(null);
					shownStep = 0;
				}
				return;
			}
			int pixels = owner.RequestPixels;
			int step = ThumbnailSteps.Snap(pixels);
			if (keepCurrent && shownStep == step && Cover.HasImage)
			{
				return;
			}
			try
			{
				if (service.TryGetCached(item.Book, pixels, out ThumbnailResult hit))
				{
					Show(hit, step, fade: false);
					return;
				}
			}
			catch (ObjectDisposedException)
			{
				return; // the library is being closed or replaced; the grid is refilled when the new one is open
			}
			if (!keepCurrent)
			{
				Cover.SetImage(null);
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
				Show(result, step, fade: !Cover.HasImage);
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

		private void Show(ThumbnailResult result, int step, bool fade)
		{
			var image = new CoverImage(result.Image);
			Cover.SetImage(image);
			image.Release(); // the cover holds its own reference
			shownStep = step;
			if (fade && owner != null && owner.Options.FadeInThumbnails)
			{
				Cover.StartFadeIn();
			}
			else
			{
				Cover.PictureOpacity = 1;
			}
		}

		// ---- the page behind the dog-ear ---------------------------------------------------------------------------------

		/// <summary>Asks for the page behind the dog-ear while the cell is selected or hot, lets go of it otherwise.</summary>
		private void UpdateBackImage()
		{
			BookItem item = Item;
			ThumbnailService service = owner?.Thumbnails;
			bool wanted = item != null && service != null && owner.Options.DogEarThumbnails && (IsSelected || IsHot) && ThumbnailBadgeBuilder.HasBackPage(item.Book);
			if (!wanted)
			{
				CancelBackRequest();
				backGeneration++;
				BackImage = null;
				return;
			}
			if (BackImage != null || backRequest != null)
			{
				return;
			}
			ComicBook book = item.Book;
			int page = ThumbnailBadgeBuilder.BackPageIndex(book);
			int pixels = owner.RequestPixels;
			try
			{
				if (service.TryGetCached(book, page, pixels, out ThumbnailResult hit))
				{
					ShowBack(hit);
					return;
				}
			}
			catch (ObjectDisposedException)
			{
				return;
			}
			var cts = new CancellationTokenSource();
			backRequest = cts;
			int mine = ++backGeneration;
			_ = AwaitBack(service, book, page, pixels, cts, mine);
		}

		private async Task AwaitBack(ThumbnailService service, ComicBook book, int page, int pixels, CancellationTokenSource cts, int mine)
		{
			try
			{
				ThumbnailResult result = await service.RequestResult(book, page, pixels, cts.Token);
				if (mine != backGeneration || !ReferenceEquals(backRequest, cts))
				{
					result.Image.Dispose();
					return;
				}
				backRequest = null;
				cts.Dispose();
				if (result.IsPlaceholder)
				{
					result.Image.Dispose(); // no page to show behind the corner
					return;
				}
				ShowBack(result);
			}
			catch (OperationCanceledException)
			{
			}
			catch (ObjectDisposedException)
			{
			}
			catch (Exception ex)
			{
				AppLog.Error("The page behind the dog-ear could not be shown.", ex);
			}
		}

		private void ShowBack(ThumbnailResult result)
		{
			if (result.IsPlaceholder)
			{
				result.Image.Dispose();
				return;
			}
			var image = new CoverImage(result.Image);
			BackImage = image;
			image.Release();
		}
	}
}
