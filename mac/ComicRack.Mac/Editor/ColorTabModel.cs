using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using ComicRack.Mac.Browser;
using cYo.Common.ComponentModel;
using cYo.Common.Drawing;
using cYo.Common.Threading;
using cYo.Compat.Drawing;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO;
using cYo.Projects.ComicRack.Engine.IO.Cache;
using SkiaSharp;

namespace ComicRack.Mac.Editor
{
	/// <summary>
	/// The Colors tab of the Info dialog without a window (<c>tbSaturation</c>, <c>tbBrightness</c>, <c>tbContrast</c>, <c>tbGamma</c>, <c>tbSharpening</c>, the white point of <c>pageViewer</c> and
	/// the page buttons): the book's colour adjustment as the sliders show it (percent, -100 to 100; sharpening 0 to 3), the page the viewer shows and the buttons that move it. Load fills
	/// it from a book (<c>SetDataToEditor</c>), <see cref="ApplyTo"/> writes it (<c>comic.ColorAdjustment = pageViewer.ColorAdjustment</c>).
	/// </summary>
	public sealed partial class ColorTabModel : ObservableObject
	{
		/// <summary>The range of the four percent sliders.</summary>
		public const int SliderMinimum = -100;

		public const int SliderMaximum = 100;

		/// <summary>The sharpening slider (0 to 3).</summary>
		public const int SharpeningMaximum = 3;

		private ComicBook book;
		private Color whitePoint = Color.White;
		private int pageCount;

		[ObservableProperty]
		public partial int Saturation { get; set; }

		[ObservableProperty]
		public partial int Brightness { get; set; }

		[ObservableProperty]
		public partial int Contrast { get; set; }

		[ObservableProperty]
		public partial int Gamma { get; set; }

		[ObservableProperty]
		public partial int Sharpening { get; set; }

		/// <summary>The page the viewer shows, counting from 0 (-1: none).</summary>
		[ObservableProperty]
		public partial int ViewerPage { get; set; } = -1;

		/// <summary>The colour that becomes white (<c>WhitePointColor</c>); white (or nothing) leaves the colours as they are.</summary>
		public Color WhitePoint
		{
			get => whitePoint;
			set
			{
				if (whitePoint != value)
				{
					whitePoint = value;
					OnPropertyChanged();
					OnPropertyChanged(nameof(Adjustment));
				}
			}
		}

		/// <summary>The adjustment the sliders and the white point describe (<c>SetCurrentColorAdjustment</c>).</summary>
		public BitmapAdjustment Adjustment => new BitmapAdjustment(Saturation / 100f, Brightness / 100f, Contrast / 100f, Gamma / 100f, WhitePoint, BitmapAdjustmentOptions.None, Sharpening);

		/// <summary>The number of pages of the book the buttons move through (<c>displayComic.PageCount</c>).</summary>
		public int PageCount
		{
			get => pageCount;
			private set
			{
				if (pageCount != value)
				{
					pageCount = value;
					OnPropertyChanged();
					RaiseButtons();
				}
			}
		}

		/// <summary>"Page 3" (the shared text <c>Page</c> and the number, <c>SetPageView</c>).</summary>
		public string PageLabel => ViewerPage < 0 ? string.Empty : UiText.Default("Page", "Page") + " " + (ViewerPage + 1);

		public bool CanGoFirst => PageCount > 0 && ViewerPage > 0;

		public bool CanGoPrevious => CanGoFirst;

		public bool CanGoNext => PageCount > 0 && ViewerPage < PageCount - 1;

		public bool CanGoLast => CanGoNext;

		/// <summary>True if there is any adjustment (the Reset button has something to reset).</summary>
		public bool HasAdjustment => !Adjustment.IsEmpty || !WhitePoint.IsBlackOrWhite();

		partial void OnSaturationChanged(int value) => AdjustmentEdited();

		partial void OnBrightnessChanged(int value) => AdjustmentEdited();

		partial void OnContrastChanged(int value) => AdjustmentEdited();

		partial void OnGammaChanged(int value) => AdjustmentEdited();

		partial void OnSharpeningChanged(int value) => AdjustmentEdited();

		partial void OnViewerPageChanged(int value)
		{
			OnPropertyChanged(nameof(PageLabel));
			RaiseButtons();
		}

		private void AdjustmentEdited()
		{
			OnPropertyChanged(nameof(Adjustment));
			OnPropertyChanged(nameof(HasAdjustment));
		}

		private void RaiseButtons()
		{
			OnPropertyChanged(nameof(CanGoFirst));
			OnPropertyChanged(nameof(CanGoPrevious));
			OnPropertyChanged(nameof(CanGoNext));
			OnPropertyChanged(nameof(CanGoLast));
		}

		/// <summary>The tool tip of a slider: its value in percent with a plus sign above zero (<c>AdjustmentSliderChanged</c>).</summary>
		public static string SliderToolTip(int value) => string.Format("{1}{0}%", value, value > 0 ? "+" : string.Empty);

		/// <summary>Fills the sliders from the book's adjustment and shows its current page (<c>SetDataToEditor</c>, <c>SetPageView(comic.CurrentPage)</c>).</summary>
		public void Load(ComicBook comic)
		{
			book = comic ?? throw new ArgumentNullException(nameof(comic));
			BitmapAdjustment adjustment = comic.ColorAdjustment;
			Saturation = (int)(adjustment.Saturation * 100f);
			Brightness = (int)(adjustment.Brightness * 100f);
			Contrast = (int)(adjustment.Contrast * 100f);
			Gamma = (int)(adjustment.Gamma * 100f);
			Sharpening = adjustment.Sharpen;
			WhitePoint = adjustment.WhitePointColor;
			PageCount = comic.PageCount;
			ViewerPage = comic.CurrentPage;
		}

		/// <summary>Writes the adjustment into a book (<c>SaveBook</c>).</summary>
		public void ApplyTo(ComicBook comic)
		{
			comic.ColorAdjustment = Adjustment;
		}

		/// <summary>The Reset button (<c>btReset_Click</c>): every slider at zero and the white point white.</summary>
		public void Reset()
		{
			Saturation = Brightness = Contrast = Gamma = 0;
			Sharpening = 0;
			WhitePoint = Color.White;
		}

		/// <summary>A double click on the page (<c>pageViewer_DoubleClick</c>): the colour under the pointer becomes the white point.</summary>
		public void SetWhitePoint(Color color) => WhitePoint = color;

		// -- the page buttons (btFirstPage, btPrevPage, btNextPage, btLastPage)

		public void ShowPage(int page)
		{
			ViewerPage = page;
		}

		public void GoFirst()
		{
			if (PageCount > 0)
			{
				ViewerPage = 0;
			}
		}

		public void GoPrevious()
		{
			int page = ViewerPage - 1;
			if (PageCount >= 0 && page >= 0)
			{
				ViewerPage = page;
			}
		}

		public void GoNext()
		{
			int page = ViewerPage + 1;
			if (PageCount >= 0 && page < PageCount)
			{
				ViewerPage = page;
			}
		}

		public void GoLast()
		{
			if (PageCount > 0)
			{
				ViewerPage = PageCount - 1;
			}
		}
	}

	/// <summary>Where the colour preview gets the page from: a copy of the page, scaled to a width, that the caller owns.</summary>
	public interface IPageImageSource
	{
		/// <summary>The page (counting from 0) as a bitmap <paramref name="width"/> wide with the aspect kept, or null if it cannot be read.</summary>
		Bitmap Load(int page, int width);
	}

	/// <summary>The Core's pages: <c>SetImage</c> of the dialog asks the image pool for the page key of the book without any adjustment and scales it to the width of the viewer.</summary>
	public sealed class CorePageImageSource : IPageImageSource
	{
		private readonly ComicBook book;
		private readonly ImagePool pool;

		public CorePageImageSource(ComicBook book, ImagePool pool)
		{
			this.book = book ?? throw new ArgumentNullException(nameof(book));
			this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
		}

		public Bitmap Load(int page, int width)
		{
			if (page < 0 || width <= 0)
			{
				return null;
			}
			try
			{
				PageKey key = book.GetPageKey(page, BitmapAdjustment.Empty);
				using (IItemLock<PageImage> item = pool.GetPage(key, book))
				{
					Bitmap bitmap = item?.Item?.Bitmap;
					if (bitmap == null)
					{
						return null;
					}
					// The Core disposes its bitmaps whenever it likes, holding ItemMonitor on the bitmap: the copy is made under the same lock.
					using (ItemMonitor.Lock(bitmap))
					{
						Bitmap scaled = bitmap.Scale(width, 0);
						return ReferenceEquals(scaled, bitmap) ? bitmap.Copy(System.Drawing.Imaging.PixelFormat.Format32bppArgb) : scaled;
					}
				}
			}
			catch (Exception)
			{
				return null;
			}
		}
	}

	/// <summary>
	/// The picture the Colors tab shows: the page scaled to the viewer's width, with the adjustment of the sliders applied by the Core's own colour code (<c>ImageProcessing.CreateAdjustedBitmap</c>:
	/// colour matrix, gamma, sharpening). The adjustment is made off the UI thread; while the user drags a slider the last request wins.
	/// </summary>
	public sealed class ColorPreview : IDisposable
	{
		private readonly IPageImageSource source;
		private readonly Func<BitmapAdjustment> adjustment;
		private readonly Action<Action> post;
		private readonly Func<Func<object>, Task<object>> run;
		private readonly object sync = new object();
		private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
		private Bitmap page; // the page as read, scaled (white point picks read this)
		private CoverImage picture;
		private int generation;
		private int pageNumber = -1;
		private int width;
		private bool disposed;

		/// <param name="source">Where pages come from.</param>
		/// <param name="adjustment">The adjustment to apply now.</param>
		/// <param name="post">Runs an action on the UI thread (the default posts through <see cref="UiDispatch"/>).</param>
		/// <param name="run">Runs work off the UI thread and returns its result (the default is the thread pool).</param>
		public ColorPreview(IPageImageSource source, Func<BitmapAdjustment> adjustment, Action<Action> post = null, Func<Func<object>, Task<object>> run = null)
		{
			this.source = source ?? throw new ArgumentNullException(nameof(source));
			this.adjustment = adjustment ?? throw new ArgumentNullException(nameof(adjustment));
			this.post = post ?? (action => UiDispatch.Post(action));
			this.run = run ?? (work => Task.Run(work));
		}

		/// <summary>The adjusted picture (a reference of the preview's own; <see cref="Changed"/> follows a new one), or null.</summary>
		public CoverImage Picture => picture;

		/// <summary>The page the picture belongs to, or -1.</summary>
		public int Page => pageNumber;

		/// <summary>True while a page or an adjustment is being made.</summary>
		public bool IsBusy { get; private set; }

		/// <summary>The picture was replaced (on the UI thread).</summary>
		public event EventHandler Changed;

		/// <summary>Shows a page: reads it at <paramref name="viewerWidth"/> and applies the adjustment. Completes when the picture is shown (or the request was overtaken).</summary>
		public Task ShowPageAsync(int pageToShow, int viewerWidth)
		{
			pageNumber = pageToShow;
			width = viewerWidth;
			return Make(readPage: true);
		}

		/// <summary>The adjustment changed: makes the picture again from the page already read.</summary>
		public Task RefreshAsync()
		{
			return Make(readPage: false);
		}

		private async Task Make(bool readPage)
		{
			if (disposed)
			{
				return;
			}
			int mine = Interlocked.Increment(ref generation);
			int wantedPage = pageNumber, wantedWidth = width;
			BitmapAdjustment wanted = adjustment();
			Bitmap known;
			lock (sync)
			{
				known = readPage ? null : page;
			}
			IsBusy = true;
			object[] made;
			// one adjustment at a time: requests that a newer one overtook while they waited do nothing (a slider dragged fast asks many times)
			await gate.WaitAsync().ConfigureAwait(false);
			try
			{
				if (disposed || mine != Volatile.Read(ref generation))
				{
					return;
				}
				made = (object[])await run(() =>
				{
					Bitmap read = known ?? (wantedPage >= 0 ? source.Load(wantedPage, wantedWidth) : null);
					SKImage image = null;
					if (read != null)
					{
						image = Adjusted(read, wanted);
					}
					return new object[] { read, image };
				}).ConfigureAwait(false);
			}
			catch (Exception)
			{
				made = new object[] { null, null };
			}
			finally
			{
				gate.Release();
			}
			var readBitmap = (Bitmap)made[0];
			var skImage = (SKImage)made[1];
			var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			post(() =>
			{
				try
				{
					if (disposed || mine != Volatile.Read(ref generation))
					{
						// overtaken by a newer request (or closed): the newer one shows its own result
						if (readBitmap != null && !ReferenceEquals(readBitmap, known))
						{
							readBitmap.Dispose();
						}
						skImage?.Dispose();
						return;
					}
					lock (sync)
					{
						// the replaced page is not disposed: an adjustment that was started on it may still be reading it, and the finalizer frees it
						page = readBitmap;
					}
					CoverImage old = picture;
					picture = skImage != null ? new CoverImage(skImage) : null;
					old?.Release();
					IsBusy = false;
					Changed?.Invoke(this, EventArgs.Empty);
				}
				finally
				{
					done.TrySetResult(true);
				}
			});
			await done.Task.ConfigureAwait(false);
		}

		// CreateAdjustedBitmap clones when there is something to change; an empty adjustment shows the page as it is
		private static SKImage Adjusted(Bitmap read, BitmapAdjustment wanted)
		{
			Bitmap adjusted = wanted.IsEmpty && !wanted.HasGamma ? null : read.CreateAdjustedBitmap(wanted, System.Drawing.Imaging.PixelFormat.Format32bppArgb, alwaysClone: true);
			try
			{
				return SKImage.FromBitmap(SkiaInterop.GetSKBitmap(adjusted ?? read));
			}
			finally
			{
				adjusted?.Dispose();
			}
		}

		/// <summary>The colour of a pixel of the page as read (not adjusted), or null outside the page (<c>BitmapViewer.GetPixel</c>).</summary>
		public Color? PixelAt(int x, int y)
		{
			lock (sync)
			{
				if (page == null || x < 0 || y < 0 || x >= page.Width || y >= page.Height)
				{
					return null;
				}
				return page.GetPixel(x, y);
			}
		}

		/// <summary>The size of the page picture in pixels (0 x 0 when there is none).</summary>
		public Size PageSize
		{
			get
			{
				lock (sync)
				{
					return page == null ? Size.Empty : page.Size;
				}
			}
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			Interlocked.Increment(ref generation);
			lock (sync)
			{
				page = null; // not disposed, see Make
			}
			picture?.Release();
			picture = null;
		}
	}
}
