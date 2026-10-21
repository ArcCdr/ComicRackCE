using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>Settings.xml (P5.1): round trip, damaged files, atomic and debounced saving. Nothing here uses a clock: the scheduler is released by hand.</summary>
	public class SettingsStoreTests : IDisposable
	{
		private readonly TempFolder temp = new TempFolder();

		public void Dispose() => temp.Dispose();

		private string SettingsFile => Path.Combine(temp.Path, "Data", SettingsStore.FileName);

		/// <summary>A scheduler that holds the action until the test runs it.</summary>
		private sealed class ManualScheduler : ISaveScheduler
		{
			public int Scheduled { get; private set; }

			public int Cancelled { get; private set; }

			private Action pending;

			public bool HasPending => pending != null;

			public IDisposable Schedule(TimeSpan delay, Action action)
			{
				Scheduled++;
				pending = action;
				return new Handle(this, action);
			}

			public void RunPending()
			{
				Action action = pending;
				pending = null;
				action?.Invoke();
			}

			private sealed class Handle : IDisposable
			{
				private readonly ManualScheduler owner;
				private readonly Action action;

				public Handle(ManualScheduler owner, Action action)
				{
					this.owner = owner;
					this.action = action;
				}

				public void Dispose()
				{
					if (ReferenceEquals(owner.pending, action))
					{
						owner.pending = null;
						owner.Cancelled++;
					}
				}
			}
		}

		[Fact]
		public void A_missing_file_gives_the_defaults_and_no_note()
		{
			using (SettingsStore store = SettingsStore.Open(SettingsFile, new ManualScheduler()))
			{
				Assert.Null(store.LoadNote);
				Assert.True(store.Current.ShowSplash);
				Assert.Equal(252, store.Current.SidebarWidth);
				Assert.Null(store.Current.LibraryWindow);
				Assert.False(File.Exists(SettingsFile));
			}
		}

		[Fact]
		public void Everything_that_is_kept_survives_a_save_and_a_load()
		{
			var written = new AppPreferences
			{
				ShowSplash = false,
				OpenLastPage = false,
				AutoShowQuickReview = true,
				TrueRightToLeftReading = true,
				ShowToolTips = true,
				CloseMinimizesToTray = false,
				ThumbCacheSizeMB = 123,
				MemoryPageCacheCount = 40,
				UpdateComicFiles = true,
				CultureName = "de",
				NavigationOverlayOnTop = true,
				LibraryWindow = new WindowFrame(-1440, 25, 1100.5, 720, maximized: true),
				ReaderWindow = new WindowFrame(100, 80, 900, 1000),
				SidebarWidth = 300,
				SidebarVisible = false,
				ThumbnailSize = 144.5,
				SortBy = "Title",
				ReaderFit = "FitWidth",
				ReaderLayout = "DoubleAdaptive",
				RightToLeftDefault = true,
				LastFolder = "/tmp/some comics/ünïcode & <more>",
				TrackCurrentPage = false,
				LastOpenBooks = new[] { "/books/one.cbz", "/books/ünï & two.cbz" },
				BrowserDockHeight = 321.5,
				BrowserDockExpanded = false,
				ReaderUndocked = true,
				MoveFilesToRecycleBin = true,
				AlsoRemoveFromLibrary = true,
				AlsoRemoveFromLibraryFiltered = true,
				RemoveFilesfromDatabase = true,
				HiddenMessageBoxes = HiddenMessageBoxes.AskClearData | HiddenMessageBoxes.RemoveFromList,
				TabLayouts = TabLayouts.Multiple | TabLayouts.Export,
			};
			SettingsStore.Save(SettingsFile, written);

			AppPreferences read = SettingsStore.Load(SettingsFile, out string note);
			Assert.Null(note);
			foreach (var property in typeof(AppPreferences).GetProperties().Where(p => p.GetIndexParameters().Length == 0))
			{
				object expected = property.GetValue(written);
				object actual = property.GetValue(read);
				bool same = expected is System.Array a && actual is System.Array b ? a.Cast<object>().SequenceEqual(b.Cast<object>()) : Equals(expected, actual);
				Assert.True(same, property.Name + ": wrote " + expected + ", read " + actual);
			}
			Assert.Equal(new[] { "/books/one.cbz", "/books/ünï & two.cbz" }, read.LastOpenBooks);
			Assert.True(new WindowFrame(-1440, 25, 1100.5, 720, true).Equals(read.LibraryWindow));
		}

		[Fact]
		public void A_value_that_is_the_default_is_not_written_so_a_later_default_reaches_the_file()
		{
			SettingsStore.Save(SettingsFile, new AppPreferences { ShowToolTips = true });
			string xml = File.ReadAllText(SettingsFile);
			Assert.Contains("<ShowToolTips>true</ShowToolTips>", xml);
			Assert.DoesNotContain("ShowSplash", xml);
			Assert.DoesNotContain("SidebarWidth", xml);
			Assert.DoesNotContain("LibraryWindow", xml);
			// the tab and dock state is not written while it is the default either
			Assert.DoesNotContain("LastOpenBooks", xml);
			Assert.DoesNotContain("BrowserDock", xml);
			Assert.DoesNotContain("ReaderUndocked", xml);
			Assert.DoesNotContain("TrackCurrentPage", xml);
			Assert.DoesNotContain("TabLayouts", xml);
		}

		[Fact]
		public void The_clamps_of_the_original_apply_to_a_value_read_from_the_file()
		{
			Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
			File.WriteAllText(SettingsFile, "<?xml version=\"1.0\"?><Settings><MemoryPageCacheCount>5000</MemoryPageCacheCount><MemoryThumbCacheSizeMB>1</MemoryThumbCacheSizeMB><Unknown>x</Unknown></Settings>");
			AppPreferences read = SettingsStore.Load(SettingsFile, out string note);
			Assert.Null(note);
			Assert.Equal(100, read.MemoryPageCacheCount);
			Assert.Equal(5, read.MemoryThumbCacheSizeMB);
		}

		[Theory]
		[InlineData("")]
		[InlineData("this is not xml")]
		[InlineData("<?xml version=\"1.0\"?><Settings><ShowSplash>perhaps</ShowSplash></Settings>")]
		[InlineData("<?xml version=\"1.0\"?><Other><ShowSplash>false</ShowSplash></Other>")]
		[InlineData("<?xml version=\"1.0\"?><Settings><ShowSplash>false</ShowSplash>")]
		public void A_damaged_file_gives_the_defaults_keeps_a_bad_copy_and_is_replaced_by_the_next_save(string content)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
			File.WriteAllText(SettingsFile, content);
			var scheduler = new ManualScheduler();
			using (SettingsStore store = SettingsStore.Open(SettingsFile, scheduler))
			{
				Assert.NotNull(store.LoadNote);
				Assert.Contains("defaults are used", store.LoadNote);
				Assert.True(store.Current.ShowSplash);
				Assert.True(File.Exists(SettingsFile + ".bad"));
				Assert.Equal(content, File.ReadAllText(SettingsFile + ".bad"));
				Assert.True(store.IsDirty); // the defaults replace the damaged file without waiting for a change

				store.Current.ShowSplash = false;
				store.Flush();
			}
			AppPreferences after = SettingsStore.Load(SettingsFile, out string note);
			Assert.Null(note);
			Assert.False(after.ShowSplash);
			Assert.Equal(content, File.ReadAllText(SettingsFile + ".bad"));
		}

		[Fact]
		public void A_damaged_file_is_replaced_by_the_defaults_at_the_first_flush_even_if_nothing_changes()
		{
			Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
			File.WriteAllText(SettingsFile, "<Settings>");
			var scheduler = new ManualScheduler();
			using (SettingsStore store = SettingsStore.Open(SettingsFile, scheduler))
			{
				Assert.True(scheduler.HasPending);
				scheduler.RunPending();
			}
			AppPreferences after = SettingsStore.Load(SettingsFile, out string note);
			Assert.Null(note);
			Assert.True(after.ShowSplash);
			Assert.Equal("<Settings>", File.ReadAllText(SettingsFile + ".bad"));
		}

		[Fact]
		public void A_second_damaged_file_replaces_the_older_bad_copy()
		{
			Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
			File.WriteAllText(SettingsFile, "first");
			SettingsStore.Load(SettingsFile, out _);
			File.WriteAllText(SettingsFile, "second");
			SettingsStore.Load(SettingsFile, out _);
			Assert.Equal("second", File.ReadAllText(SettingsFile + ".bad"));
		}

		[Fact]
		public void Changes_are_saved_once_after_the_quiet_time_and_not_before()
		{
			var scheduler = new ManualScheduler();
			using (SettingsStore store = SettingsStore.Open(SettingsFile, scheduler))
			{
				store.Current.SidebarWidth = 280;
				store.Current.SidebarWidth = 290;
				store.Current.SidebarWidth = 300;
				Assert.Equal(3, scheduler.Scheduled);
				Assert.Equal(2, scheduler.Cancelled); // each change replaced the pending save
				Assert.False(File.Exists(SettingsFile));
				Assert.True(store.IsDirty);

				scheduler.RunPending();
				Assert.Equal(1, store.SaveCount);
				Assert.False(store.IsDirty);
				Assert.Equal(300, SettingsStore.Load(SettingsFile, out _).SidebarWidth);

				store.Current.SidebarWidth = 300; // unchanged: nothing to save
				Assert.False(scheduler.HasPending);
			}
		}

		[Fact]
		public void Flush_writes_what_is_pending_at_once_and_a_dispose_does_too()
		{
			var scheduler = new ManualScheduler();
			SettingsStore store = SettingsStore.Open(SettingsFile, scheduler);
			store.Current.ThumbnailSize = 150;
			Assert.False(File.Exists(SettingsFile));
			store.Flush();
			Assert.Equal(1, store.SaveCount);
			Assert.Equal(150, SettingsStore.Load(SettingsFile, out _).ThumbnailSize);
			store.Flush(); // nothing changed since
			Assert.Equal(1, store.SaveCount);

			store.Current.SortBy = "Title";
			store.Dispose();
			Assert.Equal("Title", SettingsStore.Load(SettingsFile, out _).SortBy);
			store.Current.SortBy = "Series"; // after dispose nobody saves
			Assert.False(scheduler.HasPending);
		}

		[Fact]
		public void A_save_writes_a_temporary_file_and_renames_it_so_nothing_else_is_left_and_the_old_content_is_gone()
		{
			SettingsStore.Save(SettingsFile, new AppPreferences { ShowSplash = false, LastFolder = "/old/folder" });
			string old = File.ReadAllText(SettingsFile);
			Assert.Contains("ShowSplash", old); // a value that differs from its default is written
			Assert.Contains("/old/folder", old);
			SettingsStore.Save(SettingsFile, new AppPreferences { ShowSplash = true, OpenInNewTab = true });
			string[] files = Directory.GetFiles(Path.GetDirectoryName(SettingsFile));
			Assert.Equal(new[] { SettingsStore.FileName }, files.Select(Path.GetFileName).ToArray());
			// the file is the new state, not the old one with the new values added: the old values are not in it any more
			string text = File.ReadAllText(SettingsFile);
			Assert.DoesNotContain("ShowSplash", text); // back to its default, so no longer written
			Assert.DoesNotContain("/old/folder", text);
			Assert.Contains("OpenInNewTab", text);
			AppPreferences loaded = SettingsStore.Load(SettingsFile, out _);
			Assert.True(loaded.OpenInNewTab);
			Assert.True(loaded.ShowSplash);
			Assert.Null(loaded.LastFolder);
		}

		[Fact]
		public void A_failed_save_is_reported_kept_for_the_next_try_and_leaves_the_old_file()
		{
			SettingsStore.Save(SettingsFile, new AppPreferences { OpenInNewTab = true });
			var messages = new List<string>();
			var scheduler = new ManualScheduler();
			using (SettingsStore store = SettingsStore.Open(SettingsFile, scheduler, messages.Add))
			{
				store.Current.ShowSplash = false;
				// the target is a folder now: the rename over it fails
				File.Delete(SettingsFile);
				Directory.CreateDirectory(SettingsFile);
				File.WriteAllText(Path.Combine(SettingsFile, "keep"), "x");
				scheduler.RunPending();
				Assert.NotNull(store.LastSaveError);
				Assert.Single(messages);
				Assert.True(store.IsDirty);
				Assert.Equal(new[] { SettingsStore.FileName }, Directory.GetFileSystemEntries(Path.GetDirectoryName(SettingsFile)).Select(Path.GetFileName).ToArray());

				Directory.Delete(SettingsFile, recursive: true);
				store.Flush();
				Assert.Null(store.LastSaveError);
				Assert.False(SettingsStore.Load(SettingsFile, out _).ShowSplash);
			}
		}

		private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15); // only bounds a hang: every wait below is released by the test itself

		private static Thread Start(Action action)
		{
			var thread = new Thread(() => action()) { IsBackground = true };
			thread.Start();
			return thread;
		}

		[Fact]
		public void A_file_that_cannot_be_read_is_never_written_over()
		{
			Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
			const string content = "<?xml version=\"1.0\"?><Settings><ShowSplash>false</ShowSplash><SidebarWidth>333</SidebarWidth></Settings>";
			File.WriteAllText(SettingsFile, content);
			UnixFileMode mode = File.GetUnixFileMode(SettingsFile);
			File.SetUnixFileMode(SettingsFile, UnixFileMode.None);
			try
			{
				bool readable;
				try
				{
					using (File.OpenRead(SettingsFile))
					{
					}
					readable = true; // running as root: permissions do not apply
				}
				catch (UnauthorizedAccessException)
				{
					readable = false;
				}
				if (readable)
				{
					return;
				}
				var scheduler = new ManualScheduler();
				using (SettingsStore store = SettingsStore.Open(SettingsFile, scheduler))
				{
					Assert.NotNull(store.LoadNote);
					Assert.True(store.Current.ShowSplash); // the defaults
					Assert.False(File.Exists(SettingsFile + ".bad")); // not known to be damaged: no copy
					Assert.False(scheduler.HasPending); // and no save to replace it
					store.Current.SidebarWidth = 280; // a later change must not write either
					store.Flush();
					Assert.False(scheduler.HasPending);
					Assert.Equal(0, store.SaveCount);
				}
			}
			finally
			{
				File.SetUnixFileMode(SettingsFile, mode);
			}
			Assert.Equal(content, File.ReadAllText(SettingsFile));
			Assert.Equal(new[] { SettingsStore.FileName }, Directory.GetFileSystemEntries(Path.GetDirectoryName(SettingsFile)).Select(Path.GetFileName).ToArray());
		}

		[Fact]
		public void A_damaged_file_whose_copy_failed_is_left_as_it_is()
		{
			Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
			File.WriteAllText(SettingsFile, "<Settings>");
			Directory.CreateDirectory(SettingsFile + ".bad"); // the copy cannot be made over a folder
			var scheduler = new ManualScheduler();
			using (SettingsStore store = SettingsStore.Open(SettingsFile, scheduler))
			{
				Assert.NotNull(store.LoadNote);
				Assert.Contains("failed", store.LoadNote);
				Assert.False(scheduler.HasPending);
				store.Current.ShowSplash = false;
				store.Flush();
				Assert.False(scheduler.HasPending);
				Assert.Equal(0, store.SaveCount);
			}
			Assert.Equal("<Settings>", File.ReadAllText(SettingsFile));
		}

		[Fact]
		public void A_setting_changes_while_the_file_is_being_written_without_waiting_for_the_disk()
		{
			var entered = new ManualResetEventSlim();
			var release = new ManualResetEventSlim();
			var written = new List<byte[]>();
			Action<string, byte[]> blocked = (path, content) =>
			{
				entered.Set();
				Assert.True(release.Wait(Deadline));
				written.Add(content);
				File.WriteAllBytes(path, content);
			};
			var scheduler = new ManualScheduler();
			using (SettingsStore store = SettingsStore.Open(SettingsFile, scheduler, null, blocked))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
				store.Current.SidebarWidth = 280;
				Thread saving = Start(() => scheduler.RunPending()); // the writer is now parked on the event
				Assert.True(entered.Wait(Deadline));

				bool dirtyAfter = false;
				Thread change = Start(() =>
				{
					store.Current.SidebarWidth = 300;
					store.Current.ThumbnailSize = 150;
					dirtyAfter = store.IsDirty;
				});
				Assert.True(change.Join(Deadline), "a setter waited for the file write");
				Assert.True(dirtyAfter);
				Assert.True(saving.IsAlive); // the write is still in progress

				release.Set();
				Assert.True(saving.Join(Deadline));
				Assert.Single(written);
				Assert.Equal(280, SettingsStore.Load(SettingsFile, out _).SidebarWidth); // the snapshot taken before the change
				Assert.True(store.IsDirty);

				store.Flush();
				Assert.Equal(2, written.Count);
				AppPreferences after = SettingsStore.Load(SettingsFile, out _);
				Assert.Equal(300, after.SidebarWidth);
				Assert.Equal(150, after.ThumbnailSize);
			}
		}

		[Fact]
		public void An_older_snapshot_never_replaces_a_newer_one()
		{
			var firstTaken = new ManualResetEventSlim();
			var releaseFirst = new ManualResetEventSlim();
			var written = new List<long>();
			var scheduler = new ManualScheduler();
			using (SettingsStore store = SettingsStore.Open(SettingsFile, scheduler, null, (path, content) =>
			{
				written.Add(content.Length);
				File.WriteAllBytes(path, content);
			}))
			{
				store.AfterSnapshot = sequence =>
				{
					if (sequence == 1)
					{
						firstTaken.Set(); // the first snapshot is taken but has not reached the file
						Assert.True(releaseFirst.Wait(Deadline));
					}
				};
				Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
				store.Current.SidebarWidth = 280;
				Thread older = Start(() => store.Flush());
				Assert.True(firstTaken.Wait(Deadline));

				store.Current.SidebarWidth = 300; // a newer state, saved completely before the older one gets to write
				store.Flush();
				Assert.Equal(300, SettingsStore.Load(SettingsFile, out _).SidebarWidth);
				Assert.Equal(1, store.SaveCount);

				releaseFirst.Set();
				Assert.True(older.Join(Deadline));
				Assert.Equal(300, SettingsStore.Load(SettingsFile, out _).SidebarWidth); // the older snapshot was dropped
				Assert.Equal(1, store.SaveCount);
				Assert.Single(written);
				Assert.False(store.IsDirty);
			}
		}

		[Fact]
		public void The_core_view_and_the_preferences_are_the_same_values()
		{
			var preferences = new AppPreferences();
			var settings = new MacSettings(preferences);
			int raised = 0;
			settings.CacheSettingsChanged += (s, e) => raised++;
			preferences.ThumbCacheSizeMB = 250;
			Assert.Equal(250, settings.ThumbCacheSizeMB);
			settings.PageCacheEnabled = false;
			Assert.False(preferences.PageCacheEnabled);
			Assert.Equal(2, raised);
			preferences.ShowSplash = false; // not a cache setting: the Core is not told
			Assert.Equal(2, raised);
			settings.UpdateComicFiles = true;
			Assert.True(preferences.UpdateComicFiles);
			Assert.Equal(2, raised);
		}
	}
}
