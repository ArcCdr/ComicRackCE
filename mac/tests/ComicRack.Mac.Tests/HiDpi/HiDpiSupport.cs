using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.HiDpi
{
	/// <summary>
	/// The HiDPI test harness (P3.H). A window on the headless Skia platform is shown at a chosen render scaling
	/// (<see cref="Dpi.Apply"/> calls Avalonia's <c>SetRenderScaling</c>: the window keeps its size in device independent units, its
	/// frame grows to device pixels), and <see cref="Dpi.Capture"/> turns the frame into a <see cref="Raster"/> that tests read
	/// pixel by pixel, compare with the frame of another scale (<see cref="Raster.Downscale"/>, <see cref="Raster.Compare"/>) and, with
	/// CRMAC_FRAMES_DIR set, save as <c>name@2x.png</c> in device pixels.
	///
	/// The rule (full-port.md section 2.5): every custom drawn control or draw operation of the app adds a test that runs at
	/// <see cref="Scales.Main"/> (1x and 2x) with this harness, and carries <c>[Trait("Category", "HiDpi")]</c>.
	/// </summary>
	public static class Scales
	{
		/// <summary>The scales every control is tested at: a 1x display and a Retina display.</summary>
		public static readonly double[] Main = { 1.0, 2.0 };

		/// <summary>Main plus the fractional scale of a scaled display mode and the 3x of the largest iPhone-like panels (cheap tests only).</summary>
		public static readonly double[] All = { 1.0, 1.5, 2.0, 3.0 };

		public static IEnumerable<object[]> MainData => Main.Select(s => new object[] { s });

		public static IEnumerable<object[]> AllData => All.Select(s => new object[] { s });

		/// <summary>The device pixels of a length in device independent units, rounded as the render path rounds (to even).</summary>
		public static int Device(double logical, double scale) => (int)Math.Round((double)((float)logical * (float)scale));

		public static string Tag(double scale) => scale.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "x";
	}

	public static class Dpi
	{
		/// <summary>Runs queued jobs, layout passes and render ticks, so that what changed is laid out and drawn.</summary>
		public static void Settle(TopLevel window)
		{
			for (int i = 0; i < 4; i++)
			{
				Dispatcher.UIThread.RunJobs();
				(window as Window)?.UpdateLayout();
				AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			}
		}

		/// <summary>Moves the (shown) window to a display with this scale, as dragging it to another display does.</summary>
		public static void Apply(TopLevel window, double scale)
		{
			window.SetRenderScaling(scale);
			Assert.Equal(scale, window.RenderScaling);
			Settle(window);
		}

		/// <summary>The frame the window drew last, in device pixels; saved when CRMAC_FRAMES_DIR is set.</summary>
		public static Raster Capture(Window window, string name)
		{
			Settle(window);
			WriteableBitmap frame = window.CaptureRenderedFrame();
			Assert.NotNull(frame);
			Raster raster = Raster.From(frame);
			string dir = Environment.GetEnvironmentVariable("CRMAC_FRAMES_DIR");
			if (!string.IsNullOrEmpty(dir))
			{
				Directory.CreateDirectory(dir);
				raster.Save(Path.Combine(dir, name + "@" + Scales.Tag(window.RenderScaling) + ".png"));
			}
			return raster;
		}

		/// <summary>The top left of a control in window coordinates (device independent units).</summary>
		public static Point Origin(Visual control, Visual window)
		{
			return control.TranslatePoint(new Point(0, 0), window) ?? throw new InvalidOperationException("The control is not in the window.");
		}

		/// <summary>The rectangle of a control in window coordinates (device independent units).</summary>
		public static Rect Area(Control control, Visual window) => new Rect(Origin(control, window), control.Bounds.Size);
	}

	public readonly struct Difference
	{
		public Difference(double mean, double bad, int max)
		{
			Mean = mean;
			Bad = bad;
			Max = max;
		}

		/// <summary>Mean absolute difference of the colour channels, 0 to 255.</summary>
		public double Mean { get; }

		/// <summary>Fraction of pixels whose largest channel difference is above the threshold.</summary>
		public double Bad { get; }

		public int Max { get; }

		public override string ToString() => $"mean {Mean:0.00}, {Bad:P2} of the pixels differ by more than the threshold, largest {Max}";
	}

	/// <summary>An opaque frame in device pixels, RGBA, read by the tests.</summary>
	public sealed class Raster
	{
		private readonly byte[] rgba;

		private Raster(int width, int height, byte[] rgba)
		{
			Width = width;
			Height = height;
			this.rgba = rgba;
		}

		public int Width { get; }

		public int Height { get; }

		public static Raster From(WriteableBitmap frame)
		{
			bool bgra = frame.Format == Avalonia.Platform.PixelFormat.Bgra8888;
			Assert.True(bgra || frame.Format == Avalonia.Platform.PixelFormat.Rgba8888, "frame format " + frame.Format);
			int w = frame.PixelSize.Width, h = frame.PixelSize.Height;
			var data = new byte[w * h * 4];
			using (var buffer = frame.Lock())
			{
				unsafe
				{
					for (int y = 0; y < h; y++)
					{
						byte* row = (byte*)buffer.Address + y * buffer.RowBytes;
						for (int x = 0; x < w; x++)
						{
							int o = (y * w + x) * 4;
							data[o] = bgra ? row[x * 4 + 2] : row[x * 4];
							data[o + 1] = row[x * 4 + 1];
							data[o + 2] = bgra ? row[x * 4] : row[x * 4 + 2];
							data[o + 3] = 255;
						}
					}
				}
			}
			return new Raster(w, h, data);
		}

		/// <summary>A raster of an image (premultiplied alpha is not undone: the images of the tests are opaque).</summary>
		public static Raster From(SKImage image)
		{
			using (var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul)))
			{
				Assert.True(image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0));
				var data = new byte[image.Width * image.Height * 4];
				System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), data, 0, data.Length);
				return new Raster(image.Width, image.Height, data);
			}
		}

		public SKColor this[int x, int y]
		{
			get
			{
				if (x < 0 || y < 0 || x >= Width || y >= Height)
				{
					throw new ArgumentOutOfRangeException($"({x}, {y}) is outside the {Width} x {Height} frame.");
				}
				int o = (y * Width + x) * 4;
				return new SKColor(rgba[o], rgba[o + 1], rgba[o + 2], 255);
			}
		}

		public static bool Near(SKColor a, SKColor b, int tolerance = 2)
		{
			return Math.Abs(a.Red - b.Red) <= tolerance && Math.Abs(a.Green - b.Green) <= tolerance && Math.Abs(a.Blue - b.Blue) <= tolerance;
		}

		/// <summary>The smallest pixel rectangle (x, y, right, bottom exclusive) holding every pixel in the region that matches, or null.</summary>
		public RectInt? Bounds(Func<SKColor, bool> matches, RectInt? region = null)
		{
			RectInt r = region ?? new RectInt(0, 0, Width, Height);
			int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
			for (int y = Math.Max(0, r.Top); y < Math.Min(Height, r.Bottom); y++)
			{
				for (int x = Math.Max(0, r.Left); x < Math.Min(Width, r.Right); x++)
				{
					if (matches(this[x, y]))
					{
						left = Math.Min(left, x);
						right = Math.Max(right, x);
						top = Math.Min(top, y);
						bottom = Math.Max(bottom, y);
					}
				}
			}
			return right < 0 ? (RectInt?)null : new RectInt(left, top, right + 1, bottom + 1);
		}

		/// <summary>Number of pixels in the row segment [x0, x1) that match.</summary>
		public int CountInRow(int y, int x0, int x1, Func<SKColor, bool> matches)
		{
			int n = 0;
			for (int x = Math.Max(0, x0); x < Math.Min(Width, x1); x++)
			{
				if (matches(this[x, y]))
				{
					n++;
				}
			}
			return n;
		}

		/// <summary>Number of pixels in the column segment [y0, y1) that match.</summary>
		public int CountInColumn(int x, int y0, int y1, Func<SKColor, bool> matches)
		{
			int n = 0;
			for (int y = Math.Max(0, y0); y < Math.Min(Height, y1); y++)
			{
				if (matches(this[x, y]))
				{
					n++;
				}
			}
			return n;
		}

		/// <summary>The sub-rectangle as a raster of its own.</summary>
		public Raster Crop(RectInt area)
		{
			int w = area.Width, h = area.Height;
			var data = new byte[w * h * 4];
			for (int y = 0; y < h; y++)
			{
				Buffer.BlockCopy(rgba, ((area.Top + y) * Width + area.Left) * 4, data, y * w * 4, w * 4);
			}
			return new Raster(w, h, data);
		}

		/// <summary>
		/// The raster reduced to <paramref name="width"/> x <paramref name="height"/> by area averaging (every source pixel counts with the
		/// part of it that falls into the target pixel), the exact inverse of showing a picture at a larger scale.
		/// </summary>
		public Raster Downscale(int width, int height)
		{
			var data = new byte[width * height * 4];
			double sx = (double)Width / width, sy = (double)Height / height;
			for (int y = 0; y < height; y++)
			{
				double y0 = y * sy, y1 = (y + 1) * sy;
				for (int x = 0; x < width; x++)
				{
					double x0 = x * sx, x1 = (x + 1) * sx;
					double r = 0, g = 0, b = 0, weight = 0;
					for (int yy = (int)Math.Floor(y0); yy < Math.Min(Height, (int)Math.Ceiling(y1)); yy++)
					{
						double wy = Math.Min(y1, yy + 1) - Math.Max(y0, yy);
						for (int xx = (int)Math.Floor(x0); xx < Math.Min(Width, (int)Math.Ceiling(x1)); xx++)
						{
							double w = wy * (Math.Min(x1, xx + 1) - Math.Max(x0, xx));
							int o = (yy * Width + xx) * 4;
							r += rgba[o] * w;
							g += rgba[o + 1] * w;
							b += rgba[o + 2] * w;
							weight += w;
						}
					}
					int t = (y * width + x) * 4;
					data[t] = (byte)Math.Round(r / weight);
					data[t + 1] = (byte)Math.Round(g / weight);
					data[t + 2] = (byte)Math.Round(b / weight);
					data[t + 3] = 255;
				}
			}
			return new Raster(width, height, data);
		}

		/// <summary>Compares two rasters of the same size pixel by pixel.</summary>
		public Difference Compare(Raster other, int threshold = 48)
		{
			Assert.Equal(Width, other.Width);
			Assert.Equal(Height, other.Height);
			long sum = 0;
			long bad = 0;
			int max = 0;
			for (int i = 0; i < rgba.Length; i += 4)
			{
				int d0 = Math.Abs(rgba[i] - other.rgba[i]), d1 = Math.Abs(rgba[i + 1] - other.rgba[i + 1]), d2 = Math.Abs(rgba[i + 2] - other.rgba[i + 2]);
				sum += d0 + d1 + d2;
				int m = Math.Max(d0, Math.Max(d1, d2));
				max = Math.Max(max, m);
				if (m > threshold)
				{
					bad++;
				}
			}
			int pixels = rgba.Length / 4;
			return new Difference(sum / (3.0 * pixels), (double)bad / pixels, max);
		}

		public void Save(string path)
		{
			using (var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque)))
			{
				System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
				using (SKImage image = SKImage.FromBitmap(bitmap))
				using (SKData data = image.Encode(SKEncodedImageFormat.Png, 100))
				using (FileStream stream = File.Create(path))
				{
					data.SaveTo(stream);
				}
			}
		}
	}

	/// <summary>An integer pixel rectangle, right and bottom exclusive.</summary>
	public readonly struct RectInt : IEquatable<RectInt>
	{
		public RectInt(int left, int top, int right, int bottom)
		{
			Left = left;
			Top = top;
			Right = right;
			Bottom = bottom;
		}

		public int Left { get; }

		public int Top { get; }

		public int Right { get; }

		public int Bottom { get; }

		public int Width => Right - Left;

		public int Height => Bottom - Top;

		/// <summary>The device pixels a rectangle of device independent units covers once its edges are rounded to whole pixels.</summary>
		public static RectInt Snapped(Rect logical, double scale)
		{
			return new RectInt(Scales.Device(logical.Left, scale), Scales.Device(logical.Top, scale), Scales.Device(logical.Right, scale), Scales.Device(logical.Bottom, scale));
		}

		public RectInt Inflate(int by) => new RectInt(Left - by, Top - by, Right + by, Bottom + by);

		public bool Equals(RectInt other) => Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

		public override bool Equals(object obj) => obj is RectInt r && Equals(r);

		public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

		public override string ToString() => $"[{Left},{Top} - {Right},{Bottom}] ({Width} x {Height})";
	}

	/// <summary>Test pictures with detail that shows blur: 1 pixel lines on a smooth ground.</summary>
	public static class Pictures
	{
		/// <summary>
		/// A picture of <paramref name="width"/> x <paramref name="height"/> pixels: a ground that changes with the position, black lines
		/// of one pixel every 8 pixels (vertical) and 10 pixels (horizontal) and a 2 pixel white frame. Any resampling that is not a
		/// pixel copy softens the lines.
		/// </summary>
		public static SKImage Lines(int width, int height)
		{
			using (var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque)))
			{
				for (int y = 0; y < height; y++)
				{
					for (int x = 0; x < width; x++)
					{
						SKColor c;
						if (x < 2 || y < 2 || x >= width - 2 || y >= height - 2)
						{
							c = SKColors.White;
						}
						else if (x % 8 == 0 || y % 10 == 0)
						{
							c = SKColors.Black;
						}
						else
						{
							c = new SKColor((byte)(60 + 140 * x / width), (byte)(90 + 100 * y / height), (byte)(200 - 100 * x / width), 255);
						}
						bitmap.SetPixel(x, y, c);
					}
				}
				return SKImage.FromBitmap(bitmap);
			}
		}
	}
}
