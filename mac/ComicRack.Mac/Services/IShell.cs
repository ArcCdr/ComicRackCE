using System.Threading.Tasks;

namespace ComicRack.Mac.Services
{
	/// <summary>What the view models may ask of the windowing layer (so they need no reference to a window).</summary>
	public interface IShell
	{
		void ShowAbout();

		void Minimize();

		/// <summary>The green button of macOS: maximize or restore.</summary>
		void Zoom();

		void ToggleFullScreen();

		/// <summary>Shows the file in a Finder window (<c>Program.ShowExplorer</c>, the tab menu's Reveal in Finder). Does nothing by default.</summary>
		void RevealInFinder(string path)
		{
		}

		/// <summary>Puts text on the clipboard (Copy Full Path to Clipboard). Does nothing by default.</summary>
		Task CopyTextAsync(string text) => Task.CompletedTask;

		/// <summary>Quits the application (the Tools menu's Exit, <c>MenuClose</c>). Does nothing by default.</summary>
		void Quit()
		{
		}

		/// <summary>Brings the library window to the front (Show in Browser while the reader is in its own window). Does nothing by default.</summary>
		void ActivateLibrary()
		{
		}
	}
}
