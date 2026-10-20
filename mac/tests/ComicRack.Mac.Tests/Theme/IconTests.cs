using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using ComicRack.Mac.Themes;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// P5.T2: the original images are available by their resource name (<see cref="Icons"/>). The map is generated from the four
	/// <c>Properties/Resources.resx</c> files; these tests keep it honest: not stale, every name the original's Designer files use
	/// resolves, every mapped file is in the assembly and decodes, the dark variants follow the original's rule, magenta is keyed out of
	/// GIF and BMP sources only.
	/// </summary>
	public class IconTests
	{
		private static readonly string Root = OriginalColors.RepoRoot;
		private readonly ITestOutputHelper output;

		public IconTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		/// <summary>The four projects with an image resx: folder name and the namespace of the generated Resources class.</summary>
		private static readonly (string Project, string Namespace)[] Projects =
		{
			("ComicRack", "cYo.Projects.ComicRack.Viewer.Properties"),
			("cYo.Common.Windows", "cYo.Common.Windows.Properties"),
			("ComicRack.Engine", "cYo.Projects.ComicRack.Engine.Properties"),
			("ComicRack.Engine.Display.Forms", "cYo.Projects.ComicRack.Engine.Display.Forms.Properties"),
		};

		private static string Python()
		{
			// python3 is part of the Xcode command line tools and of every runner image used by this repository
			return "python3";
		}

		[Fact]
		public void The_committed_map_is_what_the_tool_generates_now()
		{
			string tool = Path.Combine(Root, "mac", "tools", "icon-map", "icon-map.py");
			string committed = Path.Combine(Root, "mac", "ComicRack.Mac", "Assets", "icon-map.json");
			using (var temp = new TempDir())
			{
				string regenerated = temp.Combine("icon-map.json");
				var info = new ProcessStartInfo(Python()) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
				info.ArgumentList.Add(tool);
				info.ArgumentList.Add("--root");
				info.ArgumentList.Add(Root);
				info.ArgumentList.Add("--out");
				info.ArgumentList.Add(regenerated);
				using (Process process = Process.Start(info))
				{
					string stderr = process.StandardError.ReadToEnd();
					process.StandardOutput.ReadToEnd();
					process.WaitForExit();
					Assert.True(process.ExitCode == 0, "icon-map.py failed: " + stderr);
				}
				Assert.True(File.ReadAllText(committed) == File.ReadAllText(regenerated),
					"mac/ComicRack.Mac/Assets/icon-map.json is stale: run python3 mac/tools/icon-map/icon-map.py and commit the result.");
			}
		}

		[Fact]
		public async Task Every_resource_the_original_designer_files_use_resolves()
		{
			await Ui.Run(() =>
			{
				var used = new SortedSet<string>(StringComparer.Ordinal);
				int files = 0;
				foreach ((string project, string ns) in Projects)
				{
					var pattern = new Regex(@"(?:global::)?" + Regex.Escape(ns) + @"\.Resources\.([A-Za-z0-9_]+)");
					foreach (string file in Directory.EnumerateFiles(Path.Combine(Root, project), "*.Designer.cs", SearchOption.AllDirectories))
					{
						if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
						{
							continue;
						}
						files++;
						foreach (Match m in pattern.Matches(File.ReadAllText(file)))
						{
							used.Add(project + ":" + m.Groups[1].Value);
						}
					}
				}
				Assert.True(files > 20, "the Designer files were not found under " + Root);
				Assert.True(used.Count > 100, "only " + used.Count + " resource references were found; the pattern no longer matches the Designer files");
				var missing = new List<string>();
				foreach (string qualified in used)
				{
					// a Designer file refers to a Resources member; strings are no images, so only the image resx entries count
					string project = qualified.Substring(0, qualified.IndexOf(':'));
					string name = qualified.Substring(project.Length + 1);
					if (!Icons.Contains(qualified))
					{
						if (!IsNonImageResource(project, name))
						{
							missing.Add(qualified);
						}
						continue;
					}
					Assert.NotNull(Icons.Get(qualified, ThemeVariant.Light));
					Assert.NotNull(Icons.Get(qualified, ThemeVariant.Dark));
				}
				Assert.True(missing.Count == 0, "Designer references without a map entry: " + string.Join(", ", missing));
				output.WriteLine(used.Count + " resource names used by the Designer files of the four projects resolve");
				return Task.CompletedTask;
			});
		}

		/// <summary>True if the resx entry <paramref name="name"/> exists but is not an image (a string or a byte array).</summary>
		private static bool IsNonImageResource(string project, string name)
		{
			string resx = File.ReadAllText(Path.Combine(Root, project, "Properties", "Resources.resx"));
			return Regex.IsMatch(resx, "<data name=\"" + Regex.Escape(name) + "\"") && !Regex.IsMatch(resx, "<data name=\"" + Regex.Escape(name) + "\" type=\"System.Resources.ResXFileRef");
		}

		[Fact]
		public async Task Every_mapped_file_is_in_the_assembly_and_decodes_light_and_dark()
		{
			await Ui.Run(() =>
			{
				int resolved = 0;
				foreach (string project in Icons.ProjectOrder)
				{
					foreach (string name in Icons.NamesOf(project))
					{
						string qualified = project + ":" + name;
						foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
						{
							Bitmap bitmap = Icons.Get(qualified, variant);
							Assert.True(bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0, qualified + " has no pixels");
						}
						resolved++;
					}
				}
				output.WriteLine(resolved + " resource names resolved in " + Icons.ProjectOrder.Count + " projects (" + Icons.Names.Count + " distinct names)");
				Assert.True(resolved >= 270, "expected at least the 277 names of the four resx files and the application icons, found " + resolved);
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_sizes_are_those_of_the_original_files()
		{
			await Ui.Run(() =>
			{
				// chrome-guidelines section 5: 16 x 16 menu and toolbar images, 32 x 32 preference tabs, Magnifier 650 x 400
				Assert.Equal(new PixelSize(16, 16), Icons.Get("FitWidth").PixelSize);
				Assert.Equal(new PixelSize(650, 400), Icons.Get("ComicRack.Engine.Display.Forms:Magnifier").PixelSize);
				Assert.Equal(new PixelSize(16, 16), Icons.Get("Close", ThemeVariant.Dark).PixelSize);
				Assert.Equal(96, Icons.Get("FitWidth").Dpi.X);
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Names_that_exist_in_two_projects_resolve_to_the_first_and_can_be_asked_for_by_project()
		{
			await Ui.Run(() =>
			{
				Assert.Equal("ComicRack", Icons.ProjectOrder[0]);
				Assert.Equal("ComicRack/Resources/GoLast.png", Icons.PathOf("GoLast"));
				Assert.Equal("ComicRack.Engine.Display.Forms/Resources/GoLast.png", Icons.PathOf("ComicRack.Engine.Display.Forms:GoLast"));
				Assert.Equal("cYo.Common.Windows/Resources/MozillaGrip.gif", Icons.PathOf("MozillaGrip"));
				Assert.False(Icons.Contains("NoSuchImage"));
				Assert.False(Icons.Contains("NoProject:GoLast"));
				Assert.Null(Icons.TryGet("NoSuchImage"));
				Assert.Throws<KeyNotFoundException>(() => Icons.Get("NoSuchImage"));
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_name_with_a_dark_variant_gives_another_image_in_the_dark_theme_and_the_light_one_otherwise()
		{
			await Ui.Run(() =>
			{
				// the rule of ComicRack/Properties/ResourceManagerEx: a bitmap "DarkX" is the dark variant of the bitmap "X" if X exists
				foreach (string project in new[] { "ComicRack", "cYo.Common.Windows" })
				{
					HashSet<string> names = Icons.NamesOf(project).ToHashSet();
					foreach (string name in names)
					{
						bool hasDarkTwin = !name.StartsWith("Dark", StringComparison.Ordinal) && names.Contains("Dark" + name);
						Assert.Equal(hasDarkTwin, Icons.HasDark(project + ":" + name));
					}
				}
				Assert.True(Icons.HasDark("DetailView"));
				Assert.True(Icons.HasDark("cYo.Common.Windows:SortUp"));
				Assert.False(Icons.HasDark("FitWidth"));
				Bitmap light = Icons.Get("DetailView", ThemeVariant.Light);
				Bitmap dark = Icons.Get("DetailView", ThemeVariant.Dark);
				Assert.NotSame(light, dark);
				Assert.Same(dark, Icons.Get("DarkDetailView", ThemeVariant.Light));
				Assert.Same(Icons.Get("FitWidth", ThemeVariant.Light), Icons.Get("FitWidth", ThemeVariant.Dark));
				Assert.Same(light, Icons.Get("DetailView", ThemeVariant.Light));
				// the dark variant is a different picture, not a copy
				Assert.NotEqual(Pixels(light), Pixels(dark));
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_current_appearance_chooses_the_variant_when_none_is_given()
		{
			await Ui.Run(() =>
			{
				Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
				Assert.Same(Icons.Get("DetailView", ThemeVariant.Dark), Icons.Get("DetailView"));
				Application.Current.RequestedThemeVariant = ThemeVariant.Light;
				Assert.Same(Icons.Get("DetailView", ThemeVariant.Light), Icons.Get("DetailView"));
				return Task.CompletedTask;
			});
		}

		[Fact]
		public void A_magenta_pixel_becomes_transparent_and_other_pixels_stay()
		{
			// B, G, R, A: magenta opaque, magenta with alpha (a real picture colour), red, white
			byte[] pixels = { 255, 0, 255, 255, 255, 0, 255, 128, 0, 0, 255, 255, 255, 255, 255, 255 };
			Assert.Equal(1, Icons.KeyMagenta(pixels));
			Assert.Equal(new byte[] { 0, 0, 0, 0, 255, 0, 255, 128, 0, 0, 255, 255, 255, 255, 255, 255 }, pixels);
		}

		[Fact]
		public void Only_GIF_and_BMP_sources_have_a_transparent_magenta()
		{
			Assert.True(Icons.KeysMagenta("ComicRack/Resources/AlignFill.gif"));
			Assert.True(Icons.KeysMagenta("x/Y.BMP"));
			Assert.False(Icons.KeysMagenta("ComicRack/Resources/FitWidth.png"));
			Assert.False(Icons.KeysMagenta("ComicRack/Icons/icon1.ico"));
		}

		/// <summary>A 2 x 1 GIF whose pixels are magenta and red (global palette, LZW code size 2).</summary>
		private static readonly byte[] MagentaRedGif =
		{
			0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x02, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
			0xFF, 0x00, 0xFF, 0xFF, 0x00, 0x00,
			0x2C, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01, 0x00, 0x00,
			0x02, 0x02, 0x44, 0x0A, 0x00, 0x3B,
		};

		[Fact]
		public async Task A_gif_loses_its_magenta_but_a_png_keeps_it()
		{
			await Ui.Run(() =>
			{
				using (Bitmap gif = Icons.Decode(new MemoryStream(MagentaRedGif), keyMagenta: true))
				{
					byte[] p = Pixels(gif);
					Assert.Equal(new PixelSize(2, 1), gif.PixelSize);
					Assert.Equal(0, p[3]);
					Assert.Equal((byte)255, p[7]);
					Assert.Equal((byte)255, p[6]);
					Assert.Equal(0, p[4]);
				}
				// the same picture as a PNG is not keyed: its magenta is magenta
				using (var source = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Bgra8888, SKAlphaType.Opaque)))
				{
					source.SetPixel(0, 0, new SKColor(255, 0, 255));
					source.SetPixel(1, 0, new SKColor(255, 0, 0));
					using (SKImage image = SKImage.FromBitmap(source))
					using (SKData png = image.Encode(SKEncodedImageFormat.Png, 100))
					using (Bitmap bitmap = Icons.Decode(new MemoryStream(png.ToArray()), keyMagenta: false))
					{
						byte[] p = Pixels(bitmap);
						Assert.Equal((byte)255, p[3]);
						Assert.Equal(255, Math.Max(p[0], p[2]));
						Assert.Equal(0, p[1]);
					}
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task No_gif_of_the_original_keeps_an_opaque_magenta_pixel()
		{
			await Ui.Run(() =>
			{
				int gifs = 0;
				foreach (string project in Icons.ProjectOrder)
				{
					foreach (string name in Icons.NamesOf(project))
					{
						string qualified = project + ":" + name;
						if (!Icons.KeysMagenta(Icons.PathOf(qualified)))
						{
							continue;
						}
						gifs++;
						byte[] p = Pixels(Icons.Get(qualified, ThemeVariant.Light));
						for (int i = 0; i + 3 < p.Length; i += 4)
						{
							bool magenta = p[i] == 255 && p[i + 1] == 0 && p[i + 2] == 255 && p[i + 3] == 255;
							Assert.False(magenta, qualified + " still has an opaque magenta pixel at index " + i / 4);
						}
					}
				}
				Assert.True(gifs >= 15, "found only " + gifs + " GIF sources");
				return Task.CompletedTask;
			});
		}

		/// <summary>The pixels of a bitmap as bytes in the bitmap's own format (BGRA or RGBA, premultiplied or not), row after row without padding.</summary>
		internal static byte[] Pixels(Bitmap bitmap)
		{
			int w = bitmap.PixelSize.Width, h = bitmap.PixelSize.Height;
			var data = new byte[w * h * 4];
			GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
			try
			{
				bitmap.CopyPixels(new PixelRect(0, 0, w, h), pin.AddrOfPinnedObject(), data.Length, w * 4);
			}
			finally
			{
				pin.Free();
			}
			return data;
		}
	}
}
