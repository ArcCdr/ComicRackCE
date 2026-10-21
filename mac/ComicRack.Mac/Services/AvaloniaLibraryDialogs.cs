using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ComicRack.Mac.Platform;
using ComicRack.Mac.Views;

namespace ComicRack.Mac.Services
{
	/// <summary>The real dialogs: the Avalonia StorageProvider folder picker, the Import window and the system alert, all owned by the main window.</summary>
	public sealed class AvaloniaLibraryDialogs : ILibraryDialogs
	{
		private readonly Window owner;

		public AvaloniaLibraryDialogs(Window owner)
		{
			this.owner = owner;
		}

		public async Task<string> PickFolderAsync(string title, string startFolder = null)
		{
			IStorageFolder start = string.IsNullOrEmpty(startFolder) ? null : await owner.StorageProvider.TryGetFolderFromPathAsync(startFolder);
			IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
			{
				Title = title,
				AllowMultiple = false,
				SuggestedStartLocation = start,
			});
			return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
		}

		public Task<ImportRequest> AskImportAsync()
		{
			return new ImportWindow().ShowDialog<ImportRequest>(owner);
		}

		/// <summary>A problem the user must know about: the system alert (C-DLG-1, DL.34), on the UI thread; the alert window where that cannot run.</summary>
		public async Task ShowMessageAsync(string title, string message)
		{
			await MacAlert.ShowAsync(owner, new MacAlertOptions
			{
				Message = title ?? string.Empty,
				Informative = message ?? string.Empty,
				Style = MacAlertStyle.Warning,
			});
		}

		/// <summary>
		/// A question (<c>QuestionDialog</c>): the system alert when it has no check boxes of its own to show (the "do not ask again" box is the alert's), else
		/// ComicRack's question window. The buttons are OK and Cancel with the texts of the original's <c>QuestionDialog</c> unless the request names others.
		/// </summary>
		public async Task<QuestionAnswer> AskQuestionAsync(QuestionRequest request)
		{
			var buttons = new List<string>
			{
				request.OkText ?? UiText.Get("QuestionDialog", "btOK", "&OK"),
			};
			if (request.ShowCancel)
			{
				buttons.Add(request.CancelText ?? UiText.Get("QuestionDialog", "btCancel", "&Cancel"));
			}
			for (int i = 0; i < buttons.Count; i++)
			{
				buttons[i] = ComicRack.Mac.Controls.CrDialogWindow.StripMnemonic(buttons[i]);
			}
			MacAlertOptions options = MacAlertOptions.FromQuestion(request.Question, buttons.ToArray());
			options.Style = MacAlertStyle.Warning;
			options.CancelIndex = request.ShowCancel ? 1 : -1;
			options.SuppressionText = string.IsNullOrEmpty(request.SuppressionText) ? null : ComicRack.Mac.Controls.CrDialogWindow.StripMnemonic(request.SuppressionText);
			bool option2Dependent = !request.Option2Independent;
			bool hasOption = !string.IsNullOrEmpty(request.OptionText);
			if (hasOption)
			{
				options.Options.Add(Option(request.OptionText, false));
			}
			if (!string.IsNullOrEmpty(request.Option2Text))
			{
				options.Options.Add(Option(request.Option2Text, hasOption && option2Dependent));
			}
			MacAlertResult result = await MacAlert.ShowAsync(owner, options);
			bool accepted = result.ButtonIndex == 0;
			bool first = hasOption && result.Options.Count > 0 && result.Options[0];
			bool second = !string.IsNullOrEmpty(request.Option2Text) && result.Options.Count > (hasOption ? 1 : 0) && result.Options[hasOption ? 1 : 0];
			return new QuestionAnswer(accepted, first, second, result.Suppressed);
		}

		// "!" at the start of an option text means checked from the start (QuestionDialog.Ask)
		private static MacAlertOption Option(string text, bool onlyWhilePreviousChecked)
		{
			bool isChecked = text.StartsWith("!");
			return new MacAlertOption(CrDialogText(isChecked ? text.Substring(1) : text), isChecked, onlyWhilePreviousChecked);
		}

		private static string CrDialogText(string text) => ComicRack.Mac.Controls.CrDialogWindow.StripMnemonic(text);

		/// <summary>The Info dialog over the main window.</summary>
		public Task<bool> ShowBookInfoAsync(BookInfoRequest request)
		{
			request.ShowProblem = message => ShowMessageAsync(UiText.Message("Attention", "Attention"), message);
			return new ComicBookDialog(request).ShowDialogAsync(owner);
		}

		/// <summary>The multi edit dialog over the main window.</summary>
		public Task<bool> ShowMultipleBooksAsync(MultipleBooksRequest request)
		{
			return new MultipleComicBooksDialog(request).ShowDialogAsync(owner);
		}

		/// <summary>The Quick Rating dialog over the main window.</summary>
		public Task<QuickRatingResult> AskQuickRatingAsync(QuickRatingRequest request)
		{
			return new QuickRatingWindow(request).ShowAsync(owner);
		}
	}
}
