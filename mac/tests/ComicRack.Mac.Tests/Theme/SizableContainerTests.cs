using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Tests.HiDpi;
using Xunit;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>P5.32a: the docked panel as the browser dock of the main window needs it: a click on the grip collapses and expands it, and it can fill its parent (<c>Dock = Fill</c>).</summary>
	public class SizableContainerTests
	{
		private static (CrSizableContainer dock, Window window) Docked(double height = 300)
		{
			var dock = new CrSizableContainer { Grip = GripPosition.Top, ExpandedExtent = 120, Child = new Border() };
			var panel = new DockPanel { Width = 400, Height = height, LastChildFill = true };
			DockPanel.SetDock(dock, Dock.Bottom);
			panel.Children.Add(dock);
			panel.Children.Add(new Border());
			Window window = ShowIn(panel, 420, height + 20);
			return (dock, window);
		}

		[Fact]
		public async Task A_click_on_the_grip_collapses_and_expands_the_panel_and_a_drag_only_resizes_it()
		{
			await Ui.Run(() =>
			{
				CrSizableContainer.EnableAnimation = false;
				var (dock, window) = Docked();
				Assert.Equal(120, dock.Bounds.Height);
				Point grip = new Point(200, dock.Bounds.Y + 3);
				window.MouseMove(grip);
				window.MouseDown(grip, MouseButton.Left);
				window.MouseUp(grip, MouseButton.Left);
				Dpi.Settle(window);
				Assert.False(dock.IsExpanded);
				Assert.Equal(0, dock.Bounds.Height);
				// the grip is gone with the panel (the owner shows the dock again by a command or keeps the grip with KeepGripVisible)
				dock.KeepGripVisible = true;
				dock.IsExpanded = true;
				Dpi.Settle(window);
				Assert.Equal(120, dock.Bounds.Height);
				// a press that moves more than 4 units is a drag: the panel is resized and stays open
				grip = new Point(200, dock.Bounds.Y + 3);
				window.MouseMove(grip);
				window.MouseDown(grip, MouseButton.Left);
				window.MouseMove(grip + new Vector(0, -40));
				window.MouseUp(grip + new Vector(0, -40), MouseButton.Left);
				Dpi.Settle(window);
				Assert.True(dock.IsExpanded);
				Assert.Equal(160, dock.ExpandedExtent, 1);
				// the switch turns the click off
				dock.ClickTogglesExpanded = false;
				grip = new Point(200, dock.Bounds.Y + 3);
				window.MouseMove(grip);
				window.MouseDown(grip, MouseButton.Left);
				window.MouseUp(grip, MouseButton.Left);
				Assert.True(dock.IsExpanded);
				window.Close();
				CrSizableContainer.EnableAnimation = true;
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_container_that_fills_takes_all_the_room_and_has_no_grip_and_goes_back_to_its_extent()
		{
			await Ui.Run(() =>
			{
				CrSizableContainer.EnableAnimation = false;
				var (dock, window) = Docked();
				dock.Fill = true;
				Dpi.Settle(window);
				Assert.Equal(300, dock.Bounds.Height);
				Assert.Equal(default, dock.GripRectangle);
				Assert.Equal(new Rect(0, 0, 400, 300), dock.ContentRectangle);
				Assert.True(dock.IsExpanded);
				Assert.Equal(120, dock.ExpandedExtent);
				dock.Fill = false;
				Dpi.Settle(window);
				Assert.Equal(120, dock.Bounds.Height);
				Assert.Equal(new Rect(0, 0, 400, 6), dock.GripRectangle);
				window.Close();
				CrSizableContainer.EnableAnimation = true;
				return Task.CompletedTask;
			});
		}
	}
}
