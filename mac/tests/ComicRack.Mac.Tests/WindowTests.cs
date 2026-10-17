using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Services;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Mac.Tests
{
	/// <summary>
	/// The windows on the headless platform with the Skia renderer. Frames are checked for content and, when the environment variable
	/// CRMAC_FRAMES_DIR names a folder, saved there as PNG so that they can be looked at. The native menu bar and the real
	/// StorageProvider pickers cannot be tested this way.
	/// </summary>
	public class WindowTests
	{
		private readonly ITestOutputHelper output;

		public WindowTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		private static WriteableBitmap Capture(Window window, string name)
		{
			Dispatcher.UIThread.RunJobs();
			AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			WriteableBitmap frame = window.CaptureRenderedFrame();
			Assert.NotNull(frame);
			string dir = Environment.GetEnvironmentVariable("CRMAC_FRAMES_DIR");
			if (!string.IsNullOrEmpty(dir))
			{
				Directory.CreateDirectory(dir);
				frame.Save(Path.Combine(dir, name + ".png"));
			}
			return frame;
		}

		/// <summary>Number of distinct pixel values in the frame (a blank window has 1 or 2, text and controls give many).</summary>
		private static int DistinctColors(WriteableBitmap frame)
		{
			using (var buffer = frame.Lock())
			{
				var seen = new HashSet<uint>();
				int w = buffer.Size.Width, h = buffer.Size.Height;
				unsafe
				{
					for (int y = 0; y < h; y += 2)
					{
						uint* row = (uint*)((byte*)buffer.Address + y * buffer.RowBytes);
						for (int x = 0; x < w; x += 2)
						{
							seen.Add(row[x]);
						}
					}
				}
				return seen.Count;
			}
		}

		private static (MainWindow window, MainViewModel vm) NewMainWindow()
		{
			var service = new HostService(AppOptions.Parse(new[] { "--data-dir", Path.Combine(Path.GetTempPath(), "crmac-never-started") }));
			var vm = new MainViewModel(new NullShell(), service, new FakeDialogs());
			var window = new MainWindow { DataContext = vm };
			return (window, vm);
		}

		[Fact]
		public async Task The_main_window_shows_the_library_and_the_scan_progress()
		{
			await Ui.Run(async () =>
			{
				var (window, vm) = NewMainWindow();
				window.Show();

				WriteableBitmap starting = Capture(window, "main-1-starting");
				int startingColors = DistinctColors(starting);
				Assert.False(window.FindControl<Border>("ProgressPanel").IsVisible);
				Assert.Equal("Starting the library engine...", window.FindControl<TextBlock>("BookCountLabel").Text);

				vm.BookCountText = "14,706 books in the library";
				vm.StatusText = "Library engine ready. 14706 books.";
				vm.HostText = "Library opened";
				Capture(window, "main-2-library");
				Assert.Equal("14,706 books in the library", window.FindControl<TextBlock>("BookCountLabel").Text);

				vm.ProgressTitle = "Scanning BD...";
				vm.ProgressCountText = "6,512 files, 6,498 books added";
				vm.ProgressFileText = "/Users/vince/Comics/BD/Lester Cockney/Lester Cockney - 08 - Oregon Trail.cbz";
				vm.CanCancel = true;
				vm.IsProgressVisible = true;
				WriteableBitmap scanning = Capture(window, "main-3-scanning");
				Assert.True(window.FindControl<Border>("ProgressPanel").IsVisible);
				Assert.Equal("6,512 files, 6,498 books added", window.FindControl<TextBlock>("ProgressCountLabel").Text);
				Assert.Equal("Scanning BD...", window.FindControl<TextBlock>("ProgressTitleLabel").Text);
				var cancel = window.FindControl<Button>("CancelButton");
				Assert.True(cancel.IsEffectivelyEnabled);

				// The frame shows text and controls: far more than a blank window, and the progress panel adds more.
				int scanningColors = DistinctColors(scanning);
				output.WriteLine($"distinct colours: starting {startingColors}, scanning {scanningColors}; frame {scanning.PixelSize.Width} x {scanning.PixelSize.Height}");
				Assert.True(startingColors > 20, "the first frame looks blank");
				Assert.True(scanningColors > startingColors);

				vm.CanCancel = false;
				Dispatcher.UIThread.RunJobs();
				Assert.False(cancel.IsEffectivelyEnabled, "Cancel must be disabled once it was pressed");

				vm.IsProgressVisible = false;
				Dispatcher.UIThread.RunJobs();
				Assert.False(window.FindControl<Border>("ProgressPanel").IsVisible);
				window.Close();
				await Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Pressing_Cancel_in_the_window_cancels_the_running_scan()
		{
			using (new RealLibraryGuard())
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					string books = temp.Combine("books");
					Synthetic.CreateCbzFolder(books, 200);
					using (var launch = await Launch.Start(temp.Combine("app")))
					{
						var window = new MainWindow { DataContext = launch.ViewModel };
						window.Show();
						var cancel = window.FindControl<Button>("CancelButton");
						var scan = launch.ViewModel.ScanFolderAsync(books);
						// Wait until the panel is up and some books are in, then press the button the way a click does.
						var deadline = DateTime.UtcNow.AddSeconds(30);
						while (launch.ViewModel.BookCount < 5 && !scan.IsCompleted && DateTime.UtcNow < deadline)
						{
							await Task.Delay(5);
						}
						if (!scan.IsCompleted)
						{
							Assert.True(window.FindControl<Border>("ProgressPanel").IsVisible);
							Capture(window, "main-4-scanning-live");
							Assert.True(cancel.IsEffectivelyEnabled);
							cancel.Command.Execute(cancel.CommandParameter);
						}
						var result = await scan;
						output.WriteLine($"outcome {result.Outcome}, {result.BooksAdded} of 200 books");
						// 200 tiny files can finish before the click lands; if they did, the window must be back to idle all the same.
						Assert.False(window.FindControl<Border>("ProgressPanel").IsVisible);
						Assert.True(result.Outcome == ScanOutcome.Cancelled ? result.BooksAdded < 200 : result.BooksAdded == 200);
						window.Close();
					}
				});
			}
		}

		[Fact]
		public async Task The_import_window_validates_returns_a_request_and_can_be_cancelled()
		{
			using (var temp = new TempDir())
			{
				await Ui.Run(async () =>
				{
					string db = temp.Combine("ComicDb.xml");
					File.WriteAllText(db, Synthetic.WindowsDb(2));
					var owner = new MainWindow { DataContext = NewMainWindow().vm };
					owner.Show();

					// OK with an empty form: an error, the window stays open.
					var dialog = new ImportWindow();
					Task<ImportRequest> pending = dialog.ShowDialog<ImportRequest>(owner);
					Dispatcher.UIThread.RunJobs();
					Capture(dialog, "import-1-empty");
					dialog.FindControl<Button>("ImportButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
					Dispatcher.UIThread.RunJobs();
					Assert.False(pending.IsCompleted);
					Assert.Contains("Choose the ComicDb.xml", dialog.FindControl<TextBlock>("ErrorLabel").Text);

					// A bad mapping: an error naming the line.
					dialog.ViewModel.DatabasePath = db;
					dialog.ViewModel.MappingsText = "S:\\media\\BD\\ is where it was";
					dialog.FindControl<Button>("ImportButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
					Dispatcher.UIThread.RunJobs();
					Assert.False(pending.IsCompleted);
					Assert.Contains("mapping", dialog.FindControl<TextBlock>("ErrorLabel").Text);
					Capture(dialog, "import-2-error");

					// Good input: the dialog closes with the request.
					dialog.ViewModel.MappingsText = "S:\\media\\BD\\=/Users/me/BD/\n# comment\nT:\\x=/y";
					dialog.FindControl<Button>("ImportButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
					ImportRequest request = await pending;
					Assert.NotNull(request);
					Assert.Equal(db, request.DatabasePath);
					Assert.Equal(2, request.Mappings.Count);
					Assert.Equal("/Users/me/BD/Series 01/Book 001.cbz", request.Mappings[0].Map(@"S:\media\BD\Series 01\Book 001.cbz"));

					// Cancel returns null.
					var second = new ImportWindow();
					Task<ImportRequest> pending2 = second.ShowDialog<ImportRequest>(owner);
					Dispatcher.UIThread.RunJobs();
					second.FindControl<Button>("CancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
					Assert.Null(await pending2);
					owner.Close();
				});
			}
		}

		[Fact]
		public void The_import_form_checks_its_fields()
		{
			using (var temp = new TempDir())
			{
				string db = temp.Combine("ComicDb.xml");
				File.WriteAllText(db, Synthetic.WindowsDb(1));
				var vm = new ImportViewModel();

				Assert.Null(vm.TryCreateRequest());
				Assert.Contains("Choose the ComicDb.xml", vm.ErrorText);

				vm.DatabasePath = temp.Combine("missing.xml");
				Assert.Null(vm.TryCreateRequest());
				Assert.Contains("does not exist", vm.ErrorText);

				vm.DatabasePath = "\"" + db + "\"  "; // pasted from Finder with quotes
				vm.MappingsText = "=/nothing";
				Assert.Null(vm.TryCreateRequest());
				Assert.Contains("mapping", vm.ErrorText);

				vm.MappingsText = string.Empty;
				ImportRequest r = vm.TryCreateRequest();
				Assert.NotNull(r);
				Assert.Equal(db, r.DatabasePath);
				Assert.Empty(r.Mappings);
				Assert.Equal(string.Empty, vm.ErrorText);
			}
		}

		[Fact]
		public async Task The_message_window_shows_title_and_text()
		{
			await Ui.Run(async () =>
			{
				var owner = new MainWindow { DataContext = NewMainWindow().vm };
				owner.Show();
				var message = new MessageWindow("The library was not saved", "ComicRack could not write the library file: the disk is full.");
				Task closed = message.ShowDialog(owner);
				Dispatcher.UIThread.RunJobs();
				Capture(message, "message-1");
				Assert.Equal("The library was not saved", message.FindControl<TextBlock>("TitleLabel").Text);
				message.FindControl<Button>("OkButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
				await closed;
				owner.Close();
			});
		}
	}
}
