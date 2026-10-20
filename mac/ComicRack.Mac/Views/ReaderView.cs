using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Reader;
using ComicRack.Mac.ViewModels;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The reading area: draws the scene of its <see cref="ReaderViewModel"/> (the DataContext) with Skia. <see cref="Render"/> runs
	/// when the scene, the scroll position, the fit mode or the size changes (Avalonia keeps the recorded frame otherwise); the pixels
	/// are drawn by the render thread through <see cref="ReaderDrawOperation"/>, one <c>DrawImageRect</c> per page.
	/// The view takes no reference of its own: the draw operation holds the pictures for as long as a frame needs them.
	/// Wheel and trackpad events go to <see cref="ReaderViewModel.HandleWheel"/>; a drag pans a page that is larger than the area
	/// (the content follows the pointer); a pinch on the trackpad (the macOS magnify gesture) or on a touch screen zooms about the
	/// pointer.
	/// </summary>
	public sealed class ReaderView : Control
	{
		/// <summary>The theme resource of the colour around the page (<c>CrMvpLook.axaml</c>): a neutral dark grey, so that the page is what the eye sees.</summary>
		private const string PaperKey = "Cr.Mvp.ReaderPaperBrush";

		private ReaderViewModel model;
		private bool dragging;
		private Point lastPointer;

		public ReaderView()
		{
			Focusable = true;
			ClipToBounds = true;
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

		protected override Size MeasureOverride(Size availableSize)
		{
			return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			if (model != null)
			{
				model.ViewportSize = finalSize;
			}
			return base.ArrangeOverride(finalSize);
		}

		public override void Render(DrawingContext context)
		{
			var area = new Rect(Bounds.Size);
			context.DrawRectangle(ChromePaint.ResourceBrush(this, PaperKey), null, area);
			ReaderViewModel m = model;
			ReaderScene scene = m?.Scene;
			if (m == null || scene == null || scene.Pages.Count == 0 || area.Width <= 0 || area.Height <= 0)
			{
				return;
			}
			PageArrangement arrangement = m.ArrangeFor(area.Size);
			var images = new CoverImage[scene.Pages.Count];
			for (int i = 0; i < images.Length; i++)
			{
				images[i] = scene.Pages[i].Image;
			}
			context.Custom(new ReaderDrawOperation(images, arrangement.Pages, area, scene.Turn, m.Meter));
		}

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			Focus();
			if (model != null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && model.CanPan)
			{
				dragging = true;
				lastPointer = e.GetPosition(this);
				Cursor = new Cursor(StandardCursorType.SizeAll);
				e.Pointer.Capture(this);
				e.Handled = true;
			}
		}

		protected override void OnPointerMoved(PointerEventArgs e)
		{
			base.OnPointerMoved(e);
			if (dragging && model != null)
			{
				Point now = e.GetPosition(this);
				model.PanBy(lastPointer - now); // the content follows the pointer
				lastPointer = now;
				e.Handled = true;
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
			Cursor = null;
		}

		private void EndDrag(IPointer pointer)
		{
			if (dragging)
			{
				dragging = false;
				Cursor = null;
				if (ReferenceEquals(pointer?.Captured, this))
				{
					pointer.Capture(null);
				}
			}
		}

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
			if (model != null && model.HandleWheel(e.Delta, Environment.TickCount64))
			{
				e.Handled = true;
			}
		}
	}
}
