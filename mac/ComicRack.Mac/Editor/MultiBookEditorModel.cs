using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using cYo.Common.Collections;
using cYo.Common.Reflection;
using cYo.Common.Text;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Editor
{
	/// <summary>The state of the check box in front of a field of the multi edit dialog (<c>CheckState</c>): only a checked or indeterminate field is written.</summary>
	public enum MultiCheck
	{
		/// <summary>Not touched: the books keep what they have.</summary>
		Unchecked,

		/// <summary>The value in the box is written into every book.</summary>
		Checked,

		/// <summary>A list field whose books differ: the entries that were in every book are replaced by the ones in the box, the other entries of a book stay.</summary>
		Indeterminate,
	}

	/// <summary>How the text of a field turns into the value of a book property (<c>Store</c> of <c>MultipleComicBooksDialog</c>).</summary>
	public enum MultiKind
	{
		Text,
		Int,
		Float,
		Bool,
		YesNo,
		MangaYesNo,
		Date,

		/// <summary>A comma separated list (<c>listFields</c>).</summary>
		List,

		/// <summary>A custom value, written <c>{Key}</c>.</summary>
		Custom,
	}

	/// <summary>
	/// One field of the multi edit dialog: the check box in front of it, the text (or date) of its box and what the original does when one of them changes
	/// (<c>MakeCheckBox</c>, <c>SetText</c>, <c>SetGrayText</c>). A field is only written when its check box is checked or indeterminate.
	/// </summary>
	public sealed class MultiField : ObservableObject
	{
		private string text = string.Empty;
		private DateTime date = DateTime.MinValue;
		private MultiCheck check;
		private bool enabled = true;
		private bool visible = true;
		private string promptText = string.Empty;
		private bool clearsOnUncheck;
		private bool grayText;
		private bool armed;

		internal MultiField(string name, string property, MultiKind kind)
		{
			Name = name;
			Property = property;
			Kind = kind;
			if (kind != MultiKind.Custom)
			{
				PropertyInfo = typeof(ComicBook).GetProperty(property);
			}
		}

		/// <summary>The name of the box in the original's Designer (<c>txSeries</c>, <c>cbManga</c>, <c>dtpAddedTime</c>); a custom value is <c>custom:Key</c>.</summary>
		public string Name { get; }

		/// <summary>The <see cref="ComicBook"/> property, or <c>{Key}</c> for a custom value.</summary>
		public string Property { get; }

		public MultiKind Kind { get; }

		public PropertyInfo PropertyInfo { get; }

		/// <summary>The books' common entries of a list field that differs between the books (what the entries of the box replace).</summary>
		internal HashSet<string> OldList { get; set; }

		/// <summary>The text in the box. A change to a text with something in it checks the field (<c>c.TextChanged</c>).</summary>
		public string Text
		{
			get => text;
			set
			{
				if (SetProperty(ref text, value ?? string.Empty) && armed && text.Length > 0)
				{
					EnsureChecked();
				}
			}
		}

		/// <summary>The value of a date box; <see cref="DateTime.MinValue"/> shows nothing. A date checks the field.</summary>
		public DateTime Date
		{
			get => date;
			set
			{
				if (SetProperty(ref date, value) && armed && date != DateTime.MinValue)
				{
					EnsureChecked();
				}
			}
		}

		public MultiCheck Check
		{
			get => check;
			set
			{
				bool before = IsChecked;
				if (SetProperty(ref check, value))
				{
					OnPropertyChanged(nameof(IsChecked));
					if (armed && before != IsChecked)
					{
						CheckedChanged();
					}
				}
			}
		}

		/// <summary><c>CheckBox.Checked</c>: checked or indeterminate.</summary>
		public bool IsChecked => check != MultiCheck.Unchecked;

		/// <summary>The box and its check box can be used (the original disables both for a book that cannot take the value).</summary>
		public bool Enabled
		{
			get => enabled;
			set => SetProperty(ref enabled, value);
		}

		public bool Visible
		{
			get => visible;
			set => SetProperty(ref visible, value);
		}

		/// <summary>The value all the books propose, shown gray in the empty box (<c>IPromptText.PromptText</c>); empty when the books propose different values.</summary>
		public string PromptText
		{
			get => promptText;
			internal set => SetProperty(ref promptText, value ?? string.Empty);
		}

		/// <summary>True for the fields that have a prompt (Series, Number, Count, Year, Volume and Format).</summary>
		public bool HasPrompt => grayText;

		/// <summary>A click on the check box: <c>CheckBox.OnClick</c> without three states (unchecked becomes checked, anything else unchecked).</summary>
		public void Toggle() => Check = check == MultiCheck.Unchecked ? MultiCheck.Checked : MultiCheck.Unchecked;

		private void EnsureChecked()
		{
			if (check == MultiCheck.Unchecked)
			{
				Check = MultiCheck.Checked;
			}
		}

		// the handlers the original attaches to CheckedChanged: a list field that differs clears its box when it is unchecked (SetText), a field with a prompt fills the empty
		// box with it when it is checked and empties the box again when unchecked while it still shows the prompt (SetGrayText)
		private void CheckedChanged()
		{
			if (clearsOnUncheck && !IsChecked)
			{
				Text = string.Empty;
			}
			if (grayText)
			{
				if (IsChecked)
				{
					if (text.Length == 0)
					{
						Text = promptText;
					}
				}
				else if (text == promptText)
				{
					Text = string.Empty;
				}
			}
		}

		internal void ClearOnUncheck() => clearsOnUncheck = true;

		internal void EnablePrompt() => grayText = true;

		/// <summary>Called when the original has attached its handlers (end of <c>SetText</c> and of <c>Init</c>): from now on a change of the user checks the field.</summary>
		internal void Arm() => armed = true;
	}

	/// <summary>The data of a custom field of the dialog (<c>AddCustomFields</c>): the key and the label text.</summary>
	public sealed class MultiCustomField
	{
		public MultiCustomField(string key, MultiField field)
		{
			Key = key;
			Field = field;
		}

		public string Key { get; }

		/// <summary>The label: the key and a colon.</summary>
		public string Label => Key + ":";

		public MultiField Field { get; }
	}

	/// <summary>
	/// What the multi edit dialog shows and writes (<c>MultipleComicBooksDialog</c> without a window): for every field the value the books have in common (nothing when
	/// they differ), a check box that says the field is written, and <see cref="Store"/> (OK), which writes the checked fields into every book. The books are the library's own, so
	/// the Core's change events and the undo list (<c>Database.Undo</c>, marker "Edit multiple Books" set by the caller) see the writes as one step.
	/// </summary>
	public sealed class MultiBookEditorModel
	{
		private readonly ComicBook[] books;
		private readonly Dictionary<string, MultiField> byName = new Dictionary<string, MultiField>(StringComparer.Ordinal);
		private readonly List<MultiField> fields = new List<MultiField>();
		private readonly List<MultiCustomField> customFields = new List<MultiCustomField>();
		private readonly AppPreferences preferences;
		private readonly EditorValueLists lists;

		// the fields of Init in the order of its SetText calls: control name, property, kind
		private static readonly (string Box, string Property, MultiKind Kind)[] Specs =
		{
			("txSeries", "Series", MultiKind.Text),
			("txNumber", "Number", MultiKind.Text),
			("txCount", "Count", MultiKind.Int),
			("txYear", "Year", MultiKind.Int),
			("txVolume", "Volume", MultiKind.Int),
			("cbSeriesComplete", "SeriesComplete", MultiKind.YesNo),
			("txTitle", "Title", MultiKind.Text),
			("txAlternateSeries", "AlternateSeries", MultiKind.Text),
			("txAlternateNumber", "AlternateNumber", MultiKind.Text),
			("txAlternateCount", "AlternateCount", MultiKind.Int),
			("txSeriesGroup", "SeriesGroup", MultiKind.Text),
			("txStoryArc", "StoryArc", MultiKind.Text),
			("txMonth", "Month", MultiKind.Int),
			("txDay", "Day", MultiKind.Int),
			("txWriter", "Writer", MultiKind.List),
			("txPenciller", "Penciller", MultiKind.List),
			("txColorist", "Colorist", MultiKind.List),
			("txInker", "Inker", MultiKind.List),
			("txLetterer", "Letterer", MultiKind.List),
			("txCoverArtist", "CoverArtist", MultiKind.List),
			("txEditor", "Editor", MultiKind.List),
			("txTranslator", "Translator", MultiKind.List),
			("cbPublisher", "Publisher", MultiKind.Text),
			("cbImprint", "Imprint", MultiKind.Text),
			("txGenre", "Genre", MultiKind.List),
			("cbFormat", "Format", MultiKind.Text),
			("cbAgeRating", "AgeRating", MultiKind.Text),
			("txRating", "Rating", MultiKind.Float),
			("txCommunityRating", "CommunityRating", MultiKind.Float),
			("cbLanguage", "LanguageISO", MultiKind.Text),
			("cbManga", "Manga", MultiKind.MangaYesNo),
			("cbBlackAndWhite", "BlackAndWhite", MultiKind.YesNo),
			("cbEnableProposed", "EnableProposed", MultiKind.Bool),
			("txSummary", "Summary", MultiKind.Text),
			("txNotes", "Notes", MultiKind.Text),
			("txReview", "Review", MultiKind.Text),
			("txWeb", "Web", MultiKind.Text),
			("txTags", "Tags", MultiKind.List),
			("txCharacters", "Characters", MultiKind.List),
			("txTeams", "Teams", MultiKind.List),
			("txMainCharacterOrTeam", "MainCharacterOrTeam", MultiKind.List),
			("txLocations", "Locations", MultiKind.List),
			("txScanInformation", "ScanInformation", MultiKind.Text),
			("cbBookStore", "BookStore", MultiKind.Text),
			("cbBookOwner", "BookOwner", MultiKind.Text),
			("cbBookLocation", "BookLocation", MultiKind.Text),
			("cbBookPrice", "BookPriceAsText", MultiKind.Text),
			("cbBookAge", "BookAge", MultiKind.Text),
			("cbBookCondition", "BookCondition", MultiKind.Text),
			("txCollectionStatus", "BookCollectionStatus", MultiKind.List),
			("txISBN", "ISBN", MultiKind.Text),
			("txBookNotes", "BookNotes", MultiKind.Text),
			("txPagesAsTextSimple", "PagesAsTextSimple", MultiKind.Text),
			("dtpOpenedTime", "OpenedTime", MultiKind.Date),
			("dtpAddedTime", "AddedTime", MultiKind.Date),
			("dtpReleasedTime", "ReleasedTime", MultiKind.Date),
		};

		// SetGrayText: the property of the books that gives the prompt of a box
		private static readonly (string Box, string Property)[] Prompts =
		{
			("txSeries", "ProposedSeries"),
			("txNumber", "ProposedNumber"),
			("txCount", "ProposedCountAsText"),
			("txYear", "ProposedYearAsText"),
			("txVolume", "ProposedNakedVolumeAsText"),
			("cbFormat", "ProposedFormat"),
		};

		/// <param name="books">The books to edit; their information is read from their files before (the caller refreshes them).</param>
		/// <param name="preferences">The settings the dialog reads (<c>CatalogOnlyForFileless</c>, <c>ShowCustomBookFields</c>); null: the defaults.</param>
		/// <param name="lists">The values the boxes offer; null: none.</param>
		public MultiBookEditorModel(IEnumerable<ComicBook> books, AppPreferences preferences = null, EditorValueLists lists = null)
		{
			this.books = (books ?? throw new ArgumentNullException(nameof(books))).ToArray();
			if (this.books.Length == 0)
			{
				throw new ArgumentException("There are no books to edit.", nameof(books));
			}
			this.preferences = preferences ?? new AppPreferences();
			this.lists = lists;
			foreach ((string box, string property, MultiKind kind) in Specs)
			{
				var field = new MultiField(box, property, kind);
				fields.Add(field);
				byName[box] = field;
			}
			Initialize();
		}

		/// <summary>The books that are edited.</summary>
		public IReadOnlyList<ComicBook> Books => books;

		/// <summary>The title of the dialog: "Multiple Book Information (N)" with the count.</summary>
		public int Count => books.Length;

		/// <summary>Every field, the custom ones last.</summary>
		public IReadOnlyList<MultiField> Fields => fields;

		public MultiField this[string name] => byName[name];

		public bool TryGet(string name, out MultiField field) => byName.TryGetValue(name, out field);

		/// <summary>The custom fields of the Custom Fields group in the order the keys have there.</summary>
		public IReadOnlyList<MultiCustomField> CustomFields => customFields;

		/// <summary>True if some book is linked to a file (the books are shown the Scan Information box, the Catalog group may be hidden).</summary>
		public bool AnyLinked { get; private set; }

		/// <summary>The Catalog group is shown (<c>grpCatalog.Visible = !flag || !CatalogOnlyForFileless</c>).</summary>
		public bool ShowCatalog { get; private set; }

		/// <summary>The Custom Fields group is shown (<c>ShowCustomBookFields</c> and at least one custom key).</summary>
		public bool ShowCustom { get; private set; }

		/// <summary>The Scan Information box and its label (a book with a file).</summary>
		public bool ShowScanInformation => AnyLinked;

		/// <summary>Opened/Read and Pages are for books without a file only.</summary>
		public bool ShowFilelessCatalogFields { get; private set; }

		/// <summary>The My Rating, Proposed Values and Series complete boxes can be used only when every book is in the library.</summary>
		public bool AllInContainer { get; private set; }

		/// <summary>The entries every language box offers (<c>LanguageComboBox.TopISOLanguages</c> are the languages the library uses).</summary>
		public IReadOnlyList<LanguageItem> Languages { get; private set; }

		/// <summary>The values a box offers (the drop-down of a combo box, the completions of a text box); <c>Title</c> is the raw title here, as the original asks for it.</summary>
		public List<string> Values(EditorList list) => lists == null ? new List<string>() : EditorListValues.Get(lists, list, rawTitle: true);

		/// <summary>The completions of a custom value box: every value the library holds under the key.</summary>
		public List<string> CustomValueSuggestions(string key) => lists == null || string.IsNullOrEmpty(key) ? new List<string>() : lists.GetCustomValues(key);

		/// <summary>The common prompt of a field (the books agree on what they propose), or empty.</summary>
		public string GetSameValue(string property)
		{
			string value = null;
			foreach (ComicBook book in books)
			{
				string current = book.GetStringPropertyValue(property);
				if (value == null)
				{
					value = current;
				}
				else if (value != current)
				{
					return string.Empty;
				}
			}
			return value;
		}

		// ---- Init

		private void Initialize()
		{
			AnyLinked = books.Any(b => b.IsLinked);
			AllInContainer = books.All(b => b.IsInContainer);
			ShowFilelessCatalogFields = !books.Any(b => b.IsLinked);
			byName["txPagesAsTextSimple"].Enabled = byName["txPagesAsTextSimple"].Visible = ShowFilelessCatalogFields;
			byName["dtpOpenedTime"].Enabled = byName["dtpOpenedTime"].Visible = ShowFilelessCatalogFields;
			byName["txRating"].Enabled = byName["cbEnableProposed"].Enabled = byName["cbSeriesComplete"].Enabled = AllInContainer;
			byName["txScanInformation"].Visible = AnyLinked;
			ShowCatalog = !AnyLinked || !preferences.CatalogOnlyForFileless;
			Languages = BookEditorModel.BuildLanguages(lists?.GetComicFieldList(b => b.LanguageISO).Distinct() ?? Enumerable.Empty<string>());
			string[] keys = (lists?.GetCustomValueKeys() ?? Enumerable.Empty<string>()).Where(k => !k.Contains('.')).OrderBy(k => k).ToArray();
			ShowCustom = preferences.ShowCustomBookFields && keys.Length > 0;
			if (ShowCustom)
			{
				foreach (string key in keys)
				{
					var field = new MultiField("custom:" + key, "{" + key + "}", MultiKind.Custom);
					fields.Add(field);
					byName[field.Name] = field;
					customFields.Add(new MultiCustomField(key, field));
				}
			}
			foreach (MultiField field in fields)
			{
				SetText(field);
			}
			foreach ((string box, string property) in Prompts)
			{
				MultiField field = byName[box];
				field.PromptText = GetSameValue(property);
				field.EnablePrompt();
			}
			foreach (MultiField field in fields)
			{
				field.Arm();
			}
		}

		// SetText(Control, propName): list fields first, the others by SetSimpleText
		private void SetText(MultiField field)
		{
			if (field.Kind == MultiKind.List)
			{
				if (SetListText(field))
				{
					field.Check = MultiCheck.Indeterminate;
					field.ClearOnUncheck();
				}
				else
				{
					field.Check = MultiCheck.Checked;
				}
			}
			else
			{
				SetSimpleText(field);
			}
		}

		// SetListText: the entries all the books share are shown; true if the books differ (then the field is indeterminate)
		private bool SetListText(MultiField field)
		{
			HashSet<string> shared = null;
			string first = null;
			bool same = true;
			foreach (ComicBook book in books)
			{
				string value = book.GetStringPropertyValue(field.Property);
				if (first == null)
				{
					first = value;
				}
				else
				{
					same &= first == value;
				}
				HashSet<string> set = value.ListStringToSet(',');
				shared = shared != null ? new HashSet<string>(shared.Intersect(set)) : set;
			}
			field.Text = shared.ToListString(", ");
			field.OldList = shared;
			return !same;
		}

		// SetSimpleText: when all the books have the same value it is shown and the field is checked, else the box stays empty
		private void SetSimpleText(MultiField field)
		{
			bool same = true;
			object value = null;
			foreach (ComicBook book in books)
			{
				object current = field.PropertyInfo == null ? book.GetStringPropertyValue(field.Property) : field.PropertyInfo.GetValue(book, null);
				if (value == null)
				{
					value = current;
				}
				else if (!Equals(current, value))
				{
					same = false;
					break;
				}
			}
			if (same)
			{
				field.Check = MultiCheck.Checked;
				try
				{
					switch (value)
					{
						case bool b:
							field.Text = ComicInfo.GetYesNoAsText(b);
							break;
						case YesNo yn:
							field.Text = ComicInfo.GetYesNoAsText(yn);
							break;
						case MangaYesNo manga:
							field.Text = ComicInfo.GetYesNoAsText(manga);
							break;
						case int n:
							field.Text = n == -1 ? string.Empty : n.ToString();
							break;
						case DateTime d:
							field.Date = d;
							break;
						default:
							field.Text = value?.ToString() ?? string.Empty;
							break;
					}
				}
				catch (Exception)
				{
					// as the original: a value that does not fit its box leaves the box as it is
				}
			}
			else if (field.Kind == MultiKind.Date)
			{
				field.Date = DateTime.MinValue;
			}
			else
			{
				field.Text = string.Empty;
			}
		}

		// ---- Store

		private sealed class StoreInfo
		{
			public string PropertyName;
			public bool ListMode;
			public object Value;
			public HashSet<string> NewList;
			public HashSet<string> OldList;
			public PropertyInfo PropertyInfo;
		}

		/// <summary>True if OK would write something: some field is checked (or indeterminate) and enabled.</summary>
		public bool HasChanges => fields.Any(f => f.IsChecked && f.Enabled);

		/// <summary>
		/// OK (<c>Store</c>): writes every checked field into every book. A checked field is written as its text says (Yes/No boxes, numbers and dates are converted; a number
		/// that is not one is the property's default); an indeterminate list field replaces the entries that every book had by the entries in the box and keeps the others.
		/// Returns the number of fields written.
		/// </summary>
		public int Store(Action<int, int> progress = null)
		{
			var list = new List<StoreInfo>();
			foreach (MultiField field in fields)
			{
				if (!field.IsChecked || !field.Enabled)
				{
					continue;
				}
				StoreInfo info = Describe(field);
				if (info != null)
				{
					list.Add(info);
				}
			}
			if (list.Count == 0)
			{
				return 0;
			}
			int done = 0;
			foreach (ComicBook book in books)
			{
				foreach (StoreInfo item in list)
				{
					if (item.PropertyInfo == null)
					{
						book.SetCustomValue(item.PropertyName.Substring(1, item.PropertyName.Length - 2), item.Value.ToString());
						continue;
					}
					if (!item.ListMode)
					{
						item.PropertyInfo.SetValue(book, item.Value, null);
						continue;
					}
					HashSet<string> entries = book.GetStringPropertyValue(item.PropertyName).ListStringToSet(',');
					entries.RemoveRange(item.OldList);
					entries.AddRange(item.NewList);
					item.PropertyInfo.SetValue(book, entries.ToListString(", "), null);
				}
				progress?.Invoke(++done, books.Length);
			}
			return list.Count;
		}

		private static StoreInfo Describe(MultiField field)
		{
			string text = field.Text;
			PropertyInfo property = field.PropertyInfo;
			var info = new StoreInfo { PropertyName = field.Property, PropertyInfo = property, ListMode = field.Check == MultiCheck.Indeterminate };
			if (property == null)
			{
				info.Value = text;
			}
			else if (property.PropertyType == typeof(bool))
			{
				info.Value = string.Compare(text, ComicInfo.YesText, StringComparison.CurrentCultureIgnoreCase) == 0;
			}
			else if (property.PropertyType == typeof(YesNo))
			{
				info.Value = EditControls.GetYesNo(text);
			}
			else if (property.PropertyType == typeof(MangaYesNo))
			{
				info.Value = EditControls.GetMangaYesNo(text);
			}
			else if (property.PropertyType == typeof(int))
			{
				if (!int.TryParse(text, out int number) || number < 0)
				{
					number = property.DefaultValue(-1);
				}
				info.Value = number;
			}
			else if (property.PropertyType == typeof(float))
			{
				if (!float.TryParse(text, out float real))
				{
					real = property.DefaultValue(0f);
				}
				info.Value = real;
			}
			else if (property.PropertyType == typeof(DateTime))
			{
				info.Value = field.Date;
			}
			else
			{
				info.Value = text;
				if (info.ListMode)
				{
					info.NewList = text.ListStringToSet(',');
					info.OldList = field.OldList;
				}
			}
			return info;
		}

		/// <summary>How a rating field turns its text into the stars of the box (<c>RatingControl.Rating</c>: the text as a number, clamped to 0 to 5, rounded to one digit).</summary>
		public static double RatingOf(string text)
		{
			float.TryParse(text, out float result);
			return Math.Clamp(Math.Round(result, 1), 0, 5);
		}

		/// <summary>The text a rating field holds for a number of stars (<c>RatingControl.Text = value.ToString()</c>).</summary>
		public static string RatingText(double rating) => ((float)Math.Clamp(rating, 0, 5)).ToString(CultureInfo.CurrentCulture);

		/// <summary>The spin settings of the number boxes of this dialog (its constructor's <c>SpinButton.AddUpDown</c> calls; the day starts at today's day of the month).</summary>
		public static SpinRules.Config SpinFor(string box, DateTime now)
		{
			switch (box)
			{
				case "txCount":
				case "txAlternateCount":
					return new SpinRules.Config(1, 0, int.MaxValue, 1);
				case "txYear":
					return new SpinRules.Config(now.Year, int.MinValue, int.MaxValue, 1);
				case "txMonth":
					return new SpinRules.Config(now.Month, 1, 12, 1);
				case "txDay":
					return new SpinRules.Config(now.Day, 1, 31, 1);
				case "txPagesAsTextSimple":
					return new SpinRules.Config(1, 1, int.MaxValue, 1);
				default:
					return SpinRules.Config.Default;
			}
		}
	}
}
