using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ComicRack.Mac.Host.Scripting;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>
	/// P4.3 and P4.8: the headless hooks of the scripts that ship with the program (<c>ComicRack/Output/Scripts</c>), run from an exact copy of the unmodified files
	/// (their <c>clr.AddReference*</c> lines resolve through the assembly identity of the facades, P4.8). The UI hooks are not run here (P4.10).
	/// </summary>
	public class BundledSampleTests
	{
		private static Dictionary<string, Command> Sample(MacHost host, string dir)
		{
			var commands = new List<Command>();
			commands.AddRange(host.Scripts.LoadCommands(Path.Combine(dir, "Sample.py")));
			commands.AddRange(host.Scripts.LoadCommands(Path.Combine(dir, "Sample.xml")));
			return commands.Cast<PythonCommand>().ToDictionary(c => c.Method, c => (Command)c);
		}

		[Fact]
		public void Every_bundled_script_has_its_commands_found_and_initialised()
		{
			using (var temp = new TempFolder())
			{
				string dir = ScriptTestSupport.CopyOfBundledScripts(Path.Combine(temp.Path, "Scripts"));
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
				{
					Dictionary<string, Command> all = host.Scripts.Engine.GetAllCommands().ToDictionary(c => c.Key, c => c);
					foreach (string key in new[] { "Autonumber", "CommitProposed", "NewComics", "SearchAndReplace", "RefreshView", "SaveCSVList", "RenameBookFiles", "SetComicInfoDirty",
						"GetBooksWith", "DummySearch", "DummyHtmlInfoPanel", "WebLinkInfoPanel", "ComicRackUserForum", "AutoadjustTwoPageMode", "ShowStartupMessage", "ShowShutdownMessage", "FadeReadThumbnails",
						"DummyUIInfoPanel", "DummyUIQuickOpen", "ParseComicPath", "BookHasBeenOpened" })
					{
						Assert.True(all.ContainsKey(key), "no command with the key " + key + " in " + string.Join(", ", all.Keys.OrderBy(k => k)));
					}
					Assert.Contains("Books", all["Autonumber"].Hook);
					Assert.Contains("NewBooks", all["NewComics"].Hook);
					Assert.Contains("Library", all["SaveCSVList"].Hook);
					// the images named by #@Image are loaded next to the script
					Assert.NotNull(all["Autonumber"].CommandImage);
					Assert.NotNull(all["RefreshView"].CommandImage);
					// Sample.xml: the two commands that are declared outside the script
					Assert.Equal("[Code Sample] Parse Book Path", all["ParseComicPath"].Name);
					Assert.False(all["ParseComicPath"].Enabled);
					Assert.Equal(PluginEngine.ScriptTypeBookOpened, all["BookHasBeenOpened"].Hook);
					// ConfigScript: the configuration script of BookHasBeenOpened (same key) hangs off that command and is no command of its own
					Assert.NotNull(all["BookHasBeenOpened"].Configure);
					Assert.Equal(PluginEngine.ScriptTypeConfig, all["BookHasBeenOpened"].Configure.Hook);
				}
			}
		}

		[Fact]
		public void The_headless_hooks_of_Sample_py_run()
		{
			using (var temp = new TempFolder())
			{
				string dir = ScriptTestSupport.CopyOfBundledScripts(Path.Combine(temp.Path, "Scripts"));
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
				{
					Dictionary<string, Command> sample = Sample(host, dir);

					// ParseComicPath (declared in Sample.xml): replaces the proposed values
					ComicNameInfo info = new ComicNameInfo { Series = "Batman", Volume = 3, Number = "7", Count = 12, Year = 2011 };
					sample["ParseComicPath"].Invoke(new object[] { "/comics/Batman 007.cbz", info });
					Assert.Equal("-Batman", info.Series);
					Assert.Equal("This is a sample title", info.Title);
					Assert.Equal(1, info.Volume);
					Assert.Equal("1.5", info.Number);
					Assert.Equal(56, info.Count);
					Assert.Equal(2000, info.Year);

					// CreateBookList: a smart list script
					ComicBook[] books = { ScriptTestSupport.Book("Alpha", "1"), ScriptTestSupport.Book("Beta", "2"), ScriptTestSupport.Book("Alpine", "3") };
					var picked = (IEnumerable<ComicBook>)sample["GetBooksWith"].Invoke(new object[] { books, "Al", string.Empty });
					Assert.Equal(new[] { "Alpha", "Alpine" }, picked.Select(b => b.Series));

					// NetSearch
					var general = (Dictionary<string, string>)sample["DummySearch"].Invoke(new object[] { "hint", "text", -1 });
					var three = (Dictionary<string, string>)sample["DummySearch"].Invoke(new object[] { "hint", "x", 4 });
					Assert.Equal(new[] { "General" }, general.Keys);
					Assert.Equal(3, three.Count);

					// ComicInfoHtml
					string table = (string)sample["DummyHtmlInfoPanel"].Invoke(new object[] { new[] { books[0] } });
					Assert.StartsWith("<table>", table);
					Assert.Contains("Alpha", table);
					string link = (string)sample["WebLinkInfoPanel"].Invoke(new object[] { new[] { new ComicBook { Series = "A", Web = "http://example.org/x" } } });
					Assert.Equal("!http://example.org/x", link);

					// QuickOpenHtml
					Assert.StartsWith("!http", (string)sample["ComicRackUserForum"].Invoke(new object[] { new ComicBook[0] }));
				}
			}
		}

		[Fact]
		public void The_headless_hooks_of_Sample_py_run_through_the_runner_with_the_arguments_of_the_original()
		{
			using (var temp = new TempFolder())
			{
				string dir = ScriptTestSupport.CopyOfBundledScripts(Path.Combine(temp.Path, "Scripts"));
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
				{
					ScriptTestSupport.Add(host, ScriptTestSupport.Book("Alpha", "1"), ScriptTestSupport.Book("Beta", "2"), ScriptTestSupport.Book("Alpine", "3"));
					string Run(string hook, ScriptRunArguments args, string file = "Sample.py")
					{
						var output = new StringWriter();
						var error = new StringWriter();
						int code = ScriptRunner.Run(host, Path.Combine(dir, file), hook, args, output, error);
						Assert.True(code == 0, hook + ": " + output + error);
						return output.ToString();
					}
					Assert.Contains("2 of 3 books: Alpha", Run("CreateBookList", new ScriptRunArguments { A = "Al" }));
					Assert.Contains("3 results", Run("NetSearch", new ScriptRunArguments { Text = "x", Limit = 4 }));
					Assert.Contains("1 results: General = http://google.com/search?q=", Run("NetSearch", new ScriptRunArguments { Limit = -1 }));
					Assert.Contains("characters of html", Run("ComicInfoHtml", new ScriptRunArguments { Method = "DummyHtmlInfoPanel" }));
					Assert.Contains("characters of html", Run("QuickOpenHtml", null));
					Assert.Contains("done", Run("ParseComicPath", new ScriptRunArguments { Path = "/comics/Batman 007.cbz" }, "Sample.xml"));
				}
			}
		}

		[Fact]
		public void CommitProposed_asks_through_the_user_interface_and_writes_the_proposed_values()
		{
			using (var temp = new TempFolder())
			{
				string dir = ScriptTestSupport.CopyOfBundledScripts(Path.Combine(temp.Path, "Scripts"));
				var ui = new HeadlessScriptUi { QuestionAnswer = 0 };
				using (MacHost host = ScriptTestSupport.Open(temp, ui: ui, scriptFolders: dir))
				{
					ComicBook book = new ComicBook { FilePath = "/comics/Batman 007.cbz" };
					ScriptTestSupport.Add(host, book);
					Assert.Equal(string.Empty, book.Series ?? string.Empty);
					string script = Path.Combine(dir, "CommitProposed.py");

					Assert.Equal(0, ScriptRunner.Run(host, script, "Books", null, new StringWriter(), new StringWriter()));
					Assert.Equal(string.Empty, book.Series ?? string.Empty);
					Assert.Contains(ui.Requests, r => r.StartsWith("AskQuestion: Do you really want to make the proposed values permanent?"));

					ui.QuestionAnswer = 1;
					Assert.Equal(0, ScriptRunner.Run(host, script, "Books", null, new StringWriter(), new StringWriter()));
					Assert.Equal("Batman", book.Series);
				}
			}
		}

		[Fact]
		public void A_script_that_marks_the_info_as_dirty_changes_the_books()
		{
			using (var temp = new TempFolder())
			{
				string dir = ScriptTestSupport.CopyOfBundledScripts(Path.Combine(temp.Path, "Scripts"));
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
				{
					ComicBook book = ScriptTestSupport.Book("Alpha", "1");
					ScriptTestSupport.Add(host, book);
					Assert.False(book.ComicInfoIsDirty);
					var output = new StringWriter();
					// the command is switched off in the file (#@Enabled false); running it by hand ignores that
					Assert.Equal(0, ScriptRunner.Run(host, Path.Combine(dir, "OtherScripts.py"), "Books", new ScriptRunArguments { Method = "SetComicInfoDirty" }, output, new StringWriter()));
					Assert.True(book.ComicInfoIsDirty);
					Assert.Contains("ran on 1 books", output.ToString());
				}
			}
		}

		[Fact]
		public void The_runner_reports_what_it_cannot_do()
		{
			using (var temp = new TempFolder())
			{
				string dir = ScriptTestSupport.CopyOfBundledScripts(Path.Combine(temp.Path, "Scripts"));
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
				{
					string sample = Path.Combine(dir, "Sample.py");
					var error = new StringWriter();
					Assert.Equal(1, ScriptRunner.Run(host, sample, "DrawThumbnailOverlay", null, new StringWriter(), error));
					Assert.Contains("cannot be run without windows", error.ToString());
					error = new StringWriter();
					Assert.Equal(1, ScriptRunner.Run(host, sample, "Books", null, new StringWriter(), error));
					Assert.Contains("No command with the hook Books", error.ToString());
					error = new StringWriter();
					Assert.Equal(1, ScriptRunner.Run(host, sample, "ParseComicPath", new ScriptRunArguments { Path = "x.cbz", Method = "NoSuchMethod" }, new StringWriter(), error));
					Assert.Contains("the method NoSuchMethod", error.ToString());
					error = new StringWriter();
					Assert.Equal(1, ScriptRunner.Run(host, sample, "CreateBookList", new ScriptRunArguments { List = "No such list" }, new StringWriter(), error));
					Assert.Contains("No list named", error.ToString());
				}
			}
		}
	}
}
