using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ComicRack.Mac.Services;
using ComicRack.Mac.Themes;
using ComicRack.Mac.ViewModels;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The browser grid alone in a window (<c>--show BookGrid</c>): the contact sheet of the thumbnails (<c>chrome-sheet.sh R2 BookGrid</c>) shows it
	/// over the library window's own browser, so it has the books, the covers and the options of the running app. Once the first cover is there
	/// the window logs two probe lines (<c>Book grid probe</c>) with the colour that a pixel of a screenshot must have, so that the e2e case
	/// <c>grid-look</c> can read them: the paper of the grid (<c>Cr.ItemViewMainBack</c>) and the black frame of the first cover.
	/// </summary>
	public partial class BookGridWindow : Window
	{
		private int polls;

		public BookGridWindow()
		{
			InitializeComponent();
			Grid.DataContext = ViewRegistry.Library?.Browser;
			Opened += (sender, e) =>
			{
				Grid.Focus(); // the keyboard goes to the grid at once, as in the library window
				StartProbing();
			};
		}

		private void StartProbing()
		{
			var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
			timer.Tick += (sender, e) =>
			{
				polls++;
				BookCell first = Grid.CellAt(0);
				bool ready = first != null && first.Cover.HasImage || polls > 60;
				if (!ready)
				{
					return;
				}
				timer.Stop();
				LogProbes(first);
			};
			timer.Start();
		}

		private void LogProbes(BookCell first)
		{
			var paper = ThemeTokens.Get(ThemeTokens.Names.ItemViewMainBack, ActualThemeVariant);
			AppLog.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture, "Book grid probe: ItemViewMainBack #{0:X2}{1:X2}{2:X2} at 2,2 in a client area of {3:0}x{4:0}", paper.R, paper.G, paper.B, Bounds.Width, Bounds.Height));
			if (first == null)
			{
				AppLog.Info("Book grid probe: no cell");
				return;
			}
			// the frame of the cover: its left column, in the middle of the cover's height
			Point origin = first.TranslatePoint(new Point(0, 0), this) ?? default;
			Rect picture = first.Geometry.Box;
			double x = origin.X + picture.X + first.Cover.PictureRect.X, y = origin.Y + picture.Y + first.Cover.PictureRect.Y + first.Cover.PictureRect.Height / 2;
			AppLog.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture, "Book grid probe: ThumbBorder #000000 at {0:0},{1:0} in a client area of {2:0}x{3:0}", x, y, Bounds.Width, Bounds.Height));
		}
	}
}
