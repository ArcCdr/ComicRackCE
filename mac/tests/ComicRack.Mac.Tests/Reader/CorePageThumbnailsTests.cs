#pragma warning disable xUnit1031 // the tests pump a fake UI thread first, then read what is already there
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using cYo.Projects.ComicRack.Engine;
using Xunit;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>
	/// RV8: the pictures of the page strip of the navigation overlay (<see cref="CorePageThumbnails"/>): the reference counts of what it hands out, the
	/// 64 pictures it keeps, a page that cannot be made, and a close while a picture is still being made. A decoder and a UI thread that the test controls.
	/// </summary>
	public class CorePageThumbnailsTests
	{
		private sealed class Rig : IDisposable
		{
			public readonly FakeDecoder Decoder = new FakeDecoder();
			public readonly FakeUi Ui = new FakeUi();
			public readonly ThumbnailService Service;
			public readonly ComicBook Book = Books.Make("strip");
			public readonly CorePageThumbnails Thumbs;
			public readonly List<int> Ready = new List<int>();

			public Rig()
			{
				Service = new ThumbnailService(new ThumbnailServiceOptions
				{
					Decoder = Decoder,
					Post = Ui.Post,
					Run = work => Task.Run(work),
					Log = m => { },
					DisposeWait = TimeSpan.FromSeconds(10),
				});
				Thumbs = new CorePageThumbnails(Book, Service);
				Thumbs.Ready += (s, page) => Ready.Add(page);
			}

			/// <summary>Asks for a page and waits until the picture is there (the Ready event of that page).</summary>
			public CoverImage Fetch(int page, int height = 200)
			{
				int before = Ready.Count;
				CoverImage now = Thumbs.Get(page, height);
				if (now != null)
				{
					return now;
				}
				Ui.PumpUntil(() => Ready.Count > before);
				CoverImage picture = Thumbs.Get(page, height);
				Assert.NotNull(picture);
				return picture;
			}

			public void Dispose()
			{
				Decoder.Open();
				Thumbs.Dispose();
				Service.Dispose();
				Ui.Pump();
			}
		}

		[Fact]
		public void A_picture_is_asked_for_once_arrives_with_Ready_and_is_handed_out_with_a_reference_for_every_caller()
		{
			long live = Gc.BeginCount();
			using (var rig = new Rig())
			{
				Assert.Null(rig.Thumbs.Get(3, 200)); // not there yet: the page number shows, the picture is made
				Assert.Null(rig.Thumbs.Get(3, 200)); // asking again does not make it twice
				rig.Ui.PumpUntil(() => rig.Ready.Count == 1);
				Assert.Equal(new[] { 3 }, rig.Ready);
				Assert.Equal(1, rig.Decoder.Started);

				CoverImage a = rig.Thumbs.Get(3, 200);
				CoverImage b = rig.Thumbs.Get(3, 200);
				Assert.NotNull(a);
				Assert.Same(a, b); // one picture, shared
				Assert.Equal(3, a.References); // the one the strip keeps and one for each caller
				a.Release();
				Assert.Equal(2, a.References);
				b.Release();
				Assert.Equal(1, a.References);

				// a size that snaps to the same step is the same picture, another step is another picture
				CoverImage near = rig.Thumbs.Get(3, 190);
				Assert.Same(a, near);
				near.Release();
				Assert.Null(rig.Thumbs.Get(3, 400));
				rig.Ui.PumpUntil(() => rig.Ready.Count == 2);
				Assert.Equal(2, rig.Decoder.Started);

				// a picture that a draw still holds when the strip is closed stays valid until it is released, and is freed with the last reference
				CoverImage held = rig.Thumbs.Get(3, 200);
				rig.Thumbs.Dispose();
				Assert.Equal(1, held.References);
				Assert.NotNull(held.Image);
				held.Release();
				Assert.Equal(0, held.References);
				Assert.Null(held.Image);
				Assert.Null(rig.Thumbs.Get(3, 200)); // closed: nothing is handed out
			}
			Gc.WaitForLive(live);
			Assert.Equal(live, PixelBlockLive());
		}

		private static long PixelBlockLive() => ComicRack.Mac.Tests.Thumbnails.Gc.Live;

		[Fact]
		public void Only_the_last_64_pictures_are_kept_and_an_evicted_one_is_released_and_made_again_when_it_is_asked_for()
		{
			using (var rig = new Rig())
			{
				CoverImage first = rig.Fetch(0); // one reference of ours, one of the strip
				Assert.Equal(2, first.References);
				for (int page = 1; page < CorePageThumbnails.Capacity; page++)
				{
					rig.Fetch(page).Release();
				}
				Assert.Equal(2, first.References); // 64 pictures: page 0 is still kept
				rig.Fetch(CorePageThumbnails.Capacity).Release(); // the 65th pushes the oldest out
				Assert.Equal(1, first.References); // only ours is left: the strip let go of it
				Assert.NotNull(first.Image); // and it still draws until we release it
				first.Release();
				Assert.Null(first.Image);

				int started = rig.Decoder.Started;
				Assert.Equal(CorePageThumbnails.Capacity + 1, started);
				// gone from the strip: it is asked for again, and the thumbnail service still has its pixels (no new decode), so it is back at once
				Assert.Null(rig.Thumbs.Get(0, 200));
				Assert.Equal(CorePageThumbnails.Capacity + 2, rig.Ready.Count);
				Assert.Equal(started, rig.Decoder.Started);
				CoverImage back = rig.Thumbs.Get(0, 200);
				Assert.NotNull(back);
				back.Release();

				// the use of a picture counts: the strip now holds pages 2 to 64 and 0 (page 1 went when page 0 came back); page 2 is the oldest, so a use of it
				// moves it to the end and the next picture that comes pushes out page 3 instead
				CoverImage touched = rig.Thumbs.Get(2, 200);
				Assert.NotNull(touched);
				touched.Release();
				rig.Fetch(CorePageThumbnails.Capacity + 1).Release();
				CoverImage still = rig.Thumbs.Get(2, 200);
				Assert.NotNull(still);
				still.Release();
				Assert.Null(rig.Thumbs.Get(3, 200));
			}
		}

		[Fact]
		public void A_page_that_cannot_be_made_stays_a_number_and_is_not_asked_for_again_and_again()
		{
			using (var rig = new Rig())
			{
				rig.Decoder.FailWith = r => new InvalidOperationException("damaged page");
				Assert.Null(rig.Thumbs.Get(4, 200));
				rig.Decoder.WaitForFinished(1);
				rig.Ui.PumpUntil(() => rig.Service.GetStats().InFlight == 0);
				rig.Ui.Pump();
				Assert.Empty(rig.Ready);
				for (int i = 0; i < 5; i++)
				{
					Assert.Null(rig.Thumbs.Get(4, 200));
				}
				rig.Ui.Pump();
				Assert.Equal(1, rig.Decoder.Started);
			}
		}

		[Fact]
		public void Closing_while_a_picture_is_still_being_made_raises_nothing_and_leaves_no_pixels_behind()
		{
			long live = Gc.BeginCount();
			using (var rig = new Rig())
			{
				rig.Decoder.Hold();
				Assert.Null(rig.Thumbs.Get(2, 200));
				rig.Decoder.WaitForStarted(1);
				rig.Thumbs.Dispose(); // the strip is closed (the reader closed) while the decode waits
				rig.Decoder.Open();
				rig.Decoder.WaitForFinished(1); // the decode ends on its own thread; nothing is delivered either way
				rig.Ui.Pump();
				Assert.Empty(rig.Ready);
				Assert.Null(rig.Thumbs.Get(2, 200));
				Assert.Equal(1, rig.Decoder.Started); // closed: no new request
			}
			Gc.WaitForLive(live);
			Assert.Equal(live, PixelBlockLive());
		}
	}
}
