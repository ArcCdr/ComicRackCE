using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ComicRack.Mac.Host.Library;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using cYo.Projects.ComicRack.Plugins;

namespace ComicRack.Mac.Host.Scripting
{
	/// <summary>The arguments a hook is called with when a command is run by hand (<c>crcli script run</c>); the ones a hook does not use are ignored.</summary>
	public sealed class ScriptRunArguments
	{
		/// <summary>Only the command with this method name (default: every command of the file that has the hook).</summary>
		public string Method { get; set; }

		/// <summary>The list whose books are "the selected books" (<c>Books</c>, <c>Library</c>, <c>NewBooks</c>, <c>Editor</c>, <c>CreateBookList</c>, ...). Default: the whole library.</summary>
		public string List { get; set; }

		/// <summary><c>ParseComicPath</c>: the file path to propose values for.</summary>
		public string Path { get; set; }

		/// <summary><c>NetSearch</c>: what the search provider is asked (hint, text, limit; limit -1 asks for the generic link).</summary>
		public string Hint { get; set; } = string.Empty;

		public string Text { get; set; } = string.Empty;

		public int Limit { get; set; } = 10;

		/// <summary><c>CreateBookList</c>: the two arguments of the smart list editor.</summary>
		public string A { get; set; } = string.Empty;

		public string B { get; set; } = string.Empty;

		/// <summary><c>Shutdown</c>: whether the user closes the window (the scripts may then veto).</summary>
		public bool UserClosing { get; set; }

		/// <summary><c>ReaderResized</c>: the size of the reader.</summary>
		public int Width { get; set; } = 1024;

		public int Height { get; set; } = 768;
	}

	/// <summary>
	/// Runs the commands of one script file by hand, with the arguments the WinForms app passes for the hook (<c>ScriptUtility</c>, <c>MainForm</c>, <c>ComicBook.ParseFilePath</c>), and
	/// describes what came back. It is what <c>crcli script run</c> prints; the hooks that need a window (<c>ComicInfoUI</c>, <c>QuickOpenUI</c>,
	/// <c>DrawThumbnailOverlay</c>, <c>ConfigScript</c>) are refused with a message. The scripts run even when their command is switched off (<c>#@Enabled false</c>): asking for it is the switch.
	/// </summary>
	public static class ScriptRunner
	{
		private static readonly string[] BookHooks = { PluginEngine.ScriptTypeBooks, PluginEngine.ScriptTypeLibrary, PluginEngine.ScriptTypeNewBooks, PluginEngine.ScriptTypeEditor };

		/// <summary>The hooks <see cref="Run"/> can call.</summary>
		public static readonly string[] SupportedHooks =
		{
			PluginEngine.ScriptTypeBooks, PluginEngine.ScriptTypeLibrary, PluginEngine.ScriptTypeNewBooks, PluginEngine.ScriptTypeEditor, PluginEngine.ScriptTypeParseComicPath,
			PluginEngine.ScriptTypeCreateBookList, PluginEngine.ScriptTypeSearch, PluginEngine.ScriptTypeStartup, PluginEngine.ScriptTypeShutdown, PluginEngine.ScriptTypeBookOpened,
			PluginEngine.ScriptTypeReaderResized, PluginEngine.ScriptTypeComicInfoHtml, PluginEngine.ScriptTypeQuickOpenHtml,
		};

		/// <summary>
		/// Runs the commands of <paramref name="file"/> that have <paramref name="hook"/>. Script output (<c>print</c>) and the description of each result go to
		/// <paramref name="output"/>; problems go to <paramref name="error"/>. Returns 0, or 1 if there was no such command or a script raised an exception.
		/// </summary>
		public static int Run(MacHost host, string file, string hook, ScriptRunArguments args, TextWriter output, TextWriter error)
		{
			if (!SupportedHooks.Contains(hook, StringComparer.Ordinal))
			{
				error.WriteLine("The hook \"" + hook + "\" cannot be run without windows. Hooks: " + string.Join(", ", SupportedHooks));
				return 1;
			}
			args = args ?? new ScriptRunArguments();
			ScriptHost scripts = host.Scripts;
			scripts.AttachConsole(output);
			Command[] commands = scripts.LoadCommands(file)
				.Where(c => c.IsHook(hook) && (string.IsNullOrEmpty(args.Method) || string.Equals(((PythonCommand)c).Method, args.Method, StringComparison.Ordinal)))
				.ToArray();
			if (commands.Length == 0)
			{
				error.WriteLine("No command with the hook " + hook + (args.Method == null ? string.Empty : " and the method " + args.Method) + " in " + file + ".");
				return 1;
			}
			ComicBook[] books = SelectBooks(host, args.List);
			if (books == null)
			{
				error.WriteLine("No list named \"" + args.List + "\".");
				return 1;
			}
			int failures = 0;
			foreach (Command command in commands)
			{
				string name = command.Key ?? command.Name;
				try
				{
					object result = command.Invoke(Arguments(hook, command, books, args));
					output.WriteLine("script " + hook + " " + name + ": " + Describe(hook, result, books.Length));
				}
				catch (Exception ex)
				{
					failures++;
					error.WriteLine("script " + hook + " " + name + " failed: " + (ex.InnerException ?? ex).Message);
				}
			}
			output.Flush();
			return failures == 0 ? 0 : 1;
		}

		/// <summary>The books a Python smart list expression ("{Series} == 'Batman'") matches among the books of a list (default: the whole library), through <see cref="ComicBookExpressionMatcher"/>, as a smart list does.</summary>
		public static ComicBook[] MatchExpression(MacHost host, string expression, string list = null)
		{
			ComicBook[] books = SelectBooks(host, list) ?? throw new ArgumentException("No list named \"" + list + "\".", nameof(list));
			ComicBookExpressionMatcher matcher = new ComicBookExpressionMatcher { MatchValue = expression };
			return matcher.Match(books).ToArray();
		}

		/// <summary>The books of the named list (case does not matter), or all books of the library; null if there is no such list.</summary>
		public static ComicBook[] SelectBooks(MacHost host, string list)
		{
			ComicDatabase database = host.DatabaseManager.Database;
			if (string.IsNullOrEmpty(list))
			{
				return database.Books.ToArray();
			}
			ComicListItem item = LibraryLists.BuildTree(database, host.Scripts).SelectMany(n => n.Flatten())
				.Select(n => n.Item).FirstOrDefault(i => string.Equals(i.Name, list, StringComparison.OrdinalIgnoreCase));
			return item == null ? null : new ListEvaluator(scripts: host.Scripts).Evaluate(item).Books;
		}

		private static object[] Arguments(string hook, Command command, ComicBook[] books, ScriptRunArguments args)
		{
			switch (hook)
			{
				case PluginEngine.ScriptTypeParseComicPath:
					string path = args.Path ?? throw new InvalidOperationException("ParseComicPath needs --path <file>.");
					return new object[] { path, ComicNameInfo.FromFilePath(path) };
				case PluginEngine.ScriptTypeCreateBookList:
					return new object[] { books, args.A, args.B };
				case PluginEngine.ScriptTypeSearch:
					return new object[] { args.Hint, args.Text, args.Limit };
				case PluginEngine.ScriptTypeStartup:
					return new object[0];
				case PluginEngine.ScriptTypeShutdown:
					return new object[] { args.UserClosing };
				case PluginEngine.ScriptTypeBookOpened:
					return new object[] { books.FirstOrDefault() ?? throw new InvalidOperationException("BookOpened needs a book: the library is empty.") };
				case PluginEngine.ScriptTypeReaderResized:
					return new object[] { args.Width, args.Height };
				default:
					// Books, Library, NewBooks, Editor, ComicInfoHtml, QuickOpenHtml: the array of books
					return new object[] { books };
			}
		}

		private static string Describe(string hook, object result, int bookCount)
		{
			if (BookHooks.Contains(hook))
			{
				return "ran on " + bookCount + " books";
			}
			switch (hook)
			{
				case PluginEngine.ScriptTypeCreateBookList:
					ComicBook[] list = (result as IEnumerable<ComicBook>)?.ToArray() ?? new ComicBook[0];
					return list.Length + " of " + bookCount + " books" + (list.Length == 0 ? string.Empty : ": " + string.Join("; ", list.Take(5).Select(b => b.Caption)) + (list.Length > 5 ? "; ..." : string.Empty));
				case PluginEngine.ScriptTypeSearch:
					Dictionary<string, string> found = result as Dictionary<string, string>;
					return found == null ? "no result" : found.Count + " results" + (found.Count == 0 ? string.Empty : ": " + string.Join("; ", found.Take(5).Select(kv => kv.Key + " = " + kv.Value)));
				case PluginEngine.ScriptTypeShutdown:
					return "answer " + result;
				case PluginEngine.ScriptTypeParseComicPath:
					return "done";
				case PluginEngine.ScriptTypeComicInfoHtml:
				case PluginEngine.ScriptTypeQuickOpenHtml:
					string html = result as string;
					return html == null ? "no html" : html.Length.ToString(CultureInfo.InvariantCulture) + " characters of html";
				default:
					return result == null ? "done" : "returned " + (result is string s ? "\"" + s + "\"" : result is IEnumerable && !(result is string) ? "a list" : Convert.ToString(result, CultureInfo.InvariantCulture));
			}
		}
	}
}
