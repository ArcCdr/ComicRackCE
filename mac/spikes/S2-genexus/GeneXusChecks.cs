// Spike S2 (M0.4): six checks of GeneXus.Drawing.Common 2.2.3 on macOS.
// Each test asserts the capability the Core needs. A failing test is a recorded result, not a bug to fix here.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using GeneXus.Drawing;
using GeneXus.Drawing.Imaging;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace S2Genexus
{
	public class GeneXusChecks
	{
		private readonly ITestOutputHelper log;

		public GeneXusChecks(ITestOutputHelper log)
		{
			this.log = log;
		}

		private static byte[] SkiaJpeg(int w, int h)
		{
			// Independent of GeneXus: left half red, right half blue, encoded by SkiaSharp.
			using var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
					bmp.SetPixel(x, y, x < w / 2 ? new SKColor(255, 0, 0) : new SKColor(0, 0, 255));
			using var img = SKImage.FromBitmap(bmp);
			using var data = img.Encode(SKEncodedImageFormat.Jpeg, 95);
			return data.ToArray();
		}

		private static Bitmap NoisyPhoto(int w, int h)
		{
			var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
			var rnd = new Random(1234);
			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
					bmp.SetPixel(x, y, Color.FromArgb(255, (x * 255 / w + rnd.Next(60)) % 256, (y * 255 / h + rnd.Next(60)) % 256, rnd.Next(256)));
			return bmp;
		}

		[Fact]
		public void Check1_DecodeJpegFromStream()
		{
			var jpeg = SkiaJpeg(64, 48);
			using var ms = new MemoryStream(jpeg);
			using var img = Image.FromStream(ms);
			log.WriteLine($"decoded {img.GetType().FullName} {img.Width}x{img.Height} PixelFormat={img.PixelFormat}");
			Assert.Equal(64, img.Width);
			Assert.Equal(48, img.Height);
			var bmp = Assert.IsAssignableFrom<Bitmap>(img);
			var l = bmp.GetPixel(8, 24);
			var r = bmp.GetPixel(56, 24);
			log.WriteLine($"left R,G,B = {l.R},{l.G},{l.B}; right R,G,B = {r.R},{r.G},{r.B}");
			Assert.True(l.R > 200 && l.G < 60 && l.B < 60, "left half should be red");
			Assert.True(r.B > 200 && r.R < 60 && r.G < 60, "right half should be blue");
		}

		[Fact]
		public void Check2_LockBits_Format32bppArgb_Bgra()
		{
			using var bmp = new Bitmap(8, 4, PixelFormat.Format32bppArgb);
			bmp.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30)); // A,R,G,B
			object bd = bmp.LockBits(new Rectangle(0, 0, 8, 4), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
			Assert.NotNull(bd);
			var t = bd.GetType();
			log.WriteLine($"LockBits returned {t.FullName} (public={t.IsPublic})");
			IntPtr scan0 = (IntPtr)t.GetProperty("Scan0").GetValue(bd);
			int stride = (int)t.GetProperty("Stride").GetValue(bd);
			Assert.NotEqual(IntPtr.Zero, scan0);
			Assert.True(stride >= 8 * 4, "stride covers a row of 4-byte pixels");
			var px = new byte[4];
			Marshal.Copy(scan0, px, 0, 4);
			log.WriteLine($"stride={stride} first pixel bytes = {px[0]},{px[1]},{px[2]},{px[3]}");
			Assert.Equal(new byte[] { 30, 20, 10, 255 }, px); // BGRA
			Marshal.WriteByte(scan0, 1, 99); // G of pixel 0
			bmp.UnlockBits(bd);
			Assert.Equal(99, bmp.GetPixel(0, 0).G);
		}

		[Fact]
		public void Check3_DrawImage_Downscale_HighQualityBicubic()
		{
			using var src = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
			for (int y = 0; y < 256; y++)
				for (int x = 0; x < 256; x++)
					src.SetPixel(x, y, ((x + y) & 1) == 0 ? Color.White : Color.Black); // 1-pixel checkerboard
			using var dst = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
			using (var g = Graphics.FromImage(dst))
			{
				g.InterpolationMode = GeneXus.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
				Assert.Equal(GeneXus.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic, g.InterpolationMode);
				g.DrawImage(src, new Rectangle(0, 0, 64, 64), new Rectangle(0, 0, 256, 256), GraphicsUnit.Pixel);
			}
			int worst = 0;
			for (int y = 8; y < 56; y += 3)
				for (int x = 8; x < 56; x += 3)
					worst = Math.Max(worst, Math.Abs(dst.GetPixel(x, y).R - 127));
			log.WriteLine($"max |R-127| over the inner area after a 4:1 reduction of a 1-px checkerboard: {worst}");

			// Does InterpolationMode change anything? Render the same reduction with NearestNeighbor and compare.
			using var nn = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
			using (var g2 = Graphics.FromImage(nn))
			{
				g2.InterpolationMode = GeneXus.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
				g2.DrawImage(src, new Rectangle(0, 0, 64, 64), new Rectangle(0, 0, 256, 256), GraphicsUnit.Pixel);
			}
			bool same = true;
			for (int y = 0; y < 64 && same; y++)
				for (int x = 0; x < 64 && same; x++)
					same = dst.GetPixel(x, y).ToArgb() == nn.GetPixel(x, y).ToArgb();
			log.WriteLine("HighQualityBicubic output identical to NearestNeighbor output: " + same);
			Assert.True(worst <= 40, "a proper reduction averages the checkerboard to mid grey; nearest-neighbour keeps 0/255");
		}

		[Fact]
		public void Check4_SaveJpeg_WithQuality()
		{
			using var photo = NoisyPhoto(192, 192);
			byte[] Save(int quality)
			{
				using var ms = new MemoryStream();
				photo.Save(ms, ImageFormat.Jpeg, quality);
				return ms.ToArray();
			}
			var lo = Save(20);
			var hi = Save(95);
			log.WriteLine($"quality 20 -> {lo.Length} bytes; quality 95 -> {hi.Length} bytes");
			Assert.True(lo.Length > 2 && lo[0] == 0xFF && lo[1] == 0xD8, "JPEG magic");
			Assert.True(hi.Length > 2 && hi[0] == 0xFF && hi[1] == 0xD8, "JPEG magic");
			Assert.True(lo.Length < hi.Length * 0.7, "lower quality must give a clearly smaller file");

			// The Core uses Save(Stream, ImageCodecInfo, EncoderParameters) with Encoder.Quality; check whether those types exist at all.
			var asm = typeof(Bitmap).Assembly;
			var missing = new[] { "Encoder", "EncoderParameter", "EncoderParameters", "ImageCodecInfo" }
				.Where(n => asm.GetType("GeneXus.Drawing.Imaging." + n) == null).ToArray();
			log.WriteLine("System.Drawing encoder API types absent in GeneXus: " + (missing.Length == 0 ? "(none)" : string.Join(", ", missing)));
			Assert.Empty(missing);
		}

		[Fact]
		public void Check5_ImageAttributes_ColorMatrix_Grayscale()
		{
			var asm = typeof(Bitmap).Assembly;
			var found = asm.GetExportedTypes().Where(t => t.Name.Contains("ColorMatrix") || t.Name.Contains("ImageAttributes") || t.Name.Contains("ColorFilter")).Select(t => t.FullName).ToArray();
			log.WriteLine("ColorMatrix/ImageAttributes types: " + (found.Length == 0 ? "(none)" : string.Join(", ", found)));

			// Probe the 'object imageAttrs' parameter that the DrawImage overload keeps for source compatibility.
			using var src = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
			using var dst = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
			using var g = Graphics.FromImage(dst);
			try
			{
				g.DrawImage(src, new Rectangle(0, 0, 4, 4), 0, 0, 4, 4, GraphicsUnit.Pixel, new object(), null);
				log.WriteLine("DrawImage(..., object imageAttrs, ...) accepted a plain object without throwing (attributes are ignored or unused)");
			}
			catch (Exception ex)
			{
				log.WriteLine("DrawImage(..., object imageAttrs, ...) threw " + ex.GetType().Name + ": " + ex.Message);
			}
			Assert.True(found.Length >= 2, "GeneXus has no ImageAttributes/ColorMatrix, so the grayscale path cannot be exercised");
		}

		[Fact]
		public void Check6_UnderlyingSKBitmap_WithoutCopy()
		{
			// The public path GeneXus offers is an explicit conversion operator: (SKBitmap)bitmap.
			bool Exposes(Type t) => t == typeof(SKBitmap) || t == typeof(SKImage) || t == typeof(SKSurface) || t == typeof(SKCanvas) || t == typeof(SKPixmap);
			var pub = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
			var hits = new[] { typeof(Image), typeof(Bitmap), typeof(Graphics) }
				.SelectMany(t => t.GetMembers(pub).Select(m => (t, m)))
				.Where(x => x.m is MethodInfo mi && (Exposes(mi.ReturnType) || mi.GetParameters().Any(p => Exposes(p.ParameterType))))
				.Select(x => x.t.Name + "." + x.m.Name + " -> " + ((MethodInfo)x.m).ReturnType.Name).ToArray();
			log.WriteLine("public members of Image/Bitmap/Graphics that expose SkiaSharp types: " + (hits.Length == 0 ? "(none)" : string.Join(", ", hits)));
			Assert.NotEmpty(hits);

			using var bmp = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
			SKBitmap sk1 = (SKBitmap)bmp;
			SKBitmap sk2 = (SKBitmap)bmp;
			log.WriteLine($"same SKBitmap instance on repeated conversion: {ReferenceEquals(sk1, sk2)}; same pixel pointer: {sk1.GetPixels() == sk2.GetPixels()}; color type {sk1.ColorType}, alpha type {sk1.AlphaType}, row bytes {sk1.RowBytes}");
			sk1.SetPixel(1, 1, new SKColor(1, 2, 3));
			var seen = bmp.GetPixel(1, 1);
			log.WriteLine($"a write through the SKBitmap is visible via GetPixel: {seen.R},{seen.G},{seen.B}");
			Assert.Equal(new[] { 1, 2, 3 }, new[] { (int)seen.R, seen.G, seen.B });
			Assert.Equal(sk1.GetPixels(), sk2.GetPixels());
		}
	}
}
