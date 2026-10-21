using System;
using Avalonia;
using Avalonia.Controls;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Services;
using cYo.Common.Text;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The multi edit dialog (DL.02, <c>ComicRack/Dialogs/MultipleComicBooksDialog.cs</c>): a form of 525 x 460 (client) with the panel of groups at (12,12), 497 x 407, and OK and Cancel at
	/// the bottom right (OK 77 wide, Cancel 80, at y 427). The title is "Multiple Book Information (N)". OK writes the checked fields into every selected book
	/// (<see cref="MultiBookEditorModel.Store"/>); Cancel and Escape write nothing. The caller sets the undo marker "Edit multiple Books" first, so what OK writes is one undo step.
	/// Spec: <c>mac/specs/views/MultipleComicBooksDialog.md</c>.
	/// </summary>
	public sealed class MultipleComicBooksDialog : CrDialogWindow
	{
		/// <summary>The client size of the form in the Designer (<c>ClientSize</c> 525 x 460).</summary>
		public const double DesignWidth = 525;

		public const double DesignHeight = 460;

		private readonly MultipleBooksRequest request;
		private readonly MultipleBooksBody bodyView = new MultipleBooksBody();

		public MultipleComicBooksDialog(MultipleBooksRequest request)
		{
			this.request = request ?? throw new ArgumentNullException(nameof(request));
			MultiBookEditorModel model = request.Model ?? throw new ArgumentException("The request has no editor.", nameof(request));
			TrSection = MultipleBooksBody.Section;
			Buttons = DialogButtons.OkCancel;
			OkButton.Width = 77; // btOK is 77 wide, btCancel 80
			// pageData ends at y 419 and the buttons start at 427; they end at 451, 9 above the bottom, and the right edge of Cancel is 16 from the right
			ButtonBarMargin = new Thickness(0, 8, 16, 9);
			Title = StringUtility.Format(UiText.Raw(MultipleBooksBody.Section, "MultipleComicBooksDialog", "Multiple Book Information ({0})"), model.Count);
			bodyView.Initialize(model, request.Preferences);
			var host = new Panel { Width = DesignWidth, Height = 419 };
			DesignerLayout.Place(host, bodyView, 12, 12, 497, 407, Anchor.TopLeft, DesignWidth, 419);
			Body = host;
			OkClicked += (sender, e) => model.Store();
			Closing += (sender, e) => bodyView.SaveViewState();
		}

		public MultiBookEditorModel Model => request.Model;

		/// <summary>The panel of groups and its check boxes.</summary>
		public MultipleBooksBody BodyView => bodyView;
	}
}
