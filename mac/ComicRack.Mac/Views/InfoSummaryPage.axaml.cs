using Avalonia.Controls;
using ComicRack.Mac.Controls;

namespace ComicRack.Mac.Views
{
	/// <summary>The Summary tab of the Info dialog (<c>tabSummary</c>, IN.01): the cover tile, the file facts, My Rating and Community Rating, the thumbnail and link buttons, the web link.</summary>
	public partial class InfoSummaryPage : UserControl
	{
		public InfoSummaryPage()
		{
			InitializeComponent();
			InfoPageText.Translate(this);
		}

		public CrBookTile Tile => CoverTile;

		public CrRatingStars MyRating => txRating;

		public CrRatingStars CommunityRating => txCommunityRating;

		public SplitButton ThumbnailButton => btThumbnail;

		public Button LinkFileButton => btLinkFile;

		public TextBlock WebLink => linkLabel;

		public TextBlock FileType => lblType;

		public TextBlock FilePath => lblPath;

		public Label Pages => lblPages;
	}
}
