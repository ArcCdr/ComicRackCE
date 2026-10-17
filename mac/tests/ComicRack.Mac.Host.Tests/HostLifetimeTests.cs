using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using cYo.Common.Threading;
using Xunit;

namespace ComicRack.Mac.Host.Tests
{
	public class HostLifetimeTests
	{
		// Threads that the Core starts once per process from a static constructor and never stops. They do not belong to a host.
		// MemoryOptimizedImage: started by the first page or thumbnail image of the process (MemoryOptimizedImage.cs:336).
		private static readonly HashSet<string> ProcessWideThreads = new HashSet<string> { "Free Image Memory" };

		/// <summary>Names of the Core worker threads (ThreadUtility.CreateWorkerThread: every queue, the scanner) that are alive, without the process-wide ones.</summary>
		private static List<string> CoreThreads()
		{
			return ThreadUtility.ActiveThreads.Select(t => t.Name ?? "(unnamed)").Where(n => !ProcessWideThreads.Contains(n)).OrderBy(n => n).ToList();
		}

		private static void WaitForNoCoreThreads(int timeoutMs = 10000)
		{
			var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (CoreThreads().Count > 0 && DateTime.UtcNow < end)
			{
				Thread.Sleep(20);
			}
		}

		[Fact]
		public void Open_and_close_twice_on_an_empty_folder_leaves_no_threads()
		{
			using (var temp = new TempFolder())
			{
				WaitForNoCoreThreads();
				Assert.Empty(CoreThreads());
				for (int round = 1; round <= 2; round++)
				{
					MacHost host = MacHost.Open(new MacHostOptions { Paths = temp.Paths });
					Assert.Equal(round == 1, host.IsNewDatabase);
					Assert.Null(host.OpenMessage);
					Assert.Empty(host.DatabaseManager.Database.Books);
					Assert.NotNull(host.CacheManager);
					Assert.NotNull(host.QueueManager);
					Assert.Same(host.CacheManager.ImagePool, host.ImagePool);

					// Make the Core start worker threads, so that the check after Dispose means something: a dirty database
					// is written by the "Save Database Queue" thread, a scan runs on the "Book Scanner" thread.
					host.DatabaseManager.Database.IsDirty = true;
					host.DatabaseManager.SaveInBackground();
					string books = Path.Combine(temp.Path, "books");
					Directory.CreateDirectory(books);
					host.QueueManager.Scanner.ScanFileOrFolder(books, true, false);
					Assert.Contains("Save Database Queue", CoreThreads());

					host.Dispose();
					host.Dispose(); // the second call does nothing
					Assert.True(host.IsDisposed);
					WaitForNoCoreThreads();
					Assert.Empty(CoreThreads());
					Assert.True(File.Exists(temp.Paths.DatabaseFile), "the database is written when the host closes");
				}
			}
		}

		[Fact]
		public void A_second_host_cannot_be_opened_while_one_is_open()
		{
			using (var temp = new TempFolder())
			using (var temp2 = new TempFolder())
			{
				using (MacHost.Open(new MacHostOptions { Paths = temp.Paths }))
				{
					Assert.Throws<InvalidOperationException>(() => MacHost.Open(new MacHostOptions { Paths = temp2.Paths }));
				}
				using (MacHost.Open(new MacHostOptions { Paths = temp2.Paths }))
				{
				}
			}
		}

		[Fact]
		public void The_log_file_records_open_and_close()
		{
			using (var temp = new TempFolder())
			{
				MacHost.Open(new MacHostOptions { Paths = temp.Paths }).Dispose();
				string log = File.ReadAllText(temp.Paths.LogFile);
				Assert.Contains("Opening host.", log);
				Assert.Contains("Host open.", log);
				Assert.Contains("Host closed.", log);
				Assert.DoesNotContain("[ERROR]", log);
			}
		}

		[Fact]
		public void Folders_are_created_where_MacPaths_says()
		{
			using (var temp = new TempFolder())
			{
				using (MacHost.Open(new MacHostOptions { Paths = temp.Paths }))
				{
					Assert.True(Directory.Exists(temp.Paths.DataFolder));
					Assert.True(Directory.Exists(temp.Paths.CacheFolder));
					Assert.True(Directory.Exists(temp.Paths.LogFolder));
				}
			}
		}

		[Fact]
		public void The_core_paths_use_the_explicit_database_and_cache_folders()
		{
			using (var temp = new TempFolder())
			using (var host = MacHost.Open(new MacHostOptions { Paths = temp.Paths }))
			{
				Assert.Equal(Path.Combine(temp.Paths.DataFolder, "ComicDb"), host.SystemPaths.DatabasePath);
				Assert.Equal(Path.Combine(temp.Paths.CacheFolder, "Thumbnails"), host.SystemPaths.ThumbnailCachePath);
				Assert.Equal(Path.Combine(temp.Paths.CacheFolder, "Images"), host.SystemPaths.ImageCachePath);
				Assert.Equal(Path.Combine(temp.Paths.CacheFolder, "Files"), host.SystemPaths.FileCachePath);
			}
		}

		[Fact]
		public void Default_paths_are_the_MVP_folders_in_the_user_library()
		{
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			MacPaths paths = MacPaths.ForCurrentUser();
			Assert.Equal(Path.Combine(home, "Library", "Application Support", "ComicRack CE (MVP)"), paths.DataFolder);
			Assert.Equal(Path.Combine(home, "Library", "Caches", "ComicRack CE (MVP)"), paths.CacheFolder);
			Assert.Equal(Path.Combine(home, "Library", "Logs", "ComicRack CE (MVP)"), paths.LogFolder);
		}

		[Fact]
		public void A_read_only_host_does_not_touch_the_database_file()
		{
			using (var temp = new TempFolder())
			{
				MacHost.Open(new MacHostOptions { Paths = temp.Paths }).Dispose();
				string file = temp.Paths.DatabaseFile;
				byte[] before = File.ReadAllBytes(file);
				var old = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);

				File.SetLastWriteTimeUtc(file, old);
				using (var host = MacHost.Open(new MacHostOptions { Paths = temp.Paths, DatabaseReadOnly = true }))
				{
					host.DatabaseManager.Database.IsDirty = true;
				}
				Assert.Equal(old, File.GetLastWriteTimeUtc(file));
				Assert.Equal(before, File.ReadAllBytes(file));

				// the same open without the option writes the file
				File.SetLastWriteTimeUtc(file, old);
				MacHost.Open(new MacHostOptions { Paths = temp.Paths }).Dispose();
				Assert.NotEqual(old, File.GetLastWriteTimeUtc(file));
			}
		}

		[Fact]
		public void A_database_file_outside_the_data_folder_can_be_opened()
		{
			using (var temp = new TempFolder())
			using (var other = new TempFolder())
			{
				string file = Path.Combine(other.Path, "MyBooks.xml");
				MacHost.Open(new MacHostOptions { Paths = temp.Paths, DatabaseFile = file }).Dispose();
				Assert.True(File.Exists(file));
				Assert.False(File.Exists(temp.Paths.DatabaseFile));
				Assert.Throws<ArgumentException>(() => MacHost.Open(new MacHostOptions { Paths = temp.Paths, DatabaseFile = Path.Combine(other.Path, "MyBooks.txt") }));
			}
		}

		[Fact]
		public void A_corrupt_database_is_reported_and_replaced_by_an_empty_one()
		{
			using (var temp = new TempFolder())
			{
				temp.Paths.CreateFolders();
				File.WriteAllText(temp.Paths.DatabaseFile, "this is not xml");
				using (var host = MacHost.Open(new MacHostOptions { Paths = temp.Paths, DatabaseReadOnly = true }))
				{
					Assert.False(host.IsNewDatabase);
					Assert.NotNull(host.OpenMessage);
					Assert.Empty(host.DatabaseManager.Database.Books);
				}
			}
		}
	}
}
