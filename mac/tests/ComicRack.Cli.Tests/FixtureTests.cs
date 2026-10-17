using System;
using System.Drawing;
using System.IO;
using System.Linq;
using ComicRack.Cli.Fixtures;
using ComicRack.Mac.Host;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using Xunit;

namespace ComicRack.Cli.Tests
{
	public class FixtureTests : IDisposable
	{
		private readonly string temp = Path.Combine(Path.GetTempPath(), "crcli-fixtures-" + Guid.NewGuid().ToString("N"));

		public FixtureTests()
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

		private FixtureEnvironment Env(long budget = FixtureEnvironment.DefaultBudgetBytes) => new FixtureEnvironment
		{
			FixturesRoot = Path.Combine(temp, "fixtures"),
			BudgetBytes = budget,
			PrintDiskFree = false,
			SevenZip = null,
		};

		private int Run(FixtureEnvironment env, StringWriter output, params string[] args) => FixtureCommands.Run(args, output, output, env);

		/// <summary>Opens the host (it configures the Core's archive engines) and returns the Core provider for a book.</summary>
		private static ImageProvider OpenWithCore(string book)
		{
			ImageProvider provider = Providers.Readers.CreateSourceProvider(book);
			Assert.NotNull(provider);
			provider.Open(false);
			return provider;
		}

		[Fact]
		public void Make_writes_books_that_the_Core_provider_reads_with_the_requested_page_count_and_size()
		{
			FixtureEnvironment env = Env();
			string dir = Path.Combine(env.FixturesRoot, "small");
			var output = new StringWriter();
			Assert.Equal(0, Run(env, output, "make", dir, "--books", "3", "--pages", "6"));

			string[] files = Directory.GetFiles(dir, "*.cbz");
			Assert.Equal(3, files.Length);
			using (var host = MacHost.Open(new MacHostOptions { Paths = MacPaths.Under(Path.Combine(temp, "host")) }))
			{
				foreach (string file in files)
				{
					using (ImageProvider provider = OpenWithCore(file))
					{
						Assert.Equal(6, provider.Count);
						for (int i = 0; i < provider.Count; i++)
						{
							using (Bitmap page = provider.GetImage(i))
							{
								double mp = page.Width * (double)page.Height / 1e6;
								Assert.InRange(mp, 1.9, 6.1);
								Assert.True(page.Height > page.Width);
							}
						}
					}
				}
			}
		}

		[Fact]
		public void Make_mixes_jpeg_png_and_webp()
		{
			FixtureEnvironment env = Env();
			string dir = Path.Combine(env.FixturesRoot, "mix");
			Assert.Equal(0, Run(env, new StringWriter(), "make", dir, "--books", "1", "--pages", "3"));
			using (var zip = new ICSharpCode.SharpZipLib.Zip.ZipFile(Directory.GetFiles(dir).Single()))
			{
				var names = Enumerable.Range(0, (int)zip.Count).Select(i => zip[i].Name).OrderBy(n => n).ToArray();
				Assert.Equal(new[] { "001.jpg", "002.png", "003.webp" }, names);
			}
		}

		[Fact]
		public void FromDb_writes_a_scaled_standin_at_the_remapped_path_and_copies_the_db()
		{
			string db = Path.Combine(temp, "ComicDb.xml");
			File.WriteAllText(db, @"<?xml version='1.0' encoding='utf-8'?>
<ComicDatabase><Books>
<Book Id='a' File='S:\media\BD\Serie A\Serie A - 01.cbz'><Title>One</Title><Series>Serie A</Series><Number>1</Number><PageCount>5</PageCount>
<Pages><Page Image='0' ImageWidth='1200' ImageHeight='1600'/><Page Image='1' ImageWidth='1200' ImageHeight='1500'/><Page Image='2' ImageWidth='2400' ImageHeight='1200'/></Pages></Book>
<Book Id='b' File='S:\media\BD\Serie B\..\Serie B - 02.cbz'><Title>Two</Title><Series>Serie B</Series><Number>2</Number><PageCount>1</PageCount></Book>
</Books></ComicDatabase>");
			FixtureEnvironment env = Env();
			string root = Path.Combine(env.FixturesRoot, "BD");
			Assert.Equal(0, Run(env, new StringWriter(), "from-db", db, "--root", root, "--pages", "3"));

			string a = Path.Combine(root, "Serie A", "Serie A - 01.cbz");
			Assert.True(File.Exists(a));
			Assert.True(File.Exists(Path.Combine(root, "Serie B", "Serie B - 02.cbz")), "'..' segments are dropped, never escape the root");
			Assert.Equal(File.ReadAllText(db), File.ReadAllText(Path.Combine(env.FixturesRoot, "ComicDb.xml")));
			using (var host = MacHost.Open(new MacHostOptions { Paths = MacPaths.Under(Path.Combine(temp, "host")) }))
			using (ImageProvider provider = OpenWithCore(a))
			{
				Assert.Equal(3, provider.Count);
				var sizes = Enumerable.Range(0, 3).Select(i => { using (Bitmap b = provider.GetImage(i)) { return (b.Width, b.Height); } }).ToArray();
				Assert.Equal((600, 800), sizes[0]);
				Assert.Equal((640, 800), sizes[1]);
				Assert.Equal((800, 400), sizes[2]); // the DB aspect (2:1), fitted to 800
			}
		}

		[Fact]
		public void A_command_over_the_budget_is_refused_and_writes_nothing()
		{
			FixtureEnvironment env = Env(budget: 50 * 1024);
			string dir = Path.Combine(env.FixturesRoot, "big");
			var ex = Assert.Throws<RefusedException>(() => Run(env, new StringWriter(), "make", dir, "--books", "2", "--pages", "4"));
			Assert.Contains("exceeds the budget", ex.Message);
			Assert.False(Directory.Exists(dir));
		}

		[Fact]
		public void Cb7_and_cbr_are_skipped_with_a_message_when_there_is_no_7zz()
		{
			FixtureEnvironment env = Env();
			var output = new StringWriter();
			Assert.Equal(0, Run(env, output, "make", Path.Combine(env.FixturesRoot, "x"), "--books", "1", "--pages", "1", "--formats", "cbz,cb7,cbr"));
			Assert.Contains("Skipping CB7", output.ToString());
			Assert.Contains("Skipping CBR", output.ToString());
			Assert.Single(Directory.GetFiles(Path.Combine(env.FixturesRoot, "x")));
		}
	}
}
