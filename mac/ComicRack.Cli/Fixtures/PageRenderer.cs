using System;
using SkiaSharp;

namespace ComicRack.Cli.Fixtures
{
	internal enum PageFormat
	{
		Jpeg,
		Png,
		WebP,
	}

	/// <summary>What one page should look like: size, encoding and the text that identifies it.</summary>
	internal sealed class PageSpec
	{
		public int Width;
		public int Height;
		public PageFormat Format;
		public int Quality;
		public int Seed;
		public string Series;
		public string Number;
		public string Title;
		public int Index;
		public int Total;

		public string Extension => Format == PageFormat.Jpeg ? ".jpg" : Format == PageFormat.Png ? ".png" : ".webp";

		public long Pixels => (long)Width * Height;
	}

	/// <summary>Skia drawing of a stand-in page: a gradient, a few panels, and the series, number and page index as text.</summary>
	internal static class PageRenderer
	{
		public static byte[] Render(PageSpec spec)
		{
			var info = new SKImageInfo(spec.Width, spec.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
			using (var surface = SKSurface.Create(info))
			{
				Draw(surface.Canvas, spec);
				using (var image = surface.Snapshot())
				{
					SKEncodedImageFormat format = spec.Format == PageFormat.Jpeg ? SKEncodedImageFormat.Jpeg : spec.Format == PageFormat.Png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Webp;
					using (var data = image.Encode(format, spec.Quality))
					{
						return data.ToArray();
					}
				}
			}
		}

		private static void Draw(SKCanvas canvas, PageSpec s)
		{
			var rnd = new Random(s.Seed);
			float hue = (s.Seed & 0x7fffffff) % 360;
			float w = s.Width, h = s.Height;
			SKColor top = SKColor.FromHsv(hue, 35, 95);
			SKColor bottom = SKColor.FromHsv((hue + 40) % 360, 55, 70);
			using (var paint = new SKPaint { IsAntialias = true })
			{
				paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h), new[] { top, bottom }, SKShaderTileMode.Clamp);
				canvas.DrawRect(0, 0, w, h, paint);
				paint.Shader = null;

				// Comic-like panels: 3 to 6 rounded rectangles.
				int panels = 3 + rnd.Next(4);
				float margin = w * 0.04f;
				for (int i = 0; i < panels; i++)
				{
					float pw = w * (0.25f + 0.4f * (float)rnd.NextDouble());
					float ph = h * (0.12f + 0.2f * (float)rnd.NextDouble());
					float px = margin + (w - 2 * margin - pw) * (float)rnd.NextDouble();
					float py = margin + (h - 2 * margin - ph) * (float)rnd.NextDouble();
					paint.Style = SKPaintStyle.Fill;
					paint.Color = SKColor.FromHsv((hue + 20 * i) % 360, 20, 100).WithAlpha(200);
					canvas.DrawRoundRect(px, py, pw, ph, w * 0.01f, w * 0.01f, paint);
					paint.Style = SKPaintStyle.Stroke;
					paint.StrokeWidth = Math.Max(2, w * 0.004f);
					paint.Color = SKColors.Black;
					canvas.DrawRoundRect(px, py, pw, ph, w * 0.01f, w * 0.01f, paint);
				}

				// Text: big page index in the middle, series and number on top, title and total at the bottom.
				paint.Style = SKPaintStyle.Fill;
				paint.Color = SKColors.Black;
				using (var typeface = SKTypeface.Default)
				{
					using (var big = new SKFont(typeface, Math.Min(w, h) * 0.30f))
					{
						string text = (s.Index + 1).ToString();
						float tw = big.MeasureText(text);
						paint.Color = SKColors.White.WithAlpha(230);
						canvas.DrawRect((w - tw) / 2 - 20, h / 2 - big.Size, tw + 40, big.Size * 1.3f, paint);
						paint.Color = SKColors.Black;
						canvas.DrawText(text, (w - tw) / 2, h / 2, big, paint);
					}
					using (var small = new SKFont(typeface, Math.Max(12, w * 0.045f)))
					{
						float y = small.Size * 1.4f;
						canvas.DrawText(Fit(s.Series + " #" + s.Number, small, w - 2 * margin), margin, y, small, paint);
						canvas.DrawText(Fit(s.Title ?? "", small, w - 2 * margin), margin, h - small.Size * 1.8f, small, paint);
						canvas.DrawText("page " + (s.Index + 1) + " / " + s.Total, margin, h - small.Size * 0.5f, small, paint);
					}
				}
			}
		}

		private static string Fit(string text, SKFont font, float maxWidth)
		{
			text = text ?? "";
			while (text.Length > 1 && font.MeasureText(text) > maxWidth)
			{
				text = text.Substring(0, text.Length - 1);
			}
			return text;
		}
	}
}
