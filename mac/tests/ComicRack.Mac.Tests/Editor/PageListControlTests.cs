using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Tests.HiDpi;
using Xunit;
using System.Threading.Tasks;

namespace ComicRack.Mac.Tests.Editor
{
	/// <summary>P5.7c: the page list control (<see cref="CrPageList"/>, IN.06): geometry, selection by mouse and keys, header clicks and column widths, scrolling, sizes, the three views.</summary>
	public class PageListControlTests
	{
		private sealed class Harness : IDisposable
		{
			public readonly Window Window;
			public readonly CrPageList List = new CrPageList();
			public readonly FakePageSource Source;
			public readonly List<int> Headers = new List<int>();
			public readonly List<int> Activated = new List<int>();
			public int ViewChanges;

			public Harness(int rows = 30, double width = 500, double height = 300)
			{
				Source = new FakePageSource(rows);
				List.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
				List.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
				List.HeaderClicked += (s, id) => Headers.Add(id);
				List.ItemActivated += (s, row) => Activated.Add(row);
				List.ViewChanged += (s, e) => ViewChanges++;
				Window = new Window { Width = width, Height = height, Content = List };
				Window.Show();
				List.Source = Source;
				Settle();
			}

			public void Settle() => Dpi.Settle(Window);

			public void Click(Point p, KeyModifiers modifiers = KeyModifiers.None, MouseButton button = MouseButton.Left)
			{
				RawInputModifiers raw = Raw(modifiers);
				Window.MouseDown(p, button, raw);
				Window.MouseUp(p, button, raw);
				Settle();
			}

			public static RawInputModifiers Raw(KeyModifiers m)
			{
				RawInputModifiers raw = RawInputModifiers.None;
				if ((m & KeyModifiers.Shift) != 0) raw |= RawInputModifiers.Shift;
				if ((m & KeyModifiers.Meta) != 0) raw |= RawInputModifiers.Meta;
				return raw;
			}

			public Point Center(int row) => new Point(60, List.RowRect(row).Y + List.RowHeight / 2);

			public void Key(Avalonia.Input.Key key, PhysicalKey physical, KeyModifiers modifiers = KeyModifiers.None)
			{
				Window.KeyPress(key, Raw(modifiers), physical, null);
				Settle();
			}

			public void Dispose() => Window.Close();
		}

		private static int[] Selected(Harness h) => h.Source.Selection.ToArray();

		[Fact]
		public async Task The_detail_view_has_a_20_high_header_and_rows_of_the_item_font_height_plus_6()
		{
			await Ui.Run(() =>
			{
				using (var h = new Harness())
				{
					Assert.Equal(PageListMode.Detail, h.List.Mode);
					Assert.Equal(ChromeMetrics.ColumnHeaderHeight, h.List.ViewRect.Y);
					double row = Math.Ceiling(ChromeFonts.LineHeight(ChromeFonts.ItemSize)) + 6;
					Assert.Equal(row, h.List.RowHeight);
					Assert.Equal(new Rect(0, 20, 500 - h.List.VerticalBar.DesiredSize.Width, 300 - 20 - h.List.HorizontalBar.DesiredSize.Height), h.List.ViewRect);
					Assert.Equal(20 + row * 3, h.List.RowRect(3).Y, 3);
					Assert.Equal(0, h.List.RowAt(new Point(10, 21)));
					Assert.Equal(1, h.List.RowAt(new Point(10, 20 + row + 1)));
					Assert.Equal(-1, h.List.RowAt(new Point(10, 5))); // the header
					Assert.Equal(820, h.List.ColumnsWidth); // 40 + 80 + 80 + 80 + 60 + 60 + 150 + 150 + 60 + 60
					Assert.Equal(new Rect(40, 0, 80, 20), h.List.HeaderRect(1));
					Assert.Equal(1, h.List.HeaderAt(new Point(60, 10)));
					Assert.True(h.List.VerticalBar.IsVisible); // 30 rows do not fit
					Assert.True(h.List.HorizontalBar.IsVisible); // 820 units of columns in 500
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_scroll_bars_follow_the_content_and_the_offset_never_leaves_it()
		{
			await Ui.Run(() =>
			{
				using (var h = new Harness(rows: 3, width: 900))
				{
					Assert.False(h.List.VerticalBar.IsVisible);
					Assert.False(h.List.HorizontalBar.IsVisible);
				}
				using (var h = new Harness(rows: 100))
				{
					h.Window.MouseWheel(h.Center(2), new Vector(0, -50)); // far down
					h.Settle();
					double max = h.List.ContentSize.Height - h.List.ViewRect.Height;
					Assert.True(Math.Abs(max - h.List.Offset.Y) <= 1, "offset " + h.List.Offset + ", content " + h.List.ContentSize + ", view " + h.List.ViewRect + ", bar " + h.List.VerticalBar.Maximum);
					h.Window.MouseWheel(new Point(60, 100), new Vector(0, 500));
					h.Settle();
					Assert.Equal(0, h.List.Offset.Y);
					Assert.Equal(h.List.Offset.Y, h.List.VerticalBar.Value);
					h.List.EnsureVisible(99);
					Assert.True(Math.Abs(max - h.List.Offset.Y) <= 1, "after EnsureVisible: offset " + h.List.Offset + ", max " + max + ", row 99 " + h.List.RowRect(99) + ", view " + h.List.ViewRect);
					h.Source.Rows = 5; // fewer rows: the offset comes back
					h.Source.RaiseChanged();
					h.Settle();
					Assert.Equal(0, h.List.Offset.Y);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_click_selects_Cmd_toggles_Shift_extends_from_the_anchor_and_the_empty_area_clears()
		{
			await Ui.Run(() =>
			{
				using (var h = new Harness())
				{
					h.Click(h.Center(2));
					Assert.Equal(new[] { 2 }, Selected(h));
					h.Click(h.Center(4), KeyModifiers.Meta);
					Assert.Equal(new[] { 2, 4 }, Selected(h));
					h.Click(h.Center(2), KeyModifiers.Meta);
					Assert.Equal(new[] { 4 }, Selected(h));
					h.Click(h.Center(7), KeyModifiers.Shift); // from the anchor: the row of the last plain click (2), not the toggled ones
					Assert.Equal(new[] { 2, 3, 4, 5, 6, 7 }, Selected(h));
					h.Click(h.Center(1), KeyModifiers.Shift);
					Assert.Equal(new[] { 1, 2 }, Selected(h));
					h.Click(h.Center(9), KeyModifiers.Shift | KeyModifiers.Meta); // adds the range to the selection
					Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, Selected(h));
					h.Click(h.Center(3));
					Assert.Equal(new[] { 3 }, Selected(h));
				}
				using (var h = new Harness(rows: 4, width: 900, height: 300))
				{
					h.Click(h.Center(1));
					Assert.Equal(new[] { 1 }, Selected(h));
					h.Click(new Point(100, 200)); // under the four rows
					Assert.Empty(Selected(h));
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_context_click_selects_a_page_that_was_not_selected_and_keeps_the_selection_of_one_that_was()
		{
			await Ui.Run(() =>
			{
				using (var h = new Harness())
				{
					h.Click(h.Center(1));
					h.Click(h.Center(3), KeyModifiers.Meta);
					h.Click(h.Center(5), button: MouseButton.Right);
					Assert.Equal(new[] { 5 }, Selected(h)); // not selected before: it is the only one now
					h.Click(h.Center(1), KeyModifiers.Meta);
					h.Click(h.Center(1), button: MouseButton.Right);
					Assert.Equal(new[] { 1, 5 }, Selected(h)); // a selected page: the selection stays
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_arrow_keys_move_the_focus_Shift_extends_Space_toggles_Return_activates_and_Cmd_A_selects_all()
		{
			await Ui.Run(() =>
			{
				using (var h = new Harness())
				{
					h.List.Focus();
					h.Settle();
					h.Key(Avalonia.Input.Key.Down, PhysicalKey.ArrowDown);
					Assert.Equal(new[] { 1 }, Selected(h)); // the first focus was row 0; Down moves to row 1
					h.Key(Avalonia.Input.Key.Down, PhysicalKey.ArrowDown);
					h.Key(Avalonia.Input.Key.Down, PhysicalKey.ArrowDown, KeyModifiers.Shift);
					Assert.Equal(new[] { 2, 3 }, Selected(h)); // the range from the row that was the start
					Assert.Equal(3, h.List.FocusRow);
					h.Key(Avalonia.Input.Key.Home, PhysicalKey.Home);
					Assert.Equal(new[] { 0 }, Selected(h));
					h.Key(Avalonia.Input.Key.End, PhysicalKey.End);
					Assert.Equal(new[] { 29 }, Selected(h));
					Assert.True(h.List.RowRect(29).Bottom <= h.List.ViewRect.Bottom + 0.5); // scrolled to it
					h.Key(Avalonia.Input.Key.Up, PhysicalKey.ArrowUp, KeyModifiers.Meta); // Cmd moves the focus only
					Assert.Equal(new[] { 29 }, Selected(h));
					Assert.Equal(28, h.List.FocusRow);
					h.Key(Avalonia.Input.Key.Space, PhysicalKey.Space, KeyModifiers.Meta);
					Assert.Equal(new[] { 28, 29 }, Selected(h));
					h.Key(Avalonia.Input.Key.Enter, PhysicalKey.Enter);
					Assert.Equal(new[] { 28 }, h.Activated);
					h.Key(Avalonia.Input.Key.A, PhysicalKey.A, KeyModifiers.Meta);
					Assert.Equal(30, Selected(h).Length);
					h.Key(Avalonia.Input.Key.PageUp, PhysicalKey.PageUp);
					Assert.True(Selected(h).Single() < 28);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_double_click_activates_the_page()
		{
			await Ui.Run(() =>
			{
				using (var h = new Harness())
				{
					Point p = h.Center(3);
					h.Window.MouseDown(p, MouseButton.Left);
					h.Window.MouseUp(p, MouseButton.Left);
					h.Window.MouseDown(p, MouseButton.Left);
					h.Window.MouseUp(p, MouseButton.Left);
					h.Settle();
					Assert.Equal(new[] { 3 }, h.Activated);
					Assert.Equal(new[] { 3 }, Selected(h));
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_header_click_reports_the_column_unless_it_cannot_sort_and_the_separator_resizes_the_column()
		{
			await Ui.Run(() =>
			{
				using (var h = new Harness())
				{
					h.Click(new Point(60, 10)); // Thumbnail: no sorter
					Assert.Empty(h.Headers);
					h.Click(new Point(150, 10)); // Type (120..200)
					h.Click(new Point(10, 10)); // Page
					Assert.Equal(new[] { PageColumns.Type, PageColumns.Page }, h.Headers);

					double before = h.Source.Columns[PageColumns.Type].Width;
					Point edge = new Point(h.List.HeaderRect(PageColumns.Type).Right, 10);
					h.Window.MouseDown(edge, MouseButton.Left);
					h.Window.MouseMove(new Point(edge.X + 25, 10));
					h.Window.MouseUp(new Point(edge.X + 25, 10), MouseButton.Left);
					h.Settle();
					Assert.Equal(before + 25, h.Source.Columns[PageColumns.Type].Width);
					Assert.True(h.ViewChanges >= 1);
					Assert.Equal(2, h.Headers.Count); // the drag was not a click
					Assert.Equal(845, h.List.ColumnsWidth);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Cmd_and_the_wheel_change_the_size_of_the_rows_the_thumbnails_and_the_tiles_inside_their_limits()
		{
			await Ui.Run(() =>
			{
				using (var h = new Harness())
				{
					double row = h.List.RowHeight;
					h.Window.MouseWheel(h.Center(2), new Vector(0, 1), RawInputModifiers.Meta);
					Assert.Equal(row + 1, h.List.RowHeight);
					for (int i = 0; i < 100; i++)
					{
						h.Window.MouseWheel(h.Center(2), new Vector(0, 1), RawInputModifiers.Meta);
					}
					Assert.Equal(CrPageList.MaxRowHeight, h.List.RowHeight);
					for (int i = 0; i < 100; i++)
					{
						h.Window.MouseWheel(h.Center(2), new Vector(0, -1), RawInputModifiers.Meta);
					}
					Assert.Equal(CrPageList.MinRowHeight, h.List.RowHeight);

					h.List.Mode = PageListMode.Thumbnail;
					h.Settle();
					Assert.Equal(128, h.List.ThumbnailHeight);
					h.Window.MouseWheel(new Point(100, 100), new Vector(0, 1), RawInputModifiers.Meta);
					Assert.Equal(144, h.List.ThumbnailHeight);
					h.List.Mode = PageListMode.Tile;
					h.Window.MouseWheel(new Point(100, 100), new Vector(0, -1), RawInputModifiers.Meta);
					Assert.Equal(80, h.List.TileHeight);
					h.List.ThumbnailHeight = 5000;
					Assert.Equal(CrPageList.MaxThumbHeight, h.List.ThumbnailHeight);
					h.List.TileHeight = 1;
					Assert.Equal(CrPageList.MinTileHeight, h.List.TileHeight);
					Assert.True(h.ViewChanges >= 4);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_thumbnail_and_tile_views_put_cells_in_rows_that_wrap_at_the_width()
		{
			await Ui.Run(() =>
			{
				using (var h = new Harness(rows: 40, width: 600, height: 400))
				{
					h.List.Mode = PageListMode.Thumbnail;
					h.Settle();
					Size cell = h.List.CellSize;
					Assert.Equal(128 + 8 + 4, cell.Width);
					Assert.Equal(cell.Width, cell.Height);
					int perRow = (int)Math.Floor(h.List.ViewRect.Width / cell.Width);
					Assert.Equal(new Rect(0, 0, cell.Width, cell.Height).Width, h.List.RowRect(0).Width);
					Assert.Equal(h.List.RowRect(0).Y, h.List.RowRect(perRow - 1).Y);
					Assert.Equal(h.List.RowRect(0).Y + cell.Height, h.List.RowRect(perRow).Y);
					Assert.Equal(perRow, h.List.RowAt(new Point(5, h.List.RowRect(perRow).Y + 5)));
					Assert.Equal(perRow - 1, h.List.RowAt(new Point(h.List.RowRect(perRow - 1).X + 5, 5)));
					Assert.Equal(Math.Ceiling(40.0 / perRow) * cell.Height, h.List.ContentSize.Height);
					Assert.False(h.List.HorizontalBar.IsVisible);

					h.List.Mode = PageListMode.Tile;
					h.Settle();
					Assert.Equal(new Size(192 + 8, 96 + 8), h.List.CellSize);
					Assert.Equal(-1, h.List.HeaderAt(new Point(5, 5))); // no header outside the detail view
					Assert.Equal(0, h.List.ViewRect.Y);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Only_the_pictures_of_the_rows_on_screen_are_asked_for_at_the_size_of_the_view()
		{
			await Ui.Run(() =>
			{
				using (var h = new Harness(rows: 200))
				{
					h.Source.PictureRequests.Clear();
					h.List.InvalidateVisual();
					h.Settle();
					int visible = (int)Math.Ceiling(h.List.ViewRect.Height / h.List.RowHeight) + 1;
					Assert.NotEmpty(h.Source.PictureRequests);
					Assert.True(h.Source.PictureRequests.All(r => r.Row <= visible), "rows asked for: " + string.Join(",", h.Source.PictureRequests.Select(r => r.Row)));
					Assert.All(h.Source.PictureRequests, r => Assert.Equal(256, r.Height)); // the detail view asks for 256 whatever the row height is
					h.List.Mode = PageListMode.Thumbnail;
					h.Source.PictureRequests.Clear();
					h.Settle();
					Assert.All(h.Source.PictureRequests, r => Assert.Equal(128, r.Height));
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Changing_the_source_resets_the_focus_and_a_changed_source_redraws()
		{
			await Ui.Run(() =>
			{
				using (var h = new Harness())
				{
					h.Click(h.Center(4));
					Assert.Equal(4, h.List.FocusRow);
					var other = new FakePageSource(2);
					h.List.Source = other;
					Assert.Equal(-1, h.List.FocusRow);
					h.Settle();
					Assert.False(h.List.VerticalBar.IsVisible);
					h.List.Source = null;
					h.Settle(); // draws the paper only
					Assert.Equal(-1, h.List.RowAt(new Point(20, 40)));
				}
				return Task.CompletedTask;
			});
		}
	}
}
