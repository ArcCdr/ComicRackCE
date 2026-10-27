// M1.2b: real behaviour. Only the JPEG and PNG encoders exist; the codec properties follow System.Drawing.Common
// (Flags, Version and the signature patterns are not declared).
using System;

namespace System.Drawing.Imaging
{
	public sealed partial class ImageCodecInfo
	{
		internal ImageCodecInfo()
		{
		}

		public Guid Clsid { get; set; }

		public Guid FormatID { get; set; }

		public string CodecName { get; set; }

		public string DllName { get; set; }

		public string FormatDescription { get; set; }

		public string FilenameExtension { get; set; }

		public string MimeType { get; set; }

		/// <summary>The encoders Save can use: JPEG and PNG. A new array of new objects on every call, as in GDI+.</summary>
		public static ImageCodecInfo[] GetImageEncoders() => new[]
		{
			new ImageCodecInfo
			{
				Clsid = new Guid("557cf401-1a04-11d3-9a73-0000f81ef32e"),
				FormatID = ImageFormat.Jpeg.Guid,
				CodecName = "Built-in JPEG Codec",
				FormatDescription = "JPEG",
				FilenameExtension = "*.JPG;*.JPEG;*.JPE;*.JFIF",
				MimeType = "image/jpeg",
			},
			new ImageCodecInfo
			{
				Clsid = new Guid("557cf406-1a04-11d3-9a73-0000f81ef32e"),
				FormatID = ImageFormat.Png.Guid,
				CodecName = "Built-in PNG Codec",
				FormatDescription = "PNG",
				FilenameExtension = "*.PNG",
				MimeType = "image/png",
			},
		};
	}
}
