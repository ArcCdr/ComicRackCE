using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using cYo.Common.Compression;
using cYo.Common.Drawing;
using cYo.Common.IO;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Host.Library
{
	/// <summary>
	/// The icon packages of the Core (<c>ComicBook.PublisherIcons</c>, <c>AgeRatingIcons</c>, <c>FormatIcons</c>, <c>SpecialIcons</c>, <c>GenericIcons</c>) that
	/// the covers and the Info dialog show next to a book: <c>Program.cs</c> fills them at start from the zip files <c>Publishers*.zip</c>, <c>AgeRatings*.zip</c>,
	/// <c>Formats*.zip</c> and <c>Special*.zip</c> of <c>Resources\Icons</c>. The repository holds those icons as plain folders (the Windows build zips them), so a
	/// folder of that name is read as a package too. The key rules are the original's (<c>SplitIconKeys</c>: a name may list several keys separated by
	/// <c>,</c> or <c>#</c>; <c>SplitIconKeysWithYearAndMonth</c>: <c>Name(1990-1995)</c> stands for every year of the range).
	/// </summary>
	public static class IconPackages
	{
		private static readonly Regex DateRange = new Regex(@"\((?<startYear>\d{4})(?:_(?<startMonth>\d{2}))?-(?<endYear>\d{4})(?:_(?<endMonth>\d{2}))?\)", RegexOptions.Compiled);
		private static readonly object Gate = new object();
		private static readonly HashSet<string> Loaded = new HashSet<string>(StringComparer.Ordinal);

		/// <summary>The default place of the packages: <c>Resources/Icons</c> next to the program.</summary>
		public static string DefaultFolder => Path.Combine(AppContext.BaseDirectory, "Resources", "Icons");

		/// <summary>Loads the packages of <paramref name="folders"/> once per folder and process (a folder that was loaded is skipped). Missing folders are fine.</summary>
		public static void Load(IEnumerable<string> folders)
		{
			lock (Gate)
			{
				foreach (string folder in folders.Where(f => !string.IsNullOrEmpty(f)).Select(Path.GetFullPath))
				{
					if (!Directory.Exists(folder) || !Loaded.Add(folder))
					{
						continue;
					}
					ComicBook.PublisherIcons.AddRange(Packages(folder, "Publishers*"), SplitKeysWithYearAndMonth);
					ComicBook.AgeRatingIcons.AddRange(Packages(folder, "AgeRatings*"), SplitKeys);
					ComicBook.FormatIcons.AddRange(Packages(folder, "Formats*"), SplitKeys);
					ComicBook.SpecialIcons.AddRange(Packages(folder, "Special*"), SplitKeys);
					foreach (KeyValuePair<string, ImagePackage> generic in Generics(folder, "_"))
					{
						ComicBook.GenericIcons[generic.Key] = generic.Value;
					}
				}
			}
		}

		// the zip files and the folders that match the name pattern, in name order
		private static IVirtualFolder[] Packages(string folder, string pattern)
		{
			var packages = new List<IVirtualFolder>();
			foreach (string path in Directory.GetFileSystemEntries(folder, pattern).OrderBy(p => p, StringComparer.Ordinal))
			{
				if (Directory.Exists(path))
				{
					packages.Add(new VirtualFileFolder(path));
				}
				else if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
				{
					ZipFileFolder zip = ZipFileFolder.CreateFromFile(path);
					if (zip != null)
					{
						packages.Add(zip);
					}
				}
			}
			return packages.ToArray();
		}

		// Program.CreateGenericsIcons: packages named "_<Name>" hold the icons of the tags that start with "<Name>"
		private static IEnumerable<KeyValuePair<string, ImagePackage>> Generics(string folder, string trigger)
		{
			foreach (string path in Directory.GetFileSystemEntries(folder, trigger + "*").OrderBy(p => p, StringComparer.Ordinal))
			{
				IVirtualFolder package = null;
				if (Directory.Exists(path))
				{
					package = new VirtualFileFolder(path);
				}
				else if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
				{
					package = ZipFileFolder.CreateFromFile(path);
				}
				if (package == null)
				{
					continue;
				}
				string key = Path.GetFileNameWithoutExtension(path).Replace(trigger, string.Empty);
				var icons = new ImagePackage { EnableWidthCropping = true };
				icons.Add(package, SplitKeys);
				yield return new KeyValuePair<string, ImagePackage>(key, icons);
			}
		}

		/// <summary><c>Program.SplitIconKeys</c>: a file name may name several keys.</summary>
		public static IEnumerable<string> SplitKeys(string value)
		{
			return value.Split(',', '#');
		}

		/// <summary><c>Program.SplitIconKeysWithYearAndMonth</c>: <c>Marvel(1990-1995)</c> becomes <c>Marvel(1990)</c> ... <c>Marvel(1995)</c> and the year and month keys in between.</summary>
		public static IEnumerable<string> SplitKeysWithYearAndMonth(string value)
		{
			foreach (string key in SplitKeys(value))
			{
				Match match = DateRange.Match(key);
				if (!match.Success)
				{
					yield return key;
					continue;
				}
				Group startMonthGroup = match.Groups["startMonth"];
				Group endMonthGroup = match.Groups["endMonth"];
				int startYear = int.Parse(match.Groups["startYear"].Value);
				int endYear = int.Parse(match.Groups["endYear"].Value);
				int startMonth = startMonthGroup.Success ? int.Parse(startMonthGroup.Value) : 1;
				int endMonth = endMonthGroup.Success ? int.Parse(endMonthGroup.Value) : 12;
				string baseKey = DateRange.Replace(key, string.Empty);
				for (int year = startYear; year <= endYear; year++)
				{
					// the year alone only for the files that have no month
					yield return $"{baseKey}({year})";
					int firstMonth = year == startYear ? startMonth : 1;
					int lastMonth = year == endYear ? endMonth : 12;
					for (int month = firstMonth; month <= lastMonth; month++)
					{
						yield return $"{baseKey}({year}_{month:00})";
					}
				}
			}
		}
	}
}
