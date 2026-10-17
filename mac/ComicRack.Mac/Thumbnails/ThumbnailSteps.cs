namespace ComicRack.Mac.Thumbnails
{
	/// <summary>
	/// The sizes a thumbnail is cached at. The Core keeps one thumbnail per book, 512 px high, and
	/// <c>ThumbnailImage.GetThumbnail(height)</c> picks from the chain 512, 384, 288, ... (each step 0.75 of the one before) the
	/// smallest step that is not below the request. The service uses the same chain, so a slider that moves in small increments
	/// asks for a few distinct sizes instead of one per pixel, and the cache holds one image per book and step.
	/// </summary>
	public static class ThumbnailSteps
	{
		/// <summary>The height of the thumbnail the Core stores (<c>ThumbnailImage.MaxHeight</c>); a larger request is served at this height.</summary>
		public const int MaxHeight = 512;

		/// <summary>The smallest height served; smaller requests are raised to it.</summary>
		public const int MinHeight = 32;

		/// <summary>The cached height for a requested height in pixels: 512 for 385 to 512 (and above), 384 for 289 to 384, 288 for 217 to 288, and so on.</summary>
		public static int Snap(int heightPx)
		{
			int height = heightPx < MinHeight ? MinHeight : heightPx > MaxHeight ? MaxHeight : heightPx;
			float step = MaxHeight;
			while (step * 0.75f >= height)
			{
				step *= 0.75f;
			}
			return (int)step;
		}
	}
}
