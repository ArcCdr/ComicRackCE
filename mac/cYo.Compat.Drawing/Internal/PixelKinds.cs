using System;
using System.Drawing.Imaging;
using SkiaSharp;

namespace cYo.Compat.Drawing.Internal
{
	/// <summary>The four pixel layouts the shim can store or hand out through LockBits.</summary>
	internal enum PixKind
	{
		/// <summary>BGRA, 4 bytes, colour channels not multiplied by alpha (Format32bppArgb).</summary>
		Argb,

		/// <summary>BGRA, 4 bytes, colour channels multiplied by alpha (Format32bppPArgb).</summary>
		PArgb,

		/// <summary>BGRA, 4 bytes, alpha byte ignored on read and written as 255 (Format32bppRgb; the storage of Format24bppRgb).</summary>
		Rgb32,

		/// <summary>BGR, 3 bytes, rows padded to 4 bytes (Format24bppRgb, only as a LockBits buffer).</summary>
		Rgb24,
	}

	internal static class PixelKinds
	{
		/// <summary>The fixed mapping of the supported pixel formats. Every other format is not supported.</summary>
		public static bool TryGetKind(PixelFormat format, out PixKind kind)
		{
			switch (format)
			{
				case PixelFormat.Format32bppArgb:
					kind = PixKind.Argb;
					return true;
				case PixelFormat.Format32bppPArgb:
					kind = PixKind.PArgb;
					return true;
				case PixelFormat.Format32bppRgb:
					kind = PixKind.Rgb32;
					return true;
				case PixelFormat.Format24bppRgb:
					kind = PixKind.Rgb24;
					return true;
				default:
					kind = PixKind.Argb;
					return false;
			}
		}

		/// <summary>The formats a bitmap can really have (the Format... members). Undefined, Canonical, Max and the bare flag values are not.</summary>
		public static bool IsConcrete(PixelFormat format)
		{
			switch (format)
			{
				case PixelFormat.Format1bppIndexed:
				case PixelFormat.Format4bppIndexed:
				case PixelFormat.Format8bppIndexed:
				case PixelFormat.Format16bppGrayScale:
				case PixelFormat.Format16bppRgb555:
				case PixelFormat.Format16bppRgb565:
				case PixelFormat.Format16bppArgb1555:
				case PixelFormat.Format24bppRgb:
				case PixelFormat.Format32bppRgb:
				case PixelFormat.Format32bppArgb:
				case PixelFormat.Format32bppPArgb:
				case PixelFormat.Format48bppRgb:
				case PixelFormat.Format64bppArgb:
				case PixelFormat.Format64bppPArgb:
					return true;
				default:
					return false;
			}
		}

		/// <summary>ArgumentException for a value that is no pixel format of a bitmap (as GDI+), NotSupportedException for a real format the shim does not store.</summary>
		public static PixKind GetKind(PixelFormat format, string what)
		{
			if (!IsConcrete(format))
			{
				throw new ArgumentException("Parameter is not valid.", nameof(format));
			}
			if (!TryGetKind(format, out PixKind kind))
			{
				throw new NotSupportedException($"{what}: pixel format {format} is not supported on macOS (supported: Format32bppArgb, Format32bppPArgb, Format32bppRgb, Format24bppRgb)");
			}
			return kind;
		}

		/// <summary>The kind of the 4-byte SKBitmap that backs a bitmap of this kind (24 bpp is stored as opaque BGRA).</summary>
		public static PixKind StorageKind(PixKind kind) => kind == PixKind.Rgb24 ? PixKind.Rgb32 : kind;

		public static SKAlphaType AlphaType(PixKind storageKind) => storageKind switch
		{
			PixKind.Argb => SKAlphaType.Unpremul,
			PixKind.PArgb => SKAlphaType.Premul,
			_ => SKAlphaType.Opaque,
		};

		public static int BytesPerPixel(PixKind kind) => kind == PixKind.Rgb24 ? 3 : 4;

		/// <summary>GDI+ stride: bytes per row rounded up to a multiple of 4.</summary>
		public static int Stride(PixKind kind, int width)
		{
			long bytes = (long)width * BytesPerPixel(kind);
			bytes = (bytes + 3) & ~3L;
			
			return checked((int)bytes);
		}

		public static bool HasAlpha(PixelFormat format) => format == PixelFormat.Format32bppArgb || format == PixelFormat.Format32bppPArgb;
	}
}
