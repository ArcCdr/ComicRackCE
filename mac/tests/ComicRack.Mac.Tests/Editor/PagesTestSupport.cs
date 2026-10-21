using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Host;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Editor
{
	/// <summary>One page of a test book: the name in the archive, the size and the colour of its picture.</summary>
	public readonly struct TestPage
	{
		public TestPage(string name, int width, int height, SKColor color)
		{
			Name = name;
			Width = width;
			Height = height;
			Color = color;
		}

		public string Name { get; }

		public int Width { get; }

		public int Height { get; }

		public SKColor Color { get; }
	}

	/// <summary>
	/// A host on private folders with a CBZ made by the test (pages are solid colours with a dark upper left quarter, so a rotation or a merge can be seen), the real thumbnail service
	/// and a stand-in for the UI thread. For the Pages and Colors tabs of the Info dialog and the page list model.
	/// </summary>
	public sealed class PagesRig : IDisposable
	{
		private readonly TempDir temp = new TempDir();
		private readonly RealLibraryGuard guard = new RealLibraryGuard();
		public readonly MacHost Host;
		public readonly FakeUi Ui = new FakeUi();
		public readonly ThumbnailService Thumbnails;
		private readonly bool realUi;

		/// <param name="realUi">The pictures arrive on the Avalonia UI thread (a window test) instead of the stand-in that the test pumps.</param>
		public PagesRig(bool realUi = false)
		{
			this.realUi = realUi;
			Host = MacHost.Open(new MacHostOptions { Paths = MacPaths.Under(temp.Combine("app")), DatabaseReadOnly = true });
			try
			{
				Thumbnails = new ThumbnailService(new ThumbnailServiceOptions { Decoder = new CoreThumbnailDecoder(Host.CacheManager), Post = realUi ? (Action<Action>)(action => UiDispatch.Post(action)) : Ui.Post, Log = message => { } });
			}
			catch (Exception)
			{
				Host.Dispose();
				temp.Dispose();
				guard.Dispose();
				throw;
			}
		}

		public string Folder => temp.Path;

		public static readonly TestPage[] ThreePages =
		{
			new TestPage("p1.png", 60, 90, new SKColor(200, 40, 40)),
			new TestPage("p2.png", 60, 90, new SKColor(40, 200, 40)),
			new TestPage("p10.png", 90, 60, new SKColor(40, 40, 200)),
		};

		/// <summary>Writes a CBZ and returns its path.</summary>
		public string MakeCbz(string name, params TestPage[] pages)
		{
			string path = temp.Combine(name);
			using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
			{
				foreach (TestPage page in pages)
				{
					ZipArchiveEntry entry = zip.CreateEntry(page.Name, CompressionLevel.NoCompression);
					using (Stream stream = entry.Open())
					{
						byte[] png = Png(page);
						stream.Write(png, 0, png.Length);
					}
				}
			}
			return path;
		}

		public static byte[] Png(TestPage page)
		{
			using (var bitmap = new SKBitmap(page.Width, page.Height))
			{
				bitmap.Erase(page.Color);
				using (var canvas = new SKCanvas(bitmap))
				using (var paint = new SKPaint { Color = new SKColor(20, 20, 20) })
				{
					canvas.DrawRect(0, 0, page.Width / 2f, page.Height / 2f, paint);
				}
				using (SKImage image = SKImage.FromBitmap(bitmap))
				using (SKData data = image.Encode(SKEncodedImageFormat.Png, 100))
				{
					return data.ToArray();
				}
			}
		}

		/// <summary>A Core book for a file (not in the library) with its page count read.</summary>
		public ComicBook Book(string file)
		{
			ComicBook book = Host.DatabaseManager.BookFactory.Create(file, CreateBookOption.DoNotAdd, RefreshInfoOptions.GetPageCount);
			Assert.NotNull(book);
			book.RefreshInfoFromFile(RefreshInfoOptions.GetPageCount);
			return book;
		}

		public PageListServices Services => new PageListServices { Thumbnails = Thumbnails, Pool = Host.ImagePool, Post = realUi ? null : Ui.Post };

		/// <summary>The page list of a copy of the book, started, with the file's index read.</summary>
		public PageListModel Open(ComicBook book, int expectedRows)
		{
			var copy = new ComicBook(book);
			ComicBookNavigator navigator = copy.CreateNavigator();
			var model = new PageListModel(copy, navigator, Services, ownsNavigator: true);
			model.Start();
			Ui.PumpUntil(() => model.IsIndexComplete && model.Count == expectedRows, 20000);
			return model;
		}

		public void Dispose()
		{
			try
			{
				Thumbnails.Dispose();
				Ui.Pump();
			}
			finally
			{
				Host.Dispose();
				temp.Dispose();
				guard.Dispose();
			}
		}
	}
}

namespace ComicRack.Mac.Tests.Editor
{
	/// <summary>A source of rows for <see cref="ComicRack.Mac.Controls.CrPageList"/> without a book: texts made from the row and the column, pictures that are solid colours.</summary>
	public sealed class FakePageSource : ComicRack.Mac.Controls.IPageListSource
	{
		private readonly List<int> selection = new List<int>();
		private readonly List<ComicRack.Mac.Controls.PageListColumn> columns = ComicRack.Mac.Editor.PageColumns.Create();
		public int Rows;
		public int Sorted = ComicRack.Mac.Editor.PageColumns.Page;
		public bool Ascending = true;
		public readonly List<(int Row, int Height)> PictureRequests = new List<(int, int)>();
		public bool WithPictures = true;
		public HashSet<int> Bookmarked = new HashSet<int>();
		public HashSet<int> Deleted = new HashSet<int>();

		public FakePageSource(int rows)
		{
			Rows = rows;
		}

		public int Count => Rows;

		public IReadOnlyList<ComicRack.Mac.Controls.PageListColumn> Columns => columns;

		public int SortColumn => Sorted;

		public bool SortAscending => Ascending;

		public string Text(int row, int columnId) => columnId == ComicRack.Mac.Editor.PageColumns.Page ? (row + 1).ToString() : "r" + row + "c" + columnId;

		public ComicRack.Mac.Controls.PageRowState State(int row) => new ComicRack.Mac.Controls.PageRowState((row + 1).ToString(), Deleted.Contains(row), Bookmarked.Contains(row), false);

		public IReadOnlyList<ComicRack.Mac.Controls.TileTextLine> Lines(int row) => ComicRack.Mac.Editor.PageTileText.Build(new ComicPageInfo(row) { ImageWidth = 60, ImageHeight = 90, ImageFileSize = 1000 * (row + 1) }, row);

		public string ToolTip(int row) => "Page #" + (row + 1);

		public ComicRack.Mac.Browser.CoverImage Picture(int row, int height)
		{
			PictureRequests.Add((row, height));
			if (!WithPictures)
			{
				return null;
			}
			using (var bitmap = new SKBitmap(60, 90))
			{
				bitmap.Erase(new SKColor((byte)(40 + row * 37 % 200), (byte)(80 + row * 53 % 150), (byte)(120 + row * 71 % 100)));
				return new ComicRack.Mac.Browser.CoverImage(SKImage.FromBitmap(bitmap));
			}
		}

		public IReadOnlyList<int> Selection => selection;

		public void SetSelection(IEnumerable<int> rows)
		{
			var next = rows.Where(r => r >= 0 && r < Rows).Distinct().OrderBy(r => r).ToList();
			if (!next.SequenceEqual(selection))
			{
				selection.Clear();
				selection.AddRange(next);
				SelectionChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		public event EventHandler Changed;

		public event EventHandler RowsChanged;

		public event EventHandler SelectionChanged;

		public event EventHandler PicturesChanged;

		public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

		public void RaiseRows() => RowsChanged?.Invoke(this, EventArgs.Empty);

		public void RaisePictures() => PicturesChanged?.Invoke(this, EventArgs.Empty);
	}
}
