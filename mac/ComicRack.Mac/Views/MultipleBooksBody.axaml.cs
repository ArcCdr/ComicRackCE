using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;

namespace ComicRack.Mac.Views
{
	/// <summary>What the multi edit dialog keeps from one opening to the next (<c>FormUtility.RestorePanelStates</c>: which groups were collapsed and where the panel was scrolled).</summary>
	public static class MultiEditViewState
	{
		/// <summary>The collapsed groups by name (<c>grpMain</c> ...).</summary>
		public static Dictionary<string, bool> Collapsed { get; } = new Dictionary<string, bool>();

		/// <summary>The scroll position of the panel with the groups.</summary>
		public static double ScrollY { get; set; }

		/// <summary>Forgets everything (the state is kept for the run of the program only, as the original does).</summary>
		public static void Reset()
		{
			Collapsed.Clear();
			ScrollY = 0;
		}
	}

	/// <summary>
	/// The body of the multi edit dialog (<c>pageData</c> of <c>MultipleComicBooksDialog</c>): the groups Main, Artists / People Involved, Plot &amp; Notes, Catalog and Custom Fields, each a
	/// collapsible group over the boxes of the Designer with a check box in front of every box. A box shows what all the books have in common (nothing when they differ); a check box says
	/// that the box is written into every book (<see cref="MultiBookEditorModel"/>). Typing in a box checks it, a box that holds a list of entries and differs between the books has an
	/// indeterminate check box. A double click on the empty part of a group turns the groups into tabs and back (<c>RegisterPanelToTabToggle</c>, the <c>Multiple</c> flag of <c>TabLayouts</c>).
	/// </summary>
	public partial class MultipleBooksBody : UserControl
	{
		public const string Section = "MultipleComicBooksDialog";

		private static readonly string[] GroupNames = { "grpMain", "grpArtists", "grpPlotNotes", "grpCatalog", "grpCustom" };

		// the boxes that offer the library's values (DefaultLists calls of Init) and the picture package of the combo boxes
		private static readonly Dictionary<string, EditorList> ListOf = new Dictionary<string, EditorList>
		{
			["txSeries"] = EditorList.Series,
			["txTitle"] = EditorList.Title,
			["txAlternateSeries"] = EditorList.AlternateSeries,
			["txSeriesGroup"] = EditorList.SeriesGroup,
			["txStoryArc"] = EditorList.StoryArc,
			["txWriter"] = EditorList.Writer,
			["txPenciller"] = EditorList.Penciller,
			["txColorist"] = EditorList.Colorist,
			["txInker"] = EditorList.Inker,
			["txLetterer"] = EditorList.Letterer,
			["txCoverArtist"] = EditorList.CoverArtist,
			["txEditor"] = EditorList.Editor,
			["txTranslator"] = EditorList.Translator,
			["cbPublisher"] = EditorList.Publisher,
			["cbImprint"] = EditorList.Imprint,
			["txGenre"] = EditorList.Genre,
			["cbFormat"] = EditorList.Format,
			["cbAgeRating"] = EditorList.AgeRating,
			["txTags"] = EditorList.Tags,
			["txCharacters"] = EditorList.Characters,
			["txTeams"] = EditorList.Teams,
			["txMainCharacterOrTeam"] = EditorList.MainCharacterOrTeam,
			["txLocations"] = EditorList.Locations,
			["txScanInformation"] = EditorList.ScanInformation,
			["cbBookStore"] = EditorList.BookStore,
			["cbBookOwner"] = EditorList.BookOwner,
			["cbBookLocation"] = EditorList.BookLocation,
			["cbBookPrice"] = EditorList.BookPrice,
			["cbBookAge"] = EditorList.BookAge,
			["cbBookCondition"] = EditorList.BookCondition,
			["txCollectionStatus"] = EditorList.CollectionStatus,
		};

		private static readonly Dictionary<string, IconSet> IconsOf = new Dictionary<string, IconSet>
		{
			["cbFormat"] = IconSet.Formats,
			["cbPublisher"] = IconSet.Publishers,
			["cbImprint"] = IconSet.Publishers,
			["cbAgeRating"] = IconSet.AgeRatings,
		};

		// EnableOnlyNumberKeys: digits, the decimal point and the comma (not the day and the number boxes)
		private static readonly HashSet<string> NumberKeyBoxes = new HashSet<string> { "txVolume", "txCount", "txYear", "txMonth", "txAlternateCount", "txPagesAsTextSimple" };

		private readonly Dictionary<string, Control> boxes = new Dictionary<string, Control>(StringComparer.Ordinal);
		private readonly Dictionary<string, CheckBox> checks = new Dictionary<string, CheckBox>(StringComparer.Ordinal);
		private readonly Dictionary<string, Control> labels = new Dictionary<string, Control>(StringComparer.Ordinal);
		private readonly Dictionary<string, List<Control>> companions = new Dictionary<string, List<Control>>(StringComparer.Ordinal);
		private readonly HashSet<string> filled = new HashSet<string>(StringComparer.Ordinal);
		private MultiBookEditorModel model;
		private AppPreferences preferences;
		private TabControl tabs;
		private bool applyingCheck;

		public MultipleBooksBody()
		{
			InitializeComponent();
			InfoPageText.Translate(this, Section);
			foreach (string name in GroupNames)
			{
				CrCollapsibleGroup group = Group(name);
				group.Header = UiText.Raw(Section, name, (string)group.Header);
				// the body of a group (the panel, which stays the same control in tab mode): a double click on its empty part toggles tab mode
				((Control)group.Content).DoubleTapped += OnPanelDoubleTapped;
			}
		}

		/// <summary>The groups in the order they are stacked.</summary>
		public IReadOnlyList<CrCollapsibleGroup> Groups => GroupNames.Select(Group).ToList();

		public CrCollapsibleGroup Group(string name) => this.FindControl<CrCollapsibleGroup>(name);

		/// <summary>The panel with the groups (<c>pageData</c>).</summary>
		public ScrollViewer PageData => pageData;

		/// <summary>The tab control that replaces the stacked groups in tab mode, or null.</summary>
		public TabControl Tabs => tabs;

		public bool IsTabMode => tabs != null;

		/// <summary>The box of a field (<c>txSeries</c>, <c>cbManga</c>, ...) or null.</summary>
		public Control Box(string name) => boxes.TryGetValue(name, out Control box) ? box : null;

		/// <summary>The check box in front of a field.</summary>
		public CheckBox Check(string name) => checks.TryGetValue(name, out CheckBox check) ? check : null;

		/// <summary>The label of a field, or null (the custom fields have their own).</summary>
		public Control Label(string name) => labels.TryGetValue(name, out Control label) ? label : null;

		/// <summary>The boxes whose values come from the library, by name.</summary>
		public IReadOnlyCollection<string> ListBoxNames => ListOf.Keys;

		/// <summary>Raised after the user turned the groups into tabs or back.</summary>
		public event EventHandler TabModeChanged;

		// ---- start

		/// <summary>Connects the body to the model: every box shows its value, every check box its state; <paramref name="settings"/> keeps the tab mode.</summary>
		public void Initialize(MultiBookEditorModel editor, AppPreferences settings = null)
		{
			model = editor;
			preferences = settings;
			foreach (MultiField field in model.Fields.Where(f => f.Kind != MultiKind.Custom))
			{
				Control box = this.FindControl<Control>(field.Name);
				CheckBox check = this.FindControl<CheckBox>("chk" + field.Name);
				if (box == null || check == null)
				{
					throw new InvalidOperationException("The dialog has no box " + field.Name + ".");
				}
				boxes[field.Name] = box;
				checks[field.Name] = check;
				labels[field.Name] = this.FindControl<Control>(LabelName(field.Name));
				var extras = new List<Control>();
				AddIfFound(extras, field.Name + "Selector");
				AddIfFound(extras, field.Name + "Spin");
				companions[field.Name] = extras;
			}
			grpCatalog.IsVisible = model.ShowCatalog;
			FillStaticCombos();
			foreach (MultiField field in model.Fields.Where(f => f.Kind != MultiKind.Custom))
			{
				Wire(field);
			}
			BuildCustomFields();
			Group("grpCustom").IsVisible = model.ShowCustom;
			RegisterSelectorsAndSpins();
			RestoreViewState();
			if (preferences != null && preferences.TabLayouts.HasFlag(TabLayouts.Multiple))
			{
				SetTabMode(true);
			}
		}

		private void AddIfFound(List<Control> list, string name)
		{
			Control control = this.FindControl<Control>(name);
			if (control != null)
			{
				list.Add(control);
			}
		}

		// Replace("tx", "label").Replace("cb", "label"): the label of txSeries is labelSeries, of dtpOpenedTime labelOpenedTime
		private static string LabelName(string box)
		{
			foreach (string prefix in new[] { "tx", "cb", "dtp" })
			{
				if (box.StartsWith(prefix, StringComparison.Ordinal))
				{
					return "label" + box.Substring(prefix.Length);
				}
			}
			return "label" + box;
		}

		private void FillStaticCombos()
		{
			Combo("cbSeriesComplete").SetValues(BookEditorModel.YesNoItems);
			Combo("cbBlackAndWhite").SetValues(BookEditorModel.YesNoItems);
			Combo("cbManga").SetValues(BookEditorModel.MangaItems);
			Combo("cbEnableProposed").SetValues(BookEditorModel.YesNoItems);
			Combo("cbLanguage").SetItems(model.Languages.Select(l => new ValueItem(l.Display, l.Name, l.SeparatorBefore)).ToList());
		}

		private CrValueComboBox Combo(string name) => this.FindControl<CrValueComboBox>(name);

		// ---- one field

		private void Wire(MultiField field)
		{
			Control box = boxes[field.Name];
			CheckBox check = checks[field.Name];
			switch (box)
			{
				case TextBox text:
					BindText(field, text.GetObservable(TextBox.TextProperty), () => text.Text, value => text.Text = value);
					break;
				case AutoCompleteBox completing:
					BindText(field, completing.GetObservable(AutoCompleteBox.TextProperty), () => completing.Text, value => completing.Text = value);
					break;
				case CrValueComboBox combo:
					BindText(field, combo.GetObservable(CrValueComboBox.ValueProperty), () => combo.Value, value => combo.Value = value);
					break;
				case CrRatingStars stars:
					BindRating(field, stars);
					break;
				case CalendarDatePicker picker:
					BindDate(field, picker);
					break;
			}
			if (ListOf.TryGetValue(field.Name, out EditorList list))
			{
				box.GotFocus += (sender, e) => EnsureList(field.Name);
				box.AddHandler(PointerPressedEvent, (sender, e) => EnsureList(field.Name), RoutingStrategies.Tunnel);
			}
			if (IconsOf.TryGetValue(field.Name, out IconSet icons) && box is CrValueComboBox iconBox)
			{
				iconBox.IconSet = icons;
			}
			if (NumberKeyBoxes.Contains(field.Name) && box is TextBox numbers)
			{
				numbers.AddHandler(TextInputEvent, (sender, e) =>
				{
					if (!string.IsNullOrEmpty(e.Text) && e.Text.Any(ch => "0123456789.,".IndexOf(ch) < 0))
					{
						e.Handled = true;
					}
				}, RoutingStrategies.Tunnel);
			}
			if (field.HasPrompt)
			{
				WirePrompt(field, box);
			}
			// the check box: the user's click goes to the field, the field's state to the box
			check.IsThreeState = false;
			ShowCheck(field, check);
			check.IsCheckedChanged += (sender, e) =>
			{
				if (!applyingCheck)
				{
					field.Check = check.IsChecked == null ? MultiCheck.Indeterminate : check.IsChecked == true ? MultiCheck.Checked : MultiCheck.Unchecked;
				}
			};
			Apply(field);
			field.PropertyChanged += (sender, e) =>
			{
				switch (e.PropertyName)
				{
					case nameof(MultiField.Check):
						ShowCheck(field, check);
						break;
					case nameof(MultiField.Enabled):
					case nameof(MultiField.Visible):
						Apply(field);
						break;
				}
			};
		}

		private void ShowCheck(MultiField field, CheckBox check)
		{
			applyingCheck = true;
			try
			{
				check.IsChecked = field.Check == MultiCheck.Indeterminate ? (bool?)null : field.Check == MultiCheck.Checked;
			}
			finally
			{
				applyingCheck = false;
			}
		}

		// Enabled and Visible of the field reach the box, its check box, its selector and spin buttons, and its label (the original hides the label of Opened/Read, Pages and Scan Information)
		private void Apply(MultiField field)
		{
			boxes[field.Name].IsEnabled = field.Enabled;
			checks[field.Name].IsEnabled = field.Enabled;
			boxes[field.Name].IsVisible = field.Visible;
			checks[field.Name].IsVisible = field.Visible;
			foreach (Control extra in companions[field.Name])
			{
				extra.IsEnabled = field.Enabled;
				extra.IsVisible = field.Visible;
			}
			if (labels[field.Name] != null && IsLabelHidden(field.Name))
			{
				labels[field.Name].IsVisible = field.Visible;
			}
		}

		private static bool IsLabelHidden(string name) => name == "dtpOpenedTime" || name == "txPagesAsTextSimple" || name == "txScanInformation";

		private void BindText(MultiField field, IObservable<string> changes, Func<string> get, Action<string> set)
		{
			set(field.Text);
			changes.Subscribe(new ActionObserver<string>(value =>
			{
				field.Text = value ?? string.Empty;
			}));
			field.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(MultiField.Text) && (get() ?? string.Empty) != field.Text)
				{
					set(field.Text);
				}
			};
		}

		private void BindRating(MultiField field, CrRatingStars stars)
		{
			stars.Rating = string.IsNullOrEmpty(field.Text) ? 0 : MultiBookEditorModel.RatingOf(field.Text);
			stars.RatingChanged += (sender, e) =>
			{
				stars.UserChanged = false;
				field.Text = MultiBookEditorModel.RatingText(stars.Rating);
			};
			field.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(MultiField.Text))
				{
					stars.Rating = string.IsNullOrEmpty(field.Text) ? 0 : MultiBookEditorModel.RatingOf(field.Text);
				}
			};
		}

		// NullableDateTimePicker: a date that is not set shows nothing, Delete clears it, and choosing a day keeps the time of day the box had
		private static void BindDate(MultiField field, CalendarDatePicker picker)
		{
			bool syncing = false;
			void Show()
			{
				DateTime? target = field.Date == DateTime.MinValue ? (DateTime?)null : field.Date;
				if (picker.SelectedDate?.Date == target?.Date)
				{
					return;
				}
				syncing = true;
				try
				{
					picker.SelectedDate = target;
				}
				finally
				{
					syncing = false;
				}
			}
			picker.SelectedDateChanged += (sender, e) =>
			{
				if (syncing || picker.SelectedDate?.Date == (field.Date == DateTime.MinValue ? (DateTime?)null : field.Date.Date))
				{
					return;
				}
				field.Date = InfoCatalogPage.Pick(picker.SelectedDate, field.Date);
			};
			picker.KeyDown += (sender, e) =>
			{
				if (e.Key == Key.Delete)
				{
					picker.SelectedDate = null;
					e.Handled = true;
				}
			};
			field.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(MultiField.Date))
				{
					Show();
				}
			};
			Show();
		}

		// TextBoxEx: an empty box with a prompt takes the prompt as its text when it gets the focus (and so gets checked); the prompt is shown gray while the box is empty
		private void WirePrompt(MultiField field, Control box)
		{
			switch (box)
			{
				case TextBox text:
					text.PlaceholderText = field.PromptText;
					break;
				case AutoCompleteBox completing:
					completing.PlaceholderText = field.PromptText;
					break;
				case CrValueComboBox combo:
					combo.PlaceholderText = field.PromptText;
					break;
			}
			box.GotFocus += (sender, e) =>
			{
				if (string.IsNullOrEmpty(field.PromptText) || !string.IsNullOrEmpty(field.Text))
				{
					return;
				}
				field.Text = field.PromptText;
				if (box is TextBox plain)
				{
					plain.SelectAll();
				}
			};
		}

		/// <summary>Builds the list of a box now (a box does it when it gets the focus).</summary>
		public void EnsureList(string name)
		{
			if (!ListOf.TryGetValue(name, out EditorList list) || !filled.Add(name))
			{
				return;
			}
			List<string> values = model.Values(list);
			Control box = boxes[name];
			if (box is AutoCompleteBox completing)
			{
				completing.ItemsSource = EditorValueLists.Completions(values);
			}
			else if (box is CrValueComboBox combo)
			{
				combo.SetValues(EditorValueLists.WithEmpty(values), combo.IconSet);
			}
		}

		// ---- the selector buttons and spin buttons

		private void RegisterSelectorsAndSpins()
		{
			foreach (MultiField field in model.Fields.Where(f => f.Kind != MultiKind.Custom))
			{
				MultiField current = field;
				Button selector = this.FindControl<Button>(field.Name + "Selector");
				if (selector != null && boxes[field.Name] is AutoCompleteBox box)
				{
					selector.Click += (sender, e) => OpenSelector(selector, box, ListOf[current.Name]);
				}
				CrSpinButton spin = this.FindControl<CrSpinButton>(field.Name + "Spin");
				if (spin != null && boxes[field.Name] is TextBox number)
				{
					SpinRules.Config config = MultiBookEditorModel.SpinFor(field.Name, DateTime.Now);
					spin.ButtonUp += (sender, e) => Step(number, current, config, up: true);
					spin.ButtonDown += (sender, e) => Step(number, current, config, up: false);
				}
			}
		}

		private static void Step(TextBox box, MultiField field, SpinRules.Config config, bool up)
		{
			box.Text = SpinRules.Step(box.Text, field.PromptText, config, up);
			box.SelectAll();
		}

		/// <summary>Opens the list selector of a box (<c>ListSelectorControl.ShowPopup</c>); the box takes the result when the popup closes.</summary>
		public Flyout OpenSelector(Control anchor, AutoCompleteBox box, EditorList list)
		{
			string text = box.Text;
			if (string.IsNullOrEmpty(text) && boxes.FirstOrDefault(p => p.Value == box).Key is string name && model.TryGet(name, out MultiField field))
			{
				text = field.PromptText;
			}
			return ListSelectorFlyout.Open(anchor, text, model.Values(list), result => box.Text = result);
		}

		// ---- the custom fields (AddCustomFields)

		/// <summary>The boxes of the custom fields in the order of the keys.</summary>
		public IReadOnlyList<AutoCompleteBox> CustomBoxes => customBoxes;

		private readonly List<AutoCompleteBox> customBoxes = new List<AutoCompleteBox>();

		private void BuildCustomFields()
		{
			double lastBottom = 0;
			double labelTop = 8;
			double boxTop = 23;
			foreach (MultiCustomField custom in model.CustomFields)
			{
				MultiField field = custom.Field;
				if (customBoxes.Count > 0)
				{
					labelTop = lastBottom + 8;
					boxTop = labelTop + 12 + 4;
				}
				var label = new Label { Content = custom.Label, Padding = new Thickness(0), Classes = { "cr-caption" } };
				Canvas.SetLeft(label, 10);
				Canvas.SetTop(label, labelTop);
				var check = new CheckBox { Width = 16, Height = 17, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Focusable = false, IsThreeState = false };
				Canvas.SetLeft(check, 11);
				Canvas.SetTop(check, boxTop + (20 - 17) / 2);
				var box = new AutoCompleteBox { Width = 451 - 18, Height = 20, MinWidth = 0, Tag = custom.Key };
				Canvas.SetLeft(box, 11 + 18);
				Canvas.SetTop(box, boxTop);
				customFieldsCanvas.Children.Add(label);
				customFieldsCanvas.Children.Add(check);
				customFieldsCanvas.Children.Add(box);
				customBoxes.Add(box);
				boxes[field.Name] = box;
				checks[field.Name] = check;
				labels[field.Name] = label;
				companions[field.Name] = new List<Control>();
				string key = custom.Key;
				bool listed = false;
				box.GotFocus += (sender, e) =>
				{
					if (!listed)
					{
						listed = true;
						box.ItemsSource = EditorValueLists.Completions(model.CustomValueSuggestions(key));
					}
				};
				Wire(field);
				lastBottom = boxTop + 20;
			}
			if (customBoxes.Count > 0)
			{
				// grpCustom.Height = textBox.Bottom + 32: the panel under the 24 high header is 8 taller than the last box's bottom
				customGroupsPanel.Height = lastBottom + 8;
			}
		}

		// ---- the groups as tabs (FormUtility.PanelToTab)

		/// <summary>Turns the groups into the pages of a tab control and back; true if it is tab mode afterwards.</summary>
		public bool SetTabMode(bool on)
		{
			if (on == IsTabMode)
			{
				return on;
			}
			if (on)
			{
				tabs = new TabControl { Padding = new Thickness(3, 0, 3, 3), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
				foreach (CrCollapsibleGroup group in Groups.Where(g => g.IsVisible))
				{
					var panel = (Control)group.Content;
					group.Content = null;
					var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
					var page = new TabItem { Header = group.Header, Content = scroll, Tag = group };
					tabs.Items.Add(page);
				}
				pageDataFrame.IsVisible = false;
				host.Children.Add(tabs);
			}
			else
			{
				foreach (TabItem page in tabs.Items.OfType<TabItem>().ToList())
				{
					var group = (CrCollapsibleGroup)page.Tag;
					var scroll = (ScrollViewer)page.Content;
					var panel = (Control)scroll.Content;
					scroll.Content = null;
					group.Content = panel;
				}
				host.Children.Remove(tabs);
				tabs = null;
				pageDataFrame.IsVisible = true;
			}
			return on;
		}

		private void OnPanelDoubleTapped(object sender, TappedEventArgs e)
		{
			// only the empty part of a group toggles: a double click on a box or a label is theirs
			if (e.Source != sender && e.Source != customFieldsCanvas)
			{
				return;
			}
			ToggleTabMode();
			e.Handled = true;
		}

		/// <summary>The user's double click on the body of a group: tab mode on or off, kept in the settings.</summary>
		public bool ToggleTabMode()
		{
			bool on = SetTabMode(!IsTabMode);
			if (preferences != null)
			{
				preferences.TabLayouts = on ? preferences.TabLayouts | TabLayouts.Multiple : preferences.TabLayouts & ~TabLayouts.Multiple;
			}
			TabModeChanged?.Invoke(this, EventArgs.Empty);
			return on;
		}

		// ---- RestorePanelStates

		private void RestoreViewState()
		{
			foreach (CrCollapsibleGroup group in Groups)
			{
				if (MultiEditViewState.Collapsed.TryGetValue(group.Name, out bool collapsed))
				{
					group.Collapsed = collapsed;
				}
			}
			double offset = MultiEditViewState.ScrollY;
			if (offset > 0)
			{
				// the extent is known after the first layout
				void Scroll(object sender, EventArgs e)
				{
					pageData.LayoutUpdated -= Scroll;
					pageData.Offset = new Vector(0, Math.Min(offset, Math.Max(0, pageData.Extent.Height - pageData.Viewport.Height)));
				}
				pageData.LayoutUpdated += Scroll;
			}
		}

		/// <summary>Remembers the collapsed groups and the scroll position for the next dialog (<c>FormClosing</c>).</summary>
		public void SaveViewState()
		{
			foreach (CrCollapsibleGroup group in Groups)
			{
				MultiEditViewState.Collapsed[group.Name] = group.Collapsed;
			}
			if (!IsTabMode)
			{
				MultiEditViewState.ScrollY = pageData.Offset.Y;
			}
		}

		private sealed class ActionObserver<T> : IObserver<T>
		{
			private readonly Action<T> onNext;

			public ActionObserver(Action<T> onNext) => this.onNext = onNext;

			public void OnNext(T value) => onNext(value);

			public void OnError(Exception error)
			{
			}

			public void OnCompleted()
			{
			}
		}
	}
}
