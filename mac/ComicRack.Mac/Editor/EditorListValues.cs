using System.Collections.Generic;
using ComicRack.Mac.Host.Library;

namespace ComicRack.Mac.Editor
{
	/// <summary>
	/// The values a box of the book editors offers for an <see cref="EditorList"/> (the <c>DefaultLists</c> calls in the constructors of <c>ComicBookDialog</c> and
	/// <c>MultipleComicBooksDialog</c>): the values the library's books hold plus the defaults.
	/// </summary>
	public static class EditorListValues
	{
		/// <param name="lists">The value lists of the library.</param>
		/// <param name="list">The box.</param>
		/// <param name="rawTitle">The title list of the multi edit dialog uses the title as it is (<c>cb.Title</c>); the Info dialog uses the shadow title.</param>
		public static List<string> Get(EditorValueLists lists, EditorList list, bool rawTitle = false)
		{
			switch (list)
			{
				case EditorList.Title: return lists.GetComicFieldList(b => rawTitle ? b.Title : b.ShadowTitle);
				case EditorList.Series: return lists.GetComicFieldList(b => b.ShadowSeries);
				case EditorList.Writer: return lists.GetComicFieldList(b => b.Writer);
				case EditorList.Genre: return lists.GetGenreList(withSeparator: false);
				case EditorList.Tags: return lists.GetComicFieldList(b => b.Tags);
				case EditorList.AlternateSeries: return lists.GetComicFieldList(b => b.AlternateSeries);
				case EditorList.StoryArc: return lists.GetComicFieldList(b => b.StoryArc);
				case EditorList.SeriesGroup: return lists.GetComicFieldList(b => b.SeriesGroup);
				case EditorList.Penciller: return lists.GetComicFieldList(b => b.Penciller);
				case EditorList.Colorist: return lists.GetComicFieldList(b => b.Colorist);
				case EditorList.Inker: return lists.GetComicFieldList(b => b.Inker);
				case EditorList.Letterer: return lists.GetComicFieldList(b => b.Letterer);
				case EditorList.CoverArtist: return lists.GetComicFieldList(b => b.CoverArtist);
				case EditorList.Editor: return lists.GetComicFieldList(b => b.Editor);
				case EditorList.Translator: return lists.GetComicFieldList(b => b.Translator);
				case EditorList.Characters: return lists.GetComicFieldList(b => b.Characters);
				case EditorList.Teams: return lists.GetComicFieldList(b => b.Teams);
				case EditorList.MainCharacterOrTeam: return lists.GetComicFieldList(b => b.MainCharacterOrTeam);
				case EditorList.Locations: return lists.GetComicFieldList(b => b.Locations);
				case EditorList.ScanInformation: return lists.GetComicFieldList(b => b.ScanInformation);
				case EditorList.AgeRating: return lists.GetAgeRatingList();
				case EditorList.Format: return lists.GetFormatList();
				case EditorList.Publisher: return lists.GetComicFieldList(b => b.Publisher, sort: true);
				case EditorList.Imprint: return lists.GetComicFieldList(b => b.Imprint, sort: true);
				case EditorList.BookAge: return lists.GetBookAgeList();
				case EditorList.BookStore: return lists.GetComicFieldList(b => b.BookStore, sort: true);
				case EditorList.BookOwner: return lists.GetComicFieldList(b => b.BookOwner, sort: true);
				case EditorList.BookCondition: return lists.GetBookConditionList();
				case EditorList.BookPrice: return lists.GetComicFieldList(b => b.BookPriceAsText, sort: true);
				case EditorList.BookLocation: return lists.GetComicFieldList(b => b.BookLocation, sort: true);
				case EditorList.CollectionStatus: return lists.GetBookCollectionStatusList();
				default: return new List<string>();
			}
		}
	}
}
