using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Editor;
using cYo.Common.Drawing;
using cYo.Common.Windows;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The context menu of the pages view (<c>PagesView.contextPages</c>, PV.02 to PV.05): every item of the original in its order with its text from the <c>PagesView</c> section, its picture
	/// and its shortcut (Ctrl becomes Cmd, Alt Option, DD20). The enum menus are built as <c>EnumMenuUtility</c> builds them: one item for each browsable value with the value's translated
	/// name, the shortcut starting at Alt+Shift+A for the types and Alt+Shift+6 for the rotations. <see cref="Update"/> is <c>contextPages_Opening</c>: what is visible, enabled and
	/// checked for the selection now. Items are found by the original's names. The shortcuts are also the keys of the page that shows the list (<see cref="TryRun"/>), because a
	/// native menu item takes its key only while the menu is open.
	/// </summary>
	public sealed class PagesContextMenu
	{
		private const string Section = "PagesView";
		private readonly Func<PageListModel> current;
		private readonly Func<string, string, Task<string>> askName;
		private readonly Dictionary<string, Control> byName = new Dictionary<string, Control>();
		private readonly List<Control> top = new List<Control>();
		private readonly List<(Key Key, KeyModifiers Modifiers, string Name)> shortcuts = new List<(Key, KeyModifiers, string)>();
		private readonly List<MenuItem> typeItems = new List<MenuItem>();
		private readonly List<MenuItem> rotationItems = new List<MenuItem>();
		private readonly List<MenuItem> positionItems = new List<MenuItem>();

		/// <param name="current">The list model the menu works on now (null: none, everything is off).</param>
		/// <param name="askName">Asks for a name (the bookmark): the caption and the proposed text; null after Cancel.</param>
		public PagesContextMenu(Func<PageListModel> current, Func<string, string, Task<string>> askName)
		{
			this.current = current ?? throw new ArgumentNullException(nameof(current));
			this.askName = askName;
			Flyout = new MenuFlyout();
			Build();
			foreach (Control item in top)
			{
				Flyout.Items.Add(item);
			}
			Flyout.Opening += (sender, e) => Update();
		}

		public MenuFlyout Flyout { get; }

		/// <summary>The top level entries in the original's order.</summary>
		public IReadOnlyList<Control> Entries => top;

		/// <summary>The item (or separator) with the original's name, or null.</summary>
		public Control this[string name] => byName.TryGetValue(name, out Control c) ? c : null;

		public MenuItem MenuItemOf(string name) => this[name] as MenuItem;

		/// <summary>The page type items in the order of the enum, the rotation items, the position items.</summary>
		public IReadOnlyList<MenuItem> TypeItems => typeItems;

		public IReadOnlyList<MenuItem> RotationItems => rotationItems;

		public IReadOnlyList<MenuItem> PositionItems => positionItems;

		private static string Text(string key, string english) => UiText.Get(Section, key, english);

		private MenuItem Make(string name, string english, string icon = null, Key key = Key.None, KeyModifiers modifiers = KeyModifiers.Meta, Action click = null)
		{
			var item = new MenuItem { Header = Text(name, english) };
			if (!string.IsNullOrEmpty(icon))
			{
				item.Icon = new CrIcon { IconName = icon };
			}
			if (key != Key.None)
			{
				item.InputGesture = new KeyGesture(key, modifiers);
				shortcuts.Add((key, modifiers, name));
			}
			if (click != null)
			{
				item.Click += (sender, e) => click();
			}
			byName[name] = item;
			return item;
		}

		private Separator Line(string name)
		{
			var separator = new Separator();
			byName[name] = separator;
			return separator;
		}

		private PageListModel Model => current();

		private void Build()
		{
			const KeyModifiers OptionShift = KeyModifiers.Alt | KeyModifiers.Shift;
			// miPageType: the browsable values of ComicPageType (not the two "All" masks) as EnumMenuUtility lists them, Alt+Shift+A, B, C ...
			MenuItem pageType = Make("miPageType", "Page Type");
			int index = 0;
			foreach (ComicPageType type in Enum.GetValues(typeof(ComicPageType)).Cast<ComicPageType>().Where(t => t != ComicPageType.All && t != ComicPageType.AllWithDeleted))
			{
				ComicPageType value = type;
				var item = new MenuItem { Header = LocalizeUtility.LocalizeEnum(typeof(ComicPageType), (int)value), Tag = value, ToggleType = MenuItemToggleType.CheckBox };
				item.InputGesture = new KeyGesture(Key.A + index, OptionShift);
				string name = "miPageType." + value;
				shortcuts.Add((Key.A + index, OptionShift, name));
				byName[name] = item;
				item.Click += (sender, e) => Model?.SetPageType(value);
				typeItems.Add(item);
				pageType.Items.Add(item);
				index++;
			}
			top.Add(pageType);

			// cmPageRotate: the four rotations with the pictures Rotate0Permanent ... Rotate270Permanent, Alt+Shift+6 to 9
			MenuItem rotate = Make("cmPageRotate", "Page Rotation");
			var icons = new Dictionary<ImageRotation, string>
			{
				{ ImageRotation.None, "Rotate0Permanent" },
				{ ImageRotation.Rotate90, "Rotate90Permanent" },
				{ ImageRotation.Rotate180, "Rotate180Permanent" },
				{ ImageRotation.Rotate270, "Rotate270Permanent" },
			};
			index = 0;
			foreach (ImageRotation rotation in Enum.GetValues(typeof(ImageRotation)).Cast<ImageRotation>())
			{
				ImageRotation value = rotation;
				var item = new MenuItem { Header = LocalizeUtility.LocalizeEnum(typeof(ImageRotation), (int)value), Tag = value, ToggleType = MenuItemToggleType.CheckBox, Icon = new CrIcon { IconName = icons[value] } };
				item.InputGesture = new KeyGesture(Key.D6 + index, OptionShift);
				string name = "cmPageRotate." + value;
				shortcuts.Add((Key.D6 + index, OptionShift, name));
				byName[name] = item;
				item.Click += (sender, e) => Model?.SetRotation(value);
				rotationItems.Add(item);
				rotate.Items.Add(item);
				index++;
			}
			top.Add(rotate);

			MenuItem position = Make("miPagePosition", "Page Position");
			foreach ((string name, string english, ComicPagePosition value) in new[]
			{
				("miPagePositionDefault", "Default", ComicPagePosition.Default),
				(null, null, ComicPagePosition.Default),
				("miPagePositionNear", "Near", ComicPagePosition.Near),
				("miPagePositionFar", "Far", ComicPagePosition.Far),
			})
			{
				if (name == null)
				{
					position.Items.Add(Line("toolStripMenuItem2"));
					continue;
				}
				ComicPagePosition target = value;
				MenuItem item = Make(name, english, click: () => Model?.SetPosition(target));
				item.ToggleType = MenuItemToggleType.CheckBox;
				item.Tag = value;
				positionItems.Add(item);
				position.Items.Add(item);
			}
			top.Add(position);
			top.Add(Line("tsPageTypeSeparator"));

			top.Add(Make("miSetBookmark", "Set Bookmark...", null, Key.B, KeyModifiers.Meta | KeyModifiers.Shift, () => _ = SetBookmarkAsync()));
			top.Add(Make("miRemoveBookmark", "Remove Bookmark", null, Key.D, KeyModifiers.Meta | KeyModifiers.Shift, () => Model?.RemoveBookmark()));
			top.Add(Line("tsBookmarkSeparator"));
			top.Add(Make("miMoveToTop", "&Move to Top", null, Key.T, KeyModifiers.Meta | KeyModifiers.Alt, () => Model?.MoveSelectedToTop()));
			top.Add(Make("miMoveToBottom", "Move to Bottom", null, Key.B, KeyModifiers.Meta | KeyModifiers.Alt, () => Model?.MoveSelectedToBottom()));
			top.Add(Make("miResetOriginalOrder", "Reset original Order", click: () => Model?.ResetOriginalOrder()));
			top.Add(Line("tsMovePagesSeparator"));
			top.Add(Make("miCopy", "&Copy Page", "Copy", Key.C));
			top.Add(Make("miMergePages", "Merge Pages", click: () => Model?.MergePages()));
			top.Add(Line("toolStripMenuItem1"));
			top.Add(Make("miSelectAll", "Select &All", null, Key.A, click: () => Model?.SelectAll()));
			top.Add(Make("miInvertSelection", "&Invert Selection", click: () => Model?.InvertSelection()));
			top.Add(Make("miRefreshThumbnail", "&Refresh", "RefreshThumbnail", click: () => Model?.RefreshSelected()));
			top.Add(Line("toolStripMenuItem3"));
			top.Add(Make("miMarkDeleted", "Mark as &Deleted", "EditDelete", Key.Delete, KeyModifiers.None, () => Model?.MarkAsDeleted()));
			shortcuts.Add((Key.Back, KeyModifiers.None, "miMarkDeleted")); // the Delete key of a Mac keyboard is the Backspace key: an alias of the item's Delete (a Mac addition, DD20)
		}

		/// <summary>Set Bookmark... (<c>SetBookmark</c>): asks for the name with the page's bookmark or "Page N" proposed; a name sets the bookmark.</summary>
		public async Task SetBookmarkAsync()
		{
			PageListModel model = Model;
			if (model == null || !model.CanBookmark || askName == null)
			{
				return;
			}
			string name = await askName(UiText.Default("Bookmark", "Bookmark"), model.BookmarkProposal);
			if (!string.IsNullOrEmpty(name))
			{
				model.SetBookmark(name);
			}
		}

		/// <summary>The command bound to a shortcut, if the key is one of the menu's and the command is enabled; returns true when it ran.</summary>
		public bool TryRun(Key key, KeyModifiers modifiers)
		{
			foreach ((Key k, KeyModifiers m, string name) in shortcuts)
			{
				if (k == key && m == modifiers)
				{
					MenuItem item = MenuItemOf(name);
					Update();
					if (item == null || !item.IsEnabled || !item.IsVisible)
					{
						return false;
					}
					item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
					return true;
				}
			}
			return false;
		}

		/// <summary><c>contextPages_Opening</c> and the enable functions of the <c>CommandMapper</c>: the state of every item for the selection now.</summary>
		public void Update()
		{
			PageListModel model = Model;
			bool any = model != null && model.Selection.Count > 0;
			bool edit = model != null && model.CanEditPages;
			// the editing items show only for a book whose pages can be edited
			foreach (string name in new[] { "miPageType", "tsPageTypeSeparator", "miSetBookmark", "miRemoveBookmark", "tsBookmarkSeparator", "miMoveToTop", "miMoveToBottom", "miResetOriginalOrder", "tsMovePagesSeparator" })
			{
				byName[name].IsVisible = edit;
			}
			MenuItemOf("miMergePages").IsVisible = model != null && model.CanMergePages;
			MenuItemOf("miCopy").IsEnabled = false;
			ComicPageType? type = model?.CommonPageType;
			foreach (MenuItem item in typeItems)
			{
				item.IsChecked = type.HasValue && (ComicPageType)item.Tag == type.Value;
			}
			ImageRotation? rotation = model?.CommonRotation;
			foreach (MenuItem item in rotationItems)
			{
				item.IsChecked = rotation.HasValue && (ImageRotation)item.Tag == rotation.Value;
			}
			ComicPagePosition? position = model?.CommonPosition;
			foreach (MenuItem item in positionItems)
			{
				item.IsChecked = position.HasValue && (ComicPagePosition)item.Tag == position.Value;
				item.IsEnabled = any;
			}
			MenuItemOf("miPageType").IsEnabled = MenuItemOf("cmPageRotate").IsEnabled = any;
			MenuItemOf("miPagePosition").IsEnabled = any;
			MenuItemOf("miSetBookmark").IsEnabled = model != null && model.CanBookmark;
			MenuItemOf("miRemoveBookmark").IsEnabled = model != null && model.CanRemoveBookmark;
			MenuItemOf("miMoveToTop").IsEnabled = MenuItemOf("miMoveToBottom").IsEnabled = MenuItemOf("miResetOriginalOrder").IsEnabled = any;
			MenuItemOf("miSelectAll").IsEnabled = MenuItemOf("miInvertSelection").IsEnabled = MenuItemOf("miRefreshThumbnail").IsEnabled = MenuItemOf("miMarkDeleted").IsEnabled = model != null;
			FixSeparators();
		}

		// ToolStripEx.FixSeparators
		private void FixSeparators()
		{
			bool itemBefore = false;
			foreach (Control entry in top)
			{
				if (entry is Separator separator)
				{
					separator.IsVisible = itemBefore;
					itemBefore = false;
				}
				else if (entry.IsVisible)
				{
					itemBefore = true;
				}
			}
			bool itemAfter = false;
			for (int i = top.Count - 1; i >= 0; i--)
			{
				if (top[i] is Separator separator)
				{
					if (!itemAfter)
					{
						separator.IsVisible = false;
					}
					else
					{
						itemAfter = false;
					}
				}
				else if (top[i].IsVisible)
				{
					itemAfter = true;
				}
			}
		}
	}
}
