using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ComicRack.Mac.Host;

namespace ComicRack.Mac.Services
{
	/// <summary>
	/// Puts a window where it was and remembers where it is (P5.1). <see cref="Restore"/> runs before the window is shown: it places the saved frame on the
	/// screens that exist now (<see cref="WindowPlacement"/>). <see cref="Attach"/> then reports every move and resize of a normal window, and a zoomed
	/// window as "zoomed" with the frame it returns to, through <paramref name="write"/> (which stores it in the preferences; the store saves after a quiet time).
	/// Full screen and a minimized window change nothing: the next start opens in the normal frame, as macOS apps do.
	/// </summary>
	public sealed class WindowFrameTracker : IDisposable
	{
		private readonly Window window;
		private readonly Action<WindowFrame> write;
		private WindowFrame normal;
		private WindowFrame reported;
		private bool running;
		private bool scheduled;

		private WindowFrameTracker(Window window, Action<WindowFrame> write, WindowFrame start)
		{
			this.window = window;
			this.write = write;
			normal = start;
			reported = start;
		}

		/// <summary>The screens of <paramref name="window"/> as <see cref="ScreenArea"/>s (empty when the platform reports none, as a test window may).</summary>
		public static IReadOnlyList<ScreenArea> ScreensOf(Window window)
		{
			var list = new List<ScreenArea>();
			try
			{
				IReadOnlyList<Avalonia.Platform.Screen> all = window.Screens?.All;
				if (all != null)
				{
					foreach (Avalonia.Platform.Screen s in all)
					{
						PixelRect area = s.WorkingArea;
						list.Add(new ScreenArea(area.X, area.Y, area.Width, area.Height, s.Scaling));
					}
				}
			}
			catch (Exception ex)
			{
				AppLog.Error("The screens could not be listed.", ex);
			}
			return list;
		}

		/// <summary>
		/// Gives <paramref name="window"/> the saved frame (moved and shrunk if it no longer fits on a screen). Does nothing when no usable frame is saved: the window
		/// keeps the position and size its XAML asks for. Call before <c>Show</c>.
		/// </summary>
		/// <returns>The frame that was applied, or null.</returns>
		public static WindowFrame Restore(Window window, WindowFrame saved, int offsetCount = 0)
		{
			WindowFrame frame = WindowPlacement.Correct(WindowPlacement.Offset(saved, offsetCount), ScreensOf(window), window.MinWidth, window.MinHeight);
			if (frame == null)
			{
				return null;
			}
			window.WindowStartupLocation = WindowStartupLocation.Manual;
			window.Width = frame.Width;
			window.Height = frame.Height;
			window.Position = new PixelPoint(frame.X, frame.Y);
			if (frame.Maximized)
			{
				window.WindowState = WindowState.Maximized;
			}
			return frame;
		}

		/// <summary>
		/// Starts reporting the frame of <paramref name="window"/> once it is open. <paramref name="start"/> is the frame that was restored, if any; it is what a zoomed
		/// window returns to until the user has moved it. The frame the window opens with is not reported: only what the user does to it afterwards is (so a window
		/// that was moved aside from the saved frame does not become the saved frame, and a window nobody touched leaves the settings alone).
		/// </summary>
		public static WindowFrameTracker Attach(Window window, Action<WindowFrame> write, WindowFrame start = null)
		{
			var tracker = new WindowFrameTracker(window, write, start);
			window.Opened += tracker.OnOpened;
			window.Closing += tracker.OnClosing;
			window.PositionChanged += tracker.OnMoved;
			window.Resized += tracker.OnResized;
			window.PropertyChanged += tracker.OnPropertyChanged;
			return tracker;
		}

		private void OnOpened(object sender, EventArgs e)
		{
			running = true;
			// what the window is now is the known frame; it is reported only when it changes
			if (window.WindowState == WindowState.Normal && window.ClientSize.Width > 0 && window.ClientSize.Height > 0)
			{
				var frame = new WindowFrame(window.Position.X, window.Position.Y, window.ClientSize.Width, window.ClientSize.Height);
				normal = normal != null && normal.Maximized ? normal : frame;
				reported = reported ?? frame;
			}
		}

		// The window is about to go: its last frame is reported once more (the quit that follows saves it).
		private void OnClosing(object sender, WindowClosingEventArgs e) => Record();

		private void OnMoved(object sender, PixelPointEventArgs e) => RecordSoon();

		private void OnResized(object sender, WindowResizedEventArgs e) => RecordSoon();

		// A move or resize that belongs to a zoom or a full screen transition arrives before or after the state changes; looking at the window a moment
		// later (one look for a burst of events) sees the state it settled in, so the frame it had before the transition is not overwritten.
		private void RecordSoon()
		{
			if (scheduled || !running)
			{
				return;
			}
			scheduled = true;
			Dispatcher.UIThread.Post(() =>
			{
				scheduled = false;
				Record();
			}, DispatcherPriority.Background);
		}

		private void OnPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
		{
			if (e.Property == Window.WindowStateProperty)
			{
				RecordSoon();
			}
		}

		/// <summary>Reports the frame of the window as it is now, unless it is the frame that is known already.</summary>
		public void Record()
		{
			if (!running)
			{
				return;
			}
			switch (window.WindowState)
			{
				case WindowState.Normal:
				{
					Size size = window.ClientSize;
					if (size.Width <= 0 || size.Height <= 0)
					{
						return;
					}
					var frame = new WindowFrame(window.Position.X, window.Position.Y, size.Width, size.Height);
					normal = frame;
					// the frame that is known (restored or reported) is not reported again, but a window that comes back from zoomed must say so
					if (reported == null || reported.Maximized || !frame.Equals(reported))
					{
						reported = frame;
						write(frame);
					}
					break;
				}
				case WindowState.Maximized:
					if (normal != null)
					{
						reported = new WindowFrame(normal.X, normal.Y, normal.Width, normal.Height, maximized: true);
						write(reported);
					}
					break;
			}
		}

		public void Dispose()
		{
			window.Opened -= OnOpened;
			window.Closing -= OnClosing;
			window.PositionChanged -= OnMoved;
			window.Resized -= OnResized;
			window.PropertyChanged -= OnPropertyChanged;
			running = false;
		}
	}
}
