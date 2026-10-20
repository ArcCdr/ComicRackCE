using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// P5.T4, parity policy 2.11 rule 5: colours only come from the <c>Cr.*</c> tokens. No view, custom control or XAML file outside <c>Themes/</c> may
	/// contain a colour literal (<c>#RRGGBB</c>, <c>Colors.X</c>, <c>Brushes.X</c>, <c>Color.FromRgb</c>, <c>new SolidColorBrush(...)</c>); <c>Transparent</c> is
	/// not a colour. The MVP files below are known exceptions until P5.R1 to P5.R3 restyle them; the test also fails when a listed file no longer has
	/// a literal, so the list shrinks as the restyle tasks land.
	/// </summary>
	public class NoLiteralColorsTests
	{
		/// <summary>The MVP files with literal colours, removed by the task named next to them.</summary>
		private static readonly Dictionary<string, string> KnownExceptions = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["Views/BookCell.cs"] = "P5.R2 (thumbnail items)",
			["Views/ImportWindow.axaml"] = "P5.R1 (message and import dialogs)",
			["Views/PerfOverlay.cs"] = "P5.R3 (the performance overlay moves with the reader chrome)",
			["Views/ReaderView.cs"] = "P5.R3 (reader chrome)",
			["Views/ReaderWindow.axaml"] = "P5.R3 (reader chrome)",
		};

		/// <summary>The one file that turns token colours into brushes (its brushes are built from <c>ThemeTokens</c>, never from a literal).</summary>
		private const string TokenBrushFactory = "Controls/ChromePaint.cs";

		private static readonly Regex HexInXaml = new Regex("[\"'>]\\s*#[0-9A-Fa-f]{3,8}\\b", RegexOptions.Compiled);

		private static readonly Regex[] CSharp =
		{
			new Regex(@"\bColors\.(?!Transparent\b)\w+", RegexOptions.Compiled),
			new Regex(@"\bBrushes\.(?!Transparent\b)\w+", RegexOptions.Compiled),
			new Regex(@"\bColor\.(FromRgb|FromArgb|Parse|TryParse)\b", RegexOptions.Compiled),
			new Regex(@"new\s+(Immutable)?SolidColorBrush\s*\(", RegexOptions.Compiled),
			new Regex("\"#[0-9A-Fa-f]{6,8}\"", RegexOptions.Compiled),
		};

		private static readonly Regex[] Xaml =
		{
			HexInXaml,
			new Regex(@"\b(Foreground|Background|BorderBrush|Fill|Stroke|Color)\s*=\s*""(?!\{|Transparent"")[A-Za-z]+""", RegexOptions.Compiled),
		};

		/// <summary>The literals in a source text (empty when clean); <paramref name="isXaml"/> picks the pattern set.</summary>
		public static IReadOnlyList<string> Find(string text, bool isXaml)
		{
			string withoutComments = isXaml ? Regex.Replace(text, "<!--.*?-->", string.Empty, RegexOptions.Singleline) : Regex.Replace(Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"//[^\n]*", string.Empty);
			var found = new List<string>();
			foreach (Regex pattern in isXaml ? Xaml : CSharp)
			{
				foreach (Match m in pattern.Matches(withoutComments))
				{
					found.Add(m.Value.Trim());
				}
			}
			return found;
		}

		private static IEnumerable<string> Files()
		{
			string app = Path.Combine(OriginalColors.RepoRoot, "mac", "ComicRack.Mac");
			foreach (string folder in new[] { "Views", "Controls" })
			{
				foreach (string f in Directory.EnumerateFiles(Path.Combine(app, folder), "*.cs", SearchOption.AllDirectories))
				{
					yield return f;
				}
			}
			foreach (string f in Directory.EnumerateFiles(app, "*.axaml", SearchOption.AllDirectories))
			{
				string relative = Path.GetRelativePath(app, f).Replace('\\', '/');
				if (!relative.StartsWith("Themes/", StringComparison.Ordinal) && !relative.StartsWith("bin/", StringComparison.Ordinal) && !relative.StartsWith("obj/", StringComparison.Ordinal))
				{
					yield return f;
				}
			}
		}

		[Fact]
		public void No_view_control_or_xaml_file_outside_the_themes_has_a_colour_literal_except_the_listed_MVP_files()
		{
			string app = Path.Combine(OriginalColors.RepoRoot, "mac", "ComicRack.Mac");
			var offenders = new List<string>();
			var seen = new HashSet<string>();
			foreach (string file in Files())
			{
				string relative = Path.GetRelativePath(app, file).Replace('\\', '/');
				if (relative == TokenBrushFactory)
				{
					continue;
				}
				IReadOnlyList<string> literals = Find(File.ReadAllText(file), file.EndsWith(".axaml", StringComparison.Ordinal));
				if (literals.Count > 0)
				{
					seen.Add(relative);
					if (!KnownExceptions.ContainsKey(relative))
					{
						offenders.Add(relative + ": " + string.Join(", ", literals.Distinct().Take(4)));
					}
				}
			}
			Assert.True(offenders.Count == 0, "colour literals (use a Cr token): " + string.Join("; ", offenders));
			string[] stale = KnownExceptions.Keys.Where(k => !seen.Contains(k)).ToArray();
			Assert.True(stale.Length == 0, "these files no longer have a literal, remove them from the list: " + string.Join(", ", stale));
		}

		[Fact]
		public void The_scanner_finds_each_kind_of_literal_and_ignores_Transparent_comments_and_selectors()
		{
			Assert.NotEmpty(Find("<Border Background=\"#1E1E1E\" />", true));
			Assert.NotEmpty(Find("<TextBlock Foreground=\"#B0B0B0\" />", true));
			Assert.NotEmpty(Find("<Border Background=\"Red\" />", true));
			Assert.Empty(Find("<Border Background=\"{DynamicResource Cr.WindowBrush}\" />", true));
			Assert.Empty(Find("<Border Background=\"Transparent\" />", true));
			Assert.Empty(Find("<!-- #123456 in a comment --><Style Selector=\"Button#Back\" />", true));
			Assert.NotEmpty(Find("var b = new SolidColorBrush(Color.FromRgb(1, 2, 3));", false));
			Assert.NotEmpty(Find("x = Brushes.White;", false));
			Assert.NotEmpty(Find("x = Colors.Red;", false));
			Assert.NotEmpty(Find("x = \"#FF00FF\";", false));
			Assert.Empty(Find("x = Brushes.Transparent; // Colors.Red in a comment", false));
			Assert.Empty(Find("var b = ChromePaint.Brush(this, ThemeTokens.Names.Window);", false));
		}
	}
}
