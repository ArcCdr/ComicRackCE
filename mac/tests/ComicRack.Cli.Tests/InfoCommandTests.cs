using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using ComicRack.Cli.Fixtures;
using ComicRack.Mac.Host.Platform;
using SharpCompress.Archives;
using Xunit;

namespace ComicRack.Cli.Tests
{
	/// <summary>
	/// P2.4b: `crcli info show` and `info write` on CBZ, CB7 and CBT. The write goes through ComicBook.WriteInfoToFile and the pass B
	/// SevenZipEngine (the bundled 7zz); the result is read back through the Core (`info show`) and through SharpCompress (the
	/// ComicInfo.xml entry), and the page entries must be byte for byte the same. Tests that need the 7zz return quietly on a machine
	/// where mac/native/fetch-7zz.sh has not been run (as ToolLocatorTests does).
	/// </summary>
	public class InfoCommandTests : IDisposable
	{
		private const string Series = "Éléphant & Café – 日本";
		private const string Summary = "Crème brûlée, naïve ça va.\nSecond line: ünïcödé <b> \"quotes\"";

		private readonly string temp = Path.Combine(Path.GetTempPath(), "crcli-info-" + Guid.NewGuid().ToString("N"));

		public InfoCommandTests()
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

		private static (int code, string output, string error) Crcli(params string[] args)
		{
			var o = new StringWriter();
			var e = new StringWriter();
			int code = Program.Run(args, o, e);
			return (code, o.ToString(), e.ToString());
		}

		private string MakeCbz(int pages = 4)
		{
			var env = new FixtureEnvironment { FixturesRoot = Path.Combine(temp, "fixtures"), BudgetBytes = FixtureEnvironment.DefaultBudgetBytes, PrintDiskFree = false, SevenZip = null };
			string dir = Path.Combine(env.FixturesRoot, "info");
			Assert.Equal(0, FixtureCommands.Run(new[] { "make", dir, "--books", "1", "--pages", pages.ToString() }, new StringWriter(), new StringWriter(), env));
			return Directory.GetFiles(dir, "*.cbz").Single();
		}

		/// <summary>A book in the format: the CBZ itself, a CB7 made by the located 7zz, or a CBT made by the system tar.</summary>
		private string MakeBook(string extension)
		{
			string cbz = MakeCbz();
			if (extension == ".cbz")
			{
				return cbz;
			}
			string work = Path.Combine(temp, "pages-" + extension.TrimStart('.'));
			ZipFile.ExtractToDirectory(cbz, work);
			string names = string.Join(" ", Directory.GetFiles(work).Select(f => Path.GetFileName(f)).OrderBy(n => n, StringComparer.Ordinal));
			string book = Path.Combine(temp, "book" + extension);
			if (extension == ".cb7")
			{
				Run(ToolLocator.SevenZip, work, "a", "-t7z", "-mx=0", book, "*");
			}
			else
			{
				var psi = new ProcessStartInfo("/usr/bin/tar") { WorkingDirectory = work, UseShellExecute = false };
				psi.Environment["COPYFILE_DISABLE"] = "1";
				psi.ArgumentList.Add("-cf");
				psi.ArgumentList.Add(book);
				foreach (string n in names.Split(' '))
				{
					psi.ArgumentList.Add(n);
				}
				using (var p = Process.Start(psi))
				{
					p.WaitForExit();
					Assert.Equal(0, p.ExitCode);
				}
			}
			return book;
		}

		private static void Run(string exe, string workDir, params string[] args)
		{
			var psi = new ProcessStartInfo(exe) { WorkingDirectory = workDir, UseShellExecute = false, RedirectStandardOutput = true };
			foreach (string a in args)
			{
				psi.ArgumentList.Add(a);
			}
			using (var p = Process.Start(psi))
			{
				p.StandardOutput.ReadToEnd();
				p.WaitForExit();
				Assert.Equal(0, p.ExitCode);
			}
		}

		/// <summary>Entry name -> SHA-256 of its bytes, read with SharpCompress, for every entry except the two information files.</summary>
		private static Dictionary<string, string> PageHashes(string book)
		{
			var result = new Dictionary<string, string>();
			using (var archive = ArchiveFactory.OpenArchive(book))
			{
				foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
				{
					string name = entry.Key;
					if (name.Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase) || name.Equals("ComicBook.xml", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					using (var s = entry.OpenEntryStream())
					using (var ms = new MemoryStream())
					{
						s.CopyTo(ms);
						result[name] = Convert.ToHexString(SHA256.HashData(ms.ToArray()));
					}
				}
			}
			return result;
		}

		private static XDocument ComicInfoEntry(string book)
		{
			using (var archive = ArchiveFactory.OpenArchive(book))
			{
				var entry = archive.Entries.Single(e => string.Equals(e.Key, "ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
				using (var s = entry.OpenEntryStream())
				{
					return XDocument.Load(s);
				}
			}
		}

		[Theory]
		[InlineData(".cbz")]
		[InlineData(".cb7")]
		[InlineData(".cbt")]
		public void Writing_the_information_keeps_the_pages_and_reads_back_through_the_Core_and_SharpCompress(string extension)
		{
			if (ToolLocator.SevenZip == null)
			{
				return; // mac/native/fetch-7zz.sh has not been run on this machine
			}
			string book = MakeBook(extension);
			var pagesBefore = PageHashes(book);
			Assert.Equal(4, pagesBefore.Count);
			DateTime old = new DateTime(2020, 5, 17, 10, 0, 0, DateTimeKind.Utc);
			File.SetLastWriteTimeUtc(book, old);

			var before = Crcli("info", "show", book);
			Assert.True(before.code == 0, before.output + before.error);
			Assert.DoesNotContain("Series:", before.output);

			var written = Crcli("info", "write", book, "--set", "Series=" + Series, "--set", "Number=12.5", "--set", "Summary=" + Summary, "--set", "Year=1987");
			Assert.True(written.code == 0, written.output + written.error);
			Assert.Contains("written: yes", written.output);

			// Through the Core: a fresh host reads the entry back.
			var after = Crcli("info", "show", book);
			Assert.True(after.code == 0, after.output + after.error);
			Assert.Contains("Series: " + Series, after.output);
			Assert.Contains("Number: 12.5", after.output);
			Assert.Contains("Year: 1987", after.output);
			Assert.Contains("Summary: " + Summary.Replace("\n", "\\n"), after.output);
			Assert.Contains("pages in file: 4", after.output);

			// Through SharpCompress: the ComicInfo.xml entry.
			XElement root = ComicInfoEntry(book).Root;
			Assert.Equal(Series, (string)root.Element("Series"));
			Assert.Equal("12.5", (string)root.Element("Number"));
			Assert.Equal("1987", (string)root.Element("Year"));
			Assert.Equal(Summary.Replace("\r", ""), ((string)root.Element("Summary")).Replace("\r", ""));

			// The page entries are untouched and no other entry appeared.
			Assert.Equal(pagesBefore, PageHashes(book));

			// The file was rewritten: its time moved on.
			Assert.True(File.GetLastWriteTimeUtc(book) > old.AddYears(1), "the modification time was not updated: " + File.GetLastWriteTimeUtc(book));

			// A second write replaces the values (one ComicInfo.xml entry only).
			var again = Crcli("info", "write", book, "--set", "Series=Second");
			Assert.True(again.code == 0, again.output + again.error);
			Assert.Equal("Second", (string)ComicInfoEntry(book).Root.Element("Series"));
			Assert.Equal(pagesBefore, PageHashes(book));
		}

		[Fact]
		public void A_format_that_cannot_be_written_reports_it_and_exits_with_1()
		{
			string pdf = Path.Combine(temp, "t.pdf");
			Assert.Equal(0, Program.Run(new[] { "fixtures", "pdf", pdf, "--pages", "2" }, new StringWriter(), new StringWriter()));
			byte[] bytes = File.ReadAllBytes(pdf);
			var r = Crcli("info", "write", pdf, "--set", "Series=X");
			Assert.Equal(1, r.code);
			Assert.Contains("written: no", r.output);
			Assert.Equal(bytes, File.ReadAllBytes(pdf));
		}

		[Fact]
		public void A_book_with_the_cbr_extension_is_never_written_because_the_Core_does_not_enable_updates_for_RAR()
		{
			// FileFormat("eComic (RAR)") has no EnableUpdate: ComicProvider.StoreInfo returns false before any 7zz call (DD10: no RAR creation).
			string book = Path.Combine(temp, "rar.cbr");
			File.Copy(MakeCbz(), book);
			byte[] bytes = File.ReadAllBytes(book);
			var r = Crcli("info", "write", book, "--set", "Series=X");
			Assert.Equal(1, r.code);
			Assert.Contains("written: no", r.output);
			Assert.Contains("eComic (RAR)", r.error);
			Assert.Equal(bytes, File.ReadAllBytes(book));
		}

		[Fact]
		public void A_failing_7zz_run_reports_its_error_text_and_leaves_the_book_as_it_was()
		{
			if (ToolLocator.SevenZip == null)
			{
				return; // mac/native/fetch-7zz.sh has not been run on this machine
			}
			string folder = Path.Combine(temp, "locked");
			Directory.CreateDirectory(folder);
			string book = Path.Combine(folder, "a.cbz");
			File.Copy(MakeCbz(), book);
			byte[] bytes = File.ReadAllBytes(book);
			// 7zz writes its work file next to the archive: a folder that cannot be written makes the update fail with an error message.
			File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
			try
			{
				var r = Crcli("info", "write", book, "--set", "Series=X");
				Assert.Equal(1, r.code);
				Assert.Contains("written: no", r.output);
				Assert.Contains("write error: ", r.error);
				Assert.Equal(bytes, File.ReadAllBytes(book));
			}
			finally
			{
				File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
			}
		}

		[Fact]
		public void Show_prints_the_format_and_nothing_for_a_book_without_information()
		{
			string book = MakeCbz();
			var r = Crcli("info", "show", book);
			Assert.Equal(0, r.code);
			Assert.Contains("pages in file: 4", r.output);
			Assert.DoesNotContain("Series:", r.output);
		}

		[Fact]
		public void Bad_arguments_exit_with_2_and_change_nothing()
		{
			string book = MakeCbz();
			byte[] bytes = File.ReadAllBytes(book);
			Assert.Equal(2, Crcli("info").code);
			Assert.Equal(2, Crcli("info", "write", book).code);
			var unknown = Crcli("info", "write", book, "--set", "NoSuchField=1");
			Assert.Equal(2, unknown.code);
			Assert.Contains("Unknown field", unknown.error);
			Assert.Equal(2, Crcli("info", "write", book, "--set", "Year=abc").code);
			Assert.Equal(2, Crcli("info", "write", book, "--set", "Series").code);
			Assert.Equal(2, Crcli("info", "show", book, "--set", "Series=x").code);
			Assert.Equal(2, Crcli("info", "show", Path.Combine(temp, "missing.cbz")).code);
			Assert.Equal(bytes, File.ReadAllBytes(book));
		}
	}
}
