using System.Drawing;
using cYo.Projects.ComicRack.Engine.Properties;

namespace cYo.Projects.ComicRack.Engine.IO.Cache
{
	// ImagePool for the macOS build, created by the pass B CacheManager replacement. The base class draws a message onto the
	// error page with Graphics.DrawString and SystemFonts.MenuFont, which the Drawing shim does not support (text is outside the
	// MVP), so the page would throw instead of showing the "page failed to load" picture. This returns the picture without text.
	// Resources.ErrorPage is a new bitmap on every access, so the caller owns the result, as with the original.
	public class MacImagePool : ImagePool
	{
		public MacImagePool()
		{
		}

		public MacImagePool(int thumbCount, long thumbSize, int pageCount)
			: base(thumbCount, thumbSize, pageCount)
		{
		}

		public override Bitmap CreateErrorPage()
		{
			return Resources.ErrorPage;
		}
	}
}
