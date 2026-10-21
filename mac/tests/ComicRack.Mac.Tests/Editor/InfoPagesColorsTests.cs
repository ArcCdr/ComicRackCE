using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Views;
using cYo.Common.Drawing;
using cYo.Projects.ComicRack.Engine;
using Xunit;

namespace ComicRack.Mac.Tests.Editor
{
	/// <summary>P5.7c: the Pages and Colors tabs inside the Info dialog on a real CBZ (IN.06, IN.07, PV.02 to PV.05): positions at the Designer's coordinates, the texts, the page menu, what OK and Cancel do with the pages and the colours.</summary>
	public class InfoPagesColorsTests
	{
		private static AppPreferences Allow() => new AppPreferences { UpdateComicFiles = true, UpdateComicBookFiles = true, CatalogOnlyForFileless = false, ShowCustomBookFields = true };

		private sealed class Fixture : IDisposable
		{
			public readonly PagesRig Rig = new PagesRig(realUi: true);
			public readonly ComicBook[] Books;
			public readonly BookEditorModel Model;
			public readonly ComicBookDialog Dialog;
			public readonly FakePageImages Images = new FakePageImages();

			public Fixture(int count = 2)
			{
				Books = Enumerable.Range(1, count).Select(i => Rig.Book(Rig.MakeCbz("book" + i + ".cbz", PagesRig.ThreePages))).ToArray();
				var lists = new EditorValueLists(() => Books, Array.Empty<string>());
				Model = new BookEditorModel(Books[0], Books, Allow(), lists) { PageServices = Rig.Services };
				Dialog = new ComicBookDialog(new BookInfoRequest { Model = Model, Preferences = Allow() });
				Dialog.ColorsPage.ImageSource = editor => Images;
				Dialog.Show();
				Dpi.Settle(Dialog);
			}

			public void Select(string tab)
			{
				Dialog.Tabs.SelectedItem = Dialog.TabPages.Single(t => (string)t.Tag == tab);
				Dpi.Settle(Dialog);
			}

			/// <summary>Opens the Pages tab and waits until its list shows the rows.</summary>
			public PageListModel OpenPages(int rows = 3)
			{
				Select("tabPages");
				Until(() => Dialog.PagesPage.Pages != null && Dialog.PagesPage.Pages.Count == rows && Dialog.PagesPage.Pages.IsIndexComplete);
				return Dialog.PagesPage.Pages;
			}

			/// <summary>Draws the list until the pictures of the first rows are there.</summary>
			public void WaitForPictures(PageListModel pages, int rows)
			{
				Until(() =>
				{
					bool all = true;
					for (int i = 0; i < rows; i++)
					{
						CoverImage picture = pages.Picture(i, Dialog.PagesPage.List.Mode == PageListMode.Detail ? 256 : 128);
						all &= picture != null;
						picture?.Release();
					}
					return all;
				});
				Dpi.Settle(Dialog);
			}

			public void Until(Func<bool> condition, int timeoutMs = 20000)
			{
				var watch = System.Diagnostics.Stopwatch.StartNew();
				while (!condition())
				{
					Dpi.Settle(Dialog);
					if (condition())
					{
						return;
					}
					if (watch.ElapsedMilliseconds > timeoutMs)
					{
						throw new TimeoutException("The condition was not reached in " + timeoutMs + " ms.");
					}
					Thread.Sleep(5);
				}
			}

			public void Dispose()
			{
				Dialog.Close();
				Dpi.Settle(Dialog);
				Rig.Dispose();
			}
		}

		// pages that are solid colours of the size the viewer asks for: the Colors tab reads them through this instead of the Core
		private sealed class FakePageImages : IPageImageSource
		{
			public readonly List<(int Page, int Width)> Asked = new List<(int, int)>();

			public System.Drawing.Bitmap Load(int page, int width)
			{
				lock (Asked)
				{
					Asked.Add((page, width));
				}
				var bitmap = new System.Drawing.Bitmap(width, width * 3 / 2);
				using (var g = System.Drawing.Graphics.FromImage(bitmap))
				{
					g.Clear(System.Drawing.Color.FromArgb(255, 90 + page * 20, 120, 150));
				}
				bitmap.SetPixel(0, 0, System.Drawing.Color.FromArgb(255, 235, 225, 205));
				return bitmap;
			}
		}

		private static void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

		private static void Click(SplitButton button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(SplitButton.ClickEvent));

		private static void AssertAt(UserControl page, string name, double x, double y, double width, double? height = null)
		{
			Control control = page.FindControl<Control>(name);
			Assert.NotNull(control);
			Rect area = Dpi.Area(control, page);
			bool ok = Math.Abs(area.X - x) < 0.5 && Math.Abs(area.Y - y) < 1.5 && (width <= 0 || Math.Abs(area.Width - width) < 0.5) && (height == null || Math.Abs(area.Height - height.Value) < 1.5);
			Assert.True(ok, name + " is at " + area + ", the Designer has " + x + "," + y + " " + width + (height == null ? string.Empty : " x " + height));
		}

		// ---- the Pages tab

		[Fact]
		public async Task The_Pages_tab_is_laid_out_at_the_positions_of_the_Designer()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					f.Select("tabPages");
					InfoPagesPage page = f.Dialog.PagesPage;
					Assert.Equal(566, page.Bounds.Width);
					Assert.InRange(page.Bounds.Height, 438, 446);
					double drop = 442 - page.Bounds.Height;
					AssertAt(page, "labelPagesInfo", 8, 14, 0);
					AssertAt(page, "btPageView", 479, 9, 73, 23);
					AssertAt(page, "pagesView", 11, 38, 541, 363 - drop);
					AssertAt(page, "btResetPages", 441, 407 - drop, 111, 23);
					f.Dialog.Width += 100;
					Dpi.Settle(f.Dialog);
					AssertAt(page, "btPageView", 579, 9, 73, 23); // anchored top right
					AssertAt(page, "pagesView", 11, 38, 641, 363 - drop); // anchored on all sides
					AssertAt(page, "btResetPages", 541, 407 - drop, 111, 23);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_texts_of_the_Pages_tab_are_the_designers_english_texts()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					f.Select("tabPages");
					InfoPagesPage page = f.Dialog.PagesPage;
					Assert.Equal("Change the page order with drag & drop or use the context menu to change page types:", page.InfoLabel.Content);
					Assert.Equal("Pages", ((TextBlock)((Grid)page.PageViewButton.Content).Children[0]).Text);
					Assert.Equal("Reset", page.ResetButton.Content);
					Assert.Equal(new[] { "Order by Name", "Order by Name (numeric)" }, page.OrderItems.Select(i => (string)i.Header).ToArray());
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_Pages_tab_lists_the_pages_of_the_book_with_the_column_headers_of_the_pages_view()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					PageListModel pages = f.OpenPages();
					CrPageList list = f.Dialog.PagesPage.List;
					Assert.Same(pages, list.Source);
					Assert.Equal(new[] { "p1.png", "p2.png", "p10.png" }, pages.Rows.Select(r => r.Key).ToArray());
					Assert.Equal(new[] { "Page", "Thumbnail", "Type", "Size", "Width", "Height", "Name", "Bookmark", "Rotation", "Position" }, pages.Columns.Select(c => c.Title).ToArray());
					Assert.Equal(PageListMode.Detail, list.Mode);
					Assert.NotSame(f.Books[0], pages.Comic); // a copy
					// the other tabs were not touched by it
					Assert.True(f.Dialog.TabPages.Single(t => (string)t.Tag == "tabPages").IsVisible);
					// the three views as frames (CRMAC_FRAMES_DIR), with the pictures arrived
					list.RowHeight = 48;
					pages.SetSelection(new[] { 1 });
					f.WaitForPictures(pages, 3);
					Dpi.Capture(f.Dialog, "info-tabPages-details");
					list.Mode = PageListMode.Thumbnail;
					f.WaitForPictures(pages, 3);
					Dpi.Capture(f.Dialog, "info-tabPages-thumbnails");
					list.Mode = PageListMode.Tile;
					f.WaitForPictures(pages, 3);
					Dpi.Capture(f.Dialog, "info-tabPages-tiles");
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_page_list_is_made_when_the_tab_is_shown_not_before()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					f.Select("tabSummary"); // the dialog opens on the tab the last one closed with: leave the Pages tab and drop its list
					f.Model.Close();
					Assert.Null(f.Model.Pages);
					f.Select("tabDetails");
					Assert.Null(f.Model.Pages);
					f.OpenPages();
					Assert.NotNull(f.Model.Pages);
					PageListModel first = f.Model.Pages;
					f.Select("tabSummary");
					f.Select("tabPages");
					Assert.Same(first, f.Model.Pages); // the same list when the tab comes back
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_context_menu_has_the_items_of_the_original_in_its_order()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					f.OpenPages();
					PagesContextMenu menu = f.Dialog.PagesPage.Menu;
					string[] expected =
					{
						"miPageType", "cmPageRotate", "miPagePosition", "tsPageTypeSeparator", "miSetBookmark", "miRemoveBookmark", "tsBookmarkSeparator", "miMoveToTop", "miMoveToBottom",
						"miResetOriginalOrder", "tsMovePagesSeparator", "miCopy", "miMergePages", "toolStripMenuItem1", "miSelectAll", "miInvertSelection", "miRefreshThumbnail", "toolStripMenuItem3", "miMarkDeleted",
					};
					Assert.Equal(expected, menu.Entries.Select(e => expected.First(n => ReferenceEquals(menu[n], e))).ToArray());
					Assert.Equal(new[] { "Front Cover", "Inner Cover", "Roundup", "Story", "Advertisement", "Editorial", "Letters", "Preview", "Back Cover", "Other", "Deleted" }, menu.TypeItems.Select(i => (string)i.Header).ToArray());
					Assert.Equal(new[] { "None", "90°", "180°", "270°" }, menu.RotationItems.Select(i => (string)i.Header).ToArray());
					Assert.Equal(new[] { "Default", "Near", "Far" }, menu.PositionItems.Select(i => (string)i.Header).ToArray());
					Assert.Equal("Set Bookmark...", menu.MenuItemOf("miSetBookmark").Header);
					Assert.Equal("Mark as Deleted", menu.MenuItemOf("miMarkDeleted").Header);
					// Ctrl becomes Cmd and Alt Option; the types start at Alt+Shift+A, the rotations at Alt+Shift+6
					Assert.Equal(new KeyGesture(Key.B, KeyModifiers.Meta | KeyModifiers.Shift), menu.MenuItemOf("miSetBookmark").InputGesture);
					Assert.Equal(new KeyGesture(Key.T, KeyModifiers.Meta | KeyModifiers.Alt), menu.MenuItemOf("miMoveToTop").InputGesture);
					Assert.Equal(new KeyGesture(Key.Delete), menu.MenuItemOf("miMarkDeleted").InputGesture);
					Assert.Equal(new KeyGesture(Key.A, KeyModifiers.Alt | KeyModifiers.Shift), menu.TypeItems[0].InputGesture);
					Assert.Equal(new KeyGesture(Key.D6 + 3, KeyModifiers.Alt | KeyModifiers.Shift), menu.RotationItems[3].InputGesture);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_menu_shows_what_is_possible_for_the_selection()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					PageListModel pages = f.OpenPages();
					PagesContextMenu menu = f.Dialog.PagesPage.Menu;
					menu.Update();
					Assert.False(menu.MenuItemOf("miPageType").IsEnabled); // nothing selected
					Assert.False(menu.MenuItemOf("miMoveToTop").IsEnabled);
					Assert.False(menu.MenuItemOf("miSetBookmark").IsEnabled);
					Assert.False(menu.MenuItemOf("miMergePages").IsVisible);
					Assert.True(menu.MenuItemOf("miSelectAll").IsEnabled);
					Assert.False(menu.MenuItemOf("miCopy").IsEnabled); // Copy Page waits for the pages view of the main window

					pages.SetSelection(new[] { 1 });
					menu.Update();
					Assert.True(menu.MenuItemOf("miPageType").IsEnabled);
					Assert.True(menu.MenuItemOf("miSetBookmark").IsEnabled);
					Assert.False(menu.MenuItemOf("miRemoveBookmark").IsEnabled); // it has none
					Assert.True(menu.TypeItems.Single(i => (ComicPageType)i.Tag == ComicPageType.Story).IsChecked);
					Assert.Single(menu.TypeItems, i => i.IsChecked == true);
					Assert.True(menu.RotationItems[0].IsChecked);
					Assert.True(menu.PositionItems[0].IsChecked);

					pages.SetSelection(new[] { 0, 1 });
					menu.Update();
					Assert.False(menu.MenuItemOf("miSetBookmark").IsEnabled); // two pages
					Assert.True(menu.MenuItemOf("miMergePages").IsVisible);
					Assert.DoesNotContain(menu.TypeItems, i => i.IsChecked == true); // front cover and story: no common type
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_menu_items_and_their_keys_change_the_pages_and_OK_writes_them_into_the_book()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					PageListModel pages = f.OpenPages();
					PagesContextMenu menu = f.Dialog.PagesPage.Menu;
					pages.SetSelection(new[] { 2 });
					menu.TypeItems.Single(i => (ComicPageType)i.Tag == ComicPageType.Letters).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
					menu.RotationItems[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
					Assert.Equal(ComicPageType.Letters, pages.Comic.GetPage(2).PageType);
					Assert.Equal(ImageRotation.Rotate90, pages.Comic.GetPage(2).Rotation);
					Assert.True(menu.TryRun(Key.Delete, KeyModifiers.None)); // Mark as Deleted
					Assert.Equal(ComicPageType.Deleted, pages.Comic.GetPage(2).PageType);
					Assert.True(menu.TryRun(Key.T, KeyModifiers.Meta | KeyModifiers.Alt)); // Move to Top
					Assert.Equal(new[] { "p10.png", "p1.png", "p2.png" }, pages.Rows.Select(r => r.Key).ToArray());
					Assert.False(menu.TryRun(Key.Q, KeyModifiers.Meta)); // not a key of the menu

					// nothing reached the library's book while the dialog was open
					Assert.Equal(ComicPageType.Story, f.Books[0].GetPage(2).PageType);
					f.Dialog.Accept();
					Dpi.Settle(f.Dialog);
					Assert.Equal(2, f.Books[0].GetPage(0).ImageIndex);
					Assert.Equal(ComicPageType.Deleted, f.Books[0].GetPageByImageIndex(2).PageType);
					Assert.Equal(ImageRotation.Rotate90, f.Books[0].GetPageByImageIndex(2).Rotation);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Cancel_drops_the_changes_of_the_pages()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					PageListModel pages = f.OpenPages();
					pages.SetSelection(new[] { 1 });
					pages.SetPageType(ComicPageType.Advertisement);
					f.Dialog.Cancel();
					Dpi.Settle(f.Dialog);
					Assert.Equal(ComicPageType.Story, f.Books[0].GetPage(1).PageType);
					Assert.Null(f.Model.Pages); // the copy and its navigator are gone
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_Reset_button_restores_the_page_order_and_its_menu_orders_by_name()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					PageListModel pages = f.OpenPages();
					InfoPagesPage page = f.Dialog.PagesPage;
					page.ResetButton.Flyout.ShowAt(page.ResetButton);
					Dpi.Settle(f.Dialog);
					Assert.All(page.OrderItems, i => Assert.True(i.IsEnabled));
					page.OrderItems[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
					Assert.Equal(new[] { "p1.png", "p10.png", "p2.png" }, pages.Rows.Select(r => r.Key).ToArray());
					page.OrderItems[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
					Assert.Equal(new[] { "p1.png", "p2.png", "p10.png" }, pages.Rows.Select(r => r.Key).ToArray());
					page.OrderItems[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
					Click(page.ResetButton);
					Assert.Equal(new[] { "p1.png", "p2.png", "p10.png" }, pages.Rows.Select(r => r.Key).ToArray());
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_Pages_button_menu_chooses_the_view_and_the_column_to_arrange_by()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					PageListModel pages = f.OpenPages();
					InfoPagesPage page = f.Dialog.PagesPage;
					MenuFlyout flyout = page.BuildViewMenu();
					var view = (MenuItem)flyout.Items.Cast<object>().First();
					Assert.Equal("View", view.Header);
					Assert.Equal(new[] { "Thumbnails", "Tiles", "Details" }, view.Items.Cast<MenuItem>().Select(i => (string)i.Header).ToArray());
					Assert.True(((MenuItem)view.Items[2]).IsChecked);
					((MenuItem)view.Items[0]).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
					Assert.Equal(PageListMode.Thumbnail, page.List.Mode);
					var arrange = (MenuItem)flyout.Items.Cast<object>().Last();
					Assert.Equal("Arrange by", arrange.Header);
					string[] entries = arrange.Items.OfType<MenuItem>().Select(i => (string)i.Header).ToArray();
					Assert.Equal(new[] { "Not Sorted", "Page", "Type", "Size", "Width", "Height", "Name", "Bookmark", "Rotation", "Position" }, entries);
					arrange.Items.OfType<MenuItem>().Single(i => (string)i.Header == "Name").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
					Assert.Equal(PageColumns.Name, pages.SortColumn);
					// a new menu shows the checks of now
					MenuFlyout again = page.BuildViewMenu();
					Assert.True(again.Items.Cast<object>().OfType<MenuItem>().Last().Items.OfType<MenuItem>().Single(i => (string)i.Header == "Name").IsChecked);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_view_the_user_chose_is_kept_for_the_next_dialog()
		{
			await Ui.Run(() =>
			{
				PageListMode before;
				using (var f = new Fixture())
				{
					f.OpenPages();
					before = f.Dialog.PagesPage.List.Mode;
					f.Dialog.PagesPage.List.Mode = PageListMode.Tile;
					f.Dialog.PagesPage.List.RowHeight = 30;
				}
				using (var f = new Fixture())
				{
					Assert.Equal(PageListMode.Tile, f.Dialog.PagesPage.List.Mode);
					Assert.Equal(30, f.Dialog.PagesPage.List.RowHeight);
					f.Dialog.PagesPage.List.Mode = before;
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Previous_and_Next_write_the_pages_and_show_the_pages_of_the_other_book()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					PageListModel first = f.OpenPages();
					first.SetSelection(new[] { 2 });
					first.MoveSelectedToTop();
					Click(f.Dialog.NextButton);
					Dpi.Settle(f.Dialog);
					Assert.Equal(2, f.Books[0].GetPage(0).ImageIndex); // written by Next
					Assert.NotSame(first, f.Model.Pages);
					f.Until(() => f.Dialog.PagesPage.Pages != null && f.Dialog.PagesPage.Pages.Count == 3);
					PageListModel second = f.Dialog.PagesPage.Pages;
					Assert.Equal(new[] { "p1.png", "p2.png", "p10.png" }, second.Rows.Select(r => r.Key).ToArray());
					Assert.Same(second, f.Dialog.PagesPage.List.Source);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_page_in_the_list_shows_its_thumbnail_and_a_double_click_goes_to_the_Colors_tab()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					PageListModel pages = f.OpenPages();
					CrPageList list = f.Dialog.PagesPage.List;
					list.RowHeight = 40;
					Dpi.Settle(f.Dialog);
					f.Until(() => pages.Picture(0, 256) is CoverImage picture && Release(picture));
					Point p = Dpi.Origin(list, f.Dialog) + new Point(60, list.RowRect(1).Y + 5);
					f.Dialog.MouseDown(p, MouseButton.Left);
					f.Dialog.MouseUp(p, MouseButton.Left);
					f.Dialog.MouseDown(p, MouseButton.Left);
					f.Dialog.MouseUp(p, MouseButton.Left);
					Dpi.Settle(f.Dialog);
					Assert.Equal("tabColors", (string)((TabItem)f.Dialog.Tabs.SelectedItem).Tag);
				}
				return Task.CompletedTask;
			});
		}

		private static bool Release(CoverImage picture)
		{
			picture.Release();
			return true;
		}

		// ---- the Colors tab

		[Fact]
		public async Task The_Colors_tab_is_laid_out_at_the_positions_of_the_Designer_and_the_sliders_are_hidden_until_asked_for()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					f.Select("tabColors");
					InfoColorsPage page = f.Dialog.ColorsPage;
					Assert.False(page.ControlsPanel.IsVisible);
					double bottom = page.Bounds.Height - 317 + 0; // the image panel is as high as the page while the sliders are hidden
					AssertAt(page, "pageViewer", 11, 12, 545, page.Bounds.Height - 12 - 30);
					AssertAt(page, "btFirstPage", 11, page.Bounds.Height - 4 - 23, 32, 23);
					AssertAt(page, "btPrevPage", 43, page.Bounds.Height - 4 - 23, 32, 23);
					AssertAt(page, "btNextPage", 75, page.Bounds.Height - 4 - 23, 32, 23);
					AssertAt(page, "btLastPage", 107, page.Bounds.Height - 4 - 23, 32, 23);
					AssertAt(page, "chkShowImageControls", 416, page.Bounds.Height - 3 - 23, 140, 23);
					AssertAt(page, "labelCurrentPage", 184, page.Bounds.Height - 5 - 21, 202, 21);

					page.ImageControlsButton.IsChecked = true;
					Dpi.Settle(f.Dialog);
					Assert.True(page.ControlsPanel.IsVisible);
					Assert.Equal(125, page.ControlsPanel.Bounds.Height);
					f.Until(() => page.Viewer.Picture != null);
					Dpi.Capture(f.Dialog, "info-tabColors");
					double top = page.Bounds.Height - 125; // the sliders' panel is docked at the bottom
					Rect panel = Dpi.Area(page.ControlsPanel, page);
					Assert.Equal(top, panel.Y, 1);
					AssertAt(page, "tbSaturation", 78, top + 28, 192, 18);
					AssertAt(page, "tbBrightness", 78, top + 52, 192, 18);
					AssertAt(page, "tbContrast", 363, top + 28, 192, 18);
					AssertAt(page, "tbGamma", 363, top + 52, 192, 18);
					AssertAt(page, "tbSharpening", 81, top + 86, 189, 18);
					AssertAt(page, "btResetColors", 478, top + 86, 77, 24);
					AssertAt(page, "labelSaturation", 11, top + 34, 0);
					AssertAt(page, "labelBrightness", 11, top + 58, 0);
					AssertAt(page, "labelContrast", 296, top + 34, 0);
					AssertAt(page, "labelGamma", 296, top + 58, 0);
					AssertAt(page, "labelSharpening", 11, top + 92, 0);
					AssertAt(page, "pageViewer", 11, 12, 545, top - 12 - 30);
					// the sliders keep to the middle when the page is wider (nothing is anchored)
					f.Dialog.Width += 100;
					Dpi.Settle(f.Dialog);
					AssertAt(page, "tbSaturation", 128, top + 28, 192, 18);
					AssertAt(page, "btResetColors", 528, top + 86, 77, 24);
					AssertAt(page, "chkShowImageControls", 516, page.Bounds.Height - 125 - 3 - 23, 140, 23);
					page.ImageControlsButton.IsChecked = false;
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_slider_ranges_ticks_and_texts_are_those_of_the_trackbars()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					f.Select("tabColors");
					InfoColorsPage page = f.Dialog.ColorsPage;
					foreach (Slider s in new[] { page.Saturation, page.Brightness, page.Contrast, page.Gamma })
					{
						Assert.Equal(-100, s.Minimum);
						Assert.Equal(100, s.Maximum);
						Assert.Equal(16, s.TickFrequency);
					}
					Assert.Equal(0, page.Sharpening.Minimum);
					Assert.Equal(3, page.Sharpening.Maximum);
					Assert.Equal(1, page.Sharpening.TickFrequency);
					Assert.Equal(1, page.Sharpening.LargeChange);
					Assert.Equal("Double Click on Color to set White Point", page.Viewer.Text);
					Assert.Equal("Image Control", ((TextBlock)((Grid)page.ImageControlsButton.Content).Children[0]).Text);
					Assert.Equal(new[] { "Saturation", "Brightness", "Contrast", "Gamma", "Sharpening" }, new[] { "labelSaturation", "labelBrightness", "labelContrast", "labelGamma", "labelSharpening" }.Select(n => (string)page.FindControl<Label>(n).Content).ToArray());
					Assert.Equal("Reset", page.ResetButton.Content);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_Colors_tab_shows_the_current_page_of_the_book_scaled_to_the_viewer_and_the_buttons_move_it()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					f.Books[0].CurrentPage = 1;
					f.Model.Apply(); // shows the book again with its current page
					Dpi.Settle(f.Dialog);
					f.Select("tabColors");
					InfoColorsPage page = f.Dialog.ColorsPage;
					f.Until(() => page.Viewer.Picture != null);
					Assert.Equal(1, f.Model.ColorTab.ViewerPage);
					Assert.Equal("Page 2", page.PageLabel.Content);
					Assert.Equal(1, f.Images.Asked.Last().Page);
					Assert.InRange(f.Images.Asked.Last().Width, (int)Math.Ceiling(page.Viewer.ViewWidth), (int)Math.Ceiling(page.Viewer.Bounds.Width)); // read for the width the viewer had, with or without its scroll bar
					Assert.Equal(page.Viewer.ViewWidth, page.Viewer.PictureSize.Width);
					Click(page.NextButton);
					f.Until(() => f.Images.Asked.Last().Page == 2);
					Assert.Equal("Page 3", page.PageLabel.Content);
					Assert.False(page.NextButton.IsEnabled);
					Assert.False(page.LastButton.IsEnabled);
					Assert.True(page.FirstButton.IsEnabled);
					Click(page.FirstButton);
					f.Until(() => f.Images.Asked.Last().Page == 0);
					Assert.False(page.PreviousButton.IsEnabled);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_selected_page_in_the_Pages_tab_is_the_page_of_the_viewer_and_no_selection_goes_back_to_the_cover()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					PageListModel pages = f.OpenPages();
					Assert.Equal(0, f.Model.ColorTab.ViewerPage); // nothing selected yet: the book's own page
					pages.SetSelection(new[] { 2 });
					Assert.Equal(2, f.Model.ColorTab.ViewerPage);
					pages.SetSelection(new[] { 1, 2 });
					Assert.Equal(1, f.Model.ColorTab.ViewerPage); // the first selected
					pages.SetSelection(Array.Empty<int>());
					Assert.Equal(0, f.Books[0].FrontCoverPageIndex);
					Assert.Equal(0, f.Model.ColorTab.ViewerPage); // the front cover of the book
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_sliders_set_the_adjustment_the_picture_follows_and_OK_writes_it_into_the_book()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					f.Select("tabColors");
					InfoColorsPage page = f.Dialog.ColorsPage;
					f.Until(() => page.Viewer.Picture != null);
					SkiaSharp.SKColor Pixel() { using (var pixmap = page.Viewer.Picture.Image.PeekPixels()) { return pixmap.GetPixelColor(20, 30); } }
					SkiaSharp.SKColor plain = Pixel();
					page.ImageControlsButton.IsChecked = true;
					Dpi.Settle(f.Dialog);
					page.Brightness.Value = 40;
					Assert.Equal(40, f.Model.ColorTab.Brightness);
					Assert.Equal("+40%", ToolTip.GetTip(page.Brightness));
					f.Until(() => Pixel() != plain);
					Assert.True(Pixel().Red > plain.Red);
					page.Saturation.Value = -30;
					page.Gamma.Value = 10;
					page.Contrast.Value = 5;
					page.Sharpening.Value = 2;
					Assert.Equal(new[] { -30, 40, 5, 10, 2 }, new[] { f.Model.ColorTab.Saturation, f.Model.ColorTab.Brightness, f.Model.ColorTab.Contrast, f.Model.ColorTab.Gamma, f.Model.ColorTab.Sharpening });
					Assert.True(f.Books[0].ColorAdjustment.IsEmpty); // not before OK
					f.Dialog.Accept();
					Dpi.Settle(f.Dialog);
					BitmapAdjustment written = f.Books[0].ColorAdjustment;
					Assert.Equal(-0.3f, written.Saturation);
					Assert.Equal(0.4f, written.Brightness);
					Assert.Equal(0.05f, written.Contrast);
					Assert.Equal(0.1f, written.Gamma);
					Assert.Equal(2, written.Sharpen);
					page.ImageControlsButton.IsChecked = false;
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Reset_puts_the_sliders_back_and_Cancel_leaves_the_book_alone()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					f.Select("tabColors");
					InfoColorsPage page = f.Dialog.ColorsPage;
					page.Saturation.Value = 50;
					page.Sharpening.Value = 3;
					Click(page.ResetButton);
					Assert.Equal(0, page.Saturation.Value);
					Assert.Equal(0, page.Sharpening.Value);
					Assert.True(f.Model.ColorTab.Adjustment.IsEmpty);
					page.Contrast.Value = 25;
					f.Dialog.Cancel();
					Dpi.Settle(f.Dialog);
					Assert.True(f.Books[0].ColorAdjustment.IsEmpty);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_double_click_on_the_page_makes_the_colour_under_the_pointer_the_white_point()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					f.Select("tabColors");
					InfoColorsPage page = f.Dialog.ColorsPage;
					f.Until(() => page.Viewer.Picture != null && page.Preview != null && page.Preview.PageSize.Width > 0);
					Point p = Dpi.Origin(page.Viewer, f.Dialog) + new Point(0.5, 0.5); // the light corner pixel of the fake page
					f.Dialog.MouseDown(p, MouseButton.Left);
					f.Dialog.MouseUp(p, MouseButton.Left);
					f.Dialog.MouseDown(p, MouseButton.Left);
					f.Dialog.MouseUp(p, MouseButton.Left);
					Dpi.Settle(f.Dialog);
					Assert.Equal(System.Drawing.Color.FromArgb(255, 235, 225, 205), f.Model.ColorTab.WhitePoint);
					Assert.True(f.Model.ColorTab.HasAdjustment);
					f.Dialog.Accept();
					Dpi.Settle(f.Dialog);
					Assert.Equal(System.Drawing.Color.FromArgb(255, 235, 225, 205).ToArgb(), f.Books[0].ColorAdjustment.WhitePointColor.ToArgb());
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Another_book_shows_its_own_adjustment_and_page()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					f.Books[1].ColorAdjustment = new BitmapAdjustment(0.2f, 0f, 0f, 0f);
					f.Select("tabColors");
					InfoColorsPage page = f.Dialog.ColorsPage;
					page.Contrast.Value = 10;
					Click(f.Dialog.NextButton);
					Dpi.Settle(f.Dialog);
					Assert.Equal(20, page.Saturation.Value);
					Assert.Equal(0, page.Contrast.Value);
					Assert.Equal(0.1f, f.Books[0].ColorAdjustment.Contrast); // the first book got its changes when Next was pressed
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Moved_pages_that_change_the_cover_set_the_flag_the_grid_refreshes_by()
		{
			await Ui.Run(() =>
			{
				using (var f = new Fixture())
				{
					PageListModel pages = f.OpenPages();
					Assert.False(f.Model.ThumbnailsChanged);
					pages.SetSelection(new[] { 1 });
					pages.SetPageType(ComicPageType.Letters); // a page that is not the cover
					f.Model.Save();
					Assert.False(f.Model.ThumbnailsChanged);
					pages.SetSelection(new[] { 0 });
					pages.SetRotation(ImageRotation.Rotate90); // the cover is turned: another picture
					f.Model.Save();
					Assert.True(f.Model.ThumbnailsChanged);
					Assert.Contains(f.Books[0], f.Model.ThumbnailBooks);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_book_without_a_file_has_no_Pages_and_Colors_tabs()
		{
			await Ui.Run(() =>
			{
				var book = new ComicBook { Series = "Fileless" };
				var model = new BookEditorModel(book, new[] { book }, Allow(), new EditorValueLists(() => new[] { book }, Array.Empty<string>()));
				var dialog = new ComicBookDialog(new BookInfoRequest { Model = model, Preferences = Allow() });
				dialog.Show();
				Dpi.Settle(dialog);
				Assert.False(dialog.TabPages.Single(t => (string)t.Tag == "tabPages").IsVisible);
				Assert.False(dialog.TabPages.Single(t => (string)t.Tag == "tabColors").IsVisible);
				Assert.Null(model.EnsurePages());
				dialog.Close();
				return Task.CompletedTask;
			});
		}
	}
}
