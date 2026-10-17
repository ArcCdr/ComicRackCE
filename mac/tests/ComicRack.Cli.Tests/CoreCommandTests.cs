using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ComicRack.Cli.Core;
using ComicRack.Cli.Fixtures;
using Xunit;

namespace ComicRack.Cli.Tests
{
	/// <summary>`crcli db stats`, `scan`, `page` and `thumb` (M1.5b), run in process through Program.Run on generated fixtures.</summary>
	public class CoreCommandTests : IDisposable
	{
		private readonly string temp = Path.Combine(Path.GetTempPath(), "crcli-core-" + Guid.NewGuid().ToString("N"));

		public CoreCommandTests()
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

		private string Fixtures(string name, int books, int pages)
		{
			var env = new FixtureEnvironment { FixturesRoot = Path.Combine(temp, "fixtures"), BudgetBytes = FixtureEnvironment.DefaultBudgetBytes, PrintDiskFree = false, SevenZip = null };
			string dir = Path.Combine(env.FixturesRoot, name);
			Assert.Equal(0, FixtureCommands.Run(new[] { "make", dir, "--books", books.ToString(), "--pages", pages.ToString() }, new StringWriter(), new StringWriter(), env));
			return dir;
		}

		private static (int code, string output, string error) Crcli(params string[] args)
		{
			var o = new StringWriter();
			var e = new StringWriter();
			int code = Program.Run(args, o, e);
			return (code, o.ToString(), e.ToString());
		}

		private static string Field(string output, string name) => Regex.Match(output, @"^\s*" + Regex.Escape(name) + @":\s*(.*)$", RegexOptions.Multiline).Groups[1].Value.Trim();

		[Fact]
		public void Page_decodes_jpeg_png_and_webp_pages_through_the_ImagePool()
		{
			string dir = Fixtures("small", books: 1, pages: 6);
			string book = Directory.GetFiles(dir, "*.cbz").Single();
			foreach (int page in new[] { 1, 2, 3, 6 }) // 001.jpg, 002.png, 003.webp, 006.png
			{
				string png = Path.Combine(temp, "p" + page + ".png");
				var r = Crcli("page", book, "--page", page.ToString(), "--out", png);
				Assert.True(r.code == 0, r.output + r.error);
				using (var image = Image.FromFile(png))
				{
					double mp = image.Width * (double)image.Height / 1e6;
					Assert.InRange(mp, 1.9, 6.1);
					Assert.True(image.Height > image.Width);
					Assert.Contains($"page {page}: {image.Width} x {image.Height}", r.output);
				}
				Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, File.ReadAllBytes(png).Take(4).ToArray());
			}
		}

		[Fact]
		public void Page_beyond_the_last_page_fails_without_writing()
		{
			string book = Directory.GetFiles(Fixtures("small", 1, 3), "*.cbz").Single();
			string png = Path.Combine(temp, "none.png");
			var r = Crcli("page", book, "--page", "4", "--out", png);
			Assert.Equal(1, r.code);
			Assert.Contains("3 pages", r.error);
			Assert.False(File.Exists(png));
		}

		[Fact]
		public void Thumb_writes_a_jpeg_and_the_second_run_finds_it_in_the_disk_cache()
		{
			string book = Directory.GetFiles(Fixtures("small", 1, 3), "*.cbz").Single();
			string jpg = Path.Combine(temp, "t.jpg");
			var r = Crcli("thumb", book, "--out", jpg, "--data-dir", Path.Combine(temp, "data"));
			Assert.True(r.code == 0, r.output + r.error);
			byte[] bytes = File.ReadAllBytes(jpg);
			Assert.Equal(new byte[] { 0xFF, 0xD8 }, bytes.Take(2).ToArray());
			using (var image = Image.FromFile(jpg))
			{
				Assert.InRange(image.Height, 100, 512);
				Assert.True(image.Height > image.Width);
			}
			Assert.Contains("run 1:", r.output);
			Assert.Matches(@"run 1: \d+ ms, in disk cache before: False", r.output);
			Assert.Matches(@"run 2: \d+ ms, in disk cache before: True", r.output);
		}

		[Fact]
		public void Scan_adds_every_book_and_writes_a_database_that_db_stats_reads_back_unchanged()
		{
			string dir = Fixtures("scan", books: 7, pages: 4);
			string db = Path.Combine(temp, "out", "scan.xml");
			var r = Crcli("scan", dir, "--db", db);
			Assert.True(r.code == 0, r.output + r.error);
			Assert.Contains("files seen: 7, books added: 7", r.output);
			Assert.Contains("pages counted: 28", r.output);
			Assert.True(File.Exists(db));

			byte[] before = File.ReadAllBytes(db);
			DateTime stamp = File.GetLastWriteTimeUtc(db);
			var stats = Crcli("db", "stats", db);
			Assert.True(stats.code == 0, stats.output + stats.error);
			Assert.Equal("7", Field(stats.output, "books"));
			Assert.Matches(@"load time: \d+ ms", stats.output);
			Assert.Matches(@"working set [\d.]+ MB \(RSS\)", stats.output);
			Assert.Equal(before, File.ReadAllBytes(db)); // db stats never writes the database
			Assert.Equal(stamp, File.GetLastWriteTimeUtc(db));
		}

		[Fact]
		public void Db_stats_remaps_books_and_watch_folders_in_memory_only()
		{
			string db = Path.Combine(temp, "ComicDb.xml");
			File.WriteAllText(db, @"<?xml version='1.0' encoding='utf-8'?>
<ComicDatabase xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' Id='cf4f6499-c21a-4b02-8bf2-18eca0f8bdfe'><Books>
<Book Id='23fc8a4c-7235-45c5-9fba-6ae1b575a266' File='S:\media\BD\Serie A\Serie A - 01.cbz'><Title>One</Title><Series>Serie A</Series><Number>1</Number><PageCount>5</PageCount>
<Pages><Page Image='0' ImageWidth='1200' ImageHeight='1600'/></Pages></Book>
<Book Id='33fc8a4c-7235-45c5-9fba-6ae1b575a266' File='S:\media\BD\Serie B\Serie B - 02.cbz'><Title>Two</Title><Series>Serie B</Series><Number>2</Number><PageCount>1</PageCount></Book>
<Book Id='43fc8a4c-7235-45c5-9fba-6ae1b575a266' File='D:\elsewhere\c.cbz'><Title>Three</Title><PageCount>1</PageCount></Book>
</Books><WatchFolders><WatchFolder Folder='S:\media\BD' Watch='true'/><WatchFolder Folder='S:\incoming\ToSort\Cleaned_bd'/></WatchFolders></ComicDatabase>");
			string root = Path.Combine(temp, "fixtures", "BD");
			var env = new FixtureEnvironment { FixturesRoot = Path.Combine(temp, "fixtures"), BudgetBytes = FixtureEnvironment.DefaultBudgetBytes, PrintDiskFree = false, SevenZip = null };
			Assert.Equal(0, FixtureCommands.Run(new[] { "from-db", db, "--root", root }, new StringWriter(), new StringWriter(), env));

			byte[] before = File.ReadAllBytes(db);
			var r = Crcli("db", "stats", db, "--remap", @"S:\media\BD\=" + root + "/");
			Assert.True(r.code == 0, r.output + r.error);
			Assert.Equal("3", Field(r.output, "books"));
			Assert.Contains("books remapped: 2 of 3, of which the file exists: 2", r.output);
			Assert.Contains("watch folders remapped: 1 of 2", r.output);
			Assert.Contains(root, Field(r.output, "watch folders"));
			Assert.Equal(before, File.ReadAllBytes(db));
		}

		[Fact]
		public void PathRemap_maps_prefixes_with_or_without_a_trailing_separator()
		{
			PathRemap remap = PathRemap.Parse(@"S:\media\BD\=/m/BD/");
			Assert.Equal("/m/BD/Serie/a b.cbz", remap.Map(@"S:\media\BD\Serie\a b.cbz"));
			Assert.Equal("/m/BD/Serie/a.cbz", remap.Map(@"s:\MEDIA\bd\Serie\a.cbz"));
			Assert.Equal("/m/BD", remap.Map(@"S:\media\BD"));
			Assert.Null(remap.Map(@"S:\media\BDX\a.cbz"));
			Assert.Null(remap.Map(@"S:\incoming\a.cbz"));
			Assert.Null(remap.Map(""));
			Assert.Throws<UsageException>(() => PathRemap.Parse("nonsense"));
			Assert.Null(PathRemap.Parse(null));
		}

		[Fact]
		public void The_default_session_is_a_temporary_folder_and_nothing_goes_to_the_real_app_folders()
		{
			string book = Directory.GetFiles(Fixtures("small", 1, 2), "*.cbz").Single();
			string support = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			string mvp = Path.Combine(support, "ComicRack CE (MVP)");
			string testhost = Path.Combine(support, "Microsoft Corporation", "testhost");
			bool mvpBefore = Directory.Exists(mvp);
			bool testhostBefore = Directory.Exists(testhost);

			var r = Crcli("db", "stats", Path.Combine(temp, "missing.xml"));
			Assert.Equal(2, r.code); // a DB that does not exist is a usage error
			string db = Path.Combine(temp, "scan.xml");
			Assert.Equal(0, Crcli("scan", Path.GetDirectoryName(book), "--db", db).code);
			var stats = Crcli("db", "stats", db);
			string dataFolder = Field(stats.output, "data folder");
			Assert.StartsWith(Path.GetTempPath().TrimEnd('/'), dataFolder, StringComparison.Ordinal);
			Assert.False(Directory.Exists(dataFolder), "the temporary session folder is deleted at the end");

			Assert.Equal(mvpBefore, Directory.Exists(mvp));
			Assert.Equal(testhostBefore, Directory.Exists(testhost));
		}

		[Fact]
		public void Data_dir_keeps_data_cache_and_log_in_that_folder()
		{
			string book = Directory.GetFiles(Fixtures("small", 1, 2), "*.cbz").Single();
			string data = Path.Combine(temp, "kept");
			Assert.Equal(0, Crcli("page", book, "--page", "1", "--out", Path.Combine(temp, "p.png"), "--data-dir", data).code);
			Assert.True(File.Exists(Path.Combine(data, "Logs", "ComicRack.log")));
			Assert.True(Directory.Exists(Path.Combine(data, "Cache", "Images")));
		}

		[Fact]
		public void Real_fixture_database_reports_14706_books_when_it_is_present()
		{
			// mac/fixtures is git-ignored and generated by `crcli fixtures from-db`; a fresh checkout skips this test.
			string root = FixtureEnvironment.Default().FixturesRoot;
			string db = Path.Combine(root, "ComicDb.xml");
			if (!File.Exists(db) || !Directory.Exists(Path.Combine(root, "BD")))
			{
				return;
			}
			var r = Crcli("db", "stats", db, "--remap", @"S:\media\BD\=" + Path.Combine(root, "BD") + "/");
			Assert.True(r.code == 0, r.output + r.error);
			Assert.Equal("14706", Field(r.output, "books"));
			Assert.Contains("books remapped: 14706 of 14706, of which the file exists: 14706", r.output);
			Assert.Contains("watch folders remapped: 1 of 2", r.output);
		}
	}
}
