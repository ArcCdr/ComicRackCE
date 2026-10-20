using Avalonia;

namespace ComicRack.Mac.Reader
{
	/// <summary>
	/// What the visible-part overlay needs to know (<c>ComicDisplayControl.visiblePartOverlay_RenderSurface</c>): the size of the whole content (the page,
	/// or the spread) in image pixels, where each page is in it, and which part of it the window shows. All rectangles are in image pixels of the content
	/// (the unit of the pictures), not in window units.
	/// </summary>
	public sealed class ReaderPartInfo
	{
		public ReaderPartInfo(Size content, Rect[] pages, Rect visible, bool canPan)
		{
			Content = content;
			Pages = pages;
			Visible = visible;
			CanPan = canPan;
		}

		/// <summary>The size of the whole content.</summary>
		public Size Content { get; }

		/// <summary>The rectangle of each page of the scene in the content.</summary>
		public Rect[] Pages { get; }

		/// <summary>The part of the content that is inside the window.</summary>
		public Rect Visible { get; }

		/// <summary>True if the content is larger than the window in either direction (there is a part to show).</summary>
		public bool CanPan { get; }
	}
}
