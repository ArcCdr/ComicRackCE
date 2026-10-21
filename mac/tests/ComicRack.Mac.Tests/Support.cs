using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using Xunit;

namespace ComicRack.Mac.Tests
{
	/// <summary>The headless Avalonia platform with the Skia renderer, one per process. Tests run on its UI thread through <see cref="Run{T}"/>.</summary>
	public static class Ui
	{
		private static readonly Lazy<HeadlessUnitTestSession> session = new Lazy<HeadlessUnitTestSession>(() => HeadlessUnitTestSession.StartNew(typeof(TestApp)));

		/// <summary>
		/// The session gives every job a fresh application (isolation per test): before the job it resets the Avalonia dispatcher
		/// and sets the application up on the session thread, and the first thread that touches Dispatcher.UIThread after the
		/// reset becomes its owner. A thread that an earlier test left running (a worker posting to the UI thread) can win that
		/// race by a few milliseconds, and the setup then fails with "a different thread owns it" before the job has started.
		/// That failure has no effect on the job, so the job is simply offered again.
		/// </summary>
		public static async Task<T> Run<T>(Func<Task<T>> work)
		{
			for (int attempt = 1; ; attempt++)
			{
				bool started = false;
				try
				{
					Func<Task<T>> job = () =>
					{
						started = true;
						return work();
					};
					return await session.Value.Dispatch(job, CancellationToken.None);
				}
				catch (InvalidOperationException ex) when (!started && attempt < 10 && ex.Message.Contains("different thread owns"))
				{
					await Task.Delay(25);
				}
			}
		}

		public static Task Run(Func<Task> work)
		{
			return Run(async () =>
			{
				await work();
				return 0;
			});
		}
	}

	public static class TestApp
	{
		public static AppBuilder BuildAvaloniaApp()
		{
			return AppBuilder.Configure<App>()
				.UseSkia()
				.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
		}
	}

	/// <summary>A new folder under the system temp folder, deleted on dispose. Tests keep everything in here, never in ~/Library.</summary>
	public sealed class TempDir : IDisposable
	{
		public string Path { get; }

		public TempDir()
		{
			Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "crmac-app-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path);
		}

		public string Combine(string name) => System.IO.Path.Combine(Path, name);

		public void Dispose()
		{
			try
			{
				Directory.Delete(Path, recursive: true);
			}
			catch (Exception)
			{
			}
		}
	}

	/// <summary>The generated fixtures under mac/fixtures (git-ignored): skipped when they are not there.</summary>
	public static class Fixtures
	{
		public static readonly string Root = FindRoot();

		public static string BD => System.IO.Path.Combine(Root ?? "", "BD");

		public static string BDReader => System.IO.Path.Combine(Root ?? "", "BD-reader");

		public static string ComicDb => System.IO.Path.Combine(Root ?? "", "ComicDb.xml");

		private static string FindRoot()
		{
			string dir = AppContext.BaseDirectory;
			while (!string.IsNullOrEmpty(dir))
			{
				if (File.Exists(System.IO.Path.Combine(dir, "ComicRack.Mac.slnx")))
				{
					string fixtures = System.IO.Path.Combine(dir, "fixtures");
					return Directory.Exists(fixtures) ? fixtures : null;
				}
				dir = System.IO.Path.GetDirectoryName(dir);
			}
			return null;
		}

		/// <summary>Why a fixture test is skipped, or null.</summary>
		public static string SkipReason(params string[] needed)
		{
			if (Root == null)
			{
				return "mac/fixtures does not exist (generate it with `crcli fixtures from-db`, see mac/README.md).";
			}
			foreach (string n in needed)
			{
				string p = System.IO.Path.Combine(Root, n);
				if (!Directory.Exists(p) && !File.Exists(p))
				{
					return "mac/fixtures/" + n + " does not exist.";
				}
			}
			return null;
		}
	}

	/// <summary>A fact that needs the generated fixtures; it is reported as skipped when they are missing.</summary>
	public sealed class FixtureFactAttribute : FactAttribute
	{
		public FixtureFactAttribute(params string[] needed)
		{
			Skip = Fixtures.SkipReason(needed);
		}
	}

	/// <summary>Fails the test if the real MVP folders in ~/Library changed while the test ran.</summary>
	public sealed class RealLibraryGuard : IDisposable
	{
		private readonly (string path, bool exists, DateTime time)[] before;

		public RealLibraryGuard()
		{
			before = Snapshot();
		}

		private static (string path, bool exists, DateTime time)[] Snapshot()
		{
			string library = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library");
			return new[]
			{
				System.IO.Path.Combine(library, "Application Support", "ComicRack CE (MVP)"),
				System.IO.Path.Combine(library, "Application Support", "ComicRack CE (MVP)", "ComicDb.xml"),
				System.IO.Path.Combine(library, "Caches", "ComicRack CE (MVP)"),
				System.IO.Path.Combine(library, "Logs", "ComicRack CE (MVP)"),
			}.Select(p => (path: p, exists: Directory.Exists(p) || File.Exists(p), time: Directory.Exists(p) ? Directory.GetLastWriteTimeUtc(p) : File.GetLastWriteTimeUtc(p))).ToArray();
		}

		public void Dispose()
		{
			var after = Snapshot();
			for (int i = 0; i < before.Length; i++)
			{
				Assert.True(before[i].exists == after[i].exists && before[i].time == after[i].time, "The test touched the real MVP folder " + before[i].path);
			}
		}
	}

	public static class Hashes
	{
		public static string Sha256(string file)
		{
			using (var stream = File.OpenRead(file))
			{
				return Convert.ToHexString(SHA256.HashData(stream));
			}
		}
	}

	/// <summary>An <see cref="IShell"/> that does nothing.</summary>
	public sealed class NullShell : IShell
	{
		public void ShowAbout()
		{
		}

		public void Minimize()
		{
		}

		public void Zoom()
		{
		}

		public void ToggleFullScreen()
		{
		}

		/// <summary>The files that Reveal in Finder was asked for.</summary>
		public System.Collections.Generic.List<string> Revealed { get; } = new System.Collections.Generic.List<string>();

		/// <summary>The texts that were put on the clipboard.</summary>
		public System.Collections.Generic.List<string> Copied { get; } = new System.Collections.Generic.List<string>();

		public int Activations { get; private set; }

		public int Quits { get; private set; }

		public void RevealInFinder(string path) => Revealed.Add(path);

		public Task CopyTextAsync(string text)
		{
			Copied.Add(text);
			return Task.CompletedTask;
		}

		public void ActivateLibrary() => Activations++;

		public void Quit() => Quits++;
	}

	/// <summary>Scripted dialogs: what the user would pick, and a record of what was shown.</summary>
	public sealed class FakeDialogs : ILibraryDialogs
	{
		public string FolderToPick { get; set; }

		public ImportRequest ImportToReturn { get; set; }

		public int FolderPrompts { get; private set; }

		public int ImportPrompts { get; private set; }

		public System.Collections.Generic.List<(string Title, string Message)> Messages { get; } = new System.Collections.Generic.List<(string, string)>();

		/// <summary>The folder the last folder prompt asked to start in, or null.</summary>
		public string LastStartFolder { get; private set; }

		public Task<string> PickFolderAsync(string title, string startFolder = null)
		{
			FolderPrompts++;
			LastStartFolder = startFolder;
			return Task.FromResult(FolderToPick);
		}

		public Task<ImportRequest> AskImportAsync()
		{
			ImportPrompts++;
			return Task.FromResult(ImportToReturn);
		}

		public Task ShowMessageAsync(string title, string message)
		{
			Messages.Add((title, message));
			return Task.CompletedTask;
		}

		/// <summary>The questions that were asked, in order.</summary>
		public System.Collections.Generic.List<QuestionRequest> Questions { get; } = new System.Collections.Generic.List<QuestionRequest>();

		/// <summary>The answers to give to the next questions, in order; OK with nothing checked when it is empty.</summary>
		public System.Collections.Generic.Queue<QuestionAnswer> Answers { get; } = new System.Collections.Generic.Queue<QuestionAnswer>();

		public Task<QuestionAnswer> AskQuestionAsync(QuestionRequest request)
		{
			Questions.Add(request);
			return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : QuestionAnswer.Ok);
		}

		/// <summary>The Quick Rating dialogs that were shown.</summary>
		public System.Collections.Generic.List<QuickRatingRequest> QuickRatings { get; } = new System.Collections.Generic.List<QuickRatingRequest>();

		/// <summary>What the Quick Rating dialog returns (null: Cancel).</summary>
		public QuickRatingResult QuickRatingToReturn { get; set; }

		public Task<QuickRatingResult> AskQuickRatingAsync(QuickRatingRequest request)
		{
			QuickRatings.Add(request);
			return Task.FromResult(QuickRatingToReturn);
		}

		/// <summary>The Info dialogs that were opened, in order.</summary>
		public System.Collections.Generic.List<BookInfoRequest> BookInfos { get; } = new System.Collections.Generic.List<BookInfoRequest>();

		/// <summary>What a test does while the Info dialog is "open" (it edits the model, then answers OK or Cancel); the default answers Cancel at once.</summary>
		public Func<BookInfoRequest, Task<bool>> BookInfoHandler { get; set; }

		public Task<bool> ShowBookInfoAsync(BookInfoRequest request)
		{
			BookInfos.Add(request);
			return BookInfoHandler != null ? BookInfoHandler(request) : Task.FromResult(false);
		}
	}
}
