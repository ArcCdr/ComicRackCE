using System.Text;
using cYo.Common.Text;

namespace ComicRack.Mac.Browser
{
	/// <summary>
	/// The text of the status bar while the browser is active: <c>ComicBrowserControl.SelectionInfo</c> of the original
	/// (<c>ComicRack/Views/ComicBrowserControl.cs:635-690</c>), with its texts from <c>ComicBrowserControl.xml</c> (keys ComicSingle, ComicMulti,
	/// ComicFiltered, ComicSelected). "List: 14,706 Books (2 filtered) / 12.40 GB - 1 selected / 3.20 MB": the list name, the number of books shown,
	/// the books hidden by the search or the view options, the size of the books shown, and either the path of the one selected book or the number
	/// selected with their size.
	/// </summary>
	public static class SelectionInfo
	{
		private const string Section = "ComicBrowserControl";

		/// <param name="listName">The name of the list that is shown, or null.</param>
		/// <param name="shown">Books in the grid.</param>
		/// <param name="total">Books before the search.</param>
		/// <param name="shownSize">File size of the books in the grid; 0 leaves the size out.</param>
		/// <param name="selected">Books selected.</param>
		/// <param name="selectedPath">The file of the selected book when exactly one is selected and it has a file, else null.</param>
		/// <param name="selectedSize">File size of the selected books; 0 leaves the size out.</param>
		public static string Build(string listName, int shown, int total, long shownSize, int selected, string selectedPath, long selectedSize)
		{
			var text = new StringBuilder();
			if (!string.IsNullOrEmpty(listName))
			{
				text.Append(listName).Append(": ");
			}
			text.Append(Format(shown == 1 ? UiText.Raw(Section, "ComicSingle", "{0} Book") : UiText.Raw(Section, "ComicMulti", "{0} Books"), shown));
			if (total != shown)
			{
				text.Append(" (").Append(Format(UiText.Raw(Section, "ComicFiltered", "{0} filtered"), total - shown)).Append(')');
			}
			if (shownSize != 0)
			{
				text.Append(" / ").Append(Size(shownSize));
			}
			if (selected != 0)
			{
				text.Append(" - ");
				if (selected == 1 && !string.IsNullOrEmpty(selectedPath))
				{
					text.Append(selectedPath);
				}
				else
				{
					text.Append(Format(UiText.Raw(Section, "ComicSelected", "{0} selected"), selected));
				}
			}
			if (selectedSize != 0)
			{
				text.Append(" / ").Append(Size(selectedSize));
			}
			return text.ToString();
		}

		/// <summary>The size as the original writes it (<c>FileLengthFormat</c>: "12.40 GB", "512 Bytes").</summary>
		public static string Size(long bytes)
		{
			return string.Format(new FileLengthFormat(), "{0}", bytes);
		}

		// The original passes the number to string.Format; a translation without the {0} placeholder stays as it is.
		private static string Format(string format, int number)
		{
			try
			{
				return string.Format(format, number);
			}
			catch (System.FormatException)
			{
				return format;
			}
		}
	}
}
