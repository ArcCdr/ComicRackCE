using System;
using System.Collections.Generic;
using System.Windows.Forms;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Display;

namespace ComicRack.Mac.Host.Scripting
{
	/// <summary>
	/// Everything the scripting host needs from a user interface: the calls of <c>IApplication</c>, <c>IBrowser</c> and <c>IOpenBooksManager</c> that show a window or
	/// act on the open reader and the browser (what <c>ComicRack/MainForm.cs</c> does for them in the WinForms app). The app supplies an implementation;
	/// a headless run (crcli, tests, the first start before a window exists) uses <see cref="HeadlessScriptUi"/>, which answers with defaults.
	/// </summary>
	public interface IScriptUi
	{
		/// <summary>The main window as the script's <c>ComicRack.MainWindow</c> (the owner of the dialogs it opens), or null.</summary>
		IWin32Window MainWindow { get; }

		/// <summary>The reader as the script's <c>ComicRack.ComicDisplay</c>, or null when there is none.</summary>
		IComicDisplay ComicDisplay { get; }

		/// <summary><c>IApplication.AskQuestion</c> (<c>MainForm.AskQuestion</c>): 0 = cancelled, 1 = OK, 2 = OK with the option checked.</summary>
		int AskQuestion(string question, string buttonText, string optionText);

		/// <summary><c>IApplication.ShowComicInfo</c>: the Info dialog for one book, or the multiple books dialog.</summary>
		void ShowComicInfo(IReadOnlyList<ComicBook> books);

		/// <summary>The dialog of <c>IApplication.AddNewBook(true)</c>: false if the user cancelled (the book is then not added).</summary>
		bool EditNewBook(ComicBook book);

		/// <summary><c>IApplication.Restart</c>.</summary>
		void Restart();

		/// <summary><c>IApplication.ScanFolders</c> (a full scan of the watched folders).</summary>
		void ScanFolders();

		/// <summary><c>IBrowser.OpenNextComic</c> (+1), <c>OpenPrevComic</c> (-1) and <c>OpenRandomComic</c> (0); false if there was nothing to open.</summary>
		bool OpenNextComic(int direction);

		/// <summary><c>IBrowser.SelectComics</c>.</summary>
		void SelectComics(IEnumerable<ComicBook> books);

		/// <summary><c>IOpenBooksManager.Open</c>.</summary>
		bool OpenBook(ComicBook book, bool inNewSlot, int page);

		/// <summary><c>IOpenBooksManager.OpenFile</c>.</summary>
		bool OpenFile(string file, bool inNewSlot, int page);

		/// <summary><c>IOpenBooksManager.IsOpen</c>.</summary>
		bool IsOpen(ComicBook book);

		/// <summary>
		/// <c>FormUtility.FindActiveService&lt;T&gt;()</c> for scripts (<c>cYo.Common.Windows.Forms.FormUtility</c>): the object of the active window that implements
		/// <paramref name="service"/> (the Windows program finds it in the control tree of the active form: <c>IRefreshDisplay</c> is the browser or the reader), or null.
		/// </summary>
		object FindActiveService(Type service);
	}

	/// <summary>
	/// The user interface of a run without windows: nothing is shown, every question is answered with <see cref="QuestionAnswer"/> (0, cancelled, unless a test or a
	/// command line option chooses otherwise), no book is open, and every request that needs a window is remembered in <see cref="Requests"/>.
	/// </summary>
	public sealed class HeadlessScriptUi : IScriptUi
	{
		private readonly List<string> requests = new List<string>();

		/// <summary>The answer of <see cref="AskQuestion"/>: 0 cancelled (default), 1 OK, 2 OK with the option.</summary>
		public int QuestionAnswer { get; set; }

		/// <summary>The result of <see cref="EditNewBook"/>: false (cancelled) by default.</summary>
		public bool AcceptNewBook { get; set; }

		/// <summary>One line per call that needed a window ("AskQuestion: ...", "Restart", "ScanFolders", ...), oldest first.</summary>
		public IReadOnlyList<string> Requests
		{
			get
			{
				lock (requests)
				{
					return requests.ToArray();
				}
			}
		}

		public IWin32Window MainWindow => null;

		public IComicDisplay ComicDisplay => null;

		private void Note(string text)
		{
			lock (requests)
			{
				requests.Add(text);
			}
		}

		public int AskQuestion(string question, string buttonText, string optionText)
		{
			Note("AskQuestion: " + question);
			return QuestionAnswer;
		}

		public void ShowComicInfo(IReadOnlyList<ComicBook> books) => Note("ShowComicInfo: " + (books?.Count ?? 0) + " books");

		public bool EditNewBook(ComicBook book)
		{
			Note("EditNewBook");
			return AcceptNewBook;
		}

		public void Restart() => Note("Restart");

		public void ScanFolders() => Note("ScanFolders");

		public bool OpenNextComic(int direction)
		{
			Note("OpenNextComic: " + direction);
			return false;
		}

		public void SelectComics(IEnumerable<ComicBook> books) => Note("SelectComics");

		public bool OpenBook(ComicBook book, bool inNewSlot, int page)
		{
			Note("OpenBook: " + book?.FilePath);
			return false;
		}

		public bool OpenFile(string file, bool inNewSlot, int page)
		{
			Note("OpenFile: " + file);
			return false;
		}

		public bool IsOpen(ComicBook book) => false;

		/// <summary>The services this window offers to <c>FormUtility.FindActiveService</c> by the type a script asks for (a test fills it; none by default).</summary>
		public Dictionary<Type, object> Services { get; } = new Dictionary<Type, object>();

		public object FindActiveService(Type service)
		{
			Note("FindActiveService: " + service?.Name);
			lock (Services)
			{
				return service != null && Services.TryGetValue(service, out object found) ? found : null;
			}
		}
	}
}
