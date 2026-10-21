using System.Threading.Tasks;
using ComicRack.Mac.Thumbnails;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Services
{
	/// <summary>
	/// A question as <c>QuestionDialog.AskQuestion</c> asks it (cYo.Common.Windows/Forms/QuestionDialog.cs). <see cref="Question"/> is the text with its
	/// line breaks (the first line is the question, the rest the description). <see cref="OptionText"/> and <see cref="Option2Text"/> are the optional
	/// check boxes; a text that starts with "!" is checked at the start and the "!" is dropped. The second option shows only while the first is checked
	/// unless <see cref="Option2Independent"/> is set. <see cref="SuppressionText"/> is the "do not ask again" box (<c>Program.AskQuestion</c> with a
	/// <c>HiddenMessageBoxes</c> flag); it is the system alert's own check box.
	/// </summary>
	public sealed class QuestionRequest
	{
		public string Question { get; set; } = string.Empty;

		/// <summary>The text of the OK button; null keeps the original's "OK" (<c>QuestionDialog.btOK</c>).</summary>
		public string OkText { get; set; }

		/// <summary>The text of the Cancel button; null keeps the original's "Cancel".</summary>
		public string CancelText { get; set; }

		/// <summary>False: the dialog has only the OK button (<c>ShowCancel</c>).</summary>
		public bool ShowCancel { get; set; } = true;

		public string OptionText { get; set; }

		public string Option2Text { get; set; }

		public bool Option2Independent { get; set; }

		public string SuppressionText { get; set; }
	}

	/// <summary>What the user answered: the flags of <c>QuestionResult</c>.</summary>
	public sealed class QuestionAnswer
	{
		public QuestionAnswer(bool accepted, bool option, bool option2, bool suppressed)
		{
			Accepted = accepted;
			Option = option;
			Option2 = option2;
			Suppressed = suppressed;
		}

		/// <summary>OK was pressed (else Cancel, Esc or the window was closed).</summary>
		public bool Accepted { get; }

		public bool Option { get; }

		public bool Option2 { get; }

		/// <summary>The "do not ask again" box was checked.</summary>
		public bool Suppressed { get; }

		/// <summary>OK, nothing checked.</summary>
		public static QuestionAnswer Ok { get; } = new QuestionAnswer(true, false, false, false);

		public static QuestionAnswer Cancel { get; } = new QuestionAnswer(false, false, false, false);
	}

	/// <summary>The data of the Quick Rating dialog (<c>QuickRatingDialog.Show</c>).</summary>
	public sealed class QuickRatingRequest
	{
		public ComicBook Book { get; set; }

		/// <summary>Where the front cover comes from, or null (no cover shown).</summary>
		public ThumbnailService Thumbnails { get; set; }

		/// <summary>The state of "Show when Book read" (<c>Settings.AutoShowQuickReview</c>).</summary>
		public bool ShowWhenRead { get; set; }
	}

	/// <summary>What the dialog returned after OK.</summary>
	public sealed class QuickRatingResult
	{
		public string Review { get; set; }

		public float Rating { get; set; }

		public bool ShowWhenRead { get; set; }
	}
}
