using System.Collections.Generic;
using System.Threading.Tasks;
using ComicRack.Mac.Host.Library;

namespace ComicRack.Mac.Services
{
	/// <summary>What the Import dialog returns: the Windows database to copy and the path-prefix mappings to apply to it.</summary>
	public sealed class ImportRequest
	{
		public string DatabasePath { get; }

		public IReadOnlyList<PathRemapRule> Mappings { get; }

		public ImportRequest(string databasePath, IReadOnlyList<PathRemapRule> mappings)
		{
			DatabasePath = databasePath;
			Mappings = mappings ?? new PathRemapRule[0];
		}
	}

	/// <summary>
	/// The dialogs of the library commands, behind an interface so that view models and tests do not need a window. The app's
	/// implementation (<see cref="AvaloniaLibraryDialogs"/>) uses the Avalonia StorageProvider, the system alert and the Import window; tests use a fake.
	/// </summary>
	public interface ILibraryDialogs
	{
		/// <summary>Asks for a folder, starting in <paramref name="startFolder"/> when it is given. Returns its local path, or null if the user cancelled.</summary>
		Task<string> PickFolderAsync(string title, string startFolder = null);

		/// <summary>Asks for a database file and the prefix mappings. Returns null if the user cancelled.</summary>
		Task<ImportRequest> AskImportAsync();

		/// <summary>Shows a problem the user must know about (a failed import, a failed save).</summary>
		Task ShowMessageAsync(string title, string message);

		/// <summary>
		/// Asks a question (<c>QuestionDialog</c>, <c>Program.AskQuestion</c>). The default answers OK with nothing checked, so a fake that does not care lets
		/// the command go on.
		/// </summary>
		Task<QuestionAnswer> AskQuestionAsync(QuestionRequest request) => Task.FromResult(QuestionAnswer.Ok);

		/// <summary>The Quick Rating dialog (DL.18). Null: cancelled. The default cancels.</summary>
		Task<QuickRatingResult> AskQuickRatingAsync(QuickRatingRequest request) => Task.FromResult<QuickRatingResult>(null);

		/// <summary>The Info dialog (DL.01, <c>ComicBookDialog</c>): true after OK. It writes the books itself (OK, Apply, Previous and Next call the model). The default cancels.</summary>
		Task<bool> ShowBookInfoAsync(BookInfoRequest request) => Task.FromResult(false);

		/// <summary>The multi edit dialog (DL.02, <c>MultipleComicBooksDialog</c>): true after OK, which has written the checked fields into the books. The default cancels.</summary>
		Task<bool> ShowMultipleBooksAsync(MultipleBooksRequest request) => Task.FromResult(false);
	}
}
