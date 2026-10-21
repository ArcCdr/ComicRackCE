using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Host;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Tests.Reader;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Themes;
using ComicRack.Mac.Thumbnails;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using SkiaSharp;
using Xunit;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// P5.6a: the small preview in the main window (<c>SmallComicPreview</c> in the <c>previewPane</c> under the tree): its place and size, the flat border, the toolbar in the
	/// original's order with the original's images and texts, the page viewer (paper and message of the small preview), the keys, the wheel and the double click, and the
	/// Browse &gt; Small Preview item.
	/// </summary>
	public class SmallPreviewViewTests : IDisposable
	{
		private readonly long liveBefore;
		private readonly bool animation;

		public SmallPreviewViewTests()
		{
			liveBefore = Gc.SettleLive();
			Ui.BeginTest();
			animation = CrSizableContainer.EnableAnimation;
			CrSizableContainer.EnableAnimation = false;
		}

		public void Dispose()
		{
			CrSizableContainer.EnableAnimation = animation;
			long expected = Ui.LiveBaseline(liveBefore);
			Gc.WaitForLive(expected);
			Assert.Equal(expected, PixelBlock.LiveCount);
		}

		private sealed class Setup : IDisposable
		{
			public Setup(int pages = 8)
			{
				Harness = new MainReaderHarness(new AppPreferences());
				Scheduler = new Browser.ManualLiveScheduler();
				SmallPreviewViewModel preview = Harness.Vm.SmallPreview;
				preview.Scheduler = Scheduler;
				preview.FirstSelectedBook = () => Selected;
				preview.SessionFactory = book => new FakeSession(new FakePageDecoder { PageCount = pages, Size = p => (300, 450) }) { Title = book.Title };
				preview.OpenBook = book => Opened.Add(book);
			}

			public MainReaderHarness Harness { get; }

			public Browser.ManualLiveScheduler Scheduler { get; }

			public ComicBook Selected { get; set; }

			public List<ComicBook> Opened { get; } = new List<ComicBook>();

			public MainWindow Window => Harness.Window;

			public SmallPreviewViewModel Model => Harness.Vm.SmallPreview;

			public SmallPreviewPane Pane => Harness.Window.Preview;

			public void Open()
			{
				Model.IsVisible = true;
				Harness.Tick();
			}

			public async Task Show(string name)
			{
				Selected = new ComicBook { Title = name, FilePath = "/books/" + name + ".cbz", PageCount = 8 };
				Model.SelectionChanged();
				Scheduler.RunPending();
				Dispatcher.UIThread.RunJobs();
				var deadline = DateTime.UtcNow.AddSeconds(20);
				while (Model.Reader == null || !Model.Reader.IsOpen)
				{
					if (DateTime.UtcNow > deadline)
					{
						throw new TimeoutException("The preview did not open its book.");
					}
					Dispatcher.UIThread.RunJobs();
					await Task.Delay(5);
				}
				await Model.Reader.WhenSettled;
				Harness.Tick();
			}

			public void Dispose()
			{
				Model.Close(); // the app does it when it quits (MainViewModel.CloseReaders)
				Harness.Dispose();
			}
		}

		private static string[] Names(CrToolBar bar) => bar.Children.Select(c => c is CrToolSeparator ? "-" : c.Name).ToArray();

		[Fact]
		public async Task The_pane_sits_under_the_tree_closed_with_its_grip_and_207_high_open_with_a_flat_border_two_units_in()
		{
			await Ui.Run(() =>
			{
				using (var s = new Setup())
				{
					CrSizableContainer dock = s.Window.PreviewDock;
					CrSizableContainer sidebar = s.Window.FindControl<CrSizableContainer>("SidebarPanel");
					Assert.Equal(GripPosition.Top, dock.Grip);
					Assert.True(dock.KeepGripVisible);
					Assert.True(dock.FlatBorder);
					Assert.False(dock.IsExpanded); // hidden by default
					Assert.Equal(6, dock.Bounds.Height); // only the grip
					CrTreeView tree = s.Window.FindControl<CrTreeView>("SidebarTree");
					Assert.True(Dpi.Area(tree, s.Window).Bottom <= Dpi.Area(dock, s.Window).Top + 0.01, "the tree is above the preview");

					s.Open();
					Assert.True(dock.IsExpanded);
					Assert.Equal(207, dock.Bounds.Height);
					Assert.Equal(sidebar.Bounds.Width - 6, dock.Bounds.Width); // inside the side panel, which has its own grip at the right
					Assert.Equal(new Rect(0, 0, dock.Bounds.Width, 6), dock.GripRectangle);
					// ExtendedBorderStyle.Flat: the child is 2 units inside the area below the grip (246 x 201 -> 242 x 197 in the original's 246 x 207 pane)
					Assert.Equal(new Rect(0, 6, dock.Bounds.Width, 201), dock.FrameRectangle);
					Rect child = s.Pane.Bounds;
					Assert.Equal(new Rect(2, 8, dock.Bounds.Width - 4, 197), child);
					Assert.Equal(25, s.Pane.Toolbar.Bounds.Height);

					// the grip drag sets the height and the preferences keep it
					dock.DragBy(30);
					s.Harness.Tick();
					Assert.Equal(237, s.Model.Extent);
					Assert.Equal(237, s.Harness.Preferences.PreviewSplit);
					dock.IsExpanded = false;
					Assert.False(s.Model.IsVisible);
					Assert.False(s.Harness.Preferences.ShowPreview);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_toolbar_has_the_buttons_of_the_original_in_its_order_with_its_images_and_texts_and_Close_at_the_right_end()
		{
			await Ui.Run(async () =>
			{
				using (var s = new Setup())
				{
					s.Open();
					CrToolBar bar = s.Pane.Toolbar;
					Assert.Equal(new[] { "PreviewOpenButton", "-", "PreviewFirstButton", "PreviewPreviousButton", "PreviewNextButton", "PreviewLastButton", "-", "PreviewTwoPagesButton", "-", "PreviewRefreshButton", "PreviewCloseButton" }, Names(bar));
					var expected = new (string name, string icon, string tip)[]
					{
						("PreviewOpenButton", "Open", "Open"),
						("PreviewFirstButton", "GoFirst", "Go to first page"),
						("PreviewPreviousButton", "GoPrevious", "Go to previous page"),
						("PreviewNextButton", "GoNext", "Go to next page"),
						("PreviewLastButton", "GoLast", "Go to last page"),
						("PreviewTwoPagesButton", "TwoPage", "Show one or two pages"),
						("PreviewRefreshButton", "Refresh", "Refresh"),
						("PreviewCloseButton", "SmallClose", "Close"),
					};
					foreach ((string name, string icon, string tip) in expected)
					{
						var button = s.Pane.FindControl<CrToolButton>(name);
						Assert.Equal(icon, button.IconName);
						Assert.True(string.IsNullOrEmpty(button.Text)); // DisplayStyle Image
						Assert.Equal(tip, ToolTip.GetTip(button));
					}
					var close = s.Pane.FindControl<CrToolButton>("PreviewCloseButton");
					Assert.Equal(ToolBarAlignment.Right, CrToolBar.GetAlignment(close));
					Assert.Equal(ToolBarOverflow.Never, CrToolBar.GetOverflow(close));
					Rect closeArea = Dpi.Area(close, s.Window);
					Assert.Equal(Dpi.Area(bar, s.Window).Right, closeArea.Right, 1);

					// nothing shown: only Close is on
					s.Harness.Tick();
					string states = string.Join(", ", expected.Select(e => e.name + "=" + s.Pane.FindControl<CrToolButton>(e.name).IsEffectivelyEnabled + "/" + s.Pane.FindControl<CrToolButton>(e.name).Command?.CanExecute(null)));
					foreach ((string name, _, _) in expected)
					{
						Assert.True(name == "PreviewCloseButton" == s.Pane.FindControl<CrToolButton>(name).IsEffectivelyEnabled, states + " | " + name + " is " + s.Pane.FindControl<CrToolButton>(name).IsEffectivelyEnabled + ", command " + s.Pane.FindControl<CrToolButton>(name).Command);
					}
					await s.Show("A");
					Assert.True(s.Pane.FindControl<CrToolButton>("PreviewOpenButton").IsEffectivelyEnabled);
					Assert.False(s.Pane.FindControl<CrToolButton>("PreviewFirstButton").IsEffectivelyEnabled);
					Assert.False(s.Pane.FindControl<CrToolButton>("PreviewPreviousButton").IsEffectivelyEnabled);
					Assert.True(s.Pane.FindControl<CrToolButton>("PreviewNextButton").IsEffectivelyEnabled);
					Assert.True(s.Pane.FindControl<CrToolButton>("PreviewLastButton").IsEffectivelyEnabled);
					Assert.True(s.Pane.FindControl<CrToolButton>("PreviewTwoPagesButton").IsEffectivelyEnabled);
					Assert.True(s.Pane.FindControl<CrToolButton>("PreviewRefreshButton").IsEffectivelyEnabled);
					var two = s.Pane.FindControl<CrToolButton>("PreviewTwoPagesButton");
					Assert.False(two.IsChecked);
					s.Model.TwoPagesCommand.Execute(null);
					s.Harness.Tick();
					Assert.True(two.IsChecked); // the pressed-in look follows the setting
				}
			});
		}

		[Fact]
		public async Task The_page_viewer_has_the_paper_and_the_message_of_the_small_preview_and_shows_the_page_when_a_book_is_there()
		{
			await Ui.Run(async () =>
			{
				using (var s = new Setup())
				{
					s.Open();
					ReaderView viewer = s.Pane.Viewer;
					Assert.Equal(ReaderPageLook.SmallPreview, viewer.Look);
					Assert.Equal("Nothing Selected", viewer.PlaceholderText);
					Rect area = Dpi.Area(viewer, s.Window);
					Assert.Equal(new Rect(2, 8 + 25, s.Pane.Bounds.Width, 172).Height, area.Height);
					Raster frame = Dpi.Capture(s.Window, "small-preview-empty");
					SKColor paper = Token(ThemeTokens.Names.SmallPreviewPageViewerBack, "Light");
					SKColor ink = Token(ThemeTokens.Names.SmallPreviewPageViewerText, "Light");
					Assert.Equal(paper, frame[(int)area.X + 3, (int)area.Y + 3]);
					RectInt region = RectInt.Snapped(area, 1.0);
					RectInt? text = frame.Bounds(c => Raster.Near(c, ink, 30), region);
					Assert.NotNull(text); // "Nothing Selected" in the colour of the message
					Assert.InRange(text.Value.Left + text.Value.Width / 2, region.Left + region.Width / 2 - 12, region.Left + region.Width / 2 + 12); // centred
					Assert.InRange(text.Value.Top + text.Value.Height / 2, region.Top + region.Height / 2 - 12, region.Top + region.Height / 2 + 12);

					await s.Show("A");
					Assert.Equal(string.Empty, viewer.PlaceholderText);
					Raster shown = Dpi.Capture(s.Window, "small-preview-page");
					Assert.True(Raster.Near(shown[(int)area.X + (int)area.Width / 2, (int)area.Y + (int)area.Height / 2], SkColorOf(FakePageDecoder.ColorFor(0)), 2), "the first page is in the middle of the viewer");
					Assert.Equal(paper, shown[(int)area.X + 3, (int)area.Y + 3]);
					Assert.Null(shown.Bounds(c => Raster.Near(c, ink, 4), region)); // the message is gone
					Assert.Null(viewer.ContextFlyout); // no context menu
				}
			});
		}

		private static SKColor SkColorOf(SKColor c) => c;

		[Fact]
		public async Task The_flat_border_is_a_one_unit_frame_in_the_border_colour_of_the_appearance_with_a_unit_of_panel_colour_inside()
		{
			foreach (string variantName in new[] { "Light", "Dark" })
			{
				await InAppearance(variantName, () =>
				{
					using (var s = new Setup())
					{
						s.Open();
						CrSizableContainer dock = s.Window.PreviewDock;
						Rect frameRect = dock.FrameRectangle;
						Point o = Dpi.Origin(dock, s.Window);
						Raster frame = Dpi.Capture(s.Window, "small-preview-border-" + variantName);
						SKColor border = Token(variantName == "Dark" ? ThemeTokens.Names.BorderDefault : ThemeTokens.Names.ControlDark, variantName);
						int left = (int)(o.X + frameRect.Left), top = (int)(o.Y + frameRect.Top), right = (int)(o.X + frameRect.Right) - 1, bottom = (int)(o.Y + frameRect.Bottom) - 1;
						int middleY = (top + bottom) / 2, middleX = (left + right) / 2;
						Assert.Equal(border, frame[left, middleY]);
						Assert.Equal(border, frame[right, middleY]);
						Assert.Equal(border, frame[middleX, top]);
						Assert.Equal(border, frame[middleX, bottom]);
						// one unit only: the next pixel is the panel and the one after it belongs to the toolbar (2 units in)
						Assert.False(Raster.Near(frame[left + 1, middleY], border, 0));
						Assert.False(Raster.Near(frame[right - 1, middleY], border, 0));
						Assert.False(Raster.Near(frame[middleX, bottom - 1], border, 0));
					}
				});
			}
		}

		[Fact]
		public async Task Up_and_Down_and_the_wheel_scroll_the_page_the_wheel_never_turns_it_and_a_double_click_opens_the_book_in_a_new_tab()
		{
			await Ui.Run(async () =>
			{
				using (var s = new Setup())
				{
					s.Open();
					await s.Show("A");
					ReaderViewModel reader = s.Model.Reader;
					reader.FitWidth(); // 300 x 450 at the width of the viewer: taller than the viewer, so it scrolls
					await reader.WhenSettled;
					s.Harness.Tick();
					Assert.True(reader.CanScrollDown);
					ReaderView viewer = s.Pane.Viewer;
					Rect area = Dpi.Area(viewer, s.Window);
					var middle = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
					s.Window.MouseMove(middle);
					viewer.Focus();
					Assert.Equal(0, reader.ScrollY);

					s.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
					s.Harness.Tick();
					Assert.Equal(reader.Options.ArrowScrollStep, reader.ScrollY);
					s.Window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
					s.Harness.Tick();
					Assert.Equal(0, reader.ScrollY);

					s.Window.MouseWheel(middle, new Vector(0, -2));
					s.Harness.Tick();
					Assert.Equal(2 * reader.Options.WheelScrollStep, reader.ScrollY);
					s.Window.MouseWheel(middle, new Vector(0, 1));
					s.Harness.Tick();
					Assert.Equal(reader.Options.WheelScrollStep, reader.ScrollY);
					// a sideways swipe or Cmd+wheel neither turns the page nor zooms
					for (int i = 0; i < 6; i++)
					{
						s.Window.MouseWheel(middle, new Vector(8, 0));
					}
					s.Window.MouseWheel(middle, new Vector(0, 3), RawInputModifiers.Meta);
					s.Harness.Tick();
					Assert.Equal(0, reader.CurrentPage);
					Assert.Equal(1.0, reader.Zoom);

					// a double click opens the book of the preview in a new tab, and is not full screen
					s.Window.MouseDown(middle, MouseButton.Left);
					s.Window.MouseUp(middle, MouseButton.Left);
					s.Window.MouseDown(middle, MouseButton.Left);
					s.Window.MouseUp(middle, MouseButton.Left);
					s.Harness.Tick();
					Assert.Single(s.Opened);
					Assert.Same(s.Selected, s.Opened[0]);
					Assert.False(reader.IsFullScreen);
				}
			});
		}

		[Fact]
		public async Task Browse_Small_Preview_is_a_check_item_on_Shift_F7_after_the_sidebar_that_follows_the_pane()
		{
			await Ui.Run(() =>
			{
				using (var s = new Setup())
				{
					NativeMenu bar = MainMenu.BuildMenuBar(s.Harness.Vm);
					NativeMenuItem browse = bar.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Browse");
					NativeMenuItem[] items = browse.Menu.Items.OfType<NativeMenuItem>().Where(i => !(i is NativeMenuItemSeparator)).ToArray();
					int sidebar = Array.FindIndex(items, i => i.Header == "Sidebar");
					NativeMenuItem item = items[sidebar + 1];
					Assert.Equal("Small Preview", item.Header);
					Assert.Equal(new KeyGesture(Key.F7, KeyModifiers.Shift), item.Gesture);
					Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType);
					Assert.False(item.IsChecked);
					Assert.True(item.Command.CanExecute(null));
					item.Command.Execute(null);
					s.Harness.Tick();
					Assert.True(item.IsChecked);
					Assert.True(s.Window.PreviewDock.IsExpanded);
					s.Model.CloseCommand.Execute(null); // the Close button
					Assert.False(item.IsChecked);
					Assert.False(s.Window.PreviewDock.IsExpanded);
				}
				return Task.CompletedTask;
			});
		}
	}
}
