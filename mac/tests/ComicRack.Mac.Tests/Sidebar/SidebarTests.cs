using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Tests.Browser;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using cYo.Projects.ComicRack.Plugins;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Mac.Tests.Sidebar
{
	/// <summary>The lists of the library (M2.2c): the tree, the evaluation, lists that need scripts, the sidebar view model and the window.</summary>
	public class SidebarTests
	{
		private readonly ITestOutputHelper output;

		public SidebarTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		private static ComicBook Book(string series, string number, int readPages = 0)
		{
			return new ComicBook { Series = series, Number = number, Title = series + " " + number, FilePath = "/nowhere/" + series + number + ".cbz" };
		}

		private static ComicSmartListItem SeriesList(string name, string text)
		{
			var list = new ComicSmartListItem(name);
			list.Matchers.Clear();
			list.Matchers.Add(typeof(ComicBookSeriesMatcher), ComicBookStringMatcher.OperatorContains, text, string.Empty);
			return list;
		}

		private static ComicSmartListItem ScriptList(string name)
		{
			var list = new ComicSmartListItem(name);
			list.Matchers.Clear();
			list.Matchers.Add(typeof(ComicBookPluginMatcher), 1, string.Empty, string.Empty);
			return list;
		}

		/// <summary>An open (empty) library with: books Alpha 1..3, Beta 1..2, Gamma 1; lists "Alphas", "Betas", a folder "Mine" with "Gammas" and
		/// "Script list", and a group/base-list variant of a script list.</summary>
		private static (ComicDatabase db, ComicSmartListItem alphas, ComicSmartListItem script, ComicSmartListItem viaBase, ComicSmartListItem viaGroup, ComicListItemFolder folder) Fill(MacHost host)
		{
			ComicDatabase db = host.DatabaseManager.Database;
			foreach (ComicBook b in new[] { Book("Alpha", "1"), Book("Alpha", "2"), Book("Alpha", "3"), Book("Beta", "1"), Book("Beta", "2"), Book("Gamma", "1") })
			{
				db.Books.Add(b);
			}
			ComicSmartListItem alphas = SeriesList("Alphas", "alpha");
			ComicSmartListItem betas = SeriesList("Betas", "beta");
			ComicSmartListItem gammas = SeriesList("Gammas", "gamma");
			ComicSmartListItem script = ScriptList("Script list");
			var viaBase = new ComicSmartListItem("Via base list") { BaseListId = script.Id };
			viaBase.Matchers.Clear();
			var group = new ComicBookGroupMatcher();
			group.Matchers.Add(typeof(ComicBookPluginMatcher), 1, string.Empty, string.Empty);
			var viaGroup = new ComicSmartListItem("Via group");
			viaGroup.Matchers.Clear();
			viaGroup.Matchers.Add(group);
			var folder = new ComicListItemFolder("Mine");
			db.ComicLists.Add(alphas);
			db.ComicLists.Add(betas);
			db.ComicLists.Add(folder);
			folder.Items.Add(gammas);
			folder.Items.Add(script);
			folder.Items.Add(viaBase);
			folder.Items.Add(viaGroup);
			return (db, alphas, script, viaBase, viaGroup, folder);
		}

		[Fact]
		public async Task The_tree_has_the_library_the_folders_and_the_lists_and_hides_an_empty_temporary_folder()
		{
			using (var temp = new TempDir())
			using (Launch launch = await Launch.Start(temp.Combine("app")))
			{
				var f = Fill(launch.Service.Host);
				IReadOnlyList<ListNode> tree = LibraryLists.BuildTree(f.db);
				string[] names = tree.SelectMany(n => n.Flatten()).Select(n => n.Name).ToArray();
				output.WriteLine(string.Join(" | ", names));
				Assert.Contains("Library", names);
				Assert.Contains("Alphas", names);
				Assert.Contains("Mine", names);
				Assert.DoesNotContain(names, n => n.StartsWith("Temporary"));
				ListNode mine = tree.Single(n => n.Name == "Mine");
				Assert.Equal(ListKind.Folder, mine.Kind);
				Assert.Equal(new[] { "Gammas", "Script list", "Via base list", "Via group" }, mine.Children.Select(c => c.Name));
				Assert.Equal(ListKind.Library, tree.First(n => n.Name == "Library").Kind);
				Assert.Equal(ListKind.Smart, tree.First(n => n.Name == "Alphas").Kind);
			}
		}

		[Fact]
		public async Task A_smart_list_gives_the_books_the_core_matches_and_a_folder_combines_its_children()
		{
			using (var temp = new TempDir())
			using (Launch launch = await Launch.Start(temp.Combine("app")))
			{
				var f = Fill(launch.Service.Host);
				var evaluator = new ListEvaluator();
				ListResult alphas = evaluator.Evaluate(f.alphas);
				Assert.Null(alphas.Error);
				Assert.False(alphas.RequiresScripts);
				Assert.Equal(new[] { "1", "2", "3" }, alphas.Books.Select(b => b.Number).OrderBy(x => x));
				Assert.All(alphas.Books, b => Assert.Equal("Alpha", b.Series));
				Assert.Equal(6, evaluator.Evaluate(f.db.ComicLists.GetItems<ComicLibraryListItem>().Single()).Count);
				// The folder (Or): Gammas (1 book) plus whatever the script lists keep. Only the script free part is certain.
				Assert.Null(evaluator.Evaluate(f.folder).Error);
			}
		}

		[Fact]
		public async Task A_stale_stored_result_is_ignored_unless_asked_for()
		{
			using (var temp = new TempDir())
			using (Launch launch = await Launch.Start(temp.Combine("app")))
			{
				var f = Fill(launch.Service.Host);
				// What a database file brings along: a stored result that does not match the books any more (it names 1 of the 3 Alphas
				// and a book that is not an Alpha). The Core trusts it when the cache is on.
				ComicBook beta = f.db.Books.First(b => b.Series == "Beta");
				ComicBook alpha1 = f.db.Books.First(b => b.Series == "Alpha");
				string stored = alpha1.Id + "," + beta.Id;
				f.alphas.CacheStorage = stored;
				ListResult trusting = new ListEvaluator(useStoredCaches: true).Evaluate(f.alphas);
				output.WriteLine("stored cache trusted: " + trusting.Count + " books, library loaded " + f.alphas.Library?.IsLoaded + ", cache on " + f.alphas.CacheEnabled);
				f.alphas.CacheStorage = stored;
				ListResult computed = new ListEvaluator().Evaluate(f.alphas);
				Assert.Equal(3, computed.Count);
				if (f.alphas.CacheEnabled)
				{
					Assert.Equal(2, trusting.Count); // this is what the Windows list would have shown
				}
			}
		}

		[Fact]
		public async Task Lists_that_need_scripts_are_flagged_and_empty_and_do_not_throw()
		{
			using (var temp = new TempDir())
			using (Launch launch = await Launch.Start(temp.Combine("app")))
			{
				var f = Fill(launch.Service.Host);
				Assert.True(LibraryLists.RequiresScripts(f.script));
				Assert.True(LibraryLists.RequiresScripts(f.viaGroup), "a plugin matcher inside a group matcher");
				Assert.True(LibraryLists.RequiresScripts(f.viaBase), "a base list that needs scripts");
				Assert.False(LibraryLists.RequiresScripts(f.alphas));

				var evaluator = new ListEvaluator();
				foreach (ComicSmartListItem l in new[] { f.script, f.viaGroup, f.viaBase })
				{
					ListResult r = evaluator.Evaluate(l);
					Assert.True(r.RequiresScripts, l.Name);
					Assert.Empty(r.Books);
					Assert.Null(r.Error);
				}

				// Why the Core is not asked: without a plugin engine the matcher keeps every book it is given.
				Assert.Null(ComicBookPluginMatcher.PluginEngine);
				Assert.Equal(6, f.script.Match(f.db.Books.ToArray()).Count());
			}
		}

		private static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 30000)
		{
			var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (!condition())
			{
				if (DateTime.UtcNow > deadline)
				{
					throw new TimeoutException("Timed out waiting for " + what);
				}
				await Task.Delay(5);
			}
		}

		[Fact]
		public async Task The_sidebar_view_model_counts_lists_filters_the_grid_flags_script_lists_and_toggles()
		{
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					using (Launch launch = await Launch.Start(temp.Combine("app")))
					{
						var vm = launch.ViewModel;
						var f = Fill(launch.Service.Host);
						vm.OnHostOpened(); // builds the tree and shows the library
						await WaitFor(() => vm.Sidebar.Roots.Count > 0, "the tree");
						await vm.Sidebar.CountsReady;
						await Dispatcher.UIThread.InvokeAsync(() => { }); // the posted counts

						SidebarNode Find(string name) => vm.Sidebar.AllNodes().Single(n => n.Name == name);
						Assert.Equal(6, Find("Library").Count);
						Assert.Equal(3, Find("Alphas").Count);
						Assert.Equal(2, Find("Betas").Count);
						Assert.Equal(1, Find("Gammas").Count);
						Assert.Equal(0, Find("Script list").Count);
						Assert.Equal(SidebarNode.ScriptsBadge, Find("Script list").Badge);
						Assert.True(Find("Via base list").HasBadge);
						Assert.False(Find("Alphas").HasBadge);
						Assert.Equal("Library", vm.Sidebar.SelectedNode.Name);
						await FixtureLibrary.WaitForGrid(vm, 6);

						// Select a list: the grid shows its books, off the UI thread.
						vm.Sidebar.Select(Find("Alphas"));
						await WaitFor(() => vm.Browser.TotalCount == 3 && !vm.Browser.IsLoading, "the Alphas in the grid");
						await vm.Browser.WhenIdle();
						Assert.Equal(3, vm.Browser.Items.Length);
						Assert.All(vm.Browser.Items, i => Assert.Equal("Alpha", i.Series));
						Assert.False(vm.Sidebar.HasNotice);
						Assert.Equal("3 books", vm.Browser.SummaryText);

						// A list that needs scripts: an empty grid, the notice, no crash.
						int errors = Services.AppLog.ErrorCount;
						vm.Sidebar.Select(Find("Script list"));
						await WaitFor(() => vm.Browser.TotalCount == 0 && !vm.Browser.IsLoading && vm.Sidebar.HasNotice, "the script list notice");
						Assert.Empty(vm.Browser.Items);
						Assert.Contains("Requires scripts (not in MVP)", vm.Sidebar.Notice);
						Assert.Equal(errors, Services.AppLog.ErrorCount);

						// An empty result says so.
						vm.Sidebar.Select(Find("Library"));
						await WaitFor(() => vm.Browser.TotalCount == 6 && !vm.Browser.IsLoading && !vm.Sidebar.HasNotice, "the library again");

						// The selection survives a reload of the library (a scan, an import).
						vm.Sidebar.Select(Find("Betas"));
						await WaitFor(() => vm.Browser.TotalCount == 2 && !vm.Browser.IsLoading, "the Betas");
						f.db.Books.Add(Book("Beta", "3"));
						vm.OnHostOpened();
						await WaitFor(() => vm.Browser.TotalCount == 3 && !vm.Browser.IsLoading && vm.Sidebar.SelectedNode?.Name == "Betas", "the Betas after a reload");

						// View > Show Sidebar.
						Assert.True(vm.Sidebar.IsVisible);
						vm.ToggleSidebarCommand.Execute(null);
						Assert.False(vm.Sidebar.IsVisible);
						vm.ToggleSidebarCommand.Execute(null);
						Assert.True(vm.Sidebar.IsVisible);
					}
				});
			}
		}

		[Fact]
		public async Task The_window_shows_the_tree_with_counts_and_badges_and_hides_the_sidebar()
		{
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					using (Launch launch = await Launch.Start(temp.Combine("app")))
					{
						var vm = launch.ViewModel;
						Fill(launch.Service.Host);
						vm.OnHostOpened();
						await WaitFor(() => vm.Sidebar.Roots.Count > 0, "the tree");
						await vm.Sidebar.CountsReady;
						var window = new MainWindow { DataContext = vm };
						window.Show();
						Layout(window);
						var tree = window.FindControl<TreeView>("SidebarTree");
						var panel = window.FindControl<Border>("SidebarPanel");
						Assert.True(panel.IsVisible);
						Assert.NotEmpty(tree.GetRealizedTreeContainers());
						Save(window, "sidebar-1-small-library");

						vm.Sidebar.Select(vm.Sidebar.AllNodes().Single(n => n.Name == "Script list"));
						await WaitFor(() => vm.Sidebar.HasNotice, "the notice");
						Layout(window);
						Assert.True(window.FindControl<TextBlock>("ListNotice").IsVisible);
						Save(window, "sidebar-2-script-list");

						vm.ToggleSidebarCommand.Execute(null);
						Layout(window);
						Assert.False(panel.IsVisible);
						Save(window, "sidebar-3-hidden");
					}
				});
			}
		}

		private static void Layout(Window window)
		{
			for (int i = 0; i < 4; i++)
			{
				Dispatcher.UIThread.RunJobs();
				window.UpdateLayout();
				AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			}
		}

		internal static string Save(Window window, string name)
		{
			Layout(window);
			WriteableBitmap frame = window.CaptureRenderedFrame();
			Assert.NotNull(frame);
			string dir = Environment.GetEnvironmentVariable("CRMAC_FRAMES_DIR");
			if (!string.IsNullOrEmpty(dir))
			{
				Directory.CreateDirectory(dir);
				string file = Path.Combine(dir, name + ".png");
				frame.Save(file);
				return file;
			}
			return null;
		}

		/// <summary>
		/// The accept check: every list of the fixture database shows in the sidebar the count `crcli smartlist --all` prints for it, the
		/// numbers are not trivial, the script lists have the badge and an empty result, and selecting a list fills the grid with that many books.
		/// </summary>
		[FixtureFact("ComicDb.xml", "BD")]
		public async Task Every_list_of_the_fixture_library_has_the_count_of_crcli_smartlist()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				// 1. crcli, in process, on the original database with the remap (the CLI host is closed again when it returns).
				var cli = new StringWriter();
				var cliErr = new StringWriter();
				int exit = ComicRack.Cli.Program.Run(new[] { "smartlist", Fixtures.ComicDb, "--all", "--remap", @"S:\media\BD\=" + Fixtures.BD + "/", "--data-dir", temp.Combine("cli") }, cli, cliErr);
				Assert.True(exit == 0, cliErr.ToString());
				var expected = new Dictionary<string, (string kind, int count, string note)>();
				foreach (string line in cli.ToString().Split('\n').Where(l => l.StartsWith("list\t")))
				{
					string[] p = line.TrimEnd('\r').Split('\t');
					expected[p[1].Trim()] = (p[2], int.Parse(p[3]), p.Length > 4 ? p[4] : string.Empty);
				}
				output.WriteLine(cli.ToString());
				Assert.Equal(22, expected.Count);

				// 2. The app, on the imported copy (so it did not see the Windows paths).
				await Ui.Run(async () =>
				{
					using (Launch launch = await FixtureLibrary.Open14k(temp))
					{
						var vm = launch.ViewModel;
						await WaitFor(() => vm.Sidebar.AllNodes().Count() == expected.Count, "the tree");
						await vm.Sidebar.CountsReady;
						await Dispatcher.UIThread.InvokeAsync(() => { });
						var rows = new List<string> { "| List | Kind | Sidebar | crcli | Note |", "|---|---|---:|---:|---|" };
						int middle = 0;
						foreach (SidebarNode node in vm.Sidebar.AllNodes())
						{
							Assert.True(expected.TryGetValue(node.Name, out var e), "crcli lists " + node.Name);
							Assert.Equal(e.kind, node.Kind.ToString());
							Assert.Equal(e.count, node.Count);
							Assert.Equal(e.note == "requires scripts", node.HasBadge);
							if (node.Kind == ListKind.Smart && !node.HasBadge && node.Count > 0 && node.Count < 14706)
							{
								middle++;
							}
							rows.Add($"| {node.Name} | {node.Kind} | {node.Count} | {e.count} | {(node.HasBadge ? SidebarNode.ScriptsBadge : "")} |");
						}
						output.WriteLine(string.Join("\n", rows));
						Assert.Equal(2, vm.Sidebar.AllNodes().Count(n => n.HasBadge));
						Assert.True(middle >= 6, $"only {middle} smart lists have a count between 0 and 14,706");

						// 3. Selecting lists fills the grid with that many books, and the script lists show none.
						foreach (string name in new[] { "Non-numeric numbers (to check)", "Read", "One-Shots", "Reading device", "Missing (to fix)", "Next issues to read" })
						{
							SidebarNode node = vm.Sidebar.AllNodes().Single(n => n.Name == name);
							vm.Sidebar.Select(node);
							int want = node.HasBadge ? 0 : expected[name].count;
							await WaitFor(() => vm.Browser.TotalCount == want && !vm.Browser.IsLoading && vm.Sidebar.SelectedNode == node, "the grid for " + name);
							await vm.Browser.WhenIdle();
							Assert.Equal(want, vm.Browser.Items.Length);
							Assert.Equal(node.HasBadge, vm.Sidebar.HasNotice);
						}
					}
				});
			}
		}

		/// <summary>A frame of the sidebar over the 14,706-book library, for a look (set CRMAC_FRAMES_DIR).</summary>
		[FixtureFact("ComicDb.xml", "BD")]
		public async Task The_main_window_over_the_fixture_library_shows_the_sidebar()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					using (Launch launch = await FixtureLibrary.Open14k(temp))
					{
						var vm = launch.ViewModel;
						await WaitFor(() => vm.Sidebar.Roots.Count > 0, "the tree");
						await vm.Sidebar.CountsReady;
						await Dispatcher.UIThread.InvokeAsync(() => { });
						var window = new MainWindow { DataContext = vm };
						window.Show();
						var grid = window.FindControl<BookGridView>("BookGrid");
						SidebarNode node = vm.Sidebar.AllNodes().Single(n => n.Name == "Completed series");
						vm.Sidebar.Select(node);
						await WaitFor(() => vm.Browser.TotalCount == node.Count && !vm.Browser.IsLoading, "the list");
						await vm.Browser.WhenIdle();
						var deadline = DateTime.UtcNow.AddSeconds(30);
						while (!(grid.RealizedCount > 0 && grid.BoundCells().All(c => c.Cover.HasImage)) && DateTime.UtcNow < deadline)
						{
							Layout(window);
							await Task.Delay(5);
						}
						Save(window, "sidebar-4-fixture-completed-series");
						vm.Sidebar.Select(vm.Sidebar.AllNodes().Single(n => n.Name == "Next issues to read"));
						await WaitFor(() => vm.Sidebar.HasNotice && vm.Browser.TotalCount == 0 && !vm.Browser.IsLoading, "the script list");
						Save(window, "sidebar-5-fixture-script-list");
					}
				});
			}
		}
	}
}
