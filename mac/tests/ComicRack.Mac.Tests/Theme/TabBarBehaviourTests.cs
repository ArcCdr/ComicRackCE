using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Themes;
using SkiaSharp;
using Xunit;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// P5.32a: what the tab bar does for the book tabs of the main window (<c>fileTabs</c>, <c>TabBar</c>): tabs with their own minimum width and tool tip, a New Tab
	/// button that is not a tab, the scroll arrows and the drop-down list when the tabs do not fit, and the right click menu.
	/// </summary>
	public class TabBarBehaviourTests
	{
		private static CrTabBar Books(int count, double width)
		{
			var bar = new CrTabBar { Width = width };
			for (int i = 0; i < count; i++)
			{
				bar.Tabs.Add(new CrTabItem { Text = "Book number " + i, MinimumWidth = 100, ToolTipText = "Book number " + i + "\n(shortcut)" });
			}
			return bar;
		}

		private static CrTabItem NewTabButton() => new CrTabItem { Text = "New Tab", ShowText = false, IconName = "AddTab", CanClose = false, MinimumWidth = 32, AdjustWidth = false, Selectable = false, ToolTipText = "New Tab" };

		[Fact]
		public async Task A_book_tab_is_at_least_100_wide_and_the_new_tab_button_stays_32_wide_and_is_never_selected()
		{
			await InAppearance("Light", () =>
			{
				var bar = new CrTabBar { Width = 640 };
				bar.Tabs.Add(new CrTabItem { Text = "A", MinimumWidth = 100 });
				CrTabItem add = NewTabButton();
				var clicks = new List<string>();
				add.Clicked += (s, e) => clicks.Add("add");
				bar.Tabs.Add(add);
				Window window = ShowIn(bar, 660, 60);
				Assert.Equal(100, bar.Geometry[0].Bounds.Width);
				Assert.Equal(32, bar.Geometry[1].Bounds.Width);
				Assert.False(bar.ArrowsVisible);
				Assert.Equal(0, bar.SelectedIndex);
				// clicking the button raises its event and changes nothing else
				var selected = new List<int>();
				bar.SelectedTabChanged += (s, e) => selected.Add(e.Index);
				Rect b = bar.Geometry[1].Bounds;
				bar.Click(new Point(b.X + 10, b.Y + 10));
				Assert.Equal(new[] { "add" }, clicks);
				Assert.Empty(selected);
				Assert.Equal(0, bar.SelectedIndex);
				Assert.Equal(-1, new CrTabBar().SelectedIndex);
				// a bar that holds only the button has no selected tab
				var only = new CrTabBar { Width = 200 };
				only.Tabs.Add(NewTabButton());
				Assert.Equal(-1, only.SelectedIndex);
				window.Close();
			});
		}

		[Fact]
		public async Task Tabs_that_do_not_fit_make_the_bar_scroll_with_an_arrow_at_each_end_and_a_drop_down_button()
		{
			await InAppearance("Light", () =>
			{
				CrTabBar bar = Books(8, 500);
				bar.Tabs.Add(NewTabButton());
				bar.SelectedIndex = 0;
				Window window = ShowIn(bar, 520, 60);
				Assert.True(bar.ArrowsVisible);
				// the buttons: left arrow at 0, drop-down 16 wide at the right end (the right arrow overlaps it by one unit as in the original), tab area between
				Assert.Equal(new Rect(0, 2, 16, 26), bar.LeftArrowBounds);
				Assert.Equal(new Rect(500 - 16, 2, 16, 26), bar.DropDownBounds);
				Assert.Equal(500 - 16 - 16 + 1, bar.RightArrowBounds.X);
				Assert.Equal(16, bar.TabViewport.X);
				Assert.Equal(500 - 32, bar.TabViewport.Right);
				// at the start the tabs begin after the arrow and the left indent
				Assert.Equal(16 + 8, bar.Geometry[0].Bounds.X);
				Assert.Equal(0, bar.TabsOffset);
				// the right arrow scrolls by 20 units a press, the left one back; the ends are clamped
				bar.ClickButton(left: false, right: true, dropDown: false);
				Assert.Equal(-20, bar.TabsOffset);
				Assert.Equal(16 + 8 - 20, bar.Geometry[0].Bounds.X);
				bar.ClickButton(left: true, right: false, dropDown: false);
				bar.ClickButton(left: true, right: false, dropDown: false);
				Assert.Equal(0, bar.TabsOffset);
				for (int i = 0; i < 100; i++)
				{
					bar.ClickButton(left: false, right: true, dropDown: false);
				}
				double last = bar.Geometry.Last().Bounds.Right;
				Assert.Equal(bar.TabViewport.Right, last);
				Assert.True(bar.TabsOffset < 0);
				// a tab under an arrow is no tab
				Assert.Equal(-1, bar.HitTest(new Point(5, 10)));
				Assert.Equal(-1, bar.HitTest(new Point(500 - 5, 10)));
				window.Close();
			});
		}

		[Fact]
		public async Task Selecting_a_tab_scrolls_it_into_view_and_the_drop_down_lists_the_tabs_without_the_new_tab_button_and_selects_the_one_chosen()
		{
			await InAppearance("Light", () =>
			{
				CrTabBar bar = Books(8, 500);
				bar.Tabs.Add(NewTabButton());
				bar.SelectedIndex = 0;
				Window window = ShowIn(bar, 520, 60);
				bar.SelectedIndex = 7;
				Dpi.Settle(window);
				Rect seventh = bar.Geometry[7].Bounds;
				Assert.True(seventh.Right <= bar.TabViewport.Right + 0.01 && seventh.X >= bar.TabViewport.X, $"the selected tab is in view: {seventh} in {bar.TabViewport}");
				bar.SelectedIndex = 0;
				Dpi.Settle(window);
				Assert.Equal(16 + 8, bar.Geometry[0].Bounds.X);
				// the drop-down: every book tab, the selected one marked
				MenuFlyout list = bar.OpenTabList(show: false);
				string[] headers = list.Items.OfType<MenuItem>().Select(m => (string)m.Header).ToArray();
				Assert.Equal(8, headers.Length);
				Assert.Equal("Book number 3", headers[3]);
				Assert.True(list.Items.OfType<MenuItem>().First().IsChecked);
				var changed = new List<int>();
				bar.SelectedTabChanged += (s, e) => changed.Add(e.Index);
				list.Items.OfType<MenuItem>().ElementAt(5).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
				Dpi.Settle(window);
				Assert.Equal(new[] { 5 }, changed);
				Assert.Equal(5, bar.SelectedIndex);
				Rect fifth = bar.Geometry[5].Bounds;
				Assert.True(fifth.X >= bar.TabViewport.X && fifth.Right <= bar.TabViewport.Right + 0.01);
				window.Close();
			});
		}

		[Fact]
		public async Task The_arrows_go_away_when_the_tabs_fit_again_and_the_tool_bar_slot_takes_room_from_the_tab_area()
		{
			await InAppearance("Light", () =>
			{
				CrTabBar bar = Books(8, 500);
				var slot = new CrToolBar { Transparent = true };
				slot.Children.Add(new CrToolButton { IconName = "GoNext" });
				bar.ToolbarSlot = slot;
				Window window = ShowIn(bar, 520, 60);
				Assert.True(bar.ArrowsVisible);
				double slotWidth = slot.Bounds.Width;
				Assert.Equal(500 - slotWidth, bar.DropDownBounds.Right, 3);
				while (bar.Tabs.Count > 3)
				{
					bar.Tabs.RemoveAt(bar.Tabs.Count - 1);
				}
				Dpi.Settle(window);
				Assert.False(bar.ArrowsVisible);
				Assert.Equal(0, bar.TabsOffset);
				Assert.Equal(8, bar.Geometry[0].Bounds.X);
				Assert.Equal(default, bar.LeftArrowBounds);
				window.Close();
			});
		}

		[Fact]
		public async Task The_pointer_over_a_tab_gives_the_tool_tip_of_that_tab()
		{
			await InAppearance("Light", () =>
			{
				CrTabBar bar = Books(3, 640);
				bar.Tabs.Add(NewTabButton());
				Window window = ShowIn(bar, 660, 60);
				Rect second = bar.Geometry[1].Bounds;
				bar.UpdateHot(new Point(second.X + 20, second.Y + 8));
				Assert.Equal("Book number 1\n(shortcut)", ToolTip.GetTip(bar));
				Rect add = bar.Geometry[3].Bounds;
				bar.UpdateHot(new Point(add.X + 8, add.Y + 8));
				Assert.Equal("New Tab", ToolTip.GetTip(bar));
				bar.UpdateHot(new Point(600, 8));
				Assert.Null(ToolTip.GetTip(bar));
				window.Close();
			});
		}

		[Fact]
		public async Task A_right_click_selects_the_tab_raises_the_opening_event_and_leaves_the_new_tab_button_alone()
		{
			await InAppearance("Light", () =>
			{
				CrTabBar bar = Books(3, 640);
				bar.Tabs.Add(NewTabButton());
				bar.TabMenu = new MenuFlyout();
				bar.SelectedIndex = 0;
				Window window = ShowIn(bar, 660, 60);
				var opened = new List<int>();
				var selected = new List<int>();
				bar.TabMenuOpening += (s, e) => opened.Add(e.Index);
				bar.SelectedTabChanged += (s, e) => selected.Add(e.Index);
				Rect third = bar.Geometry[2].Bounds;
				Assert.True(bar.OpenTabMenu(new Point(third.X + 20, third.Y + 8)));
				Assert.Equal(new[] { 2 }, opened);
				Assert.Equal(new[] { 2 }, selected);
				Assert.Equal(2, bar.SelectedIndex);
				// the same tab again opens the menu without a selection change
				Assert.True(bar.OpenTabMenu(new Point(third.X + 20, third.Y + 8)));
				Assert.Equal(new[] { 2, 2 }, opened);
				Assert.Equal(new[] { 2 }, selected);
				Rect add = bar.Geometry[3].Bounds;
				Assert.False(bar.OpenTabMenu(new Point(add.X + 8, add.Y + 8)));
				Assert.False(bar.OpenTabMenu(new Point(630, 8)));
				window.Close();
			});
		}

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task The_scroll_buttons_are_drawn_as_tabs_in_the_token_colours_with_the_image_in_the_middle(string variant)
		{
			await InAppearance(variant, () =>
			{
				CrTabBar bar = Books(8, 500);
				bar.SelectedIndex = 0;
				Window window = ShowIn(bar, 520, 60);
				Raster frame = Dpi.Capture(window, "tabbar-overflow-" + variant);
				SKColor fill = Token(ThemeTokens.Names.TabPageOther, variant);
				SKColor border = Token(ThemeTokens.Names.TabPageBorder, variant);
				Rect left = bar.LeftArrowBounds;
				Assert.True(Raster.Near(frame[(int)left.X + 2, (int)left.Y + 3], fill, 2), "left arrow fill " + Describe(frame[(int)left.X + 2, (int)left.Y + 3]) + " against " + Describe(fill));
				Assert.Equal(Describe(border), Describe(frame[(int)left.X + 8, (int)left.Y]));
				Rect drop = bar.DropDownBounds;
				Assert.True(Raster.Near(frame[(int)drop.X + 2, (int)drop.Y + 3], fill, 2));
				// a tab that reaches under an arrow is clipped to the tab area: the pixels right of the viewport belong to the buttons, not to a tab
				Rect view = bar.TabViewport;
				Assert.True(Raster.Near(frame[(int)view.Right + 2, (int)left.Y + 3], fill, 2), "the right arrow area shows the button fill");
				window.Close();
			});
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_scroll_buttons_and_the_new_tab_button_keep_their_logical_size_and_have_one_unit_frames_at_each_scale(double scale)
		{
			await InAppearance("Light", () =>
			{
				CrTabBar bar = Books(8, 500);
				bar.Tabs.Add(NewTabButton());
				bar.SelectedIndex = 0;
				Window window = ShowIn(bar, 520, 60, scale);
				Assert.Equal(new Rect(0, 2, 16, 26), bar.LeftArrowBounds);
				Assert.Equal(16, bar.DropDownBounds.Width);
				Raster frame = Dpi.Capture(window, "tabbar-overflow-hidpi@" + Scales.Tag(scale));
				SKColor border = Token(ThemeTokens.Names.TabPageBorder, "Light");
				int w = Scales.Device(1, scale);
				// the top edge of the left arrow: `w` rows of the page border colour in a column without the arrow image
				Rect left = bar.LeftArrowBounds;
				int x = Scales.Device(left.X + 2, scale);
				int top = Scales.Device(left.Y, scale);
				Assert.Equal(w, frame.CountInColumn(x, top, top + w, c => Raster.Near(c, border, 0)));
				Assert.False(Raster.Near(frame[x, top + w], border, 0));
				// the left edge of the drop-down button
				Rect drop = bar.DropDownBounds;
				int y = Scales.Device(drop.Y + 4, scale);
				int dx = Scales.Device(drop.X, scale);
				Assert.Equal(w, frame.CountInRow(y, dx, dx + w, c => Raster.Near(c, border, 0)));
				window.Close();
			});
		}
	}
}
