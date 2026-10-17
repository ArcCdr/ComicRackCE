using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using cYo.Common.Collections;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Browser
{
	public enum BookSort
	{
		/// <summary>Series (A to Z), then number, then title. Books without a series come last.</summary>
		Series,

		/// <summary>Title (A to Z), then series and number.</summary>
		Title,

		/// <summary>Date added, newest first.</summary>
		Added,
	}

	/// <summary>
	/// An immutable snapshot of the library for the browser, with the three sort orders and a search over title and series. It exists
	/// so that nothing the user does in the grid (typing a search, changing the sort) touches the Core's collections or sorts on the
	/// UI thread:
	/// <list type="bullet">
	/// <item>Each sort order is computed once, on first use, as an array of indexes, and kept. Changing the sort back and forth costs a
	/// copy of references.</item>
	/// <item>A search never sorts. It walks the chosen order and keeps the items whose title or series contains every word (words
	/// are separated by spaces; matching ignores case and accents), so the result is sorted already. That is one pass of ordinal
	/// <c>Contains</c> over the items, a few milliseconds for 100,000 books.</item>
	/// <item>Every long loop checks a <see cref="CancellationToken"/>, so a query superseded by the next keystroke stops early.</item>
	/// </list>
	/// A catalog is built from the books at one moment; a library that changed (a scan, an import) needs a new one.
	/// </summary>
	public sealed class BookCatalog
	{
		private const int CheckEvery = 4096;
		private readonly object orderLock = new object();
		private readonly int[][] orders = new int[3][];

		public static readonly BookCatalog Empty = new BookCatalog(Array.Empty<BookItem>());

		public BookCatalog(BookItem[] items)
		{
			Items = items ?? throw new ArgumentNullException(nameof(items));
		}

		/// <summary>The items in library order.</summary>
		public BookItem[] Items { get; }

		public int Count => Items.Length;

		/// <summary>
		/// Snapshots the books of the library (under the Core's list lock, as the Core's own consumers do) and reads their values.
		/// Takes a while for a big library: call it on a worker thread.
		/// </summary>
		public static BookCatalog FromBooks(IEnumerable<ComicBook> books, CancellationToken cancellationToken = default)
		{
			ComicBook[] snapshot = books.Lock().ToArray();
			var items = new BookItem[snapshot.Length];
			for (int i = 0; i < snapshot.Length; i++)
			{
				if ((i & (CheckEvery - 1)) == 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
				}
				items[i] = BookItem.From(snapshot[i]);
			}
			return new BookCatalog(items);
		}

		/// <summary>The words of a search text, folded; empty if the text has none.</summary>
		public static string[] SplitTerms(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return Array.Empty<string>();
			}
			return BookItem.Fold(text).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		}

		/// <summary>The items that match <paramref name="searchText"/> (all of them if it is empty), in the order of <paramref name="sort"/>.</summary>
		/// <exception cref="OperationCanceledException"/>
		public BookItem[] Query(BookSort sort, string searchText, CancellationToken cancellationToken)
		{
			int[] order = GetOrder(sort, cancellationToken);
			string[] terms = SplitTerms(searchText);
			BookItem[] items = Items;
			if (terms.Length == 0)
			{
				var all = new BookItem[order.Length];
				for (int i = 0; i < order.Length; i++)
				{
					all[i] = items[order[i]];
				}
				cancellationToken.ThrowIfCancellationRequested();
				return all;
			}
			var found = new List<BookItem>(Math.Min(order.Length, 1024));
			for (int i = 0; i < order.Length; i++)
			{
				if ((i & (CheckEvery - 1)) == 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
				}
				BookItem item = items[order[i]];
				if (Matches(item, terms))
				{
					found.Add(item);
				}
			}
			return found.ToArray();
		}

		private static bool Matches(BookItem item, string[] terms)
		{
			foreach (string term in terms)
			{
				if (!item.TitleKey.Contains(term, StringComparison.Ordinal) && !item.SeriesKey.Contains(term, StringComparison.Ordinal))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>True if the order of <paramref name="sort"/> has been computed (the next query with it is a pass over the items, not a sort).</summary>
		public bool HasOrder(BookSort sort)
		{
			lock (orderLock)
			{
				return orders[(int)sort] != null;
			}
		}

		private int[] GetOrder(BookSort sort, CancellationToken cancellationToken)
		{
			lock (orderLock)
			{
				int[] order = orders[(int)sort];
				if (order == null)
				{
					order = Sort(sort, cancellationToken);
					orders[(int)sort] = order;
				}
				return order;
			}
		}

		private int[] Sort(BookSort sort, CancellationToken cancellationToken)
		{
			BookItem[] items = Items;
			int[] index = new int[items.Length];
			for (int i = 0; i < index.Length; i++)
			{
				index[i] = i;
			}
			int calls = 0;
			Comparison<int> compare;
			switch (sort)
			{
				case BookSort.Title:
					compare = (a, b) => Step(ref calls, cancellationToken, CompareTitle(items[a], items[b], a, b));
					break;
				case BookSort.Added:
					compare = (a, b) =>
					{
						int c = items[b].Added.CompareTo(items[a].Added); // newest first
						return Step(ref calls, cancellationToken, c != 0 ? c : CompareSeries(items[a], items[b], a, b));
					};
					break;
				default:
					compare = (a, b) => Step(ref calls, cancellationToken, CompareSeries(items[a], items[b], a, b));
					break;
			}
			try
			{
				Array.Sort(index, compare);
			}
			catch (InvalidOperationException ex) when (ex.InnerException is OperationCanceledException cancelled)
			{
				throw cancelled; // Array.Sort wraps what the comparison throws
			}
			return index;
		}

		private static int Step(ref int calls, CancellationToken token, int result)
		{
			if ((++calls & 8191) == 0)
			{
				token.ThrowIfCancellationRequested();
			}
			return result;
		}

		// A total order (the last tie-break is the position in the library), so the result does not depend on the sort algorithm.
		private static int CompareSeries(BookItem x, BookItem y, int xi, int yi)
		{
			bool xe = x.SeriesKey.Length == 0, ye = y.SeriesKey.Length == 0;
			if (xe != ye)
			{
				return xe ? 1 : -1; // no series: last
			}
			int c = string.CompareOrdinal(x.SeriesKey, y.SeriesKey);
			if (c != 0)
			{
				return c;
			}
			c = CompareNumber(x, y);
			if (c != 0)
			{
				return c;
			}
			c = string.CompareOrdinal(x.TitleKey, y.TitleKey);
			return c != 0 ? c : xi.CompareTo(yi);
		}

		private static int CompareTitle(BookItem x, BookItem y, int xi, int yi)
		{
			int c = string.CompareOrdinal(x.TitleKey, y.TitleKey);
			if (c != 0)
			{
				return c;
			}
			c = string.CompareOrdinal(x.SeriesKey, y.SeriesKey);
			if (c != 0)
			{
				return c;
			}
			c = CompareNumber(x, y);
			return c != 0 ? c : xi.CompareTo(yi);
		}

		private static int CompareNumber(BookItem x, BookItem y)
		{
			bool xn = float.IsNaN(x.NumberValue), yn = float.IsNaN(y.NumberValue);
			if (xn != yn)
			{
				return xn ? 1 : -1; // numbered books first
			}
			if (!xn)
			{
				int c = x.NumberValue.CompareTo(y.NumberValue);
				if (c != 0)
				{
					return c;
				}
			}
			return string.CompareOrdinal(x.Number, y.Number);
		}
	}
}
