using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using Xunit;

namespace ComicRack.Mac.Tests.Browser
{
	/// <summary>
	/// RV6: the rubber band (marquee) of the original's ItemView (UpdateSelection, OnPaint), BV.11. The model rules, then the grid with real pointer events:
	/// where a press starts it (outside what an item draws), what it selects with and without Shift and Cmd, the focus, the auto scroll, and what is drawn.
	/// </summary>
	public class RubberBandTests
	{
		internal static BookItem[] Books(int count)
		{
			return Enumerable.Range(1, count).Select(i =>
				new BookItem(new ComicBook { FilePath = "/nowhere/" + i.ToString("000") + ".cbz", FileSize = i * 1000 }, "Book " + i.ToString("000"), "Series", i.ToString(), new DateTime(2020, 1, 1).AddDays(i))).ToArray();
		}

		private static SelectionModel Model(int count)
		{
			var model = new SelectionModel();
			model.SetItems(Books(count), keepHidden: false);
			return model;
		}

		private static int[] Selected(SelectionModel m) => m.SelectedIndices().ToArray();

		private static int[] All(int count) => Enumerable.Range(0, count).ToArray();

		// ---- the model --------------------------------------------------------------------------------------------------------------

		[Fact]
		public void The_band_selects_what_it_touches_and_lets_go_of_the_rest()
		{
			SelectionModel m = Model(10);
			m.BeginMarquee();
			m.UpdateMarquee(All(10), new[] { 2, 3, 4 }, 4, keepBase: false, flip: false);
			Assert.Equal(new[] { 2, 3, 4 }, Selected(m));
			m.UpdateMarquee(All(10), new[] { 3 }, 3, keepBase: false, flip: false); // the band shrank
			Assert.Equal(new[] { 3 }, Selected(m));
			m.EndMarquee();
			Assert.False(m.IsMarqueeActive);
		}

		[Fact]
		public void The_book_under_the_pointer_gets_the_focus_and_the_others_lose_it()
		{
			SelectionModel m = Model(10);
			m.Click(1, false, false);
			m.ClickEmpty(false, false);
			m.BeginMarquee();
			m.UpdateMarquee(All(10), new[] { 4, 5 }, 5, false, false);
			Assert.Equal(5, m.FocusedIndex);
			m.UpdateMarquee(All(10), new[] { 4, 5, 6 }, -1, false, false); // the pointer is over a gap
			Assert.Equal(-1, m.FocusedIndex);
			m.UpdateMarquee(All(10), new[] { 4, 5, 6 }, 4, false, false);
			Assert.Equal(4, m.FocusedIndex);
		}

		[Fact]
		public void Shift_keeps_the_selection_the_band_started_with_and_Cmd_flips_the_touched_books()
		{
			SelectionModel m = Model(10);
			m.Click(1, false, false);
			m.Click(7, toggle: true, extend: false);
			m.BeginMarquee();
			m.UpdateMarquee(All(10), new[] { 3, 4 }, 4, keepBase: true, flip: false); // Shift: added to what was selected
			Assert.Equal(new[] { 1, 3, 4, 7 }, Selected(m));
			m.UpdateMarquee(All(10), new[] { 1, 2 }, 2, keepBase: true, flip: true); // Cmd: flipped, 1 was selected, 2 was not
			Assert.Equal(new[] { 2, 7 }, Selected(m));
			m.UpdateMarquee(All(10), new[] { 2 }, 2, keepBase: true, flip: true); // the band shrank: 1 is back as it was, 3 and 4 too
			Assert.Equal(new[] { 1, 2, 7 }, Selected(m));
			m.EndMarquee();
		}

		[Fact]
		public void Books_that_are_not_on_screen_keep_the_state_they_have()
		{
			// ItemView.UpdateSelection walks the visible items only: a book that scrolled out of view stays as the band left it
			SelectionModel m = Model(10);
			m.BeginMarquee();
			m.UpdateMarquee(All(10), new[] { 1, 2, 8, 9 }, 9, false, false);
			Assert.Equal(new[] { 1, 2, 8, 9 }, Selected(m));
			m.UpdateMarquee(new[] { 0, 1, 2, 3 }, new[] { 1 }, 1, false, false); // 8 and 9 are off screen now
			Assert.Equal(new[] { 1, 8, 9 }, Selected(m));
		}

		[Fact]
		public void Hidden_books_stay_selected_and_the_band_without_changes_raises_no_event()
		{
			SelectionModel m = Model(6);
			m.Click(0, false, false);
			m.Click(5, toggle: true, extend: false);
			m.BeginMarquee();
			int events = 0;
			m.Changed += (s, e) => events++;
			m.UpdateMarquee(All(6), new[] { 2 }, 2, keepBase: true, flip: false);
			Assert.Equal(1, events);
			m.UpdateMarquee(All(6), new[] { 2 }, 2, keepBase: true, flip: false); // the same again
			Assert.Equal(1, events);
			m.UpdateMarquee(All(6), new[] { 2, 3 }, 2, keepBase: true, flip: false);
			Assert.Equal(2, events);
			m.EndMarquee();
			m.UpdateMarquee(All(6), new[] { 4 }, 4, false, false); // not active: ignored
			Assert.Equal(new[] { 0, 2, 3, 5 }, Selected(m));
			Assert.Equal(2, events);
		}

		// ---- the grid ---------------------------------------------------------------------------------------------------------------

		private static Point CenterOf(GridHarness h, int index)
		{
			BookCell cell = h.Grid.CellAt(index);
			Assert.NotNull(cell);
			return cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), h.Window).Value;
		}

		// The top left corner of the cell: inside the cell, in the border around the cover that the original does not count as the item.
		private static Point CornerOf(GridHarness h, int index)
		{
			BookCell cell = h.Grid.CellAt(index);
			Assert.NotNull(cell);
			return cell.TranslatePoint(new Point(1, 1), h.Window).Value;
		}

		private static Point BottomRightOf(GridHarness h, int index)
		{
			BookCell cell = h.Grid.CellAt(index);
			Assert.NotNull(cell);
			return cell.TranslatePoint(new Point(cell.Bounds.Width - 1, cell.Bounds.Height - 1), h.Window).Value;
		}

		private static void Down(GridHarness h, Point at, RawInputModifiers modifiers = RawInputModifiers.None, MouseButton button = MouseButton.Left)
		{
			h.Window.MouseDown(at, button, modifiers);
		}

		private static void Move(GridHarness h, Point to, RawInputModifiers modifiers = RawInputModifiers.None)
		{
			h.Window.MouseMove(to, modifiers);
		}

		private static void Up(GridHarness h, Point at, RawInputModifiers modifiers = RawInputModifiers.None, MouseButton button = MouseButton.Left)
		{
			h.Window.MouseUp(at, button, modifiers);
		}

		private static void Click(GridHarness h, int index, RawInputModifiers modifiers = RawInputModifiers.None)
		{
			Point at = CenterOf(h, index);
			Down(h, at, modifiers);
			Up(h, at, modifiers);
		}

		private static int[] Selected(GridHarness h) => Selected(h.Model.Selection);

		[Fact]
		public async Task A_press_in_the_border_of_a_cover_is_a_press_on_the_empty_area_and_starts_a_band()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(30)));
					Click(h, 2);
					Assert.Equal(new[] { 2 }, Selected(h));
					Down(h, CornerOf(h, 3));
					Assert.True(h.Grid.IsMarqueeActive);
					Assert.Empty(Selected(h)); // the press cleared the selection and the focus
					Assert.Equal(-1, h.Model.SelectedIndex);
					Up(h, CornerOf(h, 3));
					Assert.False(h.Grid.IsMarqueeActive);
					Assert.True(h.Grid.Band.Rectangle.Width <= 0);

					// a press on the cover is a press on the book, not a band
					Down(h, CenterOf(h, 4));
					Assert.False(h.Grid.IsMarqueeActive);
					Assert.Equal(new[] { 4 }, Selected(h));
					Up(h, CenterOf(h, 4));
				}
			});
		}

		[Fact]
		public async Task The_hit_region_of_an_item_is_its_cover_and_its_caption_text()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(12)));
					BookCell cell = h.Grid.CellAt(3);
					Rect picture = cell.Geometry.Picture;
					Rect region = cell.HitRegion;
					Assert.True(region.Contains(picture.TopLeft + new Vector(1, 1)));
					Assert.True(region.X >= ThumbnailMetrics.ItemBorder - 0.5 && region.Right <= cell.Bounds.Width - ThumbnailMetrics.ItemBorder + 0.5, region.ToString());
					Assert.False(cell.HitTest(new Point(1, 1)), "the border");
					Assert.False(cell.HitTest(new Point(picture.X - 2, picture.Y + 20)), "left of the cover");
					Assert.False(cell.HitTest(new Point(cell.Bounds.Width - 1, cell.Bounds.Height - 1)), "the corner below the caption");
					Assert.True(cell.HitTest(new Point(picture.Center.X, picture.Center.Y)), "the cover");
					// the caption text extends the region below the cover: a point on the text is on the item, a point below the text is not
					Assert.True(region.Bottom > picture.Bottom, "the region reaches the caption: " + region + " against " + picture);
					Assert.True(cell.HitTest(new Point(cell.Bounds.Width / 2, region.Bottom - 2)), "on the caption");
					Assert.False(cell.HitTest(new Point(cell.Bounds.Width / 2, region.Bottom + 1)), "below the caption");
					// a hit region is the whole thing, left and bottom edges in, right and bottom edges out
					Assert.True(cell.HitTest(region.TopLeft));
					Assert.False(cell.HitTest(region.BottomRight));
				}
			});
		}

		[Fact]
		public async Task Dragging_a_band_selects_the_books_it_touches_and_focuses_the_one_under_the_pointer()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(40)));
					int columns = h.Grid.Columns;
					Assert.True(columns >= 4);
					Point from = CornerOf(h, 0);
					Point to = CenterOf(h, columns + 2);
					Down(h, from);
					Move(h, to);
					var expected = new[] { 0, 1, 2, columns, columns + 1, columns + 2 };
					Assert.Equal(expected, Selected(h));
					Assert.Equal(columns + 2, h.Model.SelectedIndex); // the pointer is over this one
					Assert.True(h.Grid.CellAt(columns + 2).IsFocusedItem);
					Assert.True(h.Grid.CellAt(1).IsSelected && !h.Grid.CellAt(3).IsSelected);
					// the band is drawn from the press to the pointer
					Rect band = h.Grid.Band.Rectangle;
					Assert.InRange(Math.Abs(band.X - from.X), 0, 1);
					Assert.InRange(Math.Abs(band.Y - from.Y), 0, 1);
					Assert.InRange(Math.Abs(band.Right - to.X), 0, 1);
					Assert.InRange(Math.Abs(band.Bottom - to.Y), 0, 1);
					// it shrinks again when the pointer goes back
					Move(h, CenterOf(h, 1));
					Assert.Equal(new[] { 0, 1 }, Selected(h));
					Up(h, CenterOf(h, 1));
					Assert.False(h.Grid.IsMarqueeActive);
					Assert.Equal(new[] { 0, 1 }, Selected(h)); // the selection stays
					Assert.True(h.Grid.Band.Rectangle.Width <= 0 && h.Grid.Band.Rectangle.Height <= 0);
				}
			});
		}

		[Fact]
		public async Task A_band_dragged_up_and_left_works_the_same()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(40)));
					int columns = h.Grid.Columns;
					Down(h, BottomRightOf(h, columns + 2)); // the corner of a cell is outside the item
					Move(h, CenterOf(h, 1));
					Assert.Equal(new[] { 1, 2, columns + 1, columns + 2 }, Selected(h));
					Assert.Equal(1, h.Model.SelectedIndex); // the pointer is over book 1
					Up(h, CenterOf(h, 1));
				}
			});
		}

		[Fact]
		public async Task Shift_adds_to_the_selection_Cmd_flips_and_no_modifier_replaces_it()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(40)));
					Click(h, 1);
					// no modifier: the press clears the selection, the band selects 0..2
					Down(h, CornerOf(h, 0));
					Move(h, CenterOf(h, 2));
					Assert.Equal(new[] { 0, 1, 2 }, Selected(h));
					Up(h, CenterOf(h, 2));

					Click(h, 1);
					// Shift: the band adds to the selection that was there
					Down(h, CornerOf(h, 4), RawInputModifiers.Shift);
					Move(h, CenterOf(h, 5), RawInputModifiers.Shift);
					Assert.Equal(new[] { 1, 4, 5 }, Selected(h));
					Up(h, CenterOf(h, 5), RawInputModifiers.Shift);

					// Cmd: the books the band touches are flipped (1 was selected, 0 and 2 were not)
					Down(h, CornerOf(h, 0), RawInputModifiers.Meta);
					Move(h, CenterOf(h, 2), RawInputModifiers.Meta);
					Assert.Equal(new[] { 0, 2, 4, 5 }, Selected(h));
					Move(h, CenterOf(h, 0), RawInputModifiers.Meta); // the band shrinks: 1 and 2 are as they were
					Assert.Equal(new[] { 0, 1, 4, 5 }, Selected(h));
					Up(h, CenterOf(h, 0), RawInputModifiers.Meta);
				}
			});
		}

		[Fact]
		public async Task A_context_click_on_the_empty_area_clears_the_selection_without_a_band()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(12)));
					Click(h, 1);
					Down(h, CornerOf(h, 5), button: MouseButton.Right);
					Assert.Empty(Selected(h)); // UpdateSelectionFromMouse(null) for any button
					Assert.False(h.Grid.IsMarqueeActive);
					Up(h, CornerOf(h, 5), button: MouseButton.Right);
				}
			});
		}

		[Fact]
		public async Task The_band_does_not_scroll_the_view_to_the_focused_book_but_the_release_does()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(60)));
					int columns = h.Grid.Columns;
					double viewport = h.Grid.Scroller.Viewport.Height;
					BookCell third = h.Grid.CellAt(2 * columns);
					Assert.True(third.Bounds.Bottom > viewport, "the third row is cut by the bottom of the view");
					Point onThird = third.TranslatePoint(new Point(third.Bounds.Width / 2, 20), h.Window).Value;
					Assert.True(onThird.Y < viewport);
					Down(h, CornerOf(h, 0));
					Move(h, onThird);
					Assert.Equal(2 * columns, h.Model.SelectedIndex); // the book under the pointer has the focus
					Assert.Equal(0, h.Grid.Scroller.Offset.Y); // and the view stayed where it was
					Up(h, onThird);
					Assert.True(h.Grid.Scroller.Offset.Y > 0, "the release shows the focused book completely");
					Assert.True(third.Bounds.Bottom - h.Grid.Scroller.Offset.Y <= viewport + 1);
				}
			});
		}

		[Fact]
		public async Task A_pointer_below_the_view_scrolls_it_and_the_band_grows_with_the_content()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(120)));
					int columns = h.Grid.Columns;
					Down(h, CornerOf(h, 0));
					double height = h.Grid.Scroller.Viewport.Height;
					Move(h, new Point(CenterOf(h, columns - 1).X, height + 100)); // 100 units below the view
					int firstScreen = Selected(h).Length;
					Assert.True(firstScreen >= columns, "the band touches the books on screen");

					// beyond the edge by 100: 10 + (100 - 10) / 10 = 19 units per tick
					Assert.True(h.Grid.AdvanceAutoScroll());
					Assert.Equal(19, h.Grid.Scroller.Offset.Y, 3);
					for (int i = 0; i < 40; i++)
					{
						h.Grid.AdvanceAutoScroll();
						h.Layout();
					}
					h.Layout();
					Assert.True(h.Grid.Scroller.Offset.Y > 700, "the view scrolled: " + h.Grid.Scroller.Offset.Y);
					int selected = Selected(h).Length;
					Assert.True(selected > firstScreen + 2 * columns, $"more books are selected after scrolling ({selected} against {firstScreen})");
					// the band is drawn from the start (scrolled out of view) to the pointer
					Assert.True(h.Grid.Band.Rectangle.Top < 0, "the start of the band is above the view");
					Assert.InRange(h.Grid.Band.Rectangle.Bottom, height + 99, height + 101);
					Up(h, new Point(CenterOf(h, columns - 1).X, height + 100));
					Assert.False(h.Grid.AdvanceAutoScroll(), "no band, no scrolling");
				}
			});
		}

		[Fact]
		public async Task A_pointer_above_the_view_scrolls_it_back_up_and_the_scroll_stops_at_the_ends()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(120)));
					int columns = h.Grid.Columns;
					h.ScrollToRow(6);
					double start = h.Grid.Scroller.Offset.Y;
					Down(h, CornerOf(h, 6 * columns + 1));
					Move(h, new Point(300, -30)); // 30 above the view: 10 + 20 / 10 = 12 per tick
					Assert.True(h.Grid.AdvanceAutoScroll());
					Assert.Equal(start - 12, h.Grid.Scroller.Offset.Y, 3);
					for (int i = 0; i < 400; i++)
					{
						h.Grid.AdvanceAutoScroll();
					}
					Assert.Equal(0, h.Grid.Scroller.Offset.Y);
					Assert.False(h.Grid.AdvanceAutoScroll());
					Up(h, new Point(300, -30));
				}
			});
		}

		[Fact]
		public async Task Scrolling_while_the_band_is_down_moves_the_content_under_the_pointer_and_books_out_of_view_keep_their_state()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(120)));
					int columns = h.Grid.Columns;
					Down(h, CornerOf(h, 0));
					Move(h, CenterOf(h, 1));
					Assert.Equal(new[] { 0, 1 }, Selected(h));
					h.Grid.Scroller.Offset = new Vector(0, h.Grid.OffsetOfRow(3));
					h.Layout(); // the pointer stayed where it was in the view, the content moved under it
					// the band now reaches from the start (row 0, out of view) to the pointer (row 3): only the books on screen are looked at, so
					// rows 1 and 2 were never visible with the band over them and stay unselected, and 0 and 1 keep what they had
					Assert.Equal(new[] { 0, 1, 3 * columns, 3 * columns + 1 }, Selected(h));
					Assert.Equal(3 * columns + 1, h.Model.SelectedIndex);
					Up(h, CenterOf(h, 3 * columns + 1));
				}
			});
		}

		[Fact]
		public async Task Moves_outside_the_window_still_drive_the_band_and_the_release_ends_it()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness(900, 600))
				{
					await h.Show(new BookCatalog(Books(30)));
					Down(h, CornerOf(h, 0));
					var outside = new Point(2000, CenterOf(h, 0).Y); // far right of the window: the grid has the pointer captured
					Move(h, outside);
					Assert.True(h.Grid.IsMarqueeActive);
					Assert.Equal(All(h.Grid.Columns), Selected(h)); // the whole first row
					Up(h, outside);
					Assert.False(h.Grid.IsMarqueeActive);
				}
			});
		}
	}
}
