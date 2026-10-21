#pragma warning disable xUnit1031 // the tests pump a fake UI thread first, then read what is already there
using System;
using System.Collections.Generic;
using System.Linq;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Editor;
using cYo.Common.Drawing;
using cYo.Projects.ComicRack.Engine;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Editor
{
	/// <summary>P5.7c: the list of pages of a book (IN.06, PV.02 to PV.05) on a real CBZ: rows, columns, sorting, the page menu's commands, the order commands and what Save writes.</summary>
	public class PageListModelTests
	{
		private static string[] Keys(PageListModel model) => model.Rows.Select(r => r.Key).ToArray();

		private static string[] Names(PageListModel model, int column) => Enumerable.Range(0, model.Count).Select(i => model.Text(i, column)).ToArray();

		[Fact]
		public void The_list_has_a_row_for_every_page_in_page_order_with_the_texts_of_the_columns()
		{
			using (var rig = new PagesRig())
			{
				string file = rig.MakeCbz("a.cbz", PagesRig.ThreePages);
				using (PageListModel model = rig.Open(rig.Book(file), 3))
				{
					Assert.Equal(new[] { "p1.png", "p2.png", "p10.png" }, Keys(model)); // the archive's own natural order
					Assert.Equal(new[] { "1", "2", "3" }, Names(model, PageColumns.Page));
					Assert.Equal(new[] { "Front Cover", "Story", "Story" }, Names(model, PageColumns.Type));
					Assert.Equal(new[] { "p1.png", "p2.png", "p10.png" }, Names(model, PageColumns.Name));
					Assert.Equal(new[] { "0°", "0°", "0°" }, Names(model, PageColumns.Rotation));
					Assert.Equal(PageColumns.Page, model.SortColumn);
					Assert.True(model.SortAscending);
					Assert.True(model.IsPageSorted);
					// the list keeps the library's book alone: it works on its own copy
					Assert.NotSame(rig.Book(file), model.Comic);
				}
			}
		}

		[Fact]
		public void The_columns_are_those_of_the_pages_view_with_their_widths_and_alignment()
		{
			var columns = PageColumns.Create();
			Assert.Equal(new[] { "Page", "Thumbnail", "Type", "Size", "Width", "Height", "Name", "Bookmark", "Rotation", "Position" }, columns.Select(c => c.Title).ToArray());
			Assert.Equal(new double[] { 40, 80, 80, 80, 60, 60, 150, 150, 60, 60 }, columns.Select(c => c.Width).ToArray());
			Assert.Equal(new[] { true, false, false, true, true, true, false, false, true, false }, columns.Select(c => c.FarAligned).ToArray());
			Assert.Equal(new[] { true, false, true, true, true, true, true, true, true, true }, columns.Select(c => c.CanSort).ToArray());
			Assert.Equal(new[] { PageColumns.Thumbnail }, columns.Where(c => c.IsThumbnail).Select(c => c.Id).ToArray());
		}

		[Fact]
		public void A_click_on_a_header_sorts_by_the_column_and_a_second_click_flips_the_order()
		{
			using (var rig = new PagesRig())
			{
				string file = rig.MakeCbz("a.cbz", PagesRig.ThreePages);
				using (PageListModel model = rig.Open(rig.Book(file), 3))
				{
					int changes = 0;
					model.Changed += (s, e) => changes++;
					// the sizes of the pages are learned when their pictures are made; here they are given
					for (int i = 0; i < 3; i++)
					{
						model.Comic.UpdatePageSize(i, PagesRig.ThreePages[i].Width, PagesRig.ThreePages[i].Height);
					}
					model.SortBy(PageColumns.Width); // 60, 60, 90: ties keep the page order
					Assert.Equal(new[] { "p1.png", "p2.png", "p10.png" }, Keys(model));
					model.SortBy(PageColumns.Width);
					Assert.False(model.SortAscending);
					Assert.Equal(new[] { "p10.png", "p1.png", "p2.png" }, Keys(model)); // 90 first, ties still in page order
					model.SortBy(PageColumns.Name); // ordinal: p1, p10, p2
					Assert.Equal(new[] { "p1.png", "p10.png", "p2.png" }, Keys(model));
					Assert.True(model.SortAscending);
					Assert.False(model.IsPageSorted);
					model.SortBy(PageColumns.Thumbnail); // no sorter: nothing
					Assert.Equal(PageColumns.Name, model.SortColumn);
					model.SortNone();
					Assert.Equal(new[] { "p1.png", "p2.png", "p10.png" }, Keys(model)); // the order of the book
					Assert.Equal(-1, model.SortColumn);
					Assert.Equal(4, changes); // the Thumbnail column sorts nothing and raises nothing
				}
			}
		}

		[Fact]
		public void A_list_sorted_by_the_type_puts_an_edited_row_where_it_belongs_now()
		{
			using (var rig = new PagesRig())
			{
				string file = rig.MakeCbz("a.cbz", PagesRig.ThreePages);
				using (PageListModel model = rig.Open(rig.Book(file), 3))
				{
					model.SortBy(PageColumns.Type); // Front Cover, Story, Story
					Assert.Equal(new[] { "p1.png", "p2.png", "p10.png" }, Keys(model));
					model.SetSelection(new[] { 2 });
					model.MarkAsDeleted(); // "Deleted" sorts before "Front Cover"
					Assert.Equal(new[] { "p10.png", "p1.png", "p2.png" }, Keys(model));
					Assert.Equal(new[] { 0 }, model.Selection.ToArray()); // the selection follows the page
				}
			}
		}

		[Fact]
		public void Page_type_rotation_and_position_are_set_on_the_selected_pages_and_the_menu_reads_them_back()
		{
			using (var rig = new PagesRig())
			{
				string file = rig.MakeCbz("a.cbz", PagesRig.ThreePages);
				using (PageListModel model = rig.Open(rig.Book(file), 3))
				{
					Assert.Null(model.CommonPageType); // nothing selected
					model.SetSelection(new[] { 1, 2 });
					Assert.Equal(ComicPageType.Story, model.CommonPageType);
					model.SetPageType(ComicPageType.Advertisement);
					Assert.Equal(new[] { "Front Cover", "Advertisement", "Advertisement" }, Names(model, PageColumns.Type));
					model.SetRotation(ImageRotation.Rotate90);
					Assert.Equal(ImageRotation.Rotate90, model.CommonRotation);
					Assert.Equal(new[] { "0°", "90°", "90°" }, Names(model, PageColumns.Rotation));
					model.SetPosition(ComicPagePosition.Far);
					Assert.Equal(ComicPagePosition.Far, model.CommonPosition);
					model.SetSelection(new[] { 0, 1 });
					Assert.Null(model.CommonPageType); // they differ now
					Assert.Null(model.CommonRotation);
					// the book holds the changes: the copy
					Assert.Equal(ComicPageType.Advertisement, model.Comic.GetPage(1).PageType);
					Assert.Equal(ComicPageType.FrontCover, model.Comic.GetPage(0).PageType);
				}
			}
		}

		[Fact]
		public void Mark_as_Deleted_flips_each_selected_page_between_deleted_and_story()
		{
			using (var rig = new PagesRig())
			{
				string file = rig.MakeCbz("a.cbz", PagesRig.ThreePages);
				using (PageListModel model = rig.Open(rig.Book(file), 3))
				{
					model.SetSelection(new[] { 0, 1 });
					model.MarkAsDeleted();
					Assert.Equal(new[] { "Deleted", "Deleted", "Story" }, Names(model, PageColumns.Type));
					Assert.True(model.State(0).IsDeleted);
					Assert.False(model.State(2).IsDeleted);
					model.MarkAsDeleted();
					Assert.Equal(new[] { "Story", "Story", "Story" }, Names(model, PageColumns.Type)); // a deleted page becomes a story page again, the cover too
				}
			}
		}

		[Fact]
		public void A_bookmark_needs_one_selected_page_proposes_a_name_and_can_be_removed()
		{
			using (var rig = new PagesRig())
			{
				string file = rig.MakeCbz("a.cbz", PagesRig.ThreePages);
				using (PageListModel model = rig.Open(rig.Book(file), 3))
				{
					Assert.False(model.CanBookmark);
					model.SetSelection(new[] { 0, 1 });
					Assert.False(model.CanBookmark); // two pages
					Assert.Null(model.BookmarkProposal);
					model.SetSelection(new[] { 1 });
					Assert.True(model.CanBookmark);
					Assert.Equal("Page 2", model.BookmarkProposal);
					Assert.False(model.CanRemoveBookmark);
					model.SetBookmark("Chapter 1");
					Assert.Equal("Chapter 1", model.Bookmark);
					Assert.Equal("Chapter 1", model.BookmarkProposal);
					Assert.True(model.State(1).IsBookmarked);
					Assert.Equal(new[] { "", "Chapter 1", "" }, Names(model, PageColumns.Bookmark).Select(t => t ?? string.Empty).ToArray());
					Assert.True(model.CanRemoveBookmark);
					model.RemoveBookmark();
					Assert.Null(model.Bookmark);
					Assert.False(model.State(1).IsBookmarked);
				}
			}
		}

		[Fact]
		public void Move_to_Top_and_Bottom_move_the_selected_pages_and_keep_them_selected()
		{
			using (var rig = new PagesRig())
			{
				string file = rig.MakeCbz("a.cbz", PagesRig.ThreePages);
				using (PageListModel model = rig.Open(rig.Book(file), 3))
				{
					model.SetSelection(new[] { 2 });
					model.MoveSelectedToTop();
					Assert.Equal(new[] { "p10.png", "p1.png", "p2.png" }, Keys(model));
					Assert.Equal(new[] { 0 }, model.Selection.ToArray()); // the moved page stays selected, at its new row
					Assert.Equal(new[] { "1", "2", "3" }, Names(model, PageColumns.Page));
					// the book holds the order
					Assert.Equal(2, model.Comic.GetPage(0).ImageIndex);

					model.SetSelection(new[] { 0 });
					model.MoveSelectedToBottom();
					Assert.Equal(new[] { "p1.png", "p2.png", "p10.png" }, Keys(model));
					Assert.Equal(new[] { 2 }, model.Selection.ToArray());
				}
			}
		}

		[Fact]
		public void Moving_the_cover_to_the_bottom_makes_the_page_that_takes_its_place_the_cover()
		{
			using (var rig = new PagesRig())
			{
				string file = rig.MakeCbz("a.cbz", PagesRig.ThreePages);
				using (PageListModel model = rig.Open(rig.Book(file), 3))
				{
					model.SetSelection(new[] { 0 }); // the front cover
					model.MoveSelectedToBottom();
					Assert.Equal(new[] { "p2.png", "p10.png", "p1.png" }, Keys(model));
					// MoveSelectedPageEnd: the moved cover becomes "Other", the page at the index of the number of moved pages (the second one) becomes the front cover
					Assert.Equal(new[] { "Front Cover", "Story", "Other" }, Names(model, PageColumns.Type));
				}
			}
		}

		[Fact]
		public void Order_by_Name_is_ordinal_and_numeric_follows_the_numbers_and_Reset_brings_the_book_back()
		{
			using (var rig = new PagesRig())
			{
				string file = rig.MakeCbz("a.cbz", PagesRig.ThreePages);
				using (PageListModel model = rig.Open(rig.Book(file), 3))
				{
					Assert.True(model.CanOrderByName);
					model.OrderByName();
					Assert.Equal(new[] { "p1.png", "p10.png", "p2.png" }, Keys(model));
					Assert.Equal(new[] { "1", "2", "3" }, Names(model, PageColumns.Page));
					model.OrderByNameNumeric();
					Assert.Equal(new[] { "p1.png", "p2.png", "p10.png" }, Keys(model));
					model.OrderByName();
					model.ResetPages();
					Assert.Equal(new[] { "p1.png", "p2.png", "p10.png" }, Keys(model));
					Assert.Empty(model.Selection);

					model.OrderByName();
					model.SetSelection(new[] { 2 });
					model.ResetOriginalOrder();
					Assert.Equal(new[] { "p1.png", "p2.png", "p10.png" }, Keys(model));
					Assert.Equal(new[] { 1 }, model.Selection.ToArray()); // Reset original Order keeps the selected page (it was p2.png at row 2)
				}
			}
		}

		[Fact]
		public void Select_All_and_Invert_Selection_work_on_the_rows()
		{
			using (var rig = new PagesRig())
			{
				string file = rig.MakeCbz("a.cbz", PagesRig.ThreePages);
				using (PageListModel model = rig.Open(rig.Book(file), 3))
				{
					model.SelectAll();
					Assert.Equal(new[] { 0, 1, 2 }, model.Selection.ToArray());
					model.SetSelection(new[] { 1 });
					model.InvertSelection();
					Assert.Equal(new[] { 0, 2 }, model.Selection.ToArray());
					Assert.Equal(0, model.FirstSelectedPage(7)); // the first selected row is page 0
					model.SetSelection(Array.Empty<int>());
					Assert.Equal(7, model.FirstSelectedPage(7)); // nothing selected: the fallback
				}
			}
		}

		[Fact]
		public void Save_writes_the_pages_of_the_copy_into_the_book_once_the_index_is_complete()
		{
			using (var rig = new PagesRig())
			{
				string file = rig.MakeCbz("a.cbz", PagesRig.ThreePages);
				ComicBook book = rig.Book(file);
				using (PageListModel model = rig.Open(book, 3))
				{
					model.SavePagesTo(book, out bool untouched); // the sizes the file index taught the copy are a change of the pages, not of the cover
					Assert.False(untouched);
					model.SetSelection(new[] { 1 });
					model.SetPageType(ComicPageType.Letters);
					model.SetRotation(ImageRotation.Rotate180);
					model.SetSelection(new[] { 2 });
					model.MoveSelectedToTop();
					model.SetSelection(new[] { 1 }); // the old cover, now second
					model.SetPageType(ComicPageType.Story);
					model.SetSelection(new[] { 0 });
					model.SetPageType(ComicPageType.FrontCover); // the cover is the page of the new first row: another picture
					// the library's book is not touched while the dialog edits
					Assert.Equal(ComicPageType.Story, book.GetPage(1).PageType);
					Assert.True(model.SavePagesTo(book, out bool coverChanged));
					Assert.True(coverChanged); // page 3 is the first page now: another picture on the cover
					Assert.Equal(3, book.PageCount);
					Assert.Equal(2, book.GetPage(0).ImageIndex);
					Assert.Equal(ComicPageType.Letters, book.GetPageByImageIndex(1).PageType);
					Assert.Equal(ImageRotation.Rotate180, book.GetPageByImageIndex(1).Rotation);
					Assert.False(model.SavePagesTo(book)); // the same pages again: no change
				}
			}
		}

		[Fact]
		public void The_last_page_read_is_kept_inside_the_page_count_when_it_is_saved()
		{
			using (var rig = new PagesRig())
			{
				string file = rig.MakeCbz("a.cbz", PagesRig.ThreePages);
				ComicBook book = rig.Book(file);
				book.LastPageRead = 20;
				using (PageListModel model = rig.Open(book, 3))
				{
					model.SavePagesTo(book);
					Assert.Equal(2, book.LastPageRead);
				}
			}
		}

		[Fact]
		public void A_page_filter_hides_the_other_kinds_of_pages()
		{
			using (var rig = new PagesRig())
			{
				string file = rig.MakeCbz("a.cbz", PagesRig.ThreePages);
				using (PageListModel model = rig.Open(rig.Book(file), 3))
				{
					model.SetSelection(new[] { 1 });
					model.MarkAsDeleted();
					model.PageFilter = ComicPageType.All; // not the deleted ones
					Assert.Equal(new[] { "p1.png", "p10.png" }, Keys(model));
					model.PageFilter = ComicPageType.AllWithDeleted;
					Assert.Equal(3, model.Count);
				}
			}
		}

		[Fact]
		public void The_tile_lines_are_the_texts_of_the_original_page_tile()
		{
			var info = new ComicPageInfo(2) { ImageFileSize = 2048, ImageWidth = 60, ImageHeight = 90, Rotation = ImageRotation.Rotate90, Bookmark = "Start" };
			List<TileTextLine> lines = PageTileText.Build(info, 2);
			string[] texts = lines.Where(l => !l.Separator).Select(l => l.Text).ToArray();
			Assert.Equal("Page #3", texts[0]);
			Assert.Equal("Story", texts[1]);
			Assert.Contains("2", texts[2]); // Size: 2 KB
			Assert.Contains("60 x 90", texts[3]);
			Assert.Contains("90°", texts[4]);
			Assert.Contains("Start", texts[5]);
			Assert.Equal(new[] { 10, 6 }, lines.Where(l => l.Separator).Select(l => l.Spacing).ToArray());
			Assert.Single(PageTileText.Build(ComicPageInfo.Empty, 0));
		}

		[Fact]
		public void The_pictures_of_the_rows_come_from_the_thumbnail_service_stay_with_their_image_when_pages_move_and_Refresh_asks_again()
		{
			using (var rig = new PagesRig())
			{
				string file = rig.MakeCbz("a.cbz", PagesRig.ThreePages);
				using (PageListModel model = rig.Open(rig.Book(file), 3))
				{
					int arrived = 0;
					model.PicturesChanged += (s, e) => arrived++;
					Assert.Null(model.Picture(0, 128)); // asked for, made in the background
					Assert.Null(model.Picture(0, 128)); // not twice
					rig.Ui.PumpUntil(() => arrived == 1);
					CoverImage first = model.Picture(0, 128);
					Assert.NotNull(first);
					Assert.InRange(first.Width / (double)first.Height, 0.6, 0.72); // the portrait page, 60 x 90
					first.Release();
					long started = rig.Thumbnails.GetStats().DecodesStarted;

					// the picture belongs to the image, not to the row: moved to another row it is there at once
					model.SetSelection(new[] { 0 });
					model.MoveSelectedToBottom();
					Assert.Equal("p1.png", model.Rows[2].Key);
					CoverImage moved = model.Picture(2, 128);
					Assert.NotNull(moved);
					moved.Release();
					Assert.Equal(started, rig.Thumbnails.GetStats().DecodesStarted);

					// turned, it is another picture: its key has the rotation
					model.SetSelection(new[] { 2 });
					model.SetRotation(ImageRotation.Rotate90);
					Assert.Null(model.Picture(2, 128));
					rig.Ui.PumpUntil(() => arrived == 2);
					CoverImage turned = model.Picture(2, 128);
					Assert.NotNull(turned);
					Assert.True(turned.Width > turned.Height); // turned by 90 degrees: the portrait page is landscape now
					turned.Release();

					// Refresh: the service forgets the book's pictures, the rows ask again
					started = rig.Thumbnails.GetStats().DecodesStarted;
					model.SetSelection(new[] { 2 });
					model.RefreshSelected();
					Assert.Null(model.Picture(2, 128));
					rig.Ui.PumpUntil(() => arrived >= 3);
					Assert.True(rig.Thumbnails.GetStats().DecodesStarted > started);
				}
			}
		}

		[Fact]
		public void Merge_Pages_puts_the_two_pictures_side_by_side_under_the_first_page_and_deletes_the_second()
		{
			using (var rig = new PagesRig())
			{
				string file = rig.MakeCbz("a.cbz", PagesRig.ThreePages);
				using (PageListModel model = rig.Open(rig.Book(file), 3))
				{
					Assert.False(model.CanMergePages);
					model.SetSelection(new[] { 0, 1 });
					Assert.True(model.CanMergePages);
					Assert.True(model.MergePages());
					Assert.Equal(new[] { "Front Cover", "Deleted", "Story" }, Names(model, PageColumns.Type));
					var key = model.Navigator.GetPageKey(0);
					using (var item = rig.Host.ImagePool.Pages.GetImage(key, true))
					{
						Assert.NotNull(item);
						Assert.Equal(120, item.Item.Bitmap.Width); // two pages of 60 units side by side
					}
				}
			}
		}
	}
}
