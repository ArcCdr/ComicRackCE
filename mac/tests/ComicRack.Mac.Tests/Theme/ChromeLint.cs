using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// The scanner behind the chrome architecture tests (RV5, rule R10, <c>mac/plans/chrome-architecture.md</c> section 6). The app is split into
	/// the chrome layer (<c>Themes/</c>, <c>Controls/</c>, <c>Platform/</c>) and everything else, the view layer (<c>Views/</c>, <c>ViewModels/</c>,
	/// <c>Browser/</c>, <c>Reader/</c>, <c>Services/</c>, <c>Diagnostics/</c> and the top level files). The view layer may not contain the look
	/// of anything: no fonts, no corner radii, opacities or border widths, no inline styles, templates or themes, no way to load an image of its
	/// own. Colour literals are the job of <see cref="NoLiteralColorsTests"/>.
	/// </summary>
	internal static class ChromeLint
	{
		/// <summary>The files of the view layer that are chrome showcases and so may use chrome freely, with the reason.</summary>
		public static readonly IReadOnlyDictionary<string, string> Showcases = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["Views/ChromeGalleryWindow.axaml"] = "the gallery shows every chrome control in every state: it is the chrome's own test card",
			["Views/ChromeGalleryWindow.axaml.cs"] = "the gallery shows every chrome control in every state: it is the chrome's own test card",
		};

		/// <summary>
		/// A finding that is allowed in one file, with the reason: <c>file -> (text of the finding, why)</c>. Keep this list short; every entry is a
		/// decision that the view layer needs something that looks like chrome but is not.
		/// </summary>
		public static readonly IReadOnlyDictionary<string, (string Finding, string Reason)> Allowed = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
		{
			["Views/BookGridView.cs"] = ("new FuncDataTemplate", "the ItemsRepeater needs an element factory for its virtualization; it only creates a BookCell (a CrThumbnailItem), which takes its look from the chrome layer"),
		};

		private static readonly string[] ChromeFolders = { "Themes/", "Controls/", "Platform/", "Assets/", "bin/", "obj/" };

		private static readonly string[] ApplicationFiles = { "App.axaml", "App.axaml.cs", "Program.cs" };

		public sealed class Rule
		{
			public Rule(string name, Regex csharp, Regex xaml)
			{
				Name = name;
				CSharp = csharp;
				Xaml = xaml;
			}

			public string Name { get; }

			public Regex CSharp { get; }

			public Regex Xaml { get; }
		}

		private static Regex R(string pattern) => new Regex(pattern, RegexOptions.Compiled);

		public static readonly Rule[] Rules =
		{
			new Rule("font: use a cr-* text class or ChromeFonts",
				R(@"\b(FontFamily|FontSize|FontWeight|FontStyle|FontStretch)\s*=[^=]|new\s+(FontFamily|Typeface)\s*\(|\bFontWeight\.\w+"),
				R(@"\b(FontFamily|FontSize|FontWeight|FontStyle)\s*=\s*""")),
			new Rule("shape: corner radius, border width, opacity and shadows belong to a theme",
				R(@"\b(CornerRadius|BoxShadow|BoxShadows|Opacity|BorderThickness)\s*=[^=]|new\s+(CornerRadius|BoxShadows?)\s*\("),
				R(@"\b(CornerRadius|BoxShadow|Opacity|BorderThickness|Effect)\s*=\s*""")),
			new Rule("inline style, template or theme: put it in Themes/ and use a class or a Cr control",
				R(@"\bnew\s+(Style|ControlTheme|ControlTemplate|Setter|DataTemplate|FuncDataTemplate|FuncTreeDataTemplate|FuncControlTemplate)\b|\.Styles\s*\.\s*Add\b"),
				R(@"<\s*(\w+\.)?(Styles|Style|ControlTemplate|DataTemplate|TreeDataTemplate|ControlTheme)\b")),
			new Rule("image loading: icons only through Icons.Get or CrIcon",
				R(@"\bAssetLoader\b|avares://|new\s+Bitmap\s*\(|""[^""]*\.(png|gif|bmp|ico|jpg|jpeg|svg)"""),
				R(@"avares://|""[^""<>]*\.(png|gif|bmp|ico|jpg|jpeg|svg)""")),
		};

		/// <summary>Namespaces that a view (a file of <c>Views/</c>) may not name: the means to draw, style or load things of its own.</summary>
		public static readonly string[] ForbiddenInViews =
		{
			"Avalonia.Styling", "Avalonia.Markup.*", "Avalonia.Skia", "Avalonia.Media.Imaging", "Avalonia.Rendering.*", "Avalonia.Platform", "SkiaSharp",
			"ComicRack.Mac.Platform",
		};

		/// <summary>Namespaces that a view model may not name: nothing that draws, styles or lives in a window. A name ending in <c>.*</c> also forbids its children.</summary>
		public static readonly string[] ForbiddenInViewModels =
		{
			"Avalonia.Controls.*", "Avalonia.Media.*", "Avalonia.Styling", "Avalonia.Markup.*", "Avalonia.Skia", "Avalonia.Rendering.*", "Avalonia.Platform.*", "SkiaSharp",
			"ComicRack.Mac.Controls", "ComicRack.Mac.Themes", "ComicRack.Mac.Views", "ComicRack.Mac.Platform",
		};

		public static string AppRoot => Path.Combine(OriginalColors.RepoRoot, "mac", "ComicRack.Mac");

		public static string Relative(string file) => Path.GetRelativePath(AppRoot, file).Replace('\\', '/');

		/// <summary>The sources of the view layer: every <c>.cs</c> and <c>.axaml</c> outside the chrome folders and the application files.</summary>
		public static IEnumerable<string> ViewLayerFiles()
		{
			foreach (string file in Directory.EnumerateFiles(AppRoot, "*", SearchOption.AllDirectories))
			{
				if (!file.EndsWith(".cs", StringComparison.Ordinal) && !file.EndsWith(".axaml", StringComparison.Ordinal))
				{
					continue;
				}
				string relative = Relative(file);
				if (ChromeFolders.Any(f => relative.StartsWith(f, StringComparison.Ordinal)) || ApplicationFiles.Contains(relative) || Showcases.ContainsKey(relative))
				{
					continue;
				}
				yield return file;
			}
		}

		public static string StripComments(string text, bool isXaml)
		{
			return isXaml
				? Regex.Replace(text, "<!--.*?-->", string.Empty, RegexOptions.Singleline)
				: Regex.Replace(Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"(?<!:)//[^\n]*", string.Empty);
		}

		/// <summary>The violations of the rules in a source text (empty when clean).</summary>
		public static IReadOnlyList<string> Find(string text, bool isXaml)
		{
			string code = StripComments(text, isXaml);
			var found = new List<string>();
			foreach (Rule rule in Rules)
			{
				foreach (Match m in (isXaml ? rule.Xaml : rule.CSharp).Matches(code))
				{
					found.Add(rule.Name.Split(':')[0] + " (" + m.Value.Trim() + ")");
				}
			}
			return found;
		}

		/// <summary>The forbidden namespaces that a C# text names in a <c>using</c> directive.</summary>
		public static IReadOnlyList<string> ForbiddenUsings(string text, IEnumerable<string> forbidden)
		{
			var found = new List<string>();
			foreach (Match m in Regex.Matches(StripComments(text, false), @"^\s*using\s+(static\s+)?([\w\.]+)\s*;", RegexOptions.Multiline))
			{
				string ns = m.Groups[2].Value;
				if (forbidden.Any(f => f.EndsWith(".*", StringComparison.Ordinal) ? (ns == f.Substring(0, f.Length - 2) || ns.StartsWith(f.Substring(0, f.Length - 1), StringComparison.Ordinal)) : ns == f))
				{
					found.Add(ns);
				}
			}
			return found;
		}

		/// <summary>Every offender of the view layer, as <c>file: what</c> lines.</summary>
		public static List<string> Offenders()
		{
			var offenders = new List<string>();
			foreach (string file in ViewLayerFiles())
			{
				string relative = Relative(file);
				bool isXaml = file.EndsWith(".axaml", StringComparison.Ordinal);
				string text = File.ReadAllText(file);
				foreach (string finding in Find(text, isXaml).Distinct().Take(4))
				{
					if (Allowed.TryGetValue(relative, out var allowed) && finding.Contains(allowed.Finding))
					{
						continue;
					}
					offenders.Add(relative + ": " + finding);
				}
				if (!isXaml)
				{
					if (relative.StartsWith("Views/", StringComparison.Ordinal))
					{
						offenders.AddRange(ForbiddenUsings(text, ForbiddenInViews).Select(ns => relative + ": using " + ns));
					}
					else if (relative.StartsWith("ViewModels/", StringComparison.Ordinal))
					{
						offenders.AddRange(ForbiddenUsings(text, ForbiddenInViewModels).Select(ns => relative + ": using " + ns));
					}
				}
			}
			return offenders;
		}
	}
}
