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

		public async Task<string> PickFolderAsync(string title)
		{
			IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
			{
				Title = title,
				AllowMultiple = false,
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
	}
}
