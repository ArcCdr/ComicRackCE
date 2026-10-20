using System;

namespace ComicRack.Mac.Host
{
	/// <summary>
	/// The counters the library tree shows behind a list name, a copy of <c>ComicRack/Config/LibraryGauges.cs</c> with the same names and values (the saved
	/// setting <c>LibraryGaugesFormat</c> is a combination of them). <see cref="Numeric"/> draws the numbers; without it the counters are narrow bars.
	/// </summary>
	[Flags]
	public enum LibraryGauges
	{
		None = 0x0,
		New = 0x1,
		Unread = 0x2,
		Total = 0x4,
		Numeric = 0x1000,
		Default = 0x1007,
	}
}
