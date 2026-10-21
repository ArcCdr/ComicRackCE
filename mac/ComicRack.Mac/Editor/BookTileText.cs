using System;
using System.Collections.Generic;
using ComicRack.Mac.Controls;
using cYo.Common.Text;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Drawing;

namespace ComicRack.Mac.Editor
{
	/// <summary>
	/// The lines of text beside a book's cover: <c>ComicTextBuilder.GetTextBlocks</c> for a book, line by line and in the same order, with the texts of the <c>ComicBook</c> section
	/// of the language files. The original builds <c>TextLine</c> objects with fonts; the Drawing stand-in has no fonts, so this returns the style of each line
	/// (<see cref="TileTextStyle"/>) and <see cref="CrBookTile"/> draws them.
	/// </summary>
	public static class BookTileText
	{
		/// <summary>The elements the Info dialog's cover shows (<c>ComicBookDialog</c>: <c>coverThumbnail.TextElements</c>).</summary>
		public const ComicTextElements InfoDialogElements = ComicTextElements.CaptionWithoutTitle | ComicTextElements.Title | ComicTextElements.ArtistInfo | ComicTextElements.Summary
			| ComicTextElements.Opened | ComicTextElements.Added | ComicTextElements.Released | ComicTextElements.NoEmptyDates;

		private static string T(string key, string english) => UiText.Raw("ComicBook", key, english);

		/// <summary>The lines for <paramref name="book"/> (null: "No information available").</summary>
		public static List<TileTextLine> Build(ComicBook book, ComicTextElements flags)
		{
			var lines = new List<TileTextLine>();
			if (book == null)
			{
				lines.Add(new TileTextLine(T("NoInformation", "No information available"), TileTextStyle.Caption));
				return lines;
			}
			if (!book.IsLinked)
			{
				flags &= ~ComicTextElements.LinkedElements;
			}
			if (flags.HasFlag(ComicTextElements.StackTitle))
			{
				lines.Add(new TileTextLine(book.Series, TileTextStyle.Caption));
			}
			if (flags.HasFlag(ComicTextElements.Caption))
			{
				lines.Add(new TileTextLine(book.Caption, TileTextStyle.Caption));
			}
			if (flags.HasFlag(ComicTextElements.CaptionWithoutTitle))
			{
				lines.Add(new TileTextLine(book.CaptionWithoutTitle, TileTextStyle.Caption));
			}
			if (flags.HasFlag(ComicTextElements.Title))
			{
				lines.Add(new TileTextLine(book.ShadowTitle, TileTextStyle.Caption, noWrap: true));
			}
			if (flags.HasFlag(ComicTextElements.AlternateCaption))
			{
				lines.Add(new TileTextLine(book.AlternateCaption, TileTextStyle.Normal, noWrap: true));
			}
			if (flags.HasFlag(ComicTextElements.PublisherAndImprint))
			{
				lines.Add(new TileTextLine(book.Publisher.AppendWithSeparator("/", book.Imprint, book.ISBN), TileTextStyle.Small, noWrap: true));
			}
			if (flags.HasFlag(ComicTextElements.AgeRating))
			{
				lines.Add(new TileTextLine(book.AgeRating, TileTextStyle.Small, noWrap: true));
			}
			if (flags.HasFlag(ComicTextElements.ArtistInfo))
			{
				lines.Add(new TileTextLine(book.ArtistInfo, TileTextStyle.Small, noWrap: true));
			}
			lines.Add(new TileTextLine(4));
			lines.Add(new TileTextLine(0)); // ScrollStart: sets the pending gap back to nothing, as the original does
			if (flags.HasFlag(ComicTextElements.Genre))
			{
				lines.Add(new TileTextLine(book.Genre, TileTextStyle.Normal, noWrap: true));
			}
			if (flags.HasFlag(ComicTextElements.CharactersAndTeams))
			{
				lines.Add(new TileTextLine(book.Characters.AppendWithSeparator(", ", book.Teams), TileTextStyle.Normal, noWrap: true));
			}
			if (flags.HasFlag(ComicTextElements.Locations))
			{
				lines.Add(new TileTextLine(book.Locations, TileTextStyle.Normal, noWrap: true));
			}
			lines.Add(new TileTextLine(4));
			if (flags.HasFlag(ComicTextElements.PurchaseInformation))
			{
				lines.Add(new TileTextLine(book.BookStore.AppendWithSeparator("/", book.BookPriceAsText), TileTextStyle.Normal, noWrap: true));
			}
			if (flags.HasFlag(ComicTextElements.StorageInfoformation))
			{
				lines.Add(new TileTextLine(book.BookLocation.AppendWithSeparator("/", book.BookAge, book.BookCondition), TileTextStyle.Normal, noWrap: true));
			}
			if (flags.HasFlag(ComicTextElements.CollectionStatus))
			{
				lines.Add(new TileTextLine(book.BookCollectionStatus, TileTextStyle.Normal, noWrap: true));
			}
			lines.Add(new TileTextLine(6));
			if (flags.HasFlag(ComicTextElements.Summary))
			{
				lines.Add(new TileTextLine((book.Summary ?? string.Empty).Replace("\t", string.Empty), TileTextStyle.Small, ellipsisWord: true));
			}
			if (flags.HasFlag(ComicTextElements.Notes))
			{
				lines.Add(new TileTextLine((book.Notes ?? string.Empty).AppendWithSeparator("\n", book.BookNotes).Replace("\t", string.Empty), TileTextStyle.Small, ellipsisWord: true));
			}
			if (flags.HasFlag(ComicTextElements.ScanInformation))
			{
				lines.Add(new TileTextLine(book.ScanInformation, TileTextStyle.Normal, noWrap: true));
			}
			lines.Add(new TileTextLine(6));
			if (flags.HasFlag(ComicTextElements.StackBookCount))
			{
				lines.Add(Small2(StringUtility.Format(T("BookCount", "Books:\t{0}"), book.CountAsText)));
			}
			if (flags.HasFlag(ComicTextElements.StackBooksOpened))
			{
				lines.Add(Small2(StringUtility.Format(T("Opened", "Opened:\t{0}"), book.LastPageRead)));
			}
			if (flags.HasFlag(ComicTextElements.FileSize))
			{
				lines.Add(Small2(StringUtility.Format(T("Size", "Size:\t{0}/{1}"), book.FileSizeAsText, book.PagesAsText)));
			}
			if (flags.HasFlag(ComicTextElements.Released) && (!flags.HasFlag(ComicTextElements.NoEmptyDates) || book.ReleasedTime != DateTime.MinValue))
			{
				lines.Add(Small2(StringUtility.Format(T("Released", "Released:\t{0}"), book.ReleasedTimeAsText)));
			}
			if (flags.HasFlag(ComicTextElements.Opened) && (!flags.HasFlag(ComicTextElements.NoEmptyDates) || book.OpenedTime != DateTime.MinValue))
			{
				lines.Add(Small2(StringUtility.Format(T("Opened", "Opened:\t{0}"), book.OpenedTimeAsText)));
			}
			if (flags.HasFlag(ComicTextElements.Added) && (!flags.HasFlag(ComicTextElements.NoEmptyDates) || book.AddedTime != DateTime.MinValue))
			{
				lines.Add(Small2(StringUtility.Format(T("Added", "Added:\t{0}"), book.AddedTimeAsText)));
			}
			if (flags.HasFlag(ComicTextElements.FileFormat))
			{
				lines.Add(Small2(StringUtility.Format(T("Format", "Format:\t{0}"), book.FileFormat)));
			}
			if (flags.HasFlag(ComicTextElements.FileName))
			{
				lines.Add(Small2(StringUtility.Format(T("FileName", "File:\t{0}"), book.FileNameWithExtension)));
			}
			return lines;
		}

		private static TileTextLine Small2(string text) => new TileTextLine(text, TileTextStyle.SmallSmall, noWrap: true);
	}
}
