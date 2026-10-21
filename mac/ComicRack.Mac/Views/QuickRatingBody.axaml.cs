using Avalonia.Controls;
using ComicRack.Mac.Controls;

namespace ComicRack.Mac.Views
{
	/// <summary>The controls of the Quick Rating dialog (see <see cref="QuickRatingWindow"/>).</summary>
	public partial class QuickRatingBody : UserControl
	{
		public QuickRatingBody()
		{
			InitializeComponent();
		}

		public CoverView CoverBox => Cover;

		public CrRatingStars RatingBox => Stars;

		public TextBox Review => ReviewBox;
	}
}
