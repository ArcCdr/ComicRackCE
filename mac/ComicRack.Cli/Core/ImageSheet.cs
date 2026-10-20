using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using ComicRack.Cli.Fixtures;
using SkiaSharp;

namespace ComicRack.Cli.Core
{
	/// <summary>
	/// <c>crcli image sheet --out sheet.png --cols N [--cell-width W] [--gap G] [--background #RRGGBB] input...</c> (P5.T3). Puts images into
	/// rows of N cells, each scaled to the cell width, with a caption under it. An input ending in <c>.json</c> is a reference card
	/// (<see cref="CardRenderer"/>) that is drawn into an image first; <c>path::caption</c> overrides the caption. Used by
	/// <c>mac/tools/chrome-sheet.sh</c> for the contact sheets of chrome-guidelines section 9: reference, light shot and dark shot of a view side
	/// by side.
	/// </summary>
	public static class ImageSheet
	{
		private const string Usage = "image sheet --out <png> --cols N [--cell-width W] [--gap G] [--background #RRGGBB] <image-or-card.json>[::caption]...";
		private const int CaptionHeight = 26;
		private const int Margin = 12;
		private const int MaxPixels = 120_000_000;

		internal static int Run(Options o, TextWriter output)
		{
			string outFile = o.Get("out") ?? throw new UsageException("Usage: crcli " + Usage);
			int cols = o.Int("cols", 0);
			if (cols < 1 || cols > 12)
			{
				throw new UsageException("--cols must be a number from 1 to 12.");
			}
			int cellWidth = o.Int("cell-width", 480);
			int gap = o.Int("gap", 12);
			if (cellWidth < 64 || cellWidth > 4000 || gap < 0 || gap > 200)
			{
				throw new UsageException("--cell-width must be 64 to 4000 and --gap 0 to 200.");
			}
			SKColor background = CardRenderer.ParseColor(o.Get("background") ?? "#FFFFFF");
			if (o.PositionalArguments.Count == 0)
			{
				throw new UsageException("Usage: crcli " + Usage);
			}

			var cells = new List<(SKBitmap Bitmap, string Caption)>();
			try
			{
				foreach (string arg in o.PositionalArguments)
				{
					string file = arg;
					string caption = null;
					int sep = arg.LastIndexOf("::", StringComparison.Ordinal);
					if (sep > 0)
					{
						file = arg.Substring(0, sep);
						caption = arg.Substring(sep + 2);
					}
					if (!File.Exists(file))
					{
						throw new UsageException("Input not found: " + file);
					}
					if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
					{
						SKBitmap card = CardRenderer.Render(file, out string title);
						cells.Add((card, caption ?? title ?? Path.GetFileNameWithoutExtension(file)));
					}
					else
					{
						SKBitmap bitmap = SKBitmap.Decode(file) ?? throw new UsageException("Cannot decode the image: " + file);
						cells.Add((bitmap, caption ?? Path.GetFileNameWithoutExtension(file)));
					}
				}

				var scaledHeights = cells.Select(c => Math.Max(1, (int)Math.Round((double)c.Bitmap.Height * cellWidth / c.Bitmap.Width))).ToList();
				int rows = (cells.Count + cols - 1) / cols;
				var rowHeights = new int[rows];
				for (int i = 0; i < cells.Count; i++)
				{
					rowHeights[i / cols] = Math.Max(rowHeights[i / cols], scaledHeights[i] + CaptionHeight);
				}
				long width = 2L * Margin + (long)cols * cellWidth + (long)(cols - 1) * gap;
				long height = 2L * Margin + rowHeights.Sum() + (long)(rows - 1) * gap;
				if (width * height > MaxPixels)
				{
					throw new UsageException("The sheet would be " + width + " x " + height + " pixels; use a smaller --cell-width or fewer inputs.");
				}

				using (var surface = SKSurface.Create(new SKImageInfo((int)width, (int)height, SKColorType.Rgba8888, SKAlphaType.Premul)))
				{
					SKCanvas canvas = surface.Canvas;
					canvas.Clear(background);
					bool dark = CardRenderer.Luminance(background) < 0.5;
					using (var textPaint = new SKPaint { Color = dark ? new SKColor(0xE0, 0xE0, 0xE0) : new SKColor(0x20, 0x20, 0x20), IsAntialias = true })
					using (var framePaint = new SKPaint { Color = dark ? new SKColor(0x60, 0x60, 0x60) : new SKColor(0xB0, 0xB0, 0xB0), Style = SKPaintStyle.Stroke, StrokeWidth = 1 })
					using (var font = new SKFont(CardRenderer.Typeface(bold: false), 12))
					{
						int y = Margin;
						for (int r = 0; r < rows; r++)
						{
							for (int c = 0; c < cols; c++)
							{
								int i = r * cols + c;
								if (i >= cells.Count)
								{
									break;
								}
								int x = Margin + c * (cellWidth + gap);
								var dest = new SKRect(x, y, x + cellWidth, y + scaledHeights[i]);
								canvas.DrawImage(SKImage.FromBitmap(cells[i].Bitmap), dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
								canvas.DrawRect(new SKRect(dest.Left + 0.5f, dest.Top + 0.5f, dest.Right - 0.5f, dest.Bottom - 0.5f), framePaint);
								canvas.DrawText(CardRenderer.Fit(cells[i].Caption, font, cellWidth), x, y + scaledHeights[i] + 17, font, textPaint);
							}
							y += rowHeights[r] + gap;
						}
					}
					using (SKImage image = surface.Snapshot())
					using (SKData data = image.Encode(SKEncodedImageFormat.Png, 100))
					{
						string dir = Path.GetDirectoryName(Path.GetFullPath(outFile));
						if (!string.IsNullOrEmpty(dir))
						{
							Directory.CreateDirectory(dir);
						}
						using (FileStream stream = File.Create(outFile))
						{
							data.SaveTo(stream);
						}
					}
				}
				output.WriteLine("sheet: " + outFile);
				output.WriteLine("  size: " + width + "x" + height + ", " + cells.Count + " cells in " + rows + " rows of " + cols);
			}
			finally
			{
				foreach (var c in cells)
				{
					c.Bitmap.Dispose();
				}
			}
			return 0;
		}
	}

	/// <summary>
	/// Draws a reference card from a JSON file: <c>{ "title": "MainForm", "subtitle": "...", "width": 480, "background": "#FFFFFF",
	/// "foreground": "#202020", "lines": ["layout outline line", "  indented line"], "swatches": [{"name": "Cr.Window", "color": "#FFFFFF"}],
	/// "icons": [{"name": "Open", "file": "/path/Open.png"}] }</c>. Every field is optional. Long lines wrap; leading spaces are kept as indentation.
	/// </summary>
	internal static class CardRenderer
	{
		private const int Pad = 12;
		private const float TitleSize = 15;
		private const float TextSize = 11;
		private const float SmallSize = 9;
		private const float LineHeight = 15;

		public static SKBitmap Render(string file, out string title)
		{
			JsonDocument doc;
			try
			{
				doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
			}
			catch (JsonException ex)
			{
				throw new UsageException("Card " + file + " is not valid JSON: " + ex.Message);
			}
			using (doc)
			{
				JsonElement root = doc.RootElement;
				if (root.ValueKind != JsonValueKind.Object)
				{
					throw new UsageException("Card " + file + " must be a JSON object.");
				}
				title = Str(root, "title");
				int width = root.TryGetProperty("width", out JsonElement w) && w.TryGetInt32(out int wi) ? Math.Clamp(wi, 200, 2000) : 480;
				SKColor back = ParseColor(Str(root, "background") ?? "#FFFFFF");
				SKColor fore = ParseColor(Str(root, "foreground") ?? (Luminance(back) < 0.5 ? "#E8E8E8" : "#202020"));

				using (var normal = new SKFont(Typeface(false), TextSize))
				using (var bold = new SKFont(Typeface(true), TitleSize))
				using (var small = new SKFont(Typeface(false), SmallSize))
				using (var mono = new SKFont(MonoTypeface(), TextSize - 1))
				{
					int inner = width - 2 * Pad;
					var ops = new List<Action<SKCanvas>>();
					float y = Pad;
					SKColor foreCopy = fore;

					void Text(string text, float x, float baseline, SKFont font, SKColor colour)
					{
						ops.Add(canvas =>
						{
							using (var paint = new SKPaint { Color = colour, IsAntialias = true })
							{
								canvas.DrawText(text, x, baseline, font, paint);
							}
						});
					}

					if (!string.IsNullOrEmpty(title))
					{
						y += TitleSize;
						Text(Fit(title, bold, inner), Pad, y, bold, foreCopy);
						y += 6;
					}
					string subtitle = Str(root, "subtitle");
					if (!string.IsNullOrEmpty(subtitle))
					{
						y += LineHeight - 2;
						foreach (string part in Wrap(subtitle, small, inner))
						{
							Text(part, Pad, y, small, foreCopy.WithAlpha(170));
							y += SmallSize + 3;
						}
						y -= 3;
					}
					y += 6;

					if (root.TryGetProperty("lines", out JsonElement lines) && lines.ValueKind == JsonValueKind.Array)
					{
						foreach (JsonElement line in lines.EnumerateArray())
						{
							string text = (line.GetString() ?? string.Empty).Replace("\t", "    ");
							if (text.Length == 0)
							{
								y += LineHeight / 2;
								continue;
							}
							int indent = text.Length - text.TrimStart(' ').Length;
							float indentWidth = indent * mono.MeasureText(" ");
							foreach (string part in Wrap(text.TrimStart(' '), mono, inner - indentWidth))
							{
								y += LineHeight;
								Text(part, Pad + indentWidth, y, mono, foreCopy);
							}
						}
						y += 8;
					}

					if (root.TryGetProperty("swatches", out JsonElement swatches) && swatches.ValueKind == JsonValueKind.Array)
					{
						const int cw = 104, ch = 44;
						int perRow = Math.Max(1, (inner + 4) / (cw + 4));
						int n = 0;
						foreach (JsonElement s in swatches.EnumerateArray())
						{
							string name = Str(s, "name") ?? string.Empty;
							SKColor colour = ParseColor(Str(s, "color") ?? "#FF00FF");
							float x = Pad + (n % perRow) * (cw + 4);
							float top = y + (n / perRow) * (ch + 18);
							ops.Add(canvas =>
							{
								using (var fill = new SKPaint { Color = colour })
								using (var frame = new SKPaint { Color = fore.WithAlpha(110), Style = SKPaintStyle.Stroke, StrokeWidth = 1 })
								{
									canvas.DrawRect(new SKRect(x, top, x + cw, top + ch), fill);
									canvas.DrawRect(new SKRect(x + 0.5f, top + 0.5f, x + cw - 0.5f, top + ch - 0.5f), frame);
								}
							});
							Text(Fit(name, small, cw), x, top + ch + 11, small, fore);
							n++;
						}
						y += ((n + perRow - 1) / perRow) * (ch + 18) + 6;
					}

					if (root.TryGetProperty("icons", out JsonElement icons) && icons.ValueKind == JsonValueKind.Array)
					{
						const int cell = 76;
						int perRow = Math.Max(1, inner / cell);
						int n = 0;
						foreach (JsonElement ic in icons.EnumerateArray())
						{
							string name = Str(ic, "name") ?? string.Empty;
							string path = Str(ic, "file");
							float x = Pad + (n % perRow) * cell;
							float top = y + (n / perRow) * 54;
							if (path != null && File.Exists(path))
							{
								SKBitmap bmp = SKBitmap.Decode(path);
								if (bmp != null)
								{
									ops.Add(canvas =>
									{
										canvas.DrawImage(SKImage.FromBitmap(bmp), new SKRect(x, top, x + Math.Min(32, bmp.Width * 2), top + Math.Min(32, bmp.Height * 2)),
											new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
									});
								}
							}
							Text(Fit(name, small, cell - 4), x, top + 44, small, fore);
							n++;
						}
						y += ((n + perRow - 1) / perRow) * 54 + 4;
					}

					int height = Math.Max(40, (int)Math.Ceiling(y + Pad));
					var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
					using (var canvas = new SKCanvas(bitmap))
					{
						canvas.Clear(back);
						foreach (var op in ops)
						{
							op(canvas);
						}
					}
					return bitmap;
				}
			}
		}

		private static string Str(JsonElement e, string name) =>
			e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

		public static SKTypeface Typeface(bool bold)
		{
			return SKTypeface.FromFamilyName("Helvetica Neue", bold ? SKFontStyle.Bold : SKFontStyle.Normal) ?? SKTypeface.Default;
		}

		private static SKTypeface MonoTypeface()
		{
			return SKTypeface.FromFamilyName("Menlo") ?? SKTypeface.FromFamilyName("Courier New") ?? SKTypeface.Default;
		}

		public static SKColor ParseColor(string text)
		{
			if (!SKColor.TryParse(text, out SKColor colour))
			{
				throw new UsageException("Not a colour (#RRGGBB or #AARRGGBB): " + text);
			}
			return colour;
		}

		public static double Luminance(SKColor c) => (0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue) / 255.0;

		/// <summary>The text cut with an ellipsis so that it fits in <paramref name="width"/> pixels.</summary>
		public static string Fit(string text, SKFont font, float width)
		{
			if (string.IsNullOrEmpty(text) || font.MeasureText(text) <= width)
			{
				return text ?? string.Empty;
			}
			string t = text;
			while (t.Length > 1 && font.MeasureText(t + "...") > width)
			{
				t = t.Substring(0, t.Length - 1);
			}
			return t + "...";
		}

		private static IEnumerable<string> Wrap(string text, SKFont font, float width)
		{
			var line = new System.Text.StringBuilder();
			foreach (string word in text.Split(' '))
			{
				string candidate = line.Length == 0 ? word : line + " " + word;
				if (line.Length > 0 && font.MeasureText(candidate) > width)
				{
					yield return line.ToString();
					line.Clear();
					candidate = word;
				}
				line.Clear();
				line.Append(candidate);
			}
			if (line.Length > 0)
			{
				yield return line.ToString();
			}
		}
	}
}
