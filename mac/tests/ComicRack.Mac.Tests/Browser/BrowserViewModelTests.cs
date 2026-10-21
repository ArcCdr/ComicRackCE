using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Browser;
using ComicRack.Mac.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Mac.Tests.Browser
{
	public class BrowserViewModelTests
	{
		private readonly ITestOutputHelper output;

		public BrowserViewModelTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		private static BookItem Item(string title, string series, string number, int day = 1)
		{
			return new BookItem(null, title, series, number, new DateTime(2020, 1, 1).AddDays(day));
		}

		[Fact]
		public async Task Search_and_sort_changes_produce_the_list_and_the_summary()
		{
			await Ui.Run(async () =>
			{
				var vm = new BrowserViewModel { SearchDelay = TimeSpan.Zero };
				vm.SetCatalog(new BookCatalog(new[] { Item("b", "S", "2", day: 5), Item("a", "S", "1", day: 2), Item("c", "T", "1", day: 40) }));
				await vm.WhenIdle();
				Assert.Equal(new[] { "a", "b", "c" }, vm.Items.Select(i => i.Title));
				Assert.Equal("3 Books", vm.SummaryText);

				vm.SortIndex = 2; // date added, newest first
				await vm.WhenIdle();
				Assert.Equal("c", vm.Items[0].Title);

				vm.SearchText = "s";
				await vm.WhenIdle();
				Assert.Equal(new[] { "b", "a" }, vm.Items.Select(i => i.Title)); // the series S, newest first
				Assert.Equal("2 Books (1 filtered)", vm.SummaryText);
				Assert.True(vm.ConsumeScrollReset());
				Assert.False(vm.ConsumeScrollReset());

				vm.SearchText = string.Empty;
				await vm.WhenIdle();
				Assert.Equal(3, vm.Items.Length);
			});
		}

		[Fact]
		public async Task Flipping_the_sort_order_reverses_the_list_and_the_picture_and_the_same_sort_chosen_again_flips_while_another_starts_ascending()
		{
			await Ui.Run(async () =>
			{
				var vm = new BrowserViewModel { SearchDelay = TimeSpan.Zero };
				vm.SetCatalog(new BookCatalog(new[] { Item("b", "S", "2", day: 5), Item("a", "S", "1", day: 2), Item("c", "T", "1", day: 40) }));
				await vm.WhenIdle();
				Assert.False(vm.SortDescending);
				Assert.Equal("SortUp", vm.SortIconName);
				Assert.Equal(new[] { "a", "b", "c" }, vm.Items.Select(i => i.Title));

				vm.FlipSortOrder(); // the Arrange button
				await vm.WhenIdle();
				Assert.True(vm.SortDescending);
				Assert.Equal("SortDown", vm.SortIconName);
				Assert.Equal(new[] { "c", "b", "a" }, vm.Items.Select(i => i.Title));

				vm.ArrangeBy(0); // the sort the list already has, from the menu: flips back
				await vm.WhenIdle();
				Assert.False(vm.SortDescending);
				Assert.Equal(new[] { "a", "b", "c" }, vm.Items.Select(i => i.Title));

				vm.ArrangeBy(1); // another sort starts ascending
				await vm.WhenIdle();
				Assert.Equal(1, vm.SortIndex);
				Assert.False(vm.SortDescending);
				vm.FlipSortOrder();
				await vm.WhenIdle();
				Assert.Equal(new[] { "c", "b", "a" }, vm.Items.Select(i => i.Title));

				// another sort while descending: ascending again, and one query only (the list is not built for the half way state)
				var applied = new List<string[]>();
				vm.PropertyChanged += (s, e) =>
				{
					if (e.PropertyName == nameof(BrowserViewModel.Items))
					{
						applied.Add(vm.Items.Select(i => i.Title).ToArray());
					}
				};
				vm.ArrangeBy(2);
				await vm.WhenIdle();
				Assert.False(vm.SortDescending);
				Assert.Equal(2, vm.SortIndex);
				Assert.Equal("SortUp", vm.SortIconName);
				Assert.Single(applied);
				Assert.Equal(new[] { "c", "b", "a" }, applied[0]); // date added: newest first, the order of the MVP sort key

				// a search keeps the order that was chosen
				vm.FlipSortOrder();
				vm.SearchText = "s";
				await vm.WhenIdle();
				Assert.Equal(new[] { "a", "b" }, vm.Items.Select(i => i.Title));
			});
		}

		[Fact]
		public async Task A_burst_of_keystrokes_runs_one_query_and_only_the_last_text_is_applied()
		{
			await Ui.Run(async () =>
			{
				var vm = new BrowserViewModel { SearchDelay = TimeSpan.FromMilliseconds(150) };
				vm.SetCatalog(new BookCatalog(SyntheticItems.Make(2000)));
				await vm.WhenIdle();
				var applied = new List<int>();
				vm.PropertyChanged += (s, e) =>
				{
					if (e.PropertyName == nameof(BrowserViewModel.Items))
					{
						applied.Add(vm.Items.Length);
					}
				};
				foreach (string typed in new[] { "l", "lu", "lun", "lune" })
				{
					vm.SearchText = typed;
					await Task.Delay(20); // faster than the delay
				}
				await vm.WhenIdle();
				Assert.Single(applied);
				Assert.Equal(new BookCatalog(vm.Items).Count, applied[0]);
				Assert.All(vm.Items, i => Assert.True(i.TitleKey.Contains("lune") || i.SeriesKey.Contains("lune")));
			});
		}

		[Fact]
		public async Task The_selection_follows_the_book_when_the_list_is_sorted_or_filtered()
		{
			await Ui.Run(async () =>
			{
				var vm = new BrowserViewModel { SearchDelay = TimeSpan.Zero };
				vm.SetCatalog(new BookCatalog(new[] { Item("a", "S", "1", day: 1), Item("b", "S", "2", day: 2), Item("c", "S", "3", day: 3) }));
				await vm.WhenIdle();
				vm.SelectedIndex = 0; // "a"
				Assert.Equal("a", vm.SelectedItem.Title);
				vm.SortIndex = 2; // newest first: c, b, a
				await vm.WhenIdle();
				Assert.Equal(2, vm.SelectedIndex);
				Assert.Equal("a", vm.SelectedItem.Title);
				vm.SearchText = "c"; // a is filtered out
				await vm.WhenIdle();
				Assert.Equal(-1, vm.SelectedIndex);
				Assert.Null(vm.SelectedItem);
			});
		}

		[Fact]
		public async Task Open_selected_raises_the_event_only_with_a_selection()
		{
			await Ui.Run(async () =>
			{
				var vm = new BrowserViewModel { SearchDelay = TimeSpan.Zero };
				vm.SetCatalog(new BookCatalog(new[] { Item("a", "S", "1") }));
				await vm.WhenIdle();
				var opened = new List<BookItem>();
				vm.OpenBookRequested += (s, e) => opened.Add(e);
				vm.OpenSelected();
				Assert.Empty(opened);
				vm.SelectedIndex = 0;
				vm.OpenSelectedCommand.Execute(null);
				Assert.Equal("a", Assert.Single(opened).Title);
			});
		}

		[Fact]
		public async Task Queries_of_a_big_list_do_not_block_the_ui_thread()
		{
			await Ui.Run(async () =>
			{
				var vm = new BrowserViewModel { SearchDelay = TimeSpan.Zero };
				BookItem[] items = await Task.Run(() => SyntheticItems.Make(100000));
				vm.SetCatalog(new BookCatalog(items));
				await vm.WhenIdle();

				// A heartbeat on the UI thread while the first sort of each key, searches and a filter back to everything run.
				long maxGap = 0;
				int beats = 0;
				bool stop = false;
				Task beat = Task.Run(async () =>
				{
					var sw = Stopwatch.StartNew();
					long last = 0;
					while (!stop)
					{
						await Dispatcher_Invoke();
						long now = sw.ElapsedMilliseconds;
						maxGap = Math.Max(maxGap, now - last);
						last = now;
						beats++;
						await Task.Delay(2);
					}
				});
				var clock = Stopwatch.StartNew();
				foreach (int sort in new[] { 1, 2, 0 })
				{
					vm.SortIndex = sort;
					await vm.WhenIdle();
				}
				foreach (string text in new[] { "l", "lu", "lune", "lune dragon", "" })
				{
					vm.SearchText = text;
					await vm.WhenIdle();
				}
				long total = clock.ElapsedMilliseconds;
				stop = true;
				await beat;
				output.WriteLine($"3 sorts and 5 searches over 100,000 items: {total} ms in all, {beats} UI beats, longest UI gap {maxGap} ms, last query {vm.LastQueryMilliseconds:0.0} ms");
				Assert.True(maxGap < 150, "the UI thread was blocked for " + maxGap + " ms");
				Assert.Equal(100000, vm.Items.Length);
			});
		}

		private static Task Dispatcher_Invoke() => UiDispatch.InvokeAsync(() => { });
	}
}
