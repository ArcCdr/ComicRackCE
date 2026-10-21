using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Editor;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The Colors tab of the Info dialog (<c>tabColors</c>, IN.07): the page viewer with the book's colour adjustment applied to the page, the page buttons and their label, the "Image Control"
	/// button that shows the sliders (Saturation, Brightness, Contrast, Gamma, Sharpening) and Reset. A double click on the page makes the colour under the pointer the white point. The values
	/// are <see cref="BookEditorModel.ColorTab"/>; the picture is made by <see cref="ColorPreview"/> from the Core's page of the book. The tab reads the page when it is shown
	/// (<c>pageViewer_VisibleChanged</c>), not before. Whether the sliders are open is kept for the next dialog (<c>showColorControls</c>).
	/// </summary>
	public partial class InfoColorsPage : UserControl
	{
		private static bool showColorControls;

		/// <summary>Forgets whether the sliders were open, as a new application would: for the tests, which share the process.</summary>
		public static void ResetViewState() => showColorControls = false;

		private BookEditorModel model;
		private ColorPreview preview;
		private bool syncing;
		private bool attached;
		private int shownWidth;

		public InfoColorsPage()
		{
			InitializeComponent();
			InfoPageText.Translate(this);
			pageViewer.Text = InfoPageText.Get("pageViewer", "Double Click on Color to set White Point");
			chkShowImageControlsText.Text = InfoPageText.Get("chkShowImageControls", "Image Control");
			labelCurrentPage.Content = string.Empty;
			chkShowImageControls.IsChecked = showColorControls;
			panelImageControls.IsVisible = showColorControls;
			chkShowImageControls.IsCheckedChanged += (sender, e) =>
			{
				showColorControls = chkShowImageControls.IsChecked == true;
				panelImageControls.IsVisible = showColorControls;
			};
			pageViewer.Picked += OnPicked;
			pageViewer.SizeChanged += (sender, e) => OnViewerResized();
		}

		/// <summary>Makes the source of the pages for a book; the default reads them from the Core's image pool. Tests set another one.</summary>
		public Func<BookEditorModel, IPageImageSource> ImageSource { get; set; } = DefaultSource;

		private static IPageImageSource DefaultSource(BookEditorModel editor)
		{
			ComicRack.Mac.Editor.PageListServices services = editor.PageServices;
			return services?.Pool != null && editor.Display != null ? new CorePageImageSource(editor.Display, services.Pool) : null;
		}

		public CrPageViewer Viewer => pageViewer;

		public Panel ControlsPanel => panelImageControls;

		public ToggleButton ImageControlsButton => chkShowImageControls;

		public Slider Saturation => tbSaturation;

		public Slider Brightness => tbBrightness;

		public Slider Contrast => tbContrast;

		public Slider Gamma => tbGamma;

		public Slider Sharpening => tbSharpening;

		public Button ResetButton => btResetColors;

		public Button FirstButton => btFirstPage;

		public Button PreviousButton => btPrevPage;

		public Button NextButton => btNextPage;

		public Button LastButton => btLastPage;

		public Label PageLabel => labelCurrentPage;

		/// <summary>The colour preview of the shown book, or null while there is none (no pages to read).</summary>
		public ColorPreview Preview => preview;

		public void Initialize(BookEditorModel editor)
		{
			model = editor;
			DataContext = editor;
			editor.ColorTab.PropertyChanged += OnColorsChanged;
			editor.BookLoaded += (sender, e) => OnBookLoaded();
			WireSlider(tbSaturation, v => model.ColorTab.Saturation = v);
			WireSlider(tbBrightness, v => model.ColorTab.Brightness = v);
			WireSlider(tbContrast, v => model.ColorTab.Contrast = v);
			WireSlider(tbGamma, v => model.ColorTab.Gamma = v);
			WireSlider(tbSharpening, v => model.ColorTab.Sharpening = v);
			btResetColors.Click += (sender, e) => model.ColorTab.Reset();
			btFirstPage.Click += (sender, e) => model.ColorTab.GoFirst();
			btPrevPage.Click += (sender, e) => model.ColorTab.GoPrevious();
			btNextPage.Click += (sender, e) => model.ColorTab.GoNext();
			btLastPage.Click += (sender, e) => model.ColorTab.GoLast();
			SyncFromModel();
		}

		private void WireSlider(Slider slider, Action<int> set)
		{
			slider.PropertyChanged += (sender, e) =>
			{
				if (e.Property == RangeBase.ValueProperty && !syncing)
				{
					set((int)Math.Round(slider.Value));
				}
			};
		}

		// the sliders, the label and the page buttons show the model
		private void SyncFromModel()
		{
			ColorTabModel colors = model.ColorTab;
			syncing = true;
			try
			{
				tbSaturation.Value = colors.Saturation;
				tbBrightness.Value = colors.Brightness;
				tbContrast.Value = colors.Contrast;
				tbGamma.Value = colors.Gamma;
				tbSharpening.Value = colors.Sharpening;
			}
			finally
			{
				syncing = false;
			}
			foreach ((Slider slider, int value) in new[] { (tbSaturation, colors.Saturation), (tbBrightness, colors.Brightness), (tbContrast, colors.Contrast), (tbGamma, colors.Gamma) })
			{
				ToolTip.SetTip(slider, ColorTabModel.SliderToolTip(value));
			}
			labelCurrentPage.Content = colors.PageLabel;
			btFirstPage.IsEnabled = colors.CanGoFirst;
			btPrevPage.IsEnabled = colors.CanGoPrevious;
			btNextPage.IsEnabled = colors.CanGoNext;
			btLastPage.IsEnabled = colors.CanGoLast;
		}

		private void OnColorsChanged(object sender, PropertyChangedEventArgs e)
		{
			SyncFromModel();
			switch (e.PropertyName)
			{
				case nameof(ColorTabModel.Adjustment):
					_ = RefreshAsync();
					break;
				case nameof(ColorTabModel.ViewerPage):
					_ = ShowCurrentPageAsync();
					break;
			}
		}

		private void OnBookLoaded()
		{
			preview?.Dispose();
			preview = null;
			pageViewer.Clear();
			shownWidth = 0;
			SyncFromModel();
			if (attached)
			{
				_ = ShowCurrentPageAsync();
			}
		}

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);
			attached = true;
			if (model != null)
			{
				_ = ShowCurrentPageAsync();
			}
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			attached = false;
		}

		/// <summary>The dialog closes: the picture and the page are let go.</summary>
		public void Close()
		{
			attached = false;
			preview?.Dispose();
			preview = null;
			pageViewer.Clear();
		}

		private int WidthInPixels()
		{
			double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
			return Math.Max(1, (int)Math.Ceiling(pageViewer.ViewWidth * scale));
		}

		private ColorPreview EnsurePreview()
		{
			if (preview == null && model != null)
			{
				IPageImageSource source = ImageSource?.Invoke(model);
				if (source != null)
				{
					preview = new ColorPreview(source, () => model.ColorTab.Adjustment);
					preview.Changed += (sender, e) => pageViewer.SetPicture(preview.Picture);
				}
			}
			return preview;
		}

		/// <summary>Shows the page the model names (<c>SetPageView</c>): read at the width of the viewer, with the adjustment applied.</summary>
		public Task ShowCurrentPageAsync()
		{
			if (!attached || model == null || model.ColorTab.ViewerPage < 0)
			{
				return Task.CompletedTask;
			}
			ColorPreview p = EnsurePreview();
			if (p == null)
			{
				return Task.CompletedTask;
			}
			shownWidth = WidthInPixels();
			return p.ShowPageAsync(model.ColorTab.ViewerPage, shownWidth);
		}

		private Task RefreshAsync()
		{
			if (!attached || preview == null || preview.Page < 0)
			{
				return Task.CompletedTask;
			}
			return preview.RefreshAsync();
		}

		// the page was read for the width the viewer had; a clearly different width reads it again
		private void OnViewerResized()
		{
			if (attached && preview != null && shownWidth > 0 && Math.Abs(WidthInPixels() - shownWidth) > 24)
			{
				_ = ShowCurrentPageAsync();
			}
		}

		// pageViewer_DoubleClick: the colour of the page under the pointer
		private void OnPicked(object sender, Point fraction)
		{
			if (preview == null)
			{
				return;
			}
			System.Drawing.Size size = preview.PageSize;
			if (size.IsEmpty)
			{
				return;
			}
			System.Drawing.Color? color = preview.PixelAt(Math.Min(size.Width - 1, (int)(fraction.X * size.Width)), Math.Min(size.Height - 1, (int)(fraction.Y * size.Height)));
			if (color.HasValue)
			{
				model.ColorTab.SetWhitePoint(color.Value);
			}
		}
	}
}
