// M1.2b: real behaviour. Signatures and GUIDs follow the System.Drawing.Common reference assembly.
using System;

namespace System.Drawing.Imaging
{
	public sealed class ImageFormat
	{
		private static readonly ImageFormat memoryBmp = new ImageFormat(new Guid("b96b3caa-0728-11d3-9d7b-0000f81ef32e"));
		private static readonly ImageFormat bmp = new ImageFormat(new Guid("b96b3cab-0728-11d3-9d7b-0000f81ef32e"));
		private static readonly ImageFormat emf = new ImageFormat(new Guid("b96b3cac-0728-11d3-9d7b-0000f81ef32e"));
		private static readonly ImageFormat wmf = new ImageFormat(new Guid("b96b3cad-0728-11d3-9d7b-0000f81ef32e"));
		private static readonly ImageFormat jpeg = new ImageFormat(new Guid("b96b3cae-0728-11d3-9d7b-0000f81ef32e"));
		private static readonly ImageFormat png = new ImageFormat(new Guid("b96b3caf-0728-11d3-9d7b-0000f81ef32e"));
		private static readonly ImageFormat gif = new ImageFormat(new Guid("b96b3cb0-0728-11d3-9d7b-0000f81ef32e"));
		private static readonly ImageFormat tiff = new ImageFormat(new Guid("b96b3cb1-0728-11d3-9d7b-0000f81ef32e"));
		private static readonly ImageFormat exif = new ImageFormat(new Guid("b96b3cb2-0728-11d3-9d7b-0000f81ef32e"));
		private static readonly ImageFormat icon = new ImageFormat(new Guid("b96b3cb5-0728-11d3-9d7b-0000f81ef32e"));
		private static readonly ImageFormat heif = new ImageFormat(new Guid("b96b3cb6-0728-11d3-9d7b-0000f81ef32e"));
		private static readonly ImageFormat webp = new ImageFormat(new Guid("b96b3cb7-0728-11d3-9d7b-0000f81ef32e"));

		private readonly Guid guid;

		public ImageFormat(Guid guid)
		{
			this.guid = guid;
		}

		public Guid Guid => guid;

		public static ImageFormat MemoryBmp => memoryBmp;

		public static ImageFormat Bmp => bmp;

		public static ImageFormat Emf => emf;

		public static ImageFormat Wmf => wmf;

		public static ImageFormat Gif => gif;

		public static ImageFormat Jpeg => jpeg;

		public static ImageFormat Png => png;

		public static ImageFormat Tiff => tiff;

		public static ImageFormat Exif => exif;

		public static ImageFormat Icon => icon;

		public static ImageFormat Heif => heif;

		public static ImageFormat Webp => webp;

		public override bool Equals(object o) => o is ImageFormat other && guid == other.guid;

		public override int GetHashCode() => guid.GetHashCode();

		public override string ToString()
		{
			if (this == memoryBmp) return "MemoryBMP";
			if (this == bmp) return "Bmp";
			if (this == emf) return "Emf";
			if (this == wmf) return "Wmf";
			if (this == gif) return "Gif";
			if (this == jpeg) return "Jpeg";
			if (this == png) return "Png";
			if (this == tiff) return "Tiff";
			if (this == exif) return "Exif";
			if (this == icon) return "Icon";
			if (this == heif) return "Heif";
			if (this == webp) return "Webp";
			return "[ImageFormat: " + guid + "]";
		}
	}
}
