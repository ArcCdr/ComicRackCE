using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>One entry of a <see cref="CrValueComboBox"/>: what it shows, what it stands for, a separator line above it and a picture.</summary>
	public sealed class ValueItem
	{
		public ValueItem(string text, string value = null, bool separatorBefore = false, IImage icon = null)
		{
			Text = text ?? string.Empty;
			Value = value ?? Text;
			SeparatorBefore = separatorBefore;
			Icon = icon;
		}

		public string Text { get; }

		/// <summary>The value the box holds when the entry is chosen (the text, unless the box shows another name for it: the language box shows names and holds culture codes).</summary>
		public string Value { get; }

		public bool SeparatorBefore { get; }

		public IImage Icon { get; }

		public bool HasIcon => Icon != null;

		/// <summary>A one unit line above the entry when <see cref="SeparatorBefore"/>.</summary>
		public Thickness SeparatorThickness => SeparatorBefore ? new Thickness(0, 1, 0, 0) : default;

		public override string ToString() => Text;
	}

	/// <summary>
	/// The combo boxes of the original's editors (<c>ComboBoxSkinner</c>): a drop-down list or an editable box with a list, in which an entry can have a picture next to it (the
	/// publisher, age rating and format icons) and a line above it (a <c>"-"</c> in the list means: the next entry starts a new group). <see cref="Value"/> is the text of an
	/// editable box and the value of the chosen entry of a list. Entries are <see cref="ValueItem"/>; the look of an entry is the control's theme.
	/// </summary>
	public sealed class CrValueComboBox : ComboBox, IChromeElement
	{
		public static readonly StyledProperty<string> ValueProperty = AvaloniaProperty.Register<CrValueComboBox, string>(nameof(Value), string.Empty, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

		private bool syncing;
		private IconSet iconSet;

		public CrValueComboBox()
		{
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.ComboBox;

		/// <summary>The icon package whose pictures the entries show (set before <see cref="SetValues"/>).</summary>
		public IconSet IconSet
		{
			get => iconSet;
			set => iconSet = value;
		}

		/// <summary>The text of an editable box, or the value of the chosen entry of a list (empty when nothing is chosen).</summary>
		public string Value
		{
			get => GetValue(ValueProperty);
			set => SetValue(ValueProperty, value ?? string.Empty);
		}

		/// <summary>
		/// Fills the list. <paramref name="values"/> may hold <see cref="CrValueComboBox.SeparatorMarker"/> entries: the entry after one gets a line above it.
		/// <paramref name="icons"/> names the icon package that gives the picture of an entry.
		/// </summary>
		public void SetValues(IEnumerable<string> values, IconSet icons = IconSet.None)
		{
			Func<string, IImage> iconOf = icons == IconSet.None ? (Func<string, IImage>)null : key => PackageIcons.Get(icons, key);
			var items = new List<ValueItem>();
			bool separator = false;
			foreach (string value in values)
			{
				if (value == SeparatorMarker)
				{
					separator = true;
					continue;
				}
				items.Add(new ValueItem(value, null, separator, string.IsNullOrEmpty(value) ? null : iconOf?.Invoke(value)));
				separator = false;
			}
			SetItems(items);
		}

		/// <summary>Fills the list with ready entries.</summary>
		public void SetItems(IReadOnlyList<ValueItem> items)
		{
			syncing = true;
			try
			{
				ItemsSource = items;
			}
			finally
			{
				syncing = false;
			}
			SyncFromValue();
		}

		/// <summary>The marker of a separator in a list of values (<c>ComboBoxSkinner.AutoSeparatorList</c> turns the entry after a <c>"-"</c> into a separated one).</summary>
		public const string SeparatorMarker = "-";

		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
		{
			base.OnPropertyChanged(change);
			if (syncing)
			{
				return;
			}
			if (change.Property == ValueProperty)
			{
				SyncFromValue();
			}
			else if (change.Property == SelectedItemProperty && SelectedItem is ValueItem chosen)
			{
				syncing = true;
				try
				{
					SetCurrentValue(ValueProperty, chosen.Value);
				}
				finally
				{
					syncing = false;
				}
			}
			else if (change.Property == TextProperty && IsEditable)
			{
				syncing = true;
				try
				{
					SetCurrentValue(ValueProperty, Text ?? string.Empty);
				}
				finally
				{
					syncing = false;
				}
			}
		}

		// the box shows what the model holds: the entry with that value (the list), the text (the editable box)
		private void SyncFromValue()
		{
			syncing = true;
			try
			{
				string value = Value ?? string.Empty;
				if (ItemsSource is IEnumerable<ValueItem> items)
				{
					ValueItem found = items.FirstOrDefault(i => string.Equals(i.Value, value, StringComparison.Ordinal));
					if (IsEditable)
					{
						SetCurrentValue(TextProperty, value);
						SetCurrentValue(SelectedItemProperty, found);
					}
					else
					{
						SetCurrentValue(SelectedItemProperty, found);
					}
				}
				else if (IsEditable)
				{
					SetCurrentValue(TextProperty, value);
				}
			}
			finally
			{
				syncing = false;
			}
		}
	}
}
