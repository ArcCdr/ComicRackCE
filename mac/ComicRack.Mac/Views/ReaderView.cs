using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Themes;
using ComicRack.Mac.ViewModels;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The reading area (<c>ComicDisplayControl</c>): the behaviour on top of <see cref="CrReaderPage"/>, which draws the scene of its
	/// <see cref="ReaderViewModel"/> (the DataContext, set as <see cref="CrReaderPage.Source"/>). The page is drawn again (<c>InvalidateVisual</c>) when the
	/// scene, the scroll position, the zoom, the fit mode or the size changes (Avalonia keeps the recorded frame otherwise); how it looks is the chrome layer's.
	/// Wheel and trackpad events go to <see cref="ReaderViewModel.HandleWheel"/>; Command+wheel zooms; a drag pans a page that is larger than the area
	/// (the content follows the pointer); a pinch on the trackpad (the macOS magnify gesture) or on a touch screen zooms about the pointer; a double
	/// click toggles full screen. In full screen the cursor is hidden after <see cref="AutoHideCursorDelay"/> without movement (<c>AutoHideCursor</c>).
	/// </summary>
	public sealed class ReaderView : CrReaderPage
	{
		private ReaderViewModel model;
		private bool dragging;
		private Point lastPointer;
		private bool autoHideCursor;
		private bool cursorHidden;
		private bool pointerInside;
		private int autoHideDelay = 5000;
		private DispatcherTimer cursorTimer;

		/// <summary>
		/// True (the main reader) lets a double click toggle full screen (<c>CommandKey.MouseDoubleLeft</c>); false (the small preview, whose keyboard map maps it to
		/// <c>OpenBook</c>) raises <see cref="DoubleClicked"/> instead.
		/// </summary>
		public bool DoubleClickTogglesFullScreen { get; set; } = true;

		/// <summary>A double click with the left button when <see cref="DoubleClickTogglesFullScreen"/> is off.</summary>
		public event EventHandler DoubleClicked;

		/// <summary>
		/// True (the small preview, whose keyboard map has only <c>MoveUp</c> and <c>MoveDown</c> on the wheel) lets the wheel scroll the page up and down and nothing else:
		/// no turning of the page by a swipe, no zoom.
		/// </summary>
		public bool WheelOnlyScrolls { get; set; }

		public ReaderView()
		{
			Focusable = true;
			AddHandler(PointerTouchPadGestureMagnifyEvent, OnMagnify);
			GestureRecognizers.Add(new PinchGestureRecognizer());
			AddHandler(PinchEvent, OnPinch);
			AddHandler(PinchEndedEvent, (sender, e) => lastPinchScale = 1.0);
		}

		protected override void OnDataContextChanged(EventArgs e)
		{
			base.OnDataContextChanged(e);
			Attach(DataContext as ReaderViewModel);
		}

		private void Attach(ReaderViewModel next)
		{
			if (ReferenceEquals(next, model))
			{
				return;
			}
			if (model != null)
			{
				model.PropertyChanged -= OnModelChanged;
			}
			model = next;
			Source = model;
			if (model != null)
			{
				model.PropertyChanged += OnModelChanged;
				if (Bounds.Width > 0 && Bounds.Height > 0)
				{
					model.ViewportSize = Bounds.Size;
				}
			}
			InvalidateVisual();
		}

		private void OnModelChanged(object sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(ReaderViewModel.Scene):
				case nameof(ReaderViewModel.ScrollY):
				case nameof(ReaderViewModel.ScrollX):
				case nameof(ReaderViewModel.Zoom):
				case nameof(ReaderViewModel.RightToLeft):
				case nameof(ReaderViewModel.Fit):
				case nameof(ReaderViewModel.ViewportSize):
					InvalidateVisual();
					break;
			}
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			if (model != null)
			{
				model.ViewportSize = finalSize;
			}
			return base.ArrangeOverride(finalSize);
		}

		// ---- the pointer ----------------------------------------------------------------------------------------------

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			Focus();
			var properties = e.GetCurrentPoint(this).Properties;
			if (model != null && properties.IsLeftButtonPressed && e.ClickCount == 2)
			{
				// RK.16: a double click with the left mouse button toggles full screen (CommandKey.MouseDoubleLeft)
				if (DoubleClickTogglesFullScreen)
				{
					model.ToggleFullScreen();
				}
				else
				{
					DoubleClicked?.Invoke(this, EventArgs.Empty);
				}
				e.Handled = true;
				return;
			}
			if (model != null && properties.IsLeftButtonPressed && model.CanPan)
			{
				dragging = true;
				lastPointer = e.GetPosition(this);
				e.Pointer.Capture(this);
				UpdateCursor();
				e.Handled = true;
			}
		}

		protected override void OnPointerMoved(PointerEventArgs e)
		{
			base.OnPointerMoved(e);
			pointerInside = true;
			ShowCursorAgain();
			if (dragging && model != null)
			{
				Point now = e.GetPosition(this);
				model.PanBy(lastPointer - now); // the content follows the pointer
				lastPointer = now;
				e.Handled = true;
			}
		}

		protected override void OnPointerEntered(PointerEventArgs e)
		{
			base.OnPointerEntered(e);
			pointerInside = true;
			RestartCursorTimer();
		}

		protected override void OnPointerExited(PointerEventArgs e)
		{
			base.OnPointerExited(e);
			pointerInside = false;
			cursorTimer?.Stop();
			if (cursorHidden)
			{
				cursorHidden = false;
				UpdateCursor();
			}
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			base.OnPointerReleased(e);
			EndDrag(e.Pointer);
		}

		protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
		{
			base.OnPointerCaptureLost(e);
			dragging = false;
			UpdateCursor();
		}

		private void EndDrag(IPointer pointer)
		{
			if (dragging)
			{
				dragging = false;
				UpdateCursor();
				if (ReferenceEquals(pointer?.Captured, this))
				{
					pointer.Capture(null);
				}
			}
		}

		// ---- the cursor -----------------------------------------------------------------------------------------------

		/// <summary><c>AutoHideCursor</c>: hide the cursor after <see cref="AutoHideCursorDelay"/> milliseconds without movement while it is over the view (full screen with <c>HideCursorFullScreen</c>).</summary>
		public bool AutoHideCursor
		{
			get => autoHideCursor;
			set
			{
				if (autoHideCursor == value)
				{
					return;
				}
				autoHideCursor = value;
				ShowCursorAgain();
			}
		}

		/// <summary><c>AutoHideCursorDelay</c>: 5000 ms by default (<c>ExtendedSettings.AutoHideCursorDuration</c>).</summary>
		public int AutoHideCursorDelay
		{
			get => autoHideDelay;
			set
			{
				autoHideDelay = value;
				RestartCursorTimer();
			}
		}

		/// <summary>True while the cursor is hidden by the auto-hide.</summary>
		public bool CursorHidden => cursorHidden;

		/// <summary>The delay ran out: hide the cursor if it is over the view (a test calls this instead of waiting).</summary>
		public void HideCursorNow()
		{
			cursorTimer?.Stop();
			if (autoHideCursor && pointerInside && !cursorHidden)
			{
				cursorHidden = true;
				UpdateCursor();
			}
		}

		/// <summary>Movement shows the cursor again and starts the delay anew (<c>UpdateCursorAutoHide</c>).</summary>
		private void ShowCursorAgain()
		{
			if (cursorHidden)
			{
				cursorHidden = false;
				UpdateCursor();
			}
			RestartCursorTimer();
		}

		private void RestartCursorTimer()
		{
			if (!autoHideCursor || autoHideDelay <= 0 || !pointerInside)
			{
				cursorTimer?.Stop();
				return;
			}
			if (cursorTimer == null)
			{
				cursorTimer = new DispatcherTimer();
				cursorTimer.Tick += (sender, e) => HideCursorNow();
			}
			cursorTimer.Stop();
			cursorTimer.Interval = TimeSpan.FromMilliseconds(autoHideDelay);
			cursorTimer.Start();
		}

		private void UpdateCursor()
		{
			if (cursorHidden)
			{
				Cursor = new Cursor(StandardCursorType.None);
			}
			else
			{
				Cursor = dragging ? new Cursor(StandardCursorType.SizeAll) : null;
			}
		}

		// ---- gestures and wheel ---------------------------------------------------------------------------------------

		// The macOS pinch: Delta.X is the change of magnification of this event (0.1 = 10 % larger).
		private void OnMagnify(object sender, PointerDeltaEventArgs e)
		{
			if (model != null)
			{
				model.ZoomBy(1.0 + e.Delta.X, e.GetPosition(this));
				e.Handled = true;
			}
		}

		// A pinch on a touch screen: Scale is the total scale since the gesture began, so each event applies the change from the last.
		private double lastPinchScale = 1.0;

		private void OnPinch(object sender, PinchEventArgs e)
		{
			if (model == null)
			{
				return;
			}
			double step = e.Scale / (lastPinchScale > 0 ? lastPinchScale : 1.0);
			lastPinchScale = e.Scale;
			model.ZoomBy(step, e.ScaleOrigin);
			e.Handled = true;
		}

		protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
		{
			base.OnPointerWheelChanged(e);
			if (model == null)
			{
				return;
			}
			if (WheelOnlyScrolls)
			{
				model.ScrollBy(-e.Delta.Y * model.Options.WheelScrollStep);
				e.Handled = true;
				return;
			}
			if ((e.KeyModifiers & KeyModifiers.Meta) != 0)
			{
				// RK.26: Command+wheel zooms (Ctrl+wheel of the original), a notch is 0.1 of the zoom
				model.ZoomByWheel(e.Delta.Y, e.GetPosition(this));
				e.Handled = true;
				return;
			}
			if (model.HandleWheel(e.Delta, Environment.TickCount64))
			{
				e.Handled = true;
			}
		}
	}
}
