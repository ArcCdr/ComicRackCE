using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Themes;
using SkiaSharp;
using Xunit;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// P5.T4: the metrics of the chrome foundation in device independent units (toolbar 25, tab bar 31 with tabs 24, status bar 24 with 20 x 19 slots,
	/// group header 24, grip 6, column header 20, tree rows font + 8, dialog buttons 80 x 24, the control sizes of the standard controls) and the token
	/// colours of the pixels the controls draw, in the light and in the dark appearance. Headless frames; set CRMAC_FRAMES_DIR to keep them.
	/// </summary>
	public class ChromeMetricsTests
	{
		// ---- toolbar (C-TB-1) ----

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task The_toolbar_is_25_high_with_23_by_22_buttons_a_14_wide_arrow_part_and_6_wide_separators(string variant)
		{
			await InAppearance(variant, () =>
			{
				var bar = new CrToolBar { Width = 300 };
				var button = new CrToolButton { IconName = "FitWidth" };
				var split = new CrSplitButton { IconName = "Zoom", Text = "100 %" };
				var separator = new CrToolSeparator();
				bar.Children.Add(button);
				bar.Children.Add(separator);
				bar.Children.Add(split);
				Window window = ShowIn(bar, 320, 60);
				Assert.Equal(25, bar.Bounds.Height);
				Assert.Equal(new Size(23, 22), button.Bounds.Size);
				Assert.Equal(1, Dpi.Origin(button, bar).Y);
				Assert.Equal(6, separator.Bounds.Width);
				Assert.Equal(14, split.ArrowBounds.Width);
				Assert.Equal(22, split.Bounds.Height);
				Assert.True(split.Bounds.Width > 23 + 14, "the split button holds the image, the text and the arrow part: " + split.Bounds.Width);
				// the original's 16 pixel image keeps its size in the button
				Assert.Equal(16, Icons.Get("FitWidth", Variant(variant)).Size.Width);
				// gradient and border of the strip: top row is the gradient begin, last row the border
				Raster frame = Dpi.Capture(window, "chrome-toolbar-" + variant);
				SKColor begin = Token(ThemeTokens.Names.ToolToolStripGradientBegin, variant);
				Assert.True(Raster.Near(frame[200, 0], begin, 3), "top row " + Describe(frame[200, 0]) + " against " + Describe(begin));
				SKColor border = Token(ThemeTokens.Names.ToolToolStripBorder, variant);
				Assert.True(Raster.Near(frame[200, 24], border, 1), "bottom row " + Describe(frame[200, 24]) + " against " + Describe(border));
				window.Close();
			});
		}

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task Toolbar_buttons_draw_the_hover_pressed_and_checked_fills_and_frames_of_the_toolstrip_tokens(string variant)
		{
			await InAppearance(variant, () =>
			{
				var bar = new CrToolBar { Width = 200 };
				var normal = new CrToolButton { IconName = "FitWidth" };
				var hot = new CrToolButton { IconName = "FitHeight", DisplayState = ToolButtonState.Hot };
				var pressed = new CrToolButton { IconName = "FitAll", DisplayState = ToolButtonState.Pressed };
				var check = new CrToolButton { IconName = "SinglePage", IsChecked = true };
				foreach (CrToolButton b in new[] { normal, hot, pressed, check })
				{
					bar.Children.Add(b);
				}
				Window window = ShowIn(bar, 220, 60);
				Raster frame = Dpi.Capture(window, "chrome-toolbar-states-" + variant);
				Point Origin(Control c) => Dpi.Origin(c, window);

				// normal: nothing is drawn, the strip shows through
				Point n = Origin(normal);
				SKColor stripAtTop = frame[(int)n.X + 1, (int)n.Y + 1];
				Assert.True(Raster.Near(stripAtTop, frame[(int)n.X + 1, 0], 14), "normal button shows the strip: " + Describe(stripAtTop));

				Point h = Origin(hot);
				SKColor hotBorder = Token(ThemeTokens.Names.ToolButtonSelectedBorder, variant);
				Assert.Equal(Describe(hotBorder), Describe(frame[(int)h.X, (int)h.Y + 11]));
				Assert.Equal(Describe(hotBorder), Describe(frame[(int)h.X + 22, (int)h.Y + 11]));
				Assert.Equal(Describe(hotBorder), Describe(frame[(int)h.X + 11, (int)h.Y]));
				SKColor hotBegin = Token(ThemeTokens.Names.ToolButtonSelectedGradientBegin, variant);
				SKColor under = frame[(int)h.X + 1, 0];
				SKColor expectedTop = Over(hotBegin, ThemeTokens.Get(ThemeTokens.Names.ToolButtonSelectedGradientBegin, Variant(variant)).A, under);
				Assert.True(Raster.Near(frame[(int)h.X + 3, (int)h.Y + 1], expectedTop, 12), "hot top " + Describe(frame[(int)h.X + 3, (int)h.Y + 1]) + " against " + Describe(expectedTop));

				Point p = Origin(pressed);
				SKColor pressedBorder = Token(ThemeTokens.Names.ToolButtonPressedBorder, variant);
				Assert.Equal(Describe(pressedBorder), Describe(frame[(int)p.X, (int)p.Y + 11]));
				SKColor pressedBegin = Token(ThemeTokens.Names.ToolButtonPressedGradientBegin, variant);
				Assert.True(Raster.Near(frame[(int)p.X + 3, (int)p.Y + 1], Over(pressedBegin, ThemeTokens.Get(ThemeTokens.Names.ToolButtonPressedGradientBegin, Variant(variant)).A, frame[(int)p.X + 1, 0]), 12));

				Point c = Origin(check);
				Assert.Equal(Describe(Token(ThemeTokens.Names.ToolButtonCheckedHighlightBorder, variant)), Describe(frame[(int)c.X, (int)c.Y + 11]));
				SKColor fill = Token(ThemeTokens.Names.ToolButtonCheckedHighlight, variant);
				Assert.True(Raster.Near(frame[(int)c.X + 2, (int)c.Y + 2], fill, 2), "checked fill " + Describe(frame[(int)c.X + 2, (int)c.Y + 2]) + " against " + Describe(fill));
				window.Close();
			});
		}

		[Fact]
		public async Task A_toolbar_button_checks_on_click_and_the_state_follows_the_pointer_properties()
		{
			await InAppearance("Light", () =>
			{
				var button = new CrToolButton { IconName = "FitWidth", CheckOnClick = true };
				int clicks = 0;
				button.Click += (sender, e) => clicks++;
				var bar = new CrToolBar { Width = 100 };
				bar.Children.Add(button);
				Window window = ShowIn(bar, 120, 40);
				Assert.Equal(ToolButtonState.Normal, button.State);
				window.MouseMove(new Point(12, 12));
				Assert.Equal(ToolButtonState.Hot, button.State);
				window.MouseDown(new Point(12, 12), MouseButton.Left);
				Assert.Equal(ToolButtonState.Pressed, button.State);
				window.MouseUp(new Point(12, 12), MouseButton.Left);
				Assert.Equal(1, clicks);
				Assert.True(button.IsChecked);
				Assert.Equal(ToolButtonState.CheckedHot, button.State);
				button.IsEnabled = false;
				Assert.Equal(ToolButtonState.Disabled, button.State);
				window.Close();
			});
		}

		[Fact]
		public async Task A_press_on_the_arrow_part_opens_the_menu_without_a_click_and_the_button_part_clicks()
		{
			await InAppearance("Light", () =>
			{
				var split = new CrSplitButton { IconName = "Zoom", Text = "100 %", DropDown = new MenuFlyout { Items = { new MenuItem { Header = "Fit" } } } };
				int clicks = 0, openings = 0;
				split.Click += (sender, e) => clicks++;
				split.DropDownOpening += (sender, e) => openings++;
				var bar = new CrToolBar { Width = 200 };
				bar.Children.Add(split);
				Window window = ShowIn(bar, 220, 60);
				Point origin = Dpi.Origin(split, window);
				Point arrow = new Point(origin.X + split.Bounds.Width - 7, origin.Y + 11);
				window.MouseMove(arrow);
				window.MouseDown(arrow, MouseButton.Left);
				window.MouseUp(arrow, MouseButton.Left);
				Assert.Equal(0, clicks);
				Assert.Equal(1, openings);
				Assert.True(split.IsDropDownOpen);
				split.DropDown.Hide();
				Dpi.Settle(window);
				Assert.False(split.IsDropDownOpen);
				Point body = new Point(origin.X + 8, origin.Y + 11);
				window.MouseMove(body);
				window.MouseDown(body, MouseButton.Left);
				window.MouseUp(body, MouseButton.Left);
				Assert.Equal(1, clicks);
				Assert.Equal(1, openings);
				window.Close();
			});
		}

		// ---- tab bar (C-TAB-1) ----

		private static CrTabBar Tabs(params string[] texts)
		{
			var bar = new CrTabBar { Width = 640 };
			foreach (string t in texts)
			{
				bar.Tabs.Add(new CrTabItem { Text = t });
			}
			return bar;
		}

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task The_tab_bar_is_31_high_with_a_left_indent_of_8_and_the_selected_tab_one_unit_taller(string variant)
		{
			await InAppearance(variant, () =>
			{
				CrTabBar bar = Tabs("A", "A longer title for a book", "C");
				bar.SelectedIndex = 1;
				Window window = ShowIn(bar, 660, 60);
				Assert.Equal(31, bar.Bounds.Height);
				var g = bar.Geometry;
				Assert.Equal(3, g.Count);
				Assert.Equal(8, g[0].Bounds.X);
				foreach (TabGeometry tab in g)
				{
					Assert.InRange(tab.Bounds.Width, 60, 250);
				}
				Assert.Equal(60, g[0].Bounds.Width); // a one letter tab is the minimum width
				Assert.Equal(g[0].Bounds.Right - 1, g[1].Bounds.X); // tabs overlap by one unit
				Assert.Equal(26, g[1].Bounds.Height); // selected: content height 25 plus one
				Assert.Equal(2, g[1].Bounds.Y);
				Assert.Equal(4, g[0].Bounds.Y); // others start two lower and are two shorter
				Assert.Equal(22, g[0].Bounds.Height);
				Raster frame = Dpi.Capture(window, "chrome-tabbar-" + variant);
				SKColor selected = Token(ThemeTokens.Names.TabBarSelectedBack, variant);
				Rect s = g[1].Bounds;
				Assert.True(Raster.Near(frame[(int)s.X + 2, (int)s.Y + 3], selected, 2), "selected tab fill " + Describe(frame[(int)s.X + 2, (int)s.Y + 3]) + " against " + Describe(selected));
				// the tab's frame is the page border colour on its top edge
				Assert.Equal(Describe(Token(ThemeTokens.Names.TabPageBorder, variant)), Describe(frame[(int)s.X + 30, (int)s.Y]));
				window.Close();
			});
		}

		[Fact]
		public async Task Tabs_shrink_to_fit_but_never_below_60_and_a_long_title_is_capped_at_250()
		{
			await InAppearance("Light", () =>
			{
				CrTabBar wide = Tabs(new string('W', 200));
				Window w1 = ShowIn(wide, 700, 60);
				Assert.Equal(250, wide.Geometry[0].Bounds.Width);
				w1.Close();

				var bar = new CrTabBar { Width = 400 };
				for (int i = 0; i < 4; i++)
				{
					bar.Tabs.Add(new CrTabItem { Text = "A book with a rather long title number " + i });
				}
				Window w2 = ShowIn(bar, 420, 60);
				var g = bar.Geometry;
				Assert.True(g.Last().Bounds.Right <= 400, "the last tab ends at " + g.Last().Bounds.Right);
				Assert.All(g, t => Assert.True(t.Bounds.Width >= 60));
				Assert.True(g.All(t => t.Bounds.Width < 250), "the tabs were made narrower");
				w2.Close();
			});
		}

		[Fact]
		public async Task A_click_selects_a_tab_and_the_close_box_of_the_hot_tab_asks_to_close_it_without_selecting()
		{
			await InAppearance("Light", () =>
			{
				CrTabBar bar = Tabs("First", "Second", "Third");
				bar.SelectedIndex = 0;
				var selected = new System.Collections.Generic.List<int>();
				var closed = new System.Collections.Generic.List<int>();
				bar.SelectedTabChanged += (sender, e) => selected.Add(e.Index);
				bar.TabCloseRequested += (sender, e) => closed.Add(e.Index);
				Window window = ShowIn(bar, 660, 60);
				Rect second = bar.Geometry[1].Bounds;
				Assert.False(bar.Geometry[1].HasClose, "no close box until the tab is hot or selected");
				Assert.True(bar.Geometry[0].HasClose, "the selected tab shows its close box");
				bar.UpdateHot(new Point(second.X + 20, second.Y + 10));
				Assert.Equal(1, bar.HotIndex);
				Rect close = bar.Geometry[1].CloseBounds;
				Assert.Equal(16, close.Width);
				Assert.Equal(second.Right - 18, close.X);
				bar.Click(new Point(close.X + 4, close.Y + 4));
				Assert.Equal(new[] { 1 }, closed);
				Assert.Empty(selected);
				Assert.Equal(0, bar.SelectedIndex);
				bar.Click(new Point(second.X + 10, second.Y + 10));
				Assert.Equal(new[] { 1 }, selected);
				Assert.Equal(1, bar.SelectedIndex);
				// where tabs overlap by one unit the selected tab wins the hit test
				double edge = bar.Geometry[1].Bounds.X;
				Assert.Equal(1, bar.HitTest(new Point(edge, bar.Geometry[1].Bounds.Y + 10)));
				window.Close();
			});
		}

		[Fact]
		public async Task The_toolbar_slot_sits_at_the_right_end_of_the_bar_and_takes_width_from_the_tabs()
		{
			await InAppearance("Light", () =>
			{
				CrTabBar bar = Tabs("One", "Two");
				var slot = new CrToolBar { Transparent = true };
				slot.Children.Add(new CrToolButton { IconName = "GoNext" });
				slot.Children.Add(new CrToolButton { IconName = "Tools" });
				bar.ToolbarSlot = slot;
				Window window = ShowIn(bar, 660, 60);
				Rect area = Dpi.Area(slot, window);
				Assert.Equal(640, area.Right);
				Assert.Equal(46, area.Width);
				Assert.Equal(25, slot.Height);
				Assert.True(bar.Geometry.Last().Bounds.Right <= 640 - 46);
				window.Close();
			});
		}

		// ---- status bar (C-SB-1) ----

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task The_status_bar_is_24_high_its_text_segment_stretches_and_the_icon_slots_are_20_by_19_with_sunken_borders(string variant)
		{
			await InAppearance(variant, () =>
			{
				var bar = new CrStatusBar { Width = 500 };
				var s1 = new CrStatusSlot { IconName = "Refresh" };
				var s2 = new CrStatusSlot { IconName = "Search" };
				var text = new TextBlock { Text = "14.706 books" };
				bar.AddRight(s1);
				bar.AddRight(s2);
				bar.SetSpring(text);
				Window window = ShowIn(bar, 520, 60);
				Assert.Equal(24, bar.Bounds.Height);
				Assert.Equal(new Size(20, 19), s1.Bounds.Size);
				Rect a1 = Dpi.Area(s1, window), a2 = Dpi.Area(s2, window);
				Assert.Equal(500, a1.Right);
				Assert.Equal(a1.X, a2.Right);
				Assert.Equal(3, a1.Y);
				Rect t = Dpi.Area(text, window);
				// the text takes everything that the slots leave (4 units of margin on each side)
				Assert.Equal(a2.X - 4, t.Right);
				Assert.Equal(4, t.X);
				Raster frame = Dpi.Capture(window, "chrome-statusbar-" + variant);
				Assert.Equal(Describe(Token(ThemeTokens.Names.ButtonShadow, variant)), Describe(frame[(int)a1.X, (int)a1.Y + 9]));
				Assert.Equal(Describe(Token(ThemeTokens.Names.ButtonHighlight, variant)), Describe(frame[(int)a1.Right - 1, (int)a1.Y + 9]));
				Assert.Equal(Describe(Token(ThemeTokens.Names.ToolStatusStripBorder, variant)), Describe(frame[250, 0]));
				window.Close();
			});
		}

		// ---- collapsible group box (C-GRP-1) ----

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task The_group_header_is_24_high_with_a_gradient_from_start_to_end_and_the_toggle_flips_the_state(string variant)
		{
			await InAppearance(variant, () =>
			{
				var group = new CrCollapsibleGroup { Width = 300, Header = "Group", Content = new Border { Height = 40 } };
				Window window = ShowIn(group, 320, 120);
				Assert.Equal(64, group.Bounds.Height);
				Assert.Equal(new Rect(0, 0, 300, 24), group.HeaderBounds);
				Assert.Equal(new Rect(0, 0, 24, 24), group.ToggleBounds);
				Raster frame = Dpi.Capture(window, "chrome-group-" + variant);
				SKColor start = Token(ThemeTokens.Names.CollapsibleGroupBoxHeaderStart, variant);
				SKColor end = Token(ThemeTokens.Names.CollapsibleGroupBoxHeaderEnd, variant);
				Assert.True(Raster.Near(frame[26, 1], Lerp(start, end, 26.5 / 300), 4), "near the start " + Describe(frame[26, 1]));
				Assert.True(Raster.Near(frame[298, 1], Lerp(start, end, 298.5 / 300), 4), "near the end " + Describe(frame[298, 1]));

				int changed = 0, clicked = 0;
				group.CollapsedChanged += (sender, e) => changed++;
				group.CollapseClicked += (sender, e) => clicked++;
				window.MouseDown(new Point(12, 12), MouseButton.Left);
				window.MouseUp(new Point(12, 12), MouseButton.Left);
				Dpi.Settle(window);
				Assert.True(group.Collapsed);
				Assert.Equal(24, group.Bounds.Height); // collapsed: only the header
				Assert.Equal(1, changed);
				Assert.Equal(1, clicked);
				window.MouseDown(new Point(150, 12), MouseButton.Left, RawInputModifiers.None);
				window.MouseUp(new Point(150, 12), MouseButton.Left);
				Assert.True(group.Collapsed); // a single click outside the toggle square does nothing
				window.MouseDown(new Point(12, 12), MouseButton.Left);
				window.MouseUp(new Point(12, 12), MouseButton.Left);
				Dpi.Settle(window);
				Assert.False(group.Collapsed);
				Assert.Equal(64, group.Bounds.Height);
				window.Close();
			});
		}

		// ---- sizable container (C-SPLIT-1) ----

		[Fact]
		public async Task The_grip_is_6_wide_on_the_chosen_side_and_the_child_gets_the_rest()
		{
			await InAppearance("Light", () =>
			{
				foreach ((GripPosition grip, Rect expectedGrip, Rect expectedContent) in new[]
				{
					(GripPosition.Top, new Rect(0, 0, 200, 6), new Rect(0, 6, 200, 94)),
					(GripPosition.Bottom, new Rect(0, 94, 200, 6), new Rect(0, 0, 200, 94)),
				})
				{
					var panel = new CrSizableContainer { Grip = grip, ExpandedExtent = 100, Width = 200, Child = new Border() };
					Window window = ShowIn(panel, 220, 140);
					Assert.Equal(expectedGrip, panel.GripRectangle);
					Assert.Equal(expectedContent, panel.ContentRectangle);
					Assert.Equal(100, panel.Bounds.Height);
					window.Close();
				}
				foreach ((GripPosition grip, Rect expectedGrip, Rect expectedContent) in new[]
				{
					(GripPosition.Left, new Rect(0, 0, 6, 80), new Rect(6, 0, 94, 80)),
					(GripPosition.Right, new Rect(94, 0, 6, 80), new Rect(0, 0, 94, 80)),
				})
				{
					var panel = new CrSizableContainer { Grip = grip, ExpandedExtent = 100, Height = 80, Child = new Border() };
					Window window = ShowIn(panel, 220, 140);
					Assert.Equal(expectedGrip, panel.GripRectangle);
					Assert.Equal(expectedContent, panel.ContentRectangle);
					Assert.Equal(100, panel.Bounds.Width);
					window.Close();
				}
			});
		}

		[Fact]
		public async Task Collapsing_slides_in_100_ms_to_zero_or_the_grip_and_expanding_slides_back()
		{
			await InAppearance("Light", () =>
			{
				var panel = new CrSizableContainer { Grip = GripPosition.Top, ExpandedExtent = 100, Width = 200, Child = new Border() };
				Window window = ShowIn(panel, 220, 140);
				Assert.Equal(100, panel.Bounds.Height);
				panel.IsExpanded = false;
				Assert.True(panel.IsSliding);
				panel.AdvanceAnimation(TimeSpan.FromMilliseconds(50));
				Assert.Equal(50, panel.CurrentExtent, 3);
				Dpi.Settle(window);
				Assert.Equal(50, panel.Bounds.Height);
				panel.AdvanceAnimation(TimeSpan.FromMilliseconds(60));
				Assert.False(panel.IsSliding);
				Assert.Equal(0, panel.CurrentExtent);
				Dpi.Settle(window);
				Assert.Equal(0, panel.Bounds.Height);
				panel.IsExpanded = true;
				panel.AdvanceAnimation(TimeSpan.FromMilliseconds(100));
				Dpi.Settle(window);
				Assert.Equal(100, panel.Bounds.Height);
				// with the grip kept visible a collapsed panel is only the grip
				panel.KeepGripVisible = true;
				panel.IsExpanded = false;
				panel.AdvanceAnimation(TimeSpan.FromMilliseconds(100));
				Dpi.Settle(window);
				Assert.Equal(6, panel.Bounds.Height);
				// animation off: immediate
				bool before = CrSizableContainer.EnableAnimation;
				try
				{
					CrSizableContainer.EnableAnimation = false;
					panel.IsExpanded = true;
					Assert.False(panel.IsSliding);
					Dpi.Settle(window);
					Assert.Equal(100, panel.Bounds.Height);
				}
				finally
				{
					CrSizableContainer.EnableAnimation = before;
				}
				window.Close();
			});
		}

		[Fact]
		public async Task A_container_that_starts_collapsed_has_no_extent_and_a_drag_resizes_it_but_never_below_the_minimum()
		{
			await InAppearance("Light", () =>
			{
				var start = new CrSizableContainer { Grip = GripPosition.Bottom, ExpandedExtent = 120, Width = 150, KeepGripVisible = true, AnimateExpand = false, Child = new Border() };
				start.IsExpanded = false;
				Window w1 = ShowIn(start, 220, 140);
				Assert.Equal(6, start.Bounds.Height);
				w1.Close();

				var panel = new CrSizableContainer { Grip = GripPosition.Top, ExpandedExtent = 100, Width = 200, Child = new Border() };
				Window window = ShowIn(panel, 220, 200);
				var parent = (Border)panel.GetVisualParent();
				// the grip is at the top of a panel docked at the bottom: dragging it up makes the panel taller
				Point grip = new Point(100, 3);
				window.MouseMove(grip);
				window.MouseDown(grip, MouseButton.Left);
				window.MouseMove(new Point(100, -27));
				Dpi.Settle(window);
				Assert.Equal(130, panel.ExpandedExtent, 1);
				window.MouseUp(new Point(100, -27), MouseButton.Left);
				panel.DragBy(-500);
				Assert.Equal(panel.MinimumExtent, panel.ExpandedExtent);
				Assert.Equal(26, panel.MinimumExtent);
				window.Close();
			});
		}

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task The_grip_is_the_control_colour_and_turns_control_light_when_hot_and_highlight_when_pressed(string variant)
		{
			await InAppearance(variant, () =>
			{
				var normal = new CrSizableContainer { Grip = GripPosition.Top, ExpandedExtent = 40, Width = 100, Child = new Border() };
				var hot = new CrSizableContainer { Grip = GripPosition.Top, ExpandedExtent = 40, Width = 100, Child = new Border(), Margin = new Thickness(0, 50, 0, 0) };
				var pressed = new CrSizableContainer { Grip = GripPosition.Top, ExpandedExtent = 40, Width = 100, Child = new Border(), Margin = new Thickness(0, 100, 0, 0) };
				var stack = new Panel { Width = 120, Height = 160 };
				stack.Children.Add(normal);
				stack.Children.Add(hot);
				stack.Children.Add(pressed);
				hot.PreviewGrip(true, false);
				pressed.PreviewGrip(false, true);
				Window window = ShowIn(stack, 140, 180);
				Raster frame = Dpi.Capture(window, "chrome-grip-" + variant);
				// the grip image is wider than the grip and drawn over it, so most of the 100 x 6 area, not all of it, is the fill
				Assert.True(Share(frame, new Rect(0, 0, 100, 6), Token(ThemeTokens.Names.Control, variant)) > 0.7);
				Assert.True(Share(frame, new Rect(0, 50, 100, 6), Token(ThemeTokens.Names.ControlLight, variant)) > 0.7, "hot grip");
				Assert.True(Share(frame, new Rect(0, 100, 100, 6), Token(ThemeTokens.Names.Highlight, variant)) > 0.7, "pressed grip");
				Assert.True(Share(frame, new Rect(0, 56, 100, 40), Token(ThemeTokens.Names.ControlLight, variant)) < 0.01);
				window.Close();
			});
		}

		private static double Share(Raster frame, Rect area, SKColor colour)
		{
			int n = 0, total = 0;
			for (int y = (int)area.Top; y < (int)area.Bottom; y++)
			{
				for (int x = (int)area.Left; x < (int)area.Right; x++)
				{
					total++;
					if (Raster.Near(frame[x, y], colour, 2))
					{
						n++;
					}
				}
			}
			return (double)n / total;
		}

		// ---- column header (C-HDR-1) ----

		[Theory]
		[InlineData("Light", HeaderState.Normal, 92)]
		[InlineData("Light", HeaderState.Hot, 164)]
		[InlineData("Light", HeaderState.Pressed, 255)]
		[InlineData("Dark", HeaderState.Normal, 92)]
		[InlineData("Dark", HeaderState.Hot, 164)]
		[InlineData("Dark", HeaderState.Pressed, 255)]
		public async Task A_column_header_is_20_high_and_its_frame_uses_the_separator_colour_at_the_selection_alpha_of_its_state(string variant, HeaderState state, int frameAlpha)
		{
			await InAppearance(variant, () =>
			{
				var header = new CrHeader { Text = "Title", Width = 100, DisplayState = state };
				Window window = ShowIn(header, 120, 40);
				Assert.Equal(20, header.Bounds.Height);
				Raster frame = Dpi.Capture(window, "chrome-header-" + variant + "-" + state);
				SKColor separator = Token(ThemeTokens.Names.HeaderSeparator, variant);
				SKColor back = Token(ThemeTokens.Names.HeaderBack, variant);
				// the fill is a quarter of the frame alpha (the original truncates), the frame is drawn over it
				SKColor filled = Over(separator, (int)(frameAlpha * 0.25f), back);
				SKColor expectedFrame = Over(separator, frameAlpha, filled);
				Assert.True(Raster.Near(frame[0, 10], expectedFrame, 2), "left edge " + Describe(frame[0, 10]) + " against " + Describe(expectedFrame));
				Assert.True(Raster.Near(frame[99, 10], expectedFrame, 2), "right edge");
				Assert.True(Raster.Near(frame[50, 19], expectedFrame, 2), "bottom edge");
				window.Close();
			});
		}

		[Fact]
		public void The_header_frame_style_of_each_state_is_the_one_of_HeaderControl_Draw()
		{
			Assert.Equal(SelectionState.SelectedHot, CrHeader.FrameStyle(HeaderState.Pressed));
			Assert.Equal(SelectionState.Selected, CrHeader.FrameStyle(HeaderState.Hot));
			Assert.Equal(SelectionState.Hot, CrHeader.FrameStyle(HeaderState.Active));
			Assert.Equal(SelectionState.Hot, CrHeader.FrameStyle(HeaderState.Normal));
		}

		[Fact]
		public async Task A_column_header_raises_Clicked_and_DropDownClicked_by_where_the_pointer_is_released()
		{
			await InAppearance("Light", () =>
			{
				var header = new CrHeader { Text = "Title", Width = 100, Adornments = HeaderAdornments.DropDown };
				int clicks = 0, drops = 0;
				header.Clicked += (sender, e) => clicks++;
				header.DropDownClicked += (sender, e) => drops++;
				Window window = ShowIn(header, 120, 40);
				Assert.True(header.DropDownBounds.Width >= 6);
				window.MouseDown(new Point(30, 10), MouseButton.Left);
				Assert.Equal(HeaderState.Pressed, header.State);
				window.MouseUp(new Point(30, 10), MouseButton.Left);
				window.MouseDown(new Point(96, 10), MouseButton.Left);
				window.MouseUp(new Point(96, 10), MouseButton.Left);
				Assert.Equal(1, clicks);
				Assert.Equal(1, drops);
				window.Close();
			});
		}

		// ---- tree item (C-TREE-1) ----

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task Tree_rows_are_the_text_height_plus_8_with_a_bold_selected_label_and_the_original_arrow_images(string variant)
		{
			await InAppearance(variant, () =>
			{
				var child = new TreeViewItem { Header = "Child" };
				var root = new TreeViewItem { Header = "Root", IsExpanded = true, ItemsSource = new[] { child } };
				var tree = new TreeView { Width = 200, Height = 100, ItemsSource = new[] { root } };
				Window window = ShowIn(tree, 220, 120);
				double row = ChromeMetrics.DefaultTreeRowHeight;
				Assert.Equal(Math.Ceiling(ChromeFonts.LineHeight(12)) + 8, row);
				var layoutRoots = tree.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "PART_LayoutRoot").ToList();
				Assert.Equal(2, layoutRoots.Count);
				Assert.All(layoutRoots, b => Assert.Equal(row, b.Bounds.Height));
				Assert.Equal(row, Dpi.Origin(layoutRoots[1], tree).Y - Dpi.Origin(layoutRoots[0], tree).Y);
				// an item with children shows the Down arrow while expanded; a leaf keeps the space but hides the arrow
				var rootIcons = root.GetVisualDescendants().OfType<CrIcon>().ToList();
				Assert.Contains(rootIcons, i => i.Name == "Down" && i.IsVisible);
				var chevrons = tree.GetVisualDescendants().OfType<ToggleButton>().Where(t => t.Name == "PART_ExpandCollapseChevron").ToList();
				Assert.Equal(1, chevrons[0].Opacity);
				Assert.Equal(0, chevrons[1].Opacity);
				// selecting makes the label bold
				var presenters = tree.GetVisualDescendants().OfType<ContentPresenter>().Where(p => p.Name == "PART_HeaderPresenter").ToList();
				Assert.NotEqual(FontWeight.Bold, presenters[1].FontWeight);
				tree.SelectedItem = child;
				Dpi.Settle(window);
				Assert.Equal(FontWeight.Bold, presenters[1].FontWeight);
				Assert.Equal(12, presenters[1].FontSize);
				window.Close();
			});
		}

		[Theory]
		[InlineData("Light", true, 164)]
		[InlineData("Dark", true, 164)]
		[InlineData("Light", false, 164)]
		[InlineData("Dark", false, 164)]
		public async Task The_selection_style_draws_a_one_unit_frame_at_alpha_164_and_a_fill_of_41_in_the_focused_or_unfocused_colour(string variant, bool focused, int alpha)
		{
			await InAppearance(variant, () =>
			{
				var visual = new StyledSelectionVisual { Follow = false, IsSelected = true, ViewFocused = focused, Width = 80, Height = 20 };
				Window window = ShowIn(visual, 100, 40);
				Raster frame = Dpi.Capture(window, "chrome-selection-" + variant + (focused ? "-focused" : "-unfocused"));
				SKColor colour = Token(focused ? ThemeTokens.Names.SelectionFocused : ThemeTokens.Names.SelectionUnfocused, variant);
				SKColor surface = Token(ThemeTokens.Names.Control, variant);
				SKColor fill = Over(colour, 41, surface);
				SKColor expectedFrame = Over(colour, alpha, fill);
				Assert.True(Raster.Near(frame[0, 10], expectedFrame, 2), "frame " + Describe(frame[0, 10]) + " against " + Describe(expectedFrame));
				Assert.True(Raster.Near(frame[40, 10], fill, 2), "fill " + Describe(frame[40, 10]) + " against " + Describe(fill));
				Assert.Equal(Describe(surface), Describe(frame[90, 10]));
				// hot and selected: alpha 255
				visual.IsHot = true;
				Raster hot = Dpi.Capture(window, "chrome-selection-hot-" + variant);
				SKColor hotFill = Over(colour, 63, surface);
				Assert.True(Raster.Near(hot[0, 10], Over(colour, 255, hotFill), 2), "selected and hot frame");
				window.Close();
			});
		}

		// ---- standard controls (C-CTRL-1) and fonts (C-FONT-1) ----

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task Buttons_text_boxes_check_boxes_radio_buttons_sliders_and_combo_boxes_have_the_original_sizes_and_token_colours(string variant)
		{
			await InAppearance(variant, () =>
			{
				var button = new Button { Content = "Button", Width = 80 };
				var edit = new TextBox { Text = "text", Width = 100 };
				var check = new CheckBox { Content = "Check", IsChecked = true };
				var radio = new RadioButton { Content = "Radio", IsChecked = true };
				var slider = new Slider { Width = 120, Value = 30 };
				var combo = new ComboBox { Width = 120, ItemsSource = new[] { "A" }, SelectedIndex = 0 };
				var stack = new StackPanel { Spacing = 8, Margin = new Thickness(4) };
				foreach (Control ctl in new Control[] { button, edit, check, radio, slider, combo })
				{
					stack.Children.Add(ctl);
				}
				Window window = ShowIn(stack, 220, 260);
				Assert.Equal(23, button.Bounds.Height);
				Assert.Equal(21, edit.Bounds.Height);
				Assert.Equal(21, combo.Bounds.Height);
				Assert.Equal(11, button.FontSize);
				Border box = check.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Box");
				Assert.Equal(new Size(13, 13), box.Bounds.Size);
				Panel dot = radio.GetVisualDescendants().OfType<Panel>().First(p => p.Width == 13);
				Assert.Equal(new Size(13, 13), dot.Bounds.Size);
				Thumb thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
				Assert.Equal(new Size(11, 21), thumb.Bounds.Size);
				Assert.Equal(21, slider.Bounds.Height);
				Raster frame = Dpi.Capture(window, "chrome-controls-" + variant);
				Point b = Dpi.Origin(button, window);
				Assert.Equal(Describe(Token(ThemeTokens.Names.ButtonBorder, variant)), Describe(frame[(int)b.X, (int)b.Y + 11]));
				Assert.True(Raster.Near(frame[(int)b.X + 3, (int)b.Y + 3], Token(ThemeTokens.Names.ButtonBack, variant), 2), "button face " + Describe(frame[(int)b.X + 3, (int)b.Y + 3]));
				Point e = Dpi.Origin(edit, window);
				Assert.Equal(Describe(Token(ThemeTokens.Names.EditFrame, variant)), Describe(frame[(int)e.X, (int)e.Y + 10]));
				string editBack = variant == "Dark" ? ThemeTokens.Names.EditBack : ThemeTokens.Names.Window;
				Assert.True(Raster.Near(frame[(int)e.X + 98, (int)e.Y + 10], Token(editBack, variant), 2), "edit surface " + Describe(frame[(int)e.X + 98, (int)e.Y + 10]));
				Point c = Dpi.Origin(box, window);
				Assert.Equal(Describe(Token(variant == "Dark" ? ThemeTokens.Names.CheckBoxAccent : ThemeTokens.Names.CheckBoxBorder, variant)), Describe(frame[(int)c.X, (int)c.Y + 6]));
				window.Close();
			});
		}

		[Fact]
		public async Task The_font_resources_are_the_original_sizes_in_pixels()
		{
			await InAppearance("Light", () =>
			{
				Application app = Application.Current;
				double Size(string key) => (double)app.FindResource(key);
				Assert.Equal(11.0, Size("Cr.FontSize.Control"));
				Assert.Equal(9.0, Size("Cr.FontSize.Caption"));
				Assert.Equal(12.0, Size("Cr.FontSize.Item"));
				Assert.Equal(13.8, Size("Cr.FontSize.GroupHeader"), 6);
				Assert.Equal(9.0, Size("Cr.FontSize.Counter"));
				Assert.Equal(16.0, Size("Cr.FontSize.Overlay"));
				Assert.Equal(ChromeFonts.ControlSize, Size("ControlContentThemeFontSize"));
				Assert.Equal(ChromeFonts.GroupHeaderSize, Size("Cr.FontSize.GroupHeader"), 6);
				Assert.Equal(ChromeFonts.CounterSize, Size("Cr.FontSize.Counter"), 6);
			});
		}
	}
}
