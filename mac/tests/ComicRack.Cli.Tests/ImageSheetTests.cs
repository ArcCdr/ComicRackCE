using System;
using System.IO;
using SkiaSharp;
using Xunit;

namespace ComicRack.Cli.Tests
{
	/// <summary>`crcli image sheet` (P5.T3): captioned contact sheets and reference cards, run in process on small images written by the test.</summary>
	public class ImageSheetTests : IDisposable
	{
		private readonly string temp = Path.Combine(Path.GetTempPath(), "crcli-sheet-" + Guid.NewGuid().ToString("N"));

		public ImageSheetTests()
		{
			Directory.CreateDirectory(temp);
		}

		public void Dispose()
		{
			try
			{
				Directory.Delete(temp, recursive: true);
			}
			catch (IOException)
			{
			}
		}

		private string Solid(string name, int width, int height, SKColor colour)
		{
			string path = Path.Combine(temp, name);
			using (var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul)))
			{
				bitmap.Erase(colour);
				using (var image = SKImage.FromBitmap(bitmap))
				using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
				using (var stream = File.Create(path))
				{
					data.SaveTo(stream);
				}
			}
			return path;
		}

		private string Json(string name, string text)
		{
			string path = Path.Combine(temp, name);
			File.WriteAllText(path, text);
			return path;
		}

		private static (int code, string output, string error) Crcli(params string[] args)
		{
			var o = new StringWriter();
			var e = new StringWriter();
			int code = Program.Run(args, o, e);
			return (code, o.ToString(), e.ToString());
		}

		private static SKBitmap Load(string file) => SKBitmap.Decode(file);

		[Fact]
		public void Cells_are_placed_in_rows_and_scaled_to_the_cell_width()
		{
			string red = Solid("red.png", 100, 50, new SKColor(255, 0, 0));
			string blue = Solid("blue.png", 200, 200, new SKColor(0, 0, 255));
			string green = Solid("green.png", 100, 100, new SKColor(0, 160, 0));
			string sheet = Path.Combine(temp, "sheet.png");
			var r = Crcli("image", "sheet", "--out", sheet, "--cols", "2", "--cell-width", "100", "--gap", "10", red, blue, green);
			Assert.True(r.code == 0, r.output + r.error);
			using (SKBitmap bitmap = Load(sheet))
			{
				// Margin 12; row 1 = max(50, 100) + 26 caption; row 2 = 100 + 26; gap 10 between the rows.
				Assert.Equal(2 * 12 + 2 * 100 + 10, bitmap.Width);
				Assert.Equal(2 * 12 + (100 + 26) + 10 + (100 + 26), bitmap.Height);
				Assert.Equal(new SKColor(255, 0, 0), bitmap.GetPixel(12 + 50, 12 + 25));        // red cell, 100 x 50
				Assert.Equal(new SKColor(255, 255, 255), bitmap.GetPixel(12 + 50, 12 + 75));    // below the red picture: background
				Assert.Equal(new SKColor(0, 0, 255), bitmap.GetPixel(12 + 110 + 50, 12 + 50));  // blue cell, scaled 200 -> 100
				Assert.Equal(new SKColor(0, 160, 0), bitmap.GetPixel(12 + 50, 12 + 126 + 10 + 50)); // green cell is in the second row
				Assert.Equal(new SKColor(255, 255, 255), bitmap.GetPixel(12 + 105, 12 + 10));   // the gap
			}
			Assert.Contains("3 cells in 2 rows of 2", r.output);
		}

		[Fact]
		public void Each_cell_has_a_caption_and_the_text_after_the_separator_wins()
		{
			string red = Solid("red.png", 100, 100, new SKColor(255, 0, 0));
			string sheet = Path.Combine(temp, "caption.png");
			Assert.Equal(0, Crcli("image", "sheet", "--out", sheet, "--cols", "1", "--cell-width", "200", red + "::Light theme").code);
			using (SKBitmap bitmap = Load(sheet))
			{
				// The picture is 200 x 200 at (12, 12); the caption sits in the 26 pixels below it and is drawn in a dark ink.
				int ink = 0;
				for (int y = 12 + 200; y < 12 + 200 + 26; y++)
				{
					for (int x = 12; x < 12 + 200; x++)
					{
						SKColor c = bitmap.GetPixel(x, y);
						if (c.Red < 128 && c.Green < 128 && c.Blue < 128)
						{
							ink++;
						}
					}
				}
				Assert.True(ink > 20, "no caption ink under the picture (" + ink + " dark pixels)");
			}
			string longer = Path.Combine(temp, "caption2.png");
			Assert.Equal(0, Crcli("image", "sheet", "--out", longer, "--cols", "1", "--cell-width", "200", red + "::A much longer caption than the other one").code);
			Assert.NotEqual(File.ReadAllBytes(sheet), File.ReadAllBytes(longer));
		}

		[Fact]
		public void A_card_is_drawn_with_its_title_text_and_swatches()
		{
			string card = Json("card.json", "{ \"title\": \"MainForm\", \"width\": 300, \"lines\": [\"layout line\", \"  indented\"], " +
				"\"swatches\": [ { \"name\": \"Cr.Window\", \"color\": \"#FF0000\" }, { \"name\": \"Cr.Highlight\", \"color\": \"#0078D7\" } ] }");
			string sheet = Path.Combine(temp, "cardsheet.png");
			var r = Crcli("image", "sheet", "--out", sheet, "--cols", "1", "--cell-width", "300", card);
			Assert.True(r.code == 0, r.output + r.error);
			using (SKBitmap bitmap = Load(sheet))
			{
				int red = 0, blue = 0, ink = 0;
				for (int y = 0; y < bitmap.Height; y++)
				{
					for (int x = 0; x < bitmap.Width; x++)
					{
						SKColor c = bitmap.GetPixel(x, y);
						if (c == new SKColor(255, 0, 0))
						{
							red++;
						}
						else if (c == new SKColor(0, 0x78, 0xD7))
						{
							blue++;
						}
						else if (c.Red < 90 && c.Green < 90 && c.Blue < 90)
						{
							ink++;
						}
					}
				}
				Assert.True(red >= 100 * 40, "red swatch pixels: " + red);
				Assert.True(blue >= 100 * 40, "blue swatch pixels: " + blue);
				Assert.True(ink > 200, "title and text ink: " + ink);
			}
		}

		[Fact]
		public void A_card_without_a_height_grows_with_its_content()
		{
			string small = Json("small.json", "{ \"title\": \"A\", \"width\": 300 }");
			var many = new System.Text.StringBuilder("{ \"title\": \"A\", \"width\": 300, \"lines\": [");
			for (int i = 0; i < 40; i++)
			{
				many.Append(i == 0 ? "" : ",").Append("\"line ").Append(i).Append('"');
			}
			many.Append("] }");
			string big = Json("big.json", many.ToString());
			string a = Path.Combine(temp, "a.png");
			string b = Path.Combine(temp, "b.png");
			Assert.Equal(0, Crcli("image", "sheet", "--out", a, "--cols", "1", "--cell-width", "300", small).code);
			Assert.Equal(0, Crcli("image", "sheet", "--out", b, "--cols", "1", "--cell-width", "300", big).code);
			using (SKBitmap sa = Load(a))
			using (SKBitmap sb = Load(b))
			{
				Assert.True(sb.Height > sa.Height + 40 * 14, sa.Height + " against " + sb.Height);
			}
		}

		[Fact]
		public void The_same_inputs_give_the_same_file()
		{
			string red = Solid("red.png", 60, 40, new SKColor(255, 0, 0));
			string card = Json("card.json", "{ \"title\": \"T\", \"lines\": [\"x\"], \"swatches\": [ { \"name\": \"n\", \"color\": \"#102030\" } ] }");
			string one = Path.Combine(temp, "one.png");
			string two = Path.Combine(temp, "two.png");
			Assert.Equal(0, Crcli("image", "sheet", "--out", one, "--cols", "2", card, red).code);
			Assert.Equal(0, Crcli("image", "sheet", "--out", two, "--cols", "2", card, red).code);
			Assert.Equal(File.ReadAllBytes(one), File.ReadAllBytes(two));
		}

		[Fact]
		public void The_background_can_be_chosen()
		{
			string red = Solid("red.png", 20, 20, new SKColor(255, 0, 0));
			string sheet = Path.Combine(temp, "bg.png");
			Assert.Equal(0, Crcli("image", "sheet", "--out", sheet, "--cols", "1", "--cell-width", "64", "--background", "#102030", red).code);
			using (SKBitmap bitmap = Load(sheet))
			{
				Assert.Equal(new SKColor(0x10, 0x20, 0x30), bitmap.GetPixel(2, 2));
			}
		}

		[Fact]
		public void Bad_arguments_are_usage_errors()
		{
			string red = Solid("red.png", 20, 20, new SKColor(255, 0, 0));
			string sheet = Path.Combine(temp, "bad.png");
			Assert.Equal(2, Crcli("image", "sheet", "--cols", "2", red).code);                                  // no --out
			Assert.Equal(2, Crcli("image", "sheet", "--out", sheet, red).code);                                 // no --cols
			Assert.Equal(2, Crcli("image", "sheet", "--out", sheet, "--cols", "0", red).code);
			Assert.Equal(2, Crcli("image", "sheet", "--out", sheet, "--cols", "2").code);                       // no inputs
			Assert.Equal(2, Crcli("image", "sheet", "--out", sheet, "--cols", "2", Path.Combine(temp, "missing.png")).code);
			Assert.Equal(2, Crcli("image", "sheet", "--out", sheet, "--cols", "2", "--cell-width", "10", red).code);
			Assert.Equal(2, Crcli("image", "sheet", "--out", sheet, "--cols", "2", "--background", "blue-ish", red).code);
			Assert.Equal(2, Crcli("image", "sheet", "--out", sheet, "--cols", "2", Json("broken.json", "{ nope")).code);
			Assert.Equal(2, Crcli("image", "sheet", "--out", sheet, "--cols", "2", Json("array.json", "[1, 2]")).code);
			Assert.Equal(2, Crcli("image", "sheet", "--out", sheet, "--cols", "2", Json("colour.json", "{ \"swatches\": [ { \"name\": \"x\", \"color\": \"red\" } ] }")).code);
			Assert.False(File.Exists(sheet));
		}
	}
}
