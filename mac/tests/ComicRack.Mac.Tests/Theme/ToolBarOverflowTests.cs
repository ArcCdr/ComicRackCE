using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Services;
using ComicRack.Mac.Themes;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using SkiaSharp;
using Xunit;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// RV8: the toolbar lays its items out as the original's <c>HorizontalStackWithOverflow</c> (<c>ComicBrowserControl.Designer.cs</c>: the quick search is
	/// <c>Alignment.Right</c> and <c>Overflow.Never</c>, the other items overflow as needed): the plan without any control, the bar with real buttons,
	/// the drop-down that shows the items that did not fit, the browser window, and the overflow button at 1x and 2x.
	/// </summary>
	public class ToolBarOverflowTests
	{
		private static ToolBarLayout.Item Left(double width, ToolBarOverflow overflow = ToolBarOverflow.AsNeeded) => new ToolBarLayout.Item(width, ToolBarAlignment.Left, overflow);

		private static ToolBarLayout.Item Right(double width, ToolBarOverflow overflow = ToolBarOverflow.AsNeeded) => new ToolBarLayout.Item(width, ToolBarAlignment.Right, overflow);

		[Fact]
		public void When_everything_fits_the_items_stack_from_the_left_and_the_right_aligned_ones_from_the_right_and_there_is_no_overflow_button()
		{
			ToolBarLayout.Plan plan = ToolBarLayout.Compute(new[] { Left(23), Left(29), Left(6), Right(130, ToolBarOverflow.Never), Left(23) }, 400);
			Assert.False(plan.HasOverflow);
			Assert.Equal(new[] { 0.0, 23, 52, 270, 58 }, plan.X);
			Assert.DoesNotContain(true, plan.InOverflow);
		}

		[Fact]
		public void When_they_do_not_fit_the_last_items_that_may_go_move_to_the_overflow_button_and_the_ones_marked_Never_stay()
		{
			// 23 x 6 = 138 and the search box 130: in 200 units the button takes 16, the search 130, so 54 are left: two buttons
			var items = new List<ToolBarLayout.Item>();
			for (int i = 0; i < 6; i++)
			{
				items.Add(Left(23));
			}
			items.Add(Right(130, ToolBarOverflow.Never));
			ToolBarLayout.Plan plan = ToolBarLayout.Compute(items, 200);
			Assert.True(plan.HasOverflow);
			Assert.Equal(new[] { false, false, true, true, true, true, false }, plan.InOverflow);
			Assert.Equal(0, plan.X[0]);
			Assert.Equal(23, plan.X[1]);
			Assert.Equal(200 - 16 - 130, plan.X[6]); // the search box ends where the overflow button begins
		}

		[Fact]
		public void The_first_item_that_does_not_fit_sends_every_later_one_to_the_drop_down_even_a_small_one()
		{
			ToolBarLayout.Plan plan = ToolBarLayout.Compute(new[] { Left(23), Left(60), Left(10), Left(10) }, 70);
			Assert.True(plan.HasOverflow);
			Assert.Equal(new[] { false, true, true, true }, plan.InOverflow);
		}

		[Fact]
		public void An_item_marked_Always_is_in_the_drop_down_however_wide_the_bar_is_and_brings_the_button()
		{
			ToolBarLayout.Plan plan = ToolBarLayout.Compute(new[] { Left(23), Left(23, ToolBarOverflow.Always), Left(23) }, 500);
			Assert.True(plan.HasOverflow);
			Assert.Equal(new[] { false, true, false }, plan.InOverflow);
			Assert.Equal(23, plan.X[2]);
		}

		[Fact]
		public void A_bar_narrower_than_the_never_items_still_keeps_them_and_overflows_everything_else()
		{
			ToolBarLayout.Plan plan = ToolBarLayout.Compute(new[] { Left(23), Right(130, ToolBarOverflow.Never) }, 100);
			Assert.True(plan.HasOverflow);
			Assert.Equal(new[] { true, false }, plan.InOverflow);
		}

		private static CrToolBar Bar(int buttons, double width, bool withSearch)
		{
			var bar = new CrToolBar { Width = width };
			for (int i = 0; i < buttons; i++)
			{
				bar.Children.Add(new CrToolButton { Name = "B" + i, IconName = "FitHeight" });
			}
			if (withSearch)
			{
				var search = new CrSearchBox { Name = "Search" };
				CrToolBar.SetAlignment(search, ToolBarAlignment.Right);
				CrToolBar.SetOverflow(search, ToolBarOverflow.Never);
				bar.Children.Add(search);
			}
			return bar;
		}

		[Fact]
		public async Task A_bar_that_fits_has_no_overflow_and_a_bar_that_is_too_narrow_shows_the_button_and_hides_the_last_buttons()
		{
			await Ui.Run(() =>
			{
				CrToolBar bar = Bar(6, 400, withSearch: true);
				Window window = ShowIn(bar, 420, 60);
				var search = bar.Children.OfType<CrSearchBox>().Single();
				Assert.False(bar.HasOverflow);
				Assert.Empty(bar.OverflowItems);
				// right aligned: the box (130, margin 1 on both sides) ends one unit before the bar does
				Assert.Equal(269, search.Bounds.X);
				Assert.Equal(399, search.Bounds.Right);
				Assert.Equal(23, bar.Children[1].Bounds.X);
				bar.Width = 200;
				Dpi.Settle(window);
				Assert.True(bar.HasOverflow);
				Assert.Equal(new[] { "B2", "B3", "B4", "B5" }, bar.OverflowItems.Select(c => c.Name).ToArray());
				// the overflow button takes the last 16 units, the search box (Never) ends in front of it, two buttons stay
				Assert.Equal(53, search.Bounds.X);
				Assert.Equal(183, search.Bounds.Right);
				Assert.Equal(184, bar.OverflowButtonControl.Bounds.X);
				Assert.Equal(23, bar.Children[1].Bounds.X);
				Assert.True(bar.Children[2].Bounds.Right <= 0, "a button in the drop-down is out of the bar's sight");
				bar.Width = 400;
				Dpi.Settle(window);
				Assert.False(bar.HasOverflow);
				Assert.Empty(bar.OverflowItems);
				Assert.Equal(46, bar.Children[2].Bounds.X);
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_drop_down_shows_the_items_that_did_not_fit_as_the_same_controls_and_gives_them_back_in_order_when_it_closes()
		{
			await Ui.Run(() =>
			{
				CrToolBar bar = Bar(6, 200, withSearch: true);
				Window window = ShowIn(bar, 220, 60);
				Assert.True(bar.HasOverflow);
				Control[] before = bar.Children.ToArray();
				Control[] hidden = bar.OverflowItems.ToArray();
				Assert.Equal(4, hidden.Length);
				bar.ShowOverflow();
				Dispatcher.UIThread.RunJobs();
				Assert.True(bar.IsOverflowOpen);
				Assert.All(hidden, h => Assert.DoesNotContain(h, bar.Children));
				bar.HideOverflow();
				Dispatcher.UIThread.RunJobs();
				Assert.False(bar.IsOverflowOpen);
				Assert.Equal(before, bar.Children.ToArray()); // the same controls in the same order
				Dpi.Settle(window);
				Assert.Equal(hidden, bar.OverflowItems.ToArray());
				// a click in the drop-down reaches the button: the button's own Click fires and the drop-down closes
				bar.ShowOverflow();
				Dispatcher.UIThread.RunJobs();
				var clicked = hidden[1] as CrToolButton;
				int clicks = 0;
				clicked.Click += (s, e) => clicks++;
				clicked.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
				Dispatcher.UIThread.RunJobs();
				Assert.Equal(1, clicks);
				Assert.False(bar.IsOverflowOpen);
				Assert.Equal(before, bar.Children.ToArray());
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_overflow_button_takes_the_pointer_only_while_it_is_shown_and_opens_the_drop_down()
		{
			await Ui.Run(() =>
			{
				CrToolBar bar = Bar(6, 400, withSearch: false);
				Window window = ShowIn(bar, 420, 60);
				Control button = bar.OverflowButtonControl;
				Assert.False(button.IsHitTestVisible);
				bar.Width = 100;
				Dpi.Settle(window);
				Assert.True(bar.HasOverflow);
				Assert.True(button.IsHitTestVisible);
				Assert.Equal(16, button.Bounds.Width);
				Assert.Equal(100 - 16, button.Bounds.X);
				bar.ShowOverflow();
				Assert.True(bar.IsOverflowOpen);
				bar.HideOverflow();
				window.Close();
				return Task.CompletedTask;
			});
		}

		private static (MainWindow window, MainViewModel vm) NewWindow()
		{
			var service = new HostService(AppOptions.Parse(new[] { "--data-dir", Path.Combine(Path.GetTempPath(), "crmac-never-started") }));
			var vm = new MainViewModel(new NullShell(), service, new FakeDialogs());
			return (new MainWindow { DataContext = vm }, vm);
		}

		[Fact]
		public async Task In_the_library_window_the_quick_search_sits_at_the_right_end_and_a_narrow_window_moves_the_last_buttons_to_the_drop_down()
		{
			await Ui.Run(() =>
			{
				var (window, vm) = NewWindow();
				window.Width = 1100;
				window.Show();
				Dpi.Settle(window);
				var bar = window.FindControl<CrToolBar>("BrowserToolBar");
				var search = window.FindControl<CrSearchBox>("SearchBox");
				Assert.False(bar.HasOverflow);
				Assert.Equal(bar.Bounds.Width - 1, search.Bounds.Right);
				// the buttons that follow the Arrange button in the designer's order stay on its left (the search box is not between them)
				Assert.True(window.FindControl<CrDropDownButton>("ListLayoutsButton").Bounds.X > window.FindControl<CrSplitButton>("ArrangeButton").Bounds.X);
				Assert.True(window.FindControl<CrToolButton>("RedoButton").Bounds.Right < search.Bounds.X);
				// 640 wide with the 252 wide side panel leaves 388 units for the bar: the last buttons go, the first ones and the search box stay
				window.Width = 640;
				Dpi.Settle(window);
				Assert.True(bar.HasOverflow);
				string[] hidden = bar.OverflowItems.Select(c => c.Name ?? "-").ToArray();
				Assert.Equal("RedoButton", hidden[^1]);
				Assert.Contains("UndoButton", hidden);
				Assert.DoesNotContain("SidebarButton", hidden);
				Assert.DoesNotContain("SearchBox", hidden);
				Assert.Equal(bar.Bounds.Width - 16 - 1, search.Bounds.Right);
				// the drop-down gives the buttons their own controls (the disabled ones stay disabled)
				bar.ShowOverflow();
				Dispatcher.UIThread.RunJobs();
				Assert.True(bar.IsOverflowOpen);
				Assert.False(window.FindControl<CrToolButton>("UndoButton").IsEffectivelyEnabled);
				bar.HideOverflow();
				Assert.False(bar.IsOverflowOpen);
				Assert.Contains(window.FindControl<CrToolButton>("RedoButton"), bar.Children);
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		[Trait("Category", "HiDpi")]
		public async Task The_overflow_button_is_16_by_24_units_with_a_one_unit_bar_in_whole_device_pixels(double scale)
		{
			await InAppearance("Light", () =>
			{
				CrToolBar bar = Bar(6, 100, withSearch: false);
				Window window = ShowIn(bar, 120, 40, scale);
				Control button = bar.OverflowButtonControl;
				Assert.True(bar.HasOverflow);
				Assert.Equal(new Size(16, 24), button.Bounds.Size);
				Assert.Equal(25, bar.Bounds.Height);
				Raster frame = Dpi.Capture(window, "toolbar-overflow@" + Scales.Tag(scale));
				SKColor ink = Token(ThemeTokens.Names.ControlText, "Light");
				Point o = Dpi.Origin(button, window);
				// the bar over the arrow: 7 units wide, one unit high, 5 units from the left edge of the button, 9 units from its top
				int x0 = Scales.Device(o.X + 5, scale), y0 = Scales.Device(o.Y + 9, scale);
				int w = Scales.Device(7, scale), h = Scales.Device(1, scale);
				Assert.Equal(w, frame.CountInRow(y0, x0, x0 + w, c => Raster.Near(c, ink, 1)));
				Assert.Equal(h, frame.CountInColumn(x0 + w / 2, y0 - 2 * h, y0 + 2 * h, c => Raster.Near(c, ink, 1)));
				Assert.False(Raster.Near(frame[x0 - 1, y0], ink, 1));
				Assert.False(Raster.Near(frame[x0 + w, y0], ink, 1));
				window.Close();
			});
		}
	}
}
