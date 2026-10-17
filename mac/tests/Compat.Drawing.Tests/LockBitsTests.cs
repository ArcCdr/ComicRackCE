using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using cYo.Compat.Drawing;
using Xunit;
using static Compat.Drawing.Tests.TestImages;

namespace Compat.Drawing.Tests
{
	/// <summary>LockBits, UnlockBits and BitmapData, including the 24-bpp convert-on-lock / write-back-on-unlock round trip.</summary>
	public class LockBitsTests
	{
		private static byte[] Bytes(BitmapData data)
		{
			byte[] bytes = new byte[data.Stride * data.Height];
			Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
			return bytes;
		}

		[Fact]
		public void Lock_in_the_stored_format_is_zero_copy()
		{
			using (Bitmap b = new Bitmap(5, 3, PixelFormat.Format32bppArgb))
			{
				b.SetPixel(1, 0, Color.FromArgb(10, 20, 30, 40));
				BitmapData data = b.LockBits(new Rectangle(0, 0, 5, 3), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
				try
				{
					Assert.Equal(SkiaInterop.GetSKBitmap(b).GetPixels(), data.Scan0);
					Assert.Equal(20, data.Stride);
					Assert.Equal(5, data.Width);
					Assert.Equal(3, data.Height);
					Assert.Equal(PixelFormat.Format32bppArgb, data.PixelFormat);
					Assert.Equal(0, data.Reserved);
					// BGRA byte order, not premultiplied.
					Assert.Equal(new byte[] { 40, 30, 20, 10 }, new[] { Marshal.ReadByte(data.Scan0, 4), Marshal.ReadByte(data.Scan0, 5), Marshal.ReadByte(data.Scan0, 6), Marshal.ReadByte(data.Scan0, 7) });
					Marshal.WriteInt32(data.Scan0, 2 * 20 + 3 * 4, unchecked((int)0xFF112233));
				}
				finally
				{
					b.UnlockBits(data);
				}
				Assert.Equal(Color.FromArgb(255, 0x11, 0x22, 0x33), b.GetPixel(3, 2));
			}
		}

		[Fact]
		public void Lock_of_a_region_points_into_the_bitmap_and_keeps_the_stride()
		{
			using (Bitmap b = GradientBitmap(8, 6, PixelFormat.Format32bppArgb))
			{
				BitmapData data = b.LockBits(new Rectangle(3, 2, 4, 3), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
				try
				{
					Assert.Equal(4, data.Width);
					Assert.Equal(3, data.Height);
					Assert.Equal(32, data.Stride);
					Assert.Equal(SkiaInterop.GetSKBitmap(b).GetPixels() + 2 * 32 + 3 * 4, data.Scan0);
					Assert.Equal(unchecked((int)Raw(b, 3, 2)), Marshal.ReadInt32(data.Scan0));
					Assert.Equal(unchecked((int)Raw(b, 6, 4)), Marshal.ReadInt32(data.Scan0, 2 * 32 + 3 * 4));
				}
				finally
				{
					b.UnlockBits(data);
				}
			}
		}

		[Theory]
		[InlineData(1, 4)]
		[InlineData(2, 8)]
		[InlineData(3, 12)]
		[InlineData(4, 12)]
		[InlineData(5, 16)]
		[InlineData(6, 20)]
		[InlineData(7, 24)]
		[InlineData(8, 24)]
		[InlineData(9, 28)]
		public void Lock_as_24bpp_has_a_stride_rounded_up_to_four_bytes(int width, int stride)
		{
			using (Bitmap b = new Bitmap(width, 2, PixelFormat.Format24bppRgb))
			{
				BitmapData data = b.LockBits(new Rectangle(0, 0, width, 2), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
				try
				{
					Assert.Equal(stride, data.Stride);
					Assert.Equal(PixelFormat.Format24bppRgb, data.PixelFormat);
				}
				finally
				{
					b.UnlockBits(data);
				}
			}
		}

		[Fact]
		public void Lock_24bpp_round_trip_converts_on_lock_and_writes_back_on_unlock()
		{
			using (Bitmap b = new Bitmap(5, 3, PixelFormat.Format24bppRgb))
			{
				for (int y = 0; y < 3; y++)
				{
					for (int x = 0; x < 5; x++)
					{
						b.SetPixel(x, y, Color.FromArgb(255, 10 + x, 50 + y, 100 + x * y));
					}
				}

				BitmapData data = b.LockBits(new Rectangle(0, 0, 5, 3), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
				try
				{
					Assert.Equal(16, data.Stride);
					byte[] bytes = Bytes(data);
					for (int y = 0; y < 3; y++)
					{
						for (int x = 0; x < 5; x++)
						{
							int o = y * 16 + x * 3;
							Assert.Equal((byte)(100 + x * y), bytes[o]);
							Assert.Equal((byte)(50 + y), bytes[o + 1]);
							Assert.Equal((byte)(10 + x), bytes[o + 2]);
						}
					}

					// Change pixel (2,1) to B=1, G=2, R=3 and invert the colours of (4,2).
					Marshal.WriteByte(data.Scan0, 1 * 16 + 2 * 3, 1);
					Marshal.WriteByte(data.Scan0, 1 * 16 + 2 * 3 + 1, 2);
					Marshal.WriteByte(data.Scan0, 1 * 16 + 2 * 3 + 2, 3);
					for (int c = 0; c < 3; c++)
					{
						int o = 2 * 16 + 4 * 3 + c;
						Marshal.WriteByte(data.Scan0, o, (byte)(255 - Marshal.ReadByte(data.Scan0, o)));
					}
				}
				finally
				{
					b.UnlockBits(data);
				}

				Assert.Equal(Color.FromArgb(255, 3, 2, 1), b.GetPixel(2, 1));
				Assert.Equal(Color.FromArgb(255, 255 - 14, 255 - 52, 255 - 108), b.GetPixel(4, 2));
				// Everything else is unchanged, and the stored alpha stays opaque.
				Assert.Equal(Color.FromArgb(255, 10, 50, 100), b.GetPixel(0, 0));
				Assert.Equal(Color.FromArgb(255, 13, 52, 106), b.GetPixel(3, 2));
				for (int y = 0; y < 3; y++)
				{
					for (int x = 0; x < 5; x++)
					{
						Assert.Equal(0xFFu, Raw(b, x, y) >> 24);
					}
				}
			}
		}

		[Fact]
		public void Lock_24bpp_of_a_region_writes_back_only_that_region()
		{
			using (Bitmap b = GradientBitmap(6, 6, PixelFormat.Format24bppRgb))
			{
				Color outside = b.GetPixel(0, 0);
				BitmapData data = b.LockBits(new Rectangle(2, 3, 3, 2), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
				try
				{
					Assert.Equal(3, data.Width);
					Assert.Equal(2, data.Height);
					Assert.Equal(12, data.Stride);
					Assert.Equal(b.GetPixel(2, 3).B, Marshal.ReadByte(data.Scan0, 0));
					Assert.Equal(b.GetPixel(4, 4).R, Marshal.ReadByte(data.Scan0, 12 + 2 * 3 + 2));
					Marshal.WriteByte(data.Scan0, 0, 7);
					Marshal.WriteByte(data.Scan0, 12 + 2 * 3 + 2, 9);
				}
				finally
				{
					b.UnlockBits(data);
				}
				Assert.Equal(7, b.GetPixel(2, 3).B);
				Assert.Equal(9, b.GetPixel(4, 4).R);
				Assert.Equal(outside, b.GetPixel(0, 0));
			}
		}

		[Fact]
		public void ReadOnly_lock_does_not_write_back()
		{
			using (Bitmap b = GradientBitmap(4, 4, PixelFormat.Format24bppRgb))
			{
				Color before = b.GetPixel(1, 1);
				BitmapData data = b.LockBits(new Rectangle(0, 0, 4, 4), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
				Marshal.WriteByte(data.Scan0, 1 * data.Stride + 3, 0);
				Marshal.WriteByte(data.Scan0, 1 * data.Stride + 4, 0);
				Marshal.WriteByte(data.Scan0, 1 * data.Stride + 5, 0);
				b.UnlockBits(data);
				Assert.Equal(before, b.GetPixel(1, 1));
			}
		}

		[Fact]
		public void WriteOnly_lock_starts_zeroed_and_writes_everything_back()
		{
			using (Bitmap b = GradientBitmap(3, 2, PixelFormat.Format24bppRgb))
			{
				BitmapData data = b.LockBits(new Rectangle(0, 0, 3, 2), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
				try
				{
					Assert.All(Bytes(data), v => Assert.Equal(0, v));
					for (int y = 0; y < 2; y++)
					{
						for (int x = 0; x < 3; x++)
						{
							Marshal.WriteByte(data.Scan0, y * data.Stride + x * 3 + 2, (byte)(200 + x));
						}
					}
				}
				finally
				{
					b.UnlockBits(data);
				}
				Assert.Equal(Color.FromArgb(255, 201, 0, 0), b.GetPixel(1, 1));
			}
		}

		[Fact]
		public void Lock_converts_between_the_32_bit_formats()
		{
			using (Bitmap b = new Bitmap(2, 1, PixelFormat.Format32bppArgb))
			{
				b.SetPixel(0, 0, Color.FromArgb(128, 255, 0, 0));
				b.SetPixel(1, 0, Color.FromArgb(0, 9, 9, 9));

				BitmapData asPremultiplied = b.LockBits(new Rectangle(0, 0, 2, 1), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
				try
				{
					Assert.NotEqual(SkiaInterop.GetSKBitmap(b).GetPixels(), asPremultiplied.Scan0);
					Assert.Equal(unchecked((int)0x80800000), Marshal.ReadInt32(asPremultiplied.Scan0));
					Assert.Equal(0, Marshal.ReadInt32(asPremultiplied.Scan0, 4));
					Marshal.WriteInt32(asPremultiplied.Scan0, 4, unchecked((int)0x40100804));
				}
				finally
				{
					b.UnlockBits(asPremultiplied);
				}
				// Premultiplied 0x40: (0x10, 0x08, 0x04) / 0x40 * 255 = (64, 32, 16).
				Assert.Equal(0x80FF0000u, Raw(b, 0, 0));
				Assert.Equal(0x40402010u, Raw(b, 1, 0));

				BitmapData asRgb = b.LockBits(new Rectangle(0, 0, 2, 1), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
				try
				{
					Assert.Equal(unchecked((int)0xFFFF0000), Marshal.ReadInt32(asRgb.Scan0));
				}
				finally
				{
					b.UnlockBits(asRgb);
				}
			}
		}

		[Fact]
		public void Lock_of_an_opaque_bitmap_as_argb_reads_alpha_255_and_drops_written_alpha()
		{
			using (Bitmap b = GradientBitmap(2, 2, PixelFormat.Format24bppRgb))
			{
				BitmapData data = b.LockBits(new Rectangle(0, 0, 2, 2), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
				try
				{
					Assert.Equal(8, data.Stride);
					Assert.Equal(0xFFu, (uint)Marshal.ReadInt32(data.Scan0) >> 24);
					Marshal.WriteInt32(data.Scan0, unchecked((int)0x10AABBCC));
				}
				finally
				{
					b.UnlockBits(data);
				}
				Assert.Equal(Color.FromArgb(255, 0xAA, 0xBB, 0xCC), b.GetPixel(0, 0));
				Assert.Equal(0xFFAABBCCu, Raw(b, 0, 0));
			}
		}

		[Fact]
		public void Lock_of_an_alpha_bitmap_as_24bpp_drops_alpha_and_unpremultiplies()
		{
			using (Bitmap argb = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
			using (Bitmap pargb = new Bitmap(1, 1, PixelFormat.Format32bppPArgb))
			{
				argb.SetPixel(0, 0, Color.FromArgb(128, 10, 20, 30));
				pargb.SetPixel(0, 0, Color.FromArgb(128, 255, 0, 0));
				foreach (Bitmap b in new[] { argb, pargb })
				{
					BitmapData data = b.LockBits(new Rectangle(0, 0, 1, 1), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
					try
					{
						Color expected = b == argb ? Color.FromArgb(255, 10, 20, 30) : Color.FromArgb(255, 255, 0, 0);
						Assert.Equal(expected.B, Marshal.ReadByte(data.Scan0, 0));
						Assert.Equal(expected.G, Marshal.ReadByte(data.Scan0, 1));
						Assert.Equal(expected.R, Marshal.ReadByte(data.Scan0, 2));
					}
					finally
					{
						b.UnlockBits(data);
					}
				}
			}
		}

		[Fact]
		public void DontCare_locks_in_the_format_of_the_bitmap()
		{
			using (Bitmap b = new Bitmap(3, 1, PixelFormat.Format24bppRgb))
			{
				BitmapData data = b.LockBits(new Rectangle(0, 0, 3, 1), ImageLockMode.ReadOnly, PixelFormat.DontCare);
				try
				{
					Assert.Equal(PixelFormat.Format24bppRgb, data.PixelFormat);
					Assert.Equal(12, data.Stride);
				}
				finally
				{
					b.UnlockBits(data);
				}
			}
		}

		[Fact]
		public void Writing_alpha_through_a_zero_copy_lock_of_an_opaque_bitmap_is_normalised_on_unlock()
		{
			using (Bitmap b = new Bitmap(2, 1, PixelFormat.Format32bppRgb))
			{
				BitmapData data = b.LockBits(new Rectangle(0, 0, 2, 1), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
				Assert.Equal(SkiaInterop.GetSKBitmap(b).GetPixels(), data.Scan0);
				Marshal.WriteInt32(data.Scan0, unchecked((int)0x00123456));
				Marshal.WriteInt32(data.Scan0, 4, unchecked((int)0x7F654321));
				b.UnlockBits(data);
				Assert.Equal(0xFF123456u, Raw(b, 0, 0));
				Assert.Equal(0xFF654321u, Raw(b, 1, 0));
				Assert.Equal(Color.FromArgb(255, 0x12, 0x34, 0x56), b.GetPixel(0, 0));
			}
		}

		[Fact]
		public void Overlapping_regions_cannot_be_locked_together_but_disjoint_ones_can()
		{
			using (Bitmap b = new Bitmap(10, 10))
			{
				BitmapData first = b.LockBits(new Rectangle(0, 0, 5, 5), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
				Assert.Throws<InvalidOperationException>(() => b.LockBits(new Rectangle(4, 4, 3, 3), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb));
				Assert.Throws<InvalidOperationException>(() => b.LockBits(new Rectangle(0, 0, 10, 10), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb));
				BitmapData second = b.LockBits(new Rectangle(5, 5, 5, 5), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
				b.UnlockBits(first);
				b.UnlockBits(second);
				// A failed lock leaves nothing behind.
				b.UnlockBits(b.LockBits(new Rectangle(0, 0, 10, 10), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb));
			}
		}

		[Fact]
		public void Invalid_requests_are_rejected_and_leave_no_lock_behind()
		{
			using (Bitmap b = new Bitmap(4, 4))
			{
				Assert.Throws<ArgumentException>(() => b.LockBits(new Rectangle(0, 0, 5, 4), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb));
				Assert.Throws<ArgumentException>(() => b.LockBits(new Rectangle(-1, 0, 2, 2), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb));
				Assert.Throws<ArgumentException>(() => b.LockBits(new Rectangle(0, 0, 0, 2), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb));
				Assert.Throws<ArgumentException>(() => b.LockBits(new Rectangle(0, 0, 2, 2), (ImageLockMode)0, PixelFormat.Format32bppArgb));
				Assert.Throws<ArgumentException>(() => b.LockBits(new Rectangle(0, 0, 2, 2), ImageLockMode.ReadOnly, PixelFormat.Canonical));
				Assert.Throws<NotSupportedException>(() => b.LockBits(new Rectangle(0, 0, 2, 2), ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed));
				Assert.Throws<NotSupportedException>(() => b.LockBits(new Rectangle(0, 0, 2, 2), ImageLockMode.UserInputBuffer | ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb));
				b.UnlockBits(b.LockBits(new Rectangle(0, 0, 4, 4), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb));
			}
		}

		[Fact]
		public void UnlockBits_rejects_regions_that_are_not_locked()
		{
			using (Bitmap b = new Bitmap(4, 4))
			using (Bitmap other = new Bitmap(4, 4))
			{
				Assert.Throws<ArgumentNullException>(() => b.UnlockBits(null));
				Assert.Throws<InvalidOperationException>(() => b.UnlockBits(new BitmapData()));
				BitmapData data = b.LockBits(new Rectangle(0, 0, 4, 4), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
				Assert.Throws<InvalidOperationException>(() => other.UnlockBits(data));
				b.UnlockBits(data);
				Assert.Throws<InvalidOperationException>(() => b.UnlockBits(data));
			}
		}

		[Fact]
		public void Locked_pixels_can_be_read_from_several_threads()
		{
			using (Bitmap b = GradientBitmap(300, 200, PixelFormat.Format24bppRgb))
			{
				BitmapData data = b.LockBits(new Rectangle(0, 0, 300, 200), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
				try
				{
					int bad = 0;
					Parallel.For(0, 200, y =>
					{
						for (int x = 0; x < 300; x++)
						{
							Color expected = Gradient(x, y, 300, 200);
							int o = y * data.Stride + x * 3;
							if (Marshal.ReadByte(data.Scan0, o + 2) != expected.R || Marshal.ReadByte(data.Scan0, o + 1) != expected.G || Marshal.ReadByte(data.Scan0, o) != expected.B)
							{
								System.Threading.Interlocked.Increment(ref bad);
							}
						}
					});
					Assert.Equal(0, bad);
				}
				finally
				{
					b.UnlockBits(data);
				}
			}
		}

		[Fact]
		public void BitmapData_properties_are_settable_like_in_gdi()
		{
			BitmapData data = new BitmapData { Width = 3, Height = 4, Stride = 12, PixelFormat = PixelFormat.Format24bppRgb, Scan0 = (IntPtr)16, Reserved = 5 };
			Assert.Equal(3, data.Width);
			Assert.Equal(4, data.Height);
			Assert.Equal(12, data.Stride);
			Assert.Equal(PixelFormat.Format24bppRgb, data.PixelFormat);
			Assert.Equal((IntPtr)16, data.Scan0);
			Assert.Equal(5, data.Reserved);
		}

		[Fact]
		public void Locking_a_large_bitmap_in_24bpp_round_trips_every_pixel()
		{
			using (Bitmap b = GradientBitmap(641, 483, PixelFormat.Format24bppRgb))
			{
				BitmapData data = b.LockBits(new Rectangle(0, 0, 641, 483), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
				try
				{
					Assert.Equal(1924, data.Stride);
					for (int y = 0; y < 483; y += 7)
					{
						int o = y * data.Stride;
						Marshal.WriteByte(data.Scan0, o, (byte)(Marshal.ReadByte(data.Scan0, o) ^ 0xFF));
					}
				}
				finally
				{
					b.UnlockBits(data);
				}
				for (int y = 0; y < 483; y += 7)
				{
					Assert.Equal((byte)(Gradient(0, y, 641, 483).B ^ 0xFF), b.GetPixel(0, y).B);
				}
				AssertColor(Gradient(640, 482, 641, 483), b.GetPixel(640, 482));
			}
		}
	}
}
