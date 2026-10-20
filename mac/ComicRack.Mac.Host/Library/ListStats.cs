using System;
using System.Collections.Generic;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Host.Library
{
	/// <summary>
	/// The three numbers a list shows in the library tree (<c>ComicListItem.BookCount</c>, <c>UnreadBookCount</c>, <c>NewBookCount</c>): all books, the books
	/// that have not been read and are older than <c>IsRecentInDays</c>, and the books that have not been read and were added within that time. A book that
	/// has been read (<c>HasBeenRead</c>) counts in neither of the last two. The rule is the Core's own (<c>ComicListItem.CreateBookCacheStatus</c>), applied
	/// to the books a list evaluates to, so the numbers do not depend on the state of the Core's per list bookkeeping.
	/// </summary>
	public readonly struct ListStats : IEquatable<ListStats>
	{
		public ListStats(int total, int unread, int added)
		{
			Total = total;
			Unread = unread;
			New = added;
		}

		/// <summary>Books in the list.</summary>
		public int Total { get; }

		/// <summary>Books not read yet that were added more than <c>IsRecentInDays</c> ago.</summary>
		public int Unread { get; }

		/// <summary>Books not read yet that were added within <c>IsRecentInDays</c>.</summary>
		public int New { get; }

		public static readonly ListStats Empty = new ListStats(0, 0, 0);

		/// <summary>Counts <paramref name="books"/> at <paramref name="now"/>.</summary>
		public static ListStats Of(IEnumerable<ComicBook> books, DateTime now, int recentDays)
		{
			if (books == null)
			{
				throw new ArgumentNullException(nameof(books));
			}
			int total = 0, unread = 0, added = 0;
			foreach (ComicBook book in books)
			{
				total++;
				if (book.HasBeenRead)
				{
					continue;
				}
				if ((now - book.AddedTime).TotalDays < recentDays)
				{
					added++;
				}
				else
				{
					unread++;
				}
			}
			return new ListStats(total, unread, added);
		}

		/// <summary>Counts <paramref name="books"/> now with the configured number of days a book counts as new.</summary>
		public static ListStats Of(IEnumerable<ComicBook> books)
		{
			return Of(books, DateTime.Now, EngineConfiguration.Default.IsRecentInDays);
		}

		public bool Equals(ListStats other) => Total == other.Total && Unread == other.Unread && New == other.New;

		public override bool Equals(object obj) => obj is ListStats other && Equals(other);

		public override int GetHashCode() => HashCode.Combine(Total, Unread, New);

		public static bool operator ==(ListStats a, ListStats b) => a.Equals(b);

		public static bool operator !=(ListStats a, ListStats b) => !a.Equals(b);

		public override string ToString() => $"{Total} total, {Unread} unread, {New} new";
	}
}
