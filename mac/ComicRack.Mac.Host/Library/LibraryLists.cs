using System;
using System.Collections.Generic;
using System.Linq;
using cYo.Common.Collections;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using cYo.Projects.ComicRack.Plugins;

namespace ComicRack.Mac.Host.Library
{
	public enum ListKind
	{
		/// <summary>The library itself (all books).</summary>
		Library,

		/// <summary>A folder of lists. The Core combines its children (union, or intersection by <c>CombineMode</c>).</summary>
		Folder,

		/// <summary>A smart list (matchers, optional base list and limit).</summary>
		Smart,

		/// <summary>A list of explicit books (<c>ComicIdListItem</c>).</summary>
		Fixed,

		/// <summary>Any other list type of the Core.</summary>
		Other,
	}

	/// <summary>One entry of the list tree: the Core's list item plus what the sidebar needs to show it.</summary>
	public sealed class ListNode
	{
		public ListNode(ComicListItem item, ListKind kind, bool requiresScripts, IReadOnlyList<ListNode> children)
		{
			Item = item;
			Kind = kind;
			RequiresScripts = requiresScripts;
			Children = children;
		}

		public ComicListItem Item { get; }

		public Guid Id => Item.Id;

		public string Name => Item.Name ?? string.Empty;

		public ListKind Kind { get; }

		/// <summary>True if evaluating the list needs a script (<see cref="ComicBookPluginMatcher"/>); the MVP has no scripting.</summary>
		public bool RequiresScripts { get; }

		public IReadOnlyList<ListNode> Children { get; }

		/// <summary>This node and all below it, depth first.</summary>
		public IEnumerable<ListNode> Flatten()
		{
			yield return this;
			foreach (ListNode child in Children)
			{
				foreach (ListNode n in child.Flatten())
				{
					yield return n;
				}
			}
		}
	}

	/// <summary>The books of one list. <see cref="Books"/> is empty if the list needs scripts or failed.</summary>
	public sealed class ListResult
	{
		public ComicBook[] Books { get; init; } = Array.Empty<ComicBook>();

		public bool RequiresScripts { get; init; }

		/// <summary>The message of the exception the evaluation threw, or null.</summary>
		public string Error { get; init; }

		public int Count => Books.Length;
	}

	/// <summary>
	/// The database's lists as the Mac app and crcli show them: a tree built from <c>ComicDatabase.ComicLists</c> and an evaluation that
	/// is done by <see cref="ListEvaluator"/> with the Core's own <c>ComicListItem.GetBooks()</c>, as the Windows browser does (it uses the list's query cache when the
	/// cache is on, which the host switches on). Read only: nothing here changes a list.
	///
	/// <b>Lists that need scripts.</b> A <see cref="ComicBookPluginMatcher"/> asks the plugin engine for its command. There is no plugin
	/// engine in the MVP (<c>ComicBookPluginMatcher.PluginEngine</c> stays null), the command lookup then fails inside the matcher and
	/// <c>Match</c> returns <b>all</b> books it was given (not none). Evaluating such a list would therefore show a wrong, large result,
	/// so <see cref="ListEvaluator.Evaluate"/> does not call the Core for it: <see cref="RequiresScripts"/> looks at the matchers (also those inside
	/// group matchers) and at the base list chain and the list gets an empty result with the flag set.
	/// </summary>
	public static class LibraryLists
	{
		private const int MaxBaseListDepth = 16;

		/// <summary>The top level lists of the database with their children. The empty "Temporary Lists" folder is left out.</summary>
		public static IReadOnlyList<ListNode> BuildTree(ComicDatabase database)
		{
			if (database == null)
			{
				throw new ArgumentNullException(nameof(database));
			}
			var roots = new List<ListNode>();
			foreach (ComicListItem item in database.ComicLists.ToArray())
			{
				ListNode n = Build(item);
				if (n != null)
				{
					roots.Add(n);
				}
			}
			return roots;
		}

		private static ListNode Build(ComicListItem item)
		{
			if (item is ComicListItemFolder folder)
			{
				var children = new List<ListNode>();
				foreach (ComicListItem child in folder.Items.ToArray())
				{
					ListNode n = Build(child);
					if (n != null)
					{
						children.Add(n);
					}
				}
				if (folder.Temporary && children.Count == 0)
				{
					return null;
				}
				return new ListNode(item, ListKind.Folder, requiresScripts: false, children);
			}
			ListKind kind = item switch
			{
				ComicLibraryListItem => ListKind.Library,
				ComicSmartListItem => ListKind.Smart,
				ComicIdListItem => ListKind.Fixed,
				_ => ListKind.Other,
			};
			return new ListNode(item, kind, RequiresScripts(item), Array.Empty<ListNode>());
		}

		/// <summary>True if the list is a smart list with a plugin matcher, or has a base list that needs scripts.</summary>
		public static bool RequiresScripts(ComicListItem item)
		{
			return RequiresScripts(item, 0);
		}

		private static bool RequiresScripts(ComicListItem item, int depth)
		{
			if (!(item is ComicSmartListItem smart) || depth > MaxBaseListDepth)
			{
				return false;
			}
			bool plugin = smart.Matchers
				.Recurse<ComicBookMatcher>(o => o is ComicBookGroupMatcher group ? group.Matchers : null)
				.Any(m => m is ComicBookPluginMatcher);
			if (plugin)
			{
				return true;
			}
			if (smart.BaseListId != Guid.Empty)
			{
				ComicListItem baseList = smart.GetBaseList();
				return baseList != null && RequiresScripts(baseList, depth + 1);
			}
			return false;
		}
	}

	/// <summary>
	/// Evaluates lists with the Core (<c>ComicListItem.GetBooks()</c>, the call the Windows browser makes) and keeps the sidebar and
	/// crcli on the same numbers. One instance per open database; thread safe (evaluations run one at a time).
	///
	/// <b>Stale query caches.</b> A ComicDb.xml stores each list's result (<c>CacheStorage</c>) and the Core trusts it when it loads
	/// the file. The fixture database was rewritten by other tooling, and several stored results no longer match its books ("Never Read"
	/// 14,245 stored, 14,192 evaluated; "Read" 461 and 514). The numbers also change with how the file was opened (a path remap after
	/// the load invalidates some caches, an import before the load does not). So by default the first evaluation of a list drops its
	/// cache (<c>ResetCache</c>, children and base lists first) and lets the Core compute it from the matchers; later evaluations use
	/// the cache the Core then keeps up to date. <c>useStoredCaches</c> turns that off (crcli <c>--cached</c>).
	/// </summary>
	public sealed class ListEvaluator
	{
		private readonly object gate = new object();
		private readonly HashSet<Guid> fresh = new HashSet<Guid>();
		private readonly bool useStoredCaches;

		public ListEvaluator(bool useStoredCaches = false)
		{
			this.useStoredCaches = useStoredCaches;
		}

		/// <summary>
		/// The books of a list. Can take a while (a smart list that is not cached walks the library): call it on a worker thread. A list
		/// that needs scripts is not evaluated (empty, <see cref="ListResult.RequiresScripts"/>). A failure of the Core does not escape:
		/// it is returned in <see cref="ListResult.Error"/> with no books.
		/// </summary>
		public ListResult Evaluate(ComicListItem item)
		{
			if (item == null)
			{
				throw new ArgumentNullException(nameof(item));
			}
			if (LibraryLists.RequiresScripts(item))
			{
				return new ListResult { RequiresScripts = true };
			}
			lock (gate)
			{
				try
				{
					if (!useStoredCaches)
					{
						EnsureFresh(item, 0);
					}
					return new ListResult { Books = item.GetBooks().Lock().ToArray() };
				}
				catch (Exception ex)
				{
					return new ListResult { Error = ex.GetType().Name + ": " + ex.Message };
				}
			}
		}

		private void EnsureFresh(ComicListItem item, int depth)
		{
			if (depth > 16 || !fresh.Add(item.Id))
			{
				return;
			}
			if (item is ComicListItemFolder folder)
			{
				foreach (ComicListItem child in folder.Items.ToArray())
				{
					EnsureFresh(child, depth + 1);
				}
			}
			else if (item is ComicSmartListItem smart && smart.BaseListId != Guid.Empty)
			{
				ComicListItem baseList = smart.GetBaseList();
				if (baseList != null)
				{
					EnsureFresh(baseList, depth + 1);
				}
			}
			// A list that needs scripts keeps the result stored in the file: the Core cannot compute it here.
			if (!LibraryLists.RequiresScripts(item))
			{
				item.ResetCache();
			}
		}
	}
}
