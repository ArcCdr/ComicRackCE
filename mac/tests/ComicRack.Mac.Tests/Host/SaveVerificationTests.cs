using System;
using System.IO;
using System.Threading.Tasks;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using Xunit;

namespace ComicRack.Mac.Tests.Host
{
	public class SaveVerificationTests
	{
		private static string Db(TempDir temp, int books, bool cutOff = false)
		{
			string xml = Synthetic.WindowsDb(books, decoy: false);
			if (cutOff)
			{
				xml = xml.Substring(0, xml.Length / 2);
			}
			string path = temp.Combine("ComicDb.xml");
			File.WriteAllText(path, xml);
			return path;
		}

		[Fact]
		public void A_fresh_complete_file_with_the_right_count_is_ok()
		{
			using (var temp = new TempDir())
			{
				DateTime before = DateTime.UtcNow.AddSeconds(-1);
				string file = Db(temp, 10);
				var v = SaveVerification.Check(file, before, 11); // 10 + the one outside the prefix
				Assert.True(v.Ok, v.ToString());
				Assert.Equal(11, v.BooksInFile);
				Assert.True(v.Length > 0);
			}
		}

		[Fact]
		public void A_file_that_was_not_written_is_detected()
		{
			using (var temp = new TempDir())
			{
				string file = Db(temp, 3);
				var v = SaveVerification.Check(file, DateTime.UtcNow.AddMinutes(1), 4);
				Assert.False(v.Ok);
				Assert.Contains("was not written", v.Problem);
			}
		}

		[Fact]
		public void A_missing_empty_cut_off_or_wrong_count_file_is_detected()
		{
			using (var temp = new TempDir())
			{
				DateTime before = DateTime.UtcNow.AddSeconds(-1);
				Assert.Contains("does not exist", SaveVerification.Check(temp.Combine("none.xml"), before, 1).Problem);

				string empty = temp.Combine("empty.xml");
				File.WriteAllText(empty, "");
				Assert.Contains("empty", SaveVerification.Check(empty, before, 1).Problem);

				string cut = Db(temp, 50, cutOff: true);
				var vCut = SaveVerification.Check(cut, before, 51);
				Assert.False(vCut.Ok);
				Assert.Contains("could not be read back", vCut.Problem);

				string full = Db(temp, 5);
				var vCount = SaveVerification.Check(full, before, 99);
				Assert.False(vCount.Ok);
				Assert.Contains("holds 6 books, expected 99", vCount.Problem);

				// Without an expected count the content is not counted.
				Assert.True(SaveVerification.Check(full, before, -1).Ok);
			}
		}

		[Fact]
		public async Task The_host_saves_and_checks_the_file()
		{
			using (var temp = new TempDir())
			{
				string books = temp.Combine("books");
				Synthetic.CreateCbzFolder(books, 6);
				MacHost host = MacHost.Open(new MacHostOptions { Paths = MacPaths.Under(temp.Combine("h")), DatabaseBackgroundSaveSeconds = 0 });
				try
				{
					await new FolderScanner(host).ScanAsync(books, null, default);
					SaveVerification v = host.SaveDatabase();
					Assert.True(v.Ok, v.ToString());
					Assert.Equal(6, v.BooksInFile);
					Assert.Equal(host.DatabaseFile, v.File);
				}
				finally
				{
					SaveVerification closed = host.DisposeAndVerify();
					Assert.True(closed.Ok, closed.ToString());
					Assert.Equal(6, closed.BooksInFile);
					Assert.Null(closed.CloseError);
				}
			}
		}

		[Fact]
		public async Task A_save_the_core_swallowed_is_reported_as_failed()
		{
			using (var temp = new TempDir())
			{
				string books = temp.Combine("books");
				Synthetic.CreateCbzFolder(books, 4);
				var paths = MacPaths.Under(temp.Combine("h"));
				MacHost host = MacHost.Open(new MacHostOptions { Paths = paths, DatabaseBackgroundSaveSeconds = 0 });
				try
				{
					Assert.True(host.SaveDatabase().Ok);
					await new FolderScanner(host).ScanAsync(books, null, default);

					// The Core copies its .bak over the database file; a read-only file makes that fail, and DatabaseManager.Save swallows it.
					File.SetUnixFileMode(host.DatabaseFile, UnixFileMode.UserRead);
					SaveVerification v = host.SaveDatabase();
					Assert.False(v.Ok, "the save should have failed: " + v);
					Assert.NotNull(v.Problem);

					SaveVerification closed = host.DisposeAndVerify();
					Assert.False(closed.Ok, closed.ToString());
					Assert.Equal(0, SaveVerification.CountBooks(host.DatabaseFile)); // the file on disk is still the first (empty) save
				}
				finally
				{
					if (File.Exists(paths.DatabaseFile))
					{
						File.SetUnixFileMode(paths.DatabaseFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
					}
					host.Dispose();
				}
			}
		}

		[Fact]
		public void A_read_only_host_skips_the_save()
		{
			using (var temp = new TempDir())
			{
				string file = Db(temp, 2);
				MacHost host = MacHost.Open(new MacHostOptions { Paths = MacPaths.Under(temp.Combine("h")), DatabaseFile = file, DatabaseReadOnly = true });
				var v = host.SaveDatabase();
				Assert.True(v.Ok);
				Assert.True(v.Skipped);
				Assert.True(host.DisposeAndVerify().Skipped);
			}
		}
	}
}
