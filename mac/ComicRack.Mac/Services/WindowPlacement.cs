using System;
using System.Collections.Generic;
using ComicRack.Mac.Host;

namespace ComicRack.Mac.Services
{
	/// <summary>The usable area of one screen (the part the menu bar and the Dock leave), in screen pixels, and its scale (pixels per device independent pixel).</summary>
	public readonly struct ScreenArea
	{
		public ScreenArea(int x, int y, int width, int height, double scale)
		{
			X = x;
			Y = y;
			Width = width;
			Height = height;
			Scale = scale > 0 ? scale : 1;
		}

		public int X { get; }

		public int Y { get; }

		public int Width { get; }

		public int Height { get; }

		public double Scale { get; }

		public int Right => X + Width;

		public int Bottom => Y + Height;
	}

	/// <summary>
	/// Puts a saved window frame back on the screens that exist now (P5.1): a window that was on a monitor that is gone, or whose size no longer fits, comes
	/// back inside the screen it would have been closest to. Pure arithmetic (no Avalonia types), so it is tested with made-up screens.
	/// </summary>
	public static class WindowPlacement
	{
		/// <summary>The height of the title bar, in device independent pixels: the frame is the client area plus this.</summary>
		public const double TitleBar = 28;

		/// <summary>Of the title bar this much must be on a screen for the window to be kept where it was (device independent pixels).</summary>
		public const double MinimumVisibleWidth = 100;

		/// <summary>
		/// Returns the frame to open a window with: <paramref name="saved"/> when it can be used as it is, a moved and shrunk copy when it cannot, null if there is
		/// nothing usable saved. Without screens (should not happen) the saved frame is returned as it is.
		/// </summary>
		/// <param name="saved">The frame that was saved, or null.</param>
		/// <param name="screens">The screens as they are now.</param>
		/// <param name="minWidth">The smallest width of the window's client area.</param>
		/// <param name="minHeight">The smallest height of the window's client area.</param>
		public static WindowFrame Correct(WindowFrame saved, IReadOnlyList<ScreenArea> screens, double minWidth = 0, double minHeight = 0)
		{
			if (saved == null || !saved.IsUsable)
			{
				return null;
			}
			if (screens == null || screens.Count == 0)
			{
				return saved;
			}
			ScreenArea screen = Closest(saved, screens);
			double scale = screen.Scale;
			double maxWidth = Math.Max(1, screen.Width / scale);
			double maxHeight = Math.Max(1, screen.Height / scale - TitleBar);
			double width = Math.Min(Math.Max(saved.Width, Math.Min(minWidth, maxWidth)), maxWidth);
			double height = Math.Min(Math.Max(saved.Height, Math.Min(minHeight, maxHeight)), maxHeight);

			int x = saved.X;
			int y = saved.Y;
			if (!TitleBarShows(x, y, width, scale, screens))
			{
				// Out of reach: centre the window on the screen it is closest to.
				double frameWidth = width * scale;
				double frameHeight = (height + TitleBar) * scale;
				x = screen.X + (int)Math.Round((screen.Width - frameWidth) / 2);
				y = screen.Y + (int)Math.Round((screen.Height - frameHeight) / 2);
			}
			else
			{
				// Reachable, but a window that grew or sits low must still show its whole title bar on the screen: pull it back when the top is above the screen.
				ScreenArea holder = Closest(new WindowFrame(x, y, width, height), screens);
				if (y < holder.Y)
				{
					y = holder.Y;
				}
				if (width != saved.Width || height != saved.Height)
				{
					// The size was changed to fit the screen, so the position it was saved with (made for the old size) no longer says where the window ends:
					// move the whole frame onto the screen it overlaps most.
					x = ClampStart(x, width * holder.Scale, holder.X, holder.Right);
					y = ClampStart(y, (height + TitleBar) * holder.Scale, holder.Y, holder.Bottom);
				}
			}
			return new WindowFrame(x, y, width, height, saved.Maximized);
		}

		// The start of a span of the given length, moved so that the span lies inside [low, high] (at the low end when it is longer than that).
		private static int ClampStart(int start, double length, int low, int high)
		{
			int latest = (int)Math.Floor(high - length);
			return Math.Max(low, Math.Min(start, latest));
		}

		private static bool TitleBarShows(int x, int y, double width, double scale, IReadOnlyList<ScreenArea> screens)
		{
			double barWidth = width * scale;
			double barHeight = TitleBar * scale;
			double need = Math.Min(MinimumVisibleWidth * scale, barWidth);
			foreach (ScreenArea s in screens)
			{
				double w = Math.Min(x + barWidth, s.Right) - Math.Max(x, s.X);
				double h = Math.Min(y + barHeight, s.Bottom) - Math.Max(y, s.Y);
				if (w >= need && h >= barHeight / 2)
				{
					return true;
				}
			}
			return false;
		}

		// The screen the frame overlaps most; when it overlaps none, the one whose centre is nearest to the frame's centre.
		private static ScreenArea Closest(WindowFrame frame, IReadOnlyList<ScreenArea> screens)
		{
			ScreenArea best = screens[0];
			double bestOverlap = -1;
			foreach (ScreenArea s in screens)
			{
				double w = frame.Width * s.Scale;
				double h = (frame.Height + TitleBar) * s.Scale;
				double ox = Math.Min(frame.X + w, s.Right) - Math.Max(frame.X, s.X);
				double oy = Math.Min(frame.Y + h, s.Bottom) - Math.Max(frame.Y, s.Y);
				double overlap = ox > 0 && oy > 0 ? ox * oy : 0;
				if (overlap > bestOverlap)
				{
					bestOverlap = overlap;
					best = s;
				}
			}
			if (bestOverlap > 0)
			{
				return best;
			}
			double cx = frame.X + frame.Width * best.Scale / 2;
			double cy = frame.Y + (frame.Height + TitleBar) * best.Scale / 2;
			double bestDistance = double.MaxValue;
			foreach (ScreenArea s in screens)
			{
				double dx = s.X + s.Width / 2.0 - cx;
				double dy = s.Y + s.Height / 2.0 - cy;
				double d = dx * dx + dy * dy;
				if (d < bestDistance)
				{
					bestDistance = d;
					best = s;
				}
			}
			return best;
		}

		/// <summary>The frame of a second window: <paramref name="frame"/> moved right and down by <paramref name="step"/> pixels per window already open (windows opened later do not cover the first).</summary>
		public static WindowFrame Offset(WindowFrame frame, int count, double step = 26)
		{
			if (frame == null || count <= 0)
			{
				return frame;
			}
			return new WindowFrame(frame.X + (int)Math.Round(step * count), frame.Y + (int)Math.Round(step * count), frame.Width, frame.Height, frame.Maximized);
		}
	}
}
