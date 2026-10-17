using System;
using System.Runtime.InteropServices;
using cYo.Compat.Drawing.Internal;
using Xunit;

namespace Compat.Drawing.Tests
{
	/// <summary>The pixel format conversions behind LockBits, UnlockBits and Clone, exhaustively where that is cheap.</summary>
	public class PixelConvertTests
	{
		[Fact]
		public void Premultiply_is_the_rounded_product_for_every_color_and_alpha()
		{
			for (uint a = 0; a <= 255; a++)
			{
				for (uint c = 0; c <= 255; c++)
				{
					uint value = PixelConvert.Premultiply((a << 24) | (c << 16) | (c << 8) | c);
					uint expected = a == 0 ? 0 : (uint)Math.Round(c * a / 255.0, MidpointRounding.AwayFromZero);
					Assert.Equal(expected, value & 0xFF);
					Assert.Equal(expected, (value >> 8) & 0xFF);
					Assert.Equal(expected, (value >> 16) & 0xFF);
					Assert.Equal(a == 0 ? 0u : a, value >> 24);
				}
			}
		}

		[Fact]
		public void Unpremultiply_is_the_rounded_quotient_for_every_valid_premultiplied_value()
		{
			for (uint a = 1; a <= 255; a++)
			{
				for (uint c = 0; c <= a; c++)
				{
					uint value = PixelConvert.Unpremultiply((a << 24) | (c << 16) | (c << 8) | c);
					uint expected = (uint)Math.Min(255, Math.Round(c * 255.0 / a, MidpointRounding.AwayFromZero));
					Assert.Equal(expected, value & 0xFF);
					Assert.Equal(expected, (value >> 16) & 0xFF);
				}
			}
			Assert.Equal(0u, PixelConvert.Unpremultiply(0));
			Assert.Equal(0u, PixelConvert.Unpremultiply(0x00123456));
			// A channel above alpha is not a valid premultiplied value; it is clamped, not wrapped.
			Assert.Equal(0x10FFFFFFu, PixelConvert.Unpremultiply(0x10FFFFFF));
		}

		[Fact]
		public void Premultiplying_then_unpremultiplying_keeps_opaque_and_fully_covered_pixels_exactly()
		{
			foreach (uint pixel in new uint[] { 0xFF000000, 0xFFFFFFFF, 0xFF123456, 0xFFFE01FD })
			{
				Assert.Equal(pixel, PixelConvert.Unpremultiply(PixelConvert.Premultiply(pixel)));
			}
		}

		private static readonly PixKind[] Kinds = { PixKind.Argb, PixKind.PArgb, PixKind.Rgb32, PixKind.Rgb24 };

		// Not premultiplied sources; alpha values 0 and 255 and a few in between.
		private static readonly uint[] Samples = { 0xFF000000, 0xFFFFFFFF, 0x80FF0000, 0x4000FF00, 0xC00000FF, 0x01010203, 0xFE102030, 0x00000000, 0xFF8040C0 };

		/// <summary>The reference: the canonical not-premultiplied value of a stored pixel.</summary>
		private static uint Canonical(uint stored, PixKind kind) => kind switch
		{
			PixKind.Argb => stored,
			PixKind.PArgb => PixelConvert.Unpremultiply(stored),
			_ => stored | 0xFF000000,
		};

		private static uint Stored(uint canonical, PixKind kind) => kind switch
		{
			PixKind.Argb => canonical,
			PixKind.PArgb => PixelConvert.Premultiply(canonical),
			_ => canonical | 0xFF000000,
		};

		private static byte[] Encode(uint[] pixels, PixKind kind, int stride)
		{
			byte[] bytes = new byte[stride];
			for (int i = 0; i < pixels.Length; i++)
			{
				uint v = Stored(pixels[i], kind);
				if (kind == PixKind.Rgb24)
				{
					bytes[i * 3] = (byte)v;
					bytes[i * 3 + 1] = (byte)(v >> 8);
					bytes[i * 3 + 2] = (byte)(v >> 16);
				}
				else
				{
					BitConverter.TryWriteBytes(new Span<byte>(bytes, i * 4, 4), v);
				}
			}
			return bytes;
		}

		private static unsafe byte[] Convert(byte[] source, PixKind from, PixKind to, int width, int srcStride, int dstStride)
		{
			byte[] target = new byte[dstStride];
			fixed (byte* s = source)
			fixed (byte* d = target)
			{
				PixelConvert.Convert((IntPtr)s, srcStride, from, (IntPtr)d, dstStride, to, width, 1);
			}
			return target;
		}

		[Fact]
		public void Every_conversion_goes_through_the_canonical_color()
		{
			int width = Samples.Length;
			foreach (PixKind from in Kinds)
			{
				foreach (PixKind to in Kinds)
				{
					int srcStride = PixelKinds.Stride(from, width);
					int dstStride = PixelKinds.Stride(to, width);
					uint[] canonicalSource = new uint[width];
					for (int i = 0; i < width; i++)
					{
						canonicalSource[i] = Canonical(Stored(Samples[i], from), from);
					}
					byte[] source = Encode(Samples, from, srcStride);
					byte[] actual = Convert(source, from, to, width, srcStride, dstStride);
					byte[] expected = Encode(canonicalSource, to, dstStride);
					Assert.True(expected.AsSpan().SequenceEqual(actual), $"{from} -> {to}: expected {System.Convert.ToHexString(expected)} but was {System.Convert.ToHexString(actual)}");
				}
			}
		}

		[Fact]
		public void Same_kind_copies_are_bit_exact_except_for_the_alpha_byte_of_rgb32()
		{
			foreach (PixKind kind in new[] { PixKind.Argb, PixKind.PArgb, PixKind.Rgb24 })
			{
				int stride = PixelKinds.Stride(kind, Samples.Length);
				byte[] source = new byte[stride];
				new Random(3).NextBytes(source);
				int pixelBytes = Samples.Length * PixelKinds.BytesPerPixel(kind);
				Assert.Equal(source[..pixelBytes], Convert(source, kind, kind, Samples.Length, stride, stride)[..pixelBytes]);
			}
			byte[] rgb32 = new byte[Samples.Length * 4];
			new Random(4).NextBytes(rgb32);
			byte[] fixedUp = Convert(rgb32, PixKind.Rgb32, PixKind.Rgb32, Samples.Length, rgb32.Length, rgb32.Length);
			for (int i = 0; i < Samples.Length; i++)
			{
				Assert.Equal(255, fixedUp[i * 4 + 3]);
				Assert.Equal(rgb32[i * 4], fixedUp[i * 4]);
				Assert.Equal(rgb32[i * 4 + 1], fixedUp[i * 4 + 1]);
				Assert.Equal(rgb32[i * 4 + 2], fixedUp[i * 4 + 2]);
			}
		}

		[Fact]
		public void Rows_are_converted_with_their_own_strides_and_padding_is_left_alone()
		{
			// 3 pixels, 3 rows; source stride 20 (4 bytes), target stride 12 (3 bytes + 3 padding)
			byte[] source = new byte[3 * 20];
			byte[] target = new byte[3 * 12];
			Array.Fill(target, (byte)0xEE);
			for (int y = 0; y < 3; y++)
			{
				for (int x = 0; x < 3; x++)
				{
					BitConverter.TryWriteBytes(new Span<byte>(source, y * 20 + x * 4, 4), 0xFF000000u | (uint)(y << 16) | (uint)(x << 8) | 0x77);
				}
			}
			unsafe
			{
				fixed (byte* s = source)
				fixed (byte* d = target)
				{
					PixelConvert.Convert((IntPtr)s, 20, PixKind.Argb, (IntPtr)d, 12, PixKind.Rgb24, 3, 3);
				}
			}
			for (int y = 0; y < 3; y++)
			{
				for (int x = 0; x < 3; x++)
				{
					Assert.Equal(0x77, target[y * 12 + x * 3]);
					Assert.Equal(x, target[y * 12 + x * 3 + 1]);
					Assert.Equal(y, target[y * 12 + x * 3 + 2]);
				}
				Assert.Equal(new byte[] { 0xEE, 0xEE, 0xEE }, target[(y * 12 + 9)..(y * 12 + 12)]);
			}
		}

		[Fact]
		public void Large_images_convert_the_same_on_several_threads()
		{
			int width = 700, height = 500;
			int srcStride = width * 4, dstStride = PixelKinds.Stride(PixKind.Rgb24, width);
			byte[] source = new byte[srcStride * height];
			new Random(11).NextBytes(source);
			byte[] target = new byte[dstStride * height];
			unsafe
			{
				fixed (byte* s = source)
				fixed (byte* d = target)
				{
					PixelConvert.Convert((IntPtr)s, srcStride, PixKind.Argb, (IntPtr)d, dstStride, PixKind.Rgb24, width, height);
				}
			}
			for (int y = 0; y < height; y += 13)
			{
				for (int x = 0; x < width; x += 11)
				{
					Assert.Equal(source[y * srcStride + x * 4], target[y * dstStride + x * 3]);
					Assert.Equal(source[y * srcStride + x * 4 + 2], target[y * dstStride + x * 3 + 2]);
				}
			}
		}
	}
}
