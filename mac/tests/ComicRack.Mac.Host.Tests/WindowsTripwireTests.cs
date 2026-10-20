using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Cli.Fixtures;
using ComicRack.Mac.Host.Library;
using cYo.Common.Drawing;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using cYo.Projects.ComicRack.Engine.IO;
using cYo.Projects.ComicRack.Engine.IO.Cache;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using Xunit;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>
	/// The Windows P/Invoke tripwire (P1.2): a Core path that calls kernel32, user32, shell32, ... is logged once with its managed
	/// stack and counted by <see cref="NativeLibraryResolver.WindowsCalls"/>. The v1 paths must leave the counter at zero.
	/// </summary>
	public class WindowsTripwireTests
	{
		[Theory]
		[InlineData("kernel32.dll", "kernel32")]
		[InlineData("KERNEL32", "kernel32")]
		[InlineData("User32.dll", "user32")]
		[InlineData("shell32.dll", "shell32")]
		[InlineData("gdi32", "gdi32")]
		[InlineData("shlwapi.dll", "shlwapi")]
		[InlineData("ole32.dll", "ole32")]
		[InlineData("uxtheme.dll", "uxtheme")]
		[InlineData("dwmapi.dll", "dwmapi")]
		[InlineData("credui.dll", "credui")]
		[InlineData("C:\\Windows\\System32\\user32.dll", "user32")]
		[InlineData("libSystem", null)]
		[InlineData("pdfium", null)]
		[InlineData("libheif", null)]
		[InlineData("", null)]
		[InlineData(null, null)]
		public void Windows_library_names_are_recognised(string name, string module)
		{
			Assert.Equal(module, NativeLibraryResolver.WindowsModuleOf(name));
		}

		[Fact]
		public void The_resolver_is_installed_on_every_Core_assembly()
		{
			string[] names = NativeLibraryResolver.CoreAssemblies().Select(a => a.GetName().Name).OrderBy(n => n).ToArray();
			Assert.Equal(new[] { "ComicRack.Engine.Core", "ComicRack.Plugins.Core", "cYo.Common.Core", "cYo.Compat.Drawing", "cYo.Compat.Forms" }.OrderBy(n => n).ToArray(), names);
		}

		[Fact]
		public void A_call_into_cYo_Common_Win32_is_counted_and_logged_once_with_its_stack()
		{
			using (var temp = new TempFolder())
			using (var host = MacHost.Open(new MacHostOptions { Paths = temp.Paths }))
			{
				NativeLibraryResolver.ResetWindowsCalls();
				Assert.Equal(0, NativeLibraryResolver.WindowsCalls);

				// Keyboard imports user32!MapVirtualKey (cYo.Common/Win32/Keyboard.cs). There is no user32 here, so the call throws, as it
				// does in the Core paths that swallow the exception.
				for (int i = 0; i < 2; i++)
				{
					Assert.ThrowsAny<Exception>(() => cYo.Common.Win32.Keyboard.GetLocalizedKeyName(System.Windows.Forms.Keys.A));
				}

				Assert.True(NativeLibraryResolver.WindowsCalls >= 2, "calls: " + NativeLibraryResolver.WindowsCalls);
				NativeLibraryResolver.WindowsCall call = Assert.Single(NativeLibraryResolver.WindowsCallRecords, c => c.Entry == "MapVirtualKey");
				Assert.Equal("user32", call.Module);
				Assert.True(call.Count >= 2);
				Assert.Contains("cYo.Common.Win32.Keyboard", call.Stack);

				string log = File.ReadAllText(temp.Paths.LogFile);
				string line = "Windows API user32!MapVirtualKey called from";
				int first = log.IndexOf(line, StringComparison.Ordinal);
				Assert.True(first >= 0, log);
				Assert.Equal(first, log.LastIndexOf(line, StringComparison.Ordinal)); // once, although called twice
				Assert.Contains("[WARN]", log);
				Assert.Contains("cYo.Common.Win32.Keyboard", log.Substring(first));
			}
			NativeLibraryResolver.ResetWindowsCalls();
		}

		[Fact]
		public async Task The_v1_paths_make_no_Windows_calls()
		{
			using (var temp = new TempFolder())
			{
				// Books of `crcli fixtures make`.
				var env = new FixtureEnvironment { FixturesRoot = Path.Combine(temp.Path, "fixtures"), PrintDiskFree = false, SevenZip = null };
				string books = Path.Combine(env.FixturesRoot, "small");
				var output = new StringWriter();
				Assert.Equal(0, FixtureCommands.Run(new[] { "make", books, "--books", "4", "--pages", "4" }, output, output, env));
				string[] files = Directory.GetFiles(books, "*.cbz");
				Assert.Equal(4, files.Length);

				NativeLibraryResolver.ResetWindowsCalls();
				using (var host = MacHost.Open(new MacHostOptions { Paths = temp.Paths }))
				{
					// Scan.
					ScanResult scan = await new FolderScanner(host).ScanAsync(books, null, CancellationToken.None);
					Assert.Equal(ScanOutcome.Completed, scan.Outcome);
					Assert.Equal(4, host.DatabaseManager.Database.Books.Count);
					ComicBook book = host.DatabaseManager.Database.Books.First();

					// Thumbnail.
					ThumbnailKey thumbKey = book.GetFrontCoverThumbnailKey();
					using (IImageProvider provider = book.OpenProvider(thumbKey.Index))
					using (var thumb = host.ImagePool.GetThumbnail(thumbKey, provider, onErrorThrowException: true))
					{
						Assert.True(thumb.Item.Width > 0);
					}

					// Page.
					PageKey pageKey = book.GetPageKey(1, BitmapAdjustment.Empty);
					using (IImageProvider provider = book.OpenProvider(pageKey.Index))
					using (var page = host.ImagePool.GetPage(pageKey, provider, onErrorThrowException: true))
					{
						Assert.True(page.Item.Width > 0);
					}

					// Smart lists: one by series, one with a group, evaluated the way the sidebar does.
					ComicDatabase db = host.DatabaseManager.Database;
					var smart = new ComicSmartListItem("Fixtures");
					smart.Matchers.Clear();
					smart.Matchers.Add(typeof(ComicBookSeriesMatcher), ComicBookStringMatcher.OperatorContains, string.Empty, string.Empty);
					db.ComicLists.Add(smart);
					var evaluator = new ListEvaluator();
					ListResult result = evaluator.Evaluate(smart);
					Assert.Null(result.Error);
					foreach (ListNode node in LibraryLists.BuildTree(db).SelectMany(n => n.Flatten()))
					{
						Assert.Null(evaluator.Evaluate(node.Item).Error);
					}

					// Save.
					SaveVerification saved = host.SaveDatabase();
					Assert.True(saved.Ok, saved.ToString());
				} // close

				// Open again: the saved database is loaded.
				using (var host = MacHost.Open(new MacHostOptions { Paths = temp.Paths }))
				{
					Assert.Equal(4, host.DatabaseManager.Database.Books.Count);
				}

				string log = File.ReadAllText(temp.Paths.LogFile);
				var unknown = NativeLibraryResolver.WindowsCallRecords.ToList();
				Assert.True(unknown.Count == 0, "Windows calls on the v1 path:\n" + string.Join("\n", unknown.Select(c => c + "\n" + c.Stack)));
				Assert.DoesNotContain("Windows API ", log);
				Assert.Equal(0, NativeLibraryResolver.WindowsCalls);
			}
		}
	}
}
