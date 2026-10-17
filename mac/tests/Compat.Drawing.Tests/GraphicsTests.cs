using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Xunit;
using static Compat.Drawing.Tests.TestImages;

namespace Compat.Drawing.Tests
{
	/// <summary>Graphics: FromImage, Dispose, Clear, FillRectangle, the state properties and the transforms.</summary>
	public class GraphicsTests
	{
		public static readonly TheoryData<PixelFormat> Formats = new TheoryData<PixelFormat>
		{
			PixelFormat.Format32bppArgb, PixelFormat.Format32bppPArgb, PixelFormat.Format32bppRgb, PixelFormat.Format24bppRgb,
		};

		private static readonly Color Clear0 = Color.FromArgb(0, 0, 0, 0);

		private static int Live => Image.LiveStorageCount;

		// ---- FromImage and Dispose -----------------------------------------------------------------------------------------------

		[Fact]
		public void FromImage_null_throws_ArgumentNullException()
		{
			Assert.Throws<ArgumentNullException>(() => Graphics.FromImage(null));
		}

		[Fact]
		public void FromImage_of_a_disposed_image_throws_ObjectDisposedException()
		{
			Bitmap bitmap = new Bitmap(4, 4);
			bitmap.Dispose();
			Assert.Throws<ObjectDisposedException>(() => Graphics.FromImage(bitmap));
		}

		[Fact]
		public void FromImage_of_a_locked_bitmap_throws_and_so_does_drawing_while_locked()
		{
			using (Bitmap bitmap = new Bitmap(8, 8))
			{
				BitmapData data = bitmap.LockBits(new Rectangle(0, 0, 8, 8), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
				Assert.Throws<InvalidOperationException>(() => Graphics.FromImage(bitmap));
				bitmap.UnlockBits(data);
				using (Graphics g = Graphics.FromImage(bitmap))
				{
					data = bitmap.LockBits(new Rectangle(0, 0, 8, 8), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
					Assert.Throws<InvalidOperationException>(() => g.Clear(Color.Red));
					bitmap.UnlockBits(data);
					g.Clear(Color.Red);
				}
			}
		}

		[Fact]
		public void Dispose_is_idempotent_and_later_calls_throw_ObjectDisposedException()
		{
			using (Bitmap bitmap = new Bitmap(8, 8))
			{
				Graphics g = Graphics.FromImage(bitmap);
				g.Dispose();
				g.Dispose();
				((IDisposable)g).Dispose();
				Assert.Throws<ObjectDisposedException>(() => g.Clear(Color.Red));
				Assert.Throws<ObjectDisposedException>(() => g.FillRectangle(Brushes.Black, 0, 0, 1, 1));
				Assert.Throws<ObjectDisposedException>(() => g.DrawImage(bitmap, 0, 0));
				Assert.Throws<ObjectDisposedException>(() => g.TranslateTransform(1, 1));
				Assert.Throws<ObjectDisposedException>(() => g.InterpolationMode);
				Assert.Throws<ObjectDisposedException>(() => g.SmoothingMode = SmoothingMode.None);
				// the bitmap is untouched by the Graphics' end
				Assert.Equal(8, bitmap.Width);
			}
		}

		[Fact]
		public void Graphics_does_not_own_the_bitmap_and_the_bitmap_does_not_need_the_graphics()
		{
			int before = LiveSettled();
			Bitmap bitmap = new Bitmap(8, 8);
			Graphics g = Graphics.FromImage(bitmap);
			Assert.Equal(before + 1, Live);
			g.Dispose();
			Assert.Equal(before + 1, Live);
			bitmap.Dispose();
			Assert.Equal(before, Live);
		}

		[Fact]
		public void A_graphics_whose_image_was_disposed_throws_ObjectDisposedException()
		{
			Bitmap bitmap = new Bitmap(8, 8);
			Graphics g = Graphics.FromImage(bitmap);
			bitmap.Dispose();
			Assert.Throws<ObjectDisposedException>(() => g.Clear(Color.Red));
			Assert.Throws<ObjectDisposedException>(() => g.FillRectangle(Brushes.Black, 0, 0, 1, 1));
			g.Dispose();
		}

		[Fact]
		public void A_Graphics_keeps_drawing_onto_the_image_after_RotateFlip_and_keeps_its_transform()
		{
			using (Bitmap bitmap = new Bitmap(6, 2, PixelFormat.Format32bppArgb))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				g.TranslateTransform(1, 0);
				bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone); // now 2 x 6
				Assert.Equal(2, bitmap.Width);
				g.FillRectangle(Brushes.Black, 0, 4, 1, 1);
				AssertColor(Color.Black, bitmap.GetPixel(1, 4));
				AssertColor(Clear0, bitmap.GetPixel(0, 4));
			}
		}

		[Fact]
		public void Several_graphics_can_draw_on_one_bitmap()
		{
			using (Bitmap bitmap = new Bitmap(4, 4, PixelFormat.Format32bppArgb))
			using (Graphics a = Graphics.FromImage(bitmap))
			using (Graphics b = Graphics.FromImage(bitmap))
			{
				a.FillRectangle(new SolidBrush(Color.Red), 0, 0, 2, 4);
				b.FillRectangle(new SolidBrush(Color.Blue), 2, 0, 2, 4);
				AssertColor(Color.Red, bitmap.GetPixel(1, 1));
				AssertColor(Color.Blue, bitmap.GetPixel(2, 1));
			}
		}

		// ---- Clear -----------------------------------------------------------------------------------------------------------------

		[Theory]
		[MemberData(nameof(Formats))]
		public void Clear_fills_every_pixel_with_the_colour(PixelFormat format)
		{
			using (Bitmap bitmap = new Bitmap(7, 5, format))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				g.Clear(Color.FromArgb(255, 200, 100, 50));
				for (int y = 0; y < 5; y++)
				{
					for (int x = 0; x < 7; x++)
					{
						AssertColor(Color.FromArgb(255, 200, 100, 50), bitmap.GetPixel(x, y));
					}
				}
			}
		}

		[Fact]
		public void Clear_with_a_translucent_colour_is_exact_for_Argb_and_premultiplies_for_PArgb()
		{
			Color c = Color.FromArgb(128, 200, 100, 50);
			using (Bitmap argb = new Bitmap(3, 3, PixelFormat.Format32bppArgb))
			using (Bitmap pargb = new Bitmap(3, 3, PixelFormat.Format32bppPArgb))
			{
				using (Graphics g = Graphics.FromImage(argb)) g.Clear(c);
				using (Graphics g = Graphics.FromImage(pargb)) g.Clear(c);
				Assert.Equal(0x80C86432u, Raw(argb, 1, 1));
				Assert.Equal(c, argb.GetPixel(1, 1));
				uint premultiplied = Raw(pargb, 1, 1);
				Assert.Equal(0x80u, premultiplied >> 24);
				Assert.InRange((int)((premultiplied >> 16) & 0xFF), 99, 101); // 200 * 128 / 255
				AssertColor(c, pargb.GetPixel(1, 1), 2);
			}
		}

		[Theory]
		[InlineData(PixelFormat.Format32bppRgb)]
		[InlineData(PixelFormat.Format24bppRgb)]
		public void Clear_keeps_opaque_formats_opaque_and_applies_the_alpha_to_the_colour(PixelFormat format)
		{
			using (Bitmap bitmap = new Bitmap(3, 3, format))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				g.Clear(Color.Transparent); // (0, 255, 255, 255) times its alpha is black
				Assert.Equal(0xFF000000u, Raw(bitmap, 1, 1));
				g.Clear(Color.FromArgb(128, 200, 100, 50));
				AssertColor(Color.FromArgb(255, 100, 50, 25), bitmap.GetPixel(2, 2), 1);
				Assert.Equal(0xFFu, Raw(bitmap, 2, 2) >> 24);
				g.Clear(Color.FromArgb(255, 20, 30, 40));
				Assert.Equal(0xFF141E28u, Raw(bitmap, 0, 0));
			}
		}

		[Fact]
		public void Clear_ignores_the_transform()
		{
			using (Bitmap bitmap = new Bitmap(6, 6, PixelFormat.Format32bppArgb))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				g.TranslateTransform(3, 3);
				g.ScaleTransform(0.1f, 0.1f);
				g.Clear(Color.Green);
				AssertColor(Color.Green, bitmap.GetPixel(0, 0));
				AssertColor(Color.Green, bitmap.GetPixel(5, 5));
			}
		}

		// ---- FillRectangle ---------------------------------------------------------------------------------------------------------

		[Theory]
		[MemberData(nameof(Formats))]
		public void FillRectangle_paints_exactly_the_rectangle(PixelFormat format)
		{
			using (Bitmap bitmap = new Bitmap(10, 10, format))
			using (Graphics g = Graphics.FromImage(bitmap))
			using (SolidBrush red = new SolidBrush(Color.Red))
			{
				g.Clear(Color.White);
				g.FillRectangle(red, new Rectangle(2, 3, 4, 2));
				for (int y = 0; y < 10; y++)
				{
					for (int x = 0; x < 10; x++)
					{
						bool inside = x >= 2 && x < 6 && y >= 3 && y < 5;
						AssertColor(inside ? Color.Red : Color.White, bitmap.GetPixel(x, y));
					}
				}
			}
		}

		[Fact]
		public void FillRectangle_with_four_ints_is_the_same_as_with_a_Rectangle()
		{
			using (Bitmap bitmap = new Bitmap(6, 6, PixelFormat.Format32bppArgb))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				g.FillRectangle(Brushes.Black, 1, 2, 3, 1);
				AssertColor(Color.Black, bitmap.GetPixel(1, 2));
				AssertColor(Color.Black, bitmap.GetPixel(3, 2));
				AssertColor(Clear0, bitmap.GetPixel(4, 2));
				AssertColor(Clear0, bitmap.GetPixel(1, 3));
			}
		}

		[Fact]
		public void FillRectangle_blends_a_translucent_colour_over_the_pixels()
		{
			using (Bitmap opaque = new Bitmap(4, 4, PixelFormat.Format24bppRgb))
			using (Bitmap argb = new Bitmap(4, 4, PixelFormat.Format32bppArgb))
			using (SolidBrush half = new SolidBrush(Color.FromArgb(128, 255, 0, 0)))
			{
				using (Graphics g = Graphics.FromImage(opaque))
				{
					g.Clear(Color.White);
					g.FillRectangle(half, 0, 0, 4, 4);
				}
				// 50 % red over white: (255, 127, 127)
				AssertColor(Color.FromArgb(255, 255, 127, 127), opaque.GetPixel(2, 2), 2);
				using (Graphics g = Graphics.FromImage(argb))
				{
					g.FillRectangle(half, 0, 0, 4, 4);
				}
				// onto transparent, the not premultiplied pixel is the brush colour itself
				AssertColor(Color.FromArgb(128, 255, 0, 0), argb.GetPixel(2, 2), 1);
			}
		}

		[Fact]
		public void FillRectangle_with_SourceCopy_replaces_the_pixels_including_alpha()
		{
			using (Bitmap argb = new Bitmap(4, 4, PixelFormat.Format32bppArgb))
			using (Graphics g = Graphics.FromImage(argb))
			{
				g.Clear(Color.White);
				g.CompositingMode = CompositingMode.SourceCopy;
				g.FillRectangle(new SolidBrush(Color.FromArgb(64, 0, 0, 255)), 1, 1, 2, 2);
				AssertColor(Color.FromArgb(64, 0, 0, 255), argb.GetPixel(1, 1), 1);
				AssertColor(Color.White, argb.GetPixel(0, 0));
			}
		}

		[Fact]
		public void FillRectangle_checks_its_arguments_and_the_brush_type()
		{
			using (Bitmap bitmap = new Bitmap(4, 4))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				Assert.Throws<ArgumentNullException>(() => g.FillRectangle(null, 0, 0, 1, 1));
				Assert.Throws<ArgumentNullException>(() => g.FillRectangle(null, new Rectangle(0, 0, 1, 1)));
			}
		}

		[Fact]
		public void FillRectangle_with_an_empty_rectangle_changes_nothing()
		{
			using (Bitmap bitmap = new Bitmap(4, 4, PixelFormat.Format32bppArgb))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				g.FillRectangle(Brushes.Black, 1, 1, 0, 3);
				g.FillRectangle(Brushes.Black, 1, 1, 3, 0);
				Assert.Equal(0u, Raw(bitmap, 1, 1));
			}
		}

		[Fact]
		public void FillRectangle_outside_the_bitmap_is_clipped()
		{
			using (Bitmap bitmap = new Bitmap(4, 4, PixelFormat.Format32bppArgb))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				g.FillRectangle(Brushes.Black, -10, -10, 12, 12);
				g.FillRectangle(Brushes.Black, 3, 3, 100, 100);
				AssertColor(Color.Black, bitmap.GetPixel(0, 0));
				AssertColor(Color.Black, bitmap.GetPixel(1, 1));
				AssertColor(Clear0, bitmap.GetPixel(2, 2));
				AssertColor(Color.Black, bitmap.GetPixel(3, 3));
			}
		}

		// ---- opaque formats keep alpha 255 -----------------------------------------------------------------------------------------

		[Theory]
		[InlineData(PixelFormat.Format32bppRgb)]
		[InlineData(PixelFormat.Format24bppRgb)]
		public void Drawing_onto_an_opaque_bitmap_never_leaves_alpha_below_255(PixelFormat format)
		{
			using (Bitmap source = new Bitmap(8, 8, PixelFormat.Format32bppArgb))
			using (Bitmap bitmap = new Bitmap(8, 8, format))
			{
				using (Graphics s = Graphics.FromImage(source)) s.Clear(Color.FromArgb(40, 255, 0, 0));
				using (Graphics g = Graphics.FromImage(bitmap))
				{
					g.FillRectangle(new SolidBrush(Color.FromArgb(10, 0, 255, 0)), 0, 0, 8, 8);
					g.CompositingMode = CompositingMode.SourceCopy;
					g.FillRectangle(new SolidBrush(Color.FromArgb(10, 0, 255, 0)), 0, 0, 4, 4);
					g.DrawImage(source, 0, 0);
					g.CompositingMode = CompositingMode.SourceOver;
					g.DrawImage(source, 4, 4);
				}
				for (int y = 0; y < 8; y++)
				{
					for (int x = 0; x < 8; x++)
					{
						Assert.Equal(0xFFu, Raw(bitmap, x, y) >> 24);
					}
				}
			}
		}

		// ---- state properties ------------------------------------------------------------------------------------------------------

		[Fact]
		public void A_new_Graphics_has_the_GDI_defaults()
		{
			using (Bitmap bitmap = new Bitmap(2, 2))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				Assert.Equal(InterpolationMode.Bilinear, g.InterpolationMode);
				Assert.Equal(SmoothingMode.None, g.SmoothingMode);
				Assert.Equal(CompositingQuality.Default, g.CompositingQuality);
				Assert.Equal(CompositingMode.SourceOver, g.CompositingMode);
				Assert.Equal(PixelOffsetMode.Default, g.PixelOffsetMode);
			}
		}

		[Fact]
		public void State_properties_round_trip_every_valid_value()
		{
			using (Bitmap bitmap = new Bitmap(2, 2))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				foreach (InterpolationMode v in new[] { InterpolationMode.Default, InterpolationMode.Low, InterpolationMode.High, InterpolationMode.Bilinear, InterpolationMode.Bicubic, InterpolationMode.NearestNeighbor, InterpolationMode.HighQualityBilinear, InterpolationMode.HighQualityBicubic })
				{
					g.InterpolationMode = v;
					Assert.Equal(v, g.InterpolationMode);
				}
				foreach (SmoothingMode v in new[] { SmoothingMode.Default, SmoothingMode.HighSpeed, SmoothingMode.HighQuality, SmoothingMode.None, SmoothingMode.AntiAlias })
				{
					g.SmoothingMode = v;
					Assert.Equal(v, g.SmoothingMode);
				}
				foreach (CompositingQuality v in new[] { CompositingQuality.Default, CompositingQuality.HighSpeed, CompositingQuality.HighQuality, CompositingQuality.GammaCorrected, CompositingQuality.AssumeLinear })
				{
					g.CompositingQuality = v;
					Assert.Equal(v, g.CompositingQuality);
				}
				foreach (CompositingMode v in new[] { CompositingMode.SourceOver, CompositingMode.SourceCopy })
				{
					g.CompositingMode = v;
					Assert.Equal(v, g.CompositingMode);
				}
				foreach (PixelOffsetMode v in new[] { PixelOffsetMode.Default, PixelOffsetMode.HighSpeed, PixelOffsetMode.HighQuality, PixelOffsetMode.None, PixelOffsetMode.Half })
				{
					g.PixelOffsetMode = v;
					Assert.Equal(v, g.PixelOffsetMode);
				}
			}
		}

		[Fact]
		public void State_properties_refuse_Invalid_and_out_of_range_values()
		{
			using (Bitmap bitmap = new Bitmap(2, 2))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				Assert.Throws<InvalidEnumArgumentException>(() => g.InterpolationMode = InterpolationMode.Invalid);
				Assert.Throws<InvalidEnumArgumentException>(() => g.InterpolationMode = (InterpolationMode)99);
				Assert.Throws<InvalidEnumArgumentException>(() => g.SmoothingMode = SmoothingMode.Invalid);
				Assert.Throws<InvalidEnumArgumentException>(() => g.CompositingQuality = CompositingQuality.Invalid);
				Assert.Throws<InvalidEnumArgumentException>(() => g.CompositingMode = (CompositingMode)5);
				Assert.Throws<InvalidEnumArgumentException>(() => g.PixelOffsetMode = PixelOffsetMode.Invalid);
				Assert.Equal(InterpolationMode.Bilinear, g.InterpolationMode); // unchanged
			}
		}

		[Theory]
		[InlineData(InterpolationMode.NearestNeighbor, SkiaSharp.SKFilterMode.Nearest, SkiaSharp.SKMipmapMode.None, false)]
		[InlineData(InterpolationMode.Low, SkiaSharp.SKFilterMode.Linear, SkiaSharp.SKMipmapMode.None, false)]
		[InlineData(InterpolationMode.Default, SkiaSharp.SKFilterMode.Linear, SkiaSharp.SKMipmapMode.None, false)]
		[InlineData(InterpolationMode.Bilinear, SkiaSharp.SKFilterMode.Linear, SkiaSharp.SKMipmapMode.None, false)]
		[InlineData(InterpolationMode.HighQualityBilinear, SkiaSharp.SKFilterMode.Linear, SkiaSharp.SKMipmapMode.Linear, false)]
		[InlineData(InterpolationMode.Bicubic, SkiaSharp.SKFilterMode.Nearest, SkiaSharp.SKMipmapMode.None, true)]
		[InlineData(InterpolationMode.HighQualityBicubic, SkiaSharp.SKFilterMode.Nearest, SkiaSharp.SKMipmapMode.None, true)]
		[InlineData(InterpolationMode.High, SkiaSharp.SKFilterMode.Nearest, SkiaSharp.SKMipmapMode.None, true)]
		public void Interpolation_modes_map_to_the_planned_Skia_sampling(InterpolationMode mode, SkiaSharp.SKFilterMode filter, SkiaSharp.SKMipmapMode mip, bool cubic)
		{
			SkiaSharp.SKSamplingOptions s = Graphics.Sampling(mode);
			Assert.Equal(cubic, s.UseCubic);
			if (cubic)
			{
				Assert.Equal(SkiaSharp.SKCubicResampler.Mitchell.B, s.Cubic.B);
				Assert.Equal(SkiaSharp.SKCubicResampler.Mitchell.C, s.Cubic.C);
			}
			else
			{
				Assert.Equal(filter, s.Filter);
				Assert.Equal(mip, s.Mipmap);
			}
		}

		// ---- transform -------------------------------------------------------------------------------------------------------------

		private static int CountPainted(Bitmap bitmap, out Rectangle bounds)
		{
			int count = 0, minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
			for (int y = 0; y < bitmap.Height; y++)
			{
				for (int x = 0; x < bitmap.Width; x++)
				{
					if (bitmap.GetPixel(x, y).A != 0)
					{
						count++;
						minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
					}
				}
			}
			bounds = count == 0 ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
			return count;
		}

		[Fact]
		public void TranslateTransform_moves_what_is_drawn()
		{
			using (Bitmap bitmap = new Bitmap(20, 20, PixelFormat.Format32bppArgb))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				g.TranslateTransform(10, 5);
				g.FillRectangle(Brushes.Black, 0, 0, 2, 3);
				Assert.Equal(6, CountPainted(bitmap, out Rectangle bounds));
				Assert.Equal(new Rectangle(10, 5, 2, 3), bounds);
			}
		}

		[Fact]
		public void ScaleTransform_scales_what_is_drawn()
		{
			using (Bitmap bitmap = new Bitmap(20, 20, PixelFormat.Format32bppArgb))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				g.ScaleTransform(2, 3);
				g.FillRectangle(Brushes.Black, 1, 1, 2, 1);
				CountPainted(bitmap, out Rectangle bounds);
				Assert.Equal(new Rectangle(2, 3, 4, 3), bounds);
			}
		}

		[Fact]
		public void Transform_order_defaults_to_Prepend_and_Append_applies_last()
		{
			using (Bitmap bitmap = new Bitmap(60, 20, PixelFormat.Format32bppArgb))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				// Prepend: the scale applies to the coordinates first, then the translation: x -> 2x + 10
				g.TranslateTransform(10, 0);
				g.ScaleTransform(2, 1);
				g.FillRectangle(Brushes.Black, 1, 0, 1, 1);
				CountPainted(bitmap, out Rectangle bounds);
				Assert.Equal(new Rectangle(12, 0, 2, 1), bounds);

				// Append: the scale applies after the translation: x -> 2 * (x + 10)
				g.ResetTransform();
				g.Clear(Clear0);
				g.TranslateTransform(10, 0);
				g.ScaleTransform(2, 1, MatrixOrder.Append);
				g.FillRectangle(Brushes.Black, 1, 0, 1, 1);
				CountPainted(bitmap, out bounds);
				Assert.Equal(new Rectangle(22, 0, 2, 1), bounds);
			}
		}

		[Fact]
		public void TranslateTransform_has_the_same_orders()
		{
			using (Bitmap bitmap = new Bitmap(60, 20, PixelFormat.Format32bppArgb))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				g.ScaleTransform(2, 1);
				g.TranslateTransform(10, 0); // prepend: x -> 2 * (x + 10)
				g.FillRectangle(Brushes.Black, 0, 0, 1, 1);
				CountPainted(bitmap, out Rectangle bounds);
				Assert.Equal(new Rectangle(20, 0, 2, 1), bounds);

				g.ResetTransform();
				g.Clear(Clear0);
				g.ScaleTransform(2, 1);
				g.TranslateTransform(10, 0, MatrixOrder.Append); // x -> 2x + 10
				g.FillRectangle(Brushes.Black, 0, 0, 1, 1);
				CountPainted(bitmap, out bounds);
				Assert.Equal(new Rectangle(10, 0, 2, 1), bounds);
			}
		}

		[Fact]
		public void ResetTransform_restores_identity()
		{
			using (Bitmap bitmap = new Bitmap(20, 20, PixelFormat.Format32bppArgb))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				g.TranslateTransform(7, 7);
				g.ScaleTransform(3, 3);
				g.ResetTransform();
				g.FillRectangle(Brushes.Black, 1, 2, 3, 4);
				CountPainted(bitmap, out Rectangle bounds);
				Assert.Equal(new Rectangle(1, 2, 3, 4), bounds);
			}
		}

		[Fact]
		public void Transform_applies_to_DrawImage()
		{
			using (Bitmap source = new Bitmap(2, 2, PixelFormat.Format32bppArgb))
			using (Bitmap bitmap = new Bitmap(20, 20, PixelFormat.Format32bppArgb))
			{
				using (Graphics s = Graphics.FromImage(source)) s.Clear(Color.Red);
				using (Graphics g = Graphics.FromImage(bitmap))
				{
					g.TranslateTransform(5, 6);
					g.ScaleTransform(2, 2);
					g.DrawImage(source, 0, 0);
				}
				CountPainted(bitmap, out Rectangle bounds);
				Assert.Equal(new Rectangle(5, 6, 4, 4), bounds);
			}
		}

		[Fact]
		public void Transform_with_an_invalid_order_throws()
		{
			using (Bitmap bitmap = new Bitmap(2, 2))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				Assert.Throws<ArgumentException>(() => g.TranslateTransform(1, 1, (MatrixOrder)7));
				Assert.Throws<ArgumentException>(() => g.ScaleTransform(1, 1, (MatrixOrder)7));
			}
		}

		// ---- members that stay unsupported -----------------------------------------------------------------------------------------

		[Fact]
		public void Text_clip_and_state_stack_stay_unsupported()
		{
			using (Bitmap bitmap = new Bitmap(2, 2))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				Assert.Throws<NotSupportedException>(() => g.DrawString("x", null, Brushes.Black, 0f, 0f));
				Assert.Throws<NotSupportedException>(() => g.MeasureString("x", null));
				Assert.Throws<NotSupportedException>(() => g.Save());
				Assert.Throws<NotSupportedException>(() => g.SetClip(new Rectangle(0, 0, 1, 1), CombineMode.Replace));
				Assert.Throws<NotSupportedException>(() => g.GetHdc());
			}
		}
	}
}
