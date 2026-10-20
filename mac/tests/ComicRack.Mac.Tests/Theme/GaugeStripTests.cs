using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Themes;
using ComicRack.Mac.ViewModels;
using cYo.Projects.ComicRack.Engine.Database;
using SkiaSharp;
using Xunit;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>The counters of the library tree (LT.01, PR.10): the arithmetic of <c>LibraryTreeSkin</c>, the drawing in both appearances and at 1x and 2x, the row that gives them their room, the settings that switch them.</summary>
	public class GaugeStripTests
	{
		// six units per character: every text width is known
		private static double Width(string text) => text.Length * 6;

		private static GaugeCell[] Layout(int total, int unread, int added, bool showTotal = true, bool showUnread = true, bool showNew = true, bool numeric = true,
			double available = double.PositiveInfinity, double height = 18)
		{
			return GaugeStripLayout.Compute(total, unread, added, showTotal, showUnread, showNew, numeric, available, height, Width, out _).ToArray();
		}

		[Fact]
		public void The_total_alone_is_one_green_pill_of_the_text_plus_four_with_both_ends_rounded()
		{
			GaugeCell[] cells = Layout(50, 0, 0);
			GaugeCell cell = Assert.Single(cells);
			Assert.Equal(GaugeKind.Total, cell.Kind);
			Assert.Equal("50", cell.Text);
			Assert.Equal(new Rect(0, 1, 16, 16), cell.Bounds); // (int)12 + 4 wide, one unit off the top and the bottom of the row
			Assert.True(cell.RoundLeft && cell.RoundRight);
		}

		[Fact]
		public void Three_counters_go_from_the_right_total_unread_new_one_unit_apart_with_the_corners_of_the_original()
		{
			GaugeCell[] cells = Layout(100, 20, 5);
			Assert.Equal(new[] { GaugeKind.New, GaugeKind.Unread, GaugeKind.Total }, cells.Select(c => c.Kind));
			Assert.Equal(new[] { "5", "20", "100" }, cells.Select(c => c.Text));
			GaugeStripLayout.Compute(100, 20, 5, true, true, true, true, double.PositiveInfinity, 18, Width, out double width);
			Assert.Equal(10 + 16 + 22 + 2, width); // widths 10, 16, 22 and two gaps of one
			Assert.Equal(width, cells[2].Bounds.Right);
			Assert.Equal(cells[2].Bounds.Left - 1, cells[1].Bounds.Right);
			Assert.Equal(cells[1].Bounds.Left - 1, cells[0].Bounds.Right);
			Assert.Equal(0, cells[0].Bounds.Left);
			// LibraryTreeSkin.DrawMarkers: the total is rounded on the right only, the unread one on neither side, the new one on the left only
			Assert.Equal((false, true), (cells[2].RoundLeft, cells[2].RoundRight));
			Assert.Equal((false, false), (cells[1].RoundLeft, cells[1].RoundRight));
			Assert.Equal((true, false), (cells[0].RoundLeft, cells[0].RoundRight));
		}

		[Fact]
		public void Without_a_counter_for_the_new_books_the_unread_one_takes_them_along_and_a_zero_shows_nothing()
		{
			GaugeCell[] cells = Layout(100, 20, 5, showNew: false);
			Assert.Equal(new[] { GaugeKind.Unread, GaugeKind.Total }, cells.Select(c => c.Kind));
			Assert.Equal("25", cells[0].Text);
			Assert.Equal((true, false), (cells[0].RoundLeft, cells[0].RoundRight)); // rounded where it has no neighbour: left
			// a counter whose number is 0 is not drawn; nothing at all when the list has no books
			Assert.Equal(new[] { GaugeKind.Total }, Layout(100, 0, 0).Select(c => c.Kind));
			Assert.Equal(new[] { GaugeKind.New, GaugeKind.Total }, Layout(100, 0, 7).Select(c => c.Kind));
			Assert.Empty(Layout(0, 0, 0));
			Assert.Empty(Layout(100, 20, 5, showTotal: false, showUnread: false, showNew: false));
		}

		[Fact]
		public void Numbers_that_do_not_fit_behind_the_label_or_are_switched_off_become_bars()
		{
			// numbers: 16 wide plus the gap needs 17, which is more than the 17 units behind the label
			Assert.All(Layout(50, 0, 0, available: 18), c => Assert.Equal("50", c.Text));
			GaugeCell[] bars = Layout(50, 0, 0, available: 17);
			Assert.Equal(5, Assert.Single(bars).Bounds.Width); // a third of the room, at most 6
			Assert.Equal(string.Empty, bars[0].Text);
			Assert.Equal(6, Layout(50, 0, 0, numeric: false, available: 100).Single().Bounds.Width);
			Assert.Equal(6, Layout(50, 0, 0, numeric: false).Single().Bounds.Width);
			Assert.Equal(3, Layout(50, 0, 0, numeric: false, available: 9).Single().Bounds.Width);
			// 2 or less is not worth a bar
			Assert.Empty(Layout(50, 0, 0, numeric: false, available: 8));
			Assert.Equal(new[] { 6.0, 6.0, 6.0 }, Layout(9, 8, 7, numeric: false).Select(c => c.Bounds.Width));
		}

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task The_three_counters_are_drawn_in_their_token_colours_darker_at_the_bottom(string appearance)
		{
			await InAppearance(appearance, () =>
			{
				var strip = new CrGaugeStrip { Total = 100, Unread = 20, New = 5, Width = 90, Height = 18 };
				Window shell = ShowIn(strip, 110, 40);
				Assert.Equal(3, strip.Cells.Count);
				Raster frame = Dpi.Capture(shell, "p52-gauges-" + appearance);
				SKColor surface = Token(ThemeTokens.Names.Control, appearance);
				Point o = Dpi.Origin(strip, shell);
				double offset = strip.Bounds.Width - strip.Cells.Max(c => c.Bounds.Right);
				foreach (GaugeCell cell in strip.Cells)
				{
					SKColor colour = Token(cell.Kind == GaugeKind.Total ? ThemeTokens.Names.GaugeTotal : cell.Kind == GaugeKind.Unread ? ThemeTokens.Names.GaugeUnread : ThemeTokens.Names.GaugeNew, appearance);
					int x = (int)(o.X + offset + cell.Bounds.Left + 1);
					SKColor top = frame[x, (int)(o.Y + cell.Bounds.Top + 1)];
					SKColor bottom = frame[x, (int)(o.Y + cell.Bounds.Bottom - 2)];
					// the pill is the colour of its counter over the surface: alpha 128 at the top and 192 at the bottom, so the bottom is nearer to the colour
					Assert.True(Distance(bottom, colour) < Distance(top, colour), cell.Kind + ": top " + Describe(top) + " bottom " + Describe(bottom) + " colour " + Describe(colour));
					Assert.True(Distance(top, colour) < Distance(surface, colour), cell.Kind + " is not drawn: " + Describe(top));
				}
				shell.Close();
			});
		}

		private static double Distance(SKColor a, SKColor b) => Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue);

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		[Trait("Category", "HiDpi")]
		public async Task A_counter_keeps_its_logical_size_and_its_sides_are_crisp_at_every_scale(double scale)
		{
			await InAppearance("Light", () =>
			{
				var strip = new CrGaugeStrip { Total = 100, Unread = 20, Width = 70, Height = 18 };
				Window shell = ShowIn(strip, 100, 40, scale);
				GaugeCell total = strip.Cells.Single(c => c.Kind == GaugeKind.Total);
				Assert.Equal(18, strip.Bounds.Height);
				Assert.Equal(16, total.Bounds.Height);
				Assert.InRange(total.Bounds.Width, 16, 30); // the text plus 4
				Raster frame = Dpi.Capture(shell, "p52-gauges-hidpi@" + Scales.Tag(scale));
				Point o = Dpi.Origin(strip, shell);
				double offset = strip.Bounds.Width - strip.Cells.Max(c => c.Bounds.Right);
				SKColor surface = Token(ThemeTokens.Names.Control, "Light");
				int y = Scales.Device(o.Y + 9, scale);
				int left = Scales.Device(o.X + offset + total.Bounds.Left, scale);
				int right = Scales.Device(o.X + offset + total.Bounds.Right, scale);
				// the side of a pill is on a whole device pixel: surface before it, the counter's colour from it on; no blended pixel between
				Assert.True(Raster.Near(frame[left - 1, y], surface, 1), "before the left side " + Describe(frame[left - 1, y]));
				Assert.False(Raster.Near(frame[left, y], surface, 1), "the left side " + Describe(frame[left, y]));
				Assert.False(Raster.Near(frame[right - 1, y], surface, 1), "the right side " + Describe(frame[right - 1, y]));
				Assert.True(Raster.Near(frame[right, y], surface, 1), "behind the right side " + Describe(frame[right, y]));
				shell.Close();
			});
		}

		[Fact]
		public async Task The_row_gives_the_counters_what_the_label_leaves_and_trims_a_label_that_is_too_long()
		{
			await Ui.Run(() =>
			{
				var label = new TextBlock { Text = "Never Read", TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
				var strip = new CrGaugeStrip { Total = 14706, Unread = 14000, New = 3 };
				var row = new CrTreeRow { Icon = new CrIcon { IconName = "List" }, Label = label, Gauges = strip };
				Window wide = ShowIn(row, 300, 22);
				Assert.Equal(3, strip.Cells.Count);
				Assert.All(strip.Cells, c => Assert.NotEqual(string.Empty, c.Text));
				// the counters start 4 units behind the label and end at the end of the row
				Assert.Equal(row.Bounds.Width, strip.Bounds.Right);
				Assert.Equal(label.Bounds.Right + CrTreeView.RowSpacing, strip.Bounds.Left);
				double natural = label.Bounds.Width;
				wide.Close();

				// a row with room for 10 units behind the label: no numbers, three bars would be 3 wide each (a third of the room), so the layout takes one at a time
				var label2 = new TextBlock { Text = "Never Read", TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
				var strip2 = new CrGaugeStrip { Total = 14706 };
				var row2 = new CrTreeRow { Icon = new CrIcon { IconName = "List" }, Label = label2, Gauges = strip2, Width = CrTreeView.IconColumnWidth + CrTreeView.RowSpacing + natural + CrTreeView.RowSpacing + 12 };
				Window narrow = ShowIn(row2, 300, 22);
				GaugeCell bar = Assert.Single(strip2.Cells);
				Assert.Equal(string.Empty, bar.Text);
				Assert.Equal(4, bar.Bounds.Width);
				narrow.Close();

				// a row narrower than the label: the label is trimmed and nothing is left for the counters
				var label3 = new TextBlock { Text = "Never Read", TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
				var strip3 = new CrGaugeStrip { Total = 14706 };
				var row3 = new CrTreeRow { Icon = new CrIcon { IconName = "List" }, Label = label3, Gauges = strip3, Width = CrTreeView.IconColumnWidth + CrTreeView.RowSpacing + natural - 20 };
				Window tight = ShowIn(row3, 300, 22);
				Assert.True(label3.Bounds.Width < natural);
				Assert.Empty(strip3.Cells);
				tight.Close();
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_tree_follows_the_gauge_settings_of_the_original()
		{
			await Ui.Run(() =>
			{
				var item = new ComicSmartListItem("Alphas");
				var node = new SidebarNode(new ListNode(item, ListKind.Smart, requiresScripts: false, Array.Empty<ListNode>()), Array.Empty<SidebarNode>());
				node.SetStats(new ListStats(100, 20, 5));
				var vm = new SidebarViewModel();
				var prefs = new AppPreferences();
				vm.BindGauges(prefs);
				Assert.True(vm.GaugesVisible && vm.GaugeTotal && vm.GaugeUnread && vm.GaugeNew && vm.GaugeNumeric); // LibraryGauges.Default
				var tree = new CrTreeView
				{
					TextMember = "Name", IconMember = "IconName", TotalMember = "Count", UnreadMember = "UnreadCount", NewMember = "NewCount",
					ItemsSource = new[] { node },
					Width = 252, // the side panel gives the tree its width, and every row the width of the tree
				};
				tree.Bind(CrTreeView.GaugesVisibleProperty, new Avalonia.Data.Binding(nameof(SidebarViewModel.GaugesVisible)) { Source = vm });
				tree.Bind(CrTreeView.GaugeTotalProperty, new Avalonia.Data.Binding(nameof(SidebarViewModel.GaugeTotal)) { Source = vm });
				tree.Bind(CrTreeView.GaugeUnreadProperty, new Avalonia.Data.Binding(nameof(SidebarViewModel.GaugeUnread)) { Source = vm });
				tree.Bind(CrTreeView.GaugeNewProperty, new Avalonia.Data.Binding(nameof(SidebarViewModel.GaugeNew)) { Source = vm });
				tree.Bind(CrTreeView.GaugeNumericProperty, new Avalonia.Data.Binding(nameof(SidebarViewModel.GaugeNumeric)) { Source = vm });
				Window window = ShowIn(tree, 252, 100);
				CrGaugeStrip strip = tree.GetVisualDescendants().OfType<CrGaugeStrip>().Single();
				Assert.True(strip.IsVisible);
				Assert.Equal(new[] { "5", "20", "100" }, strip.Cells.Select(c => c.Text));

				prefs.LibraryGaugesFormat = LibraryGauges.Total | LibraryGauges.Unread | LibraryGauges.Numeric;
				Dpi.Settle(window);
				Assert.Equal(new[] { "25", "100" }, strip.Cells.Select(c => c.Text)); // the unread counter takes the new books
				prefs.LibraryGaugesFormat = LibraryGauges.Total | LibraryGauges.Numeric;
				Dpi.Settle(window);
				Assert.Equal(new[] { "100" }, strip.Cells.Select(c => c.Text));
				prefs.LibraryGaugesFormat = LibraryGauges.Total; // no Numeric: a bar
				Dpi.Settle(window);
				GaugeCell bar = Assert.Single(strip.Cells);
				Assert.Equal(string.Empty, bar.Text);
				prefs.DisplayLibraryGauges = false;
				Dpi.Settle(window);
				Assert.False(strip.IsVisible);
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Fact]
		public void A_node_takes_its_three_numbers_and_shows_no_counter_without_books_or_with_scripts()
		{
			var item = new ComicSmartListItem("Alphas");
			var node = new SidebarNode(new ListNode(item, ListKind.Smart, false, Array.Empty<ListNode>()), Array.Empty<SidebarNode>());
			Assert.Equal(-1, node.Count);
			node.SetStats(new ListStats(100, 20, 5));
			Assert.Equal((100, 20, 5), (node.Count, node.UnreadCount, node.NewCount));
			Assert.True(node.HasCount);
			node.SetStats(ListStats.Empty);
			Assert.False(node.HasCount);
		}
	}
}
