using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using ComicRack.Mac.Host.Platform;
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

		public void RevealInFinder(string path)
		{
			try
			{
				MacShell.Reveal(path);
			}
			catch (Exception ex)
			{
				AppLog.Error("Reveal in Finder failed.", ex);
			}
		}

		public async Task CopyTextAsync(string text)
		{
			IClipboard clipboard = TopLevel.GetTopLevel(window)?.Clipboard;
			if (clipboard != null)
			{
				await clipboard.SetTextAsync(text);
			}
		}

		public void Quit()
		{
			if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IControlledApplicationLifetime lifetime)
			{
				lifetime.Shutdown();
			}
		}

		public void ActivateLibrary()
		{
			if (window.WindowState == WindowState.Minimized)
			{
				window.WindowState = WindowState.Normal;
			}
			window.Activate();
		}
	}
}
