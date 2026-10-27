using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ComicRack.Mac.Host.Scripting;
using cYo.Common.Net.Search;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>P4.3: the script host of an open <see cref="MacHost"/>, without a window: folders, command switches, the hooks wired into the Core, Startup and Shutdown, the environment a script sees.</summary>
	public class ScriptHostTests
	{
		private const string Hooks = @"
#@Name Edit books
#@Key EditBooks
#@Hook Books
def EditBooks(books):
	for b in books:
		b.Series = 'Edited ' + b.Series

#@Name Library only
#@Key LibraryOnly
#@Hook Library
def LibraryOnly(books):
	print 'library', len(books)

#@Name Switched off
#@Key SwitchedOff
#@Hook Books
#@Enabled false
def SwitchedOff(books):
	pass

#@Name Parse
#@Key Parse
#@Hook ParseComicPath
def Parse(path, proposed):
	proposed.Series = 'Parsed ' + proposed.Series
	proposed.Number = '99'

#@Name Search
#@Key Search
#@Hook NetSearch
def Search(hint, text, limit):
	d = Dictionary[str, str]()
	if limit == -1:
		d.Add('General', 'http://search.invalid/?q=')
	else:
		for n in range(1, limit + 1):
			d.Add('Result ' + str(n), 'http://search.invalid/?q=' + text)
	return d

#@Name Start
#@Key Start
#@Hook Startup
def Start():
	print 'started'

#@Name Stop
#@Key Stop
#@Hook Shutdown
def Stop(user_is_closing):
	print 'stopping', user_is_closing
	return not user_is_closing

#@Name Environment
#@Key Env
#@Hook Books
def Env(books):
	print 'version', ComicRack.App.ProductVersion
	print 'libs', ';'.join(ComicRack.LibraryPaths)
	print 'path', ComicRack.CommandPath == ScriptPath
	print 'text', ComicRack.Localize('NoSuchKey', 'Name', 'Default text')
	print 'theme', ComicRack.Theme.CurrentTheme
	print 'window', ComicRack.MainWindow, ComicRack.ComicDisplay
	print 'fields', 'Series' in ComicRack.App.GetComicFields().Values or 'Series' in ComicRack.App.GetComicFields().Keys

#@Name Book opened
#@Key BookOpenedEvent
#@Hook BookOpened
def BookOpenedEvent(book):
	print 'book opened', book.ShadowSeries

#@Name Reader resized
#@Key ReaderResizedEvent
#@Hook ReaderResized
def ReaderResizedEvent(width, height):
	print 'reader resized', width, height
";

		private static string Prelude => "import clr\nfrom System.Collections.Generic import *\n";

		private static string Folder(TempFolder temp, string name = "scripts") => ScriptTestSupport.WriteScripts(temp, name, ("hooks.py", Prelude + Hooks));

		[Fact]
		public void The_scripts_of_the_folders_are_read_and_the_commands_listed_by_hook()
		{
			using (var temp = new TempFolder())
			{
				string first = Folder(temp, "first");
				string second = ScriptTestSupport.WriteScripts(temp, "second", ("dup.py", "#@Name Duplicate\n#@Key EditBooks\n#@Hook Books\ndef Other(books):\n\tpass\n#@Name Second\n#@Key Second\n#@Hook Books\ndef Second(books):\n\tpass\n"));
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: new[] { first, second }))
				{
					ScriptHost scripts = host.Scripts;
					Assert.True(scripts.Enabled);
					Assert.True(scripts.IsStarted);
					string[] keys(string hook) => scripts.GetCommands(hook).Select(c => c.Key).ToArray();
					Assert.Equal(new[] { "EditBooks", "Env", "Second" }, keys(PluginEngine.ScriptTypeBooks).OrderBy(k => k));
					Assert.Equal(new[] { "LibraryOnly" }, keys(PluginEngine.ScriptTypeLibrary));
					Assert.Equal(new[] { "Parse" }, keys(PluginEngine.ScriptTypeParseComicPath));
					Assert.Equal(new[] { "BookOpenedEvent" }, keys(PluginEngine.ScriptTypeBookOpened));
					Assert.Equal(new[] { "ReaderResizedEvent" }, keys(PluginEngine.ScriptTypeReaderResized));
					// the switched off command exists but is not listed; the first folder wins a key that two folders use
					Assert.Contains(scripts.Engine.GetAllCommands(), c => c.Key == "SwitchedOff" && !c.Enabled);
					Assert.Equal("Edit books", scripts.Engine.GetAllCommands().Single(c => c.Key == "EditBooks").Name);
					// the first twelve enabled commands get Ctrl+F1.. (ScriptUtility's rule in PluginEngine.Initialize)
					Assert.All(scripts.GetCommands(PluginEngine.ScriptTypeBooks), c => Assert.NotEqual(System.Windows.Forms.Keys.None, c.ShortCutKeys));
				}
			}
		}

		[Fact]
		public void Folders_that_do_not_exist_are_skipped_and_the_user_folder_is_read()
		{
			using (var temp = new TempFolder())
			{
				MacPaths paths = temp.Paths;
				Directory.CreateDirectory(Path.Combine(paths.DataFolder, "Scripts", "sub"));
				File.WriteAllText(Path.Combine(paths.DataFolder, "Scripts", "sub", "own.py"), "#@Name Own\n#@Hook Books\ndef Own(books):\n\tpass\n");
				using (MacHost host = MacHost.Open(new MacHostOptions { Paths = paths, ScriptFolders = ScriptFolders.For(paths, new[] { Path.Combine(temp.Path, "missing") }) }))
				{
					// the scripts next to the program (the test project gets them like crcli does) come first, then the user's own
					string[] keys = host.Scripts.GetCommands(PluginEngine.ScriptTypeBooks).Select(c => c.Key).ToArray();
					Assert.Contains("CommitProposed", keys);
					Assert.Equal("Own", keys.Last());
				}
				var folders = ScriptFolders.For(paths);
				Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Scripts"), folders.Bundled);
				Assert.Equal(Path.Combine(paths.DataFolder, "Scripts"), folders.User);
			}
		}

		[Fact]
		public void With_scripting_off_nothing_is_read_and_no_hook_is_wired()
		{
			using (var temp = new TempFolder())
			{
				using (MacHost host = ScriptTestSupport.Open(temp, scripting: false, scriptFolders: Folder(temp)))
				{
					Assert.False(host.Scripts.Enabled);
					Assert.Empty(host.Scripts.Engine.GetAllCommands());
					Assert.Empty(host.Scripts.GetCommands(PluginEngine.ScriptTypeBooks));
					Assert.Null(ComicBookPluginMatcher.PluginEngine);
					Assert.False(host.Scripts.Invoke(PluginEngine.ScriptTypeStartup));
					Assert.False(host.Scripts.HasListCommand("anything"));
					Assert.Empty(host.Scripts.SearchEngines);
				}
			}
		}

		[Fact]
		public void The_scripting_preference_decides_when_the_option_does_not()
		{
			using (var temp = new TempFolder())
			{
				var settings = new MacSettings();
				settings.Preferences.Scripting = false;
				using (MacHost host = MacHost.Open(new MacHostOptions { Paths = temp.Paths, Settings = settings, ScriptFolders = ScriptFolders.Only(Folder(temp)) }))
				{
					Assert.False(host.Scripts.Enabled);
				}
				using (MacHost host = MacHost.Open(new MacHostOptions { Paths = temp.Paths, Settings = settings, Scripting = true, ScriptFolders = ScriptFolders.Only(Folder(temp)) }))
				{
					Assert.True(host.Scripts.Enabled);
					Assert.NotEmpty(host.Scripts.Engine.GetAllCommands());
				}
				Assert.True(new AppPreferences().Scripting, "scripting is on by default, as in the original");
			}
		}

		[Fact]
		public void The_saved_switches_of_the_commands_are_applied_and_written_back()
		{
			using (var temp = new TempFolder())
			{
				string dir = Folder(temp);
				using (MacHost host = ScriptTestSupport.Open(temp, pluginStates: "-EditBooks,+SwitchedOff", scriptFolders: dir))
				{
					string[] books = host.Scripts.GetCommands(PluginEngine.ScriptTypeBooks).Select(c => c.Key).OrderBy(k => k).ToArray();
					Assert.Equal(new[] { "Env", "SwitchedOff" }, books);
					host.Scripts.Engine.GetAllCommands().Single(c => c.Key == "Env").Enabled = false;
					host.Scripts.StoreCommandStates();
					string saved = host.Settings.Preferences.PluginsStates;
					Assert.Contains("-Env", saved);
					Assert.Contains("+SwitchedOff", saved);
				}
			}
		}

		[Fact]
		public void A_books_script_runs_on_the_books_it_is_given()
		{
			using (var temp = new TempFolder())
			{
				string dir = Folder(temp);
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
				{
					ComicBook[] books = { ScriptTestSupport.Book("Alpha", "1"), ScriptTestSupport.Book("Beta", "1") };
					ScriptTestSupport.Add(host, books);
					Command edit = host.Scripts.GetCommands(PluginEngine.ScriptTypeBooks).Single(c => c.Key == "EditBooks");
					edit.Invoke(new object[] { books });
					Assert.Equal(new[] { "Edited Alpha", "Edited Beta" }, books.Select(b => b.Series));

					var output = new StringWriter();
					Assert.Equal(0, ScriptRunner.Run(host, Path.Combine(dir, "hooks.py"), "Library", null, output, new StringWriter()));
					Assert.Contains("library 2", output.ToString());
					Assert.Contains("script Library LibraryOnly: ran on 2 books", output.ToString());
				}
			}
		}

		[Fact]
		public void A_ParseComicPath_script_changes_the_values_proposed_for_a_path_and_is_taken_out_when_the_host_closes()
		{
			using (var temp = new TempFolder())
			{
				string dir = Folder(temp);
				ComicBook before = ScriptTestSupport.Book(null, null, 0, "/comics/Batman 007.cbz");
				Assert.Equal("Batman", before.ShadowSeries);
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
				{
					ComicBook book = new ComicBook { FilePath = "/comics/Batman 007.cbz" };
					Assert.Equal("Parsed Batman", book.ShadowSeries);
					Assert.Equal("99", book.ShadowNumber);
				}
				ComicBook after = new ComicBook { FilePath = "/comics/Batman 007.cbz" };
				Assert.Equal("Batman", after.ShadowSeries);
			}
		}

		[Fact]
		public void A_NetSearch_script_becomes_a_search_provider()
		{
			using (var temp = new TempFolder())
			{
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: Folder(temp)))
				{
					INetSearch search = Assert.Single(host.Scripts.SearchEngines);
					Assert.Equal("Search", search.Name);
					SearchResult[] results = search.Search("hint", "batman", 3).ToArray();
					Assert.Equal(new[] { "Result 1", "Result 2", "Result 3" }, results.Select(r => r.Name));
					Assert.All(results, r => Assert.Equal("http://search.invalid/?q=batman", r.Result));
					Assert.Equal("http://search.invalid/?q=", search.GenericSearchLink("hint", "batman"));
				}
			}
		}

		[Fact]
		public void Startup_scripts_run_on_request_and_a_shutdown_script_can_stop_the_user_from_closing()
		{
			using (var temp = new TempFolder())
			{
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: Folder(temp)))
				{
					var console = new StringWriter();
					host.Scripts.AttachConsole(console);
					host.Scripts.RunStartup();
					Assert.Contains("started", console.ToString());

					Assert.False(host.Scripts.RunShutdown(userClosing: true), "the script answers False to a user who closes the window: the close is stopped");
					Assert.True(host.Scripts.RunShutdown(userClosing: false), "the program may close for any other reason");
					Assert.Contains("stopping True", console.ToString());
					Assert.Contains("stopping False", console.ToString());
				}
			}
		}

		[Fact]
		public void A_failing_script_is_logged_and_does_not_stop_the_caller()
		{
			using (var temp = new TempFolder())
			{
				string dir = ScriptTestSupport.WriteScripts(temp, "bad", ("bad.py", "#@Name Bad\n#@Hook Startup\ndef Bad():\n\traise ValueError('on purpose')\n"));
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
				{
					Assert.False(host.Scripts.Invoke(PluginEngine.ScriptTypeStartup));
					host.Scripts.RunStartup();
					Assert.True(host.Scripts.RunShutdown(true));
				}
			}
		}

		[Fact]
		public void A_script_sees_the_environment_of_the_original()
		{
			using (var temp = new TempFolder())
			{
				string dir = Folder(temp);
				var settings = new MacSettings();
				settings.Preferences.ScriptingLibraries = "/lib/one;/lib/two\r\n;";
				using (MacHost host = MacHost.Open(new MacHostOptions { Paths = temp.Paths, Settings = settings, ScriptFolders = ScriptFolders.Only(dir) }))
				{
					var output = new StringWriter();
					Assert.Equal(0, ScriptRunner.Run(host, Path.Combine(dir, "hooks.py"), "Books", new ScriptRunArguments { Method = "Env" }, output, new StringWriter()));
					string text = output.ToString();
					Assert.Contains("version " + ScriptHost.ProductVersion, text);
					Assert.Contains("libs /lib/one;/lib/two", text);
					Assert.Contains("path True", text);
					Assert.Contains("text Default text", text);
					Assert.Contains("theme Default", text);
					Assert.Contains("window None None", text);
					Assert.Contains("fields True", text);
				}
			}
		}

		[Fact]
		public void The_reader_hooks_are_called_with_the_arguments_of_the_original()
		{
			using (var temp = new TempFolder())
			{
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: Folder(temp)))
				{
					var console = new StringWriter();
					host.Scripts.AttachConsole(console);
					Assert.True(host.Scripts.Invoke(PluginEngine.ScriptTypeBookOpened, ScriptTestSupport.Book("A", "1")));
					Assert.True(host.Scripts.Invoke(PluginEngine.ScriptTypeReaderResized, 800, 600));
					string[] lines = console.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
					Assert.Equal(new[] { "book opened A", "reader resized 800 600" }, lines);
				}
			}
		}

		[Fact]
		public void The_product_version_is_the_one_of_the_windows_program()
		{
			string info = File.ReadAllText(Path.Combine(ScriptTestSupport.RepoRoot(), "ComicRack", "Properties", "AssemblyInfo.cs"));
			Assert.Contains("[assembly: AssemblyInformationalVersion(\"" + ScriptHost.ProductVersion + "\")]", info);
		}

		[Fact]
		public void A_second_host_starts_clean_after_the_first_one_closed()
		{
			using (var temp = new TempFolder())
			{
				string dir = Folder(temp);
				for (int round = 0; round < 2; round++)
				{
					using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
					{
						Assert.Same(host.Scripts.Engine, ComicBookPluginMatcher.PluginEngine);
						Assert.Single(host.Scripts.SearchEngines);
					}
					Assert.Null(ComicBookPluginMatcher.PluginEngine);
				}
			}
		}
	}
}
