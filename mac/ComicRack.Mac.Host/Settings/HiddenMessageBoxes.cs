using System;

namespace ComicRack.Mac.Host
{
	/// <summary>
	/// The questions that the user turned off with "Do not ask me again", a copy of <c>ComicRack/Config/HiddenMessageBoxes.cs</c> with the same names and
	/// values (the saved setting <c>HiddenMessageBoxes</c> is a combination of them). A question whose flag is set is not asked and counts as answered
	/// with the default button (<c>Program.AskQuestion</c>); Preferences > Advanced can clear the setting (PR.27).
	/// </summary>
	[Flags]
	public enum HiddenMessageBoxes
	{
		None = 0x0,
		RemoveFromList = 0x1,
		RemoveList = 0x2,
		RemoveFavorite = 0x4,
		ConvertComics = 0x8,
		SetAllListLayouts = 0x10,
		CloseExternalReader = 0x20,
		ComicRackMinimized = 0x40,
		AskDirtyItems = 0x80,
		AskClearData = 0x100,
		DoNotCheckForUpdate = 0x200,
		NeverAskDirtyItems = 0x400,
	}
}
