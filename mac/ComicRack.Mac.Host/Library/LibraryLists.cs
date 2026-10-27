using System;
using System.Collections.Generic;
using System.Linq;
using ComicRack.Mac.Host.Scripting;
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
		public ListNode(ComicListItem item, ListKind kind, bool requiresScripts, IReadOnlyList<ListNode> children, bool scriptsDisabled = false, IReadOnlyList<string> missingScripts = null)
		{
			Item = item;
			Kind = kind;
			RequiresScripts = requiresScripts;
			Children = children;
			ScriptsDisabled = requiresScripts && scriptsDisabled;
			MissingScripts = missingScripts ?? Array.Empty<string>();
		}

		public ComicListItem Item { get; }

		public Guid Id => Item.Id;

		public string Name => Item.Name ?? string.Empty;

		public ListKind Kind { get; }

		/// <summary>True if the list needs a script (<see cref="ComicBookPluginMatcher"/>) that cannot run: scripting is off (<see cref="ScriptsDisabled"/>) or a script is not there (<see cref="MissingScripts"/>).</summary>
		public bool RequiresScripts { get; }

		/// <summary>True if <see cref="RequiresScripts"/> is because scripting is switched off (or no script engine was given).</summary>
		public bool ScriptsDisabled { get; }

		/// <summary>The keys of the script lists (<c>CreateBookList</c> commands) this list uses that no enabled command has; empty when scripting is off.</summary>
		public IReadOnlyList<string> MissingScripts { get; }

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
	/// <b>Lists that need scripts.</b> A <see cref="ComicBookPluginMatcher"/> asks the plugin engine for its command. When the engine does not have it
	/// (scripting is off, so <c>ComicBookPluginMatcher.PluginEngine</c> is null, or no enabled <c>CreateBookList</c> command has the matcher's key) the matcher's
	/// <c>Match</c> returns <b>all</b> books it was given (not none). Evaluating such a list would therefore show a wrong, large result,
	/// so <see cref="ListEvaluator.Evaluate"/> does not call the Core for it: <see cref="RequiresScripts(ComicListItem, ScriptHost)"/> looks at the matchers (also those inside
	/// group matchers) and at the base list chain and the list gets an empty result with the flag set. With a running <see cref="ScriptHost"/> (P4.3) a list whose
	/// scripts are all there is evaluated like any other.
	/// </summary>
	public static class LibraryLists
	{
		private const int MaxBaseListDepth = 16;

		/// <summary>The top level lists of the database with their children. The empty "Temporary Lists" folder is left out.</summary>
		public static IReadOnlyList<ListNode> BuildTree(ComicDatabase database, ScriptHost scripts = null)
		{
			if (database == null)
			{
				throw new ArgumentNullException(nameof(database));
			}
			var roots = new List<ListNode>();
			foreach (ComicListItem item in database.ComicLists.ToArray())
			{
				ListNode n = Build(item, scripts);
				if (n != null)
				{
					roots.Add(n);
				}
			}
			return roots;
		}

		private static ListNode Build(ComicListItem item, ScriptHost scripts)
		{
			if (item is ComicListItemFolder folder)
			{
				var children = new List<ListNode>();
				foreach (ComicListItem child in folder.Items.ToArray())
				{
					ListNode n = Build(child, scripts);
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
			bool uses = RequiresScripts(item);
			IReadOnlyList<string> missing = uses ? MissingScripts(item, scripts) : Array.Empty<string>();
			bool blocked = uses && (scripts == null || !scripts.Enabled || missing.Count > 0);
			return new ListNode(item, kind, blocked, Array.Empty<ListNode>(), scriptsDisabled: scripts == null || !scripts.Enabled, missing);
		}

		/// <summary>True if the list is a smart list with a plugin matcher, or has a base list that needs scripts.</summary>
		public static bool RequiresScripts(ComicListItem item)
		{
			return RequiresScripts(item, 0);
		}

		/// <summary>
		/// True if the list uses scripts (<see cref="RequiresScripts(ComicListItem)"/>) and they cannot run: <paramref name="scripts"/> is null or has scripting off, or
		/// one of the scripts the list names is not there (<see cref="MissingScripts"/>).
		/// </summary>
		public static bool RequiresScripts(ComicListItem item, ScriptHost scripts)
		{
			return RequiresScripts(item) && (scripts == null || !scripts.Enabled || MissingScripts(item, scripts).Count > 0);
		}

		/// <summary>The keys of the script lists that the list (and its base lists) name and that no enabled <c>CreateBookList</c> command of <paramref name="scripts"/> has. Empty when scripting is off.</summary>
		public static IReadOnlyList<string> MissingScripts(ComicListItem item, ScriptHost scripts)
		{
			if (scripts == null || !scripts.Enabled)
			{
				return Array.Empty<string>();
			}
			return PluginKeys(item, 0).Distinct(StringComparer.Ordinal).Where(key => !scripts.HasListCommand(key)).ToArray();
		}

		private static IEnumerable<string> PluginKeys(ComicListItem item, int depth)
		{
			if (!(item is ComicSmartListItem smart) || depth > MaxBaseListDepth)
			{
				yield break;
			}
			foreach (ComicBookPluginMatcher m in smart.Matchers.Recurse<ComicBookMatcher>(o => o is ComicBookGroupMatcher group ? group.Matchers : null).OfType<ComicBookPluginMatcher>())
			{
				// a matcher whose operator is "None" has no key and returns the books it is given: it needs no script
				if (!string.IsNullOrEmpty(m.PluginKey))
				{
					yield return m.PluginKey;
				}
			}
			if (smart.BaseListId != Guid.Empty)
			{
				ComicListItem baseList = smart.GetBaseList();
				if (baseList != null)
				{
					foreach (string key in PluginKeys(baseList, depth + 1))
					{
						yield return key;
					}
				}
			}
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
		private readonly ScriptHost scripts;

		/// <param name="useStoredCaches">Trust the query caches stored in the file instead of computing each list once.</param>
		/// <param name="scripts">The script engine of the open host (<see cref="MacHost.Scripts"/>): lists whose scripts are all there are evaluated. Null: every list with a script matcher is "Requires scripts".</param>
		public ListEvaluator(bool useStoredCaches = false, ScriptHost scripts = null)
		{
			this.useStoredCaches = useStoredCaches;
			this.scripts = scripts;
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
			if (LibraryLists.RequiresScripts(item, scripts))
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
			// A list whose scripts cannot run keeps the result stored in the file: the Core cannot compute it here.
			if (!LibraryLists.RequiresScripts(item, scripts))
			{
				item.ResetCache();
			}
		}
	}
}
