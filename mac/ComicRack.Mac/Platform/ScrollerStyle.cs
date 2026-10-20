using System;

namespace ComicRack.Mac.Platform
{
	/// <summary>
	/// C-SCROLL-1: scroll bars follow System Settings, "Show scroll bars" (Automatically based on mouse or trackpad, When scrolling, Always).
	/// <c>NSScroller.preferredScrollerStyle</c> says whether the system wants overlay scrollers (1) or legacy, always visible ones (0); the app turns
	/// <c>ScrollViewer.AllowAutoHide</c> on for overlay and off for legacy. Read once at start (a change of the setting while the app runs is picked up at the next start).
	/// </summary>
	public static class ScrollerStyle
	{
		/// <summary>True when the system uses overlay scrollers; true as well where it cannot be asked (the thin auto-hiding bars are the default look).</summary>
		public static bool IsOverlay
		{
			get
			{
				if (!ObjC.IsAvailable)
				{
					return true;
				}
				try
				{
					IntPtr scroller = ObjC.Class("NSScroller");
					if (scroller == IntPtr.Zero || !ObjC.HasClassMethod("NSScroller", "preferredScrollerStyle"))
					{
						return true;
					}
					return ObjC.CallReturnsLong(scroller, "preferredScrollerStyle") != 0;
				}
				catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
				{
					return true;
				}
			}
		}
	}
}
