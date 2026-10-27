using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using ComicRack.Cli.Fixtures;
using ComicRack.Mac.Host.Scripting;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins.Automation;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>P4.3: <c>ComicRack.App</c>, <c>ComicRack.Browser</c> and <c>ComicRack.OpenBooks</c> over the host, and what a headless run answers for the parts that need a window.</summary>
	public class ScriptApplicationTests
	{
		[Fact]
		public void The_library_calls_work_on_the_open_database()
		{
			using (var temp = new TempFolder())
			using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: ScriptTestSupport.TestScripts))
			{
				IApplication app = host.Scripts.Application;
				ComicBook one = ScriptTestSupport.Book("Alpha", "1");
				ComicBook two = ScriptTestSupport.Book("Beta", "2");
				ScriptTestSupport.Add(host, one, two);
				Assert.Equal(2, app.GetLibraryBooks().Count());

				// AddNewBook without a dialog adds an empty book with the time it was added
				ComicBook added = app.AddNewBook(false);
				Assert.NotNull(added);
				Assert.True(added.AddedTime > DateTime.Now.AddMinutes(-1));
				Assert.Equal(3, host.DatabaseManager.Database.Books.Count);

				Assert.True(app.RemoveBook(two));
				Assert.Equal(new[] { one, added }.OrderBy(b => b.Id), app.GetLibraryBooks().OrderBy(b => b.Id));

				IDictionary<string, string> fields = app.GetComicFields();
				Assert.Contains("Series", fields.Keys.Concat(fields.Values));
			}
		}

		[Fact]
		public void A_headless_run_answers_the_calls_that_need_a_window_with_defaults_and_remembers_them()
		{
			using (var temp = new TempFolder())
			{
				var ui = new HeadlessScriptUi();
				using (MacHost host = ScriptTestSupport.Open(temp, ui: ui, scriptFolders: ScriptTestSupport.TestScripts))
				{
					ScriptHost scripts = host.Scripts;
					Assert.Same(ui, scripts.Ui);
					Assert.Equal(0, scripts.Application.AskQuestion("Sure?", "Yes", "Also"));
					Assert.Null(scripts.Application.AddNewBook(true));
					Assert.Empty(host.DatabaseManager.Database.Books);
					scripts.Application.ShowComicInfo(new[] { ScriptTestSupport.Book("A", "1") });
					scripts.Application.ScanFolders();
					scripts.Application.Restart();
					Assert.False(scripts.Environment.Browser.OpenNextComic());
					Assert.False(scripts.Environment.Browser.OpenRandomComic());
					Assert.False(scripts.Environment.OpenBooks.IsOpen(ScriptTestSupport.Book("A", "1")));
					Assert.False(scripts.Environment.OpenBooks.OpenFile("/x.cbz", true, 0));
					Assert.Null(scripts.Environment.MainWindow);
					Assert.Null(scripts.Environment.ComicDisplay);
					Assert.Equal(new[]
					{
						"AskQuestion: Sure?", "EditNewBook", "ShowComicInfo: 1 books", "ScanFolders", "Restart", "OpenNextComic: 1", "OpenNextComic: 0", "OpenFile: /x.cbz",
					}, ui.Requests);

					ui.QuestionAnswer = 2;
					ui.AcceptNewBook = true;
					Assert.Equal(2, scripts.Application.AskQuestion("Sure?", "Yes", "Also"));
					Assert.NotNull(scripts.Application.AddNewBook(true));
				}
			}
		}

		[Fact]
		public void The_user_interface_can_be_replaced_after_the_scripts_were_read()
		{
			using (var temp = new TempFolder())
			using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: ScriptTestSupport.TestScripts))
			{
				IApplication app = host.Scripts.Application;
				IPluginEnvironmentProbe probe = new IPluginEnvironmentProbe(host);
				Assert.Equal(0, app.AskQuestion("q", "b", "o"));
				host.Scripts.Ui = new HeadlessScriptUi { QuestionAnswer = 1 };
				Assert.Equal(1, app.AskQuestion("q", "b", "o"));
				Assert.Equal(1, probe.EnvironmentOfACommand().App.AskQuestion("q", "b", "o"));
				host.Scripts.Ui = null; // back to a headless one
				Assert.Equal(0, app.AskQuestion("q", "b", "o"));
			}
		}

		private sealed class IPluginEnvironmentProbe
		{
			private readonly MacHost host;

			public IPluginEnvironmentProbe(MacHost host)
			{
				this.host = host;
			}

			public cYo.Projects.ComicRack.Plugins.IPluginEnvironment EnvironmentOfACommand()
			{
				return host.Scripts.Engine.GetAllCommands().First().Environment;
			}
		}

		[Fact]
		public void A_book_file_gives_pages_thumbnails_and_info_through_the_application()
		{
			using (var temp = new TempFolder())
			{
				var env = new FixtureEnvironment { FixturesRoot = Path.Combine(temp.Path, "fixtures"), PrintDiskFree = false, SevenZip = null };
				string dir = Path.Combine(env.FixturesRoot, "small");
				var output = new StringWriter();
				Assert.Equal(0, FixtureCommands.Run(new[] { "make", dir, "--books", "1", "--pages", "3" }, output, output, env));
				string file = Directory.GetFiles(dir, "*.cbz").Single();
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: ScriptTestSupport.TestScripts))
				{
					IApplication app = host.Scripts.Application;
					ComicBook book = app.GetBook(file);
					Assert.NotNull(book);
					Assert.Equal(file, book.FilePath);
					book.RefreshInfoFromFile(); // GetBook is the original's Create(file, AddToTemporary): the info is read when asked for
					Assert.Equal(3, book.PageCount);
					Assert.Empty(host.DatabaseManager.Database.Books); // AddToTemporary: not in the library

					using (Bitmap page = app.GetComicPage(book, 1))
					{
						Assert.NotNull(page);
						Assert.True(page.Width > 100 && page.Height > page.Width);
					}
					using (Bitmap thumb = app.GetComicThumbnail(book, 0))
					{
						Assert.NotNull(thumb);
						Assert.True(thumb.Width > 10);
					}
					// a page or a book that cannot be read never raises an exception (the Core answers with its error page, or the call answers null)
					Assert.Null(Record.Exception(() => app.GetComicPage(book, 99)?.Dispose()));
					Assert.Null(Record.Exception(() => app.GetComicPage(new ComicBook { FilePath = "/nowhere/x.cbz" }, 0)?.Dispose()));

					// the same from a script
					string scripts = ScriptTestSupport.WriteScripts(temp, "app", ("app.py", "#@Name App\n#@Hook Books\ndef AppCalls(books):\n\tb = ComicRack.App.GetBook(r'" + file + "')\n\tp = ComicRack.App.GetComicPage(b, 0)\n\tb.RefreshInfoFromFile()\n\tprint 'pages', b.PageCount, p.Width > 0\n"));
					var printed = new StringWriter();
					Assert.Equal(0, ScriptRunner.Run(host, Path.Combine(scripts, "app.py"), "Books", null, printed, new StringWriter()));
					Assert.Contains("pages 3 True", printed.ToString());
				}
			}
		}

		[Fact]
		public void Reading_another_database_gives_its_books()
		{
			using (var temp = new TempFolder())
			{
				string other = Path.Combine(temp.Path, "other.xml");
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: ScriptTestSupport.TestScripts))
				{
					ScriptTestSupport.Add(host, ScriptTestSupport.Book("Alpha", "1"), ScriptTestSupport.Book("Beta", "2"));
					host.SaveDatabase();
					File.Copy(host.DatabaseFile, other);
					Assert.Equal(2, host.Scripts.Application.ReadDatabaseBooks(other).Count());
					Assert.Equal(2, host.Scripts.Environment.ReadDatabaseBooks(other).Count());
				}
			}
		}
	}
}
