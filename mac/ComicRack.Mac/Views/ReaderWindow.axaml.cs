using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ComicRack.Mac.Diagnostics;
using ComicRack.Mac.ViewModels;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// A reader window: the page view, a status strip and the native menu of the reader. The keys go to
	/// <see cref="ReaderViewModel.HandleKey"/>; Esc and Cmd+W close the window. Closing disposes the view model, which records the
	/// page in the book and releases the book (<see cref="Services.ReaderManager"/> does that through <see cref="Closed"/>; a window
	/// made without the manager, as the tests do, passes <paramref name="ownsModel"/>).
	/// </summary>
	public partial class ReaderWindow : Window
	{
		private readonly ReaderViewModel model;
		private readonly bool ownsModel;
		private readonly PerfOverlayController perf;

		public ReaderWindow()
		{
			InitializeComponent();
		}

		public ReaderWindow(ReaderViewModel model, bool ownsModel = false, PerfOverlayController perf = null)
		{
			InitializeComponent();
			this.model = model;
			this.ownsModel = ownsModel;
			DataContext = model;
			model.CloseRequested += OnCloseRequested;
			model.PropertyChanged += OnModelChanged;
			this.perf = perf;
			if (perf != null)
			{
				this.FindControl<Grid>("ContentArea").Children.Add(new PerfOverlay(perf, StopwatchClock.Now));
			}
			NativeMenu.SetMenu(this, ReaderMenu.Build(model, perf));
			Opened += (sender, e) => ReadingArea.Focus();
		}

		public ReaderViewModel Model => model;

		public ReaderView ReadingArea => this.FindControl<ReaderView>("PageView");

		private void OnCloseRequested(object sender, EventArgs e)
		{
			Close();
		}

		// Native full screen: the window follows the view model, and the view model follows the window (the green button, or macOS itself).
		private void OnModelChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(ReaderViewModel.IsFullScreen))
			{
				WindowState wanted = model.IsFullScreen ? WindowState.FullScreen : WindowState.Normal;
				if (WindowState != wanted && (model.IsFullScreen || WindowState == WindowState.FullScreen))
				{
					WindowState = wanted;
				}
			}
		}

		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
		{
			base.OnPropertyChanged(change);
			if (change.Property == WindowStateProperty && model != null)
			{
				bool full = WindowState == WindowState.FullScreen;
				if (model.IsFullScreen != full)
				{
					model.IsFullScreen = full;
				}
			}
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (!e.Handled && perf != null && e.Key == Key.P && e.KeyModifiers == (KeyModifiers.Meta | KeyModifiers.Alt))
			{
				perf.Toggle();
				e.Handled = true;
				return;
			}
			if (!e.Handled && model != null && model.HandleKey(e.Key, e.KeyModifiers))
			{
				e.Handled = true;
			}
		}

		protected override void OnClosed(EventArgs e)
		{
			if (model != null)
			{
				model.CloseRequested -= OnCloseRequested;
				model.PropertyChanged -= OnModelChanged;
				if (ownsModel)
				{
					model.Dispose();
				}
			}
			base.OnClosed(e);
		}
	}
}
