using System;
using System.Linq;
using System.Threading.Tasks;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using cYo.Projects.ComicRack.Engine;
using Xunit;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>P5.5, PB.18: <c>AutoShowQuickReview</c> shows the Quick Rating dialog when a book that was read to the end and has no rating is closed (<c>MainForm.OnBookClosing</c>).</summary>
	public class QuickReviewTests : IDisposable
	{
		private readonly long liveBefore;

		public QuickReviewTests()
		{
			liveBefore = Gc.BeginCount();
		}

		public void Dispose()
		{
			long expected = liveBefore;
			Gc.WaitForLive(expected);
			Assert.Equal(expected, Gc.Live);
		}

		private static async Task<ReaderSlot> ReadBook(MainReaderHarness h, string name, int lastPageRead, float rating = 0)
		{
			ReaderSlot slot = await h.AddBook(name, open: OpenBookOptions.InNewTab);
			slot.Book.PageCount = 10;
			slot.Book.LastPageRead = lastPageRead;
			slot.Book.Rating = rating;
			return slot;
		}

		[Fact]
		public async Task A_book_read_to_the_end_without_a_rating_asks_for_a_quick_review_when_it_is_closed()
		{
			await Ui.Run(async () =>
			{
				using (var h = new MainReaderHarness())
				{
					h.Preferences.AutoShowQuickReview = true;
					ReaderSlot slot = await ReadBook(h, "done", lastPageRead: 9);
					Assert.True(slot.Book.HasBeenRead);
					h.Tabs.Close(0);
					await h.Vm.WhenQuickReviewsDoneAsync();
					QuickRatingRequest asked = Assert.Single(h.Dialogs.QuickRatings);
					Assert.Same(slot.Book, asked.Book);
					Assert.True(asked.ShowWhenRead);
				}
			});
		}

		[Fact]
		public async Task Nothing_is_asked_when_the_setting_is_off_the_book_is_not_read_or_it_has_a_rating()
		{
			await Ui.Run(async () =>
			{
				using (var h = new MainReaderHarness())
				{
					await ReadBook(h, "off", lastPageRead: 9); // the setting is off by default
					h.Tabs.Close(0);
					h.Preferences.AutoShowQuickReview = true;
					await ReadBook(h, "half", lastPageRead: 4);
					h.Tabs.Close(0);
					await ReadBook(h, "rated", lastPageRead: 9, rating: 3);
					h.Tabs.Close(0);
					await h.Vm.WhenQuickReviewsDoneAsync();
					Assert.Empty(h.Dialogs.QuickRatings);
				}
			});
		}

		[Fact]
		public async Task Closing_all_tabs_asks_one_after_the_other_and_closing_the_readers_for_good_asks_nothing()
		{
			await Ui.Run(async () =>
			{
				using (var h = new MainReaderHarness())
				{
					h.Preferences.AutoShowQuickReview = true;
					await ReadBook(h, "one", lastPageRead: 9);
					await ReadBook(h, "two", lastPageRead: 9);
					h.Tabs.CloseAll();
					await h.Vm.WhenQuickReviewsDoneAsync();
					Assert.Equal(2, h.Dialogs.QuickRatings.Count);
					Assert.Equal(2, h.Dialogs.QuickRatings.Select(r => r.Book).Distinct().Count());

					await ReadBook(h, "three", lastPageRead: 9);
					h.Vm.CloseReaders(); // the application quits or the library is replaced
					await h.Vm.WhenQuickReviewsDoneAsync();
					Assert.Equal(2, h.Dialogs.QuickRatings.Count);
				}
			});
		}
	}
}
