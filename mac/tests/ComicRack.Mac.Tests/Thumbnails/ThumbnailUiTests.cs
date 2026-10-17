using System;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Thumbnails;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Thumbnails
{
	/// <summary>The default delivery path: <see cref="UiDispatch"/> on the real (headless) Avalonia UI thread.</summary>
	public class ThumbnailUiTests
	{
		[Fact]
		public async Task Results_and_cancellations_complete_on_the_ui_thread_whichever_thread_asks()
		{
			await Ui.Run(async () =>
			{
				Assert.True(UiDispatch.IsUiThread);
				var decoder = new FakeDecoder();
				decoder.Hold();
				using (var service = new ThumbnailService(new ThumbnailServiceOptions { Decoder = decoder })) // the default Post is UiDispatch.Post
				{
					bool? resultOnUi = null;
					bool? cancelOnUi = null;
					var cts = new CancellationTokenSource();

					// Asked from pool threads; the continuations are attached before anything can complete (the decoder is held).
					Task<ThumbnailResult> result = null;
					Task<ThumbnailResult> cancelled = null;
					await Task.Run(() =>
					{
						result = service.RequestResult(Books.Make("ui-a"), 200, CancellationToken.None);
						cancelled = service.RequestResult(Books.Make("ui-b"), 200, cts.Token);
					});
					Task markResult = result.ContinueWith(_ => resultOnUi = UiDispatch.IsUiThread, TaskContinuationOptions.ExecuteSynchronously);
					Task markCancel = cancelled.ContinueWith(_ => cancelOnUi = UiDispatch.IsUiThread, TaskContinuationOptions.ExecuteSynchronously);

					await Task.Run(() => cts.Cancel()); // cancelled from a pool thread
					await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
					await markCancel;
					decoder.Open();
					ThumbnailResult r = await result;
					await markResult;

					Assert.True(resultOnUi);
					Assert.True(cancelOnUi);
					Assert.False(r.IsPlaceholder);
					Assert.Equal(FakeDecoder.ColorFor("/virtual/ui-a.cbz"), r.Image.PeekPixels().GetPixelColor(1, 1));
					r.Image.Dispose();

					// A cache hit is synchronous, in the caller.
					Task<ThumbnailResult> hit = service.RequestResult(Books.Make("ui-a"), 200, CancellationToken.None);
					Assert.True(hit.IsCompletedSuccessfully);
					(await hit).Image.Dispose();
				}
			});
		}

		[Fact]
		public async Task Request_can_be_awaited_from_the_ui_thread_like_a_grid_cell_does()
		{
			await Ui.Run(async () =>
			{
				using (var service = new ThumbnailService(new ThumbnailServiceOptions { Decoder = new FakeDecoder() }))
				using (var cts = new CancellationTokenSource())
				{
					using (SKImage image = await service.Request(Books.Make("cell"), 200, cts.Token))
					{
						Assert.True(UiDispatch.IsUiThread); // the continuation of the await
						Assert.Equal(100, image.Width);
					}
					cts.Cancel();
					await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.Request(Books.Make("cell2"), 200, cts.Token));
				}
			});
		}
	}
}
