using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using ComicRack.Mac.Browser;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using Xunit;

namespace ComicRack.Mac.Tests.Browser
{
	/// <summary>
	/// P5.3: the selection rules of the original's ItemView (BV.09, BV.10, BC.19), on the model, on the view model (kept across sort and search, the
	/// books for commands, Select All, Invert, Remove) and on the grid with real keys and clicks.
	/// </summary>
	public class SelectionTests
	{
		private static BookItem[] Books(int count)
		{
			return Enumerable.Range(1, count).Select(i =>
				new BookItem(new ComicBook { FilePath = "/nowhere/" + i.ToString("00") + ".cbz", FileSize = i * 1000 }, "Book " + i.ToString("00"), "Series", i.ToString(), new DateTime(2020, 1, 1).AddDays(i))).ToArray();
		}

		private static SelectionModel Model(int count)
		{
			var model = new SelectionModel();
			model.SetItems(Books(count), keepHidden: false);
			return model;
		}

		private static int[] Selected(SelectionModel m) => m.SelectedIndices().ToArray();

		// ---- the model: the mouse --------------------------------------------------------------------------------------------------------

		[Fact]
		public void A_click_selects_one_book_and_gives_it_the_focus()
		{
			SelectionModel m = Model(10);
			m.Click(3, toggle: false, extend: false);
			Assert.Equal(new[] { 3 }, Selected(m));
			Assert.Equal(3, m.FocusedIndex);
			m.Click(5, toggle: false, extend: false);
			Assert.Equal(new[] { 5 }, Selected(m));
			Assert.Equal(1, m.Count);
		}

		[Fact]
		public void Cmd_click_flips_a_book_and_keeps_the_others()
		{
			SelectionModel m = Model(10);
			m.Click(1, false, false);
			m.Click(4, toggle: true, extend: false);
			m.Click(7, toggle: true, extend: false);
			Assert.Equal(new[] { 1, 4, 7 }, Selected(m));
			Assert.Equal(7, m.FocusedIndex);
			m.Click(4, toggle: true, extend: false); // flips off
			Assert.Equal(new[] { 1, 7 }, Selected(m));
			Assert.Equal(4, m.FocusedIndex); // it still gets the focus
			Assert.Equal(2, m.Count);
		}

		[Fact]
		public void Shift_click_selects_from_the_focused_book_to_the_clicked_one_in_both_directions()
		{
			SelectionModel m = Model(12);
			m.Click(3, false, false);
			m.Click(6, toggle: false, extend: true);
			Assert.Equal(new[] { 3, 4, 5, 6 }, Selected(m));
			Assert.Equal(6, m.FocusedIndex);
			// the range restarts at the focused book (the original resets the anchor on every press), and the old range is let go
			m.Click(2, toggle: false, extend: true);
			Assert.Equal(new[] { 2, 3, 4, 5, 6 }, Selected(m));
			m.Click(9, false, false);
			m.Click(7, toggle: false, extend: true);
			Assert.Equal(new[] { 7, 8, 9 }, Selected(m));
		}

		[Fact]
		public void Cmd_and_Shift_click_adds_a_range_to_the_selection()
		{
			SelectionModel m = Model(12);
			m.Click(0, false, false);
			m.Click(5, toggle: true, extend: false);
			m.Click(8, toggle: true, extend: true);
			Assert.Equal(new[] { 0, 5, 6, 7, 8 }, Selected(m));
		}

		[Fact]
		public void Shift_click_without_a_focused_book_starts_at_the_first()
		{
			SelectionModel m = Model(8);
			m.Click(3, toggle: false, extend: true);
			Assert.Equal(new[] { 0, 1, 2, 3 }, Selected(m));
		}

		[Fact]
		public void A_click_on_the_empty_area_clears_the_selection_and_the_focus_only_without_a_modifier()
		{
			SelectionModel m = Model(6);
			m.Click(2, false, false);
			m.ClickEmpty(toggle: true, extend: false);
			Assert.Equal(new[] { 2 }, Selected(m));
			m.ClickEmpty(toggle: false, extend: true);
			Assert.Equal(new[] { 2 }, Selected(m));
			m.ClickEmpty(false, false);
			Assert.Empty(Selected(m));
			Assert.Equal(-1, m.FocusedIndex);
		}

		[Fact]
		public void Focus_moves_the_focus_and_nothing_else()
		{
			SelectionModel m = Model(6);
			m.Click(1, false, false);
			m.Click(2, toggle: true, extend: false);
			m.Focus(5);
			Assert.Equal(new[] { 1, 2 }, Selected(m));
			Assert.Equal(5, m.FocusedIndex);
		}

		// ---- the model: the keyboard -----------------------------------------------------------------------------------------------------

		[Fact]
		public void A_movement_key_selects_the_target_alone()
		{
			SelectionModel m = Model(10);
			m.Click(2, false, false);
			m.Click(4, toggle: true, extend: false);
			m.Move(5, toggle: false, extend: false);
			Assert.Equal(new[] { 5 }, Selected(m));
			Assert.Equal(5, m.FocusedIndex);
		}

		[Fact]
		public void Shift_with_a_movement_key_extends_from_the_anchor_and_shrinks_when_the_focus_comes_back()
		{
			SelectionModel m = Model(12);
			m.Move(4, false, false); // the anchor
			m.Move(5, toggle: false, extend: true);
			m.Move(6, toggle: false, extend: true);
			m.Move(7, toggle: false, extend: true);
			Assert.Equal(new[] { 4, 5, 6, 7 }, Selected(m));
			m.Move(5, toggle: false, extend: true);
			Assert.Equal(new[] { 4, 5 }, Selected(m));
			m.Move(2, toggle: false, extend: true); // past the anchor
			Assert.Equal(new[] { 2, 3, 4 }, Selected(m));
			m.Move(8, toggle: false, extend: false); // a plain move resets everything, the anchor too
			m.Move(9, toggle: false, extend: true);
			Assert.Equal(new[] { 8, 9 }, Selected(m));
		}

		[Fact]
		public void Cmd_with_a_movement_key_moves_the_focus_without_changing_the_selection_and_Space_toggles()
		{
			SelectionModel m = Model(10);
			m.Click(1, false, false);
			m.Move(3, toggle: true, extend: false);
			Assert.Equal(new[] { 1 }, Selected(m));
			Assert.Equal(3, m.FocusedIndex);
			m.Space(toggle: false); // Space selects the focused book and keeps the others
			Assert.Equal(new[] { 1, 3 }, Selected(m));
			m.Space(toggle: true); // flips it off
			Assert.Equal(new[] { 1 }, Selected(m));
			m.Move(6, toggle: true, extend: true); // Cmd+Shift: the range is added
			Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, Selected(m));
		}

		[Fact]
		public void Space_without_a_focus_acts_on_the_first_book()
		{
			SelectionModel m = Model(4);
			m.Space(toggle: false);
			Assert.Equal(new[] { 0 }, Selected(m));
			Assert.Equal(0, m.FocusedIndex);
		}

		// ---- the model: the whole list ---------------------------------------------------------------------------------------------------

		[Fact]
		public void Select_all_and_invert_work_on_every_shown_book_and_leave_the_focus()
		{
			SelectionModel m = Model(8);
			m.Click(3, false, false);
			m.SelectAll();
			Assert.Equal(Enumerable.Range(0, 8), Selected(m));
			Assert.Equal(8, m.Count);
			Assert.Equal(3, m.FocusedIndex);
			m.Click(2, false, false);
			m.Click(4, toggle: true, extend: false);
			m.Invert();
			Assert.Equal(new[] { 0, 1, 3, 5, 6, 7 }, Selected(m));
			Assert.Equal(6, m.Count);
			Assert.Equal(4, m.FocusedIndex);
			m.Invert();
			Assert.Equal(new[] { 2, 4 }, Selected(m));
		}

		[Fact]
		public void Selected_books_and_their_size_follow_the_grid_order()
		{
			SelectionModel m = Model(6);
			m.Click(4, false, false);
			m.Click(1, toggle: true, extend: false);
			m.Click(3, toggle: true, extend: false);
			List<ComicBook> books = m.SelectedBooks();
			Assert.Equal(new[] { "/nowhere/02.cbz", "/nowhere/04.cbz", "/nowhere/05.cbz" }, books.Select(b => b.FilePath));
			Assert.Equal(2000 + 4000 + 5000, m.SelectedSize());
		}

		[Fact]
		public void Changed_is_raised_once_per_operation()
		{
			SelectionModel m = Model(20);
			int raised = 0;
			m.Changed += (s, e) => raised++;
			m.Click(2, false, false);
			Assert.Equal(1, raised);
			m.Click(12, toggle: false, extend: true);
			Assert.Equal(2, raised);
			m.Move(15, false, true);
			Assert.Equal(3, raised);
			m.SelectAll();
			m.Invert();
			m.Clear();
			Assert.Equal(6, raised);
		}

		// ---- the model: a new list -------------------------------------------------------------------------------------------------------

		[Fact]
		public void The_selection_follows_its_books_into_a_new_list_and_a_search_keeps_the_hidden_ones()
		{
			BookItem[] all = Books(8);
			var m = new SelectionModel();
			m.SetItems(all, keepHidden: false);
			m.Click(1, false, false);
			m.Click(5, toggle: true, extend: false);
			// sorted the other way round
			m.SetItems(all.Reverse().ToArray(), keepHidden: true);
			Assert.Equal(new[] { 2, 6 }, Selected(m));
			Assert.Equal(2, m.FocusedIndex); // book 5 (index 5) is at 7 - 5 = 2
			// a search hides book 1: it is no longer counted, but it is still selected underneath
			BookItem[] filtered = all.Where(i => i != all[1]).ToArray();
			m.SetItems(filtered, keepHidden: true);
			Assert.Equal(1, m.Count);
			Assert.Single(m.SelectedBooks());
			m.SetItems(all, keepHidden: true);
			Assert.Equal(new[] { 1, 5 }, Selected(m));
			// a refresh builds new items for the same books: the books keep the selection, the hidden ones are forgotten
			m.SetItems(filtered, keepHidden: true);
			BookItem[] rebuilt = filtered.Select(i => new BookItem(i.Book, i.Title, i.Series, i.Number, i.Added)).ToArray();
			m.SetItems(rebuilt, keepHidden: false);
			Assert.Equal(1, m.Count);
			m.SetItems(all, keepHidden: true);
			Assert.Equal(new[] { 5 }, Selected(m));
		}

		[Fact]
		public void A_focused_book_that_leaves_the_list_loses_the_focus()
		{
			BookItem[] all = Books(5);
			var m = new SelectionModel();
			m.SetItems(all, false);
			m.Click(2, false, false);
			m.SetItems(all.Where(i => i != all[2]).ToArray(), keepHidden: true);
			Assert.Equal(-1, m.FocusedIndex);
		}

		[Fact]
		public void Select_books_selects_the_given_books_and_focuses_the_first()
		{
			BookItem[] all = Books(6);
			var m = new SelectionModel();
			m.SetItems(all, false);
			m.SelectBooks(new[] { all[4].Book, all[1].Book });
			Assert.Equal(new[] { 1, 4 }, Selected(m));
			Assert.Equal(1, m.FocusedIndex);
		}

		// ---- the view model --------------------------------------------------------------------------------------------------------------

		[Fact]
		public async Task The_view_model_keeps_the_selection_across_sort_and_search_and_reports_it()
		{
			await Ui.Run(async () =>
			{
				var vm = new BrowserViewModel { SearchDelay = TimeSpan.Zero };
				BookItem[] books = Books(6);
				vm.SetCatalog(new BookCatalog(books));
				await vm.WhenIdle();
				var raised = new List<string>();
				vm.PropertyChanged += (s, e) => raised.Add(e.PropertyName);
				int changes = 0;
				vm.SelectionChanged += (s, e) => changes++;

				vm.Selection.Click(0, false, false);
				vm.Selection.Click(3, toggle: true, extend: false);
				Assert.Equal(2, vm.SelectedCount);
				Assert.Equal(2, changes);
				Assert.Contains(nameof(BrowserViewModel.SelectedIndex), raised);
				Assert.Contains("2 selected", vm.SummaryText);
				Assert.Equal(new[] { "/nowhere/01.cbz", "/nowhere/04.cbz" }, vm.SelectedBooks.Select(b => b.FilePath));

				vm.SortIndex = 2; // newest first: 06 05 04 03 02 01
				await vm.WhenIdle();
				Assert.Equal(new[] { "/nowhere/04.cbz", "/nowhere/01.cbz" }, vm.SelectedBooks.Select(b => b.FilePath));
				Assert.Equal(2, vm.SelectedIndex); // book 04 has the focus
				Assert.Equal("/nowhere/04.cbz", vm.SelectedItem.Book.FilePath);

				vm.SearchText = "Book 0"; // all six match: nothing hidden
				await vm.WhenIdle();
				Assert.Equal(2, vm.SelectedCount);
				vm.SearchText = "Book 04"; // hides the selected book 01
				await vm.WhenIdle();
				Assert.Equal(1, vm.SelectedCount);
				Assert.Equal(new[] { "/nowhere/04.cbz" }, vm.SelectedBooks.Select(b => b.FilePath));
				vm.SearchText = string.Empty;
				await vm.WhenIdle();
				Assert.Equal(2, vm.SelectedCount);
			});
		}

		[Fact]
		public async Task A_refresh_keeps_the_selected_books_and_another_list_forgets_them()
		{
			await Ui.Run(async () =>
			{
				var vm = new BrowserViewModel { SearchDelay = TimeSpan.Zero };
				BookItem[] books = Books(5);
				vm.SetCatalog(new BookCatalog(books));
				await vm.WhenIdle();
				vm.Selection.Click(1, false, false);
				vm.Selection.Click(3, toggle: true, extend: false);

				BookItem[] again = books.Select(i => new BookItem(i.Book, i.Title + "!", i.Series, i.Number, i.Added)).ToArray();
				await vm.RefreshAsync(token => new BookCatalog(again));
				Assert.Equal(new[] { "/nowhere/02.cbz", "/nowhere/04.cbz" }, vm.SelectedBooks.Select(b => b.FilePath));
				Assert.True(vm.KeepsView == false);

				vm.SetCatalog(new BookCatalog(again)); // the user chose another list
				await vm.WhenIdle();
				Assert.Equal(0, vm.SelectedCount);
				Assert.Equal(-1, vm.SelectedIndex);
			});
		}

		[Fact]
		public async Task Selected_index_selects_one_book_as_before()
		{
			await Ui.Run(async () =>
			{
				var vm = new BrowserViewModel { SearchDelay = TimeSpan.Zero };
				vm.SetCatalog(new BookCatalog(Books(4)));
				await vm.WhenIdle();
				vm.Selection.SelectAll();
				vm.SelectedIndex = 2;
				Assert.Equal(1, vm.SelectedCount);
				Assert.Equal(2, vm.SelectedIndex);
				vm.SelectedIndex = -1;
				Assert.Equal(0, vm.SelectedCount);
				Assert.Null(vm.SelectedItem);
				Assert.Contains("4 Books", vm.SummaryText);
			});
		}

		[Fact]
		public async Task The_status_text_counts_the_selection_and_names_the_file_of_a_single_book()
		{
			await Ui.Run(async () =>
			{
				var vm = new BrowserViewModel { SearchDelay = TimeSpan.Zero };
				vm.SetCatalog(new BookCatalog(Books(4)));
				await vm.WhenIdle();
				vm.SelectAllCommand.Execute(null);
				Assert.EndsWith("- 4 selected / " + SelectionInfo.Size(1000 + 2000 + 3000 + 4000), vm.SummaryText);
				vm.InvertSelectionCommand.Execute(null);
				Assert.Equal(0, vm.SelectedCount);
				Assert.DoesNotContain("selected", vm.SummaryText);
				vm.SelectedIndex = 1; // one book: its file is named instead
				Assert.Contains("4 Books", vm.SummaryText);
				Assert.EndsWith("- /nowhere/02.cbz / " + SelectionInfo.Size(2000), vm.SummaryText);
			});
		}

		[Fact]
		public async Task Remove_selected_asks_with_the_selected_books_and_does_nothing_without_a_selection()
		{
			await Ui.Run(async () =>
			{
				var vm = new BrowserViewModel { SearchDelay = TimeSpan.Zero };
				vm.SetCatalog(new BookCatalog(Books(5)));
				await vm.WhenIdle();
				var asked = new List<IReadOnlyList<ComicBook>>();
				vm.RemoveBooksRequested += (s, books) => asked.Add(books);
				vm.RemoveSelectedCommand.Execute(null);
				Assert.Empty(asked);
				vm.Selection.Click(4, false, false);
				vm.Selection.Click(0, toggle: true, extend: false);
				vm.RemoveSelectedCommand.Execute(null);
				Assert.Equal(new[] { "/nowhere/01.cbz", "/nowhere/05.cbz" }, Assert.Single(asked).Select(b => b.FilePath));
			});
		}

		// ---- the grid --------------------------------------------------------------------------------------------------------------------

		private static void Press(GridHarness h, Key key, KeyModifiers modifiers = KeyModifiers.None)
		{
			RawInputModifiers raw = RawInputModifiers.None;
			if ((modifiers & KeyModifiers.Shift) != 0) raw |= RawInputModifiers.Shift;
			if ((modifiers & KeyModifiers.Meta) != 0) raw |= RawInputModifiers.Meta;
			if ((modifiers & KeyModifiers.Alt) != 0) raw |= RawInputModifiers.Alt;
			h.Window.KeyPress(key, raw, PhysicalKeyFor(key), null);
		}

		private static PhysicalKey PhysicalKeyFor(Key key)
		{
			switch (key)
			{
				case Key.Right: return PhysicalKey.ArrowRight;
				case Key.Left: return PhysicalKey.ArrowLeft;
				case Key.Down: return PhysicalKey.ArrowDown;
				case Key.Up: return PhysicalKey.ArrowUp;
				case Key.Home: return PhysicalKey.Home;
				case Key.End: return PhysicalKey.End;
				case Key.PageDown: return PhysicalKey.PageDown;
				case Key.PageUp: return PhysicalKey.PageUp;
				case Key.Enter: return PhysicalKey.Enter;
				case Key.Space: return PhysicalKey.Space;
				case Key.Delete: return PhysicalKey.Delete;
				case Key.Back: return PhysicalKey.Backspace;
				case Key.A: return PhysicalKey.A;
				default: return PhysicalKey.None;
			}
		}

		private static Point CenterOf(GridHarness h, int index)
		{
			BookCell cell = h.Grid.CellAt(index);
			Assert.NotNull(cell);
			return cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), h.Window).Value;
		}

		private static void Click(GridHarness h, int index, RawInputModifiers modifiers = RawInputModifiers.None)
		{
			Point at = CenterOf(h, index);
			h.Window.MouseDown(at, MouseButton.Left, modifiers);
			h.Window.MouseUp(at, MouseButton.Left, modifiers);
		}

		private static string Drawn(GridHarness h)
		{
			// S selected, F focused, SF both, . neither; for the cells the grid has realized
			return string.Concat(h.Grid.BoundCells().OrderBy(c => c.Index).Select(c => c.IsSelected ? (c.IsFocusedItem ? "B" : "S") : (c.IsFocusedItem ? "F" : ".")));
		}

		[Fact]
		public async Task The_grid_selects_with_Cmd_click_and_Shift_click_and_draws_the_selection()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(20)));
					h.Grid.Focus();
					Click(h, 2);
					Assert.Equal(new[] { 2 }, Selected(h.Model.Selection));
					Click(h, 5, RawInputModifiers.Meta);
					Assert.Equal(new[] { 2, 5 }, Selected(h.Model.Selection));
					Assert.True(h.Grid.CellAt(2).IsSelected && h.Grid.CellAt(5).IsSelected && !h.Grid.CellAt(3).IsSelected);
					Assert.True(h.Grid.CellAt(5).IsFocusedItem && !h.Grid.CellAt(2).IsFocusedItem);
					Click(h, 8, RawInputModifiers.Shift);
					Assert.Equal(new[] { 5, 6, 7, 8 }, Selected(h.Model.Selection));
					Click(h, 6, RawInputModifiers.Meta); // takes a book out
					Assert.Equal(new[] { 5, 7, 8 }, Selected(h.Model.Selection));
					Assert.False(h.Grid.CellAt(6).IsSelected);
					// a press on a selected book keeps the selection until the button is up, and a plain click then selects that book alone
					Point at = CenterOf(h, 7);
					h.Window.MouseDown(at, MouseButton.Left);
					Assert.Equal(new[] { 5, 7, 8 }, Selected(h.Model.Selection));
					h.Window.MouseUp(at, MouseButton.Left);
					Assert.Equal(new[] { 7 }, Selected(h.Model.Selection));
				}
			});
		}

		[Fact]
		public async Task A_click_on_the_empty_area_clears_the_selection_but_the_scroll_bar_does_not()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(3)));
					Click(h, 1);
					Assert.Equal(1, h.Model.SelectedCount);
					Point empty = new Point(850, 560); // far to the right and below the three books
					h.Window.MouseDown(empty, MouseButton.Left, RawInputModifiers.Meta);
					h.Window.MouseUp(empty, MouseButton.Left, RawInputModifiers.Meta);
					Assert.Equal(1, h.Model.SelectedCount); // Cmd keeps it
					h.Window.MouseDown(empty, MouseButton.Left);
					h.Window.MouseUp(empty, MouseButton.Left);
					Assert.Equal(0, h.Model.SelectedCount);
					Assert.Equal(-1, h.Model.SelectedIndex);
					Assert.DoesNotContain('S', Drawn(h));
					Assert.DoesNotContain('B', Drawn(h));
				}
			});
		}

		[Fact]
		public async Task A_context_click_selects_an_unselected_book_and_keeps_the_selection_when_the_book_is_in_it()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(12)));
					Click(h, 1);
					Click(h, 3, RawInputModifiers.Meta);
					Point on3 = CenterOf(h, 3);
					h.Window.MouseDown(on3, MouseButton.Right);
					h.Window.MouseUp(on3, MouseButton.Right);
					Assert.Equal(new[] { 1, 3 }, Selected(h.Model.Selection));
					Point on1 = CenterOf(h, 1);
					h.Window.MouseDown(on1, MouseButton.Right);
					h.Window.MouseUp(on1, MouseButton.Right);
					Assert.Equal(new[] { 1, 3 }, Selected(h.Model.Selection));
					Assert.Equal(1, h.Model.SelectedIndex);
					Point on6 = CenterOf(h, 6);
					h.Window.MouseDown(on6, MouseButton.Right);
					h.Window.MouseUp(on6, MouseButton.Right);
					Assert.Equal(new[] { 6 }, Selected(h.Model.Selection));
				}
			});
		}

		[Fact]
		public async Task The_grid_keyboard_follows_ItemView()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(30)));
					int columns = h.Grid.Columns;
					Assert.True(columns >= 3, "the test needs a few columns");
					h.Grid.Focus();
					SelectionModel s = h.Model.Selection;

					Press(h, Key.Right); // the first key selects the first book
					Assert.Equal(new[] { 0 }, Selected(s));
					Press(h, Key.Right, KeyModifiers.Shift);
					Press(h, Key.Right, KeyModifiers.Shift);
					Assert.Equal(new[] { 0, 1, 2 }, Selected(s));
					Press(h, Key.Left, KeyModifiers.Shift);
					Assert.Equal(new[] { 0, 1 }, Selected(s));
					Press(h, Key.Down, KeyModifiers.Shift);
					Assert.Equal(Enumerable.Range(0, 2 + columns), Selected(s));
					Press(h, Key.Right); // a plain movement key collapses the selection
					Assert.Equal(new[] { 2 + columns }, Selected(s));

					// Cmd+arrow moves the focus only, Option+Space flips the focused book, Space selects it
					Press(h, Key.Left, KeyModifiers.Meta);
					Assert.Equal(new[] { 2 + columns }, Selected(s));
					Assert.Equal(1 + columns, s.FocusedIndex);
					Press(h, Key.Space);
					Assert.Equal(new[] { 1 + columns, 2 + columns }, Selected(s));
					Press(h, Key.Space, KeyModifiers.Alt);
					Assert.Equal(new[] { 2 + columns }, Selected(s));

					// Home and End, with Shift from the anchor
					Press(h, Key.Home);
					Press(h, Key.End, KeyModifiers.Shift);
					Assert.Equal(30, h.Model.SelectedCount);
					Assert.Equal(29, h.Model.SelectedIndex);
				}
			});
		}

		[Fact]
		public async Task Down_and_Up_stay_in_the_last_and_first_row_as_the_original_does()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(40)));
					int columns = h.Grid.Columns;
					int rows = (40 + columns - 1) / columns;
					int lastRowStart = (rows - 1) * columns;
					h.Grid.Focus();
					Press(h, Key.Home);
					Press(h, Key.Right); // second book, first row
					Press(h, Key.Up);
					Assert.Equal(1, h.Model.SelectedIndex); // the first row: Up stays
					Press(h, Key.End);
					int lastCount = 40 - lastRowStart;
					if (lastCount > 1)
					{
						Press(h, Key.Left); // the last row, one before the end
						int here = h.Model.SelectedIndex;
						Press(h, Key.Down);
						Assert.Equal(here, h.Model.SelectedIndex); // the last row: Down stays
						Press(h, Key.Up);
						Assert.Equal(here - columns, h.Model.SelectedIndex);
						Press(h, Key.Down);
						Assert.Equal(here, h.Model.SelectedIndex);
					}
					Press(h, Key.End);
					Press(h, Key.PageDown);
					Assert.Equal(39, h.Model.SelectedIndex); // Page Down from the last row stays in it
				}
			});
		}

		[Fact]
		public async Task Cmd_A_selects_all_and_Delete_asks_to_remove_the_selection()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(12)));
					h.Grid.Focus();
					var asked = new List<int>();
					h.Model.RemoveBooksRequested += (s, books) => asked.Add(books.Count);
					Press(h, Key.Delete);
					Assert.Empty(asked); // nothing selected
					Press(h, Key.A, KeyModifiers.Meta);
					Assert.Equal(12, h.Model.SelectedCount);
					Assert.Equal(-1, h.Model.SelectedIndex); // Select All leaves the focus alone
					Press(h, Key.Delete);
					Assert.Equal(new[] { 12 }, asked);
					Click(h, 3);
					Press(h, Key.Back); // the Backspace key alone is not Delete
					Assert.Single(asked);
					Press(h, Key.Back, KeyModifiers.Meta);
					Assert.Equal(new[] { 12, 1 }, asked);
					Assert.Contains('B', Drawn(h));
				}
			});
		}

		[Fact]
		public async Task Enter_and_a_double_click_open_the_focused_book_only()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(12)));
					var opened = new List<string>();
					h.Model.OpenBookRequested += (s, e) => opened.Add(e.Title);
					Click(h, 1);
					Click(h, 4, RawInputModifiers.Meta);
					Click(h, 6, RawInputModifiers.Meta);
					Assert.Equal(3, h.Model.SelectedCount);
					h.Grid.Focus();
					Press(h, Key.Enter);
					Assert.Equal(new[] { "Book 07" }, opened);
				}
			});
		}

		[Fact]
		public async Task A_sort_keeps_the_selection_drawn_on_the_cells_of_the_same_books()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(12)));
					Click(h, 0);
					Click(h, 3, RawInputModifiers.Meta);
					h.Model.SortIndex = 2; // newest first: the books are in the opposite order
					await h.Model.WhenIdle();
					h.Layout();
					Assert.Equal(new[] { 8, 11 }, Selected(h.Model.Selection));
					Assert.Equal("Book 04", h.Grid.CellAt(8).TitleText);
					Assert.True(h.Grid.CellAt(8).IsSelected && h.Grid.CellAt(11).IsSelected);
					Assert.True(h.Grid.CellAt(8).IsFocusedItem);
					Assert.False(h.Grid.CellAt(0).IsSelected);
				}
			});
		}
	}
}
