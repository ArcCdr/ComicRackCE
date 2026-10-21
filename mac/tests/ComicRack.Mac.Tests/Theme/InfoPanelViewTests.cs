using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Host;
using ComicRack.Mac.Info;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Tests.Reader;
using ComicRack.Mac.Tests.Browser;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using ComicRack.Mac.Views;
using Xunit;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// P5.6b: the info panel in the main window (<c>pluginContainer</c> under the browser): hidden until a page exists, 162 high with the grip on top, the tab strip, the page
	/// that shows, the 6 units at the bottom, the Browse &gt; Info Panel item (Shift+F9, unavailable without a page).
	/// </summary>
	public class InfoPanelViewTests : IDisposable
	{
		private readonly long liveBefore;
		private readonly bool animation;

		public InfoPanelViewTests()
		{
			liveBefore = Gc.BeginCount();
			animation = CrSizableContainer.EnableAnimation;
			CrSizableContainer.EnableAnimation = false;
		}

		public void Dispose()
		{
			CrSizableContainer.EnableAnimation = animation;
			long expected = liveBefore;
			Gc.WaitForLive(expected);
			Assert.Equal(expected, Gc.Live);
		}

		[Fact]
		public async Task The_container_is_hidden_and_6_units_stay_free_until_a_page_is_added_then_it_is_its_grip_and_162_high_open_under_the_browser()
		{
			await Ui.Run(() =>
			{
				using (var h = new MainReaderHarness(new AppPreferences()))
				{
					MainWindow w = h.Window;
					CrSizableContainer dock = w.InfoDock;
					Border placeholder = w.FindControl<Border>("InfoPlaceholder");
					Assert.False(dock.IsVisible);
					Assert.True(placeholder.IsVisible);
					Assert.Equal(6, placeholder.Bounds.Height);
					var grid = w.FindControl<Control>("BookGrid");
					double gridBottomWithout = Dpi.Area(grid, w).Bottom;
					Assert.Equal(Dpi.Area(placeholder, w).Top, gridBottomWithout, 1);

					h.Vm.InfoPanel.AddPage(new TestInfoPage());
					h.Tick();
					Assert.True(dock.IsVisible);
					Assert.False(placeholder.IsVisible);
					Assert.Equal(GripPosition.Top, dock.Grip);
					Assert.True(dock.KeepGripVisible);
					Assert.False(dock.IsExpanded);
					Assert.Equal(6, dock.Bounds.Height); // only the grip

					h.Vm.InfoPanel.IsVisible = true;
					h.Tick();
					Assert.True(dock.IsExpanded);
					Assert.Equal(162, dock.Bounds.Height);
					Assert.Equal(new Rect(0, 0, dock.Bounds.Width, 6), dock.GripRectangle);
					// to the right of the side panel and under the book list
					CrSizableContainer side = w.FindControl<CrSizableContainer>("SidebarPanel");
					Assert.True(Dpi.Area(dock, w).Left >= Dpi.Area(side, w).Right - 0.01);
					Assert.True(Dpi.Area(grid, w).Bottom <= Dpi.Area(dock, w).Top + 0.01);
					// the pane has 6 units of padding at its bottom
					Assert.Equal(dock.Bounds.Height - 12, w.Info.Bounds.Height, 1); // the grip above, the padding below
					Assert.Equal(dock.Bounds.Width, w.Info.Bounds.Width, 1);

					dock.DragBy(30);
					h.Tick();
					Assert.Equal(192, h.Vm.InfoPanel.Extent);
					Assert.Equal(192, h.Preferences.InfoBrowserSize);
					dock.IsExpanded = false;
					Assert.False(h.Vm.InfoPanel.IsVisible);
					Assert.False(h.Preferences.ShowInfo);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_tab_strip_shows_with_two_pages_and_the_selected_tab_decides_which_page_shows()
		{
			await Ui.Run(() =>
			{
				using (var h = new MainReaderHarness(new AppPreferences()))
				{
					MainWindow w = h.Window;
					var a = new FakeInfoPage("Alpha");
					var b = new FakeInfoPage("Beta");
					h.Vm.InfoPanel.AddPage(a);
					h.Vm.InfoPanel.IsVisible = true;
					h.Tick();
					Assert.False(w.Info.TabBar.IsVisible); // one page: no strip
					Assert.Same(a.View, w.Info.PageHost.Child);

					h.Vm.InfoPanel.AddPage(b);
					h.Tick();
					CrTabBar bar = w.Info.TabBar;
					Assert.True(bar.IsVisible);
					Assert.Equal(31, bar.Bounds.Height);
					Assert.Equal(new[] { "Alpha", "Beta" }, bar.Tabs.Select(t => t.Text).ToArray());
					Assert.All(bar.Tabs, t => Assert.False(t.CanClose));
					Assert.Equal(0, bar.SelectedIndex);
					Assert.Same(a.View, w.Info.PageHost.Child);
					Assert.Equal(31, w.Info.PageHost.Bounds.Top, 1);

					bar.Click(bar.Geometry[1].Bounds.Center); // a click on the second tab
					h.Tick();
					Assert.Equal(1, h.Vm.InfoPanel.SelectedIndex);
					Assert.Same(b.View, w.Info.PageHost.Child);
					b.Title = "Gamma";
					Assert.Equal("Gamma", bar.Tabs[1].Text);

					h.Vm.InfoPanel.RemovePage(b);
					h.Tick();
					Assert.False(bar.IsVisible);
					Assert.Same(a.View, w.Info.PageHost.Child);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Browse_Info_Panel_follows_Small_Preview_is_unavailable_without_a_page_and_toggles_the_panel_with_Shift_F9()
		{
			await Ui.Run(() =>
			{
				using (var h = new MainReaderHarness(new AppPreferences()))
				{
					Avalonia.Controls.NativeMenu bar = MainMenu.BuildMenuBar(h.Vm);
					NativeMenuItem browse = bar.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Browse");
					NativeMenuItem[] items = browse.Menu.Items.OfType<NativeMenuItem>().Where(i => !(i is NativeMenuItemSeparator)).ToArray();
					int preview = Array.FindIndex(items, i => i.Header == "Small Preview");
					NativeMenuItem item = items[preview + 1];
					Assert.Equal("Info Panel", item.Header);
					Assert.Equal(new KeyGesture(Key.F9, KeyModifiers.Shift), item.Gesture);
					Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType);
					Assert.False(item.IsChecked);
					Assert.False(item.IsEnabled);
					Assert.False(item.Command.CanExecute(null));

					h.Vm.InfoPanel.AddPage(new TestInfoPage());
					h.Tick();
					Assert.True(item.IsEnabled);
					Assert.True(item.Command.CanExecute(null));
					item.Command.Execute(null);
					h.Tick();
					Assert.True(item.IsChecked);
					Assert.True(h.Window.InfoDock.IsExpanded);
					item.Command.Execute(null);
					Assert.False(item.IsChecked);
					Assert.False(h.Window.InfoDock.IsExpanded);

					h.Vm.InfoPanel.RemovePage(h.Vm.InfoPanel.Pages[0].Page);
					Assert.False(item.IsEnabled);
				}
				return Task.CompletedTask;
			});
		}
	}
}
