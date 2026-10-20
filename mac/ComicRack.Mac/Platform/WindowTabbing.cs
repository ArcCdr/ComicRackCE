using System;

namespace ComicRack.Mac.Platform
{
	/// <summary>
	/// C-WIN-3: automatic window tabbing off. macOS merges windows of an app into tabs ("Merge All Windows", the Window menu, the tab
	/// bar of a full-screen Space) unless <c>NSWindow.allowsAutomaticWindowTabbing</c> is NO; the app has its own tab bars (book tabs, browser
	/// tabs), and two tab systems would confuse both. The setting is a class property, so it is set once at start, before the first window.
	/// </summary>
	public static class WindowTabbing
	{
		/// <summary>Sets <c>NSWindow.allowsAutomaticWindowTabbing = NO</c>. Returns false (and does nothing) when the runtime or the class is missing.</summary>
		public static bool Disable()
		{
			if (!ObjC.IsAvailable)
			{
				return false;
			}
			try
			{
				IntPtr window = ObjC.Class("NSWindow");
				if (window == IntPtr.Zero || !ObjC.HasClassMethod("NSWindow", "setAllowsAutomaticWindowTabbing:"))
				{
					return false;
				}
				ObjC.CallBool(window, "setAllowsAutomaticWindowTabbing:", false);
				return true;
			}
			catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
			{
				return false;
			}
		}

		/// <summary>The current value of <c>NSWindow.allowsAutomaticWindowTabbing</c>, or null where it cannot be read.</summary>
		public static bool? IsAutomaticTabbingAllowed()
		{
			if (!ObjC.IsAvailable)
			{
				return null;
			}
			try
			{
				IntPtr window = ObjC.Class("NSWindow");
				if (window == IntPtr.Zero || !ObjC.HasClassMethod("NSWindow", "allowsAutomaticWindowTabbing"))
				{
					return null;
				}
				return ObjC.CallReturnsBool(window, "allowsAutomaticWindowTabbing");
			}
			catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
			{
				return null;
			}
		}
	}
}
