using System;
using System.Collections.Generic;
using ComicRack.Mac.Browser;

namespace ComicRack.Mac.Controls
{
	/// <summary>How <see cref="CrPageList"/> shows its rows (<c>ItemViewMode</c> of the original's pages view).</summary>
	public enum PageListMode
	{
		Detail,
		Thumbnail,
		Tile,
	}

	/// <summary>One column of the detail view (<c>ItemViewColumn</c> of <c>PagesView</c>): the title is the translated column text.</summary>
	public sealed class PageListColumn
	{
		public PageListColumn(int id, string title, double width, bool farAligned, bool isThumbnail, bool canSort)
		{
			Id = id;
			Title = title;
			Width = width;
			FarAligned = farAligned;
			IsThumbnail = isThumbnail;
			CanSort = canSort;
		}

		/// <summary>The id the original gives the column (0 = Page ... 9 = Position); the view names columns by it.</summary>
		public int Id { get; }

		public string Title { get; }

		/// <summary>The width in units at the start; the view can change it.</summary>
		public double Width { get; set; }

		/// <summary>Right aligned text (<c>StringAlignment.Far</c>).</summary>
		public bool FarAligned { get; }

		/// <summary>The cell shows the page's picture instead of text.</summary>
		public bool IsThumbnail { get; }

		/// <summary>A click on the header sorts by the column.</summary>
		public bool CanSort { get; }
	}

	/// <summary>What a row of the list draws besides its columns.</summary>
	public readonly struct PageRowState
	{
		public PageRowState(string number, bool isDeleted, bool isBookmarked, bool isCurrent)
		{
			Number = number;
			IsDeleted = isDeleted;
			IsBookmarked = isBookmarked;
			IsCurrent = isCurrent;
		}

		/// <summary>The page number as the original shows it (the page + 1).</summary>
		public string Number { get; }

		/// <summary>The page is marked as deleted: the list shows the deleted state picture over its thumbnail.</summary>
		public bool IsDeleted { get; }

		/// <summary>The page has a bookmark.</summary>
		public bool IsBookmarked { get; }

		/// <summary>The page the viewer next to the list shows.</summary>
		public bool IsCurrent { get; }
	}

	/// <summary>
	/// The rows of a <see cref="CrPageList"/>: the list asks for texts, states and pictures by row index and never holds on to a row. The selection lives here, so that the commands of the
	/// model act on what the list shows. Events are raised on the UI thread.
	/// </summary>
	public interface IPageListSource
	{
		int Count { get; }

		IReadOnlyList<PageListColumn> Columns { get; }

		/// <summary>The id of the column the rows are sorted by, or -1 (not sorted).</summary>
		int SortColumn { get; }

		bool SortAscending { get; }

		/// <summary>The text of a cell of the detail view.</summary>
		string Text(int row, int columnId);

		PageRowState State(int row);

		/// <summary>The lines of text beside the picture in tile mode.</summary>
		IReadOnlyList<TileTextLine> Lines(int row);

		string ToolTip(int row);

		/// <summary>
		/// The picture of a row at (about) <paramref name="height"/> pixels, or null while it is being made (<see cref="PicturesChanged"/> follows). The caller gets a reference of its own and
		/// releases it.
		/// </summary>
		CoverImage Picture(int row, int height);

		/// <summary>The rows selected, in row order.</summary>
		IReadOnlyList<int> Selection { get; }

		void SetSelection(IEnumerable<int> rows);

		/// <summary>The rows were added, removed or ordered again: the list measures and draws everything again.</summary>
		event EventHandler Changed;

		/// <summary>The text, state or picture of rows changed, the layout did not.</summary>
		event EventHandler RowsChanged;

		event EventHandler SelectionChanged;

		/// <summary>A picture that <see cref="Picture"/> had to make is there now.</summary>
		event EventHandler PicturesChanged;
	}
}
