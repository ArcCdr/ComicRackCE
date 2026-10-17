using System.IO;
using SkiaSharp;

namespace ComicRack.Cli.Fixtures
{
	/// <summary>Writes a small multi-page vector PDF with Skia (A5 portrait, 420 x 595 points).</summary>
	internal static class PdfWriter
	{
		public static void Write(string file, int pages)
		{
			using (var stream = File.Create(file))
			using (var doc = SKDocument.CreatePdf(stream))
			{
				for (int i = 1; i <= pages; i++)
				{
					using (var canvas = doc.BeginPage(420, 595))
					using (var paint = new SKPaint { IsAntialias = true })
					{
						canvas.Clear(SKColors.White);
						paint.Color = SKColor.FromHsv(i * 70 % 360, 45, 90);
						canvas.DrawRect(20, 20, 380, 555, paint);
						paint.Color = SKColors.White;
						canvas.DrawRect(40, 40, 160, 240, paint);
						canvas.DrawRect(220, 40, 160, 240, paint);
						canvas.DrawRect(40, 300, 340, 255, paint);
						paint.Color = SKColors.Black;
						using (var font = new SKFont { Size = 48 })
						{
							canvas.DrawText("PDF page " + i + "/" + pages, 50, 420, SKTextAlign.Left, font, paint);
						}
					}
					doc.EndPage();
				}
				doc.Close();
			}
		}
	}
}
