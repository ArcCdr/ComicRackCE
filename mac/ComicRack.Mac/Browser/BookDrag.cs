using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ComicRack.Mac.Services;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Browser
{
	/// <summary>One drag of the selected books out of the browser: where it started, with which press, and what it carries.</summary>
	public sealed class BookDragRequest
	{
		public BookDragRequest(Visual source, PointerPressedEventArgs trigger, IReadOnlyList<ComicBook> books, DragDropEffects effects)
		{
			Source = source;
			Trigger = trigger;
			Books = books;
			Effects = effects;
		}

		/// <summary>The control the drag starts from (the browser grid).</summary>
		public Visual Source { get; }

		/// <summary>The press that began the gesture (the operating system starts its drag session from it).</summary>
		public PointerPressedEventArgs Trigger { get; }

		/// <summary>The selected books, in grid order.</summary>
		public IReadOnlyList<ComicBook> Books { get; }

		/// <summary>What a target may do with them: <see cref="DragDropEffects.Copy"/>, and Move too once the shown list can be edited.</summary>
		public DragDropEffects Effects { get; }
	}

	/// <summary>Starts the operating system's drag session for a <see cref="BookDragRequest"/> (a test replaces it with one that only records).</summary>
	public interface IBookDragStarter
	{
		/// <summary>Runs the drag and gives the effect the target applied (<see cref="DragDropEffects.None"/> when it was dropped nowhere).</summary>
		Task<DragDropEffects> StartAsync(BookDragRequest request);
	}

	/// <summary>
	/// What the browser puts on the drag when books are dragged out of it (<c>ComicBrowserControl.itemView_ItemDrag</c>): the <see cref="ComicBookContainer"/>
	/// of the selected books for the targets inside the application (lists, P5.16c), and the files of the linked books for Finder and other applications
	/// (<c>SetFileDropList</c>).
	/// </summary>
	public static class BookDrag
	{
		/// <summary>The application's own format: a <see cref="ComicBookContainer"/> holding the dragged books (the original's <c>DataObject.SetData(comicBookContainer)</c>), valid inside this process.</summary>
		public static readonly DataFormat<ComicBookContainer> BooksFormat = DataFormat.CreateInProcessFormat<ComicBookContainer>("ComicRack.ComicBookContainer");

		/// <summary>The container with the books, as the original builds it.</summary>
		public static ComicBookContainer Container(IEnumerable<ComicBook> books)
		{
			var container = new ComicBookContainer();
			container.Books.AddRange(books);
			return container;
		}

		/// <summary>The files of the dragged books: the linked ones (<c>ComicBookContainer.GetBookFiles</c>).</summary>
		public static IReadOnlyList<string> Files(IEnumerable<ComicBook> books) => books.Where(b => b.IsLinked).Select(b => b.FilePath).ToList();

		/// <summary>
		/// The transfer for a drag. The system's drag session wants one pasteboard item per dragged item (a session with more drag images than pasteboard
		/// items is refused with an exception that ends the process), so the application's own format rides on the first file item, and when no file
		/// exists the books travel as one text item (<paramref name="text"/>) that carries it.
		/// </summary>
		public static DataTransfer Create(ComicBookContainer container, IEnumerable<IStorageItem> files, string text = null)
		{
			var transfer = new DataTransfer();
			bool carrier = false;
			foreach (IStorageItem file in files ?? Array.Empty<IStorageItem>())
			{
				DataTransferItem item = DataTransferItem.CreateFile(file);
				if (!carrier)
				{
					item.Set(BooksFormat, container);
					carrier = true;
				}
				transfer.Add(item);
			}
			if (!carrier)
			{
				DataTransferItem item = DataTransferItem.CreateText(text ?? string.Empty);
				item.Set(BooksFormat, container);
				transfer.Add(item);
			}
			return transfer;
		}

		/// <summary>The plain text that stands for the books when there are no files: one caption per line.</summary>
		public static string Text(IEnumerable<ComicBook> books) => string.Join(Environment.NewLine, books.Select(b => b.Caption));
	}

	/// <summary>The drag session of the application: Avalonia's <c>DragDrop</c> over the books and files of <see cref="BookDrag"/>.</summary>
	public sealed class AvaloniaBookDragStarter : IBookDragStarter
	{
		public async Task<DragDropEffects> StartAsync(BookDragRequest request)
		{
			IReadOnlyList<string> paths = BookDrag.Files(request.Books);
			IStorageProvider provider = TopLevel.GetTopLevel(request.Source)?.StorageProvider;
			var files = new List<IStorageItem>();
			if (provider != null)
			{
				foreach (string path in paths)
				{
					Task<IStorageFile> found = provider.TryGetFileFromPathAsync(new Uri(System.IO.Path.GetFullPath(path)));
					IStorageFile file = found.IsCompleted ? found.Result : await found; // on the desktop it is complete at once, and the drag must start inside the pointer event
					if (file != null)
					{
						files.Add(file);
					}
				}
			}
			DataTransfer transfer = BookDrag.Create(BookDrag.Container(request.Books), files, BookDrag.Text(request.Books));
			AppLog.Info($"Drag started: {request.Books.Count} books, {files.Count} files");
			DragDropEffects effect = await DragDrop.DoDragDropAsync(request.Trigger, transfer, request.Effects);
			AppLog.Info($"Drag ended: {effect}");
			return effect;
		}
	}
}
