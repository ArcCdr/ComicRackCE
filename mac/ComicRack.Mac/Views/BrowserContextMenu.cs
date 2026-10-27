using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Scripting;
using ComicRack.Mac.ViewModels;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The context menu of the book grid (BC.*, <c>ComicBrowserControl.contextMenuItems</c>): every item of the original in its order, with its texts through
	/// TR (section <c>ComicBrowserControl</c>, key = the item's name), its picture and its shortcut (Ctrl becomes Cmd, Alt Option, DD20). Rendering is the
	/// popup style of the chrome layer (C-MENU-3). An item whose feature a later task builds is there and disabled: the task enables it by setting
	/// <see cref="Enable"/> for the item's name and adding its click. The items are found by the original's name (<see cref="this[string]"/>).
	/// <see cref="Update"/> is <c>contextMenuItems_Opening</c>: enabled and visible states by the rules of the original, then the separators are fixed
	/// (<c>FixSeparators</c>: none at the ends or twice in a row).
	/// </summary>
	public sealed class BrowserContextMenu
	{
		private const string Section = "ComicBrowserControl";
		private readonly MainViewModel vm;
		private readonly BrowserViewModel browser;
		private readonly Dictionary<string, Control> byName = new Dictionary<string, Control>();
		private readonly List<Control> top = new List<Control>();
		private readonly List<MenuItem> ratingItems = new List<MenuItem>();
		private readonly HashSet<string> enabledByTask = new HashSet<string>();
		private readonly CrRatingStars stars;
		private MenuItem rateMenu;

		public BrowserContextMenu(MainViewModel vm)
		{
			this.vm = vm ?? throw new ArgumentNullException(nameof(vm));
			browser = vm.Browser;
			Flyout = new MenuFlyout();
			stars = new CrRatingStars { Width = 200, Height = 18, DrawText = true, DrawBorder = false, CenterRating = false };
			Build();
			foreach (Control item in top)
			{
				Flyout.Items.Add(item);
			}
			Flyout.Opening += (sender, e) =>
			{
				// the menu is the menu of books: a right click on the empty area shows none (the original shows the view's own menu there, P5.13a)
				if (browser.SelectedCount == 0 && e is System.ComponentModel.CancelEventArgs cancel)
				{
					cancel.Cancel = true;
					return;
				}
				Update();
			};
			Flyout.Closed += (sender, e) => ApplyStars();
			// miAutomation: the commands of the Books hooks (SC.01), listed when scripts are read and whenever they change
			FillAutomation();
			vm.Scripts.Changed += (sender, e) => FillAutomation();
		}

		// ComicBrowserControl: miAutomation.DropDownItems = one item per Books command (CreateToolItems), the item is visible when there is one
		private void FillAutomation()
		{
			MenuItem automation = MenuItemOf("miAutomation");
			automation.Items.Clear();
			foreach (ScriptCommandItem command in vm.Scripts.Books)
			{
				var item = new MenuItem { Header = command.Name, Command = vm.Scripts.CommandFor(command, cYo.Projects.ComicRack.Plugins.PluginEngine.ScriptTypeBooks) };
				ToolTip.SetTip(item, command.Description);
				item.InputGesture = command.Gesture;
				if (command.IconName != null)
				{
					item.Icon = new CrIcon { IconName = command.IconName };
				}
				automation.Items.Add(item);
			}
			automation.IsVisible = automation.Items.Count != 0;
		}

		/// <summary>The flyout to put on the grid (<c>ContextFlyout</c>).</summary>
		public MenuFlyout Flyout { get; }

		/// <summary>The top level entries in order: items and separators.</summary>
		public IReadOnlyList<Control> Entries => top;

		/// <summary>The rating editor in the My Rating menu.</summary>
		public CrRatingStars Stars => stars;

		/// <summary>The item (or separator) with the original's name, such as <c>miRead</c>, <c>miMarkRead</c>, <c>tsMarkAsSeparator</c>; null if there is none.</summary>
		public Control this[string name] => byName.TryGetValue(name, out Control c) ? c : null;

		/// <summary>The original's name of an item or separator of this menu, or null.</summary>
		public string NameOf(Control control) => byName.FirstOrDefault(pair => ReferenceEquals(pair.Value, control)).Key;

		/// <summary>The menu item with the original's name (a separator gives null).</summary>
		public MenuItem MenuItemOf(string name) => this[name] as MenuItem;

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

		// An item of a later task: there, in the original's place, with its picture and shortcut, and off.
		private MenuItem Later(string name, string english, string icon = null, Key key = Key.None, KeyModifiers modifiers = KeyModifiers.Meta)
		{
			MenuItem item = Make(name, english, icon, key, modifiers);
			item.IsEnabled = false;
			return item;
		}

		private void Add(Control item) => top.Add(item);

		private void Build()
		{
			const KeyModifiers OptionShift = KeyModifiers.Alt | KeyModifiers.Shift;
			Add(Make("miRead", "&Open", "Open", Key.O, click: () => browser.OpenSelected(OpenTabMode.Default)));
			Add(Make("miReadTab", "Open in new Tab", null, Key.O, KeyModifiers.Meta | KeyModifiers.Shift, () => browser.OpenSelected(OpenTabMode.NewTab)));
			Add(Later("miOpenWith", "Open With"));
			Add(Make("miProperties", "Info...", "GetInfo", Key.I, click: () => browser.Run(BookCommand.Info)));
			Add(Later("miShowWeb", "Web...", "WebBlog", Key.R));
			Add(Later("miEdit", "Edit", null, Key.F2, KeyModifiers.None));
			Add(Line("toolStripMenuItem5"));

			// My Rating: None, a line, 1 to 5 stars, a line, the stars, a line, Quick Rating and Review (contextRating after InsertRatingControl inserted a line and the editor in front of the last two items)
			rateMenu = Make("miRateMenu", "My &Rating");
			string[] names = { "None", "* (1 Star)", "** (2 Stars)", "*** (3 Stars)", "**** (4 Stars)", "***** (5 Stars)" };
			for (int rating = 0; rating <= 5; rating++)
			{
				int value = rating;
				if (rating == 1)
				{
					rateMenu.Items.Add(Line("toolStripMenuItem3"));
				}
				MenuItem item = Make("miRating" + rating, names[rating], null, (Key)((int)Key.D0 + rating), OptionShift, () => browser.Run(BookCommand.Rate, value));
				item.ToggleType = MenuItemToggleType.Radio;
				item.GroupName = "browser-rating";
				ratingItems.Add(item);
				rateMenu.Items.Add(item);
			}
			rateMenu.Items.Add(Line("ratingSeparator"));
			rateMenu.Items.Add(stars);
			byName["ratingControl"] = stars;
			rateMenu.Items.Add(Line("toolStripSeparator1"));
			rateMenu.Items.Add(Make("miQuickRating", "Quick Rating and Review...", null, Key.Q, OptionShift, () => browser.Run(BookCommand.QuickRating)));
			Add(rateMenu);

			var markAs = Make("miMarkAs", "&Mark as");
			markAs.Items.Add(Make("miMarkUnread", "&Unread", null, Key.U, OptionShift, () => browser.Run(BookCommand.MarkUnread)));
			markAs.Items.Add(Make("miMarkRead", "&Read", null, Key.R, OptionShift, () => browser.Run(BookCommand.MarkRead)));
			markAs.Items.Add(Line("toolStripMenuItem9"));
			markAs.Items.Add(Make("miMarkChecked", "Checked", null, Key.M, OptionShift, () => browser.Run(BookCommand.MarkChecked)));
			markAs.Items.Add(Make("miMarkUnchecked", "Unchecked", null, Key.V, OptionShift, () => browser.Run(BookCommand.MarkUnchecked)));
			Add(markAs);

			MenuItem editList = Later("miEditList", "Edit List");
			editList.Items.Add(Later("miEditListMoveToTop", "Move to Top", null, Key.T, KeyModifiers.Meta | KeyModifiers.Alt));
			editList.Items.Add(Later("miEditListMoveToBottom", "Move to Bottom", null, Key.B, KeyModifiers.Meta | KeyModifiers.Alt));
			editList.Items.Add(Line("toolStripMenuItem8"));
			editList.Items.Add(Later("miEditListApplyOrder", "Apply current Order"));
			Add(editList);
			Add(Later("miAddList", "Add to List"));
			Add(Line("tsMarkAsSeparator"));
			Add(Later("miAddLibrary", "&Add to Library"));
			Add(Later("miShowOnly", "&Browse Books"));
			Add(Later("miShowInList", "Show in List"));
			MenuItem export = Later("miExportComics", "Export Books");
			export.Items.Add(Later("miExportComicsAs", "Export Books...", "Save", Key.E, KeyModifiers.Meta | KeyModifiers.Shift));
			export.Items.Add(Later("miExportComicsWithPrevious", "Export with Previous", null, Key.E, KeyModifiers.Meta | KeyModifiers.Alt));
			Add(export);
			Add(Make("miAutomation", "Automation"));
			Add(Make("miUpdateComicFiles", "Update Book Files", "UpdateSmall", click: () => browser.Run(BookCommand.UpdateFiles)));
			// the platform name replaces Explorer (ledger BC.17, as for the tab menu)
			MenuItem reveal = Make("miRevealBrowser", "Reveal Book in &Explorer", "RevealExplorer", Key.G, click: () => browser.Run(BookCommand.Reveal));
			reveal.Header = UiText.Get("MacOnly", "RevealInFinder", "Reveal in Finder");
			Add(reveal);
			Add(Line("toolStripMenuItem7"));
			Add(Later("miCopyData", "&Copy Data", "EditCopy", Key.C));
			Add(Later("miPasteData", "&Paste Data...", "EditPaste", Key.V));
			Add(Later("miClearData", "Clear Data"));
			Add(Line("tsCopySeparator"));
			Add(Make("miSelectAll", "Select &All", null, Key.A, click: () => browser.SelectAll()));
			Add(Make("miInvertSelection", "&Invert Selection", click: () => browser.InvertSelection()));
			Add(Later("miRefreshInformation", "Refresh", "RefreshThumbnail"));
			Add(Line("sepListBackground"));
			Add(Later("miResetTopOfStack", "Reset Top of Stack"));
			Add(Later("miSetTopOfStack", "Set as Top of Stack"));
			Add(Later("miSetStackThumbnail", "Set custom Stack Thumbnail..."));
			Add(Later("miRemoveStackThumbnail", "Remove custom Stack Thumbnail"));
			Add(Later("miSetListBackground", "Set as List Background"));
			Add(Line("toolStripRemoveSeparator"));
			Add(Make("miRemove", "Re&move...", "EditDelete", Key.Delete, KeyModifiers.None, () => browser.RemoveSelected(ask: true)));
		}

		/// <summary>
		/// Declares that the task of an item built it: <see cref="Update"/> then leaves its enabled state to <paramref name="enabled"/> instead of the default off.
		/// </summary>
		public void Enable(string name, Func<bool> enabled)
		{
			MenuItem item = MenuItemOf(name);
			if (item == null)
			{
				throw new ArgumentException("There is no item " + name + " in the book context menu.", nameof(name));
			}
			enabledByTask.Add(name);
			item.IsEnabled = enabled();
			byEnabled[name] = enabled;
		}

		private readonly Dictionary<string, Func<bool>> byEnabled = new Dictionary<string, Func<bool>>();

		private static bool HasWeb(ComicBook book) => book != null && !string.IsNullOrEmpty(book.Web);

		/// <summary><c>contextMenuItems_Opening</c>: the state of every item for the selection now.</summary>
		public void Update()
		{
			bool any = browser.SelectedCount > 0;
			MenuItemOf("miRead").IsEnabled = browser.CanOpen;
			MenuItemOf("miReadTab").IsEnabled = browser.CanOpen;
			MenuItemOf("miProperties").IsEnabled = browser.CanRun(BookCommand.Info);
			rateMenu.IsEnabled = any;
			MenuItemOf("miMarkAs").IsEnabled = any;
			float rating = browser.SelectedRating;
			int rounded = rating < 0 ? -1 : (int)Math.Round(rating);
			for (int i = 0; i < ratingItems.Count; i++)
			{
				ratingItems[i].IsChecked = i == rounded;
			}
			stars.Rating = rating < 0 ? 0 : rating;
			stars.UserChanged = false;
			MenuItemOf("miRevealBrowser").IsEnabled = browser.CanRun(BookCommand.Reveal);
			// Update Book Files: shown when a selected book has changes that are not in its file, enabled when one has a file
			MenuItemOf("miUpdateComicFiles").IsEnabled = browser.CanRun(BookCommand.UpdateFiles);
			MenuItemOf("miUpdateComicFiles").IsVisible = browser.SelectedBooks.Any(book => book.ComicInfoIsDirty || book.ComicBookIsDirty);
			MenuItemOf("miRemove").IsEnabled = any;
			MenuItemOf("miAutomation").IsVisible = vm.Scripts.Books.Count != 0;
			MenuItemOf("miAutomation").IsEnabled = any;
			// the items of the original that show only for a book with a Web field, in the Details view, or on a stack
			MenuItemOf("miShowWeb").IsVisible = HasWeb(browser.SelectedItem?.Book);
			MenuItemOf("miEdit").IsVisible = false;
			foreach (string name in new[] { "miResetTopOfStack", "miSetTopOfStack", "miSetStackThumbnail", "miRemoveStackThumbnail" })
			{
				MenuItemOf(name).IsVisible = false;
			}
			foreach (KeyValuePair<string, Func<bool>> task in byEnabled)
			{
				MenuItemOf(task.Key).IsEnabled = task.Value();
			}
			FixSeparators();
		}

		// ToolStripEx.FixSeparators: a separator is hidden when no visible item comes before it (after the previous separator) or none after it.
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

		// The stars apply when the menu closes (InsertRatingControl: strip.Closed), only if the user touched them.
		/// <summary>The menu closed: the stars set the rating if the user used them (<c>InsertRatingControl</c>'s <c>strip.Closed</c>).</summary>
		public void ApplyStars()
		{
			if (stars.UserChanged && browser.SelectedCount > 0)
			{
				stars.UserChanged = false;
				browser.Run(BookCommand.Rate, (float)stars.Rating);
			}
		}
	}
}
