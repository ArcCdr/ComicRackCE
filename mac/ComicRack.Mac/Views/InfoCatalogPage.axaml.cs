using System;
using System.Collections.Generic;
using System.ComponentModel;
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
	/// The Catalog tab of the Info dialog (<c>tabCatalog</c>, IN.04): store, price, age, condition, owner, location, collection status, ISBN, the three dates and the notes about the
	/// book. Opened/Read and Pages belong to a book without a file. The date boxes are <c>NullableDateTimePicker</c>s: a date that is not set shows nothing, the Delete key clears one,
	/// and choosing a day keeps the time of day the book had.
	/// </summary>
	public partial class InfoCatalogPage : UserControl
	{
		private BookEditorModel model;
		private readonly Dictionary<Control, EditorList> lists = new Dictionary<Control, EditorList>();
		private readonly HashSet<Control> filled = new HashSet<Control>();

		public InfoCatalogPage()
		{
			InitializeComponent();
			InfoPageText.Translate(this);
		}

		/// <summary>The boxes whose values come from the library.</summary>
		public IReadOnlyDictionary<Control, EditorList> ListBoxes => lists;

		/// <summary>The date boxes: Released, Added/Purchased and Opened/Read.</summary>
		public IEnumerable<CalendarDatePicker> DateBoxes => new[] { dtpReleasedTime, dtpAddedTime, dtpOpenedTime };

		public void Initialize(BookEditorModel editor)
		{
			model = editor;
			DataContext = editor;
			Map(cbBookStore, EditorList.BookStore);
			Map(cbBookPrice, EditorList.BookPrice);
			Map(cbBookAge, EditorList.BookAge);
			Map(cbBookCondition, EditorList.BookCondition);
			Map(cbBookOwner, EditorList.BookOwner);
			Map(cbBookLocation, EditorList.BookLocation);
			Map(txCollectionStatus, EditorList.CollectionStatus);
			foreach (Control box in lists.Keys)
			{
				box.GotFocus += (sender, e) => EnsureList((Control)sender);
				// the drop-down of a combo box opens before its first focus event when it is clicked
				box.AddHandler(PointerPressedEvent, (sender, e) => EnsureList((Control)sender), RoutingStrategies.Tunnel);
			}
			model.BookLoaded += (sender, e) => filled.Clear();
			txCollectionStatusSelector.Click += (sender, e) => OpenSelector(txCollectionStatusSelector, txCollectionStatus, EditorList.CollectionStatus);
			// EnableOnlyNumberKeys: digits, the decimal point and the comma
			txPagesAsTextSimple.AddHandler(TextInputEvent, (sender, e) =>
			{
				if (!string.IsNullOrEmpty(e.Text) && e.Text.Any(ch => "0123456789.,".IndexOf(ch) < 0))
				{
					e.Handled = true;
				}
			}, RoutingStrategies.Tunnel);
			Date(dtpAddedTime, () => model.AddedTime, value => model.AddedTime = value, nameof(BookEditorModel.AddedTime));
			Date(dtpReleasedTime, () => model.ReleasedTime, value => model.ReleasedTime = value, nameof(BookEditorModel.ReleasedTime));
			Date(dtpOpenedTime, () => model.OpenedTime, value => model.OpenedTime = value, nameof(BookEditorModel.OpenedTime));
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

		/// <summary>Opens the list selector of a box (<c>ListSelectorControl.ShowPopup</c>); the box takes the result when the popup closes.</summary>
		public Flyout OpenSelector(Control anchor, AutoCompleteBox box, EditorList list)
		{
			return ListSelectorFlyout.Open(anchor, box.Text, model.Values(list), text => box.Text = text);
		}

		// ---- NullableDateTimePicker

		private void Date(CalendarDatePicker picker, Func<DateTime> get, Action<DateTime> set, string property)
		{
			bool syncing = false;
			Action show = () =>
			{
				DateTime value = get();
				DateTime? target = value == DateTime.MinValue ? (DateTime?)null : value;
				if (picker.SelectedDate?.Date == target?.Date)
				{
					return; // the box shows that day already (it reports its own change a moment later; echoing it back would only cut the time of day)
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
			};
			picker.SelectedDateChanged += (sender, e) =>
			{
				DateTime current = get();
				if (syncing || picker.SelectedDate?.Date == (current == DateTime.MinValue ? (DateTime?)null : current.Date))
				{
					return;
				}
				set(Pick(picker.SelectedDate, current));
			};
			// Delete clears the date (NullableDateTimePicker.OnKeyDown)
			picker.KeyDown += (sender, e) =>
			{
				if (e.Key == Key.Delete)
				{
					picker.SelectedDate = null;
					e.Handled = true;
				}
			};
			model.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == property || string.IsNullOrEmpty(e.PropertyName))
				{
					show();
				}
			};
			model.BookLoaded += (sender, e) => show();
			show();
		}

		/// <summary>The value after the user chose <paramref name="picked"/> (null: cleared) in a box that held <paramref name="current"/>: the chosen day with the time of day the box had.</summary>
		public static DateTime Pick(DateTime? picked, DateTime current)
		{
			if (picked == null)
			{
				return DateTime.MinValue;
			}
			TimeSpan time = current == DateTime.MinValue ? TimeSpan.Zero : current.TimeOfDay;
			return BookEditorModel.PickerValue(picked.Value.Date + time);
		}
	}
}
