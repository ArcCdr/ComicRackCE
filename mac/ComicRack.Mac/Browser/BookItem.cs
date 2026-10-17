using System;
using System.Globalization;
using System.IO;
using System.Text;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Browser
{
	/// <summary>
	/// One row of the browser: the book and the few strings the grid shows, sorts and searches by, read once when the catalog is
	/// built. Reading them once keeps sorting and searching (which run on a worker thread) free of the Core's per-property locks,
	/// and means a book that is edited later does not move under a running query.
	/// </summary>
	public sealed class BookItem
	{
		/// <summary>The Core book; null only for synthetic items in tests (the cell then draws no thumbnail).</summary>
		public ComicBook Book { get; }

		/// <summary>The title, or the file name without extension if the book has none.</summary>
		public string Title { get; }

		public string Series { get; }

		public string Number { get; }

		public DateTime Added { get; }

		/// <summary>The second line of a cell: "Series #Number".</summary>
		public string Subtitle { get; }

		// Lower case, without accents: what sorting and searching compare (ordinal).
		public string TitleKey { get; }

		public string SeriesKey { get; }

		/// <summary>The number as a value, or NaN if it is not one (such books sort after the numbered ones).</summary>
		public float NumberValue { get; }

		public BookItem(ComicBook book, string title, string series, string number, DateTime added)
		{
			Book = book;
			Title = string.IsNullOrWhiteSpace(title) ? "Untitled" : title;
			Series = series ?? string.Empty;
			Number = number ?? string.Empty;
			Added = added;
			Subtitle = Series.Length == 0
				? (Number.Length == 0 ? string.Empty : "#" + Number)
				: (Number.Length == 0 ? Series : Series + " #" + Number);
			TitleKey = Fold(Title);
			SeriesKey = Fold(Series);
			NumberValue = float.TryParse(Number, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? n : float.NaN;
		}

		/// <summary>Reads the shown values of a Core book (the effective values: the ones the Windows app shows too).</summary>
		public static BookItem From(ComicBook book)
		{
			string title = book.ShadowTitle;
			if (string.IsNullOrWhiteSpace(title))
			{
				string path = book.FilePath;
				title = string.IsNullOrEmpty(path) ? null : Path.GetFileNameWithoutExtension(path);
			}
			return new BookItem(book, title, book.ShadowSeries, book.ShadowNumber, book.AddedTime);
		}

		/// <summary>
		/// Lower case and without diacritics, so "ete" finds "Été"; the ligatures and the letters that do not decompose are spelled
		/// out ("Œil" is "oeil", "Æsir" "aesir", "Łódź" "lodz"), so that French and Nordic titles sort and search where a reader
		/// expects them. ASCII text takes the cheap path.
		/// </summary>
		internal static string Fold(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return string.Empty;
			}
			bool ascii = true;
			foreach (char c in text)
			{
				if (c > 127)
				{
					ascii = false;
					break;
				}
			}
			if (ascii)
			{
				return text.ToLowerInvariant();
			}
			string d = text.Normalize(NormalizationForm.FormD);
			var sb = new StringBuilder(d.Length + 4);
			foreach (char raw in d)
			{
				if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark)
				{
					continue;
				}
				char c = char.ToLowerInvariant(raw);
				switch (c)
				{
					case '\u0153': sb.Append("oe"); break; // œ
					case '\u00E6': sb.Append("ae"); break; // æ
					case '\u00DF': sb.Append("ss"); break; // ß
					case '\u00F8': sb.Append('o'); break; // ø
					case '\u0142': sb.Append('l'); break; // ł
					case '\u0111': sb.Append('d'); break; // đ
					default: sb.Append(c); break;
				}
			}
			return sb.ToString();
		}

		public override string ToString() => string.IsNullOrEmpty(Subtitle) ? Title : Title + " (" + Subtitle + ")";
	}
}
