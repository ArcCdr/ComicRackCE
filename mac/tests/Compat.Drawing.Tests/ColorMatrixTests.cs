using System;
using System.Drawing;
using System.Drawing.Imaging;
using Xunit;
using static Compat.Drawing.Tests.TestImages;

namespace Compat.Drawing.Tests
{
	/// <summary>ColorMatrix, ImageAttributes.SetColorMatrix and their effect in Graphics.DrawImage.</summary>
	public class ColorMatrixTests
	{
		private static ColorMatrix Matrix(params float[] rowMajor25)
		{
			float[][] rows = new float[5][];
			for (int r = 0; r < 5; r++)
			{
				rows[r] = new float[5];
				Array.Copy(rowMajor25, r * 5, rows[r], 0, 5);
			}
			return new ColorMatrix(rows);
		}

		/// <summary>The matrix GDI+ code uses for grayscale (the ITU-R BT.601 weights, in the column of each output channel).</summary>
		private static ColorMatrix Grayscale() => Matrix(
			0.299f, 0.299f, 0.299f, 0, 0,
			0.587f, 0.587f, 0.587f, 0, 0,
			0.114f, 0.114f, 0.114f, 0, 0,
			0, 0, 0, 1, 0,
			0, 0, 0, 0, 1);

		private static Bitmap Draw(Bitmap source, ColorMatrix matrix, PixelFormat targetFormat = PixelFormat.Format32bppArgb, Color? background = null)
		{
			Bitmap target = new Bitmap(source.Width, source.Height, targetFormat);
			using (Graphics g = Graphics.FromImage(target))
			using (ImageAttributes attributes = new ImageAttributes())
			{
				if (background.HasValue)
				{
					g.Clear(background.Value);
				}
				attributes.SetColorMatrix(matrix);
				g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
			}
			return target;
		}

		private static Bitmap Colours()
		{
			// a grid of saturated and mixed colours, so that every channel weight matters
			Bitmap bitmap = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
			for (int y = 0; y < 16; y++)
			{
				for (int x = 0; x < 16; x++)
				{
					bitmap.SetPixel(x, y, Color.FromArgb(255, x * 17, y * 17, (x * 7 + y * 13) % 256));
				}
			}
			return bitmap;
		}

		// ---- ColorMatrix -----------------------------------------------------------------------------------------------------------

		[Fact]
		public void The_default_matrix_is_the_identity()
		{
			ColorMatrix m = new ColorMatrix();
			for (int r = 0; r < 5; r++)
			{
				for (int c = 0; c < 5; c++)
				{
					Assert.Equal(r == c ? 1f : 0f, m[r, c]);
				}
			}
			Assert.Equal(1f, m.Matrix33);
		}

		[Fact]
		public void The_indexer_and_Matrix33_read_and_write_the_same_cells()
		{
			ColorMatrix m = new ColorMatrix();
			m[2, 4] = 0.25f;
			Assert.Equal(0.25f, m[2, 4]);
			m.Matrix33 = 0.5f;
			Assert.Equal(0.5f, m[3, 3]);
			m[3, 3] = 0.75f;
			Assert.Equal(0.75f, m.Matrix33);
		}

		[Fact]
		public void The_indexer_checks_its_range()
		{
			ColorMatrix m = new ColorMatrix();
			Assert.Throws<IndexOutOfRangeException>(() => m[5, 0]);
			Assert.Throws<IndexOutOfRangeException>(() => m[0, -1]);
			Assert.Throws<IndexOutOfRangeException>(() => m[0, 5] = 1);
		}

		[Fact]
		public void The_jagged_array_constructor_copies_the_values()
		{
			float[][] rows = new float[5][];
			for (int r = 0; r < 5; r++)
			{
				rows[r] = new float[5];
				rows[r][r] = 2f;
			}
			rows[1][3] = 0.5f;
			ColorMatrix m = new ColorMatrix(rows);
			rows[1][3] = 99f;
			Assert.Equal(0.5f, m[1, 3]);
			Assert.Equal(2f, m[4, 4]);
		}

		[Fact]
		public void The_jagged_array_constructor_refuses_anything_but_5_by_5()
		{
			Assert.Throws<ArgumentNullException>(() => new ColorMatrix(null));
			Assert.Throws<ArgumentException>(() => new ColorMatrix(new float[4][]));
			float[][] ragged = new float[5][];
			for (int r = 0; r < 5; r++) ragged[r] = new float[5];
			ragged[2] = new float[4];
			Assert.Throws<ArgumentException>(() => new ColorMatrix(ragged));
			ragged[2] = null;
			Assert.Throws<ArgumentException>(() => new ColorMatrix(ragged));
		}

		[Fact]
		public void ToSkia_transposes_the_4x4_block_and_moves_the_fifth_row_to_the_translation()
		{
			ColorMatrix m = new ColorMatrix();
			m[0, 2] = 0.7f; // input R contributes to output B
			m[4, 1] = 0.1f; // translation of output G
			m[0, 4] = 5f; // output "w" column: ignored
			float[] s = m.ToSkia();
			Assert.Equal(20, s.Length);
			Assert.Equal(0.7f, s[2 * 5 + 0]); // row = output B, column = input R
			Assert.Equal(0f, s[0 * 5 + 2]);
			Assert.Equal(0.1f, s[1 * 5 + 4]);
			Assert.Equal(1f, s[0]);
			Assert.Equal(1f, s[3 * 5 + 3]);
			Assert.Equal(0f, s[4]);
			Assert.Equal(0f, s[19]);
		}

		// ---- ImageAttributes -------------------------------------------------------------------------------------------------------

		[Fact]
		public void SetColorMatrix_null_throws_and_a_disposed_ImageAttributes_refuses_calls()
		{
			ImageAttributes a = new ImageAttributes();
			Assert.Throws<ArgumentNullException>(() => a.SetColorMatrix(null));
			a.Dispose();
			a.Dispose();
			Assert.Throws<ObjectDisposedException>(() => a.SetColorMatrix(new ColorMatrix()));
			Assert.Throws<ObjectDisposedException>(() => a.Clone());
		}

		[Fact]
		public void The_matrix_is_copied_when_it_is_set()
		{
			using (Bitmap source = Colours())
			{
				ColorMatrix m = new ColorMatrix();
				Bitmap target = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
				using (Graphics g = Graphics.FromImage(target))
				using (ImageAttributes a = new ImageAttributes())
				{
					a.SetColorMatrix(m);
					m[0, 0] = 0f; // after the call: must not matter
					m[1, 1] = 0f;
					g.DrawImage(source, new Rectangle(0, 0, 16, 16), 0, 0, 16, 16, GraphicsUnit.Pixel, a);
				}
				using (target)
				{
					AssertColor(source.GetPixel(9, 4), target.GetPixel(9, 4), 1);
				}
			}
		}

		[Fact]
		public void Clone_copies_the_matrix()
		{
			using (Bitmap source = Colours())
			using (ImageAttributes a = new ImageAttributes())
			{
				a.SetColorMatrix(Grayscale());
				using (ImageAttributes clone = (ImageAttributes)a.Clone())
				using (Bitmap target = new Bitmap(16, 16, PixelFormat.Format32bppArgb))
				{
					using (Graphics g = Graphics.FromImage(target))
					{
						g.DrawImage(source, new Rectangle(0, 0, 16, 16), 0, 0, 16, 16, GraphicsUnit.Pixel, clone);
					}
					Color c = target.GetPixel(9, 4);
					Assert.Equal(c.R, c.G);
					Assert.Equal(c.G, c.B);
				}
			}
		}

		[Fact]
		public void Attributes_without_a_matrix_change_nothing()
		{
			using (Bitmap source = Colours())
			using (Bitmap target = new Bitmap(16, 16, PixelFormat.Format32bppArgb))
			{
				using (Graphics g = Graphics.FromImage(target))
				using (ImageAttributes a = new ImageAttributes())
				{
					g.DrawImage(source, new Rectangle(0, 0, 16, 16), 0, 0, 16, 16, GraphicsUnit.Pixel, a);
				}
				AssertColor(source.GetPixel(3, 11), target.GetPixel(3, 11), 1);
			}
		}

		// ---- the effect of the matrix ----------------------------------------------------------------------------------------------

		[Theory]
		[InlineData(PixelFormat.Format32bppArgb)]
		[InlineData(PixelFormat.Format32bppPArgb)]
		[InlineData(PixelFormat.Format32bppRgb)]
		[InlineData(PixelFormat.Format24bppRgb)]
		public void The_grayscale_matrix_gives_the_luma_of_every_pixel_within_2_levels(PixelFormat targetFormat)
		{
			using (Bitmap source = Colours())
			using (Bitmap gray = Draw(source, Grayscale(), targetFormat))
			{
				for (int y = 0; y < 16; y++)
				{
					for (int x = 0; x < 16; x++)
					{
						Color c = source.GetPixel(x, y);
						int expected = (int)Math.Round(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
						Color g = gray.GetPixel(x, y);
						Assert.InRange(g.R, expected - 2, expected + 2);
						Assert.InRange(g.G, expected - 2, expected + 2);
						Assert.InRange(g.B, expected - 2, expected + 2);
						Assert.Equal(255, g.A);
					}
				}
			}
		}

		[Theory]
		[InlineData(PixelFormat.Format24bppRgb)]
		[InlineData(PixelFormat.Format32bppPArgb)]
		public void The_grayscale_matrix_works_for_every_source_format(PixelFormat sourceFormat)
		{
			using (Bitmap argb = Colours())
			using (Bitmap source = argb.Clone(new Rectangle(0, 0, 16, 16), sourceFormat))
			using (Bitmap gray = Draw(source, Grayscale()))
			{
				for (int y = 0; y < 16; y += 3)
				{
					for (int x = 0; x < 16; x += 3)
					{
						Color c = argb.GetPixel(x, y);
						int expected = (int)Math.Round(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
						Assert.InRange(gray.GetPixel(x, y).G, expected - 2, expected + 2);
					}
				}
			}
		}

		[Fact]
		public void The_matrix_is_applied_in_the_GDI_row_vector_order()
		{
			// m[0, 2] = 1: the input red goes into the output blue. Swap red and blue.
			ColorMatrix swap = Matrix(
				0, 0, 1, 0, 0,
				0, 1, 0, 0, 0,
				1, 0, 0, 0, 0,
				0, 0, 0, 1, 0,
				0, 0, 0, 0, 1);
			using (Bitmap source = Colours())
			using (Bitmap swapped = Draw(source, swap))
			{
				for (int y = 0; y < 16; y += 2)
				{
					for (int x = 0; x < 16; x += 2)
					{
						Color c = source.GetPixel(x, y);
						AssertColor(Color.FromArgb(255, c.B, c.G, c.R), swapped.GetPixel(x, y), 1);
					}
				}
			}
		}

		[Fact]
		public void The_fifth_row_translates_the_colours_in_0_to_1_units()
		{
			ColorMatrix brighter = new ColorMatrix();
			brighter[4, 0] = 0.1f; // + 25.5 levels of red
			brighter[4, 2] = -0.2f; // - 51 levels of blue
			using (Bitmap source = Solid(4, 4, Color.FromArgb(255, 100, 100, 100)))
			using (Bitmap result = Draw(source, brighter))
			{
				AssertColor(Color.FromArgb(255, 126, 100, 49), result.GetPixel(2, 2), 2);
			}
		}

		[Fact]
		public void Results_are_clamped_to_the_valid_range()
		{
			ColorMatrix twice = Matrix(
				2, 0, 0, 0, 0,
				0, 2, 0, 0, 0,
				0, 0, 2, 0, 0,
				0, 0, 0, 1, 0,
				0, 0, 0, 0, 1);
			using (Bitmap source = Solid(2, 2, Color.FromArgb(255, 200, 50, 0)))
			using (Bitmap result = Draw(source, twice))
			{
				AssertColor(Color.FromArgb(255, 255, 100, 0), result.GetPixel(0, 0), 1);
			}
		}

		[Fact]
		public void Matrix33_scales_the_alpha_the_way_the_Core_draws_with_opacity()
		{
			ColorMatrix half = new ColorMatrix { Matrix33 = 0.5f };
			using (Bitmap source = Solid(4, 4, Color.FromArgb(255, 200, 100, 50)))
			{
				using (Bitmap onTransparent = Draw(source, half))
				{
					// not premultiplied: the colour is untouched, the alpha is halved
					AssertColor(Color.FromArgb(128, 200, 100, 50), onTransparent.GetPixel(1, 1), 2);
				}
				using (Bitmap white = Draw(source, half, PixelFormat.Format24bppRgb, Color.White))
				{
					// 50 % of (200, 100, 50) over white
					AssertColor(Color.FromArgb(255, 228, 178, 153), white.GetPixel(1, 1), 2);
				}
			}
		}

		[Fact]
		public void Matrix33_also_scales_the_alpha_of_a_translucent_image()
		{
			ColorMatrix quarter = new ColorMatrix { Matrix33 = 0.25f };
			using (Bitmap source = Solid(4, 4, Color.FromArgb(128, 255, 0, 0)))
			using (Bitmap result = Draw(source, quarter))
			{
				AssertColor(Color.FromArgb(32, 255, 0, 0), result.GetPixel(1, 1), 2);
			}
		}

		[Fact]
		public void The_matrix_applies_together_with_scaling_and_a_source_rectangle()
		{
			using (Bitmap source = Colours())
			using (Bitmap target = new Bitmap(8, 8, PixelFormat.Format24bppRgb))
			using (ImageAttributes a = new ImageAttributes())
			{
				a.SetColorMatrix(Grayscale());
				using (Graphics g = Graphics.FromImage(target))
				{
					g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
					g.DrawImage(source, new Rectangle(0, 0, 8, 8), 4, 4, 8, 8, GraphicsUnit.Pixel, a);
				}
				for (int y = 0; y < 8; y++)
				{
					for (int x = 0; x < 8; x++)
					{
						Color c = target.GetPixel(x, y);
						Assert.Equal(c.R, c.G);
						Assert.Equal(c.G, c.B);
					}
				}
			}
		}
	}
}
