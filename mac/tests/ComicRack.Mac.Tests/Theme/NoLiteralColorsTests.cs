using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// P5.T4, parity policy 2.11 rule 5: colours only come from the <c>Cr.*</c> tokens. No source or XAML file of the app outside <c>Themes/</c> and
	/// <c>Platform/</c> may contain a colour literal (<c>#RRGGBB</c>, <c>Colors.X</c>, <c>Brushes.X</c>, <c>Color.FromRgb</c>, <c>new SolidColorBrush(...)</c>);
	/// <c>Transparent</c> is not a colour. Since RV8 the scan covers every folder (<c>Thumbnails/</c>, <c>Reader/</c>, <c>Browser/</c>, <c>Services/</c> ...), and
	/// outside the chrome layer (<c>Controls/</c> too) Skia colours and the short forms are literals as well (<c>new SKColor</c>, <c>SKColors.X</c>,
	/// <c>Color.FromUInt32</c>, <c>"#rgb"</c>): a colour that code needs comes from a token or from a look object of the chrome layer. The test also fails
	/// when a file listed as an exception no longer has a literal, so a list can only shrink.
	/// </summary>
	public class NoLiteralColorsTests
	{
		/// <summary>
		/// The MVP files with literal colours, removed by the task named next to them. Empty since RV5: the literals of the grid cells, the reader window and
		/// the overlay moved into <c>Themes/Controls/CrMvpLook.axaml</c> (where P5.R2 and P5.R3 replace them with tokens).
		/// </summary>
		private static readonly Dictionary<string, string> KnownExceptions = new Dictionary<string, string>(StringComparer.Ordinal)
		{
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

		/// <summary>What only the chrome layer (<c>Controls/</c>) may write: colours of the Skia canvas, the alpha of a draw and the short hex forms.</summary>
		private static readonly Regex[] CSharpOutsideChrome =
		{
			new Regex(@"\bnew\s+SKColor\s*\(", RegexOptions.Compiled),
			new Regex(@"\bSKColors\.\w+", RegexOptions.Compiled),
			new Regex(@"\bColor\.FromUInt32\b", RegexOptions.Compiled),
			new Regex("\"#[0-9A-Fa-f]{3,4}\"", RegexOptions.Compiled),
		};

		private static readonly Regex[] Xaml =
		{
			HexInXaml,
			new Regex(@"\b(Foreground|Background|BorderBrush|Fill|Stroke|Color)\s*=\s*""(?!\{|Transparent"")[A-Za-z]+""", RegexOptions.Compiled),
		};

		/// <summary>The literals in a source text (empty when clean); <paramref name="isXaml"/> picks the pattern set, <paramref name="chromeLayer"/> the files that may build Skia colours.</summary>
		public static IReadOnlyList<string> Find(string text, bool isXaml, bool chromeLayer = true)
		{
			string withoutComments = isXaml ? Regex.Replace(text, "<!--.*?-->", string.Empty, RegexOptions.Singleline) : Regex.Replace(Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"//[^\n]*", string.Empty);
			var found = new List<string>();
			IEnumerable<Regex> patterns = isXaml ? Xaml : (chromeLayer ? CSharp : CSharp.Concat(CSharpOutsideChrome));
			foreach (Regex pattern in patterns)
			{
				foreach (Match m in pattern.Matches(withoutComments))
				{
					found.Add(m.Value.Trim());
				}
			}
			return found;
		}

		/// <summary>Every source of the app except the themes and the native pieces; <c>Controls/</c> is the chrome layer that may build Skia colours.</summary>
		private static IEnumerable<string> Files()
		{
			string app = Path.Combine(OriginalColors.RepoRoot, "mac", "ComicRack.Mac");
			foreach (string f in Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories))
			{
				if (!f.EndsWith(".cs", StringComparison.Ordinal) && !f.EndsWith(".axaml", StringComparison.Ordinal))
				{
					continue;
				}
				string relative = Path.GetRelativePath(app, f).Replace('\\', '/');
				if (relative.StartsWith("Themes/", StringComparison.Ordinal) || relative.StartsWith("Platform/", StringComparison.Ordinal)
					|| relative.StartsWith("bin/", StringComparison.Ordinal) || relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("Assets/", StringComparison.Ordinal))
				{
					continue;
				}
				yield return f;
			}
		}

		[Fact]
		public void No_source_or_xaml_file_outside_the_themes_has_a_colour_literal_except_the_listed_MVP_files()
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
				IReadOnlyList<string> literals = Find(File.ReadAllText(file), file.EndsWith(".axaml", StringComparison.Ordinal), chromeLayer: relative.StartsWith("Controls/", StringComparison.Ordinal));
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
			// outside the chrome layer the Skia colours and the short forms count too, inside it they are the chrome's business
			Assert.NotEmpty(Find("canvas.Clear(new SKColor(0xDC, 0xDC, 0xDC));", false, chromeLayer: false));
			Assert.NotEmpty(Find("paint.Color = SKColors.White;", false, chromeLayer: false));
			Assert.NotEmpty(Find("var c = Color.FromUInt32(0xFF336699);", false, chromeLayer: false));
			Assert.NotEmpty(Find("var c = \"#abc\";", false, chromeLayer: false));
			Assert.Empty(Find("var paint = new SKPaint { Color = new SKColor(255, 255, 255, 128) };", false, chromeLayer: true));
			Assert.Empty(Find("var c = look.Tile; var n = \"#\" + 12;", false, chromeLayer: false));
		}
	}
}
