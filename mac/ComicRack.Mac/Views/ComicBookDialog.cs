using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Services;
using ComicRack.Mac.Thumbnails;
using cYo.Common.Localize;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO.Provider;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The Info dialog (DL.01, <c>ComicRack/Dialogs/ComicBookDialog.cs</c>): the tabs Summary, Details, Plot &amp; Notes, Catalog, Custom, Pages and Colors in the original's order, Previous and
	/// Next at the left of the buttons, OK, Cancel and Apply at the right. The dialog edits the library's own book through <see cref="BookEditorModel"/>: OK, Apply, Previous and Next
	/// write it, Cancel does not. P5.7a builds the frame, Summary and Details; the other tabs are filled by P5.7b and P5.7c. The window is 593 x 517 at least (the Designer's client
	/// size) and can be resized; the size and the tab the user chose are kept (<c>SafeSize</c>, <c>lastActivePage</c>). The cover is the book's front cover thumbnail, drawn as the
	/// 3D book of the Core's <c>ComicBox3D</c> unless the user clicked it to get the flat cover (<c>InformationCover3D</c>). Spec: <c>mac/specs/views/ComicBookDialog.md</c>.
	/// </summary>
	public sealed class ComicBookDialog : CrDialogWindow
	{
		/// <summary>The client size of the form in the Designer (<c>ClientSize</c> 593 x 517); the dialog is never smaller (<c>MinimumSize</c> 609 x 556 with the frame).</summary>
		public const double DesignWidth = 593;

		public const double DesignHeight = 517;

		private static int lastActivePage = -1;

		private readonly BookInfoRequest request;
		private readonly BookEditorModel model;
		private readonly TabControl tabs = new TabControl();
		private readonly TabItem tabSummary, tabDetails, tabPlot, tabCatalog, tabCustom, tabPages, tabColors;
		private readonly Panel plot = new Panel(), catalog = new Panel(), custom = new Panel(), pagesPanel = new Panel(), colors = new Panel();
		private readonly InfoSummaryPage summary = new InfoSummaryPage();
		private readonly InfoDetailsPage details = new InfoDetailsPage();
		private readonly Button previousButton = new Button();
		private readonly Button nextButton = new Button();
		private CancellationTokenSource coverRequest;
		private CoverImage coverImage;
		private ComicBook watched;
		private Control lastFocusedBox;

		public ComicBookDialog(BookInfoRequest request)
		{
			this.request = request ?? throw new ArgumentNullException(nameof(request));
			model = request.Model ?? throw new ArgumentException("The request has no editor.", nameof(request));
			TrSection = InfoPageText.Section;
			CanResize = true;
			SizeToContent = SizeToContent.Manual;
			MinWidth = DesignWidth;
			MinHeight = DesignHeight;
			Width = Math.Max(DesignWidth, request.Preferences?.InfoDialogWidth ?? 0);
			Height = Math.Max(DesignHeight, request.Preferences?.InfoDialogHeight ?? 0);
			ButtonBarMargin = new Thickness(8, 6, 12, 10);
			Buttons = DialogButtons.OkCancelApply;

			tabSummary = Tab("tabSummary", "Summary", summary);
			tabDetails = Tab("tabDetails", "Details", details);
			tabPlot = Tab("tabPlot", "Plot & Notes", plot);
			tabCatalog = Tab("tabCatalog", "Catalog", catalog);
			tabCustom = Tab("tabCustom", "Custom", custom);
			tabPages = Tab("tabPages", "Pages", pagesPanel);
			tabColors = Tab("tabColors", "Colors", colors);
			foreach (TabItem tab in new[] { tabSummary, tabDetails, tabPlot, tabCatalog, tabCustom, tabPages, tabColors })
			{
				tabs.Items.Add(tab);
			}
			tabs.Padding = new Thickness(3, 0, 3, 3); // the pages are 566 x 442 as in the Designer (the stock tab control leaves 572 x 445)
			var body = new Panel { Width = DesignWidth, Height = 477 };
			DesignerLayout.Place(body, tabs, 8, 9, 574, 468, Anchor.All, DesignWidth, 477);
			Body = body;
			body.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
			body.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
			body.ClearValue(WidthProperty);
			body.ClearValue(HeightProperty);

			previousButton.Content = DialogText("btPrev", "Previous");
			nextButton.Content = DialogText("btNext", "Next");
			var navigation = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };
			foreach (Button button in new[] { previousButton, nextButton })
			{
				button.Width = ChromeMetrics.DialogButtonWidth;
				button.Height = ChromeMetrics.DialogButtonHeight;
				button.Padding = new Thickness(0);
				button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
				navigation.Children.Add(button);
			}
			Footer = navigation;

			details.Initialize(model);
			summary.DataContext = model;
			WireModel();
			WireSummary();
			WireButtons();
			RestoreTab();
			ApplyModelState();
			summary.Tile.ThreeD = model.ThreeD;
			Opened += (sender, e) => OnOpened();
			Closed += (sender, e) => OnClosed();
		}

		public BookEditorModel Model => model;

		public TabControl Tabs => tabs;

		public InfoSummaryPage Summary => summary;

		public InfoDetailsPage Details => details;

		public Button PreviousButton => previousButton;

		public Button NextButton => nextButton;

		/// <summary>The tab pages in the original's order (Summary, Details, Plot &amp; Notes, Catalog, Custom, Pages, Colors).</summary>
		public IReadOnlyList<TabItem> TabPages => new[] { tabSummary, tabDetails, tabPlot, tabCatalog, tabCustom, tabPages, tabColors };

		/// <summary>The panels of the tabs that later tasks fill: Plot &amp; Notes and Custom (P5.7b), Catalog (P5.7b), Pages and Colors (P5.7c).</summary>
		public Panel PlotPage => plot;

		public Panel CatalogPage => catalog;

		public Panel CustomPage => custom;

		public Panel PagesPage => pagesPanel;

		public Panel ColorsPage => colors;

		private TabItem Tab(string key, string english, Control content)
		{
			// the Win32 tab control shows the text as it is: the "&" of "Plot & Notes" stays
			return new TabItem { Header = UiText.Raw(InfoPageText.Section, key, english), Content = content, Tag = key };
		}

		// ---- model state -----------------------------------------------------------------------------------------------

		private void WireModel()
		{
			model.PropertyChanged += OnModelChanged;
			model.BookLoaded += (sender, e) => OnBookLoaded();
			model.CoverChanged += (sender, book) => _ = LoadCoverAsync(book);
		}

		private void OnModelChanged(object sender, PropertyChangedEventArgs e)
		{
			if (model.IsLoading)
			{
				return; // BookLoaded follows with everything the dialog has to show
			}
			switch (e.PropertyName)
			{
				case nameof(BookEditorModel.DialogTitle):
					Title = model.DialogTitle;
					break;
				case nameof(BookEditorModel.ThreeD):
					summary.Tile.ThreeD = model.ThreeD;
					if (model.ThreeD)
					{
						_ = RenderBoxAsync(CancellationToken.None);
					}
					break;
				case nameof(BookEditorModel.Format):
				case nameof(BookEditorModel.AgeRating):
				case nameof(BookEditorModel.Publisher):
				case nameof(BookEditorModel.Imprint):
				case nameof(BookEditorModel.Year):
				case nameof(BookEditorModel.BlackAndWhite):
				case nameof(BookEditorModel.Manga):
				case nameof(BookEditorModel.SeriesComplete):
					RefreshIcons();
					break;
				case nameof(BookEditorModel.CanGoPrevious):
				case nameof(BookEditorModel.CanGoNext):
				case nameof(BookEditorModel.HasNavigation):
					ApplyModelState();
					break;
				default:
					if (e.PropertyName.StartsWith("Show", StringComparison.Ordinal) || e.PropertyName.EndsWith("Enabled", StringComparison.Ordinal))
					{
						ApplyModelState();
					}
					break;
			}
		}

		// the tab pages, the buttons and the enabled states that follow the book (SetComicToEditor)
		private void ApplyModelState()
		{
			tabPages.IsVisible = model.ShowPagesTab;
			tabColors.IsVisible = model.ShowColorsTab;
			tabCatalog.IsVisible = model.ShowCatalogTab;
			tabCustom.IsVisible = model.ShowCustomTab;
			details.IsEnabled = model.DetailsEnabled;
			plot.IsEnabled = model.PlotEnabled;
			colors.IsEnabled = model.ColorsEnabled;
			pagesPanel.IsEnabled = model.PagesEnabled;
			catalog.IsEnabled = model.CatalogEnabled;
			custom.IsEnabled = model.CustomEnabled;
			previousButton.IsVisible = nextButton.IsVisible = model.HasNavigation;
			previousButton.IsEnabled = model.CanGoPrevious;
			nextButton.IsEnabled = model.CanGoNext;
			if (tabs.SelectedItem is TabItem selected && !selected.IsVisible)
			{
				tabs.SelectedItem = tabSummary;
			}
		}

		private void OnBookLoaded()
		{
			Title = model.DialogTitle;
			ApplyModelState();
			Watch(model.Display);
			RefreshTileText();
			RefreshIcons();
		}

		private void Watch(ComicBook book)
		{
			if (watched != null)
			{
				watched.BookChanged -= OnWatchedBookChanged;
			}
			watched = book;
			if (watched != null)
			{
				watched.BookChanged += OnWatchedBookChanged;
			}
		}

		// the text beside the cover follows the book (ThumbnailControl.comicBook_PropertyChanged)
		private void OnWatchedBookChanged(object sender, PropertyChangedEventArgs e)
		{
			UiDispatch.Post(RefreshTileText);
		}

		private void RefreshTileText()
		{
			summary.Tile.Lines = BookTileText.Build(model.Display, BookTileText.InfoDialogElements);
		}

		/// <summary><c>IconTextsChanged</c>: the icons follow the publisher, imprint, age rating, format, year and the yes/no fields as they are edited, not as the book holds them.</summary>
		private void RefreshIcons()
		{
			ComicBook shown = model.Display;
			if (shown == null)
			{
				return;
			}
			var clone = (ComicBook)shown.Clone();
			clone.Publisher = model.Publisher;
			clone.Year = EditControls.GetNumber(model.Year);
			clone.Imprint = model.Imprint;
			clone.AgeRating = model.AgeRating;
			clone.Format = model.Format;
			clone.Manga = EditControls.GetMangaYesNo(model.Manga);
			clone.SeriesComplete = EditControls.GetYesNo(model.SeriesComplete);
			clone.BlackAndWhite = EditControls.GetYesNo(model.BlackAndWhite);
			summary.Tile.SetIcons(Cover3D.IconImages(clone.GetIcons()).Select(i => i));
		}

		// ---- the cover -------------------------------------------------------------------------------------------------

		private async Task LoadCoverAsync(ComicBook book)
		{
			coverRequest?.Cancel();
			var cts = new CancellationTokenSource();
			coverRequest = cts;
			if (request.Thumbnails == null || book == null)
			{
				return;
			}
			try
			{
				ThumbnailResult result = await request.Thumbnails.RequestResult(book, ThumbnailService.FrontCover, 512, cts.Token);
				if (cts.IsCancellationRequested)
				{
					result.Image?.Dispose();
					return;
				}
				var cover = new CoverImage(result.Image);
				CoverImage old = coverImage;
				coverImage = cover;
				summary.Tile.SetCover(cover);
				summary.Tile.SetBox(null);
				old?.Release();
				if (model.ThreeD)
				{
					await RenderBoxAsync(cts.Token);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (ObjectDisposedException)
			{
			}
			catch (Exception ex)
			{
				AppLog.Error("The cover of the Info dialog could not be shown.", ex);
			}
		}

		// the 3D book of the cover, made off the UI thread (a software renderer: 50 to 200 ms)
		private async Task RenderBoxAsync(CancellationToken token)
		{
			CoverImage cover = coverImage;
			if (cover == null)
			{
				return;
			}
			try
			{
				CoverImage box = await Cover3D.RenderAsync(cover, model.Display?.PageCount ?? 0, token);
				if (box == null)
				{
					return;
				}
				if (token.IsCancellationRequested || !ReferenceEquals(cover, coverImage))
				{
					box.Release();
					return;
				}
				summary.Tile.SetBox(box);
				box.Release();
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				AppLog.Error("The 3D cover of the Info dialog could not be drawn.", ex);
			}
		}

		// ---- the Summary tab -------------------------------------------------------------------------------------------

		private void WireSummary()
		{
			summary.Tile.Clicked += (sender, e) => model.ThreeD = !model.ThreeD;
			summary.Tile.PointerWheelChanged += (sender, e) =>
			{
				// coverThumbnail_MouseWheel: up shows the book before, down the next one
				if (e.Delta.Y > 0)
				{
					Previous();
				}
				else if (e.Delta.Y < 0)
				{
					Next();
				}
				e.Handled = true;
			};
			summary.WebLink.PointerPressed += (sender, e) =>
			{
				if (!string.IsNullOrEmpty(model.WebLink))
				{
					request.OpenUrl?.Invoke(model.WebLink);
				}
			};
			summary.ThumbnailButton.Click += async (sender, e) => await ChooseThumbnailAsync();
			summary.LinkFileButton.Click += async (sender, e) => await ChooseLinkFileAsync();
			summary.ThumbnailButton.Flyout = ThumbnailMenu();
		}

		private MenuFlyout ThumbnailMenu()
		{
			var menu = new MenuFlyout();
			menu.Opening += (sender, e) =>
			{
				menu.Items.Clear();
				var reset = new MenuItem { Header = InfoPageText.Get("miResetThumbnail", "Reset") };
				reset.Click += (s, a) => model.ResetThumbnail();
				menu.Items.Add(reset);
				string[] recent = request.Preferences?.RecentThumbnailFiles ?? Array.Empty<string>();
				if (recent.Length > 0)
				{
					menu.Items.Add(new Separator());
					foreach (string file in recent)
					{
						string path = file;
						var item = new MenuItem { Header = path };
						item.Click += (s, a) => LoadThumbnail(path);
						menu.Items.Add(item);
					}
				}
			};
			return menu;
		}

		private async Task ChooseThumbnailAsync()
		{
			string title = (summary.ThumbnailButton.Content as string) ?? "Thumbnail...";
			IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
			{
				Title = title,
				AllowMultiple = false,
				FileTypeFilter = ThumbnailFilter(),
			});
			string path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
			if (path != null)
			{
				LoadThumbnail(path);
			}
		}

		/// <summary><c>LoadThumbnail</c>: makes the custom thumbnail of a picture file and shows it; the file goes to the front of the recent files.</summary>
		public void LoadThumbnail(string file)
		{
			string key = request.AddCustomThumbnail?.Invoke(file);
			if (!string.IsNullOrEmpty(key))
			{
				request.Preferences?.AddRecentThumbnailFile(file);
				model.CustomThumbnailKey = key;
			}
		}

		private async Task ChooseLinkFileAsync()
		{
			string title = (summary.LinkFileButton.Content as string) ?? "Link to File...";
			IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
			{
				Title = title,
				AllowMultiple = false,
				FileTypeFilter = new[]
				{
					new FilePickerFileType(TR.Default["AllFiles", "All Files"]) { Patterns = new[] { "*" } },
				}.Concat(Providers.Readers.GetSourceFormats().OrderBy(f => f).Select(f => new FilePickerFileType(f.Name) { Patterns = f.Extensions.Select(x => "*" + x).ToArray() })).ToArray(),
			});
			string path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
			if (path != null)
			{
				await LinkFileAsync(path);
			}
		}

		/// <summary><c>LinkFile</c>: shows the book of the chosen file with the fields of this book, unless the library has that file already.</summary>
		public async Task<bool> LinkFileAsync(string path)
		{
			if (!model.LinkFile(path, request.FindBook, out string problem))
			{
				if (problem != null)
				{
					await ShowProblemAsync(problem);
				}
				return false;
			}
			return true;
		}

		private Task ShowProblemAsync(string text) => request.ShowProblem?.Invoke(text) ?? Task.CompletedTask;

		// The pictures a custom thumbnail can be made of (Program.LoadCustomThumbnail: jpeg, bitmap, png, gif, tiff and icon files). A translation of the original's
		// filter text ("JPEG Image|*.jpg|...") is used as it is; without one the picker's own list of picture types is used.
		private static FilePickerFileType[] ThumbnailFilter()
		{
			string translated = TR.Load("FileFilter")["LoadThumbnail", string.Empty];
			if (!string.IsNullOrEmpty(translated))
			{
				return FileFilter(translated);
			}
			return new[] { FilePickerFileTypes.ImageAll }; // on macOS: every picture type the system knows (public.image), which covers the original's list
		}

		// "JPEG Image|*.jpg|Windows Bitmap Image|*.bmp|..." as picker filters
		private static FilePickerFileType[] FileFilter(string filter)
		{
			string[] parts = filter.Split('|');
			var types = new List<FilePickerFileType>();
			for (int i = 0; i + 1 < parts.Length; i += 2)
			{
				types.Add(new FilePickerFileType(parts[i]) { Patterns = parts[i + 1].Split(';').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray() });
			}
			return types.ToArray();
		}

		// ---- buttons ---------------------------------------------------------------------------------------------------

		private void WireButtons()
		{
			previousButton.Click += (sender, e) => Previous();
			nextButton.Click += (sender, e) => Next();
			OkClicked += (sender, e) =>
			{
				RememberTab();
				model.Ok();
				SaveSize();
			};
			ApplyClicked += (sender, e) =>
			{
				model.Apply();
				SaveSize();
			};
			CancelClicked += (sender, e) => RememberTab();
			RememberFocusedBox();
		}

		// the box that had the focus last is focused again after Previous and Next (currentTextBox, FocusTextBox)
		private void RememberFocusedBox()
		{
			AddHandler(GotFocusEvent, (sender, e) =>
			{
				if (e.Source is TextBox box && box.IsEffectivelyEnabled)
				{
					lastFocusedBox = box;
				}
				else if (e.Source is ComboBox combo)
				{
					lastFocusedBox = combo;
				}
			}, RoutingStrategies.Bubble);
		}

		/// <summary>Previous: the book before (<c>btPrev_Click</c>), the box that was being edited stays selected.</summary>
		public void Previous()
		{
			previousButton.Focus();
			if (model.GoPrevious())
			{
				FocusLastBox();
			}
		}

		/// <summary>Next: the book after (<c>btNext_Click</c>).</summary>
		public void Next()
		{
			nextButton.Focus();
			if (model.GoNext())
			{
				FocusLastBox();
			}
		}

		private void FocusLastBox()
		{
			if (lastFocusedBox != null && lastFocusedBox.IsVisible && lastFocusedBox.IsEffectivelyEnabled)
			{
				lastFocusedBox.Focus();
				(lastFocusedBox as TextBox)?.SelectAll();
			}
		}

		private void RestoreTab()
		{
			var all = TabPages;
			if (lastActivePage >= 0 && lastActivePage < all.Count && all[lastActivePage].IsVisible)
			{
				tabs.SelectedItem = all[lastActivePage];
			}
			else
			{
				tabs.SelectedItem = tabSummary;
			}
		}

		private void RememberTab()
		{
			int index = TabPages.ToList().IndexOf(tabs.SelectedItem as TabItem);
			if (index >= 0)
			{
				lastActivePage = index;
			}
		}

		// OK and Apply keep the size when it is larger than the minimum (UpdateSafeSize / ComicBookDialogOutputSize)
		private void SaveSize()
		{
			if (request.Preferences == null)
			{
				return;
			}
			Size size = ClientSize;
			bool minimum = size.Width <= DesignWidth + 0.5 && size.Height <= DesignHeight + 0.5;
			request.Preferences.InfoDialogWidth = minimum ? 0 : size.Width;
			request.Preferences.InfoDialogHeight = minimum ? 0 : size.Height;
		}

		// ---- lifetime --------------------------------------------------------------------------------------------------

		private void OnOpened()
		{
			Title = model.DialogTitle;
			Watch(model.Display);
			RefreshTileText();
			RefreshIcons();
			_ = LoadCoverAsync(model.Display);
		}

		private void OnClosed()
		{
			RememberTab();
			coverRequest?.Cancel();
			Watch(null);
			model.PropertyChanged -= OnModelChanged;
			summary.Tile.Clear();
			coverImage?.Release();
			coverImage = null;
		}
	}
}
