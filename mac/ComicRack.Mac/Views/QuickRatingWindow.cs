using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Services;
using ComicRack.Mac.Thumbnails;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The Quick Rating dialog (DL.18, <c>ComicRack/Dialogs/QuickRatingDialog.cs</c>): the cover of the book (170 x 227), the rating editor below it (170 x 21),
	/// the review text at the right (369 x 254), "Show when Book read" at the bottom left and OK and Cancel. OK writes the review and the rating into the book
	/// and the check box into <c>AutoShowQuickReview</c> (the caller does that with the <see cref="QuickRatingResult"/>). The title is the dialog's text, a dash
	/// and the caption of the book without its title (<c>CaptionWithoutTitle</c>). Spec: <c>mac/specs/views/QuickRatingDialog.md</c>.
	/// </summary>
	public sealed class QuickRatingWindow : CrDialogWindow
	{
		private readonly QuickRatingRequest request;
		private readonly QuickRatingBody body = new QuickRatingBody();
		private readonly CheckBox showWhenRead = new CheckBox();
		private readonly CoverView cover;
		private readonly CrRatingStars stars;
		private readonly TextBox review;
		private CancellationTokenSource coverRequest;

		public QuickRatingWindow(QuickRatingRequest request)
		{
			this.request = request ?? throw new ArgumentNullException(nameof(request));
			TrSection = "QuickRatingDialog";
			ComicBook book = request.Book;
			Title = UiText.Get("QuickRatingDialog", "QuickRatingDialog", "Quick Rating") + " - " + book?.CaptionWithoutTitle;

			cover = body.CoverBox;
			stars = body.RatingBox;
			review = body.Review;
			Body = body;

			showWhenRead.Content = UiText.Get("QuickRatingDialog", "chkShow", "Show when Book read");
			showWhenRead.IsChecked = request.ShowWhenRead;
			Footer = showWhenRead;

			review.Text = book?.Review;
			stars.Rating = book?.Rating ?? 0;
			Opened += (sender, e) => _ = LoadCoverAsync();
			Closed += (sender, e) =>
			{
				coverRequest?.Cancel();
				cover.SetImage(null);
			};
		}

		public CoverView Cover => cover;

		public CrRatingStars Stars => stars;

		public TextBox ReviewBox => review;

		public CheckBox ShowWhenReadBox => showWhenRead;

		// the front cover (SetThumbnailImage): from the cache at once, else decoded; a failure leaves the box empty
		private async Task LoadCoverAsync()
		{
			ThumbnailService service = request.Thumbnails;
			ComicBook book = request.Book;
			if (service == null || book == null)
			{
				return;
			}
			var cts = new CancellationTokenSource();
			coverRequest = cts;
			try
			{
				double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
				ThumbnailResult result = await service.RequestResult(book, (int)Math.Ceiling(227 * scale), cts.Token);
				if (cts.IsCancellationRequested)
				{
					result.Image.Dispose();
					return;
				}
				var image = new ComicRack.Mac.Browser.CoverImage(result.Image);
				cover.SetImage(image);
				image.Release();
			}
			catch (OperationCanceledException)
			{
			}
			catch (ObjectDisposedException)
			{
			}
			catch (Exception ex)
			{
				AppLog.Error("The cover of the quick rating dialog could not be shown.", ex);
			}
		}

		/// <summary>Shows the dialog over <paramref name="owner"/>; the result after OK, else null.</summary>
		public async Task<QuickRatingResult> ShowAsync(Window owner)
		{
			bool ok = await ShowDialogAsync(owner);
			return ok ? Result() : null;
		}

		/// <summary>What the dialog holds now (what OK returns).</summary>
		public QuickRatingResult Result()
		{
			return new QuickRatingResult
			{
				Review = review.Text ?? string.Empty,
				Rating = (float)stars.Rating,
				ShowWhenRead = showWhenRead.IsChecked == true,
			};
		}
	}
}
