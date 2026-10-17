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
	}
}
