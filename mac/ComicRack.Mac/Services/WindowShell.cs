using Avalonia.Controls;
using ComicRack.Mac.Views;

namespace ComicRack.Mac.Services
{
	public sealed class WindowShell : IShell
	{
		private readonly Window window;

		public WindowShell(Window window)
		{
			this.window = window;
		}

		public void ShowAbout()
		{
			new AboutWindow().ShowDialog(window);
		}

		public void Minimize()
		{
			window.WindowState = WindowState.Minimized;
		}

		public void Zoom()
		{
			window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
		}

		public void ToggleFullScreen()
		{
			window.WindowState = window.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
		}
	}
}
