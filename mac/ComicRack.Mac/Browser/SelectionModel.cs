using System;
using System.Collections.Generic;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Browser
{
	/// <summary>
	/// Which books of the shown list are selected, which one has the focus and where a Shift range starts: the selection rules of the original's
	/// <c>ItemView</c> (<c>cYo.Common.Windows/Forms/ItemView.cs</c>: <c>UpdateSelectionFromMouse</c>, <c>OnKeyDown</c>, <c>SelectFromAnchorItem</c>,
	/// <c>SelectAll</c>, <c>InvertSelection</c>), without any control. "Ctrl" of the original is <c>toggle</c> here (Cmd on the Mac, DD20).
	///
	/// The state is kept per <b>book</b> (the Core book, else the item), not per position, so it survives a new sort, a search, a refresh that builds new
	/// <see cref="BookItem"/>s and a new list that still contains the book. Books that a search hides stay selected (the original keeps the state of an
	/// item that is not displayed) but do not count and are not in <see cref="SelectedBooks"/> until they are shown again; a refresh of the list
	/// (<see cref="SetItems"/> with <c>keepHidden</c> false) forgets them.
	///
	/// Positions are indexes into <see cref="Items"/>. All members run on the UI thread. Nothing here is slow: a lookup is one hash probe, and the
	/// operations on the whole list (select all, invert) are a single pass.
	/// </summary>
	public sealed class SelectionModel
	{
		private static readonly BookItem[] NoItems = Array.Empty<BookItem>();

		private readonly HashSet<object> selected = new HashSet<object>(ReferenceEqualityComparer.Instance);
		private BookItem[] items = NoItems;
		private int shownSelected;
		private object focused;
		private object anchor;
		private int muted;

		/// <summary>The books shown, in the order of the grid.</summary>
		public BookItem[] Items => items;

		/// <summary>The selection, the focus or the list changed (raised once per operation).</summary>
		public event EventHandler Changed;

		/// <summary>Number of selected books that are shown.</summary>
		public int Count => shownSelected;

		/// <summary>Index of the focused book, or -1.</summary>
		public int FocusedIndex => IndexOf(focused);

		/// <summary>The focused book, or null.</summary>
		public BookItem FocusedItem
		{
			get
			{
				int i = FocusedIndex;
				return i >= 0 ? items[i] : null;
			}
		}

		public bool IsSelected(int index) => index >= 0 && index < items.Length && selected.Contains(Key(items[index]));

		/// <summary>Positions of the selected books, in grid order.</summary>
		public List<int> SelectedIndices()
		{
			var result = new List<int>(shownSelected);
			if (shownSelected > 0)
			{
				for (int i = 0; i < items.Length; i++)
				{
					if (selected.Contains(Key(items[i])))
					{
						result.Add(i);
					}
				}
			}
			return result;
		}

		/// <summary>The selected items that are shown, in grid order.</summary>
		public List<BookItem> SelectedItems()
		{
			var result = new List<BookItem>(shownSelected);
			if (shownSelected > 0)
			{
				foreach (BookItem item in items)
				{
					if (selected.Contains(Key(item)))
					{
						result.Add(item);
					}
				}
			}
			return result;
		}

		/// <summary>The Core books of the selected items that are shown, in grid order (what the commands work on).</summary>
		public List<ComicBook> SelectedBooks()
		{
			var result = new List<ComicBook>(shownSelected);
			foreach (BookItem item in SelectedItems())
			{
				if (item.Book != null)
				{
					result.Add(item.Book);
				}
			}
			return result;
		}

		/// <summary>The sum of the file sizes of the selected books that are shown.</summary>
		public long SelectedSize()
		{
			long total = 0;
			if (shownSelected > 0)
			{
				foreach (BookItem item in items)
				{
					if (selected.Contains(Key(item)))
					{
						total += item.Book?.FileSize ?? 0;
					}
				}
			}
			return total;
		}

		// ---- the list ------------------------------------------------------------------------------------------------------------------

		/// <summary>
		/// A new list is shown (sorted, searched, refreshed). The selection, the focus and the anchor stay with their books. <paramref name="keepHidden"/>
		/// keeps the selected books that are not in the new list (a search hides them); false forgets them (another list, a refresh).
		/// </summary>
		public void SetItems(BookItem[] value, bool keepHidden)
		{
			items = value ?? NoItems;
			if (selected.Count > 0)
			{
				if (keepHidden)
				{
					CountShown();
				}
				else
				{
					var keep = new HashSet<object>(ReferenceEqualityComparer.Instance);
					foreach (BookItem item in items)
					{
						object key = Key(item);
						if (selected.Contains(key))
						{
							keep.Add(key);
						}
					}
					selected.Clear();
					selected.UnionWith(keep);
					shownSelected = selected.Count;
				}
			}
			else
			{
				shownSelected = 0;
			}
			if (focused != null && IndexOf(focused) < 0)
			{
				focused = null;
			}
			if (anchor != null && IndexOf(anchor) < 0)
			{
				anchor = null;
			}
			Raise();
		}

		private void CountShown()
		{
			int n = 0;
			foreach (BookItem item in items)
			{
				if (selected.Contains(Key(item)))
				{
					n++;
				}
			}
			shownSelected = n;
		}

		// ---- mouse ---------------------------------------------------------------------------------------------------------------------

		/// <summary>
		/// A press on the book at <paramref name="index"/> (<c>UpdateSelectionFromMouse</c>): without a modifier it becomes the only selected book and the
		/// start of a range; with <paramref name="toggle"/> it flips and keeps the others; with <paramref name="extend"/> everything between the focused
		/// book and it is selected (the others are let go unless <paramref name="toggle"/> is held too). The book gets the focus.
		/// </summary>
		public void Click(int index, bool toggle, bool extend)
		{
			if (index < 0 || index >= items.Length)
			{
				return;
			}
			BookItem item = items[index];
			Begin();
			if (!toggle && !extend)
			{
				anchor = Key(item);
			}
			if (!toggle)
			{
				ClearSelection();
			}
			if (extend)
			{
				SelectRange(index, FocusedIndex, fromFocus: true);
			}
			else if (toggle)
			{
				Flip(index);
			}
			else
			{
				Set(index, true);
			}
			focused = Key(item);
			End();
		}

		/// <summary>A press on the empty area of the view: without a modifier nothing is selected and nothing has the focus.</summary>
		public void ClickEmpty(bool toggle, bool extend)
		{
			if (!toggle && !extend)
			{
				Clear();
			}
		}

		/// <summary>The focus goes to a book and nothing else changes (a press on a selected book that is kept until the button is released, a context click).</summary>
		public void Focus(int index)
		{
			if (index < 0 || index >= items.Length)
			{
				return;
			}
			Begin();
			focused = Key(items[index]);
			End();
		}

		// ---- keyboard ------------------------------------------------------------------------------------------------------------------

		/// <summary>
		/// A movement key took the focus to <paramref name="target"/> (<c>OnKeyDown</c>): without a modifier the target is the only selected book and the
		/// start of a range; with Shift the range from the anchor to the target is selected; with <paramref name="toggle"/> only the focus moves.
		/// </summary>
		public void Move(int target, bool toggle, bool extend)
		{
			if (target < 0 || target >= items.Length)
			{
				return;
			}
			BookItem item = items[target];
			Begin();
			int oldFocus = FocusedIndex;
			focused = Key(item);
			if (!toggle && !extend)
			{
				anchor = focused;
			}
			if (!toggle)
			{
				ClearSelection();
				Set(target, true);
			}
			if (extend)
			{
				SelectRange(target, oldFocus, fromFocus: false);
			}
			End();
		}

		/// <summary>
		/// Space: the focused book is selected; with <paramref name="toggle"/> it is flipped. Without a focus the first book is the one, and, as in
		/// <c>ItemView.OnKeyDown</c>, it is selected without getting the focus.
		/// </summary>
		public void Space(bool toggle)
		{
			if (items.Length == 0)
			{
				return;
			}
			int index = Math.Max(0, FocusedIndex);
			Begin();
			if (toggle)
			{
				Flip(index);
			}
			else
			{
				Set(index, true);
			}
			End();
		}

		// ---- whole list ----------------------------------------------------------------------------------------------------------------

		/// <summary>Select All (Cmd+A, <c>ItemView.SelectAll</c>): every shown book; the focus stays where it is.</summary>
		public void SelectAll()
		{
			Begin();
			foreach (BookItem item in items)
			{
				selected.Add(Key(item));
			}
			shownSelected = items.Length;
			End();
		}

		/// <summary>Invert Selection (<c>ItemView.InvertSelection</c>): every shown book flips; the focus stays where it is.</summary>
		public void Invert()
		{
			Begin();
			foreach (BookItem item in items)
			{
				object key = Key(item);
				if (!selected.Remove(key))
				{
					selected.Add(key);
				}
			}
			CountShown();
			End();
		}

		/// <summary>Nothing is selected and nothing has the focus.</summary>
		public void Clear()
		{
			Begin();
			ClearSelection();
			focused = null;
			End();
		}

		/// <summary>The book at <paramref name="index"/> is the only selected book and has the focus (the selection of the MVP's single selection grid).</summary>
		public void SelectOnly(int index)
		{
			if (index < 0 || index >= items.Length)
			{
				Clear();
				return;
			}
			if (shownSelected == 1 && IsSelected(index) && selected.Count == 1 && FocusedIndex == index)
			{
				anchor = focused;
				return;
			}
			Click(index, toggle: false, extend: false);
		}

		/// <summary>The books to select, e.g. after a command that creates or finds them (<c>SelectComics</c>): they are selected, the first one has the focus.</summary>
		public void SelectBooks(IEnumerable<ComicBook> books)
		{
			var wanted = new HashSet<ComicBook>(books ?? Array.Empty<ComicBook>());
			Begin();
			ClearSelection();
			focused = null;
			for (int i = 0; i < items.Length; i++)
			{
				if (items[i].Book != null && wanted.Contains(items[i].Book))
				{
					Set(i, true);
					if (focused == null)
					{
						focused = Key(items[i]);
						anchor = focused;
					}
				}
			}
			End();
		}

		// ---- rubber band ---------------------------------------------------------------------------------------------------------------

		private HashSet<object> marqueeBase;

		/// <summary>True between <see cref="BeginMarquee"/> and <see cref="EndMarquee"/>.</summary>
		public bool IsMarqueeActive => marqueeBase != null;

		/// <summary>
		/// A press on the empty area started a rubber band (<c>UpdateSelectionFromMouse</c> with no item: the state at this moment is kept, after the
		/// press has cleared the selection when no modifier was held).
		/// </summary>
		public void BeginMarquee()
		{
			marqueeBase = new HashSet<object>(selected, ReferenceEqualityComparer.Instance);
		}

		/// <summary>
		/// The rubber band moved or the view scrolled (<c>ItemView.UpdateSelection</c>). The focus is let go. Every book in <paramref name="visible"/> (the
		/// ones on screen: the others keep the state they have) goes back to the state it had when the band started (<paramref name="keepBase"/>, held
		/// while Shift or Cmd is down) or to not selected, and the ones in <paramref name="hit"/> (the band touches them) are selected, or flipped when
		/// <paramref name="flip"/> (Cmd). The book under the pointer, <paramref name="pointed"/>, gets the focus.
		/// </summary>
		public void UpdateMarquee(IReadOnlyList<int> visible, IReadOnlyCollection<int> hit, int pointed, bool keepBase, bool flip)
		{
			if (marqueeBase == null)
			{
				return;
			}
			var touched = new HashSet<int>(hit);
			object oldFocus = focused;
			bool[] before = new bool[visible.Count];
			for (int i = 0; i < before.Length; i++)
			{
				before[i] = visible[i] >= 0 && visible[i] < items.Length && selected.Contains(Key(items[visible[i]]));
			}
			Begin();
			focused = null;
			foreach (int index in visible)
			{
				if (index < 0 || index >= items.Length)
				{
					continue;
				}
				Set(index, keepBase && marqueeBase.Contains(Key(items[index])));
				if (touched.Contains(index))
				{
					if (flip)
					{
						Flip(index);
					}
					else
					{
						Set(index, true);
					}
					if (index == pointed && focused == null)
					{
						focused = Key(items[index]);
					}
				}
			}
			bool changed = !ReferenceEquals(oldFocus, focused);
			for (int i = 0; i < before.Length && !changed; i++)
			{
				changed = visible[i] >= 0 && visible[i] < items.Length && before[i] != selected.Contains(Key(items[visible[i]]));
			}
			if (!changed)
			{
				muted--; // the band moved but nothing changed: no event
				return;
			}
			End();
		}

		/// <summary>The button went up (<c>OnMouseUpView</c>): the state kept at the start is let go.</summary>
		public void EndMarquee()
		{
			marqueeBase = null;
		}

		// ---- parts ---------------------------------------------------------------------------------------------------------------------

		// SelectFromAnchorItem: selects from the anchor to the target. A press resets the anchor to the focused book (overideAnchor); a key keeps it,
		// else the book that had the focus before the move becomes the anchor (the first one if there was none).
		private void SelectRange(int target, int previousFocus, bool fromFocus)
		{
			if (items.Length == 0)
			{
				return;
			}
			int a = fromFocus ? -1 : IndexOf(anchor);
			if (a < 0)
			{
				a = previousFocus;
				if (a < 0)
				{
					a = 0;
				}
				anchor = Key(items[a]);
			}
			int from = Math.Min(a, target);
			int to = Math.Max(a, target);
			for (int i = from; i <= to; i++)
			{
				Set(i, true);
			}
		}

		private void ClearSelection()
		{
			if (selected.Count > 0)
			{
				selected.Clear();
				shownSelected = 0;
			}
		}

		private void Set(int index, bool on)
		{
			object key = Key(items[index]);
			if (on)
			{
				if (selected.Add(key))
				{
					shownSelected++;
				}
			}
			else if (selected.Remove(key))
			{
				shownSelected--;
			}
		}

		private void Flip(int index) => Set(index, !selected.Contains(Key(items[index])));

		private int IndexOf(object key)
		{
			if (key == null)
			{
				return -1;
			}
			for (int i = 0; i < items.Length; i++)
			{
				if (ReferenceEquals(Key(items[i]), key))
				{
					return i;
				}
			}
			return -1;
		}

		// The identity of an item: its Core book (a new BookItem of the same book is the same book), else the item itself (synthetic items).
		private static object Key(BookItem item) => (object)item.Book ?? item;

		private void Begin() => muted++;

		private void End()
		{
			muted--;
			Raise();
		}

		private void Raise()
		{
			if (muted > 0)
			{
				return;
			}
			Changed?.Invoke(this, EventArgs.Empty);
		}
	}
}
