using System;
using Avalonia.Controls;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// A list of values with a check box in front of each (<c>CheckedListBoxEx</c>): the checked items are the selected ones, a click on an item or its box flips it. The look of an item is the
	/// control's theme.
	/// </summary>
	public sealed class CrCheckList : ListBox, IChromeElement
	{
		public CrCheckList()
		{
			SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle;
			Classes.Add("cr-check-list");
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.Buttons;
	}
}
