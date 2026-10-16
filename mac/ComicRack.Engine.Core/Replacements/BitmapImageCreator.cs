namespace CSJ2K.Util
{
	// Replacement: CSJ2K 3.0.0 ships BitmapImageCreator (System.Drawing) only in its .NET Framework build, so it is
	// missing on net10.0. Registration is a no-op: JPEG 2000 pages do not decode as System.Drawing bitmaps yet
	// (see discovered work in mac/plans/mvp-track.md).
	internal static class BitmapImageCreator
	{
		public static void Register()
		{
		}
	}
}
