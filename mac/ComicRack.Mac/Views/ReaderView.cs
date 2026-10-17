using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Reader;
using ComicRack.Mac.ViewModels;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The reading area: draws the scene of its <see cref="ReaderViewModel"/> (the DataContext) with Skia. <see cref="Render"/> runs
	/// when the scene, the scroll position, the fit mode or the size changes (Avalonia keeps the recorded frame otherwise); the pixels
	/// are drawn by the render thread through <see cref="ReaderDrawOperation"/>, one <c>DrawImageRect</c> per page.
	/// The view takes no reference of its own: the draw operation holds the pictures for as long as a frame needs them.
	/// Wheel and trackpad events go to <see cref="ReaderViewModel.HandleWheel"/>.
	/// </summary>
	public sealed class ReaderView : Control
	{
		/// <summary>The colour around the page: a neutral dark grey, as readers use so that the page is what the eye sees.</summary>
		public static readonly IBrush PaperBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

		private ReaderViewModel model;

		public ReaderView()
		{
			Focusable = true;
			ClipToBounds = true;
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
			context.DrawRectangle(PaperBrush, null, area);
			ReaderViewModel m = model;
			ReaderScene scene = m?.Scene;
			if (m == null || scene == null || scene.Pages.Count == 0 || area.Width <= 0 || area.Height <= 0)
			{
				return;
			}
			PageArrangement arrangement = m.Layout.Arrange(area.Size, scene.Sizes(), m.Fit, m.ScrollY);
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
