using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Services;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;

namespace ComicRack.Mac.ViewModels
{
	/// <summary>
	/// What the book context menu of the browser does with the selected books (P5.5, <c>ComicBrowserControl</c>'s command table): Info, Reveal in Finder, Mark as,
	/// My Rating, Quick Rating and Review, Remove. The menu and the keys ask the browser (<see cref="BrowserViewModel.Run"/>, <see cref="BrowserViewModel.RemoveSelected()"/>);
	/// this part answers, with the dialogs of <see cref="ILibraryDialogs"/>.
	/// </summary>
	public sealed partial class MainViewModel
	{
		private Task quickReviewChain = Task.CompletedTask;
		private bool closingEverything;

		private void WireBooks()
		{
			Browser.BookCommandRequested += (sender, e) => OnBookCommand(e);
			Browser.RemoveBooksRequested += (sender, books) => _ = RemoveBooksAsync(books, Browser.PendingRemoveAsk);
			if (tabs != null)
			{
				tabs.BookClosed += (sender, book) => OnBookClosed(book);
			}
		}

		private void OnBookCommand(BookCommandEventArgs e)
		{
			switch (e.Command)
			{
				case BookCommand.Info:
					// Info... (miProperties, MainForm.ShowInfo): the Info dialog of the selected books
					_ = ShowInfoAsync(e.Books);
					break;
				case BookCommand.Reveal:
					RevealBooks(e.Books);
					break;
				case BookCommand.MarkRead:
					foreach (ComicBook book in e.Books)
					{
						book.MarkAsRead();
					}
					break;
				case BookCommand.MarkUnread:
					foreach (ComicBook book in e.Books)
					{
						book.MarkAsNotRead();
					}
					break;
				case BookCommand.MarkChecked:
					foreach (ComicBook book in e.Books)
					{
						book.Checked = true;
					}
					break;
				case BookCommand.MarkUnchecked:
					foreach (ComicBook book in e.Books)
					{
						book.Checked = false;
					}
					break;
				case BookCommand.Rate:
					RateBooks(e.Books, e.Value);
					break;
				case BookCommand.QuickRating:
					_ = QuickRatingAsync(e.Books);
					break;
				case BookCommand.UpdateFiles:
					UpdateBookFiles(e.Books);
					break;
			}
		}

		// Reveal Book in Explorer (RevealInExplorer): the first selected book that has a file.
		private void RevealBooks(IReadOnlyList<ComicBook> books)
		{
			ComicBook book = books.FirstOrDefault(b => b.IsLinked);
			if (book != null)
			{
				shell.RevealInFinder(book.FilePath);
			}
		}

		private void SetUndoMarker(string text)
		{
			try
			{
				hostService.Host?.DatabaseManager.Database.Undo.SetMarker(text);
			}
			catch (Exception ex)
			{
				AppLog.Error("The undo marker could not be set.", ex);
			}
		}

		/// <summary>My Rating of <c>RatingEditor.SetRating</c>: an undo marker "Change Rating", then every book gets the rating.</summary>
		public void RateBooks(IReadOnlyList<ComicBook> books, float rating)
		{
			if (books == null || books.Count == 0)
			{
				return;
			}
			SetUndoMarker(UiText.Message("UndoRating", "Change Rating"));
			rating = Math.Clamp(rating, 0f, 5f);
			foreach (ComicBook book in books)
			{
				book.Rating = rating;
			}
		}

		/// <summary>
		/// The browser the keyboard focus is in, set by the window: the Edit menu's My Rating and its keys (<c>MainForm.GetRatingEditor</c> asks the active control)
		/// rate the selected books of the browser while it has the focus, the book of the current tab otherwise.
		/// </summary>
		public Func<bool> BrowserHasFocus { get; set; }

		/// <summary>True if the Edit menu's My Rating works on the book of the current tab (the reader has the focus), false if on the selection of the browser.</summary>
		public bool RatingTargetsReader => Reader != null && ShowReaderPage && !(BrowserHasFocus?.Invoke() ?? false);

		/// <summary>The rating the Edit menu's My Rating would change: the book of the current tab, or what the selected books share (-1: nothing or different).</summary>
		public float CurrentRating => RatingTargetsReader ? Reader.Rating : Browser.SelectedRating;

		/// <summary>True if My Rating can work now (<c>IEditRating.IsValid</c>).</summary>
		public bool CanRate => RatingTargetsReader || Browser.SelectedCount > 0;

		/// <summary>The Edit menu's None, 1 to 5 stars (<c>miRate0</c> to <c>miRate5</c>).</summary>
		public void Rate(float rating)
		{
			if (RatingTargetsReader)
			{
				SetUndoMarker(UiText.Message("UndoRating", "Change Rating"));
				Reader.SetRating(rating);
			}
			else
			{
				RateBooks(Browser.SelectedBooks, rating);
			}
		}

		/// <summary>Quick Rating and Review (Option+Shift+Q) of the Edit menu: the book of the current tab or the first selected book.</summary>
		[RelayCommand]
		private Task QuickRating()
		{
			ComicBook open = tabs?.Current?.Book;
			if (RatingTargetsReader && open != null)
			{
				return QuickRatingAsync(new[] { open });
			}
			return QuickRatingAsync(Browser.SelectedBooks);
		}

		/// <summary>
		/// <c>RatingEditor.QuickRatingAndReview</c>: an undo marker "Quick Rating", then the dialog for the first book; after OK its review and rating are
		/// written to the book and the check box into <c>AutoShowQuickReview</c>.
		/// </summary>
		public async Task<bool> QuickRatingAsync(IReadOnlyList<ComicBook> books)
		{
			ComicBook book = books?.FirstOrDefault();
			if (book == null)
			{
				return false;
			}
			SetUndoMarker(UiText.Message("QuickRating", "Quick Rating"));
			QuickRatingResult result = await dialogs.AskQuickRatingAsync(new QuickRatingRequest
			{
				Book = book,
				Thumbnails = hostService.Thumbnails,
				ShowWhenRead = preferences?.AutoShowQuickReview ?? false,
			});
			if (result == null)
			{
				return false;
			}
			book.Review = result.Review;
			book.Rating = result.Rating;
			if (preferences != null)
			{
				preferences.AutoShowQuickReview = result.ShowWhenRead;
			}
			return true;
		}

		// MainForm.OnBookClosing: a book that was read and not rated gets the quick review dialog when AutoShowQuickReview is on. Not while the application
		// closes its readers (quit, import), and one dialog at a time when several tabs close together.
		private void OnBookClosed(ComicBook book)
		{
			// OnBookClosed of the original: a book that was kept back from the write queue while it was open goes in now
			fileUpdates?.BookClosed(book);
			if (closingEverything || book == null || preferences == null || !preferences.AutoShowQuickReview || !book.HasBeenRead || book.Rating != 0f)
			{
				return;
			}
			quickReviewChain = ChainAsync(quickReviewChain, () => QuickRatingAsync(new[] { book }));
		}

		/// <summary>Completes when the quick reviews that closing books asked for are done (tests).</summary>
		public Task WhenQuickReviewsDoneAsync() => quickReviewChain;

		private static async Task ChainAsync(Task previous, Func<Task> next)
		{
			try
			{
				await previous;
			}
			catch (Exception)
			{
			}
			try
			{
				await next();
			}
			catch (Exception ex)
			{
				AppLog.Error("The quick review failed.", ex);
			}
		}

		// ---- Remove (ComicListLibraryBrowser.RemoveBookHandler)

		/// <summary>
		/// Remove... : asks what to do (unless <paramref name="ask"/> is false) and takes the books out of the shown list or out of the library, as the original's
		/// handler does: the library, and a list that cannot drop a book by itself, ask "Are you sure you want to remove these books from the library?" with the
		/// option to move the files to the Trash; a fixed list and a smart list ask "...from the list?" with the options to remove them from the library as well and
		/// to move the files to the Trash. The options are remembered in the preferences. Returns what was done, or null if the user cancelled.
		/// </summary>
		public async Task<RemoveResult> RemoveBooksAsync(IReadOnlyList<ComicBook> books, bool ask = true)
		{
			MacHost host = hostService.Host;
			if (host == null || books == null || books.Count == 0)
			{
				return null;
			}
			ComicListItem list = Sidebar.SelectedNode?.Node.Item;
			RemoveMode mode = BookRemoval.ModeOf(list);
			bool filtered = BookRemoval.IsFiltered(list);
			AppPreferences settings = preferences ?? new AppPreferences();
			string moveToBin = null;
			if (ask && books.Any(b => b.IsLinked))
			{
				moveToBin = (settings.MoveFilesToRecycleBin ? "!" : string.Empty) + UiText.Message("MoveBin", "&Also move the files to the Trash");
			}
			bool alsoFromLibrary = true;
			if (mode == RemoveMode.Library)
			{
				if (ask)
				{
					QuestionAnswer answer = await dialogs.AskQuestionAsync(new QuestionRequest
					{
						Question = UiText.Message("AskRemoveFromLibrary", "Are you sure you want to remove these books from the library?\nAll information not stored in the files will be lost (like Open Count, Last Read etc.)!"),
						OkText = UiText.Message("Remove", "Remove"),
						OptionText = moveToBin,
					});
					if (!answer.Accepted)
					{
						return null;
					}
					if (!string.IsNullOrEmpty(moveToBin))
					{
						settings.MoveFilesToRecycleBin = answer.Option;
					}
				}
			}
			else
			{
				alsoFromLibrary = filtered ? settings.AlsoRemoveFromLibraryFiltered : settings.AlsoRemoveFromLibrary;
				if (ask)
				{
					QuestionAnswer answer = await dialogs.AskQuestionAsync(new QuestionRequest
					{
						Question = UiText.Message("AskRemoveComics", "Are you sure you want to remove these books from the list?"),
						OkText = UiText.Message("Remove", "Remove"),
						OptionText = (alsoFromLibrary ? "!" : string.Empty) + UiText.Message("AlsoRemoveFromLibrary", "&Additionally remove the books from the Library (all information not stored in the files will be lost)"),
						Option2Text = moveToBin,
						ShowCancel = true,
					});
					if (!answer.Accepted)
					{
						return null;
					}
					alsoFromLibrary = answer.Option;
					if (filtered)
					{
						settings.AlsoRemoveFromLibraryFiltered = alsoFromLibrary;
					}
					else
					{
						settings.AlsoRemoveFromLibrary = alsoFromLibrary;
					}
					if (!string.IsNullOrEmpty(moveToBin))
					{
						settings.MoveFilesToRecycleBin = answer.Option2;
					}
				}
			}
			ComicBook[] chosen = books.ToArray();
			bool moveFiles = settings.MoveFilesToRecycleBin;
			ComicDatabase database = host.DatabaseManager.Database;
			RemoveResult result;
			try
			{
				result = await Task.Run(() => BookRemoval.Remove(database, list, chosen, alsoFromLibrary, moveFiles));
			}
			catch (Exception ex)
			{
				AppLog.Error("Removing books failed.", ex);
				await ReportProblem(UiText.Message("Remove", "Remove"), ex.Message);
				return null;
			}
			AppLog.Info($"Removed books: {result.RemovedFromList} from the list, {result.RemovedFromLibrary} from the library, {result.FilesTrashed} files to the Trash, {result.FilesFailed} files could not be moved.");
			SetBookCount(database.Books.Count);
			RequestLiveRefresh();
			if (result.FilesFailed > 0)
			{
				await dialogs.ShowMessageAsync("ComicRack", UiText.Message("FailedDeleteBooks", "Some files could not be deleted (maybe they are in use)!"));
			}
			return result;
		}

		/// <summary>
		/// A question with "do not ask again" (<c>Program.AskQuestion</c>): answered with OK at once when its flag is set; else asked with the alert's own check box,
		/// and OK with the box checked sets the flag. False after Cancel.
		/// </summary>
		public async Task<bool> AskQuestionAsync(string question, string okText, HiddenMessageBoxes flag, string askAgainText = null, string cancelText = null)
		{
			if (preferences != null && (preferences.HiddenMessageBoxes & flag) != 0)
			{
				return true;
			}
			QuestionAnswer answer = await dialogs.AskQuestionAsync(new QuestionRequest
			{
				Question = question,
				OkText = okText,
				CancelText = cancelText,
				SuppressionText = string.IsNullOrEmpty(askAgainText) ? UiText.Message("NotAskAgain", "&Do not ask me again") : askAgainText,
			});
			if (!answer.Accepted)
			{
				return false;
			}
			if (answer.Suppressed && preferences != null)
			{
				preferences.HiddenMessageBoxes |= flag;
			}
			return true;
		}
	}
}
