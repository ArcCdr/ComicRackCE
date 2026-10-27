using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ComicRack.Mac.Host.Scripting.Scan
{
	/// <summary>
	/// The planning data of the gap report (P4.17), kept in a text file so that the code stays generic: which task implements a type or member that is still a stub or missing (<c>owner</c> lines), and facts about
	/// members that exist on .NET 10 but behave differently on macOS (<c>note</c> lines). A line is <c>owner &lt;pattern&gt; &lt;task&gt;</c> or <c>note &lt;pattern&gt; &lt;differs|-&gt; &lt;text&gt;</c>; a line that starts with <c>#</c> is a comment.
	/// A pattern is a CLR type name (every member and nested type), <c>Type::Member</c>, or a prefix of the string <c>Type::Member</c> ending in <c>*</c>. The first line that matches wins, so specific lines come first.
	/// </summary>
	public sealed class GapRules
	{
		private sealed class Rule
		{
			public string Kind;
			public string Pattern;
			public string Value;
			public string Status;
		}

		private readonly List<Rule> rules = new List<Rule>();

		public static GapRules Empty { get; } = new GapRules();

		public int Count => rules.Count;

		public static GapRules Load(string file) => Parse(File.ReadAllLines(file));

		public static GapRules Parse(IEnumerable<string> lines)
		{
			var result = new GapRules();
			int number = 0;
			foreach (string raw in lines)
			{
				number++;
				string line = raw.Trim();
				if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
				{
					continue;
				}
				string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
				if (parts[0] == "owner" && parts.Length == 3)
				{
					result.rules.Add(new Rule { Kind = "owner", Pattern = parts[1], Value = parts[2] });
				}
				else if (parts[0] == "note" && parts.Length >= 4 && (parts[2] == "differs" || parts[2] == "-"))
				{
					string text = line.Substring(line.IndexOf(parts[2], line.IndexOf(parts[1], StringComparison.Ordinal) + parts[1].Length, StringComparison.Ordinal) + parts[2].Length).Trim();
					result.rules.Add(new Rule { Kind = "note", Pattern = parts[1], Status = parts[2], Value = text });
				}
				else
				{
					throw new FormatException("Line " + number + " of the gap rules is not `owner <pattern> <task>` or `note <pattern> <differs|-> <text>`: " + raw);
				}
			}
			return result;
		}

		/// <summary>The task that owns the type or member, or null.</summary>
		public string OwnerOf(string type, string member) => rules.FirstOrDefault(r => r.Kind == "owner" && Matches(r.Pattern, type, member))?.Value;

		/// <summary>The note for the type or member (and the status it forces, <c>differs</c> or null), or null.</summary>
		public string NoteOf(string type, string member, out string status)
		{
			Rule r = rules.FirstOrDefault(x => x.Kind == "note" && Matches(x.Pattern, type, member));
			status = r != null && r.Status == "differs" ? "differs" : null;
			return r?.Value;
		}

		/// <summary>The task ids the rules name.</summary>
		public IEnumerable<string> Tasks => rules.Where(r => r.Kind == "owner").Select(r => r.Value).Distinct(StringComparer.Ordinal);

		private static bool Matches(string pattern, string type, string member)
		{
			string key = type + "::" + (member ?? string.Empty);
			if (pattern.EndsWith("*", StringComparison.Ordinal))
			{
				return key.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.Ordinal);
			}
			if (pattern.Contains("::"))
			{
				return string.Equals(key, pattern, StringComparison.Ordinal);
			}
			return string.Equals(type, pattern, StringComparison.Ordinal) || type.StartsWith(pattern + "+", StringComparison.Ordinal);
		}
	}
}
