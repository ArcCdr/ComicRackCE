using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ComicRack.Mac.Host.Platform;
using ComicRack.Mac.Services;
using cYo.Common.Net.Search;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Info
{
	/// <summary>One entry of a search link menu: a result, the generic search, a separator, a sub menu of an engine or a web address.</summary>
	public sealed class SearchMenuEntry
	{
		public string Header { get; set; }

		/// <summary>The address that opens in the default browser, or null (a separator, a sub menu, "No Results").</summary>
		public string Address { get; set; }

		public bool IsSeparator { get; set; }

		public bool IsEnabled { get; set; } = true;

		/// <summary>The name of the engine whose icon the entry has (<c>search.Image</c>), or null.</summary>
		public string EngineName { get; set; }

		/// <summary>The entries of a sub menu (several engines), filled by <see cref="SearchLinkMenu.BuildItemsAsync"/> when it opens.</summary>
		public INetSearch Engine { get; set; }
	}

	/// <summary>
	/// The search links of a book field (<c>SearchContextMenuBuilder</c> and <c>CoverViewItem.DrawSearchLinkButton</c>): the arrow right of a text cell of the details view (while the
	/// list has the focus) opens a menu of the results of the search engines (Wikipedia, <c>SearchEngines.Engines</c>) for the cell's text, "No Results" without one, and
	/// "Search..." for the engine's own search page; every entry opens in the default browser. The button needs the option <c>ShowSearchLinks</c> (on by default), a
	/// non-empty text and a field the book declares searchable. The details view (P5.11a) shows the arrow; this class is its menu.
	/// </summary>
	public sealed class SearchLinkMenu
	{
		private const int Limit = 10;
		private const int MultiLimit = 20;

		private readonly IReadOnlyList<INetSearch> engines;
		private readonly Func<bool> enabled;

		/// <param name="engines">The engines, Wikipedia by default (<c>SearchEngines.Engines</c>); a test passes a fake that never reads the network.</param>
		/// <param name="enabled"><c>Settings.ShowSearchLinks</c>.</param>
		/// <param name="opener">Opens an address in the default browser (<c>Process.Start(address)</c>); <see cref="MacShell.OpenUrl"/> by default.</param>
		public SearchLinkMenu(IEnumerable<INetSearch> engines = null, Func<bool> enabled = null, Func<string, bool> opener = null)
		{
			this.engines = (engines ?? new INetSearch[] { new WikiSearch() }).ToArray();
			this.enabled = enabled ?? (() => true);
			Opener = opener ?? MacShell.OpenUrl;
		}

		public IReadOnlyList<INetSearch> Engines => engines;

		public Func<string, bool> Opener { get; set; }

		/// <summary>Whether the cell gets the arrow (<c>ShowSearchLinks</c>, text not empty, <c>Comic.IsSearchable(field)</c>).</summary>
		public bool IsAvailable(ComicBook book, string field, string text)
		{
			return enabled() && !string.IsNullOrEmpty(text) && book != null && book.IsSearchable(field);
		}

		/// <summary>
		/// <c>CreateContextMenu</c>: several engines give one entry each (<see cref="SearchMenuEntry.Engine"/>, whose items <see cref="BuildItemsAsync"/> makes when it
		/// opens), one engine gives its items directly (the results come when the menu opens: call <see cref="BuildItemsAsync"/>). The web address of the book follows after a separator.
		/// </summary>
		public async Task<IReadOnlyList<SearchMenuEntry>> BuildAsync(string hint, string text, string webAddress = null)
		{
			var list = new List<SearchMenuEntry>();
			if (engines.Count > 1)
			{
				foreach (INetSearch engine in engines)
				{
					list.Add(new SearchMenuEntry { Header = UiText.Get("SearchMenu", engine.Name, engine.Name), EngineName = engine.Name, Engine = engine });
				}
			}
			else if (engines.Count == 1)
			{
				list.AddRange(await BuildItemsAsync(engines[0], hint, text));
			}
			AddWebAddress(list, webAddress);
			return list;
		}

		private static void AddWebAddress(List<SearchMenuEntry> list, string webAddress)
		{
			if (!string.IsNullOrEmpty(webAddress))
			{
				list.Add(new SearchMenuEntry { IsSeparator = true });
				list.Add(new SearchMenuEntry { Header = webAddress, Address = webAddress });
			}
		}

		/// <summary>
		/// <c>CreateItems</c>: up to 10 results of the engine for the text; none for the whole text: the parts between commas, 2 results each (20 entries at most), separated.
		/// "No Results" (disabled) when there is none; then a separator and "Search..." with the engine's generic search link. The search runs on the pool.
		/// </summary>
		public Task<IReadOnlyList<SearchMenuEntry>> BuildItemsAsync(INetSearch search, string hint, string text)
		{
			return Task.Run(() => BuildItems(search, hint, text));
		}

		/// <summary>The synchronous form of <see cref="BuildItemsAsync"/> (it reads the network for a real engine).</summary>
		public IReadOnlyList<SearchMenuEntry> BuildItems(INetSearch search, string hint, string text)
		{
			var list = new List<SearchMenuEntry>();
			text = (text ?? string.Empty).Trim();
			try
			{
				if (!string.IsNullOrEmpty(text))
				{
					SearchResult[] results = search.Search(hint, text, Limit).ToArray();
					if (results.Length != 0)
					{
						AddEntries(list, results, search.Name);
					}
					else
					{
						foreach (string part in text.Split(',').Select(t => t.Trim()).Where(t => t.Length != 0).Distinct())
						{
							if (AddEntries(list, search.Search(hint, part, 2), search.Name) > 0)
							{
								list.Add(new SearchMenuEntry { IsSeparator = true });
							}
							if (list.Count > MultiLimit)
							{
								break;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				AppLog.Info("Search links: " + search.Name + " failed: " + ex.Message);
			}
			if (list.Count == 0)
			{
				list.Add(new SearchMenuEntry { Header = UiText.Get("SearchMenu", "NoResult", "No Results"), IsEnabled = false });
			}
			string generic = search.GenericSearchLink(hint, text);
			if (!string.IsNullOrEmpty(generic))
			{
				if (list.Count > 0 && !list[list.Count - 1].IsSeparator)
				{
					list.Add(new SearchMenuEntry { IsSeparator = true });
				}
				list.Add(new SearchMenuEntry { Header = UiText.Get("SearchMenu", "Search", "Search..."), Address = generic, EngineName = search.Name });
			}
			return list;
		}

		/// <summary>Opens the entry's address in the default browser (<c>SafeStart</c>: a failure is ignored). Returns whether it was started.</summary>
		public bool Open(SearchMenuEntry entry)
		{
			if (entry == null || !entry.IsEnabled || string.IsNullOrEmpty(entry.Address))
			{
				return false;
			}
			try
			{
				return Opener(entry.Address);
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static int AddEntries(List<SearchMenuEntry> list, IEnumerable<SearchResult> results, string engine)
		{
			int count = 0;
			foreach (SearchResult result in results)
			{
				list.Add(new SearchMenuEntry { Header = result.Name, Address = result.Result, EngineName = engine });
				count++;
			}
			return count;
		}
	}
}
