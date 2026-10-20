using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Thumbnails;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Mac.Tests.Browser
{
	/// <summary>The grid on the headless platform: virtualization, recycling, thumbnail requests, selection and input, and frames saved as PNG.</summary>
	public class BookGridTests
	{
		private readonly ITestOutputHelper output;

		public BookGridTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		[Theory]
		[InlineData(14706)]
		[InlineData(100000)]
		public async Task Only_the_cells_around_the_viewport_exist_whatever_the_size_of_the_library(int count)
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness())
				{
					await h.Show(new BookCatalog(SyntheticItems.Make(count)));
					int columns = h.Grid.Columns;
					int rows = h.Grid.RowCount;
					output.WriteLine($"{count:N0} books: {columns} columns, {rows:N0} rows, cell {h.Grid.CellWidth} x {h.Grid.CellHeight}; viewport {h.Grid.Scroller.Viewport}; extent {h.Grid.Scroller.Extent}");
					Assert.True(columns >= 4, "columns " + columns);
					Assert.True(h.Grid.Scroller.Extent.Height > 1000 * 20, "the scroll extent must cover all rows");

					double viewRows = Math.Ceiling(h.Grid.Scroller.Viewport.Height / (h.Grid.CellHeight + BookGridView.Spacing));
					// Viewport plus one viewport of cache above and below, plus the partly visible rows.
					int bound = (int)((3 * viewRows + 4) * columns);
					int atTop = h.Grid.RealizedCount;
					output.WriteLine($"at the top: {atTop} realized cells, {h.Grid.CellControlCount} cell controls (bound {bound})");
					Assert.InRange(atTop, columns, bound);
					Assert.True(atTop < count / 50);

					// Scroll to the middle, the end, and through a few hundred positions: the number of cells does not grow.
					int maxRealized = atTop, maxControls = h.Grid.CellControlCount;
					foreach (int row in new[] { rows / 2, rows - 1, 0, rows / 3 }.Concat(Enumerable.Range(0, 300).Select(i => (int)((long)i * (rows - 1) / 300))))
					{
						h.ScrollToRow(row);
						maxRealized = Math.Max(maxRealized, h.Grid.RealizedCount);
						maxControls = Math.Max(maxControls, h.Grid.CellControlCount);
						Assert.True(h.Grid.RealizedCount >= 1, "no cell at row " + row);
						BookCell any = h.Grid.BoundCells().FirstOrDefault();
						Assert.NotNull(any);
					}
					output.WriteLine($"after 304 scroll positions: at most {maxRealized} realized cells and {maxControls} cell controls");
					Assert.InRange(maxRealized, columns, bound);
					Assert.InRange(maxControls, 1, bound * 2);

					// The cell at the end shows the last book.
					h.ScrollToRow(rows - 1);
					BookCell last = h.Grid.BoundCells().OrderBy(c => c.Index).Last();
					Assert.Equal(count - 1, last.Index);
					Assert.Equal(h.Model.Items[count - 1].Title, last.TitleText);
				}
			});
		}

		[Fact]
		public async Task Cells_show_the_title_and_series_and_number_and_the_selection_with_keyboard_and_mouse()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					var items = Enumerable.Range(1, 60).Select(i => new BookItem(null, "Album " + i, i <= 30 ? "Alpha" : "Beta", i.ToString(), new DateTime(2020, 1, 1).AddDays(i))).ToArray();
					await h.Show(new BookCatalog(items));
					BookCell first = h.Grid.CellAt(0);
					Assert.Equal("Album 1", first.TitleText);
					Assert.Equal("Alpha #1", first.SubtitleText);
					h.Capture("grid-1-text-only");

					var opened = new List<string>();
					h.Model.OpenBookRequested += (s, e) => opened.Add(e.Title);
					int columns = h.Grid.Columns;

					// Keyboard: arrows move by one and by a row; Enter opens; the selected cell is scrolled into view.
					h.Grid.Focus();
					h.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
					Assert.Equal(0, h.Model.SelectedIndex); // the first key selects the first book
					h.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
					Assert.Equal(1, h.Model.SelectedIndex);
					h.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
					Assert.Equal(1 + columns, h.Model.SelectedIndex);
					h.Window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
					Assert.Equal(columns, h.Model.SelectedIndex);
					h.Window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
					Assert.Equal(0, h.Model.SelectedIndex);
					h.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
					Assert.Equal(new[] { "Album 1" }, opened);

					h.Window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
					Assert.Equal(59, h.Model.SelectedIndex);
					h.Layout();
					Assert.True(h.Grid.Scroller.Offset.Y > 0, "End must scroll to the last book");
					Assert.NotNull(h.Grid.CellAt(59));
					Assert.True(h.Grid.CellAt(59).IsSelectedForTest);
					h.Capture("grid-2-selected-last");
					h.Window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
					h.Layout();
					Assert.Equal(0, h.Model.SelectedIndex);
					Assert.Equal(0, h.Grid.Scroller.Offset.Y);
					h.Window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
					Assert.True(h.Model.SelectedIndex >= columns, "Page Down moves at least a row");

					// Mouse: a click selects the cell under the pointer, a double click opens it.
					opened.Clear();
					h.Window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
					h.Layout();
					BookCell target = h.Grid.CellAt(2);
					Point center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), h.Window).Value;
					h.Window.MouseDown(center, MouseButton.Left);
					h.Window.MouseUp(center, MouseButton.Left);
					Assert.Equal(2, h.Model.SelectedIndex);
					Assert.Empty(opened);
					h.Window.MouseDown(center, MouseButton.Left);
					h.Window.MouseUp(center, MouseButton.Left);
					Assert.Equal(new[] { "Album 3" }, opened);
				}
			});
		}

		private static unsafe (byte r, byte g, byte b) PixelAt(WriteableBitmap frame, int x, int y)
		{
			using (var buffer = frame.Lock())
			{
				byte* p = (byte*)buffer.Address + y * buffer.RowBytes + x * 4;
				return buffer.Format == Avalonia.Platform.PixelFormat.Bgra8888 ? (p[2], p[1], p[0]) : (p[0], p[1], p[2]);
			}
		}

		[Fact]
		public void A_cover_image_is_freed_with_its_last_reference_and_cannot_be_retained_after_that()
		{
			var bitmap = new SkiaSharp.SKBitmap(4, 4);
			var block = PixelBlock.FromBitmap(bitmap);
			SkiaSharp.SKImage image = block.CreateImage();
			block.ReleaseOwner();
			var cover = new CoverImage(image);
			CoverImage second = cover.Retain();
			Assert.Same(cover, second);
			Assert.Equal(2, cover.References);
			cover.Release();
			Assert.False(block.IsFreed); // the image still lives
			cover.Release();
			Assert.True(block.IsFreed);
			Assert.Null(cover.Image);
			Assert.Throws<ObjectDisposedException>(() => cover.Retain());
		}

		[Fact]
		public void A_picture_is_fitted_into_its_box_with_its_aspect_ratio_and_centred()
		{
			Assert.Equal(new Rect(25, 0, 50, 100), CoverView.Fit(100, 100, 1, 2));
			Assert.Equal(new Rect(0, 25, 100, 50), CoverView.Fit(100, 100, 2, 1));
			Assert.Equal(new Rect(0, 0, 100, 100), CoverView.Fit(100, 100, 5, 5));
			Assert.Equal(default, CoverView.Fit(0, 100, 5, 5));
		}

		private static BookItem[] VirtualBooks(int count)
		{
			var items = new BookItem[count];
			for (int i = 0; i < count; i++)
			{
				ComicBook book = Books.Make("grid-" + i.ToString("00000"));
				book.Series = "Series " + (i / 12 + 1);
				book.Number = (i % 12 + 1).ToString();
				book.Title = "Album " + (i + 1);
				items[i] = BookItem.From(book);
			}
			return items;
		}

		private static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 15000)
		{
			var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (!condition())
			{
				if (DateTime.UtcNow > deadline)
				{
					throw new TimeoutException("Timed out waiting for " + what);
				}
				await Task.Delay(5);
				Dispatcher.UIThread.RunJobs();
			}
		}

		[Fact]
		public async Task A_cell_requests_its_cover_when_realized_and_cancels_when_recycled()
		{
			await Ui.Run(async () =>
			{
				Gc.Flush();
				long liveBefore = PixelBlock.LiveCount;
				var decoder = new FakeDecoder { Size = r => (100, 150) };
				decoder.Hold();
				using (var service = new ThumbnailService(new ThumbnailServiceOptions { Decoder = decoder }))
				using (var h = new GridHarness())
				{
					h.Model.Thumbnails = service;
					await h.Show(new BookCatalog(VirtualBooks(3000)));
					int atTop = h.Grid.RealizedCount;
					ThumbnailServiceStats s0 = service.GetStats();
					output.WriteLine($"at the top: {atTop} cells, {s0.Requests} requests, {s0.DecodesStarted} decodes started, {s0.Queued} queued");
					Assert.True(s0.Requests >= atTop, "every realized cell asks for its cover");
					Assert.True(h.Grid.BoundCells().All(c => c.IsRequestPending));
					decoder.WaitForStarted(4);

					// Scroll fast through 150 positions while the decoder is held: cells that are recycled withdraw their requests.
					int rows = h.Grid.RowCount;
					for (int i = 1; i <= 150; i++)
					{
						h.ScrollToRow(i * (rows - 20) / 150);
					}
					ThumbnailServiceStats s1 = service.GetStats();
					output.WriteLine($"after the scroll: {s1.Requests} requests in all, {s1.DroppedFromQueue} dropped from the queue, {s1.DecodesStarted} decodes started, {s1.Queued} queued, {s1.InFlight} in flight");
					Assert.True(s1.Requests > 1000, "the scroll asked for many covers: " + s1.Requests);
					Assert.True(s1.Queued <= h.Grid.RealizedCount + 8, "the queue holds only what is on screen: " + s1.Queued);
					Assert.True(s1.DroppedFromQueue > 900, "withdrawn requests leave the queue: " + s1.DroppedFromQueue);

					// Let the decodes go: the cells on screen get their covers, and nothing else is decoded.
					decoder.Open();
					await WaitFor(() => h.Grid.BoundCells().All(c => c.Cover.HasImage), "the covers of the visible cells");
					ThumbnailServiceStats s2 = service.GetStats();
					output.WriteLine($"covers shown: {s2.DecodesStarted} decodes started, {s2.DecodesCompleted} completed, {s2.DecodesCancelled} cancelled, for {s2.Requests} requests; {s2.CachedCount} cached");
					// A decode that was running when its cell was recycled is cancelled at once; the ones that finished are the visible cells'.
					Assert.True(decoder.Blocks.Length <= 4 + h.Grid.RealizedCount + 8, "pictures made: " + decoder.Blocks.Length);
					Assert.True(s2.DecodesCompleted <= 4 + h.Grid.RealizedCount + 8, "decodes completed: " + s2.DecodesCompleted);
					Assert.True(s2.DecodesCancelled > 100, "decodes cancelled: " + s2.DecodesCancelled);
					WriteableBitmap frame = h.Capture("grid-3-fake-covers");

					// The draw operation put each cover where its cell is: the pixel in the middle of a cell's cover is the colour of its book.
					int checkedCells = 0;
					foreach (BookCell cell in h.Grid.BoundCells())
					{
						Point middle = cell.Cover.TranslatePoint(new Point(cell.Cover.Bounds.Width / 2, cell.Cover.Bounds.Height / 2), h.Window).Value;
						if (middle.X < 0 || middle.Y < 0 || middle.X >= frame.PixelSize.Width || middle.Y >= frame.PixelSize.Height)
						{
							continue; // realized for the cache, outside the window
						}
						checkedCells++;
						var expected = FakeDecoder.ColorFor(Books.Location(cell.Item.Book));
						(byte r, byte g, byte b) = PixelAt(frame, (int)middle.X, (int)middle.Y);
						Assert.True(Math.Abs(r - expected.Red) <= 2 && Math.Abs(g - expected.Green) <= 2 && Math.Abs(b - expected.Blue) <= 2,
							$"cell {cell.Index}: frame pixel ({r},{g},{b}), cover colour ({expected.Red},{expected.Green},{expected.Blue})");
					}
					Assert.True(checkedCells >= 8, "cells checked in the frame: " + checkedCells);

					// A cell shows the cover of its own book: the fake decoder paints a colour per file.
					foreach (BookCell cell in h.Grid.BoundCells().Take(6))
					{
						Assert.Equal(FakeDecoder.ColorFor(Books.Location(cell.Item.Book)), cell.Cover.Image.Image.PeekPixels().GetPixelColor(1, 1));
					}

					// Going back to a place that was left: the covers come from the memory cache at once (no flash, no task).
					h.ScrollToRow(0);
					Assert.True(h.Grid.BoundCells().Take(5).All(c => c.Cover.HasImage || c.IsRequestPending));
					await WaitFor(() => h.Grid.BoundCells().All(c => c.Cover.HasImage), "the covers at the top");
					h.ScrollToRow(rows / 2);
					await WaitFor(() => h.Grid.BoundCells().All(c => c.Cover.HasImage), "the covers in the middle");
					h.ScrollToRow(0);
					Assert.True(h.Grid.BoundCells().Where(c => c.Index < 10).All(c => c.Cover.HasImage), "cached covers must be there in the same layout pass");

					// The library closes: the service goes first (as HostService does), the cells must cope and later let go of every picture.
					service.Dispose();
					h.ScrollToRow(rows / 4);
					h.Model.Thumbnails = null;
					h.Layout();
					h.Model.SetCatalog(BookCatalog.Empty);
					await h.Model.WhenIdle();
					h.Layout();
				}
				Gc.Flush();
				Assert.True(decoder.Blocks.All(b => b.IsFreed), "every picture the decoder made must be freed once the cells and the cache let go");
				Assert.True(PixelBlock.LiveCount <= liveBefore, $"live pixel blocks: {PixelBlock.LiveCount} (before the test: {liveBefore})");
			});
		}

		[Fact]
		public async Task A_cell_that_is_recycled_and_not_used_again_withdraws_its_request()
		{
			await Ui.Run(async () =>
			{
				var decoder = new FakeDecoder();
				decoder.Hold();
				using (var service = new ThumbnailService(new ThumbnailServiceOptions { Decoder = decoder }))
				using (var h = new GridHarness())
				{
					h.Model.Thumbnails = service;
					await h.Show(new BookCatalog(VirtualBooks(200)));
					int cells = h.Grid.RealizedCount;
					decoder.WaitForStarted(4);
					Assert.True(cells > 8);
					Assert.Equal(cells, service.GetStats().InFlight + service.GetStats().Queued);

					// The list becomes empty: every cell is recycled and none is bound again.
					h.Model.SetCatalog(BookCatalog.Empty);
					await h.Model.WhenIdle();
					h.Layout();
					Assert.Equal(0, h.Grid.RealizedCount);
					ThumbnailServiceStats s = service.GetStats();
					Assert.Equal(0, s.Queued);
					await WaitFor(() => service.GetStats().InFlight == 0, "the running decodes to be cancelled");
					s = service.GetStats();
					// Each request ended without a picture, either dropped while it waited or cancelled while it ran (a freed slot starts the next waiting job first).
					Assert.True(s.DroppedFromQueue + s.DecodesCancelled >= cells, $"dropped {s.DroppedFromQueue} + cancelled {s.DecodesCancelled} of {cells} requests");
					Assert.Equal(0, decoder.Blocks.Length); // nothing was decoded for cells that went away
				}
			});
		}

		[Fact]
		public async Task The_thumbnail_size_changes_the_cells_and_asks_again_only_when_the_cached_step_changes()
		{
			await Ui.Run(async () =>
			{
				var decoder = new FakeDecoder { Size = r => (r.Height * 2 / 3, r.Height) };
				using (var service = new ThumbnailService(new ThumbnailServiceOptions { Decoder = decoder }))
				using (var h = new GridHarness())
				{
					h.Model.Thumbnails = service;
					await h.Show(new BookCatalog(VirtualBooks(500)));
					await WaitFor(() => h.Grid.BoundCells().All(c => c.Cover.HasImage), "covers");
					double w1 = h.Grid.CellWidth, h1 = h.Grid.CellHeight;
					int columns1 = h.Grid.Columns;
					int requests1 = (int)service.GetStats().Requests;
					CoverImage picture1 = h.Grid.CellAt(0).Cover.Image;
					int height1 = h.Grid.CellAt(0).Cover.Image.Height;

					// 200 to 190 px: the same step (the cell is a little smaller, the picture is the same).
					h.Model.ThumbnailSize = 190;
					h.Layout();
					Assert.True(h.Grid.CellHeight < h1);
					Assert.Equal(height1, h.Grid.CellAt(0).Cover.Image.Height);
					Assert.Same(picture1, h.Grid.CellAt(0).Cover.Image); // not asked again, not replaced

					// 120 px: a smaller step, more columns, new requests; the old picture stays until the new one arrives.
					h.Model.ThumbnailSize = 120;
					h.Layout();
					Assert.True(h.Grid.CellWidth < w1);
					Assert.True(h.Grid.Columns > columns1, $"{h.Grid.Columns} columns at 120, {columns1} at 200");
					await WaitFor(() => h.Grid.BoundCells().All(c => c.Cover.HasImage && c.Cover.Image.Height < height1), "smaller covers");
					Assert.True(service.GetStats().Requests > requests1);
					h.Capture("grid-4-size-120");

					// The largest size is served at 512 px at most.
					h.Model.ThumbnailSize = BrowserViewModel.MaxThumbnailSize;
					h.Layout();
					await WaitFor(() => h.Grid.BoundCells().All(c => c.Cover.HasImage && c.Cover.Image.Height >= height1), "larger covers");
					Assert.True(h.Grid.CellAt(0).Cover.Image.Height <= ThumbnailSteps.MaxHeight);
					h.Capture("grid-5-size-360");
				}
			});
		}

		[Fact]
		public async Task A_search_filters_the_grid_and_scrolls_back_to_the_top()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness())
				{
					h.Model.SearchDelay = TimeSpan.Zero;
					await h.Show(new BookCatalog(SyntheticItems.Make(14706)));
					h.ScrollToRow(h.Grid.RowCount / 2);
					Assert.True(h.Grid.Scroller.Offset.Y > 1000);
					// A reload of the same library (after a scan) keeps the position.
					double before = h.Grid.Scroller.Offset.Y;
					h.Model.SetCatalog(new BookCatalog(h.Model.Items));
					await h.Model.WhenIdle();
					h.Layout();
					Assert.Equal(before, h.Grid.Scroller.Offset.Y);
					Assert.True(h.Grid.RealizedCount > 0);
					h.Model.SearchText = "dragon lune";
					await h.Model.WhenIdle();
					h.Layout();
					Assert.Equal(0, h.Grid.Scroller.Offset.Y);
					Assert.InRange(h.Model.Items.Length, 1, 14706 - 1);
					Assert.Equal(h.Model.Items[0].Title, h.Grid.CellAt(0).TitleText);
					Assert.All(h.Grid.BoundCells(), c => Assert.True(c.Item.TitleKey.Contains("dragon") || c.Item.SeriesKey.Contains("dragon")));
					Assert.Equal(h.Model.Items.Length, h.Grid.Repeater.ItemsSourceView.Count);
					h.Capture("grid-6-search");
				}
			});
		}
	}
}
