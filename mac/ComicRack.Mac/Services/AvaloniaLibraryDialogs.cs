using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ComicRack.Mac.Views;

namespace ComicRack.Mac.Services
{
	/// <summary>The real dialogs: the Avalonia StorageProvider folder picker and two small windows, all owned by the main window.</summary>
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

		public Task ShowMessageAsync(string title, string message)
		{
			return new MessageWindow(title, message).ShowDialog(owner);
		}
	}
}
