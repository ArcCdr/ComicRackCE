using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// Reads the colour definitions of the WinForms original (never edited) with regular expressions, so that the theme tokens of the Mac
	/// app are compared with the source and not with a copy of it. The source root can be redirected with the environment variable
	/// <see cref="RootVariable"/> (the mutation check of P5.T1 points it at a folder with a changed copy of <c>ThemeColorTable.cs</c>):
	/// a file is looked for there first (by its path, then by its name) and in the repository otherwise.
	/// </summary>
	public static class OriginalColors
	{
		public const string RootVariable = "CRMAC_THEME_SOURCE_ROOT";

		public const string KnownColorTable = "cYo.Common/Drawing/ExtendedColors/KnownColorTableEx.cs";
		public const string ThemeColorTable = "cYo.Common.Windows/Forms/Theme/Internal/ThemeColorTable.cs";
		public const string DarkThemeColorTable = "cYo.Common.Windows/Forms/Theme/DarkMode/DarkThemeColorTable.cs";
		public const string DarkColors = "cYo.Common.Windows/Forms/Theme/DarkMode/Resources/DarkColors.cs";
		public const string StyledRenderer = "cYo.Common.Windows/Forms/StyledRenderer.cs";
		public const string DisplayWorkspace = "ComicRack/Config/DisplayWorkspace.cs";

		/// <summary>The repository root (the folder that contains <c>cYo.Common.Windows</c> and <c>mac</c>).</summary>
		public static readonly string RepoRoot = FindRepoRoot();

		private static string FindRepoRoot()
		{
			string dir = AppContext.BaseDirectory;
			while (!string.IsNullOrEmpty(dir))
			{
				if (Directory.Exists(Path.Combine(dir, "cYo.Common.Windows")) && Directory.Exists(Path.Combine(dir, "mac")))
				{
					return dir;
				}
				dir = Path.GetDirectoryName(dir);
			}
			throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory + ".");
		}

		/// <summary>The text of an original file, from the redirected root if it has the file.</summary>
		public static string Read(string relative)
		{
			string redirect = Environment.GetEnvironmentVariable(RootVariable);
			if (!string.IsNullOrEmpty(redirect))
			{
				foreach (string candidate in new[] { Path.Combine(redirect, relative), Path.Combine(redirect, Path.GetFileName(relative)) })
				{
					if (File.Exists(candidate))
					{
						return File.ReadAllText(candidate);
					}
				}
			}
			return File.ReadAllText(Path.Combine(RepoRoot, relative));
		}

		/// <summary>True if the source root is redirected (the tests then expect to find a difference).</summary>
		public static bool Redirected => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(RootVariable));

		/// <summary>Standard colour names of System.Drawing.Color that the original uses (X11 values).</summary>
		private static readonly Dictionary<string, uint> Named = new Dictionary<string, uint>
		{
			["Black"] = 0xFF000000, ["White"] = 0xFFFFFFFF, ["Gray"] = 0xFF808080, ["LightGray"] = 0xFFD3D3D3, ["DarkBlue"] = 0xFF00008B,
			["LightSkyBlue"] = 0xFF87CEFA, ["Green"] = 0xFF008000, ["Orange"] = 0xFFFFA500, ["Red"] = 0xFFFF0000, ["Blue"] = 0xFF0000FF,
			["DarkRed"] = 0xFF8B0000, ["SteelBlue"] = 0xFF4682B4, ["LightBlue"] = 0xFFADD8E6, ["MediumOrchid"] = 0xFFBA55D3,
			["WhiteSmoke"] = 0xFFF5F5F5, ["Gainsboro"] = 0xFFDCDCDC, ["Transparent"] = 0x00FFFFFF,
		};

		/// <summary>The Windows 10 system colours (comment column "existing") and the dark set (the array values) of KnownColorTableEx.</summary>
		public static (Dictionary<string, uint> Light, Dictionary<string, uint> Dark) SystemPalettes()
		{
			var light = new Dictionary<string, uint>();
			var dark = new Dictionary<string, uint>();
			string pending = null;
			foreach (string line in Read(KnownColorTable).Split('\n'))
			{
				Match m = Regex.Match(line, @"^\s*(//)?\s*0[xX]([0-9A-Fa-f]{8}),\s*//\s*([0-9A-Fa-f]{8}) - [0-9A-Fa-f]{8}: (\w+)");
				if (m.Success)
				{
					string name = m.Groups[4].Value;
					light[name] = Hex(m.Groups[3].Value);
					if (m.Groups[1].Success)
					{
						pending = name; // a commented-out entry: the array line that follows holds the dark value
					}
					else
					{
						dark[name] = Hex(m.Groups[2].Value);
					}
					continue;
				}
				m = Regex.Match(line, @"^\s*0[xX]([0-9A-Fa-f]{8}),\s*//\s*see remarks");
				if (m.Success && pending != null)
				{
					dark[pending] = Hex(m.Groups[1].Value);
					pending = null;
				}
			}
			return (light, dark);
		}

		private static uint Hex(string text) => uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

		/// <summary>The property bodies of a ThemeColorTable class: name to expression (commented-out lines are skipped).</summary>
		public static Dictionary<string, string> TableProperties(string relative)
		{
			var result = new Dictionary<string, string>();
			foreach (Match m in Regex.Matches(Read(relative), @"^\s*public\s+(?:virtual|override)\s+Color\s+(\w+)\s*=>\s*(.+?);", RegexOptions.Multiline))
			{
				result[m.Groups[1].Value] = m.Groups[2].Value.Trim();
			}
			return result;
		}

		/// <summary>The static Color fields of DarkColors.cs, keyed "Class.Field" (or "Field" at the top level), before the brushes class.</summary>
		public static Dictionary<string, string> DarkColorFields()
		{
			string text = Read(DarkColors);
			int brushes = text.IndexOf("class DarkBrushes", StringComparison.Ordinal);
			if (brushes > 0)
			{
				text = text.Substring(0, brushes);
			}
			var fields = new Dictionary<string, string>();
			var stack = new List<(string Name, int Depth)>();
			int depth = 0;
			string pendingClass = null;
			foreach (string line in text.Split('\n'))
			{
				string code = line.Trim();
				if (code.StartsWith("//", StringComparison.Ordinal))
				{
					continue;
				}
				Match cls = Regex.Match(code, @"\bclass\s+(\w+)");
				if (cls.Success)
				{
					pendingClass = cls.Groups[1].Value;
				}
				Match field = Regex.Match(code, @"\bstatic\s+(?:readonly\s+)?Color\s+(\w+)\s*=\s*(.+?);");
				if (field.Success)
				{
					string scope = string.Join(".", stack.Select(s => s.Name).Skip(1));
					fields[scope.Length == 0 ? field.Groups[1].Value : scope + "." + field.Groups[1].Value] = field.Groups[2].Value.Trim();
				}
				foreach (char c in code)
				{
					if (c == '{')
					{
						depth++;
						if (pendingClass != null)
						{
							stack.Add((pendingClass, depth));
							pendingClass = null;
						}
					}
					else if (c == '}')
					{
						if (stack.Count > 0 && stack[stack.Count - 1].Depth == depth)
						{
							stack.RemoveAt(stack.Count - 1);
						}
						depth--;
					}
				}
			}
			return fields;
		}

		/// <summary>Evaluates colour expressions of the original sources against the light or the dark system palette.</summary>
		public sealed class Evaluator
		{
			private readonly Dictionary<string, uint> palette;
			private readonly Dictionary<string, string> darkFields;

			public Evaluator(bool dark)
			{
				(Dictionary<string, uint> light, Dictionary<string, uint> darkSet) = SystemPalettes();
				palette = dark ? darkSet : light;
				darkFields = DarkColorFields();
			}

			public uint SystemColor(string name) => palette[name];

			/// <summary>Evaluates an expression, ARGB. Throws for a form the original does not use yet, so a new one is noticed.</summary>
			public uint Eval(string expression)
			{
				string e = expression.Trim();
				Match m = Regex.Match(e, @"^(?:System\.Drawing\.)?Color\.FromArgb\((.*)\)$");
				if (m.Success)
				{
					string[] args = SplitArguments(m.Groups[1].Value);
					switch (args.Length)
					{
						case 2:
							return ((uint)Number(args[0]) << 24) | (Eval(args[1]) & 0x00FFFFFF);
						case 3:
							return 0xFF000000 | ((uint)Number(args[0]) << 16) | ((uint)Number(args[1]) << 8) | (uint)Number(args[2]);
						case 4:
							return ((uint)Number(args[0]) << 24) | ((uint)Number(args[1]) << 16) | ((uint)Number(args[2]) << 8) | (uint)Number(args[3]);
					}
					throw new FormatException("Color.FromArgb with " + args.Length + " arguments: " + e);
				}
				m = Regex.Match(e, @"^(?:System\.Drawing\.)?SystemColors\.(\w+)$");
				if (m.Success)
				{
					return palette[m.Groups[1].Value];
				}
				if (e == "Control.DefaultForeColor")
				{
					return palette["ControlText"];
				}
				m = Regex.Match(e, @"^(?:System\.Drawing\.)?Color\.(\w+)$");
				if (m.Success && Named.TryGetValue(m.Groups[1].Value, out uint named))
				{
					return named;
				}
				string key = e.StartsWith("DarkColors.", StringComparison.Ordinal) ? e.Substring("DarkColors.".Length) : e;
				if (darkFields.TryGetValue(key, out string field))
				{
					return Eval(field);
				}
				throw new FormatException("Unknown colour expression in the original sources: " + e);
			}

			private static int Number(string text)
			{
				string t = text.Trim();
				return t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
					? int.Parse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
					: int.Parse(t, CultureInfo.InvariantCulture);
			}

			private static string[] SplitArguments(string text)
			{
				var parts = new List<string>();
				int depth = 0;
				int start = 0;
				for (int i = 0; i < text.Length; i++)
				{
					if (text[i] == '(')
					{
						depth++;
					}
					else if (text[i] == ')')
					{
						depth--;
					}
					else if (text[i] == ',' && depth == 0)
					{
						parts.Add(text.Substring(start, i - start));
						start = i + 1;
					}
				}
				parts.Add(text.Substring(start));
				return parts.ToArray();
			}
		}
	}
}
