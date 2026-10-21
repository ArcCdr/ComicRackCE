using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// A text panel over the page (<c>TextOverlay</c> of <c>ComicRack.Engine.Display.Forms</c>): the black glass picture with white text in the
	/// reader font (Segoe UI 12 pt: the system font at 16 units). The page number at the top right (150 x 25), "Loading Page N..." (200 x 30) and
	/// the message in the middle (300 x 30) are three of them. The size follows the text: the text is measured with a maximum width of
	/// <see cref="MaxSize"/> less the glass margins (15 each way) and the panel is never smaller than <see cref="MinSize"/> (<c>TextOverlay.Resize</c>).
	/// The place in the window is the layout's job (the view aligns it; the original aligns inside the reader's 16 unit padding).
	/// </summary>
	public class CrTextOverlay : CrReaderOverlay
	{
		public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<CrTextOverlay, string>(nameof(Text));

		private Size minSize = new Size(150, 25);
		private Size maxSize = new Size(400, 400);

		static CrTextOverlay()
		{
			AffectsMeasure<CrTextOverlay>(TextProperty);
			AffectsRender<CrTextOverlay>(TextProperty);
		}

		public CrTextOverlay()
		{
			IsHitTestVisible = false;
		}

		/// <summary>The text (several lines are wrapped at the maximum width).</summary>
		public string Text
		{
			get => GetValue(TextProperty);
			set => SetValue(TextProperty, value);
		}

		/// <summary>The smallest size: the size the original gives each overlay (150 x 25, 200 x 30, 300 x 30).</summary>
		public Size MinSize
		{
			get => minSize;
			set
			{
				minSize = value;
				InvalidateMeasure();
			}
		}

		/// <summary>The largest size (<c>TextOverlay.MaxSize</c>, 400 x 400).</summary>
		public Size MaxSize
		{
			get => maxSize;
			set
			{
				maxSize = value;
				InvalidateMeasure();
			}
		}

		private FormattedText Format()
		{
			double width = Math.Max(1, maxSize.Width - GlassPainter.PanelMargin.Left - GlassPainter.PanelMargin.Right);
			var text = new FormattedText(Text ?? string.Empty, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), ChromeFonts.OverlaySize, ChromePaint.ResourceBrush(this, "Cr.Reader.OverlayTextBrush") ?? Brushes.Transparent)
			{
				MaxTextWidth = width,
				TextAlignment = TextAlignment.Center,
			};
			return text;
		}

		protected override Size ContentSize()
		{
			FormattedText text = Format();
			double width = Math.Ceiling(text.Width) + GlassPainter.PanelMargin.Left + GlassPainter.PanelMargin.Right;
			double height = Math.Ceiling(text.Height) + GlassPainter.PanelMargin.Top + GlassPainter.PanelMargin.Bottom;
			return new Size(Math.Min(maxSize.Width, Math.Max(minSize.Width, width)), Math.Min(maxSize.Height, Math.Max(minSize.Height, height)));
		}

		public override void Render(DrawingContext context)
		{
			var area = new Rect(Bounds.Size);
			Rect inner = GlassPainter.Panel(context, this, area);
			// a window that is being resized (leaving full screen) can lay the overlay out with no room for a moment: nothing to draw then
			if (string.IsNullOrEmpty(Text) || inner.Width <= 0 || inner.Height <= 0)
			{
				return;
			}
			FormattedText text = Format();
			text.MaxTextWidth = inner.Width;
			text.MaxTextHeight = inner.Height;
			context.DrawText(text, new Point(inner.X, inner.Y + Math.Max(0, (inner.Height - text.Height) / 2)));
		}
	}
}
