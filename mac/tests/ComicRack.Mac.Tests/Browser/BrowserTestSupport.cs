using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Services;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using Xunit;

namespace ComicRack.Mac.Tests.Browser
{
	/// <summary>Library-sized lists of synthetic items, made in memory (the CLI has no `db synth` yet, M1.5c is deferred).</summary>
	public static class SyntheticItems
	{
		private static readonly string[] Words =
		{
			"Lune", "Étoile", "Dragon", "Cité", "Rivière", "Ombre", "Forêt", "Océan", "Héros", "Légende", "Château", "Désert", "Vent",
			"Feu", "Glace", "Nuit", "Soleil", "Voyage", "Secret", "Empire", "Pirates", "Mystère", "Âme", "Épée", "Tempête", "Jardin",
			"Montagne", "Île", "Sorcier", "Fantôme", "Cirque", "Train", "Ville", "Tour", "Cœur", "Mer", "Roi", "Reine", "Chevalier", "Loup",
		};

		/// <summary>
		/// <paramref name="count"/> items without a Core book: about 33 books per series, 5% without a series, 5% without a number,
		/// French words with accents, added dates spread over ten years. The same seed gives the same list.
		/// </summary>
		public static BookItem[] Make(int count, int seed = 42)
		{
			var random = new Random(seed);
			int seriesCount = Math.Max(1, count / 33);
			string[] series = new string[seriesCount];
			for (int i = 0; i < series.Length; i++)
			{
				series[i] = Words[random.Next(Words.Length)] + " " + Words[random.Next(Words.Length)] + " " + (i + 1);
			}
			var items = new BookItem[count];
			DateTime start = new DateTime(2016, 1, 1);
			for (int i = 0; i < count; i++)
			{
				string s = random.Next(20) == 0 ? null : series[random.Next(seriesCount)];
				string n = random.Next(20) == 0 ? null : (1 + random.Next(60)).ToString();
				string title = Words[random.Next(Words.Length)] + " " + Words[random.Next(Words.Length)].ToLowerInvariant() + " " + random.Next(1000);
				items[i] = new BookItem(null, title, s, n, start.AddMinutes(random.Next(10 * 365 * 24 * 60)));
			}
			return items;
		}
	}

	/// <summary>A window with the grid in it, driven by a view model, on the headless Skia platform.</summary>
	public sealed class GridHarness : IDisposable
	{
		public GridHarness(double width = 1100, double height = 720)
		{
			Model = new BrowserViewModel { SearchDelay = TimeSpan.Zero };
			Grid = new BookGridView { DataContext = Model };
			Window = new Window { Width = width, Height = height, Content = Grid };
			Window.Show();
			Layout();
		}

		public Window Window { get; }

		public BookGridView Grid { get; }

		public BrowserViewModel Model { get; }

		/// <summary>Runs queued jobs and layout passes until the repeater has realized its cells.</summary>
		public void Layout()
		{
			for (int i = 0; i < 4; i++)
			{
				Dispatcher.UIThread.RunJobs();
				Window.UpdateLayout();
				AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			}
		}

		public async Task Show(BookCatalog catalog)
		{
			Model.SetCatalog(catalog);
			await Model.WhenIdle();
			Layout();
		}

		public void ScrollToRow(int row)
		{
			Grid.Scroller.Offset = new Avalonia.Vector(0, Grid.OffsetOfRow(row));
			Layout();
		}

		public WriteableBitmap Capture(string name)
		{
			Layout();
			WriteableBitmap frame = Window.CaptureRenderedFrame();
			Assert.NotNull(frame);
			string dir = Environment.GetEnvironmentVariable("CRMAC_FRAMES_DIR");
			if (!string.IsNullOrEmpty(dir))
			{
				Directory.CreateDirectory(dir);
				frame.Save(Path.Combine(dir, name + ".png"));
			}
			return frame;
		}

		public void Dispose()
		{
			Window.Close();
		}
	}

	/// <summary>The 14,706-book fixture library imported into a private data folder, with the app wiring of <see cref="Launch"/>.</summary>
	public static class FixtureLibrary
	{
		public static async Task<Launch> Open14k(TempDir temp)
		{
			Launch launch = await Launch.Start(temp.Combine("app"));
			var request = new ImportRequest(Fixtures.ComicDb, new[] { new PathRemapRule(@"S:\media\BD\", Fixtures.BD + "/") });
			await launch.ViewModel.ImportLibraryAsync(request);
			await WaitForGrid(launch.ViewModel, 14706);
			return launch;
		}

		/// <summary>Waits until the browser shows all <paramref name="total"/> books (the catalog is built on a worker thread after open and import).</summary>
		public static async Task WaitForGrid(MainViewModel vm, int total, int timeoutMs = 60000)
		{
			var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (!(vm.Browser.TotalCount == total && vm.Browser.Items.Length == total && !vm.Browser.IsLoading))
			{
				if (DateTime.UtcNow > deadline)
				{
					throw new TimeoutException($"The grid shows {vm.Browser.Items.Length} of {total} books (catalog {vm.Browser.TotalCount}, loading {vm.Browser.IsLoading}).");
				}
				await Task.Delay(10);
			}
			await vm.Browser.WhenIdle();
		}
	}
}
