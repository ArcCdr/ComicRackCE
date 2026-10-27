using System;
using System.Threading.Tasks;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Host;
using ComicRack.Mac.Scripting;
using ComicRack.Mac.Thumbnails;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Services
{
	/// <summary>The data of the Info dialog (<c>ComicBookDialog.Show</c>): the editor model and the services the window needs next to it.</summary>
	public sealed class BookInfoRequest
	{
		/// <summary>What is edited and how the dialog moves between books.</summary>
		public BookEditorModel Model { get; set; }

		/// <summary>Where the covers come from, or null (no cover shown).</summary>
		public ThumbnailService Thumbnails { get; set; }

		/// <summary>The settings (the size of the dialog, the recent thumbnail files); null keeps nothing.</summary>
		public AppPreferences Preferences { get; set; }

		/// <summary>Makes a custom thumbnail of a picture file in the Core's image pool and returns its key, or null when the picture cannot be used (<c>Program.LoadCustomThumbnail</c>).</summary>
		public Func<string, string> AddCustomThumbnail { get; set; }

		/// <summary>The book of the library that has this file, or null (Link to File refuses a file the library already uses).</summary>
		public Func<string, ComicBook> FindBook { get; set; }

		/// <summary>Tells the user about a problem with a system alert ("This file is already used in the Library").</summary>
		public Func<string, Task> ShowProblem { get; set; }

		/// <summary>Opens a web address in the browser (the link under the Summary tab).</summary>
		public Action<string> OpenUrl { get; set; }

		/// <summary>The commands of the <c>Editor</c> hook (<c>InitializeScriptButton</c>): the script button of the dialog lists them. Null or empty: no button.</summary>
		public System.Collections.Generic.IReadOnlyList<ScriptCommandItem> EditorScripts { get; set; }

		/// <summary>Runs an editor command on the dialog's copy of the book on a worker thread; true when it ended normally (<c>ExecuteScript</c>).</summary>
		public Func<ScriptCommandItem, ComicBook, Task<bool>> RunEditorScript { get; set; }
	}
}
