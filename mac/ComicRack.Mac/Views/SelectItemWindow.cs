using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using ComicRack.Mac.Controls;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The dialog that asks for one name (<c>cYo.Common.Windows/Forms/SelectItemDialog.cs</c>): a label (the caption and a colon, "Name:" without one), a text box (a combo box
	/// when there are items to choose from), OK and Cancel. OK is off while the text is empty. The title is the caller's caption. Returns the trimmed text, or null after Cancel
	/// (<c>GetName</c>). The Pages tab asks for the name of a bookmark with it. Spec: <c>mac/specs/views/SelectItemDialog.md</c>.
	/// </summary>
	public sealed class SelectItemWindow : CrDialogWindow
	{
		/// <summary>The client size of the form in the Designer (344 x 130); the buttons start at y 94, 12 from the right edge and from the bottom.</summary>
		public const double DesignWidth = 344, ButtonsTop = 94;

		private readonly Label label = new Label { Padding = new Thickness(0) };
		private readonly TextBox text = new TextBox { MinWidth = 0 };
		private readonly ComboBox choice = new ComboBox { MinWidth = 0, IsEditable = true };
		private readonly bool hasItems;

		public SelectItemWindow(string caption, string value, IReadOnlyList<string> items = null, string itemCaption = null)
		{
			TrSection = "SelectItemDialog";
			Title = caption;
			ButtonBarMargin = new Thickness(12, 0, 12, 12);
			hasItems = items != null && items.Count > 0;
			label.Content = string.IsNullOrEmpty(itemCaption) ? UiText.Get("SelectItemDialog", "lblName", "Name:") : itemCaption + ":";
			var body = new Panel { Width = DesignWidth, Height = ButtonsTop };
			DesignerLayout.Place(body, label, 12, 24, 320, 15, Anchor.TopLeft, DesignWidth, ButtonsTop);
			if (hasItems)
			{
				foreach (string item in items)
				{
					choice.Items.Add(item);
				}
				choice.Text = value ?? string.Empty;
				DesignerLayout.Place(body, choice, 12, 40, 320, 21, Anchor.TopLeft, DesignWidth, ButtonsTop);
			}
			else
			{
				text.Text = value ?? string.Empty;
				DesignerLayout.Place(body, text, 12, 40, 320, 21, Anchor.TopLeft, DesignWidth, ButtonsTop);
			}
			Body = body;
			OkButton.IsEnabled = !string.IsNullOrEmpty(value);
			// the property itself, not TextChanged (which the box raises a moment later)
			text.PropertyChanged += (sender, e) =>
			{
				if (e.Property == TextBox.TextProperty)
				{
					OkButton.IsEnabled = !string.IsNullOrEmpty(text.Text);
				}
			};
			choice.PropertyChanged += (sender, e) =>
			{
				if (e.Property == ComboBox.TextProperty)
				{
					OkButton.IsEnabled = !string.IsNullOrEmpty(choice.Text);
				}
			};
			Opened += (sender, e) =>
			{
				Control box = hasItems ? (Control)choice : text;
				box.Focus();
				text.SelectAll();
			};
		}

		public Label NameLabel => label;

		public TextBox NameBox => text;

		public ComboBox NameChoice => choice;

		/// <summary>The text as OK returns it: trimmed.</summary>
		public string Value => ((hasItems ? choice.Text : text.Text) ?? string.Empty).Trim();

		/// <summary>Asks for a name over <paramref name="owner"/>; null after Cancel.</summary>
		public static async Task<string> GetNameAsync(Window owner, string caption, string value, IReadOnlyList<string> items = null, string itemCaption = null)
		{
			var window = new SelectItemWindow(caption, value, items, itemCaption);
			return await window.ShowDialogAsync(owner) ? window.Value : null;
		}
	}
}
