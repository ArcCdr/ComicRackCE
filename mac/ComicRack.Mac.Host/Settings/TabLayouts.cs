using System;

namespace ComicRack.Mac.Host
{
	/// <summary>
	/// The dialogs whose groups of controls the user can turn into tabs (double click on the body of a group), a copy of <c>ComicRack/Config/TabLayouts.cs</c> with the same
	/// names and values. The setting <c>TabLayouts</c> keeps one flag for each dialog that is in tab mode.
	/// </summary>
	[Flags]
	public enum TabLayouts
	{
		None = 0x0,
		Paste = 0x1,
		Export = 0x2,
		Multiple = 0x4,
		ReaderSettings = 0x8,
		BehaviorSettings = 0x10,
		LibrarySettings = 0x20,
		ScriptSettings = 0x40,
		AdvancedSettings = 0x80,
	}
}
