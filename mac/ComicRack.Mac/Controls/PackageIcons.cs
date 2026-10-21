using System;
using System.Collections.Concurrent;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using cYo.Compat.Drawing;
using cYo.Projects.ComicRack.Engine;
using SkiaSharp;

namespace ComicRack.Mac.Controls
{
	/// <summary>The icon packages of the Core that a combo box can show next to its entries (<c>new ComboBoxSkinner(cbPublisher, ComicBook.PublisherIcons)</c>).</summary>
	public enum IconSet
	{
		None,
		Publishers,
		AgeRatings,
		Formats,
	}

	/// <summary>The pictures of the Core's icon packages as images a control can show, made once per picture.</summary>
	public static class PackageIcons
	{
		private static readonly ConcurrentDictionary<(IconSet, string), IImage> cache = new ConcurrentDictionary<(IconSet, string), IImage>();

		/// <summary>The picture for <paramref name="key"/> (a publisher, age rating or format), or null when the package has none.</summary>
		public static IImage Get(IconSet set, string key)
		{
			if (set == IconSet.None || string.IsNullOrEmpty(key))
			{
				return null;
			}
			if (cache.TryGetValue((set, key), out IImage cached))
			{
				return cached;
			}
			System.Drawing.Image source = Package(set)?.GetImage(key);
			IImage made = source is System.Drawing.Bitmap bitmap ? Convert(bitmap) : null;
			if (made != null)
			{
				cache[(set, key)] = made;
			}
			return made;
		}

		private static cYo.Common.Drawing.ImagePackage Package(IconSet set)
		{
			switch (set)
			{
				case IconSet.Publishers: return ComicBook.PublisherIcons;
				case IconSet.AgeRatings: return ComicBook.AgeRatingIcons;
				case IconSet.Formats: return ComicBook.FormatIcons;
				default: return null;
			}
		}

		private static IImage Convert(System.Drawing.Bitmap bitmap)
		{
			try
			{
				using (SKImage image = SKImage.FromBitmap(SkiaInterop.GetSKBitmap(bitmap)))
				using (SKData data = image.Encode(SKEncodedImageFormat.Png, 100))
				using (var stream = new MemoryStream(data.ToArray()))
				{
					return new Bitmap(stream);
				}
			}
			catch (Exception)
			{
				return null;
			}
		}
	}
}
