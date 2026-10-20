using System.Collections.Generic;
using ComicRack.Mac.Controls;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Browser
{
	/// <summary>
	/// Reads what a thumbnail shows besides the cover from a Core book, the way <c>CoverViewItem.OnDraw</c> does: the state icons in its order
	/// (unsaved changes, open, last opened, new pages, fileless, file missing), the two ribbons, the two ratings. The result is plain values
	/// for <see cref="CrThumbnailItem"/>; which picture belongs to which state is the chrome's business, so only resource names are passed.
	/// </summary>
	public static class ThumbnailBadgeBuilder
	{
		public static ThumbnailBadges From(BookItem item, ThumbnailOptions options)
		{
			ComicBook book = item?.Book;
			if (book == null)
			{
				return ThumbnailBadges.None;
			}
			var icons = new List<string>();
			if (book.IsLinked && (book.ComicInfoIsDirty && options.UpdateComicFiles || book.ComicBookIsDirty && options.UpdateComicBookFiles))
			{
				icons.Add("UpdateBig");
			}
			switch (options.MarkerOf?.Invoke(book) ?? ThumbnailMarker.None)
			{
				case ThumbnailMarker.IsOpen:
					icons.Add("OpenMarker");
					break;
				case ThumbnailMarker.IsLast:
					icons.Add("LastMarker");
					break;
			}
			if (book.NewPages > 0)
			{
				icons.Add(NewPagesIcon(book.NewPages));
			}
			if (!book.IsLinked)
			{
				icons.Add("FilelessMarker");
			}
			if (book.IsLinked && book.FileIsMissing && book.EditMode.IsLocalComic())
			{
				icons.Add("Deleted");
			}
			return new ThumbnailBadges
			{
				PageCount = book.PageCount,
				CurrentPage = book.CurrentPage,
				LastPageRead = book.LastPageRead,
				ShowRibbons = book.IsLinked,
				Rating = book.Rating,
				CommunityRating = book.CommunityRating,
				StateIcons = icons,
			};
		}

		/// <summary><c>ThumbRenderer.GetNewPageStatusImage</c>: one picture for 1 to 5 new pages, "5+" above.</summary>
		public static string NewPagesIcon(int newPages)
		{
			int index = System.Math.Clamp(newPages - 1, 0, 5);
			return index == 5 ? "NewPages5Plus" : "NewPages" + (index + 1);
		}

		/// <summary><c>CoverViewItem.OnDraw</c> asks for the page behind the dog-ear only for a linked book of two pages or more whose file is there.</summary>
		public static bool HasBackPage(ComicBook book)
		{
			return book != null && book.IsLinked && book.PageCount > 1 && !book.FileIsMissing;
		}

		/// <summary><c>CoverViewItem.GetBackThumbnail</c>: the page the book is at, or its first page that is not a cover.</summary>
		public static int BackPageIndex(ComicBook book)
		{
			return book.CurrentPage <= 0 ? book.FirstNonCoverPageIndex : book.CurrentPage;
		}

		/// <summary>The caption of a thumbnail: <c>ComicBook.Caption</c>, or what the catalog shows for an item without a Core book.</summary>
		public static string Caption(BookItem item)
		{
			string caption = item.Book?.Caption;
			if (!string.IsNullOrEmpty(caption))
			{
				return caption;
			}
			return item.Subtitle.Length == 0 ? item.Title : item.Title + " (" + item.Subtitle + ")";
		}
	}
}
