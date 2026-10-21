using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Editor;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The Custom tab of the Info dialog (<c>tabCustom</c>, IN.05): a grid of the custom value keys of the library and their values for the book (<c>customValuesData</c>). A key is a
	/// row; the last row is empty and takes a new key. The Name column can be resized (the header's splitter), the Value cell offers the values other books have under the key.
	/// Script values (keys with a point) are not listed unless <see cref="BookEditorModel.ShowScriptValues"/>.
	/// </summary>
	public partial class InfoCustomPage : UserControl
	{
		private BookEditorModel model;
		private readonly List<Row> rows = new List<Row>();
		private double nameWidth = 100;

		private sealed class Row
		{
			public CustomValueRow Data;
			public Grid Grid;
			public TextBox Name;
			public AutoCompleteBox Value;
		}

		public InfoCustomPage()
		{
			InitializeComponent();
			CustomValueName.Text = InfoPageText.Get("CustomValueName", "Name");
			CustomValueValue.Text = InfoPageText.Get("CustomValueValue", "Value");
			CustomValueName.SizeChanged += (sender, e) => SetNameWidth(e.NewSize.Width);
		}

		/// <summary>The width of the Name column.</summary>
		public double NameWidth => nameWidth;

		/// <summary>The header cells: Name and Value.</summary>
		public IReadOnlyList<CrHeader> Headers => new[] { CustomValueName, CustomValueValue };

		/// <summary>The rows the grid shows (the hidden script values are not among them): the name box and the value box of each.</summary>
		public IReadOnlyList<(TextBox Name, AutoCompleteBox Value)> Cells => rows.Select(r => (r.Name, r.Value)).ToList();

		public void Initialize(BookEditorModel editor)
		{
			model = editor;
			DataContext = editor;
			model.CustomRows.CollectionChanged += OnRowsChanged;
			Rebuild();
		}

		private void OnRowsChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
			{
				foreach (CustomValueRow row in e.NewItems)
				{
					AddRow(row);
				}
			}
			else
			{
				Rebuild();
			}
		}

		private void Rebuild()
		{
			RowsHost.Children.Clear();
			rows.Clear();
			foreach (CustomValueRow row in model.CustomRows)
			{
				AddRow(row);
			}
		}

		private void SetNameWidth(double width)
		{
			if (width <= 0 || Math.Abs(width - nameWidth) < 0.01 && rows.Count == 0)
			{
				return;
			}
			nameWidth = width;
			foreach (Row row in rows)
			{
				row.Grid.ColumnDefinitions[0].Width = new GridLength(nameWidth);
			}
		}

		private void AddRow(CustomValueRow data)
		{
			if (data.Hidden)
			{
				return;
			}
			var row = new Row { Data = data };
			row.Name = new TextBox { Text = data.Name, MinWidth = 0, MinHeight = 0, Height = 22 };
			row.Name.Classes.Add("cr-grid-cell");
			row.Value = new AutoCompleteBox { Text = data.Value, MinWidth = 0, MinHeight = 0, Height = 22, IsTextCompletionEnabled = true };
			row.Value.Classes.Add("cr-grid-cell");
			var line = new Border { Width = 1, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
			line.Classes.Add("cr-grid-line");
			var bottom = new Border { Height = 1, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
			bottom.Classes.Add("cr-grid-line");
			row.Grid = new Grid { ColumnDefinitions = new ColumnDefinitions { new ColumnDefinition(new GridLength(nameWidth)), new ColumnDefinition(new GridLength(4)), new ColumnDefinition(GridLength.Star) }, RowDefinitions = new RowDefinitions("22,1") };
			Grid.SetColumn(row.Name, 0);
			Grid.SetColumn(line, 1);
			Grid.SetColumn(row.Value, 2);
			Grid.SetRow(bottom, 1);
			Grid.SetColumnSpan(bottom, 3);
			row.Grid.Children.Add(row.Name);
			row.Grid.Children.Add(line);
			row.Grid.Children.Add(row.Value);
			row.Grid.Children.Add(bottom);
			// the property itself, not the TextChanged event (which the box raises a moment later): OK must find what was typed
			row.Name.PropertyChanged += (sender, e) =>
			{
				if (e.Property == TextBox.TextProperty)
				{
					data.Name = row.Name.Text ?? string.Empty;
					NewRowIfLast(data);
				}
			};
			row.Value.PropertyChanged += (sender, e) =>
			{
				if (e.Property == AutoCompleteBox.TextProperty)
				{
					data.Value = row.Value.Text ?? string.Empty;
					NewRowIfLast(data);
				}
			};
			// the values other books have under the key (customValuesData_EditingControlShowing)
			row.Value.GotFocus += (sender, e) => row.Value.ItemsSource = model.CustomValueSuggestions(data.Name);
			rows.Add(row);
			RowsHost.Children.Add(row.Grid);
		}

		// the grid's new row moves down once the row was used
		private void NewRowIfLast(CustomValueRow data)
		{
			if (ReferenceEquals(model.CustomRows.LastOrDefault(), data) && !data.IsEmpty)
			{
				model.EnsureNewCustomRow();
			}
		}
	}
}
