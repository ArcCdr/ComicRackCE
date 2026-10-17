using System;
using System.Threading.Tasks;

namespace cYo.Compat.Drawing.Internal
{
	/// <summary>
	/// Converts rows of pixels between the four <see cref="PixKind"/> layouts. Every conversion goes through the
	/// un-premultiplied ARGB value (0xAARRGGBB): premultiplied sources are un-premultiplied first, targets without alpha
	/// drop the alpha channel, sources without alpha read as 255. Same-kind copies of Argb, PArgb and Rgb24 are plain memory copies.
	/// </summary>
	internal static unsafe class PixelConvert
	{
		/// <summary>Images with at least this many pixels are converted on several threads.</summary>
		private const long ParallelThreshold = 1 << 18;

		public static void Convert(IntPtr src, int srcStride, PixKind srcKind, IntPtr dst, int dstStride, PixKind dstKind, int width, int height)
		{
			if (width <= 0 || height <= 0)
			{
				return;
			}
			if (srcKind == dstKind && srcKind != PixKind.Rgb32)
			{
				int bytes = width * PixelKinds.BytesPerPixel(srcKind);
				Run(height, width, y => Buffer.MemoryCopy((byte*)src + (long)y * srcStride, (byte*)dst + (long)y * dstStride, bytes, bytes));
				return;
			}
			switch (srcKind)
			{
				case PixKind.Argb:
					Dispatch<ArgbPx>(src, srcStride, dst, dstStride, dstKind, width, height);
					break;
				case PixKind.PArgb:
					Dispatch<PArgbPx>(src, srcStride, dst, dstStride, dstKind, width, height);
					break;
				case PixKind.Rgb32:
					Dispatch<Rgb32Px>(src, srcStride, dst, dstStride, dstKind, width, height);
					break;
				default:
					Dispatch<Rgb24Px>(src, srcStride, dst, dstStride, dstKind, width, height);
					break;
			}
		}

		private static void Dispatch<S>(IntPtr src, int srcStride, IntPtr dst, int dstStride, PixKind dstKind, int width, int height)
			where S : struct, IPx
		{
			switch (dstKind)
			{
				case PixKind.Argb:
					Rows<S, ArgbPx>(src, srcStride, dst, dstStride, width, height);
					break;
				case PixKind.PArgb:
					Rows<S, PArgbPx>(src, srcStride, dst, dstStride, width, height);
					break;
				case PixKind.Rgb32:
					Rows<S, Rgb32Px>(src, srcStride, dst, dstStride, width, height);
					break;
				default:
					Rows<S, Rgb24Px>(src, srcStride, dst, dstStride, width, height);
					break;
			}
		}

		private static void Rows<S, D>(IntPtr src, int srcStride, IntPtr dst, int dstStride, int width, int height)
			where S : struct, IPx
			where D : struct, IPx
		{
			Run(height, width, y =>
			{
				byte* s = (byte*)src + (long)y * srcStride;
				byte* d = (byte*)dst + (long)y * dstStride;
				for (int x = 0; x < width; x++)
				{
					D.Store(d, S.Load(s));
					s += S.Bpp;
					d += D.Bpp;
				}
			});
		}

		private static void Run(int height, int width, Action<int> row)
		{
			if ((long)height * width >= ParallelThreshold && height > 1)
			{
				Parallel.For(0, height, row);
			}
			else
			{
				for (int y = 0; y < height; y++)
				{
					row(y);
				}
			}
		}

		/// <summary>Colour channel times alpha, rounded (exact division by 255).</summary>
		public static uint Premultiply(uint argb)
		{
			uint a = argb >> 24;
			if (a == 255)
			{
				return argb;
			}
			if (a == 0)
			{
				return 0;
			}
			uint r = Mul255((argb >> 16) & 0xFF, a);
			uint g = Mul255((argb >> 8) & 0xFF, a);
			uint b = Mul255(argb & 0xFF, a);
			return (a << 24) | (r << 16) | (g << 8) | b;
		}

		/// <summary>Inverse of <see cref="Premultiply"/>, rounded and clamped. Fully transparent pixels become 0.</summary>
		public static uint Unpremultiply(uint pargb)
		{
			uint a = pargb >> 24;
			if (a == 255)
			{
				return pargb;
			}
			if (a == 0)
			{
				return 0;
			}
			uint r = Div255(((pargb >> 16) & 0xFF), a);
			uint g = Div255(((pargb >> 8) & 0xFF), a);
			uint b = Div255((pargb & 0xFF), a);
			return (a << 24) | (r << 16) | (g << 8) | b;
		}

		private static uint Mul255(uint c, uint a)
		{
			uint t = c * a + 128;
			return (t + (t >> 8)) >> 8;
		}

		private static uint Div255(uint c, uint a)
		{
			uint v = (c * 255 + (a >> 1)) / a;
			return v > 255 ? 255 : v;
		}

		/// <summary>One pixel layout: loads to and stores from the un-premultiplied ARGB value.</summary>
		private interface IPx
		{
			static abstract int Bpp { get; }

			static abstract uint Load(byte* p);

			static abstract void Store(byte* p, uint argb);
		}

		private struct ArgbPx : IPx
		{
			public static int Bpp => 4;

			public static uint Load(byte* p) => *(uint*)p;

			public static void Store(byte* p, uint argb) => *(uint*)p = argb;
		}

		private struct PArgbPx : IPx
		{
			public static int Bpp => 4;

			public static uint Load(byte* p) => Unpremultiply(*(uint*)p);

			public static void Store(byte* p, uint argb) => *(uint*)p = Premultiply(argb);
		}

		private struct Rgb32Px : IPx
		{
			public static int Bpp => 4;

			public static uint Load(byte* p) => *(uint*)p | 0xFF000000u;

			public static void Store(byte* p, uint argb) => *(uint*)p = argb | 0xFF000000u;
		}

		private struct Rgb24Px : IPx
		{
			public static int Bpp => 3;

			public static uint Load(byte* p) => 0xFF000000u | ((uint)p[2] << 16) | ((uint)p[1] << 8) | p[0];

			public static void Store(byte* p, uint argb)
			{
				p[0] = (byte)argb;
				p[1] = (byte)(argb >> 8);
				p[2] = (byte)(argb >> 16);
			}
		}
	}
}
