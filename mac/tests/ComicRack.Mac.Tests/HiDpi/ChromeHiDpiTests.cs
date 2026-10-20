using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Themes;
using SkiaSharp;
using Xunit;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.HiDpi
{
	/// <summary>
	/// P5.T4: the custom-drawn chrome at 1x and 2x. Constant in logical units: every rectangle (toolbar 25, button 23 x 22, tab 24 high with the bar 31,
	/// status slot 20 x 19, group header 24, grip 6, column header 20, selection row). What scales: the frame, and the one unit frames and lines, which are
	/// exactly <c>scale</c> device pixels wide and of the pure token colour (the controls draw lines as filled rectangles, so nothing blends).
	/// </summary>
	[Trait("Category", "HiDpi")]
	public class ChromeHiDpiTests
	{
		private static bool Is(SKColor actual, SKColor expected) => Raster.Near(actual, expected, 1);

		/// <summary>Counts the device pixels of a colour along a row starting at a logical x.</summary>
		private static int Run(Raster frame, double scale, double x, double y, int length, SKColor colour)
		{
			int x0 = Scales.Device(x, scale), yy = Scales.Device(y, scale);
			return frame.CountInRow(yy, x0, x0 + length, c => Is(c, colour));
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task A_hot_toolbar_button_has_a_frame_of_one_unit_in_the_pure_token_colour_and_the_same_logical_size(double scale)
		{
			await InAppearance("Light", () =>
			{
				var bar = new CrToolBar { Width = 100 };
				var hot = new CrToolButton { IconName = "FitHeight", DisplayState = ToolButtonState.Hot };
				bar.Children.Add(hot);
				Window window = ShowIn(bar, 120, 40, scale);
				Assert.Equal(new Size(23, 22), hot.Bounds.Size);
				Assert.Equal(25, bar.Bounds.Height);
				Raster frame = Dpi.Capture(window, "chrome-hidpi-toolbar@" + Scales.Tag(scale));
				SKColor border = Token(ThemeTokens.Names.ToolButtonSelectedBorder, "Light");
				Point o = Dpi.Origin(hot, window);
				int w = Scales.Device(1, scale);
				// the left frame is `w` device pixels wide at the button's device x, the pixel before it is not the frame colour, nor the one after it
				Assert.Equal(w, Run(frame, scale, o.X, o.Y + 11, w, border));
				Assert.False(Is(frame[Scales.Device(o.X, scale) + w, Scales.Device(o.Y + 11, scale)], border));
				Assert.False(Is(frame[Scales.Device(o.X, scale) + w + 1, Scales.Device(o.Y + 11, scale)], border));
				// the top frame: a full row of the border colour across the button
				Assert.Equal(Scales.Device(23, scale), Run(frame, scale, o.X, o.Y, Scales.Device(23, scale), border));
				window.Close();
			});
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_selected_tab_has_a_page_border_frame_of_one_unit_and_the_bar_keeps_its_logical_size(double scale)
		{
			await InAppearance("Light", () =>
			{
				var bar = new CrTabBar { Width = 300 };
				bar.Tabs.Add(new CrTabItem { Text = "One" });
				bar.Tabs.Add(new CrTabItem { Text = "Two" });
				bar.SelectedIndex = 1;
				Window window = ShowIn(bar, 320, 60, scale);
				Assert.Equal(31, bar.Bounds.Height);
				Rect tab = bar.Geometry[1].Bounds;
				Assert.Equal(new Rect(bar.Geometry[0].Bounds.Right - 1, 2, tab.Width, 26), tab);
				Raster frame = Dpi.Capture(window, "chrome-hidpi-tabbar@" + Scales.Tag(scale));
				SKColor border = Token(ThemeTokens.Names.TabPageBorder, "Light");
				int w = Scales.Device(1, scale);
				// top edge of the selected tab: `w` rows of the border colour at a column without text
				int x = Scales.Device(tab.X + 40, scale);
				int top = Scales.Device(tab.Y, scale);
				Assert.Equal(w, frame.CountInColumn(x, top, top + w, c => Is(c, border)));
				Assert.False(Is(frame[x, top + w], border));
				window.Close();
			});
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task A_pressed_column_header_has_a_one_unit_frame_in_the_pure_separator_colour(double scale)
		{
			await InAppearance("Light", () =>
			{
				var header = new CrHeader { Text = "Title", Width = 100, DisplayState = HeaderState.Pressed };
				Window window = ShowIn(header, 120, 40, scale);
				Assert.Equal(20, header.Bounds.Height);
				Raster frame = Dpi.Capture(window, "chrome-hidpi-header@" + Scales.Tag(scale));
				SKColor frameColour = Over(Token(ThemeTokens.Names.HeaderSeparator, "Light"), 255, Token(ThemeTokens.Names.HeaderBack, "Light"));
				int w = Scales.Device(1, scale);
				Assert.Equal(w, Run(frame, scale, 0, 10, w, frameColour));
				Assert.False(Is(frame[w, Scales.Device(10, scale)], frameColour));
				int bottom = Scales.Device(19, scale);
				Assert.Equal(w, frame.CountInColumn(Scales.Device(50, scale), bottom, bottom + w, c => Is(c, frameColour)));
				window.Close();
			});
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_pressed_grip_fills_exactly_its_6_unit_strip_and_the_sunken_slot_has_pure_one_unit_edges(double scale)
		{
			await InAppearance("Light", () =>
			{
				var grip = new CrSizableContainer { Grip = GripPosition.Top, ExpandedExtent = 40, Width = 100, Child = new Border() };
				grip.PreviewGrip(false, true);
				var slot = new CrStatusSlot { IconName = "Refresh", Margin = new Thickness(0, 60, 0, 0) };
				var panel = new Panel { Width = 120, Height = 100 };
				panel.Children.Add(grip);
				panel.Children.Add(slot);
				Window window = ShowIn(panel, 140, 120, scale);
				Assert.Equal(new Rect(0, 0, 100, 6), grip.GripRectangle);
				Assert.Equal(new Size(20, 19), slot.Bounds.Size);
				Raster frame = Dpi.Capture(window, "chrome-hidpi-grip-slot@" + Scales.Tag(scale));
				SKColor highlight = Token(ThemeTokens.Names.Highlight, "Light");
				int top = 0, bottom = Scales.Device(6, scale);
				// the strip is the Highlight colour over most of its 100 x 6 units (the grip image is drawn over it) and ends at its last device row
				int hits = 0;
				for (int y = top; y < bottom; y++)
				{
					hits += frame.CountInRow(y, 0, Scales.Device(100, scale), c => Is(c, highlight));
				}
				Assert.True(hits > 0.4 * bottom * Scales.Device(100, scale), "highlight pixels in the strip: " + hits);
				Assert.Equal(0, frame.CountInRow(bottom, 0, Scales.Device(100, scale), c => Is(c, highlight)));
				SKColor shadow = Token(ThemeTokens.Names.ButtonShadow, "Light");
				SKColor lightEdge = Token(ThemeTokens.Names.ButtonHighlight, "Light");
				Rect s = Dpi.Area(slot, window);
				int w = Scales.Device(1, scale);
				Assert.Equal(w, Run(frame, scale, s.X, s.Y + 9, w, shadow));
				Assert.Equal(w, Run(frame, scale, s.Right - 1, s.Y + 9, w, lightEdge));
				window.Close();
			});
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_group_header_gradient_spans_the_header_and_the_selection_frame_is_one_unit_wide(double scale)
		{
			await InAppearance("Light", () =>
			{
				var group = new CrCollapsibleGroup { Width = 200, Header = "Group", Content = new Border { Height = 20 } };
				var selection = new StyledSelectionVisual { Follow = false, IsSelected = true, ViewFocused = true, Width = 80, Height = 20, Margin = new Thickness(0, 60, 0, 0) };
				var panel = new Panel { Width = 220, Height = 100 };
				panel.Children.Add(group);
				panel.Children.Add(selection);
				Window window = ShowIn(panel, 240, 120, scale);
				Assert.Equal(24, group.HeaderBounds.Height);
				Raster frame = Dpi.Capture(window, "chrome-hidpi-group@" + Scales.Tag(scale));
				SKColor start = Token(ThemeTokens.Names.CollapsibleGroupBoxHeaderStart, "Light");
				SKColor end = Token(ThemeTokens.Names.CollapsibleGroupBoxHeaderEnd, "Light");
				int y = Scales.Device(2, scale);
				Assert.True(Raster.Near(frame[Scales.Device(28, scale), y], Lerp(start, end, 28.5 / 200), 4));
				Assert.True(Raster.Near(frame[Scales.Device(198, scale), y], Lerp(start, end, 198.5 / 200), 4));
				// the header ends at its 24th unit: the next row is the group's own (transparent) back over the dialog colour, not the gradient end
				int headerBottom = Scales.Device(24, scale);
				Assert.False(Raster.Near(frame[Scales.Device(198, scale), headerBottom], end, 0));
				SKColor surface = Token(ThemeTokens.Names.Control, "Light");
				SKColor fill = Over(Token(ThemeTokens.Names.SelectionFocused, "Light"), 41, surface);
				SKColor frameColour = Over(Token(ThemeTokens.Names.SelectionFocused, "Light"), 164, fill);
				int w = Scales.Device(1, scale);
				Point o = Dpi.Origin(selection, window);
				Assert.Equal(w, Run(frame, scale, o.X, o.Y + 10, w, frameColour));
				Assert.True(Raster.Near(frame[Scales.Device(o.X + 40, scale), Scales.Device(o.Y + 10, scale)], fill, 2));
				window.Close();
			});
		}
	}
}
