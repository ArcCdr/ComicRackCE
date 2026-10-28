using System;

namespace ComicRack.Compat.VisualBasic
{
	/// <summary>Where a file goes when a script asks for the Recycle Bin (<c>RecycleOption.SendToRecycleBin</c>). The program supplies the Trash; tests supply a fake one.</summary>
	public interface ITrash
	{
		/// <summary>Moves a file or folder to the Trash. Throws when it cannot: the caller never falls back to deleting for good.</summary>
		void MoveToTrash(string fullPath);
	}

	/// <summary>Where <c>UIOption.AllDialogs</c> and <c>OnlyErrorDialogs</c> show an error (the script UI of the program).</summary>
	public interface IFileSystemUi
	{
		/// <summary>Shows <paramref name="message"/> (the text of the failure) and returns when the user has dismissed it.</summary>
		void ShowError(string message);
	}

	/// <summary>
	/// The services behind <c>Microsoft.VisualBasic.FileIO.FileSystem</c> on macOS. They are process-wide, like the Windows shell they replace; the program sets them when its host starts.
	/// With no <see cref="Trash"/> the Recycle Bin route fails (it never deletes for good); with no <see cref="Ui"/> an error is not shown, only thrown.
	/// </summary>
	public static class FileSystemServices
	{
		private static volatile ITrash trash;
		private static volatile IFileSystemUi ui;

		public static ITrash Trash
		{
			get => trash;
			set => trash = value;
		}

		public static IFileSystemUi Ui
		{
			get => ui;
			set => ui = value;
		}
	}
}
