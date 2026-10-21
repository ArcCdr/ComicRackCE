using ComicRack.Mac.Editor;
using ComicRack.Mac.Host;

namespace ComicRack.Mac.Services
{
	/// <summary>The data of the multi edit dialog (<c>new MultipleComicBooksDialog(books)</c>): the editor model over the selected books and the settings the dialog keeps.</summary>
	public sealed class MultipleBooksRequest
	{
		/// <summary>What is edited; OK calls <see cref="MultiBookEditorModel.Store"/>.</summary>
		public MultiBookEditorModel Model { get; set; }

		/// <summary>The settings (the tab mode of the groups, <c>TabLayouts.Multiple</c>); null keeps nothing.</summary>
		public AppPreferences Preferences { get; set; }
	}
}
