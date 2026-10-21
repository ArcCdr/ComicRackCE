using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using Xunit;

namespace ComicRack.Mac.Tests.Browser
{
	/// <summary>
	/// RV6: dragging the selected books out of the grid (ItemView.OnItemDrag, ComicBrowserControl.itemView_ItemDrag), BV.12. The operating system's drag
	/// session is replaced by a recorder; what is tested is when the drag starts, what it carries and what happens to the selection.
	/// </summary>
	public class BookDragOutTests
	{
		private sealed class Recorder : IBookDragStarter
		{
			public readonly List<BookDragRequest> Requests = new List<BookDragRequest>();

			public Task<DragDropEffects> StartAsync(BookDragRequest request)
			{
				Requests.Add(request);
				return Task.FromResult(DragDropEffects.None);
			}
		}

		private static Point CenterOf(GridHarness h, int index)
		{
			BookCell cell = h.Grid.CellAt(index);
			Assert.NotNull(cell);
			return cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), h.Window).Value;
		}

		private static void Click(GridHarness h, int index, RawInputModifiers modifiers = RawInputModifiers.None)
		{
			Point at = CenterOf(h, index);
			h.Window.MouseDown(at, MouseButton.Left, modifiers);
			h.Window.MouseUp(at, MouseButton.Left, modifiers);
		}

		private static void DragTo(GridHarness h, Point from, double dx, double dy)
		{
			h.Window.MouseMove(new Point(from.X + dx, from.Y + dy), RawInputModifiers.LeftMouseButton);
		}

		private static int[] Selected(GridHarness h) => h.Model.Selection.SelectedIndices().ToArray();

		private static async Task<(GridHarness, Recorder)> Open(int count = 30)
		{
			var h = new GridHarness(900, 600);
			await h.Show(new BookCatalog(RubberBandTests.Books(count)));
			var recorder = new Recorder();
			h.Grid.DragStarter = recorder;
			return (h, recorder);
		}

		[Fact]
		public async Task Pressing_a_book_and_moving_further_than_the_drag_size_drags_the_selection_out()
		{
			await Ui.Run(async () =>
			{
				(GridHarness h, Recorder drags) = await Open();
				using (h)
				{
					Point at = CenterOf(h, 3);
					h.Window.MouseDown(at, MouseButton.Left);
					Assert.Equal(new[] { 3 }, Selected(h)); // an unselected book is selected by the press
					DragTo(h, at, 3, 0);
					DragTo(h, at, 4, 4); // still within the drag size (4)
					Assert.Empty(drags.Requests);
					DragTo(h, at, 5, 0);
					BookDragRequest request = Assert.Single(drags.Requests);
					Assert.Same(h.Grid, request.Source);
					Assert.NotNull(request.Trigger);
					Assert.Equal(DragDropEffects.Copy, request.Effects);
					Assert.Equal(new[] { "/nowhere/004.cbz" }, request.Books.Select(b => b.FilePath));
					DragTo(h, at, 40, 10);
					Assert.Single(drags.Requests); // once per gesture
					h.Window.MouseUp(new Point(at.X + 40, at.Y + 10), MouseButton.Left);
					Assert.Equal(new[] { 3 }, Selected(h));
				}
			});
		}

		[Fact]
		public async Task Dragging_a_selected_book_takes_the_whole_selection_and_does_not_collapse_it()
		{
			await Ui.Run(async () =>
			{
				(GridHarness h, Recorder drags) = await Open();
				using (h)
				{
					Click(h, 1);
					Click(h, 5, RawInputModifiers.Meta);
					Click(h, 2, RawInputModifiers.Meta);
					Assert.Equal(new[] { 1, 2, 5 }, Selected(h));
					Point at = CenterOf(h, 5);
					h.Window.MouseDown(at, MouseButton.Left);
					Assert.Equal(new[] { 1, 2, 5 }, Selected(h)); // a press on a selected book waits for the release
					DragTo(h, at, 0, 12);
					BookDragRequest request = Assert.Single(drags.Requests);
					Assert.Equal(new[] { "/nowhere/002.cbz", "/nowhere/003.cbz", "/nowhere/006.cbz" }, request.Books.Select(b => b.FilePath)); // in grid order
					h.Window.MouseUp(new Point(at.X, at.Y + 12), MouseButton.Left);
					Assert.Equal(new[] { 1, 2, 5 }, Selected(h)); // the click that was waiting is dropped

					// without a drag the release is the click: the selection collapses to that book
					h.Window.MouseDown(at, MouseButton.Left);
					h.Window.MouseUp(at, MouseButton.Left);
					Assert.Equal(new[] { 5 }, Selected(h));
					Assert.Single(drags.Requests);
				}
			});
		}

		[Fact]
		public async Task A_press_on_the_empty_area_or_with_the_other_button_or_a_hover_drags_nothing()
		{
			await Ui.Run(async () =>
			{
				(GridHarness h, Recorder drags) = await Open();
				using (h)
				{
					BookCell cell = h.Grid.CellAt(2);
					Point corner = cell.TranslatePoint(new Point(1, 1), h.Window).Value;
					h.Window.MouseDown(corner, MouseButton.Left); // the empty area: a band, not a drag
					DragTo(h, corner, 30, 30);
					h.Window.MouseUp(new Point(corner.X + 30, corner.Y + 30), MouseButton.Left);

					Point at = CenterOf(h, 4);
					h.Window.MouseDown(at, MouseButton.Right);
					h.Window.MouseMove(new Point(at.X + 30, at.Y), RawInputModifiers.RightMouseButton);
					h.Window.MouseUp(new Point(at.X + 30, at.Y), MouseButton.Right);

					h.Window.MouseMove(CenterOf(h, 6)); // no button
					h.Window.MouseMove(new Point(CenterOf(h, 6).X + 30, CenterOf(h, 6).Y));
					Assert.Empty(drags.Requests);
				}
			});
		}

		[Fact]
		public async Task A_double_click_opens_the_book_and_does_not_start_a_drag()
		{
			await Ui.Run(async () =>
			{
				(GridHarness h, Recorder drags) = await Open();
				using (h)
				{
					var opened = new List<string>();
					h.Model.OpenBookRequested += (s, e) => opened.Add(e.Title);
					Point at = CenterOf(h, 2);
					h.Window.MouseDown(at, MouseButton.Left);
					h.Window.MouseUp(at, MouseButton.Left);
					h.Window.MouseDown(at, MouseButton.Left); // a second press at the same place is a double click
					DragTo(h, at, 30, 0);
					h.Window.MouseUp(new Point(at.X + 30, at.Y), MouseButton.Left);
					Assert.Equal(new[] { "Book 003" }, opened);
					Assert.Empty(drags.Requests);
				}
			});
		}

		[Fact]
		public void The_drag_carries_the_books_for_the_application_and_the_files_of_the_linked_books()
		{
			var linked = new ComicBook { FilePath = "/nowhere/a.cbz" };
			var other = new ComicBook { FilePath = "/nowhere/b.cbz" };
			var fileless = new ComicBook();
			var books = new[] { linked, fileless, other };
			Assert.Equal(new[] { "/nowhere/a.cbz", "/nowhere/b.cbz" }, BookDrag.Files(books));

			ComicBookContainer container = BookDrag.Container(books);
			Assert.Equal(3, container.Books.Count);
			// no file at all: one text item stands for the books and carries the application's format
			Avalonia.Input.DataTransfer transfer = BookDrag.Create(container, null, "text");
			DataTransferItem item = Assert.Single(transfer.Items.OfType<DataTransferItem>());
			Assert.Same(container, item.TryGetRaw(BookDrag.BooksFormat));
			Assert.Contains(BookDrag.BooksFormat, transfer.Formats);
		}

		[Fact]
		public async Task The_transfer_has_one_item_per_file_and_the_books_ride_on_the_first()
		{
			await Ui.Run(async () =>
			{
				// the system's drag session throws (and the process ends) when the items of the pasteboard and the drag images differ in number:
				// the application's own format must not be an item of its own
				(GridHarness h, Recorder _) = await Open(4);
				using (h)
				using (var temp = new TempDir())
				{
					var storage = Avalonia.Controls.TopLevel.GetTopLevel(h.Grid)?.StorageProvider;
					Assert.NotNull(storage);
					var items = new List<Avalonia.Platform.Storage.IStorageItem>();
					foreach (string name in new[] { "a.cbz", "b.cbz" })
					{
						string path = temp.Combine(name);
						System.IO.File.WriteAllText(path, "x");
						items.Add(await storage.TryGetFileFromPathAsync(new Uri(path)));
					}
					Assert.All(items, Assert.NotNull);
					ComicBookContainer container = BookDrag.Container(new[] { new ComicBook { FilePath = "/nowhere/a.cbz" }, new ComicBook { FilePath = "/nowhere/b.cbz" } });
					Avalonia.Input.DataTransfer transfer = BookDrag.Create(container, items, "text");
					DataTransferItem[] made = transfer.Items.OfType<DataTransferItem>().ToArray();
					Assert.Equal(2, made.Length);
					Assert.Same(container, made[0].TryGetRaw(BookDrag.BooksFormat));
					Assert.Null(made[1].TryGetRaw(BookDrag.BooksFormat));
					Assert.All(made, i => Assert.Contains(DataFormat.File, i.Formats));
				}
			});
		}
	}
}
