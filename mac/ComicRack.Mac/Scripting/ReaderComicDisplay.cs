using System;
using System.Drawing;
using System.Windows.Forms;
using ComicRack.Mac.ViewModels;
using cYo.Common.Drawing;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Display;
using cYo.Projects.ComicRack.Engine.IO.Cache;

namespace ComicRack.Mac.Scripting
{
	/// <summary>
	/// The reader as a script sees it, <c>ComicRack.ComicDisplay</c> (<see cref="IComicDisplay"/>, the page display of the WinForms program). It is a view of the reader of the current tab:
	/// the settings the reader has (<see cref="PageLayout"/>, <see cref="ImageFitMode"/>, <see cref="ImageZoom"/>, <see cref="RightToLeftReading"/>) read and change it, on the UI thread; the
	/// display settings that the reader does not have yet (backgrounds, magnifier, overlays, animation) keep the value a script sets and read it back, which is what the display of the original does
	/// as long as nothing draws with them, so a script that adapts the display to the window size runs. The members that act on a particular drawn page do nothing and answer what an idle display
	/// answers.
	/// </summary>
	public sealed class ReaderComicDisplay : IComicDisplay
	{
		private readonly Func<ReaderViewModel> reader;

		public ReaderComicDisplay(Func<ReaderViewModel> reader)
		{
			this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
		}

		// ---- settings the reader has

		public PageLayoutMode PageLayout
		{
			get => UiCall.Invoke(() => reader()?.LayoutMode ?? pageLayout);
			set
			{
				pageLayout = value;
				UiCall.Invoke(() => reader()?.SetLayoutMode(value));
			}
		}

		public ImageFitMode ImageFitMode
		{
			get => UiCall.Invoke(() => reader()?.Fit ?? fitMode);
			set
			{
				fitMode = value;
				UiCall.Invoke(() => reader()?.SetFit(value));
			}
		}

		public float ImageZoom
		{
			get => UiCall.Invoke(() => (float)(reader()?.Zoom ?? 1.0));
			set => UiCall.Invoke(() => reader()?.SetZoom(value));
		}

		public bool RightToLeftReading
		{
			get => UiCall.Invoke(() => reader()?.RightToLeft ?? rightToLeft);
			set
			{
				rightToLeft = value;
				UiCall.Invoke(() => reader()?.SetRightToLeft(value));
			}
		}

		public int CurrentPage => UiCall.Invoke(() => reader()?.CurrentPage ?? 0);

		public bool IsValid => UiCall.Invoke(() => reader() != null);

		private PageLayoutMode pageLayout;
		private ImageFitMode fitMode;
		private bool rightToLeft;

		// ---- settings kept for the script (the reader has no counterpart yet)

		public ImageBackgroundMode ImageBackgroundMode { get; set; }

		public Color BackColor { get; set; }

		public string BackgroundTexture { get; set; }

		public ImageLayout BackgroundImageLayout { get; set; }

		public string PaperTexture { get; set; }

		public float PaperTextureStrength { get; set; }

		public ImageLayout PaperTextureLayout { get; set; }

		public bool PageMargin { get; set; }

		public float PageMarginPercentWidth { get; set; }

		public float DoublePageOverlap { get; set; }

		public bool ImageAutoRotate { get; set; }

		public ImageRotation ImageRotation { get; set; }

		public bool ImageFitOnlyIfOversized { get; set; }

		public ImageDisplayOptions ImageDisplayOptions { get; set; }

		public bool RealisticPages { get; set; }

		public bool AutoHideCursor { get; set; }

		public bool LeftRightMovementReversed { get; set; }

		public RightToLeftReadingMode RightToLeftReadingMode { get; set; }

		public bool TwoPageNavigation { get; set; }

		public bool BlendWhilePaging { get; set; }

		public float MagnifierOpacity { get; set; }

		public Size MagnifierSize { get; set; }

		public bool MagnifierVisible { get; set; }

		public float MagnifierZoom { get; set; }

		public MagnifierStyle MagnifierStyle { get; set; }

		public bool AutoHideMagnifier { get; set; }

		public bool AutoMagnifier { get; set; }

		public float InfoOverlayScaling { get; set; }

		public InfoOverlays VisibleInfoOverlays { get; set; }

		public bool SmoothScrolling { get; set; }

		public PageTransitionEffect PageTransitionEffect { get; set; }

		public bool DisplayChangeAnimation { get; set; }

		public bool FlowingMouseScrolling { get; set; }

		public bool SoftwareFiltering { get; set; }

		public bool HardwareFiltering { get; set; }

		public bool NavigationOverlayVisible { get; set; }

		public ImagePartInfo ImageVisiblePart { get; set; }

		// ---- the drawn page: an idle display

		public ComicBookNavigator Book { get; set; }

		public IPagePool PagePool { get; set; }

		public IThumbnailPool ThumbnailPool { get; set; }

		public bool IsMovementFlipped => RightToLeftReading;

		public int CurrentMousePage => -1;

		public ImageRotation CurrentImageRotation => ImageRotation.None;

		public Size ImageSize => Size.Empty;

		public int ImagePartCount => 1;

		public bool IsDoubleImage => false;

		public bool IsHardwareRenderer => false;

		public bool ShouldPagingBlend => false;

		public event EventHandler BookChanged
		{
			add { }
			remove { }
		}

		public event EventHandler DrawnPageCountChanged
		{
			add { }
			remove { }
		}

		public event EventHandler<BrowseEventArgs> Browse
		{
			add { }
			remove { }
		}

		public event EventHandler<BookPageEventArgs> PageChange
		{
			add { }
			remove { }
		}

		public event EventHandler<BookPageEventArgs> PageChanged
		{
			add { }
			remove { }
		}

		public event EventHandler<GestureEventArgs> Gesture
		{
			add { }
			remove { }
		}

		public event EventHandler<GestureEventArgs> PreviewGesture
		{
			add { }
			remove { }
		}

		public event EventHandler VisibleInfoOverlaysChanged
		{
			add { }
			remove { }
		}

		public bool SetRenderer(bool hardware) => false;

		public object GetState() => null;

		public void Animate(object a, object b, int time)
		{
		}

		public void Animate(Action<float> animate, int time)
		{
			animate?.Invoke(1f);
		}

		public Bitmap CreatePageImage() => null;

		public void MovePartDown(float percent)
		{
		}

		public bool MovePart(Point offset) => false;

		public bool DisplayPart(PartPageToDisplay ptd) => false;

		public void DisplayOpenMessage()
		{
		}

		public void ZoomTo(Point location, float zoom)
		{
			ImageZoom = zoom;
		}
	}
}
