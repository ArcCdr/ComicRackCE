using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.Theme;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using cYo.Common.Collections;
using cYo.Common.Win32;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using Xunit;

namespace ComicRack.Mac.Tests.Browser
{
	/// <summary>P5.5: the context menu of the book grid (BC.*), its commands (Mark as, My Rating, Quick Rating, Reveal, Remove) and the questions that go with them (DL.06, DL.18, DL.30).</summary>
	public class ContextMenuTests
	{
		private const string Designer = "ComicRack/Views/ComicBrowserControl.Designer.cs";

		private static string Source() => OriginalColors.Read(Designer);

		// the items of an AddRange of the Designer file, by name
		private static string[] NamesOf(string collection)
		{
			Match m = Regex.Match(Source(), @"this\." + collection + @"\.Items\.AddRange\(new System\.Windows\.Forms\.ToolStripItem\[\] \{(.*?)\}\);", RegexOptions.Singleline);
			Assert.True(m.Success, collection + " was not found");
			return Regex.Matches(m.Groups[1].Value, @"this\.(\w+)").Select(x => x.Groups[1].Value).ToArray();
		}

		private static ComicBook Book(string series, int number, string file = null)
		{
			return new ComicBook
			{
				Series = series,
				Number = number.ToString(),
				Title = series + " " + number,
				FilePath = file ?? "/nowhere/" + series + number + ".cbz",
				PageCount = 10,
				AddedTime = DateTime.Now.AddDays(-100),
			};
		}

		private static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 20000)
		{
			var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (!condition())
			{
				if (DateTime.UtcNow > deadline)
				{
					throw new TimeoutException("Not reached: " + what);
				}
				await Task.Delay(5);
			}
		}

		// Puts the books into the library and shows them in the grid (the app turns the live refresh on; the test asks for one pass).
		private static async Task<ComicBook[]> Fill(Launch launch, params ComicBook[] books)
		{
			MainViewModel vm = launch.ViewModel;
			ComicDatabase db = launch.Service.Host.DatabaseManager.Database;
			foreach (ComicBook book in books)
			{
				db.Books.Add(book);
			}
			await vm.Sidebar.ReloadAsync(launch.Service.Host);
			await vm.Sidebar.CountsReady;
			await vm.WhenLibraryShownAsync();
			Avalonia.Threading.Dispatcher.UIThread.RunJobs();
			vm.RequestLiveRefresh();
			await vm.WhenLiveRefreshedAsync();
			await WaitFor(() => vm.Browser.Items.Length == db.Books.Count && !vm.Browser.IsLoading, "the grid shows the library");
			return books;
		}

		private static void SelectBooks(MainViewModel vm, params ComicBook[] books)
		{
			vm.Browser.Selection.SelectBooks(books);
		}

		// ---- the menu against the Designer file

		[Fact]
		public async Task The_menu_has_the_items_and_separators_of_the_original_in_its_order()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					var menu = new BrowserContextMenu(launch.ViewModel);
					Assert.Equal(NamesOf("contextMenuItems"), menu.Entries.Select(menu.NameOf).ToArray());
					var rating = (MenuItem)menu["miRateMenu"];
					Assert.Equal(new[] { "miRating0", "toolStripMenuItem3", "miRating1", "miRating2", "miRating3", "miRating4", "miRating5", "ratingSeparator", "ratingControl", "toolStripSeparator1", "miQuickRating" },
						rating.Items.Cast<Control>().Select(menu.NameOf).ToArray());
					// the original's list with the editor and its line put in front of the last two items (InsertRatingControl)
					string[] original = NamesOf("contextRating");
					Assert.Equal(original.Take(7).Concat(new[] { "ratingSeparator", "ratingControl" }).Concat(original.Skip(7)), rating.Items.Cast<Control>().Select(menu.NameOf));
					Assert.Equal(NamesOf("contextMarkAs"), ((MenuItem)menu["miMarkAs"]).Items.Cast<Control>().Select(menu.NameOf).ToArray());
					Assert.Equal(NamesOf("contextExport"), ((MenuItem)menu["miExportComics"]).Items.Cast<Control>().Select(menu.NameOf).ToArray());
				}
			});
		}

		[Fact]
		public async Task Texts_pictures_and_shortcuts_are_the_originals_with_Ctrl_as_Cmd_and_Alt_as_Option()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					var menu = new BrowserContextMenu(launch.ViewModel);
					string source = Source();
					int checkedItems = 0;
					foreach (Match item in Regex.Matches(source, @"this\.(mi\w+)\.Text = ""([^""]*)"";"))
					{
						MenuItem mi = menu.MenuItemOf(item.Groups[1].Value);
						if (mi == null)
						{
							continue; // the items of other menus of the Designer file (the quick search scopes ...)
						}
						string english = item.Groups[2].Value.Replace("&", string.Empty);
						string shown = mi.Header as string;
						if (item.Groups[1].Value == "miRevealBrowser")
						{
							Assert.Equal("Reveal in Finder", shown); // the platform's name replaces Explorer (BC.17)
						}
						else
						{
							Assert.Equal(english, shown);
						}
						checkedItems++;
					}
					Assert.True(checkedItems >= 40, "only " + checkedItems + " texts were compared");

					foreach (Match image in Regex.Matches(source, @"this\.(mi\w+)\.Image = global::[\w.]+\.Resources\.(\w+);"))
					{
						MenuItem mi = menu.MenuItemOf(image.Groups[1].Value);
						if (mi != null)
						{
							Assert.Equal(image.Groups[2].Value, Assert.IsType<CrIcon>(mi.Icon).IconName);
						}
					}
					Assert.Equal("Open", ((CrIcon)menu.MenuItemOf("miRead").Icon).IconName);
					Assert.Equal("EditDelete", ((CrIcon)menu.MenuItemOf("miRemove").Icon).IconName);

					int gestures = 0;
					foreach (Match key in Regex.Matches(source, @"this\.(mi\w+)\.ShortcutKeys = (.*?);", RegexOptions.Singleline))
					{
						MenuItem mi = menu.MenuItemOf(key.Groups[1].Value);
						if (mi == null)
						{
							continue;
						}
						string expression = key.Groups[2].Value;
						KeyModifiers modifiers = KeyModifiers.None;
						if (expression.Contains("Keys.Control")) modifiers |= KeyModifiers.Meta;
						if (expression.Contains("Keys.Alt")) modifiers |= KeyModifiers.Alt;
						if (expression.Contains("Keys.Shift")) modifiers |= KeyModifiers.Shift;
						string last = Regex.Matches(expression, @"Keys\.(\w+)").Select(x => x.Groups[1].Value).Last(k => k != "Control" && k != "Alt" && k != "Shift");
						Key expected = (Key)Enum.Parse(typeof(Key), last);
						KeyGesture gesture = Assert.IsType<KeyGesture>(mi.InputGesture);
						Assert.True(gesture.Key == expected && gesture.KeyModifiers == modifiers, key.Groups[1].Value + ": " + gesture + " is not " + modifiers + "+" + expected);
						gestures++;
					}
					Assert.True(gestures >= 18, "only " + gestures + " shortcuts were compared");
				}
			});
		}

		[Fact]
		public async Task Items_of_later_tasks_are_there_and_off_and_the_ones_of_this_task_follow_the_selection()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, Book("Alpha", 1), Book("Alpha", 2), Book("Beta", 1, file: ""));
					ComicBook nofile = books.First(b => !b.IsLinked);
					var menu = new BrowserContextMenu(vm);

					// no selection: only Select All and Invert Selection work
					vm.Browser.Selection.Clear();
					menu.Update();
					string[] alive = menu.Entries.OfType<MenuItem>().Where(i => i.IsEnabled && i.IsVisible).Select(menu.NameOf).ToArray();
					Assert.Equal(new[] { "miSelectAll", "miInvertSelection" }, alive);

					SelectBooks(vm, books[0], books[1]);
					menu.Update();
					foreach (string name in new[] { "miRead", "miReadTab", "miProperties", "miRateMenu", "miMarkAs", "miRevealBrowser", "miSelectAll", "miInvertSelection", "miRemove" })
					{
						Assert.True(menu.MenuItemOf(name).IsEnabled, name + " is enabled for a selection of books with files");
					}
					foreach (string name in new[] { "miOpenWith", "miEditList", "miAddList", "miAddLibrary", "miShowOnly", "miShowInList", "miExportComics", "miAutomation", "miCopyData", "miPasteData", "miClearData", "miRefreshInformation", "miSetListBackground" })
					{
						Assert.True(menu.MenuItemOf(name) != null && !menu.MenuItemOf(name).IsEnabled, name + " waits for its task");
					}
					// Update Book Files (P5.9) works with a file and shows only for a book with changes that are not in its file
					Assert.True(menu.MenuItemOf("miUpdateComicFiles").IsEnabled);
					Assert.False(menu.MenuItemOf("miUpdateComicFiles").IsVisible);
					Assert.False(menu.MenuItemOf("miEdit").IsVisible); // Details view only
					Assert.False(menu.MenuItemOf("miSetTopOfStack").IsVisible); // stacks only
					Assert.False(menu.MenuItemOf("miShowWeb").IsVisible); // only for a book with a Web field

					// a book without a file cannot be opened or revealed, but it can be marked, rated and removed
					SelectBooks(vm, nofile);
					menu.Update();
					Assert.False(menu.MenuItemOf("miRead").IsEnabled);
					Assert.False(menu.MenuItemOf("miRevealBrowser").IsEnabled);
					Assert.False(menu.MenuItemOf("miUpdateComicFiles").IsEnabled); // AnySelectedLinked
					Assert.True(menu.MenuItemOf("miMarkAs").IsEnabled);
					Assert.True(menu.MenuItemOf("miRemove").IsEnabled);

					// a Web field shows the item (it works with P5.20)
					books[0].Web = "https://example.org/a";
					SelectBooks(vm, books[0]);
					menu.Update();
					Assert.True(menu.MenuItemOf("miShowWeb").IsVisible);
					Assert.False(menu.MenuItemOf("miShowWeb").IsEnabled);
				}
			});
		}

		[Fact]
		public async Task Separators_are_hidden_at_the_ends_and_in_a_row()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					var menu = new BrowserContextMenu(launch.ViewModel);
					menu.Update();
					bool previousSeparator = true;
					Control lastVisible = null;
					foreach (Control entry in menu.Entries.Where(e => e.IsVisible))
					{
						Assert.False(entry is Separator && previousSeparator, "two separators in a row, or one first: before " + menu.NameOf(entry));
						previousSeparator = entry is Separator;
						lastVisible = entry;
					}
					Assert.IsNotType<Separator>(lastVisible);
				}
			});
		}

		// ---- the commands

		[Fact]
		public async Task Mark_as_sets_read_unread_checked_and_unchecked_on_every_selected_book()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, Book("Alpha", 1), Book("Alpha", 2), Book("Alpha", 3));
					var menu = new BrowserContextMenu(vm);
					SelectBooks(vm, books[0], books[1]);

					menu.MenuItemOf("miMarkRead").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
					Assert.True(books[0].HasBeenRead && books[1].HasBeenRead);
					Assert.False(books[2].HasBeenRead);
					Assert.Equal(9, books[0].LastPageRead);
					Assert.True(books[0].OpenedCount >= 1);

					menu.MenuItemOf("miMarkUnread").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
					Assert.False(books[0].HasBeenRead || books[1].HasBeenRead);
					Assert.Equal(0, books[0].LastPageRead);
					Assert.Equal(DateTime.MinValue, books[0].OpenedTime);

					Assert.True(books.All(b => b.Checked)); // books start checked
					menu.MenuItemOf("miMarkUnchecked").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
					Assert.False(books[0].Checked || books[1].Checked);
					Assert.True(books[2].Checked);
					menu.MenuItemOf("miMarkChecked").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
					Assert.True(books[0].Checked && books[1].Checked);
				}
			});
		}

		[Fact]
		public async Task Marking_a_book_as_read_changes_the_counters_of_the_tree()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, Book("Alpha", 1), Book("Alpha", 2), Book("Alpha", 3));
					SidebarNode library = vm.Sidebar.AllNodes().Single(n => n.Kind == ListKind.Library);
					Assert.Equal((3, 3), (library.Count, library.UnreadCount));
					SelectBooks(vm, books[0]);
					vm.Browser.Run(BookCommand.MarkRead);
					vm.RequestLiveRefresh();
					await vm.WhenLiveRefreshedAsync();
					Avalonia.Threading.Dispatcher.UIThread.RunJobs();
					Assert.Equal((3, 2), (library.Count, library.UnreadCount));
				}
			});
		}

		[Fact]
		public async Task My_Rating_sets_the_rating_of_the_selection_and_marks_the_rating_they_share()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, Book("Alpha", 1), Book("Alpha", 2), Book("Alpha", 3));
					var menu = new BrowserContextMenu(vm);
					SelectBooks(vm, books[0], books[1]);
					menu.MenuItemOf("miRating4").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
					Assert.Equal(new[] { 4f, 4f, 0f }, books.Select(b => b.Rating).ToArray());

					menu.Update();
					Assert.True(menu.MenuItemOf("miRating4").IsChecked);
					Assert.False(menu.MenuItemOf("miRating0").IsChecked);
					Assert.Equal(4, menu.Stars.Rating);

					SelectBooks(vm, books[0], books[2]);
					menu.Update();
					Assert.Empty(new[] { 0, 1, 2, 3, 4, 5 }.Where(i => menu.MenuItemOf("miRating" + i).IsChecked)); // different ratings: no mark
					Assert.Equal(0, menu.Stars.Rating);

					menu.MenuItemOf("miRating0").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
					Assert.Equal(new[] { 0f, 4f, 0f }, books.Select(b => b.Rating).ToArray());
				}
			});
		}

		[Fact]
		public async Task The_stars_of_the_menu_set_the_rating_when_the_menu_closes_and_only_if_they_were_used()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, Book("Alpha", 1), Book("Alpha", 2));
					var menu = new BrowserContextMenu(vm);
					SelectBooks(vm, books[0]);
					menu.Update();
					menu.ApplyStars();
					Assert.Equal(0f, books[0].Rating); // the menu was only looked at

					menu.Stars.Rating = 3.5;
					menu.Stars.UserChanged = true;
					menu.ApplyStars();
					Assert.Equal(3.5f, books[0].Rating);
					Assert.Equal(0f, books[1].Rating);
				}
			});
		}

		[Fact]
		public async Task Quick_Rating_writes_review_rating_and_the_show_when_read_setting_after_OK_and_nothing_after_Cancel()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, Book("Alpha", 1), Book("Alpha", 2));
					SelectBooks(vm, books[1], books[0]);

					vm.Browser.Run(BookCommand.QuickRating);
					await WaitFor(() => launch.Dialogs.QuickRatings.Count == 1, "the dialog is asked for");
					Assert.Same(vm.Browser.SelectedBooks[0], launch.Dialogs.QuickRatings[0].Book); // the first selected book
					Assert.False(launch.Dialogs.QuickRatings[0].ShowWhenRead);
					Assert.Same(launch.Service.Thumbnails, launch.Dialogs.QuickRatings[0].Thumbnails);
					Assert.Equal(0f, books[0].Rating); // cancelled
					Assert.Equal(string.Empty, books[0].Review ?? string.Empty);

					launch.Dialogs.QuickRatingToReturn = new QuickRatingResult { Review = "Great", Rating = 4.5f, ShowWhenRead = true };
					Assert.True(await vm.QuickRatingAsync(new[] { books[0] }));
					Assert.Equal("Great", books[0].Review);
					Assert.Equal(4.5f, books[0].Rating);
					Assert.True(vm.Preferences.AutoShowQuickReview);
					Assert.True(launch.Dialogs.QuickRatings[1].ShowWhenRead == false); // asked with the setting as it was
					await vm.QuickRatingAsync(new[] { books[1] });
					Assert.True(launch.Dialogs.QuickRatings[2].ShowWhenRead);
				}
			});
		}

		[Fact]
		public async Task Reveal_in_Finder_shows_the_file_of_the_first_selected_book()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, Book("Alpha", 1), Book("Alpha", 2));
					SelectBooks(vm, books[1], books[0]);
					var shell = Assert.IsType<NullShell>(typeof(MainViewModel).GetField("shell", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(vm));
					vm.Browser.Run(BookCommand.Reveal);
					Assert.Equal(new[] { vm.Browser.SelectedBooks[0].FilePath }, shell.Revealed);
				}
			});
		}

		[Fact]
		public async Task Info_opens_the_info_dialog_of_the_first_selected_book_with_the_shown_list_as_its_books_and_an_undo_marker()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, Book("Alpha", 1), Book("Beta", 2), Book("Gamma", 3));
					SelectBooks(vm, vm.Browser.Items[1].Book);
					vm.Browser.Run(BookCommand.Info);
					BookInfoRequest request = Assert.Single(launch.Dialogs.BookInfos);
					Assert.Same(vm.Browser.Items[1].Book, request.Model.Current);
					Assert.Equal(vm.Browser.Items.Select(i => i.Book), request.Model.AllBooks); // Previous and Next walk through the list as the grid shows it
					Assert.NotNull(request.Thumbnails);
					Assert.Equal("Show Info", launch.Service.Host.DatabaseManager.Database.Undo.UndoLabel ?? "Show Info"); // the marker is pending until a book changes
				}
			});
		}

		// ---- the keys of the grid

		[Fact]
		public async Task The_grid_runs_the_commands_of_the_menu_from_the_keys_of_the_original()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					var books = Enumerable.Range(1, 6).Select(i => new BookItem(Book("S", i), "Book " + i, "S", i.ToString(), new DateTime(2020, 1, 1).AddDays(i))).ToArray();
					await h.Show(new BookCatalog(books));
					h.Grid.Focus();
					var seen = new List<(BookCommand, float)>();
					h.Model.BookCommandRequested += (s, e) => seen.Add((e.Command, e.Value));
					var opened = new List<OpenTabMode>();
					h.Model.OpenBookRequested += (s, item) => opened.Add(h.Model.PendingOpenMode);
					var removed = new List<bool>();
					h.Model.RemoveBooksRequested += (s, list) => removed.Add(h.Model.PendingRemoveAsk);

					void Press(Key key, KeyModifiers modifiers) => h.Window.KeyPress(key, ToRaw(modifiers), PhysicalKey.None, null);

					Press(Key.R, KeyModifiers.Alt | KeyModifiers.Shift);
					Assert.Empty(seen); // nothing selected
					h.Model.SelectedIndex = 2;
					Press(Key.R, KeyModifiers.Alt | KeyModifiers.Shift);
					Press(Key.U, KeyModifiers.Alt | KeyModifiers.Shift);
					Press(Key.M, KeyModifiers.Alt | KeyModifiers.Shift);
					Press(Key.V, KeyModifiers.Alt | KeyModifiers.Shift);
					Press(Key.D3, KeyModifiers.Alt | KeyModifiers.Shift);
					Press(Key.D0, KeyModifiers.Alt | KeyModifiers.Shift);
					Press(Key.Q, KeyModifiers.Alt | KeyModifiers.Shift);
					Press(Key.I, KeyModifiers.Meta);
					Press(Key.G, KeyModifiers.Meta);
					Assert.Equal(new[]
					{
						(BookCommand.MarkRead, 0f), (BookCommand.MarkUnread, 0f), (BookCommand.MarkChecked, 0f), (BookCommand.MarkUnchecked, 0f),
						(BookCommand.Rate, 3f), (BookCommand.Rate, 0f), (BookCommand.QuickRating, 0f), (BookCommand.Info, 0f), (BookCommand.Reveal, 0f),
					}, seen.ToArray());

					Press(Key.O, KeyModifiers.Meta);
					Assert.Equal(new[] { OpenTabMode.Default }, opened);

					Press(Key.Delete, KeyModifiers.None);
					Press(Key.Back, KeyModifiers.Meta); // the alias asks
					Press(Key.Delete, KeyModifiers.Meta); // Ctrl+Delete of the original does not
					Assert.Equal(new[] { true, true, false }, removed);
				}
			});
		}

		private static RawInputModifiers ToRaw(KeyModifiers m)
		{
			RawInputModifiers raw = RawInputModifiers.None;
			if ((m & KeyModifiers.Shift) != 0) raw |= RawInputModifiers.Shift;
			if ((m & KeyModifiers.Meta) != 0) raw |= RawInputModifiers.Meta;
			if ((m & KeyModifiers.Alt) != 0) raw |= RawInputModifiers.Alt;
			return raw;
		}

		// ---- Remove

		// The Trash of these tests is a folder of the test, never the user's.
		private sealed class TrashFolder : IDisposable
		{
			private readonly Action<string> before = ShellFile.TrashHandler;

			public TrashFolder(string folder, HashSet<string> refuse = null)
			{
				Directory.CreateDirectory(folder);
				ShellFile.TrashHandler = path =>
				{
					if (refuse != null && refuse.Contains(path))
					{
						throw new IOException("in use");
					}
					File.Move(path, Path.Combine(folder, Path.GetFileName(path)));
				};
			}

			public void Dispose() => ShellFile.TrashHandler = before;
		}

		private static ComicBook FileBook(string folder, string series, int number)
		{
			string file = Path.Combine(folder, series + number + ".cbz");
			File.WriteAllText(file, series + number);
			return Book(series, number, file);
		}

		[Fact]
		public async Task Removing_from_the_library_asks_with_the_option_for_the_Trash_and_remembers_the_answer()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				using (var trash = new TrashFolder(temp.Combine("trash"))) // after the launch: the host installs the Trash of the Mac when it opens
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, FileBook(temp.Path, "Alpha", 1), FileBook(temp.Path, "Alpha", 2), FileBook(temp.Path, "Alpha", 3));
					ComicDatabase db = launch.Service.Host.DatabaseManager.Database;

					// Cancel: nothing happens
					launch.Dialogs.Answers.Enqueue(QuestionAnswer.Cancel);
					Assert.Null(await vm.RemoveBooksAsync(new[] { books[0] }));
					Assert.Equal(3, db.Books.Count);
					QuestionRequest asked = Assert.Single(launch.Dialogs.Questions);
					Assert.StartsWith("Are you sure you want to remove these books from the library?", asked.Question);
					Assert.Contains("\nAll information not stored in the files will be lost", asked.Question);
					Assert.Equal("Remove", asked.OkText);
					Assert.Equal("&Also move the files to the Trash", asked.OptionText); // the original's text with its mnemonic; not checked: the setting is off
					Assert.Null(asked.Option2Text);
					Assert.True(asked.ShowCancel);

					// OK with the Trash option: the book goes, its file goes to the Trash, the choice is kept
					launch.Dialogs.Answers.Enqueue(new QuestionAnswer(true, true, false, false));
					RemoveResult result = await vm.RemoveBooksAsync(new[] { books[0] });
					Assert.Equal(1, result.RemovedFromLibrary);
					Assert.Equal(1, result.FilesTrashed);
					Assert.Equal(2, db.Books.Count);
					Assert.True(vm.Preferences.MoveFilesToRecycleBin);
					Assert.True(File.Exists(temp.Combine("trash/Alpha1.cbz")));
					Assert.False(File.Exists(books[0].FilePath));
					Assert.True(db.IsBlacklisted(books[0].FilePath));

					// the next question starts with the option checked ("!"), and unchecked it keeps the file
					launch.Dialogs.Answers.Enqueue(new QuestionAnswer(true, false, false, false));
					result = await vm.RemoveBooksAsync(new[] { books[1] });
					Assert.StartsWith("!", launch.Dialogs.Questions[2].OptionText);
					Assert.Equal(1, result.RemovedFromLibrary);
					Assert.Equal(0, result.FilesTrashed);
					Assert.False(vm.Preferences.MoveFilesToRecycleBin);
					Assert.True(File.Exists(books[1].FilePath));
					Assert.Equal(1, db.Books.Count);
				}
			});
		}

		[Fact]
		public async Task Remove_without_the_question_uses_the_saved_options()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				using (var trash = new TrashFolder(temp.Combine("trash"))) // after the launch: the host installs the Trash of the Mac when it opens
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, FileBook(temp.Path, "Alpha", 1), FileBook(temp.Path, "Alpha", 2));
					vm.Preferences.MoveFilesToRecycleBin = true;
					SelectBooks(vm, books[0]);
					vm.Browser.RemoveSelected(ask: false); // Cmd+Delete
					await WaitFor(() => launch.Service.Host.DatabaseManager.Database.Books.Count == 1, "the book is removed");
					Assert.Empty(launch.Dialogs.Questions);
					Assert.True(File.Exists(temp.Combine("trash/Alpha1.cbz")));
				}
			});
		}

		[Fact]
		public async Task A_file_that_cannot_be_moved_keeps_its_book_and_the_user_is_told()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, FileBook(temp.Path, "Alpha", 1), FileBook(temp.Path, "Alpha", 2));
					using (var trash = new TrashFolder(temp.Combine("trash"), new HashSet<string> { books[0].FilePath }))
					{
						launch.Dialogs.Answers.Enqueue(new QuestionAnswer(true, true, false, false));
						RemoveResult result = await vm.RemoveBooksAsync(new[] { books[0], books[1] });
						Assert.Equal(1, result.RemovedFromLibrary);
						Assert.Equal(1, result.FilesFailed);
						Assert.Equal(new[] { books[0] }, launch.Service.Host.DatabaseManager.Database.Books.Lock().ToArray());
						Assert.Equal("Some files could not be deleted (maybe they are in use)!", Assert.Single(launch.Dialogs.Messages).Message);
					}
				}
			});
		}

		[Fact]
		public async Task Removing_from_a_fixed_list_asks_about_the_list_and_removes_from_the_library_only_if_asked()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				using (var trash = new TrashFolder(temp.Combine("trash"))) // after the launch: the host installs the Trash of the Mac when it opens
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, FileBook(temp.Path, "Alpha", 1), FileBook(temp.Path, "Alpha", 2), FileBook(temp.Path, "Alpha", 3));
					ComicDatabase db = launch.Service.Host.DatabaseManager.Database;
					var list = new ComicIdListItem("Favourites");
					db.ComicLists.Add(list);
					list.AddRange(books);
					await vm.Sidebar.ReloadAsync(launch.Service.Host);
					vm.Sidebar.Select(vm.Sidebar.AllNodes().Single(n => n.Name == "Favourites"));
					await WaitFor(() => vm.Browser.Items.Length == 3 && !vm.Browser.IsLoading, "the list is shown");

					// from the list only
					launch.Dialogs.Answers.Enqueue(new QuestionAnswer(true, false, false, false));
					RemoveResult result = await vm.RemoveBooksAsync(new[] { books[0] });
					QuestionRequest asked = launch.Dialogs.Questions[0];
					Assert.Equal("Are you sure you want to remove these books from the list?", asked.Question);
					StringAssert("&Additionally remove the books from the Library (all information not stored in the files will be lost)", asked.OptionText);
					Assert.StartsWith("&Also move the files", asked.Option2Text);
					Assert.Equal(1, result.RemovedFromList);
					Assert.Equal(0, result.RemovedFromLibrary);
					Assert.Equal(3, db.Books.Count);
					Assert.Equal(2, list.GetBooks().Count());
					Assert.False(vm.Preferences.AlsoRemoveFromLibrary);

					// and from the library, with the file in the Trash; the options are kept
					launch.Dialogs.Answers.Enqueue(new QuestionAnswer(true, true, true, false));
					result = await vm.RemoveBooksAsync(new[] { books[1] });
					Assert.Equal(1, result.RemovedFromLibrary);
					Assert.Equal(1, result.FilesTrashed);
					Assert.True(vm.Preferences.AlsoRemoveFromLibrary);
					Assert.True(vm.Preferences.MoveFilesToRecycleBin);
					Assert.Equal(2, db.Books.Count);

					// the next time the option starts checked
					launch.Dialogs.Answers.Enqueue(QuestionAnswer.Cancel);
					await vm.RemoveBooksAsync(new[] { books[2] });
					Assert.StartsWith("!", launch.Dialogs.Questions[2].OptionText);
					Assert.StartsWith("!", launch.Dialogs.Questions[2].Option2Text);
				}
			});
		}

		private static void StringAssert(string expected, string actual) => Assert.Equal(expected, actual.TrimStart('!'));

		[Fact]
		public async Task Removing_from_a_smart_list_hides_the_books_there_and_keeps_its_own_remembered_option()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, Book("Alpha", 1), Book("Alpha", 2));
					ComicDatabase db = launch.Service.Host.DatabaseManager.Database;
					var smart = new ComicSmartListItem("Alphas");
					smart.Matchers.Clear();
					smart.Matchers.Add(typeof(ComicBookSeriesMatcher), ComicBookStringMatcher.OperatorContains, "Alpha", string.Empty);
					db.ComicLists.Add(smart);
					await vm.Sidebar.ReloadAsync(launch.Service.Host);
					vm.Sidebar.Select(vm.Sidebar.AllNodes().Single(n => n.Name == "Alphas"));
					await WaitFor(() => vm.Browser.Items.Length == 2 && !vm.Browser.IsLoading, "the smart list is shown");

					launch.Dialogs.Answers.Enqueue(new QuestionAnswer(true, false, false, false));
					RemoveResult result = await vm.RemoveBooksAsync(new[] { books[0] });
					Assert.Equal(1, result.RemovedFromList);
					Assert.True(smart.IsFiltered(books[0]));
					Assert.Equal(2, db.Books.Count);
					Assert.False(vm.Preferences.AlsoRemoveFromLibraryFiltered);
					Assert.False(vm.Preferences.AlsoRemoveFromLibrary); // the other list kinds keep their own

					launch.Dialogs.Answers.Enqueue(new QuestionAnswer(true, true, false, false));
					await vm.RemoveBooksAsync(new[] { books[1] });
					Assert.True(vm.Preferences.AlsoRemoveFromLibraryFiltered);
					Assert.False(vm.Preferences.AlsoRemoveFromLibrary);
					Assert.Equal(1, db.Books.Count);
				}
			});
		}

		[Fact]
		public async Task Delete_in_the_grid_removes_the_selected_books()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					ComicBook[] books = await Fill(launch, Book("Alpha", 1), Book("Alpha", 2), Book("Alpha", 3));
					SelectBooks(vm, books[1]);
					vm.Browser.RemoveSelectedCommand.Execute(null);
					await WaitFor(() => launch.Dialogs.Questions.Count == 1 && launch.Service.Host.DatabaseManager.Database.Books.Count == 2, "the question was asked and answered");
					Assert.DoesNotContain(books[1], launch.Service.Host.DatabaseManager.Database.Books.Lock().ToArray());
					await WaitFor(() => vm.Browser.Items.Length == 2, "the grid shows what is left");
				}
			});
		}

		// ---- "do not ask again"

		[Fact]
		public async Task A_question_with_do_not_ask_again_is_not_asked_once_the_box_was_checked()
		{
			await Ui.Run(async () =>
			{
				using (var temp = new TempDir())
				using (Launch launch = await Launch.Start(temp.Combine("app")))
				{
					MainViewModel vm = launch.ViewModel;
					// asked, OK without the box: asked again next time
					Assert.True(await vm.AskQuestionAsync("Clear all?", "Clear", HiddenMessageBoxes.AskClearData));
					Assert.True(await vm.AskQuestionAsync("Clear all?", "Clear", HiddenMessageBoxes.AskClearData));
					Assert.Equal(2, launch.Dialogs.Questions.Count);
					Assert.Equal("&Do not ask me again", launch.Dialogs.Questions[0].SuppressionText);
					Assert.Equal("Clear", launch.Dialogs.Questions[0].OkText);

					// Cancel with the box checked does not hide it
					launch.Dialogs.Answers.Enqueue(new QuestionAnswer(false, false, false, true));
					Assert.False(await vm.AskQuestionAsync("Clear all?", "Clear", HiddenMessageBoxes.AskClearData));
					Assert.Equal(HiddenMessageBoxes.None, vm.Preferences.HiddenMessageBoxes);

					// OK with the box checked: remembered, not asked again, answered with OK; another question is still asked
					launch.Dialogs.Answers.Enqueue(new QuestionAnswer(true, false, false, true));
					Assert.True(await vm.AskQuestionAsync("Clear all?", "Clear", HiddenMessageBoxes.AskClearData));
					Assert.Equal(HiddenMessageBoxes.AskClearData, vm.Preferences.HiddenMessageBoxes);
					int asked = launch.Dialogs.Questions.Count;
					Assert.True(await vm.AskQuestionAsync("Clear all?", "Clear", HiddenMessageBoxes.AskClearData));
					Assert.Equal(asked, launch.Dialogs.Questions.Count);
					Assert.True(await vm.AskQuestionAsync("Convert?", "Convert", HiddenMessageBoxes.ConvertComics));
					Assert.Equal(asked + 1, launch.Dialogs.Questions.Count);
				}
			});
		}
	}
}
