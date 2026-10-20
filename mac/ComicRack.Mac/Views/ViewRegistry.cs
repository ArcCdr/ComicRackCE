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

		/// <summary>The view model of the library window, for the views that show a part of it (set by the app once it exists; null in tests).</summary>
		public static ViewModels.MainViewModel Library { get; set; }

		private static readonly Dictionary<string, Func<Window>> factories = new Dictionary<string, Func<Window>>(StringComparer.OrdinalIgnoreCase);

		static ViewRegistry()
		{
			factories[MainWindowName] = null;
			Register("ChromeGallery", () => new ChromeGalleryWindow());
			Register("BookGrid", () => new BookGridWindow());
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
