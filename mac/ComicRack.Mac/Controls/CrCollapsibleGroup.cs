using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// The preference group box of the original (C-GRP-1, <c>CollapsibleGroupBox</c>): a 24 high header with a left to right gradient
	/// (<c>CollapsibleGroupBoxHeaderStart</c> to <c>CollapsibleGroupBoxHeaderEnd</c>), a toggle arrow (<c>SimpleArrowDown</c> or <c>SimpleArrowRight</c>)
	/// in a 24 x 24 square, the header text in bold, and the content below. A click on the toggle square or a double click on the header flips
	/// <see cref="IsExpanded"/>; collapsed, the control is only the header high (the original does not animate it).
	/// </summary>
	public class CrCollapsibleGroup : Avalonia.Controls.Primitives.HeaderedContentControl
	{
		public static readonly StyledProperty<bool> IsExpandedProperty = AvaloniaProperty.Register<CrCollapsibleGroup, bool>(nameof(IsExpanded), true);

		static CrCollapsibleGroup()
		{
			AffectsRender<CrCollapsibleGroup>(IsExpandedProperty, HeaderProperty);
		}

		public CrCollapsibleGroup()
		{
			FontSize = ChromeFonts.ControlSize;
			ActualThemeVariantChanged += (sender, e) => InvalidateVisual();
		}

		protected override Type StyleKeyOverride => typeof(CrCollapsibleGroup);

		public bool IsExpanded
		{
			get => GetValue(IsExpandedProperty);
			set => SetValue(IsExpandedProperty, value);
		}

		/// <summary><c>CollapsibleGroupBox.Collapsed</c>.</summary>
		public bool Collapsed
		{
			get => !IsExpanded;
			set => IsExpanded = !value;
		}

		/// <summary>The header rectangle (24 high, full width).</summary>
		public Rect HeaderBounds => new Rect(0, 0, Bounds.Width, ChromeMetrics.GroupHeaderHeight);

		/// <summary>The toggle square at the left of the header (<c>ToggleRectange</c>).</summary>
		public Rect ToggleBounds => new Rect(0, 0, ChromeMetrics.GroupHeaderHeight, ChromeMetrics.GroupHeaderHeight);

		public event EventHandler CollapsedChanged;

		/// <summary>Raised after the user flipped the state with the toggle square or a double click (<c>CollapseClicked</c>).</summary>
		public event EventHandler CollapseClicked;

		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
		{
			base.OnPropertyChanged(change);
			if (change.Property == IsExpandedProperty)
			{
				CollapsedChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		public override void Render(DrawingContext context)
		{
			var area = new Rect(Bounds.Size);
			context.DrawRectangle(ChromePaint.Brush(this, ThemeTokens.Names.CollapsibleGroupBoxBack), null, area);
			Rect header = HeaderBounds;
			context.DrawRectangle(ChromePaint.HorizontalGradient(this, ThemeTokens.Names.CollapsibleGroupBoxHeaderStart, ThemeTokens.Names.CollapsibleGroupBoxHeaderEnd), null, header);
			Bitmap arrow = Icons.TryGet(IsExpanded ? "SimpleArrowDown" : "SimpleArrowRight", ChromePaint.VariantOf(this));
			if (arrow != null)
			{
				ChromePaint.Image(context, arrow, ChromePaint.Centered(arrow.Size, ToggleBounds));
			}
			string text = Header?.ToString();
			if (!string.IsNullOrEmpty(text))
			{
				var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyle.Normal, FontWeight.Bold), FontSize, ChromePaint.Brush(this, ThemeTokens.Names.CollapsibleGroupBoxHeaderText))
				{
					MaxTextWidth = Math.Max(0, header.Width - ToggleBounds.Width),
					Trimming = TextTrimming.CharacterEllipsis,
				};
				formatted.MaxTextHeight = formatted.Height;
				context.DrawText(formatted, new Point(ToggleBounds.Width, Math.Floor((header.Height - formatted.Height) / 2)));
			}
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			base.OnPointerReleased(e);
			if (e.InitialPressMouseButton == MouseButton.Left && ToggleBounds.Contains(e.GetPosition(this)))
			{
				Flip();
			}
		}

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			if (e.ClickCount == 2 && HeaderBounds.Contains(e.GetPosition(this)) && !ToggleBounds.Contains(e.GetPosition(this)))
			{
				Flip();
			}
		}

		private void Flip()
		{
			IsExpanded = !IsExpanded;
			CollapseClicked?.Invoke(this, EventArgs.Empty);
		}
	}
}
