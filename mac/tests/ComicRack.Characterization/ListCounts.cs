using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ComicRack.Mac.Host.Library;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;

namespace ComicRack.Characterization
{
	/// <summary>One row of a list table: the tree of the library's lists with the count (and a digest of the members) of each.</summary>
	internal sealed record ListRow(int Depth, string Name, ListKind Kind, int Count, string Members, bool RequiresScripts, string Error);

	internal static class ListCounts
	{
		/// <summary>
		/// Every list of the database, depth first, evaluated the way the sidebar does (<see cref="ListEvaluator"/>: the stored caches are dropped and the
		/// Core computes the list). <paramref name="skip"/> leaves out lists whose result depends on the date of the run.
		/// </summary>
		public static List<ListRow> Evaluate(ComicDatabase db, Func<ComicListItem, bool> skip = null)
		{
			return Evaluate(db, new ListEvaluator(), skip);
		}

		/// <summary>As above with the given evaluator (<c>new ListEvaluator(useStoredCaches: true)</c> trusts the caches stored in the file).</summary>
		public static List<ListRow> Evaluate(ComicDatabase db, ListEvaluator evaluator, Func<ComicListItem, bool> skip = null)
		{
			var rows = new List<ListRow>();
			foreach (ListNode root in LibraryLists.BuildTree(db))
			{
				Add(root, 0, evaluator, skip, rows);
			}
			return rows;
		}

		private static void Add(ListNode node, int depth, ListEvaluator evaluator, Func<ComicListItem, bool> skip, List<ListRow> rows)
		{
			if (skip == null || !skip(node.Item))
			{
				ListResult result = evaluator.Evaluate(node.Item);
				rows.Add(new ListRow(depth, node.Name, node.Kind, result.Count, Digest(result.Books), result.RequiresScripts, result.Error));
			}
			foreach (ListNode child in node.Children)
			{
				Add(child, depth + 1, evaluator, skip, rows);
			}
		}

		/// <summary>The first 8 hex digits of the SHA-256 of the sorted book ids: equal members give an equal digest, whatever the order.</summary>
		public static string Digest(IEnumerable<ComicBook> books)
		{
			string joined = string.Join("\n", books.Select(b => b.Id.ToString("D")).OrderBy(x => x, StringComparer.Ordinal));
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined))).Substring(0, 8).ToLowerInvariant();
		}

		/// <summary>One line per list: indentation by depth, then name, kind, count, member digest (tab separated).</summary>
		public static string Format(IEnumerable<ListRow> rows)
		{
			var sb = new StringBuilder();
			sb.Append("# list\tkind\tcount\tmembers (sha256 of the sorted ids, first 8 digits)\n");
			foreach (ListRow r in rows)
			{
				string note = r.RequiresScripts ? "\trequires scripts" : r.Error != null ? "\terror: " + r.Error : string.Empty;
				sb.Append(new string(' ', r.Depth * 2)).Append(r.Name).Append('\t').Append(r.Kind).Append('\t').Append(r.Count).Append('\t').Append(r.Members).Append(note).Append('\n');
			}
			return sb.ToString();
		}
	}
}
