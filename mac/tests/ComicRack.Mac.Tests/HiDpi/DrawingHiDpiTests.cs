using System;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using Avalonia;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Reader;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;
using Bitmap = System.Drawing.Bitmap;
using Graphics = System.Drawing.Graphics;
using GraphicsUnit = System.Drawing.GraphicsUnit;
using Image = System.Drawing.Image;
using SolidBrush = System.Drawing.SolidBrush;

namespace ComicRack.Mac.Tests.HiDpi
{
	/// <summary>
	/// The drawing code below the controls at 1x, 1.5x, 2x and 3x: the painter that puts a page on a Skia canvas whose matrix carries the
	/// display scale, the hit testing of the draw operations (in device independent units), and the System.Drawing stand-in, which has
	/// no DPI of its own.
	/// </summary>
	[Trait("Category", "HiDpi")]
	public class DrawingHiDpiTests
	{
		private readonly ITestOutputHelper output;

		public DrawingHiDpiTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		private static SKColor Cover = new SKColor(200, 60, 40, 255);

		private static SKImage Solid(int width, int height, SKColor color)
		{
			using (var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque)))
			{
				bitmap.Erase(color);
				return SKImage.FromBitmap(bitmap);
			}
		}

		/// <summary>A canvas of the size of a window of <paramref name="width"/> x <paramref name="height"/> points at the scale, with the matrix a Retina window has.</summary>
		private static (SKBitmap bitmap, SKCanvas canvas) Surface(double width, double height, double scale, SKColor background)
		{
			var bitmap = new SKBitmap(new SKImageInfo((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), SKColorType.Bgra8888, SKAlphaType.Opaque));
			var canvas = new SKCanvas(bitmap);
			canvas.Clear(background);
			canvas.Scale((float)scale);
			return (bitmap, canvas);
		}

		private static Raster Read(SKBitmap bitmap)
		{
			using (SKImage image = SKImage.FromBitmap(bitmap))
			{
				return Raster.From(image);
			}
		}

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public void The_painter_covers_whole_device_pixels_and_touches_nothing_else(double scale)
		{
			SKColor ground = new SKColor(30, 30, 30, 255);
			foreach (Rect logical in new[] { new Rect(10, 7, 40, 30), new Rect(10.3, 7.2, 40.4, 30.3), new Rect(10.5, 7.5, 40.5, 30.5), new Rect(0.2, 0.2, 99.6, 69.7) })
			{
				(SKBitmap bitmap, SKCanvas canvas) = Surface(100, 70, scale, ground);
				using (bitmap)
				using (canvas)
				using (SKImage image = Solid(64, 48, Cover))
				{
					double used = ReaderPainter.DrawPage(canvas, image, logical);
					Raster r = Read(bitmap);
					RectInt expected = RectInt.Snapped(logical, scale);
					RectInt found = r.Bounds(c => Raster.Near(c, Cover, 1)).Value;
					output.WriteLine($"scale {scale} {logical}: expected {expected}, drawn {found}, {used:0.000} device pixels per source pixel");
					// the rectangle the layout asked for, mapped to the device and rounded (the right edge is clipped at the surface)
					Assert.Equal(expected.Left, found.Left);
					Assert.Equal(expected.Top, found.Top);
					Assert.Equal(Math.Min(expected.Right, r.Width), found.Right);
					Assert.Equal(Math.Min(expected.Bottom, r.Height), found.Bottom);
					// every pixel inside is the picture, every pixel outside the ground: no blended edge, no gap
					int other = 0;
					for (int y = 0; y < r.Height; y++)
					{
						for (int x = 0; x < r.Width; x++)
						{
							bool covered = x >= expected.Left && x < expected.Right && y >= expected.Top && y < expected.Bottom;
							SKColor c = r[x, y];
							if (!(covered ? Raster.Near(c, Cover, 1) : Raster.Near(c, ground, 0)))
							{
								other++;
							}
						}
					}
					Assert.Equal(0, other);
					Assert.Equal(Math.Min(expected.Right, r.Width) - expected.Left, r.CountInRow(expected.Top + 1, 0, r.Width, c => Raster.Near(c, Cover, 1)));
				}
			}
		}

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public void Two_pages_that_share_an_edge_leave_no_gap_at_any_scale(double scale)
		{
			SKColor ground = new SKColor(30, 30, 30, 255);
			SKColor second = new SKColor(40, 90, 200, 255);
			// the shared edge at 33.3, 50.5, 77.77 points: a different fraction of a device pixel at each scale
			foreach (double edge in new[] { 33.3, 50.5, 77.77 })
			{
				(SKBitmap bitmap, SKCanvas canvas) = Surface(120, 40, scale, ground);
				using (bitmap)
				using (canvas)
				using (SKImage a = Solid(50, 80, Cover))
				using (SKImage b = Solid(70, 80, second))
				{
					ReaderPainter.DrawPage(canvas, a, new Rect(0, 0, edge, 40));
					ReaderPainter.DrawPage(canvas, b, new Rect(edge, 0, 120 - edge, 40));
					Raster r = Read(bitmap);
					int y = r.Height / 2;
					Assert.Equal(r.Width, r.CountInRow(y, 0, r.Width, c => Raster.Near(c, Cover, 1) || Raster.Near(c, second, 1)));
					Assert.Equal(Scales.Device(edge, scale), r.CountInRow(y, 0, r.Width, c => Raster.Near(c, Cover, 1)));
				}
			}
		}

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public void A_page_with_the_pixels_of_its_rectangle_is_copied_whatever_the_scale(double scale)
		{
			int w = Scales.Device(40, scale), h = Scales.Device(30, scale);
			using (SKImage picture = Pictures.Lines(w, h))
			{
				(SKBitmap bitmap, SKCanvas canvas) = Surface(60, 50, scale, SKColors.Black);
				using (bitmap)
				using (canvas)
				{
					double used = ReaderPainter.DrawPage(canvas, picture, new Rect(10, 8, 40, 30));
					Assert.Equal(1.0, used, 6);
					Raster shown = Read(bitmap).Crop(new RectInt(Scales.Device(10, scale), Scales.Device(8, scale), Scales.Device(10, scale) + w, Scales.Device(8, scale) + h));
					Assert.Equal(0, shown.Compare(Raster.From(picture), 0).Max);
				}
			}
		}

		// ---- Hit testing and bounds of the draw operations are in device independent units

		[Fact]
		public void The_draw_operations_hit_test_and_report_their_bounds_in_logical_units_and_know_no_scale()
		{
			var cover = new CoverImage(Solid(8, 12, Cover));
			var dest = new Rect(10, 20, 100, 150);
			using (var op = new CoverDrawOperation(cover, dest))
			{
				cover.Release();
				Assert.Equal(dest, op.Bounds);
				Assert.True(op.HitTest(new Point(10, 20)));
				Assert.True(op.HitTest(new Point(109.9, 169.9)));
				Assert.False(op.HitTest(new Point(9.9, 20)));
				Assert.False(op.HitTest(new Point(110.1, 100)));
				Assert.False(op.HitTest(new Point(50, 170.1)));
			}
			var page = new CoverImage(Solid(8, 12, Cover));
			var area = new Rect(0, 0, 400, 300);
			using (var op = new ReaderDrawOperation(new[] { page }, new[] { new Rect(50.5, 10.25, 200, 280) }, area, 1, null))
			{
				page.Release();
				Assert.Equal(area, op.Bounds);
				Assert.True(op.HitTest(new Point(399.9, 299.9)));
				Assert.False(op.HitTest(new Point(400.1, 100)));
			}
		}

		[Fact]
		public void The_draw_operations_are_equal_when_they_draw_the_same_picture_in_the_same_logical_rectangle()
		{
			var image = new CoverImage(Solid(8, 12, Cover));
			var rect = new Rect(1, 2, 30, 40);
			using (var a = new CoverDrawOperation(image, rect))
			using (var b = new CoverDrawOperation(image, rect))
			using (var c = new CoverDrawOperation(image, new Rect(1, 2, 30, 40.5)))
			{
				image.Release();
				Assert.True(a.Equals(b));
				Assert.False(a.Equals(c));
			}
		}

		// ---- The System.Drawing stand-in has no DPI of its own

		[Fact]
		public void The_stand_in_for_System_Drawing_has_no_DPI_members_so_nothing_in_it_depends_on_the_display()
		{
			// Bitmap, Image and Graphics of the stand-in (cYo.Compat.Drawing) have no HorizontalResolution, VerticalResolution, SetResolution,
			// DpiX, DpiY or PageScale: a size in pixels is a size in pixels. The day one of them is added, this test fails and the author
			// adds the 1x/2x tests of the new path (full-port.md section 2.5, rule for custom drawing).
			string[] names = { "HorizontalResolution", "VerticalResolution", "SetResolution", "DpiX", "DpiY", "PageScale" };
			foreach (Type type in new[] { typeof(Bitmap), typeof(Image), typeof(Graphics) })
			{
				Assert.Equal(typeof(Bitmap).Assembly, type.Assembly);
				foreach (string name in names)
				{
					Assert.Null(type.GetMember(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).FirstOrDefault());
				}
			}
		}

		[Fact]
		public void Graphics_PageUnit_is_not_supported_yet_and_says_so()
		{
			using (var bitmap = new Bitmap(8, 8))
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				// the only DPI related setter the Core calls (SimpleTextRenderer); P4.9 implements it together with text, and then this test is replaced by 1x/2x tests
				Assert.Throws<NotSupportedException>(() => g.PageUnit = GraphicsUnit.Pixel);
			}
		}

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public void Drawing_a_bitmap_into_a_bitmap_works_in_pixels_at_every_size(double scale)
		{
			// what a scaled copy of a page looks like through the stand-in (the Core's thumbnail and page scaling): the size is in pixels,
			// the result at `scale` times the size, reduced again, is the picture
			int w = 48, h = 32;
			using (var source = new Bitmap(w, h))
			{
				using (Graphics g = Graphics.FromImage(source))
				{
					g.Clear(System.Drawing.Color.FromArgb(255, 200, 60, 40));
					g.FillRectangle(new SolidBrush(System.Drawing.Color.FromArgb(255, 40, 90, 200)), new System.Drawing.Rectangle(0, 0, w / 2, h));
				}
				int dw = (int)(w * scale), dh = (int)(h * scale);
				using (var target = new Bitmap(dw, dh))
				using (Graphics g = Graphics.FromImage(target))
				{
					g.InterpolationMode = InterpolationMode.NearestNeighbor;
					g.DrawImage(source, new System.Drawing.Rectangle(0, 0, dw, dh));
					Assert.Equal(dw, target.Width);
					Assert.Equal(dh, target.Height);
					Assert.Equal(System.Drawing.Color.FromArgb(255, 40, 90, 200).ToArgb(), target.GetPixel(2, dh / 2).ToArgb());
					Assert.Equal(System.Drawing.Color.FromArgb(255, 200, 60, 40).ToArgb(), target.GetPixel(dw - 3, dh / 2).ToArgb());
				}
			}
		}
	}
}
