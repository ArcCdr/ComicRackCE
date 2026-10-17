using System;
using System.Collections.Generic;

namespace ComicRack.Mac.Host.Library
{
	/// <summary>
	/// One path-prefix replacement of a Windows library import, for example <c>S:\media\BD\</c> to <c>/Users/me/Comics/BD/</c>.
	/// The prefix matches case-insensitively, with or without a trailing separator, and only on whole path segments
	/// (<c>S:\media\BD</c> does not match <c>S:\media\BDX\a.cbz</c>). The rest of the path keeps its order, its backslashes
	/// become <c>/</c>.
	/// </summary>
	public sealed class PathRemapRule
	{
		public string From { get; }

		public string To { get; }

		public PathRemapRule(string from, string to)
		{
			if (string.IsNullOrWhiteSpace(from))
			{
				throw new ArgumentException("The source prefix is empty.", nameof(from));
			}
			if (string.IsNullOrWhiteSpace(to))
			{
				throw new ArgumentException("The target prefix is empty.", nameof(to));
			}
			From = from.Trim();
			To = to.Trim();
		}

		public override string ToString() => From + " => " + To;

		/// <summary>Parses <c>from=to</c> (split at the first '='). Throws <see cref="FormatException"/> with a message for the user.</summary>
		public static PathRemapRule Parse(string text)
		{
			int i = text == null ? -1 : text.IndexOf('=');
			if (i <= 0 || i == text.Length - 1)
			{
				throw new FormatException("A mapping must look like  S:\\media\\BD\\=/Users/me/Comics/BD/  (got: " + text + ")");
			}
			try
			{
				return new PathRemapRule(text.Substring(0, i), text.Substring(i + 1));
			}
			catch (ArgumentException ex)
			{
				throw new FormatException(ex.Message + " (got: " + text + ")");
			}
		}

		/// <summary>Parses one mapping per line; empty lines and lines starting with '#' are skipped.</summary>
		public static List<PathRemapRule> ParseLines(string text)
		{
			var rules = new List<PathRemapRule>();
			foreach (string raw in (text ?? string.Empty).Split('\n'))
			{
				string line = raw.Trim();
				if (line.Length == 0 || line[0] == '#')
				{
					continue;
				}
				rules.Add(Parse(line));
			}
			return rules;
		}

		/// <summary>The mapped path, or null if <paramref name="path"/> is outside the prefix.</summary>
		public string Map(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}
			string fromBare = From.TrimEnd('\\', '/');
			string toBare = To.TrimEnd('\\', '/');
			if (path.Equals(fromBare, StringComparison.OrdinalIgnoreCase))
			{
				return toBare.Length == 0 ? "/" : toBare; // an empty bare target is the root
			}
			if (path.Length > fromBare.Length && path.StartsWith(fromBare, StringComparison.OrdinalIgnoreCase) && (path[fromBare.Length] == '\\' || path[fromBare.Length] == '/'))
			{
				return toBare + "/" + path.Substring(fromBare.Length + 1).Replace('\\', '/');
			}
			return null;
		}

		/// <summary>The first rule that maps <paramref name="path"/> wins; null if none does.</summary>
		public static string MapFirst(IReadOnlyList<PathRemapRule> rules, string path)
		{
			for (int i = 0; i < rules.Count; i++)
			{
				string mapped = rules[i].Map(path);
				if (mapped != null)
				{
					return mapped;
				}
			}
			return null;
		}
	}
}
