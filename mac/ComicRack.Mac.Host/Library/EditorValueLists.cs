using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using cYo.Common.IO;
using cYo.Common.Text;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Host.Library
{
	/// <summary>
	/// The value lists of the book editors (the drop-downs and the auto-complete of the Info dialog): what the books of the library already hold for a field,
	/// plus the defaults of <c>DefaultLists.txt</c>. It is <c>ComicRack/Config/DefaultLists.cs</c> without the WinForms <c>AutoCompleteStringCollection</c>:
	/// the same sources in the same order, returned as plain lists. A list keeps the first of equal values (the editors collect the values into a set), the
	/// empty value comes first in the lists of the combo boxes (<see cref="WithEmpty"/>), and a <c>"-"</c> entry marks a separator before the next entry.
	/// </summary>
	public sealed class EditorValueLists
	{
		/// <summary>The marker of a separator in a list (<c>ComboBoxSkinner.AutoSeparatorList</c> turns the entry after it into a separated item).</summary>
		public const string Separator = "-";

		private const string DefaultGenresSection = "Book Genres";
		private const string DefaultFormatsSection = "Book Formats";
		private const string DefaultAgeRatingsSection = "Age Ratings";
		private const string DefaultBookAgesSection = "Book Ages";
		private const string DefaultBookConditionsSection = "Book Conditions";
		private const string DefaultBookCollectionStatusSection = "Book Collection Status";

		private readonly Func<IEnumerable<ComicBook>> getBooks;

		public EditorValueLists(Func<IEnumerable<ComicBook>> getBooks, IEnumerable<string> initPaths)
		{
			this.getBooks = getBooks ?? throw new ArgumentNullException(nameof(getBooks));
			string[] paths = (initPaths ?? Array.Empty<string>()).ToArray();
			DefaultGenres = LoadDefaultTextList(paths, DefaultGenresSection).ToArray();
			DefaultFormats = LoadDefaultTextList(paths, DefaultFormatsSection).Concat(ComicBook.FormatIcons.Keys).Distinct().OrderBy(s => s, StringComparer.CurrentCulture).ToArray();
			DefaultAgeRatings = LoadDefaultTextList(paths, DefaultAgeRatingsSection).Concat(ComicBook.AgeRatingIcons.Keys).OrderBy(s => s, StringComparer.CurrentCulture).Distinct().ToArray();
			DefaultBookAges = LoadDefaultTextList(paths, DefaultBookAgesSection).ToArray();
			DefaultBookConditions = LoadDefaultTextList(paths, DefaultBookConditionsSection).ToArray();
			DefaultBookCollectionStatus = LoadDefaultTextList(paths, DefaultBookCollectionStatusSection).ToArray();
		}

		/// <summary>The places <c>DefaultLists.txt</c> is read from: next to the program, then <paramref name="userFolder"/> (the Windows app also reads the all users and the roaming folder).</summary>
		public static IEnumerable<string> DefaultLocations(string userFolder = null)
		{
			yield return Path.Combine(AppContext.BaseDirectory, "DefaultLists.txt");
			if (!string.IsNullOrEmpty(userFolder))
			{
				yield return Path.Combine(userFolder, "DefaultLists.txt");
			}
		}

		public string[] DefaultGenres { get; }

		public string[] DefaultFormats { get; }

		public string[] DefaultAgeRatings { get; }

		public string[] DefaultBookAges { get; }

		public string[] DefaultBookConditions { get; }

		public string[] DefaultBookCollectionStatus { get; }

		/// <summary>The values of one field of every book (empty ones included, duplicates kept); <paramref name="sort"/> orders them by the current culture.</summary>
		public List<string> GetComicFieldList(Func<ComicBook, string> field, bool sort = false)
		{
			var values = new List<string>();
			foreach (ComicBook book in getBooks())
			{
				values.Add(field(book) ?? string.Empty);
			}
			if (sort)
			{
				values.Sort(StringComparer.CurrentCulture);
			}
			return values;
		}

		/// <summary>The genres the books use, then the defaults (<c>GetGenreList</c>); with <paramref name="withSeparator"/> a separator marker sits between them.</summary>
		public List<string> GetGenreList(bool withSeparator)
		{
			List<string> list = GetComicFieldList(book => book.Genre);
			list.Remove(string.Empty);
			if (withSeparator)
			{
				list.Remove(Separator);
				if (list.Count > 0)
				{
					list.Add(Separator);
				}
			}
			list.AddRange(DefaultGenres);
			return list;
		}

		public List<string> GetFormatList()
		{
			return WithDefaults(GetComicFieldList(book => book.ShadowFormat, sort: true), DefaultFormats);
		}

		public List<string> GetAgeRatingList()
		{
			return WithDefaults(GetComicFieldList(book => book.AgeRating), DefaultAgeRatings);
		}

		public List<string> GetBookAgeList()
		{
			return WithDefaults(GetComicFieldList(book => book.BookAge), DefaultBookAges);
		}

		public List<string> GetBookConditionList()
		{
			return WithDefaults(GetComicFieldList(book => book.BookCondition), DefaultBookConditions);
		}

		public List<string> GetBookCollectionStatusList()
		{
			List<string> list = GetComicFieldList(book => book.BookCollectionStatus);
			list.Remove(string.Empty);
			list.Remove(Separator);
			list.AddRange(DefaultBookCollectionStatus);
			return list;
		}

		// the used values, a separator marker when there are some, then the defaults
		private static List<string> WithDefaults(List<string> used, string[] defaults)
		{
			used.Remove(string.Empty);
			used.Remove(Separator);
			if (used.Count > 0)
			{
				used.Add(Separator);
			}
			used.AddRange(defaults);
			return used;
		}

		/// <summary>
		/// What <c>EditControlUtility.SetText(ComboBox, ...)</c> puts into a combo box: the empty value, then the values once each in the order they first appear
		/// (a repeated <c>"-"</c> stays one marker).
		/// </summary>
		public static List<string> WithEmpty(IEnumerable<string> values)
		{
			var seen = new HashSet<string>(StringComparer.Ordinal) { string.Empty };
			var list = new List<string> { string.Empty };
			foreach (string value in values)
			{
				if (value != null && seen.Add(value))
				{
					list.Add(value);
				}
			}
			return list;
		}

		/// <summary>
		/// What a text box offers as completions (<c>AutoCompleteCustomSource</c>): the values once each, without the empty one and the separator markers.
		/// A value that holds a list ("Stan Lee, Jack Kirby") is offered whole, as the original does; the list selector splits it.
		/// </summary>
		public static List<string> Completions(IEnumerable<string> values)
		{
			return values.Where(v => !string.IsNullOrEmpty(v) && v != Separator).Distinct(StringComparer.Ordinal).ToList();
		}

		private static IEnumerable<string> LoadDefaultTextList(IEnumerable<string> files, string section)
		{
			var list = new List<string>();
			foreach (string file in files)
			{
				try
				{
					list.AddRange(LoadTextList(file, section));
				}
				catch (Exception)
				{
					// an unreadable file is skipped, as the original does
				}
			}
			return list;
		}

		private static IEnumerable<string> LoadTextList(string file, string section)
		{
			if (!File.Exists(file))
			{
				yield break;
			}
			string sectionHeader = $"[{section}]";
			bool sectionFound = false;
			string prefix = null;
			foreach (string line in FileUtility.ReadLines(file).TrimEndStrings().RemoveEmpty())
			{
				if (line.StartsWith(";"))
				{
					continue;
				}
				if (line.StartsWith(sectionHeader))
				{
					sectionFound = true;
					continue;
				}
				if (sectionFound && line.StartsWith("["))
				{
					break;
				}
				if (sectionFound)
				{
					if (!char.IsWhiteSpace(line[0]) || string.IsNullOrEmpty(prefix))
					{
						prefix = line;
						yield return line;
					}
					else
					{
						yield return prefix + ": " + line.Trim();
					}
				}
			}
		}
	}
}
