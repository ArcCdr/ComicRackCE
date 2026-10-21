using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>Which half of a <see cref="CrSpinButton"/>.</summary>
	public enum SpinPart
	{
		None,
		Up,
		Down,
	}

	/// <summary>
	/// The little up and down buttons at the right of a number box (<c>cYo.Common.Windows.Forms.SpinButton</c>, <c>SpinButton.AddUpDown</c>): 11 wide as the box is high, two
	/// halves, each a flat button with a small arrow. A press raises <see cref="ButtonUp"/> or <see cref="ButtonDown"/> at once and again every
	/// <see cref="RepeatInterval"/> (250 ms) while the button stays down. The halves are drawn as the original's flat fallback does (<c>ControlPaint.DrawScrollButton</c>
	/// with <c>ButtonState.Flat</c>), from the button tokens, with one unit lines that stay crisp at every display scale.
	/// </summary>
	public sealed class CrSpinButton : Control, IChromeElement
	{
		/// <summary>The width of the buttons next to a number box (<c>FormUtility.ScaleDpiX(11)</c>).</summary>
		public const double ButtonWidth = 11;

		private readonly DispatcherTimer repeat = new DispatcherTimer();
		private SpinPart hit;
		private SpinPart hot;
		private bool upEnabled = true;
		private bool downEnabled = true;

		public CrSpinButton()
		{
			repeat.Tick += (sender, e) => Press(hit);
			RepeatInterval = 250;
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.Buttons;

		/// <summary>Milliseconds between the presses while a button is held; 0 turns the repeat off.</summary>
		public int RepeatInterval { get; set; }

		public bool UpEnabled
		{
			get => upEnabled;
			set
			{
				if (upEnabled != value)
				{
					upEnabled = value;
					InvalidateVisual();
				}
			}
		}

		public bool DownEnabled
		{
			get => downEnabled;
			set
			{
				if (downEnabled != value)
				{
					downEnabled = value;
					InvalidateVisual();
				}
			}
		}

		public event EventHandler ButtonUp;

		public event EventHandler ButtonDown;

		/// <summary>The part pressed right now, or none.</summary>
		public SpinPart Pressed => hit;

		/// <summary>The rectangle of one half: the upper or the lower half of the bounds (<c>GetButtonBounds</c>).</summary>
		public static Rect PartBounds(Rect bounds, SpinPart part)
		{
			double half = Math.Floor(bounds.Height / 2);
			return part == SpinPart.Down ? new Rect(bounds.X, bounds.Y + half, bounds.Width, bounds.Height - half) : new Rect(bounds.X, bounds.Y, bounds.Width, half);
		}

		/// <summary>Which enabled half contains the point (<c>HitTest</c>).</summary>
		public static SpinPart HitTest(Rect bounds, Point point, bool up = true, bool down = true)
		{
			if (up && PartBounds(bounds, SpinPart.Up).Contains(point))
			{
				return SpinPart.Up;
			}
			if (down && PartBounds(bounds, SpinPart.Down).Contains(point))
			{
				return SpinPart.Down;
			}
			return SpinPart.None;
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			return new Size(ButtonWidth, double.IsInfinity(availableSize.Height) ? 20 : availableSize.Height);
		}

		/// <summary>Presses a half as the mouse does (the key handling of a text box calls it too).</summary>
		public void Press(SpinPart part)
		{
			if (part == SpinPart.Up && upEnabled && IsEffectivelyEnabled)
			{
				ButtonUp?.Invoke(this, EventArgs.Empty);
			}
			else if (part == SpinPart.Down && downEnabled && IsEffectivelyEnabled)
			{
				ButtonDown?.Invoke(this, EventArgs.Empty);
			}
		}

		protected override void OnPointerMoved(PointerEventArgs e)
		{
			base.OnPointerMoved(e);
			if (hit == SpinPart.None)
			{
				SpinPart part = HitTest(new Rect(Bounds.Size), e.GetPosition(this), upEnabled, downEnabled);
				if (part != hot)
				{
					hot = part;
					InvalidateVisual();
				}
			}
		}

		protected override void OnPointerExited(PointerEventArgs e)
		{
			base.OnPointerExited(e);
			if (hot != SpinPart.None)
			{
				hot = SpinPart.None;
				InvalidateVisual();
			}
		}

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			{
				return;
			}
			hit = HitTest(new Rect(Bounds.Size), e.GetPosition(this), upEnabled, downEnabled);
			if (hit != SpinPart.None)
			{
				e.Pointer.Capture(this);
				InvalidateVisual();
				Press(hit);
				if (RepeatInterval > 0)
				{
					repeat.Interval = TimeSpan.FromMilliseconds(RepeatInterval);
					repeat.Start();
				}
				e.Handled = true;
			}
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			base.OnPointerReleased(e);
			Release();
		}

		protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
		{
			base.OnPointerCaptureLost(e);
			Release();
		}

		private void Release()
		{
			if (hit != SpinPart.None)
			{
				hit = SpinPart.None;
				repeat.Stop();
				InvalidateVisual();
			}
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			repeat.Stop();
			hit = SpinPart.None;
		}

		public override void Render(DrawingContext context)
		{
			var area = new Rect(Bounds.Size);
			DrawPart(context, PartBounds(area, SpinPart.Up), SpinPart.Up, upEnabled);
			DrawPart(context, PartBounds(area, SpinPart.Down), SpinPart.Down, downEnabled);
		}

		private void DrawPart(DrawingContext context, Rect rect, SpinPart part, bool enabled)
		{
			enabled &= IsEffectivelyEnabled;
			bool pressed = enabled && hit == part;
			bool isHot = enabled && hot == part && hit == SpinPart.None;
			string back = pressed ? ThemeTokens.Names.ButtonPressedBack : isHot ? ThemeTokens.Names.ButtonHoverBack : ThemeTokens.Names.ButtonBack;
			string border = pressed ? ThemeTokens.Names.ButtonPressedBorder : isHot ? ThemeTokens.Names.ButtonHoverBorder : ThemeTokens.Names.ButtonBorder;
			context.DrawRectangle(ChromePaint.Brush(this, back), null, rect);
			ChromePaint.Frame(context, ChromePaint.Brush(this, border), rect);
			// the arrow: 5 wide and 3 high, in the middle of the half
			IBrush arrow = ChromePaint.Brush(this, enabled ? ThemeTokens.Names.ControlText : ThemeTokens.Names.GrayText);
			double cx = Math.Floor(rect.X + rect.Width / 2);
			double cy = Math.Floor(rect.Y + rect.Height / 2);
			for (int row = 0; row < 3; row++)
			{
				int width = part == SpinPart.Up ? 1 + 2 * row : 5 - 2 * row;
				double y = part == SpinPart.Up ? cy - 1 + row : cy - 1 + row;
				context.DrawRectangle(arrow, null, new Rect(cx - (width - 1) / 2.0, y, width, 1));
			}
		}
	}
}
