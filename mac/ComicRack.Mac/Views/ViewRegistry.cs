using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The views that the hidden option <c>--show NAME</c> can open after launch (chrome contact sheets and e2e cases show every view
	/// of the app this way, with fixture data: <c>mac/tools/chrome-sheet.sh</c>, chrome-guidelines section 9). The name is the type name of the
	/// WinForms original where there is one (<c>PreferencesDialog</c>, <c>ComicBookDialog</c>), so the view spec
	/// <c>mac/specs/views/NAME.md</c> and the sheet row carry the same name. <c>MainWindow</c> is the library window itself and needs no
	/// window of its own. Every task that adds a window, dialog or view calls <see cref="Register"/> for it (in the static
	/// constructor below) and lists the name in its report.
	/// </summary>
	public static class ViewRegistry
	{
		/// <summary>The name of the library window, which is always open.</summary>
		public const string MainWindowName = "MainWindow";

		/// <summary>The main window (set by the app once it exists; null in tests): the views that live in it, such as the reader, return it.</summary>
		public static Window MainWindow { get; set; }

		/// <summary>The view model of the library window, for the views that show a part of it (set by the app once it exists; null in tests).</summary>
		public static ViewModels.MainViewModel Library { get; set; }

		private static readonly Dictionary<string, Func<Window>> factories = new Dictionary<string, Func<Window>>(StringComparer.OrdinalIgnoreCase);

		static ViewRegistry()
		{
			factories[MainWindowName] = null;
			Register("ChromeGallery", () => new ChromeGalleryWindow());
			Register("BookGrid", () => new BookGridWindow());
			Register("Reader", OpenFirstBook);
			Register("ComicBookDialog", InfoOfFirstBook);
			Register("MultipleComicBooksDialog", MultiEditOfFirstBooks);
		}

		/// <summary>
		/// The Info dialog of the original (<c>--show ComicBookDialog</c>): the dialog of the first book of the library with the list as its Previous and Next books, shown over the
		/// library window without blocking it (the contact sheet and the e2e cases look at it and close it).
		/// </summary>
		private static Window InfoOfFirstBook()
		{
			ViewModels.MainViewModel library = Library ?? throw new InvalidOperationException("The library window is not open.");
			ComicRack.Mac.Browser.BookItem[] items = library.Browser.Items;
			if (items.Length == 0 || items[0].Book == null)
			{
				throw new InvalidOperationException("The library has no book to show (use --fixture-library).");
			}
			var dialog = new ComicBookDialog(library.CreateBookInfoRequest(items[0].Book, items.Where(i => i.Book != null).Select(i => i.Book).ToArray()));
			// COMICRACK_MAC_INFO_TAB=tabPlot (or tabDetails, tabCatalog, tabCustom ...) opens the dialog on that tab: the contact sheet shoots every tab this way
			string tab = Environment.GetEnvironmentVariable("COMICRACK_MAC_INFO_TAB");
			if (!string.IsNullOrEmpty(tab))
			{
				dialog.ShowTab(tab);
			}
			return dialog;
		}

		/// <summary>
		/// The multi edit dialog of the original (<c>--show MultipleComicBooksDialog</c>): the dialog over the first books of the library (at most 12; COMICRACK_MAC_MULTI_BOOKS sets the number),
		/// shown over the library window without blocking it. It reads the books as they are and writes nothing until OK.
		/// </summary>
		private static Window MultiEditOfFirstBooks()
		{
			ViewModels.MainViewModel library = Library ?? throw new InvalidOperationException("The library window is not open.");
			int count = int.TryParse(Environment.GetEnvironmentVariable("COMICRACK_MAC_MULTI_BOOKS"), out int n) && n > 1 ? n : 12;
			var books = library.Browser.Items.Where(i => i.Book != null).Select(i => i.Book).Take(count).ToArray();
			if (books.Length < 2)
			{
				throw new InvalidOperationException("The library has fewer than two books to edit (use --fixture-library).");
			}
			return new MultipleComicBooksDialog(library.CreateMultipleBooksRequest(books));
		}

		/// <summary>
		/// The reader of the original (<c>--show Reader</c>): the first book of the library in a tab of the main window, opened through the same path as a double
		/// click. Used by the contact sheet <c>chrome-sheet.sh P5.32a MainWindow Reader</c> with a fixture library. The window (the main window, which hosts the reader)
		/// is open already.
		/// </summary>
		private static Window OpenFirstBook()
		{
			ViewModels.MainViewModel library = Library ?? throw new InvalidOperationException("The library window is not open.");
			if (library.Browser.Items.Length == 0)
			{
				throw new InvalidOperationException("The library has no book to read (use --fixture-library).");
			}
			library.Browser.SelectedIndex = 0;
			library.Browser.OpenSelected();
			Services.ReaderManager readers = library.Readers as Services.ReaderManager;
			if (readers == null || readers.OpenCount == 0)
			{
				throw new InvalidOperationException("The library has no book to read (use --fixture-library).");
			}
			return MainWindow ?? throw new InvalidOperationException("The library window is not open.");
		}

		/// <summary>Adds (or replaces) a view. The factory runs on the UI thread and returns a window that is not shown yet.</summary>
		public static void Register(string name, Func<Window> factory)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				throw new ArgumentException("A view needs a name.", nameof(name));
			}
			factories[name] = factory ?? throw new ArgumentNullException(nameof(factory));
		}

		/// <summary>Removes a view again (a test that registered a view of its own); <c>MainWindow</c> cannot be removed.</summary>
		public static bool Unregister(string name)
		{
			return name != null && !string.Equals(name, MainWindowName, StringComparison.OrdinalIgnoreCase) && factories.Remove(Canonical(name) ?? name);
		}

		/// <summary>The registered names, sorted.</summary>
		public static IReadOnlyList<string> Names => factories.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

		public static bool IsKnown(string name) => name != null && factories.ContainsKey(name);

		/// <summary>The registered spelling of a name (the case of the registration), or null.</summary>
		public static string Canonical(string name) => name == null ? null : factories.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

		/// <summary>
		/// Opens the view over <paramref name="owner"/> (without an owner if that is null) and returns its window, or null for the library window (which is the owner).
		/// Throws <see cref="ArgumentException"/> for an unknown name.
		/// </summary>
		public static Window Show(string name, Window owner)
		{
			if (!IsKnown(name))
			{
				throw new ArgumentException("Unknown view " + name + ". Known: " + string.Join(", ", Names) + ".", nameof(name));
			}
			Func<Window> factory = factories[Canonical(name)];
			if (factory == null)
			{
				return null;
			}
			Window window = factory();
			if (window.IsVisible)
			{
				return window; // opened by the factory itself (the reader)
			}
			if (owner != null)
			{
				window.Show(owner);
			}
			else
			{
				window.Show();
			}
			return window;
		}
	}
}
