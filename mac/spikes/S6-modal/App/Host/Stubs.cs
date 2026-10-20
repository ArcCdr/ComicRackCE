using System;
using System.Drawing;

// Stand-ins for what the scripts reach through the ComicRack object model. The spike does not link the Core (the production
// run through PythonCommand is checked by the identity runs on the S4 console); these classes have the members the two bundled
// UI scripts use, with the same names.

namespace cYo.Projects.ComicRack.Engine.Display
{
	/// <summary>Sample.py does <c>from cYo.Projects.ComicRack.Engine.Display import *</c> and uses PageTransitionEffect.</summary>
	public enum PageTransitionEffect
	{
		None,
		Paging,
		Fade,
		Slide
	}
}

namespace S6.Host
{
	/// <summary>The members of ComicBook that Autonumber.py and Sample.py touch.</summary>
	public class Book
	{
		public string ShadowSeries { get; set; } = "Series";

		public string ShadowNumber { get; set; } = "1";

		public string Number { get; set; } = "1";

		public int Count { get; set; }

		public string AlternateNumber { get; set; } = string.Empty;

		public int AlternateCount { get; set; }

		public string Web { get; set; } = string.Empty;

		public int ReadPercentage { get; set; }

		public Guid Id { get; } = Guid.NewGuid();

		public string Caption => ShadowSeries + " #" + Number;
	}

	public sealed class ThemeStub
	{
		public int Applied;

		public void ApplyTheme(System.Windows.Forms.Control control)
		{
			Applied++;
		}
	}

	public sealed class AppStub
	{
		public string ProductVersion => "0.9.184";
	}

	/// <summary>The <c>ComicRack</c> variable of a script: Localize, MainWindow, App, Theme.</summary>
	public sealed class ComicRackStub
	{
		public System.Windows.Forms.IWin32Window MainWindow { get; set; }

		public AppStub App { get; } = new AppStub();

		public ThemeStub Theme { get; } = new ThemeStub();

		public string Localize(string resourceKey, string nameKey, string text)
		{
			return text;
		}
	}
}
