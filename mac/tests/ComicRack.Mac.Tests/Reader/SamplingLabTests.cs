using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ComicRack.Mac.Reader;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>
	/// Makes the contact sheets that were used to pick the filter of <see cref="ReaderSampling"/> (the aliasing finding of M1.2c) and
	/// prints the numbers. It writes PNG files only when CRMAC_FRAMES_DIR is set; otherwise it only checks the numbers.
	/// A synthetic 2000 x 3000 page with the things that alias in comics: small text, hatching, halftone dots and 1 pixel outlines.
	/// </summary>
	public class SamplingLabTests
	{
		private readonly ITestOutputHelper output;

		public SamplingLabTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		private static SKBitmap MakePage()
		{
			var page = new SKBitmap(new SKImageInfo(2000, 3000, SKColorType.Bgra8888, SKAlphaType.Opaque));
			using (var canvas = new SKCanvas(page))
			using (var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black })
			using (var font = new SKFont { Size = 22 })
			{
				canvas.Clear(new SKColor(250, 247, 240));
				// text of several sizes (the first block of the sheet)
				float y = 60;
				foreach (float size in new[] { 36f, 28f, 22f, 18f, 15f, 12f, 10f })
				{
					font.Size = size;
					canvas.DrawText("Le bal des vautours 0123456789 rhythm", 40, y, SKTextAlign.Left, font, paint);
					y += size * 1.5f;
				}
				// hatching: 1 px lines every 3 px, vertical and diagonal
				paint.IsAntialias = false;
				paint.StrokeWidth = 1;
				for (int x = 40; x < 440; x += 3)
				{
					canvas.DrawLine(x, 400, x, 600, paint);
				}
				for (int i = 0; i < 400; i += 3)
				{
					canvas.DrawLine(500 + i, 400, 500 + i - 200, 600, paint);
				}
				// halftone dots, period 5, radius growing to the right
				paint.IsAntialias = true;
				for (int gy = 0; gy < 40; gy++)
				{
					for (int gx = 0; gx < 80; gx++)
					{
						paint.Color = SKColors.Black;
						canvas.DrawCircle(40 + gx * 5, 700 + gy * 5, 0.6f + gx * 0.03f, paint);
					}
				}
				// 1 px outlines
				paint.IsAntialias = false;
				paint.Style = SKPaintStyle.Stroke;
				paint.StrokeWidth = 1;
				for (int i = 0; i < 10; i++)
				{
					canvas.DrawRect(1000 + i * 12, 400 + i * 8, 300 - i * 24, 200 - i * 16, paint);
				}
				paint.IsAntialias = true;
				for (int i = 0; i < 10; i++)
				{
					canvas.DrawCircle(1500, 500, 20 + i * 8, paint);
				}
			}
			return page;
		}

		private static SKRect Region = new SKRect(30, 20, 530, 320); // the text and part of the hatching... see Crops

		private static readonly (string name, SKSamplingOptions options)[] Filters =
		{
			("linear", new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)),
			("mitchell", new SKSamplingOptions(SKCubicResampler.Mitchell)),
			("catmull-rom", new SKSamplingOptions(SKCubicResampler.CatmullRom)),
			("mipmap", new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)),
		};

		/// <summary>The standard deviation of the luma of a region of a bitmap: for a flat grey that the source pattern should average to, it measures how much pattern (alias) is left.</summary>
		private static double Deviation(SKBitmap bitmap, SKRectI region)
		{
			double sum = 0, sum2 = 0;
			int n = 0;
			for (int y = region.Top; y < region.Bottom; y++)
			{
				for (int x = region.Left; x < region.Right; x++)
				{
					SKColor c = bitmap.GetPixel(x, y);
					double luma = 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;
					sum += luma;
					sum2 += luma * luma;
					n++;
				}
			}
			double mean = sum / n;
			return Math.Sqrt(Math.Max(0, sum2 / n - mean * mean));
		}

		private static SKBitmap Reduce(SKImage page, double scale, SKSamplingOptions options)
		{
			int w = (int)Math.Round(page.Width * scale), h = (int)Math.Round(page.Height * scale);
			var target = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
			using (var canvas = new SKCanvas(target))
			{
				canvas.DrawImage(page, new SKRect(0, 0, w, h), options);
			}
			return target;
		}

		[Fact]
		public void Contact_sheets_of_the_filters_at_the_scales_a_reader_meets()
		{
			string dir = Environment.GetEnvironmentVariable("CRMAC_FRAMES_DIR");
			double[] scales = { 0.9, 0.7, 0.5, 0.35, 0.2 };
			using (SKBitmap page = MakePage())
			using (SKImage image = SKImage.FromBitmap(page))
			{
				if (dir != null)
				{
					Directory.CreateDirectory(dir);
				}
				// Crops of the page (source coordinates): text, hatching, halftone. One sheet per crop and scale; the filters are the rows,
				// each shown at 1:1 device pixels and magnified 3 times without smoothing.
				var crops = new[] { ("text", new SKRect(30, 30, 430, 150)), ("hatch", new SKRect(30, 390, 430, 540)), ("halftone", new SKRect(30, 690, 430, 810)) };
				const int zoom = 3;
				foreach ((string cropName, SKRect crop) in crops)
				{
					var lines = new List<string>();
					foreach (double scale in scales)
					{
						string chosen = ReaderSampling.Describe(scale);
						int cellW = (int)(crop.Width * scale * zoom) + 8, cellH = (int)(crop.Height * scale * zoom) + 8;
						using (var sheet = new SKBitmap(new SKImageInfo(cellW, cellH * Filters.Length, SKColorType.Bgra8888, SKAlphaType.Opaque)))
						using (var canvas = new SKCanvas(sheet))
						using (var label = new SKPaint { Color = new SKColor(255, 60, 60), IsAntialias = true })
						using (var font = new SKFont { Size = 13 })
						{
							canvas.Clear(new SKColor(60, 60, 60));
							var devs = new List<string>();
							for (int row = 0; row < Filters.Length; row++)
							{
								using (SKBitmap reduced = Reduce(image, scale, Filters[row].options))
								{
									var source = new SKRectI((int)(crop.Left * scale), (int)(crop.Top * scale), (int)(crop.Right * scale), (int)(crop.Bottom * scale));
									using (var cropped = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Opaque)))
									{
										reduced.ExtractSubset(cropped, source);
										using (SKImage small = SKImage.FromBitmap(cropped))
										{
											canvas.DrawImage(small, new SKRect(4, row * cellH + 4, 4 + source.Width * zoom, row * cellH + 4 + source.Height * zoom), new SKSamplingOptions(SKFilterMode.Nearest));
										}
										if (cropName == "hatch")
										{
											devs.Add(Filters[row].name + " " + Deviation(cropped, new SKRectI(2, 2, Math.Max(3, source.Width - 2), Math.Max(3, source.Height - 2))).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
										}
									}
								}
								canvas.DrawText(Filters[row].name + (Filters[row].name == chosen ? " (chosen)" : string.Empty) + " scale " + scale, 8, row * cellH + 16, SKTextAlign.Left, font, label);
							}
							if (cropName == "hatch")
							{
								lines.Add("scale " + scale + " (chosen: " + chosen + "): std dev of the hatching: " + string.Join(", ", devs));
							}
							if (dir != null)
							{
								using (SKImage sheetImage = SKImage.FromBitmap(sheet))
								using (SKData data = sheetImage.Encode(SKEncodedImageFormat.Png, 100))
								using (FileStream f = File.Create(Path.Combine(dir, "sampling-" + cropName + "-" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png")))
								{
									data.SaveTo(f);
								}
							}
						}
					}
					foreach (string line in lines)
					{
						output.WriteLine(line);
					}
				}
			}
		}
	}
}
