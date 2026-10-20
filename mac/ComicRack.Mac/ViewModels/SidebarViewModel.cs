using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Services;
using cYo.Common.Collections;
using cYo.Projects.ComicRack.Engine.Database;

namespace ComicRack.Mac.ViewModels
{
	/// <summary>One row of the sidebar tree: a list, a folder of lists or the library.</summary>
	public sealed partial class SidebarNode : ObservableObject
	{
		public const string ScriptsBadge = "Requires scripts (not in MVP)";

		public SidebarNode(ListNode node, IReadOnlyList<SidebarNode> children)
		{
			Node = node;
			Children = children;
			IsExpanded = node.Kind == ListKind.Folder;
		}

		public ListNode Node { get; }

		public string Name => Node.Name;

		public ListKind Kind => Node.Kind;

		public Guid Id => Node.Id;

		public IReadOnlyList<SidebarNode> Children { get; }

		public bool RequiresScripts => Node.RequiresScripts;

		/// <summary>
		/// The resource name of the row's image in the original: <c>ComicListItem.ImageKey</c> picks the entry of the tree's image list
		/// (<c>ComicListLibraryBrowser.OnLoad</c>: Library, Folder = SearchFolder, Search = SearchDocument, List, TempFolder).
		/// </summary>
		public string IconName => IconNameOf(Node.Item?.ImageKey);

		/// <summary>The image of a list item key (<c>ComicListItem.ImageKey</c>); an unknown key shows the plain list image.</summary>
		public static string IconNameOf(string imageKey)
		{
			switch (imageKey)
			{
				case "Library":
					return "Library";
				case "Folder":
					return "SearchFolder";
				case "Search":
					return "SearchDocument";
				case "TempFolder":
					return "TempFolder";
				default:
					return "List";
			}
		}

		/// <summary>The badge under the name of a list that needs scripts, or null.</summary>
		public string Badge => Node.RequiresScripts ? ScriptsBadge : null;

		public bool HasBadge => Node.RequiresScripts;

		/// <summary>Number of books, or -1 while it is not known yet (the counts are computed in the background).</summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(CountText))]
		[NotifyPropertyChangedFor(nameof(HasCount))]
		public partial int Count { get; set; } = -1;

		// A list that needs scripts has no number to show (its result is empty because it is not evaluated).
		public string CountText => Count < 0 || Node.RequiresScripts ? string.Empty : Count.ToString("N0", CultureInfo.CurrentCulture);

		/// <summary>True when the counter shows: the original draws none for a list without books (<c>LibraryTreeSkin.DrawMarkers</c>).</summary>
		public bool HasCount => Count > 0 && !Node.RequiresScripts;

		/// <summary>Books of the list that are not read and not new (<c>ComicListItem.UnreadBookCount</c>).</summary>
		[ObservableProperty]
		public partial int UnreadCount { get; set; }

		/// <summary>Books of the list that are not read and were added recently (<c>ComicListItem.NewBookCount</c>).</summary>
		[ObservableProperty]
		public partial int NewCount { get; set; }

		/// <summary>Takes the three numbers of a list (the counters of the tree).</summary>
		public void SetStats(ListStats stats)
		{
			Count = stats.Total;
			UnreadCount = stats.Unread;
			NewCount = stats.New;
		}

		[ObservableProperty]
		public partial bool IsExpanded { get; set; }

		public override string ToString() => Name;
	}

	/// <summary>
	/// The sidebar (M2.2c): a read-only tree of the library's lists, folders and smart lists, with a book count for each. Selecting one
	/// shows its books in the grid (<see cref="MainViewModel"/> asks <see cref="LoaderFor"/>).
	///
	/// <b>Nothing here evaluates a list on the UI thread.</b> The tree is built on a worker thread, the counts are computed one list at a
	/// time on a worker thread (children before the folders that combine them) and posted to the nodes as they come, and a selected
	/// list is evaluated inside the grid's load, also on a worker thread. All evaluation goes through one <see cref="ListEvaluator"/>,
	/// the same code `crcli smartlist` runs, so the numbers agree.
	/// </summary>
	public sealed partial class SidebarViewModel : ObservableObject
	{
		private int version;
		private CancellationTokenSource countsCts = new CancellationTokenSource();
		private ListEvaluator evaluator = new ListEvaluator();
		private MacHost host;

		/// <summary>The top level rows (the library, the folders).</summary>
		[ObservableProperty]
		public partial IReadOnlyList<SidebarNode> Roots { get; set; } = Array.Empty<SidebarNode>();

		/// <summary>The selected row, or null. Setting it raises <see cref="SelectionChanged"/> (unless the tree is being replaced).</summary>
		[ObservableProperty]
		public partial SidebarNode SelectedNode { get; set; }

		/// <summary>View > Show Sidebar.</summary>
		[ObservableProperty]
		public partial bool IsVisible { get; set; } = true;

		/// <summary>The width of the open side panel in device independent pixels (the grip changes it; 252 as in the original).</summary>
		[ObservableProperty]
		public partial double Width { get; set; } = AppPreferences.DefaultSidebarWidth;

		/// <summary><c>DisplayLibraryGauges</c>: the counters behind the list names show at all.</summary>
		[ObservableProperty]
		public partial bool GaugesVisible { get; set; } = true;

		/// <summary><c>LibraryGauges.Total</c> of <c>LibraryGaugesFormat</c>.</summary>
		[ObservableProperty]
		public partial bool GaugeTotal { get; set; } = true;

		/// <summary><c>LibraryGauges.Unread</c>.</summary>
		[ObservableProperty]
		public partial bool GaugeUnread { get; set; } = true;

		/// <summary><c>LibraryGauges.New</c>.</summary>
		[ObservableProperty]
		public partial bool GaugeNew { get; set; } = true;

		/// <summary><c>LibraryGauges.Numeric</c>: numbers in the counters, else bars.</summary>
		[ObservableProperty]
		public partial bool GaugeNumeric { get; set; } = true;

		/// <summary>
		/// Takes the counter settings of the preferences (<c>DisplayLibraryGauges</c>, <c>LibraryGaugesFormat</c>) and follows them, as the original redraws its
		/// tree when <c>DisplayLibraryGaugesChanged</c> fires (the Libraries pane of Preferences, P5.22b, changes them).
		/// </summary>
		public void BindGauges(AppPreferences preferences)
		{
			if (preferences == null)
			{
				return;
			}
			void Apply()
			{
				GaugesVisible = preferences.DisplayLibraryGauges;
				LibraryGauges format = preferences.LibraryGaugesFormat;
				GaugeTotal = format.HasFlag(LibraryGauges.Total);
				GaugeUnread = format.HasFlag(LibraryGauges.Unread);
				GaugeNew = format.HasFlag(LibraryGauges.New);
				GaugeNumeric = format.HasFlag(LibraryGauges.Numeric);
			}
			Apply();
			preferences.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(AppPreferences.DisplayLibraryGauges) || e.PropertyName == nameof(AppPreferences.LibraryGaugesFormat))
				{
					if (UiDispatch.IsUiThread)
					{
						Apply();
					}
					else
					{
						UiDispatch.Post(Apply);
					}
				}
			};
		}

		/// <summary>A message for the middle of the grid ("Requires scripts (not in MVP)", "This list is empty"), or null.</summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(HasNotice))]
		public partial string Notice { get; set; }

		public bool HasNotice => !string.IsNullOrEmpty(Notice);

		/// <summary>Raised on the UI thread when the user (or <see cref="Select"/>) picks another row.</summary>
		public event EventHandler SelectionChanged;

		private bool replacing;

		partial void OnSelectedNodeChanged(SidebarNode value)
		{
			if (!replacing)
			{
				SelectionChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		/// <summary>The rows, depth first.</summary>
		public IEnumerable<SidebarNode> AllNodes()
		{
			foreach (SidebarNode root in Roots)
			{
				foreach (SidebarNode n in Flatten(root))
				{
					yield return n;
				}
			}
		}

		private static IEnumerable<SidebarNode> Flatten(SidebarNode node)
		{
			yield return node;
			foreach (SidebarNode child in node.Children)
			{
				foreach (SidebarNode n in Flatten(child))
				{
					yield return n;
				}
			}
		}

		/// <summary>Selects a row without raising <see cref="SelectionChanged"/> twice for the same row. Raises it when the row changes.</summary>
		public void Select(SidebarNode node)
		{
			SelectedNode = node;
		}

		/// <summary>Empties the tree (no library).</summary>
		public void Clear()
		{
			Interlocked.Increment(ref version);
			countsCts.Cancel();
			host = null;
			replacing = true;
			try
			{
				Roots = Array.Empty<SidebarNode>();
				SelectedNode = null;
			}
			finally
			{
				replacing = false;
			}
			Notice = null;
		}

		/// <summary>The task of the newest <see cref="ReloadAsync"/> counts pass (completes when every count has been posted).</summary>
		public Task CountsReady { get; private set; } = Task.CompletedTask;

		/// <summary>
		/// Builds the tree of the open library on a worker thread, keeps the selected list if it still exists (else selects the library)
		/// and starts the counts. Does not raise <see cref="SelectionChanged"/>: the caller shows the selected list itself afterwards.
		/// </summary>
		public async Task ReloadAsync(MacHost newHost)
		{
			int mine = Interlocked.Increment(ref version);
			countsCts.Cancel();
			Guid keep = SelectedNode?.Id ?? Guid.Empty;
			IReadOnlyList<ListNode> tree = await Task.Run(() => LibraryLists.BuildTree(newHost.DatabaseManager.Database)).ConfigureAwait(true);
			if (mine != Volatile.Read(ref version))
			{
				return;
			}
			host = newHost;
			evaluator = new ListEvaluator(); // a new library: lists are computed again
			SidebarNode[] roots = tree.Select(Wrap).ToArray();
			replacing = true;
			try
			{
				Roots = roots;
				SidebarNode selected = roots.SelectMany(Flatten).FirstOrDefault(n => n.Id == keep)
					?? roots.SelectMany(Flatten).FirstOrDefault(n => n.Kind == ListKind.Library)
					?? roots.FirstOrDefault();
				SelectedNode = selected;
			}
			finally
			{
				replacing = false;
			}
			countsCts = new CancellationTokenSource();
			CountsReady = ComputeCounts(roots, newHost, evaluator, countsCts.Token);
		}

		private static SidebarNode Wrap(ListNode node)
		{
			return new SidebarNode(node, node.Children.Select(Wrap).ToArray());
		}

		/// <summary>
		/// Counts every list again (live refresh): the books, the unread and the new ones of each, on a worker thread, children before their folders, posting
		/// each row's numbers as they come. Does nothing without a tree; a reload of the tree cancels it.
		/// </summary>
		public Task RefreshCountsAsync()
		{
			MacHost current = host;
			SidebarNode[] roots = Roots.ToArray();
			if (current == null || roots.Length == 0)
			{
				return Task.CompletedTask;
			}
			return ComputeCounts(roots, current, evaluator, countsCts.Token);
		}

		/// <summary>Stops a running count (the library is about to be closed).</summary>
		public void CancelCounts()
		{
			countsCts.Cancel();
		}

		// One list at a time, children before their folder, so that a folder never combines a child's stale stored result.
		private static async Task ComputeCounts(SidebarNode[] roots, MacHost host, ListEvaluator evaluator, CancellationToken token)
		{
			SidebarNode[] order = roots.SelectMany(Flatten).Reverse().ToArray();
			try
			{
				await Task.Run(() =>
				{
					foreach (SidebarNode node in order)
					{
						token.ThrowIfCancellationRequested();
						ListStats stats = node.Kind == ListKind.Library
							? ListStats.Of(host.DatabaseManager.Database.Books.Lock().ToArray())
							: ListStats.Of(evaluator.Evaluate(node.Node.Item).Books);
						UiDispatch.Post(() => node.SetStats(stats));
					}
				}, token).ConfigureAwait(true);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				AppLog.Error("The list counts could not be computed.", ex);
			}
		}

		/// <summary>
		/// Gives the grid's load for a row: the books of the list, computed on the worker thread that runs the load. The row's count is
		/// updated and <paramref name="outcome"/> receives what the middle of the grid should say (null = nothing). A list that needs
		/// scripts is not evaluated and gives an empty catalog.
		/// </summary>
		public Func<CancellationToken, BookCatalog> LoaderFor(SidebarNode node, Action<string> outcome)
		{
			MacHost loaderHost = host;
			ListEvaluator loaderEvaluator = evaluator;
			return ct =>
			{
				if (loaderHost == null)
				{
					outcome(null);
					return BookCatalog.Empty;
				}
				if (node == null || node.Kind == ListKind.Library)
				{
					BookCatalog all = BookCatalog.FromBooks(loaderHost.DatabaseManager.Database.Books, ct);
					outcome(null);
					return all;
				}
				ListResult result = loaderEvaluator.Evaluate(node.Node.Item);
				if (result.RequiresScripts)
				{
					outcome(SidebarNode.ScriptsBadge + ". This list uses a script, so it shows no books.");
					return BookCatalog.Empty;
				}
				if (result.Error != null)
				{
					AppLog.Error("The list \"" + node.Name + "\" could not be evaluated: " + result.Error);
					outcome("This list could not be evaluated (see the log).");
					return BookCatalog.Empty;
				}
				int count = result.Count;
				ListStats stats = ListStats.Of(result.Books);
				UiDispatch.Post(() => node.SetStats(stats));
				BookCatalog catalog = BookCatalog.FromBooks(result.Books, ct);
				outcome(count == 0 ? "This list has no books." : null);
				return catalog;
			};
		}
	}
}
