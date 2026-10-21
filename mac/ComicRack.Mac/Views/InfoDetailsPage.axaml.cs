using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Host.Library;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The Details tab of the Info dialog (<c>tabDetails</c>, IN.02): Series, Title, Volume, Number, Count, Year, Month, Day, Publisher, Imprint, Format, the alternate series, the credits,
	/// story arc, series group, age rating, manga, language, black and white, genre, tags, "Include in Updates" and "Proposed Values". The boxes offer the values the library uses
	/// (<see cref="EditorValueLists"/>), the number boxes accept digits only and have the little spin buttons, the boxes with a proposed value show it as a prompt and take it when
	/// they get the focus while empty (<c>TextBoxEx</c>).
	/// </summary>
	public partial class InfoDetailsPage : UserControl
	{
		private BookEditorModel model;
		private readonly Dictionary<Control, EditorList> lists = new Dictionary<Control, EditorList>();
		private readonly HashSet<Control> filled = new HashSet<Control>();
		private readonly Dictionary<Control, Func<string>> boxPrompts = new Dictionary<Control, Func<string>>();

		public InfoDetailsPage()
		{
			InitializeComponent();
			InfoPageText.Translate(this);
		}

		/// <summary>The boxes whose values come from the library (the text boxes and the editable combo boxes).</summary>
		public IReadOnlyDictionary<Control, EditorList> ListBoxes => lists;

		/// <summary>The text boxes of the number fields.</summary>
		public IEnumerable<TextBox> NumberBoxes => new[] { txVolume, txCount, txYear, txMonth, txDay, txAlternateCount };

		/// <summary>Connects the page to the model: the bindings, the lists, the spin buttons, the prompts.</summary>
		public void Initialize(BookEditorModel editor)
		{
			model = editor;
			DataContext = editor;
			FillStaticCombos();
			RegisterLists();
			RegisterNumbers();
			RegisterPrompts();
			RegisterSelectors();
			model.BookLoaded += (sender, e) => OnBookLoaded();
			model.PromptsChanged += (sender, e) => ApplyPrompts();
			OnBookLoaded();
			ApplyPrompts();
		}

		// the drop-down lists of the boxes that do not take the library's values
		private void FillStaticCombos()
		{
			cbSeriesComplete.SetValues(BookEditorModel.YesNoItems);
			cbBlackAndWhite.SetValues(BookEditorModel.YesNoItems);
			cbManga.SetValues(BookEditorModel.MangaItems);
			cbEnableProposed.SetValues(BookEditorModel.YesNoOnlyItems);
			cbEnableDynamicUpdate.SetValues(BookEditorModel.YesNoOnlyItems);
		}

		private void OnBookLoaded()
		{
			cbLanguage.SetItems(model.Languages.Select(l => new ValueItem(l.Display, l.Name, l.SeparatorBefore)).ToList());
			// the lists are built when a box gets the focus (the library may hold 15,000 books): a new book asks for them again
			filled.Clear();
		}

		private void RegisterLists()
		{
			Map(txSeries, EditorList.Series);
			Map(txTitle, EditorList.Title);
			Map(txAlternateSeries, EditorList.AlternateSeries);
			Map(txStoryArc, EditorList.StoryArc);
			Map(txSeriesGroup, EditorList.SeriesGroup);
			Map(txWriter, EditorList.Writer);
			Map(txPenciller, EditorList.Penciller);
			Map(txInker, EditorList.Inker);
			Map(txColorist, EditorList.Colorist);
			Map(txLetterer, EditorList.Letterer);
			Map(txCoverArtist, EditorList.CoverArtist);
			Map(txEditor, EditorList.Editor);
			Map(txTranslator, EditorList.Translator);
			Map(txGenre, EditorList.Genre);
			Map(txTags, EditorList.Tags);
			Map(cbFormat, EditorList.Format);
			Map(cbPublisher, EditorList.Publisher);
			Map(cbImprint, EditorList.Imprint);
			Map(cbAgeRating, EditorList.AgeRating);
			foreach (Control box in lists.Keys.ToList())
			{
				box.GotFocus += (sender, e) => EnsureList((Control)sender);
				// the drop-down of a combo box opens before its first focus event when it is clicked
				box.AddHandler(PointerPressedEvent, (sender, e) => EnsureList((Control)sender), RoutingStrategies.Tunnel);
			}
			cbFormat.IconSet = IconSet.Formats;
			cbPublisher.IconSet = IconSet.Publishers;
			cbImprint.IconSet = IconSet.Publishers;
			cbAgeRating.IconSet = IconSet.AgeRatings;
		}

		private void Map(Control box, EditorList list)
		{
			lists[box] = list;
		}

		/// <summary>Builds the list of a box now (a box does it when it gets the focus).</summary>
		public void EnsureList(Control box)
		{
			if (!lists.TryGetValue(box, out EditorList list) || !filled.Add(box))
			{
				return;
			}
			List<string> values = model.Values(list);
			if (box is AutoCompleteBox completing)
			{
				completing.ItemsSource = EditorValueLists.Completions(values);
			}
			else if (box is CrValueComboBox combo)
			{
				combo.SetValues(EditorValueLists.WithEmpty(values), combo.IconSet);
			}
		}

		// ListSelectorControl.Register: the boxes that hold a list have the button that opens the list selector
		private void RegisterSelectors()
		{
			Selector(txWriterSelector, txWriter, EditorList.Writer);
			Selector(txPencillerSelector, txPenciller, EditorList.Penciller);
			Selector(txInkerSelector, txInker, EditorList.Inker);
			Selector(txColoristSelector, txColorist, EditorList.Colorist);
			Selector(txEditorSelector, txEditor, EditorList.Editor);
			Selector(txTranslatorSelector, txTranslator, EditorList.Translator);
			Selector(txCoverArtistSelector, txCoverArtist, EditorList.CoverArtist);
			Selector(txLettererSelector, txLetterer, EditorList.Letterer);
			Selector(txGenreSelector, txGenre, EditorList.Genre);
			Selector(txTagsSelector, txTags, EditorList.Tags);
		}

		private void Selector(Button button, AutoCompleteBox box, EditorList list)
		{
			button.Click += (sender, e) => OpenSelector(button, box, list);
		}

		/// <summary>Opens the list selector of a box (<c>ListSelectorControl.ShowPopup</c>); the box takes the result when the popup closes.</summary>
		public Flyout OpenSelector(Control anchor, AutoCompleteBox box, EditorList list)
		{
			var selector = new ListSelectorModel(box.Text, model.Values(list));
			var view = new ListSelectorView();
			view.Attach(selector);
			var flyout = new Flyout { Content = view, Placement = PlacementMode.BottomEdgeAlignedRight };
			flyout.Closed += (sender, e) => box.Text = view.Result();
			flyout.ShowAt(anchor);
			return flyout;
		}

		private void RegisterNumbers()
		{
			foreach (TextBox box in NumberBoxes)
			{
				// EnableOnlyNumberKeys: digits, the decimal point and the comma
				box.AddHandler(TextInputEvent, (sender, e) =>
				{
					if (!string.IsNullOrEmpty(e.Text) && e.Text.Any(ch => "0123456789.,".IndexOf(ch) < 0))
					{
						e.Handled = true;
					}
				}, RoutingStrategies.Tunnel);
			}
			Spin(txVolume, txVolumeSpin, () => model.ProposedVolume);
			Spin(txNumber, txNumberSpin, () => model.ProposedNumber);
			Spin(txCount, txCountSpin, () => model.ProposedCount);
			Spin(txYear, txYearSpin, () => model.ProposedYear);
			Spin(txMonth, txMonthSpin, () => string.Empty);
			Spin(txDay, txDaySpin, () => string.Empty);
			Spin(txAlternateNumber, txAlternateNumberSpin, () => string.Empty);
			Spin(txAlternateCount, txAlternateCountSpin, () => string.Empty);
		}

		private void Spin(TextBox box, CrSpinButton spin, Func<string> prompt)
		{
			SpinRules.Config config = SpinRules.ForField(box.Name, DateTime.Now);
			spin.ButtonUp += (sender, e) => Step(box, prompt, config, up: true);
			spin.ButtonDown += (sender, e) => Step(box, prompt, config, up: false);
		}

		private static void Step(TextBox box, Func<string> prompt, SpinRules.Config config, bool up)
		{
			box.Text = SpinRules.Step(box.Text, prompt(), config, up);
			box.SelectAll();
		}

		private void RegisterPrompts()
		{
			Prompt(txSeries, () => model.ProposedSeries);
			Prompt(txTitle, () => model.ProposedTitle);
			Prompt(txNumber, () => model.ProposedNumber);
			Prompt(txCount, () => model.ProposedCount);
			Prompt(txYear, () => model.ProposedYear);
			Prompt(txVolume, () => model.ProposedVolume);
			Prompt(cbFormat, () => model.ProposedFormat);
		}

		private void Prompt(Control box, Func<string> prompt)
		{
			boxPrompts[box] = prompt;
			// TextBoxEx.DoEnter: an empty box with a prompt takes the prompt as its text when it gets the focus, and selects it
			box.GotFocus += (sender, e) =>
			{
				string text = prompt();
				if (string.IsNullOrEmpty(text))
				{
					return;
				}
				if (box is TextBox plain && string.IsNullOrEmpty(plain.Text))
				{
					plain.Text = text;
					plain.SelectAll();
				}
				else if (box is AutoCompleteBox completing && string.IsNullOrEmpty(completing.Text))
				{
					completing.Text = text;
				}
				else if (box is CrValueComboBox combo && string.IsNullOrEmpty(combo.Value))
				{
					combo.Value = text;
				}
			};
		}

		private void ApplyPrompts()
		{
			foreach (KeyValuePair<Control, Func<string>> pair in boxPrompts)
			{
				string text = pair.Value();
				switch (pair.Key)
				{
					case TextBox plain:
						plain.PlaceholderText = text;
						break;
					case AutoCompleteBox completing:
						completing.Watermark = text;
						break;
					case CrValueComboBox combo:
						combo.PlaceholderText = text;
						break;
				}
			}
		}
	}
}
