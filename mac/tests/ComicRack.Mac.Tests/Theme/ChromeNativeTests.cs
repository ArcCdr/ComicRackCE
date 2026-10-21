using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Tests.Browser;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Themes;
using ComicRack.Mac.Views;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// RV5b: every element of <see cref="ChromeElement"/> flips from Original to Native through <see cref="ChromeVariants"/> and nothing else changes: the same view
	/// tree and controls, the other elements untouched, the element's pixels different, and the flip back gives the first frame again pixel for pixel. Light and
	/// dark, 1x and 2x. The chrome gallery holds the controls of eleven elements; the item view, the reader overlays and the dialog window have their own tests.
	/// </summary>
	public class ChromeNativeTests
	{
		/// <summary>Per element: the part of the gallery the flip must change, and a part of another element that it must not change.</summary>
		private static readonly Dictionary<ChromeElement, (string Area, string Bystander)> Gallery = new Dictionary<ChromeElement, (string, string)>
		{
			[ChromeElement.Toolbar] = ("ToolBarSample", "ComboSample"),
			[ChromeElement.TabBar] = ("TabBarSample", "ComboSample"),
			[ChromeElement.StatusBar] = ("StatusBarSample", "ComboSample"),
			[ChromeElement.Tree] = ("TreeFocused", "ComboSample"),
			[ChromeElement.ListHeader] = ("Headers", "ComboSample"),
			[ChromeElement.GroupBox] = ("GroupExpanded", "ComboSample"),
			[ChromeElement.SidePanel] = ("Containers", "ComboSample"),
			[ChromeElement.Icons] = ("TabBarSample", "ComboSample"),
			[ChromeElement.Buttons] = ("ButtonNormal", "ToolBarSample"),
			[ChromeElement.ComboBox] = ("ComboSample", "ButtonNormal"),
			[ChromeElement.Menus] = ("MenuSample", "ComboSample"),
		};

		public static IEnumerable<object[]> GalleryCases()
		{
			foreach (ChromeElement element in Gallery.Keys)
			{
				foreach (string appearance in new[] { "Light", "Dark" })
				{
					foreach (double scale in new[] { 1.0, 2.0 })
					{
						yield return new object[] { element, appearance, scale };
					}
				}
			}
		}

		private static readonly Type[] StockViewTypes =
		{
			typeof(Button), typeof(ToggleButton), typeof(CheckBox), typeof(RadioButton), typeof(ComboBox), typeof(TreeView), typeof(TreeViewItem), typeof(TextBox),
			typeof(NumericUpDown), typeof(Slider), typeof(TabControl), typeof(ListBox), typeof(MenuItem), typeof(ScrollViewer),
		};

		/// <summary>
		/// Type and name of the controls the view put into the window, in tree order: the named ones, the chrome controls and the stock controls. What a template
		/// makes of them (the text and presenters inside a stock button, check box or combo box) is not the view's and differs between the stock templates.
		/// </summary>
		private static List<string> Shape(Window window)
		{
			return window.GetLogicalDescendants()
				.Where(v => v is Control c && (!string.IsNullOrEmpty(c.Name) || c is IChromeElement || StockViewTypes.Any(t => t.IsInstanceOfType(c))))
				.Select(v => v.GetType().FullName + ":" + ((Control)v).Name)
				.ToList();
		}

		/// <summary>The Theme of every chrome control of an element, in tree order.</summary>
		private static List<ControlTheme> Themes(Window window, ChromeElement element)
		{
			// the chrome controls of the view, not the ones a stock template makes (the original's tree item draws its selection with one, the stock tree item has none)
			return window.GetVisualDescendants().OfType<IChromeElement>().Where(c => c.Element == element && ((Control)c).TemplatedParent == null).Select(c => ((Control)c).Theme).ToList();
		}

		private static RectInt Snap(Control part, Window window, double scale) => RectInt.Snapped(Dpi.Area(part, window), scale);

		[Theory]
		[MemberData(nameof(GalleryCases))]
		public async Task Flipping_an_element_changes_its_pixels_and_nothing_else_and_flipping_back_restores_the_frame(ChromeElement element, string appearance, double scale)
		{
			await ChromeTestSupport.InAppearance(appearance, () =>
			{
				try
				{
					var gallery = (ChromeGalleryWindow)ViewRegistry.Show("ChromeGallery", null);
					gallery.Height = 1200;
					if (scale != 1.0)
					{
						Dpi.Apply(gallery, scale);
					}
					Dpi.Settle(gallery);
					(string areaName, string bystanderName) = Gallery[element];
					Control area = gallery.FindControl<Control>(areaName);
					Control bystander = gallery.FindControl<Control>(bystanderName);
					Assert.NotNull(area);
					Assert.NotNull(bystander);
					var otherThemes = Enum.GetValues(typeof(ChromeElement)).Cast<ChromeElement>().Where(e => e != element).ToDictionary(e => e, e => Themes(gallery, e));
					List<ControlTheme> originalThemes = Themes(gallery, element);
					List<string> shape = Shape(gallery);
					string tag = element + "-" + appearance.ToLowerInvariant();
					// a tree whose items are TreeViewItem controls loses its selection when the stock templates are swapped (the controls are their own containers);
					// a tree bound to data keeps it (The_tree_bound_to_data_keeps_its_selection_through_the_flip), so the gallery's two are selected again after a flip
					var trees = new[] { gallery.FindControl<TreeView>("TreeFocused"), gallery.FindControl<TreeView>("TreeUnfocused") };
					object[] selected = trees.Select(t => t.SelectedItem).ToArray();
					void Reselect()
					{
						for (int i = 0; i < trees.Length; i++)
						{
							trees[i].SelectedItem = selected[i];
						}
						Dpi.Settle(gallery);
					}
					Raster before = Dpi.Capture(gallery, "native-" + tag + "-original");

					Assert.True(ChromeVariants.Set(element, ChromeVariant.Native));
					Dpi.Settle(gallery);
					if (element == ChromeElement.Tree)
					{
						Reselect();
					}
					Raster native = Dpi.Capture(gallery, "native-" + tag + "-native");

					// the same window, controls and tree
					Assert.Equal(shape, Shape(gallery));
					Assert.Same(area, gallery.FindControl<Control>(areaName));
					Assert.True(ChromeVariants.IsNative(element));
					// the controls of the element wear their Native theme; every other element wears the themes it wore
					List<ControlTheme> nativeThemes = Themes(gallery, element);
					Assert.Equal(originalThemes.Count, nativeThemes.Count);
					foreach (IChromeElement chrome in gallery.GetVisualDescendants().OfType<IChromeElement>().Where(c => c.Element == element && ((Control)c).TemplatedParent == null))
					{
						Assert.Same(ChromeVariants.FindTheme(chrome.GetType(), ChromeVariant.Native), ((Control)chrome).Theme);
					}
					foreach (KeyValuePair<ChromeElement, List<ControlTheme>> other in otherThemes)
					{
						Assert.Equal(other.Value, Themes(gallery, other.Key));
					}
					// the pixels of the element differ, those of a bystander of another element do not
					RectInt zone = Snap(area, gallery, scale);
					Difference changed = before.Crop(zone).Compare(native.Crop(zone), 0);
					Assert.True(changed.Max > 8, element + " looks the same in Native: " + changed);
					RectInt away = Snap(bystander, gallery, scale);
					Difference untouched = before.Crop(away).Compare(native.Crop(away), 0);
					Assert.True(untouched.Max == 0, "the " + bystanderName + " of another element changed: " + untouched);

					Assert.True(ChromeVariants.Set(element, ChromeVariant.Original));
					Dpi.Settle(gallery);
					if (element == ChromeElement.Tree)
					{
						Reselect();
					}
					Raster again = Dpi.Capture(gallery, "native-" + tag + "-restored");
					Assert.Equal(0, before.Compare(again, 0).Max);
					Assert.Equal(originalThemes, Themes(gallery, element));
					gallery.Close();
				}
				finally
				{
					ChromeVariants.Reset();
				}
			});
		}

		private sealed class Node
		{
			public string Text { get; set; }

			public bool IsExpanded { get; set; }

			public Node[] Children { get; set; } = new Node[0];
		}

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task The_tree_bound_to_data_keeps_its_selection_through_the_flip_and_changes_its_rows(string appearance)
		{
			await ChromeTestSupport.InAppearance(appearance, () =>
			{
				try
				{
					var child = new Node { Text = "Recently Added" };
					var root = new Node { Text = "Library", IsExpanded = true, Children = new[] { child, new Node { Text = "Unread" } } };
					var tree = new CrTreeView { Width = 220, Height = 100, TextMember = "Text", ChildrenMember = "Children", ExpandedMember = "IsExpanded", ItemsSource = new[] { root } };
					var window = new Window { Width = 260, Height = 140, Content = tree };
					window.Show();
					Dpi.Settle(window);
					tree.SelectedItem = child;
					Dpi.Settle(window);
					List<string> shape = Shape(window);
					Raster before = Dpi.Capture(window, "datatree-original");
					Assert.True(ChromeVariants.Set(ChromeElement.Tree, ChromeVariant.Native));
					Dpi.Settle(window);
					Raster native = Dpi.Capture(window, "datatree-native");
					Assert.Same(child, tree.SelectedItem);
					// what the view put in is the same; the items and rows a tree item template makes are the template's (the stock one realizes them in another order and number)
					bool Own(string x) => !x.Contains("TreeViewItem") && !x.Contains("CrTreeRow");
					Assert.Equal(shape.Where(Own), Shape(window).Where(Own));
					Assert.Same(ChromeVariants.FindTheme(typeof(CrTreeView), ChromeVariant.Native), tree.Theme);
					Assert.True(before.Compare(native, 0).Max > 8, "the tree looks the same in Native");
					Assert.True(ChromeVariants.Set(ChromeElement.Tree, ChromeVariant.Original));
					Dpi.Settle(window);
					Assert.Same(child, tree.SelectedItem);
					Assert.Equal(0, before.Compare(Dpi.Capture(window, "datatree-restored"), 0).Max);
					window.Close();
				}
				finally
				{
					ChromeVariants.Reset();
				}
			});
		}

		[Fact]
		public async Task The_stock_packs_follow_the_variant_and_a_flip_moves_exactly_one_pack()
		{
			await Ui.Run(() =>
			{
				try
				{
					Application app = Application.Current;
					foreach (ChromeElement element in new[] { ChromeElement.Buttons, ChromeElement.ComboBox, ChromeElement.Menus, ChromeElement.Tree })
					{
						Assert.Equal(ChromeVariant.Original, StockPacks.InstalledVariant(element));
					}
					int count = app.Styles.Count;
					IStyle comboPack = StockPacks.Installed(ChromeElement.ComboBox);
					IStyle buttonsOriginal = StockPacks.Installed(ChromeElement.Buttons);
					Assert.True(ChromeVariants.Set(ChromeElement.Buttons, ChromeVariant.Native));
					Assert.Equal(ChromeVariant.Native, StockPacks.InstalledVariant(ChromeElement.Buttons));
					Assert.NotSame(buttonsOriginal, StockPacks.Installed(ChromeElement.Buttons));
					Assert.DoesNotContain(buttonsOriginal, app.Styles.Cast<IStyle>());
					Assert.Contains(StockPacks.Installed(ChromeElement.Buttons), app.Styles.Cast<IStyle>());
					Assert.Same(comboPack, StockPacks.Installed(ChromeElement.ComboBox)); // another element keeps its pack
					Assert.Equal(count, app.Styles.Count);
					// the packs keep their order whatever the order of the flips
					int a = app.Styles.IndexOf(StockPacks.Installed(ChromeElement.Buttons));
					int b = app.Styles.IndexOf(StockPacks.Installed(ChromeElement.ComboBox));
					int c = app.Styles.IndexOf(StockPacks.Installed(ChromeElement.Menus));
					int d = app.Styles.IndexOf(StockPacks.Installed(ChromeElement.Tree));
					Assert.True(a < b && b < c && c < d, "pack order " + a + "," + b + "," + c + "," + d);
					ChromeVariants.Set(ChromeElement.Buttons, ChromeVariant.Original);
					Assert.Equal(a, app.Styles.IndexOf(StockPacks.Installed(ChromeElement.Buttons)));
					// every pack file exists in both variants
					foreach (ChromeElement element in StockPacks.Elements)
					{
						foreach (ChromeVariant variant in new[] { ChromeVariant.Original, ChromeVariant.Native })
						{
							Assert.True(ChromeVariants.Set(element, variant));
							Assert.Equal(variant, StockPacks.InstalledVariant(element));
						}
						ChromeVariants.Set(element, ChromeVariant.Original);
					}
				}
				finally
				{
					ChromeVariants.Reset();
				}
				return Task.CompletedTask;
			});
		}

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task Stock_controls_change_their_theme_and_shape_with_the_flip_and_come_back(string appearance)
		{
			await ChromeTestSupport.InAppearance(appearance, () =>
			{
				try
				{
					var button = new Button { Content = "OK" };
					var check = new CheckBox { Content = "Check", IsChecked = true };
					var radio = new RadioButton { Content = "Radio", IsChecked = true };
					var combo = new ComboBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0, Width = 120 };
					var tree = new TreeView { Width = 120, Height = 60, ItemsSource = new[] { new TreeViewItem { Header = "Item", IsSelected = true } } };
					var panel = new StackPanel { Spacing = 6, Margin = new Thickness(10), Children = { button, check, radio, combo, tree } };
					var window = new Window { Width = 300, Height = 300, Content = panel };
					window.Show();
					Dpi.Settle(window);
					var shape = Shape(window);
					ControlTheme checkTheme = check.Theme;
					ControlTheme radioTheme = radio.Theme;
					Assert.Equal(new CornerRadius(0), button.CornerRadius);
					Assert.Equal(23, button.MinHeight);
					Assert.Equal(21, combo.MinHeight);
					Raster before = Dpi.Capture(window, "stock-" + appearance.ToLowerInvariant() + "-original");

					ChromeVariants.Configure("Buttons=Native,ComboBox=Native,Tree=Native");
					Dpi.Settle(window);
					Raster native = Dpi.Capture(window, "stock-" + appearance.ToLowerInvariant() + "-native");
					Assert.Equal(shape, Shape(window));
					Assert.Same(button, panel.Children[0]);
					Assert.Equal(new CornerRadius(5), button.CornerRadius);
					Assert.Equal(22, button.MinHeight);
					Assert.Equal(13, button.FontSize);
					Assert.Equal(new CornerRadius(5), combo.CornerRadius);
					Assert.NotSame(checkTheme, check.Theme); // the stock check box theme took over from the original's
					Assert.NotSame(radioTheme, radio.Theme);
					Assert.True(before.Compare(native, 0).Max > 8, "the stock controls look the same in Native");

					ChromeVariants.Configure("original");
					Dpi.Settle(window);
					Raster again = Dpi.Capture(window, "stock-" + appearance.ToLowerInvariant() + "-restored");
					Assert.Equal(new CornerRadius(0), button.CornerRadius);
					Assert.Equal(23, button.MinHeight);
					Assert.Same(checkTheme, check.Theme);
					Assert.Same(radioTheme, radio.Theme);
					Assert.Equal(0, before.Compare(again, 0).Max);
					window.Close();
				}
				finally
				{
					ChromeVariants.Reset();
				}
			});
		}

		[Theory]
		[InlineData("Light", 1.0)]
		[InlineData("Dark", 2.0)]
		public async Task The_item_view_draws_a_rounded_selection_and_a_hairline_frame_in_native(string appearance, double scale)
		{
			await ChromeTestSupport.InAppearance(appearance, () =>
			{
				try
				{
					using (var h = new ThumbnailLookTests.ItemHarness(scale))
					{
						h.Item.SetSelected(true);
						h.Layout();
						List<string> shape = Shape(h.Window);
						Raster before = h.Frame("item-original");
						Assert.True(ChromeVariants.Set(ChromeElement.ItemView, ChromeVariant.Native));
						h.Layout();
						Raster native = h.Frame("item-native");
						Assert.Equal(shape, Shape(h.Window));
						Assert.Same(ChromeVariants.FindTheme(typeof(CrThumbnailItem), ChromeVariant.Native), h.Item.Theme);
						Assert.True(before.Compare(native, 0).Max > 8, "the item looks the same in Native");
						// the cover outline (the first column of the picture) is the stock line colour: not the original's black any more
						Rect cover = h.Cover;
						int x = h.Px(cover.X), y = h.Px(cover.Y + cover.Height / 2);
						Assert.Equal(new SKColor(0, 0, 0), before[x, y]);
						Assert.NotEqual(new SKColor(0, 0, 0), native[x, y]);
						Assert.True(ChromeVariants.Set(ChromeElement.ItemView, ChromeVariant.Original));
						h.Layout();
						Assert.Equal(0, before.Compare(h.Frame("item-restored"), 0).Max);
					}
				}
				finally
				{
					ChromeVariants.Reset();
				}
			});
		}

		[Theory]
		[InlineData("Light", 1.0)]
		[InlineData("Dark", 2.0)]
		public async Task The_reader_overlays_draw_a_stock_panel_instead_of_the_glass_in_native(string appearance, double scale)
		{
			await ChromeTestSupport.InAppearance(appearance, () =>
			{
				try
				{
					var overlay = new CrTextOverlay { Text = "4 (of 12)", Opacity = 1 };
					Window window = ChromeTestSupport.ShowIn(overlay, 240, 100, scale);
					List<string> shape = Shape(window);
					Raster before = Dpi.Capture(window, "overlay-original");
					Assert.True(ChromeVariants.Set(ChromeElement.ReaderOverlay, ChromeVariant.Native));
					Dpi.Settle(window);
					Raster native = Dpi.Capture(window, "overlay-native");
					Assert.Equal(shape, Shape(window));
					Assert.Same(ChromeVariants.FindTheme(typeof(CrTextOverlay), ChromeVariant.Native), overlay.Theme);
					Assert.True(before.Compare(native, 0).Max > 8, "the overlay looks the same in Native");
					// the panel is the stock chrome surface, with the text in the stock text colour
					Assert.True(window.TryFindResource("SystemControlBackgroundChromeMediumLowBrush", window.ActualThemeVariant, out object surface));
					Avalonia.Media.Color expected = ((ISolidColorBrush)surface).Color;
					Rect area = Dpi.Area(overlay, window);
					SKColor inside = native[Scales.Device(area.X + 8, scale), Scales.Device(area.Y + area.Height - 8, scale)];
					Assert.True(Raster.Near(inside, new SKColor(expected.R, expected.G, expected.B), 3), "panel pixel " + inside + " against " + expected);
					Assert.True(ChromeVariants.Set(ChromeElement.ReaderOverlay, ChromeVariant.Original));
					Dpi.Settle(window);
					Assert.Equal(0, before.Compare(Dpi.Capture(window, "overlay-restored"), 0).Max);
					window.Close();
				}
				finally
				{
					ChromeVariants.Reset();
				}
			});
		}

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task The_dialog_window_takes_the_stock_surface_and_the_mac_button_order_in_native(string appearance)
		{
			await ChromeTestSupport.InAppearance(appearance, () =>
			{
				try
				{
					var dialog = new CrDialogWindow { Buttons = DialogButtons.OkCancelApply, Body = new TextBlock { Text = "Body", Margin = new Thickness(20) } };
					dialog.Show();
					Dpi.Settle(dialog);
					var row = dialog.OkButton.Parent as StackPanel;
					Assert.Equal(new Control[] { dialog.OkButton, dialog.CancelButton, dialog.ApplyButton }, row.Children.ToArray());
					Raster before = Dpi.Capture(dialog, "dialog-original");
					ISolidColorBrush originalBack = (ISolidColorBrush)dialog.Background;

					Assert.True(ChromeVariants.Set(ChromeElement.Dialogs, ChromeVariant.Native));
					Dpi.Settle(dialog);
					Assert.True(dialog.IsNative);
					Assert.Same(ChromeVariants.FindTheme(typeof(CrDialogWindow), ChromeVariant.Native), dialog.Theme);
					Assert.Equal(new Control[] { dialog.ApplyButton, dialog.CancelButton, dialog.OkButton }, row.Children.ToArray()); // the default button is the rightmost
					Assert.True(dialog.TryFindResource("SystemControlBackgroundChromeMediumLowBrush", dialog.ActualThemeVariant, out object surface));
					Assert.Equal(((ISolidColorBrush)surface).Color, ((ISolidColorBrush)dialog.Background).Color);
					Assert.NotEqual(originalBack.Color, ((ISolidColorBrush)dialog.Background).Color);
					Assert.True(before.Compare(Dpi.Capture(dialog, "dialog-native"), 0).Max > 8);

					Assert.True(ChromeVariants.Set(ChromeElement.Dialogs, ChromeVariant.Original));
					Dpi.Settle(dialog);
					Assert.Equal(new Control[] { dialog.OkButton, dialog.CancelButton, dialog.ApplyButton }, row.Children.ToArray());
					Assert.Equal(originalBack.Color, ((ISolidColorBrush)dialog.Background).Color);
					Assert.Equal(0, before.Compare(Dpi.Capture(dialog, "dialog-restored"), 0).Max);
					dialog.Close();
				}
				finally
				{
					ChromeVariants.Reset();
				}
			});
		}

		[Theory]
		[InlineData("Light")]
		[InlineData("Dark")]
		public async Task Every_resource_the_native_palette_names_exists_in_the_stock_theme_in_both_appearances(string appearance)
		{
			await ChromeTestSupport.InAppearance(appearance, () =>
			{
				ThemeVariant variant = ChromeTestSupport.Variant(appearance);
				foreach (string key in ChromeNativePalette.Keys)
				{
					Assert.True(ChromeNativePalette.StockBrush(variant, key) != null, key + " is not a brush of the stock theme (" + appearance + ")");
				}
				foreach (string token in ChromeNativePalette.Tokens)
				{
					Assert.True(ThemeTokens.TryGetColor(token, out _, ChromeTestSupport.Variant("Dark")), token + " is not a token");
					if (ThemeTokens.TryGetColor(token, out _, variant)) // BorderDefault exists in the dark theme only
					{
						Assert.NotNull(ChromeNativePalette.Brush(variant, token));
					}
				}
			});
		}

		[Fact]
		public async Task A_native_element_paints_with_the_stock_brush_and_flat_gradients_and_other_elements_keep_the_tokens()
		{
			await Ui.Run(() =>
			{
				try
				{
					var toolbar = new CrToolBar();
					var tabs = new CrTabBar();
					var window = new Window { Content = new StackPanel { Children = { toolbar, tabs } } };
					window.Show();
					Dpi.Settle(window);
					IBrush original = ChromePaint.Brush(toolbar, ThemeTokens.Names.ControlText);
					IBrush gradient = ChromePaint.VerticalGradient(toolbar, ThemeTokens.Names.ToolToolStripGradientBegin, ThemeTokens.Names.ToolToolStripGradientMiddle, ThemeTokens.Names.ToolToolStripGradientEnd);
					Assert.IsAssignableFrom<IGradientBrush>(gradient);
					ChromeVariants.Set(ChromeElement.Toolbar, ChromeVariant.Native);
					IBrush stockText = ChromePaint.StockBrush(toolbar, ChromeNativePalette.KeyOf(ChromeNativePalette.Role.Text));
					Assert.Equal(((ISolidColorBrush)stockText).Color, ((ISolidColorBrush)ChromePaint.Brush(toolbar, ThemeTokens.Names.ControlText)).Color);
					Assert.IsNotAssignableFrom<IGradientBrush>(ChromePaint.VerticalGradient(toolbar, ThemeTokens.Names.ToolToolStripGradientBegin, ThemeTokens.Names.ToolToolStripGradientMiddle, ThemeTokens.Names.ToolToolStripGradientEnd));
					// the frame tokens of the original are not drawn by the stock look
					Assert.Equal(Colors.Transparent, ((ISolidColorBrush)ChromePaint.Brush(toolbar, ThemeTokens.Names.ToolButtonSelectedBorder)).Color);
					// the tab bar is another element: still the token
					Assert.Same(original, ChromePaint.Brush(tabs, ThemeTokens.Names.ControlText));
					// a part of a chrome control follows its control (the overflow button belongs to the toolbar)
					Assert.True(ChromePaint.IsNative(toolbar.OverflowButtonControl));
					Assert.False(ChromePaint.IsNative(tabs));
					// a control that is in no chrome element is never Native
					Assert.False(ChromePaint.IsNative(new Border()));
					window.Close();
				}
				finally
				{
					ChromeVariants.Reset();
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Native_icons_are_grey_glyphs_and_original_icons_keep_their_colours()
		{
			await Ui.Run(() =>
			{
				try
				{
					var icon = new CrIcon { IconName = "StarYellow" };
					Window window = ChromeTestSupport.ShowIn(icon, 40, 40);
					Raster before = Dpi.Capture(window, "icon-original");
					Rect area = Dpi.Area(icon, window);
					RectInt zone = RectInt.Snapped(area, 1.0);
					Assert.True(Colourful(before.Crop(zone)), "the original icon has more than one hue");
					ChromeVariants.Set(ChromeElement.Icons, ChromeVariant.Native);
					Dpi.Settle(window);
					Raster native = Dpi.Capture(window, "icon-native");
					Assert.False(Colourful(native.Crop(zone)), "the Native icon has no colour");
					ChromeVariants.Set(ChromeElement.Icons, ChromeVariant.Original);
					Dpi.Settle(window);
					Assert.Equal(0, before.Compare(Dpi.Capture(window, "icon-restored"), 0).Max);
					window.Close();
				}
				finally
				{
					ChromeVariants.Reset();
				}
				return Task.CompletedTask;
			});

			// true when the pixels differ in hue (not only in brightness) from the panel's own grey
			static bool Colourful(Raster r)
			{
				int tinted = 0;
				for (int y = 0; y < r.Height; y++)
				{
					for (int x = 0; x < r.Width; x++)
					{
						SKColor c = r[x, y];
						if (Math.Max(c.Red, Math.Max(c.Green, c.Blue)) - Math.Min(c.Red, Math.Min(c.Green, c.Blue)) > 40)
						{
							tinted++;
						}
					}
				}
				return tinted > 3;
			}
		}
	}
}
