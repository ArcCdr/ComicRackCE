using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Browser
{
	/// <summary>
	/// The text of the tool tip of a book (<c>ShowToolTips</c>): the lines of <c>ComicTextElements.DefaultComic</c> that <c>ComicBrowserControl.toolTip_Draw</c>
	/// draws next to the cover: the caption without the title, the title, the artists, the summary, the size and the format, with the texts of the
	/// <c>ComicBook</c> section of the language files (<c>ComicTextBuilder</c>).
	/// </summary>
	public sealed class BookToolTipData
	{
		/// <summary>The caption without the title, then the title (bold lines).</summary>
		public string Caption { get; set; }

		public string Title { get; set; }

		public string Artists { get; set; }

		public string Summary { get; set; }

		/// <summary>"Size: 54.5 MB/32 Pages" and "Format: CBZ".</summary>
		public string Size { get; set; }

		public string Format { get; set; }

		/// <summary>The texts that are not empty, in order.</summary>
		public string[] Lines
		{
			get
			{
				var lines = new System.Collections.Generic.List<string>();
				foreach (string line in new[] { Caption, Title, Artists, Summary, Size, Format })
				{
					if (!string.IsNullOrWhiteSpace(line))
					{
						lines.Add(line);
					}
				}
				return lines.ToArray();
			}
		}

		public static BookToolTipData From(ComicBook book)
		{
			if (book == null)
			{
				return new BookToolTipData { Caption = UiText.Raw("ComicBook", "NoInformation", "No information available") };
			}
			string size = UiText.Raw("ComicBook", "Size", "Size:\t{0}/{1}");
			string format = UiText.Raw("ComicBook", "Format", "Format:\t{0}");
			return new BookToolTipData
			{
				Caption = book.CaptionWithoutTitle,
				Title = book.ShadowTitle,
				Artists = book.ArtistInfo,
				Summary = (book.Summary ?? string.Empty).Replace("\t", string.Empty),
				Size = book.IsLinked ? string.Format(size.Replace('\t', ' '), book.FileSizeAsText, book.PagesAsText) : null,
				Format = book.IsLinked ? string.Format(format.Replace('\t', ' '), book.FileFormat) : null,
			};
		}
	}
}
