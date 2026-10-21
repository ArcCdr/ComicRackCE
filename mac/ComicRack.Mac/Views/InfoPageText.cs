using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using ComicRack.Mac.Controls;

namespace ComicRack.Mac.Views
{
	/// <summary>The texts of the Info dialog: every label and button the Designer names (its <c>Tag</c> is the control's name) gets the original's translation (TR section <c>ComicBookDialog</c>).</summary>
	public static class InfoPageText
	{
		public const string Section = "ComicBookDialog";

		/// <summary>The text for a control of the dialog: the translation of <paramref name="key"/> (the control's name) or the Designer's English text, without the Windows mnemonic.</summary>
		public static string Get(string key, string english) => UiText.Get(Section, key, english);

		/// <summary>Translates the labels, buttons and check boxes below <paramref name="root"/> whose <c>Tag</c> names their key.</summary>
		public static void Translate(Control root)
		{
			foreach (ContentControl control in root.GetLogicalDescendants().OfType<ContentControl>().ToList())
			{
				if (control.Tag is string key && control.Content is string english)
				{
					control.Content = Get(key, english);
				}
			}
		}
	}
}
