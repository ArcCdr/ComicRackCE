using System;
using System.Drawing;
using System.Drawing.Imaging;
using Xunit;
using static Compat.Drawing.Tests.TestImages;

namespace Compat.Drawing.Tests
{
	/// <summary>Bitmap: constructors, Clone(Rectangle, PixelFormat), GetPixel and SetPixel.</summary>
	public class BitmapTests
	{
		[Fact]
		public void Constructor_with_size_and_format_makes_transparent_black_or_opaque_black()
		{
			foreach (PixelFormat format in AllFormats)
			{
				using (Bitmap b = new Bitmap(5, 4, format))
				{
					Assert.Equal(new Size(5, 4), b.Size);
					Assert.Equal(format, b.PixelFormat);
					Color expected = Image.GetPixelFormatSize(format) == 32 && (format == PixelFormat.Format32bppArgb || format == PixelFormat.Format32bppPArgb)
						? Color.FromArgb(0, 0, 0, 0)
						: Color.FromArgb(255, 0, 0, 0);
					Assert.Equal(expected, b.GetPixel(0, 0));
					Assert.Equal(expected, b.GetPixel(4, 3));
				}
			}
		}

		[Fact]
		public void Constructor_with_size_makes_a_32bpp_argb_bitmap()
		{
			using (Bitmap b = new Bitmap(3, 2))
			{
				Assert.Equal(PixelFormat.Format32bppArgb, b.PixelFormat);
				Assert.Equal(new Size(3, 2), b.Size);
				Assert.Equal(Color.FromArgb(0, 0, 0, 0), b.GetPixel(1, 1));
			}
		}

		[Fact]
		public void Constructors_reject_empty_sizes_and_unsupported_formats()
		{
			Assert.Throws<ArgumentException>(() => new Bitmap(0, 5));
			Assert.Throws<ArgumentException>(() => new Bitmap(5, -1, PixelFormat.Format24bppRgb));
			Assert.Throws<ArgumentException>(() => new Bitmap(5, 5, PixelFormat.Undefined));
			Assert.Throws<ArgumentException>(() => new Bitmap(5, 5, PixelFormat.Canonical));
			foreach (PixelFormat format in new[] { PixelFormat.Format8bppIndexed, PixelFormat.Format16bppRgb565, PixelFormat.Format48bppRgb, PixelFormat.Format64bppArgb })
			{
				Assert.Throws<NotSupportedException>(() => new Bitmap(4, 4, format));
			}
		}

		[Fact]
		public void Constructor_that_scales_an_image_waits_for_the_graphics_subset()
		{
			using (Bitmap source = new Bitmap(4, 4))
			{
				Assert.Throws<NotSupportedException>(() => new Bitmap(source, 2, 2));
			}
		}

		[Theory]
		[InlineData(PixelFormat.Format32bppArgb)]
		[InlineData(PixelFormat.Format32bppPArgb)]
		[InlineData(PixelFormat.Format32bppRgb)]
		[InlineData(PixelFormat.Format24bppRgb)]
		public void Clone_with_a_rectangle_crops_in_the_same_format(PixelFormat format)
		{
			using (Bitmap source = GradientBitmap(8, 6, format))
			using (Bitmap part = source.Clone(new Rectangle(2, 1, 4, 3), format))
			{
				Assert.Equal(format, part.PixelFormat);
				Assert.Equal(new Size(4, 3), part.Size);
				for (int y = 0; y < 3; y++)
				{
					for (int x = 0; x < 4; x++)
					{
						Assert.Equal(source.GetPixel(x + 2, y + 1), part.GetPixel(x, y));
					}
				}
			}
		}

		[Fact]
		public void Clone_converts_between_pixel_formats()
		{
			using (Bitmap argb = new Bitmap(2, 1, PixelFormat.Format32bppArgb))
			{
				argb.SetPixel(0, 0, Color.FromArgb(128, 255, 0, 0));
				argb.SetPixel(1, 0, Color.FromArgb(255, 10, 20, 30));

				using (Bitmap premultiplied = argb.Clone(new Rectangle(0, 0, 2, 1), PixelFormat.Format32bppPArgb))
				{
					Assert.Equal(PixelFormat.Format32bppPArgb, premultiplied.PixelFormat);
					Assert.Equal(0x80800000u, Raw(premultiplied, 0, 0));
					AssertColor(Color.FromArgb(128, 255, 0, 0), premultiplied.GetPixel(0, 0), 1);
					Assert.Equal(Color.FromArgb(255, 10, 20, 30), premultiplied.GetPixel(1, 0));
				}

				using (Bitmap opaque = argb.Clone(new Rectangle(0, 0, 2, 1), PixelFormat.Format24bppRgb))
				{
					Assert.Equal(PixelFormat.Format24bppRgb, opaque.PixelFormat);
					Assert.Equal(Color.FromArgb(255, 255, 0, 0), opaque.GetPixel(0, 0));
					Assert.Equal(0xFFFF0000u, Raw(opaque, 0, 0));
				}

				using (Bitmap back = argb.Clone(new Rectangle(0, 0, 2, 1), PixelFormat.Format32bppRgb))
				using (Bitmap again = back.Clone(new Rectangle(0, 0, 2, 1), PixelFormat.Format32bppArgb))
				{
					Assert.Equal(Color.FromArgb(255, 255, 0, 0), again.GetPixel(0, 0));
				}
			}
		}

		[Fact]
		public void Clone_unpremultiplies_premultiplied_pixels()
		{
			using (Bitmap premultiplied = new Bitmap(1, 1, PixelFormat.Format32bppPArgb))
			{
				premultiplied.SetPixel(0, 0, Color.FromArgb(128, 255, 0, 0));
				using (Bitmap straight = premultiplied.Clone(new Rectangle(0, 0, 1, 1), PixelFormat.Format32bppArgb))
				{
					Assert.Equal(0x80FF0000u, Raw(straight, 0, 0));
				}
				using (Bitmap flat = premultiplied.Clone(new Rectangle(0, 0, 1, 1), PixelFormat.Format24bppRgb))
				{
					Assert.Equal(0xFFFF0000u, Raw(flat, 0, 0));
				}
			}
		}

		[Fact]
		public void Clone_with_a_rectangle_follows_the_gdi_error_rules()
		{
			using (Bitmap b = new Bitmap(4, 4))
			{
				Assert.Throws<OutOfMemoryException>(() => b.Clone(new Rectangle(2, 2, 4, 4), PixelFormat.Format32bppArgb));
				Assert.Throws<OutOfMemoryException>(() => b.Clone(new Rectangle(-1, 0, 2, 2), PixelFormat.Format32bppArgb));
				Assert.Throws<ArgumentException>(() => b.Clone(new Rectangle(0, 0, 0, 2), PixelFormat.Format32bppArgb));
				Assert.Throws<NotSupportedException>(() => b.Clone(new Rectangle(0, 0, 2, 2), PixelFormat.Format8bppIndexed));
			}
		}

		[Fact]
		public void SetPixel_and_GetPixel_round_trip_not_premultiplied_formats_exactly()
		{
			Color c = Color.FromArgb(77, 1, 2, 3);
			using (Bitmap argb = new Bitmap(3, 2, PixelFormat.Format32bppArgb))
			{
				argb.SetPixel(2, 1, c);
				Assert.Equal(c, argb.GetPixel(2, 1));
				Assert.Equal(0x4D010203u, Raw(argb, 2, 1));
				Assert.Equal(Color.FromArgb(0, 0, 0, 0), argb.GetPixel(0, 0));
			}
			using (Bitmap rgb24 = new Bitmap(3, 2, PixelFormat.Format24bppRgb))
			{
				rgb24.SetPixel(2, 1, c);
				Assert.Equal(Color.FromArgb(255, 1, 2, 3), rgb24.GetPixel(2, 1));
				Assert.Equal(0xFF010203u, Raw(rgb24, 2, 1));
			}
			using (Bitmap rgb32 = new Bitmap(3, 2, PixelFormat.Format32bppRgb))
			{
				rgb32.SetPixel(0, 0, c);
				Assert.Equal(Color.FromArgb(255, 1, 2, 3), rgb32.GetPixel(0, 0));
				Assert.Equal(0xFF010203u, Raw(rgb32, 0, 0));
			}
		}

		[Fact]
		public void SetPixel_premultiplies_for_pargb_and_GetPixel_gives_the_color_back()
		{
			using (Bitmap b = new Bitmap(2, 1, PixelFormat.Format32bppPArgb))
			{
				b.SetPixel(0, 0, Color.FromArgb(128, 255, 0, 0));
				Assert.Equal(0x80800000u, Raw(b, 0, 0));
				Assert.Equal(Color.FromArgb(128, 255, 0, 0), b.GetPixel(0, 0));

				b.SetPixel(1, 0, Color.FromArgb(100, 200, 50, 10));
				AssertColor(Color.FromArgb(100, 200, 50, 10), b.GetPixel(1, 0), 3);

				b.SetPixel(1, 0, Color.FromArgb(0, 200, 50, 10));
				Assert.Equal(0u, Raw(b, 1, 0));

				b.SetPixel(1, 0, Color.FromArgb(255, 200, 50, 10));
				Assert.Equal(Color.FromArgb(255, 200, 50, 10), b.GetPixel(1, 0));
			}
		}

		[Fact]
		public void GetPixel_and_SetPixel_check_the_coordinates()
		{
			using (Bitmap b = new Bitmap(3, 2))
			{
				Assert.Throws<ArgumentOutOfRangeException>(() => b.GetPixel(3, 0));
				Assert.Throws<ArgumentOutOfRangeException>(() => b.GetPixel(0, 2));
				Assert.Throws<ArgumentOutOfRangeException>(() => b.GetPixel(-1, 0));
				Assert.Throws<ArgumentOutOfRangeException>(() => b.SetPixel(0, -1, Color.Red));
				Assert.Throws<ArgumentOutOfRangeException>(() => b.SetPixel(3, 1, Color.Red));
			}
		}

		[Fact]
		public void SetPixel_is_refused_while_a_region_is_locked()
		{
			using (Bitmap b = new Bitmap(3, 2))
			{
				BitmapData data = b.LockBits(new Rectangle(0, 0, 1, 1), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
				Assert.Throws<InvalidOperationException>(() => b.SetPixel(2, 1, Color.Red));
				b.UnlockBits(data);
				b.SetPixel(2, 1, Color.Red);
			}
		}

		[Fact]
		public void Handle_based_members_stay_unsupported()
		{
			using (Bitmap b = new Bitmap(2, 2))
			{
				Assert.Throws<NotSupportedException>(() => b.GetHbitmap());
				Assert.Throws<NotSupportedException>(() => b.GetHicon());
			}
		}
	}
}
