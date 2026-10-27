using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicRack.Mac.Host.Scripting;
using ComicRack.Mac.Services;
using ComicRack.Mac.ViewModels;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Display;

namespace ComicRack.Mac.Scripting
{
	/// <summary>
	/// What scripts ask of the windows (<see cref="IScriptUi"/>), answered by the library window: the questions (<c>MainForm.AskQuestion</c>), the Info dialog, opening and selecting books, the
	/// reader as <c>ComicRack.ComicDisplay</c>. A call may come from a worker thread (a command, a hook) or from the UI thread; <see cref="UiCall"/> brings it to the UI thread and waits.
	/// Restart and the scan of the watched folders have no command in the application yet; they are logged and answered as the headless host does until P5.10b and P5.19b add them.
	/// </summary>
	public sealed class AppScriptUi : IScriptUi
	{
		private readonly MainViewModel vm;
		private readonly ILibraryDialogs dialogs;
		private readonly ReaderComicDisplay display;

		public AppScriptUi(MainViewModel vm, ILibraryDialogs dialogs)
		{
			this.vm = vm ?? throw new ArgumentNullException(nameof(vm));
			this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
			display = new ReaderComicDisplay(() => vm.Reader);
		}

		/// <summary>No WinForms window exists to hand a script; dialogs that a script makes are owned by the active window (the script UI layer, P4.9 and later, provides the form classes).</summary>
		public IWin32Window MainWindow => null;

		public IComicDisplay ComicDisplay => display;

		public int AskQuestion(string question, string buttonText, string optionText)
		{
			QuestionAnswer answer = UiCall.Run(() => dialogs.AskQuestionAsync(new QuestionRequest { Question = question ?? string.Empty, OkText = buttonText, OptionText = optionText }));
			if (answer == null || !answer.Accepted)
			{
				return 0;
			}
			return answer.Option ? 2 : 1;
		}

		public void ShowComicInfo(IReadOnlyList<ComicBook> books)
		{
			if (books == null || books.Count == 0)
			{
				return;
			}
			UiCall.Run(async () =>
			{
				await vm.ShowInfoAsync(books);
				return true;
			});
		}

		public bool EditNewBook(ComicBook book)
		{
			return UiCall.Run(() => dialogs.ShowBookInfoAsync(vm.CreateBookInfoRequest(book, new[] { book })));
		}

		public void Restart() => AppLog.Info("A script asked for a restart (Restart is P5.10b).");

		public void ScanFolders() => AppLog.Info("A script asked to scan the book folders (Scan Book Folders is P5.19b).");

		/// <summary><c>IBrowser.OpenNextComic</c> (+1), <c>OpenPrevComic</c> (-1), <c>OpenRandomComic</c> (0): the book after, before or any other than the open one in the shown list.</summary>
		public bool OpenNextComic(int direction)
		{
			return UiCall.Invoke(() =>
			{
				ComicBook[] list = vm.Browser.Items.Where(i => i.Book != null).Select(i => i.Book).ToArray();
				if (list.Length == 0 || vm.Tabs == null)
				{
					return false;
				}
				ComicBook open = vm.Tabs.Current?.Book;
				int at = Array.FindIndex(list, b => ReferenceEquals(b, open));
				int target = direction == 0 ? new Random().Next(list.Length) : at + direction;
				if (target < 0 || target >= list.Length || (direction != 0 && at < 0 && direction < 0))
				{
					return false;
				}
				if (!list[target].IsLinked)
				{
					return false;
				}
				return vm.Tabs.Open(list[target], OpenBookOptions.None) != null;
			});
		}

		public void SelectComics(IEnumerable<ComicBook> books)
		{
			ComicBook first = books?.FirstOrDefault();
			if (first != null)
			{
				UiCall.Invoke(() => vm.Browser.SelectBook(first));
			}
		}

		public bool OpenBook(ComicBook book, bool inNewSlot, int page)
		{
			return UiCall.Invoke(() =>
			{
				if (vm.Tabs == null || book == null)
				{
					return false;
				}
				ReaderSlot slot = vm.Tabs.Open(book, inNewSlot ? OpenBookOptions.InNewTab : OpenBookOptions.None);
				if (slot != null && page > 0 && slot.Model != null)
				{
					_ = slot.Model.GoToAsync(page);
				}
				return slot != null;
			});
		}

		public bool OpenFile(string file, bool inNewSlot, int page)
		{
			ComicBook book = vm.CreateBookForFile(file);
			return book != null && OpenBook(book, inNewSlot, page);
		}

		public bool IsOpen(ComicBook book) => UiCall.Invoke(() => vm.Tabs != null && vm.Tabs.IsOpen(book));
	}
}
