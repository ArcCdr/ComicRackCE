using Avalonia;

namespace ComicRack.Mac.Reader
{
	/// <summary>
	/// What the reading surface (<c>CrReaderPage</c>, chrome layer) draws: the pages of the scene, where they are placed in an area, and the two switches of
	/// the picture around them. The reader's view model implements it; the surface asks at every frame, so it needs no change notification of its own.
	/// </summary>
	public interface IReaderPageSource
	{
		/// <summary>The decoded pages of the current position, or null.</summary>
		ReaderScene Scene { get; }

		/// <summary>The meter that counts the page turns drawn.</summary>
		PageTurnMeter Meter { get; }

		/// <summary>The outline and the shadow of the original's "realistic pages" are drawn around the pages (<c>RealisticPages</c>, on by default).</summary>
		bool RealisticPages { get; }

		/// <summary>Where the pages of the scene are placed in a reading area of the given size.</summary>
		PageArrangement ArrangeFor(Size area);
	}
}
