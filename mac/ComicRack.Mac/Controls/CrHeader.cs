using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>What a column header shows besides its text (<c>HeaderAdornments</c>).</summary>
	[Flags]
	public enum HeaderAdornments
	{
		None = 0,
		SortUp = 1,
		SortDown = 2,
		DropDown = 4,
	}

	/// <summary>The states of a column header (<c>HeaderState</c>).</summary>
	public enum HeaderState
	{
		Normal,
		Hot,
		Pressed,
		Active,
	}

	/// <summary>
	/// The column header of the original (C-HDR-1, <c>HeaderControl.Draw</c>): <c>HeaderBack</c> fill, a one unit frame in the
	/// <c>HeaderSeparator</c> colour with the selection style alpha of the state (pressed = selected and hot, hot = selected, active and
	/// normal = hot), the sort arrow (<c>SortUp</c> or <c>SortDown</c>) at the top centre with 0.7 opacity, an optional drop-down part
	/// (a split line and <c>SmallArrowDown</c>) and the text centred with 3 above and below. The state follows the pointer unless
	/// <see cref="Active"/> marks the sorted column.
	/// </summary>
	public class CrHeader : Control, IChromeElement
	{
		public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<CrHeader, string>(nameof(Text), string.Empty);

		public static readonly StyledProperty<HeaderAdornments> AdornmentsProperty = AvaloniaProperty.Register<CrHeader, HeaderAdornments>(nameof(Adornments));

		public static readonly StyledProperty<bool> ActiveProperty = AvaloniaProperty.Register<CrHeader, bool>(nameof(Active));

		public static readonly StyledProperty<TextAlignment> TextAlignmentProperty = AvaloniaProperty.Register<CrHeader, TextAlignment>(nameof(TextAlignment), TextAlignment.Center);

		private bool pressed;

		static CrHeader()
		{
			AffectsRender<CrHeader>(TextProperty, AdornmentsProperty, ActiveProperty, TextAlignmentProperty, IsPointerOverProperty);
		}

		public CrHeader()
		{
			Height = ChromeMetrics.ColumnHeaderHeight;
			Focusable = false;
			TextElement.SetFontSize(this, ChromeFonts.ItemSize);
			ActualThemeVariantChanged += (sender, e) => InvalidateVisual();
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.ListHeader;

		public string Text
		{
			get => GetValue(TextProperty);
			set => SetValue(TextProperty, value);
		}

		public HeaderAdornments Adornments
		{
			get => GetValue(AdornmentsProperty);
			set => SetValue(AdornmentsProperty, value);
		}

		/// <summary>True for the column the list is sorted by.</summary>
		public bool Active
		{
			get => GetValue(ActiveProperty);
			set => SetValue(ActiveProperty, value);
		}

		public TextAlignment TextAlignment
		{
			get => GetValue(TextAlignmentProperty);
			set => SetValue(TextAlignmentProperty, value);
		}

		/// <summary>Shows the header in a fixed state whatever the pointer does (the chrome gallery and tests); null follows the pointer.</summary>
		public HeaderState? DisplayState
		{
			get => displayState;
			set
			{
				displayState = value;
				InvalidateVisual();
			}
		}

		private HeaderState? displayState;

		public HeaderState State => displayState ?? (pressed ? HeaderState.Pressed : IsPointerOver ? HeaderState.Hot : Active ? HeaderState.Active : HeaderState.Normal);

		/// <summary>Raised when the header is clicked (button released over it).</summary>
		public event EventHandler Clicked;

		/// <summary>Raised when the drop-down part is clicked.</summary>
		public event EventHandler DropDownClicked;

		/// <summary>The drop-down part at the right (<c>GetDropDownBounds</c>): the arrow image plus 4, inside the 2 unit text inset.</summary>
		public Rect DropDownBounds => DropDownBoundsOf(this, Bounds.Size);

		private static Rect DropDownBoundsOf(Control owner, Size size)
		{
			Rect inner = new Rect(2, 0, Math.Max(0, size.Width - 4), size.Height);
			double w = Math.Min(Icons.TryGet("cYo.Common.Windows:SmallArrowDown", ChromePaint.VariantOf(owner))?.Size.Width + 4 ?? 10, inner.Width);
			return new Rect(inner.Right - w, inner.Top, w, inner.Height);
		}

		/// <summary>The selection style that the frame of a state uses (<c>HeaderControl.Draw</c>).</summary>
		public static SelectionState FrameStyle(HeaderState state)
		{
			switch (state)
			{
				case HeaderState.Pressed:
					return SelectionState.SelectedHot;
				case HeaderState.Hot:
					return SelectionState.Selected;
				default:
					return SelectionState.Hot;
			}
		}

		public override void Render(DrawingContext context)
		{
			Draw(context, this, new Rect(Bounds.Size), Text, TextAlignment, Adornments, State, TextElement.GetFontFamily(this), TextElement.GetFontSize(this));
		}

		/// <summary>
		/// Draws a column header into <paramref name="area"/> (<c>HeaderControl.Draw</c>); the page list draws the headers of its columns with it. The area's origin is the origin of the drawing.
		/// </summary>
		public static void Draw(DrawingContext context, Control owner, Rect area, string text, TextAlignment alignment, HeaderAdornments adornments, HeaderState state, FontFamily family, double fontSize)
		{
			using (context.PushTransform(Matrix.CreateTranslation(area.X, area.Y)))
			{
				var local = new Rect(area.Size);
				context.DrawRectangle(ChromePaint.Brush(owner, ThemeTokens.Names.HeaderBack), null, local);
				var style = new StyledSelection(FrameStyle(state), ThemeTokens.Get(ThemeTokens.Names.HeaderSeparator, ChromePaint.VariantOf(owner)));
				ChromePaint.Selection(context, local, style);
				Rect inner = new Rect(2, 0, Math.Max(0, local.Width - 4), local.Height);
				Rect textArea = inner;
				if ((adornments & HeaderAdornments.DropDown) != 0)
				{
					Rect drop = DropDownBoundsOf(owner, local.Size);
					ChromePaint.VLine(context, ChromePaint.Brush(owner, ThemeTokens.Names.ControlDark), drop.X, 4, local.Height - 8);
					ChromePaint.VLine(context, ChromePaint.Brush(owner, ThemeTokens.Names.ControlLight), drop.X + 1, 4, local.Height - 8);
					Bitmap arrow = Icons.TryGet("cYo.Common.Windows:SmallArrowDown", ChromePaint.VariantOf(owner));
					if (arrow != null)
					{
						ChromePaint.Image(context, arrow, ChromePaint.Centered(arrow.Size, drop));
					}
					textArea = new Rect(inner.X, inner.Y, Math.Max(0, drop.X - inner.X), inner.Height);
				}
				string sort = (adornments & HeaderAdornments.SortDown) != 0 ? "cYo.Common.Windows:SortDown" : (adornments & HeaderAdornments.SortUp) != 0 ? "cYo.Common.Windows:SortUp" : null;
				if (sort != null)
				{
					Bitmap image = Icons.TryGet(sort, ChromePaint.VariantOf(owner));
					if (image != null)
					{
						var target = new Rect(Math.Floor(textArea.X + (textArea.Width - image.Size.Width) / 2), textArea.Y + 1, image.Size.Width, image.Size.Height);
						ChromePaint.Image(context, image, target, 0.7);
					}
				}
				if (!string.IsNullOrEmpty(text))
				{
					var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(family), fontSize, ChromePaint.Brush(owner, ThemeTokens.Names.ControlText))
					{
						MaxTextWidth = Math.Max(0, textArea.Width),
						Trimming = TextTrimming.CharacterEllipsis,
						TextAlignment = alignment,
					};
					formatted.MaxTextHeight = formatted.Height;
					context.DrawText(formatted, new Point(textArea.X, Math.Floor(textArea.Y + 3 + Math.Max(0, (textArea.Height - 6 - formatted.Height) / 2))));
				}
			}
		}

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			{
				pressed = true;
				e.Pointer.Capture(this);
				InvalidateVisual();
			}
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			base.OnPointerReleased(e);
			if (!pressed)
			{
				return;
			}
			pressed = false;
			e.Pointer.Capture(null);
			Point p = e.GetPosition(this);
			if (new Rect(Bounds.Size).Contains(p))
			{
				if ((Adornments & HeaderAdornments.DropDown) != 0 && DropDownBounds.Contains(p))
				{
					DropDownClicked?.Invoke(this, EventArgs.Empty);
				}
				else
				{
					Clicked?.Invoke(this, EventArgs.Empty);
				}
			}
			InvalidateVisual();
		}
	}
}
