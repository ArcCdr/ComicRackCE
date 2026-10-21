using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// The colours of the tile that stands for a thumbnail that cannot be made (a missing or damaged file): the tile, its frame and its cross, as
	/// opaque ARGB values. They are the <c>Cr.Thumb.Placeholder*Brush</c> entries of <c>Themes/Controls/CrThumbnailLook.axaml</c> for the light and the
	/// dark appearance, read once per appearance on the UI thread (<see cref="Follow"/>), so that the thumbnail decoder, which draws the tile on a worker
	/// thread, never touches a resource or hard-codes a colour. <see cref="Current"/> is an immutable snapshot that any thread may read.
	/// </summary>
	public sealed class ThumbnailPlaceholderLook
	{
		/// <summary>The light appearance as the theme file has it (the values here are only the fallback for a process without the theme).</summary>
		public static readonly ThumbnailPlaceholderLook Light = new ThumbnailPlaceholderLook(0xFFDCDCDC, 0xFFA8A8A8, 0xFF9C9C9C);

		/// <summary>The dark appearance as the theme file has it.</summary>
		public static readonly ThumbnailPlaceholderLook Dark = new ThumbnailPlaceholderLook(0xFF3A3A3A, 0xFF5A5A5A, 0xFF6A6A6A);

		private static volatile ThumbnailPlaceholderLook current = Light;

		public ThumbnailPlaceholderLook(uint tile, uint frame, uint cross)
		{
			Tile = tile;
			Frame = frame;
			Cross = cross;
		}

		/// <summary>The fill of the tile.</summary>
		public uint Tile { get; }

		/// <summary>The one unit frame inside the tile.</summary>
		public uint Frame { get; }

		/// <summary>The two strokes of the cross.</summary>
		public uint Cross { get; }

		/// <summary>The look for the appearance that is shown now (light until <see cref="Follow"/> says otherwise).</summary>
		public static ThumbnailPlaceholderLook Current => current;

		/// <summary>The look of an appearance, from the theme resources (the fallback values where the theme is not loaded).</summary>
		public static ThumbnailPlaceholderLook For(ThemeVariant variant)
		{
			ThumbnailPlaceholderLook fallback = variant == ThemeVariant.Dark ? Dark : Light;
			uint tile = Read("Cr.Thumb.PlaceholderTileBrush", variant, fallback.Tile);
			uint frame = Read("Cr.Thumb.PlaceholderFrameBrush", variant, fallback.Frame);
			uint cross = Read("Cr.Thumb.PlaceholderCrossBrush", variant, fallback.Cross);
			return new ThumbnailPlaceholderLook(tile, frame, cross);
		}

		private static uint Read(string key, ThemeVariant variant, uint fallback)
		{
			if (Application.Current != null && Application.Current.TryGetResource(key, variant, out object found) && found is ISolidColorBrush brush)
			{
				Color c = brush.Color;
				return ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
			}
			return fallback;
		}

		/// <summary>Makes <see cref="Current"/> follow the appearance of the application (call on the UI thread, once per application).</summary>
		public static void Follow(Application app)
		{
			if (app == null)
			{
				return;
			}
			void Update() => current = For(app.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light);
			app.ActualThemeVariantChanged += (sender, e) => Update();
			Update();
		}
	}
}
