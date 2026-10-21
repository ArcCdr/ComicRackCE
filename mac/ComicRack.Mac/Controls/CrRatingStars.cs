using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>Where the stars of a <see cref="CrRatingStars"/> are, and what a point in them means (<c>RatingRenderer.GetStripDisplayBounds</c>, <c>GetRatingFromStrip</c>).</summary>
	public static class RatingStrip
	{
		/// <summary>
		/// The strip of <paramref name="count"/> pictures of <paramref name="imageSize"/> scaled to fit <paramref name="bounds"/> (the largest scale that
		/// fits, as <c>SizeF.ToRectangle</c> does); <paramref name="center"/> puts it in the middle of the bounds (<c>RectangleScaleMode.Center</c>), else in the top left corner.
		/// </summary>
		public static Rect Compute(Rect bounds, Size imageSize, int count, bool center)
		{
			if (imageSize.Width <= 0 || imageSize.Height <= 0 || count <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
			{
				return default;
			}
			double naturalWidth = imageSize.Width * count;
			double scale = Math.Min(bounds.Width / naturalWidth, bounds.Height / imageSize.Height);
			double width = naturalWidth * scale, height = imageSize.Height * scale;
			double x = bounds.X, y = bounds.Y;
			if (center)
			{
				x += (bounds.Width - width) / 2;
				y += (bounds.Height - height) / 2;
			}
			return new Rect(x, y, width, height);
		}

		/// <summary>The rating a point on the strip stands for: 0 left of it, the maximum right of it, else the share of the width times the maximum.</summary>
		public static double RatingAt(Rect strip, double x, int count)
		{
			if (strip.Width <= 0 || x < strip.X)
			{
				return 0;
			}
			if (x > strip.Right)
			{
				return count;
			}
			return (x - strip.X) / strip.Width * count;
		}
	}

	/// <summary>
	/// The rating editor of the original (<c>ComicRack.Engine/Controls/RatingControl.cs</c>): five <c>StarYellow</c> pictures, the rated part opaque and the
	/// rest at a quarter, optionally followed by the number in a small rounded frame. A press or a drag over the stars sets the rating (rounded to
	/// <see cref="RatingDigits"/>), the wheel and the arrow keys change it by one step, Page Up and Page Down by one, Home and End to 0 and the maximum, a digit
	/// sets that rating. <see cref="RatingChanged"/> is raised for the user's changes only. The rating menu of the browser hosts one (<c>InsertRatingControl</c>),
	/// the Quick Rating dialog another.
	/// </summary>
	public class CrRatingStars : Control, IChromeElement
	{
		public static readonly StyledProperty<double> RatingProperty = AvaloniaProperty.Register<CrRatingStars, double>(nameof(Rating), 0d, coerce: (o, v) => ((CrRatingStars)o).Coerce(v));

		public static readonly StyledProperty<int> MaximumRatingProperty = AvaloniaProperty.Register<CrRatingStars, int>(nameof(MaximumRating), 5);

		public static readonly StyledProperty<bool> DrawTextProperty = AvaloniaProperty.Register<CrRatingStars, bool>(nameof(DrawText), false);

		public static readonly StyledProperty<bool> DrawBorderProperty = AvaloniaProperty.Register<CrRatingStars, bool>(nameof(DrawBorder), true);

		public static readonly StyledProperty<bool> CenterRatingProperty = AvaloniaProperty.Register<CrRatingStars, bool>(nameof(CenterRating), true);

		private Rect strip;
		private bool pressed;

		static CrRatingStars()
		{
			AffectsRender<CrRatingStars>(RatingProperty, MaximumRatingProperty, DrawTextProperty, DrawBorderProperty, CenterRatingProperty);
			FocusableProperty.OverrideDefaultValue<CrRatingStars>(true);
		}

		public CrRatingStars()
		{
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.Buttons;

		/// <summary>The picture of one rating point (<c>RatingImage</c>), an original image by name.</summary>
		public string StarName { get; set; } = "StarYellow";

		/// <summary>Number of stars (<c>MaximumRating</c>, 0 to 100).</summary>
		public int MaximumRating
		{
			get => GetValue(MaximumRatingProperty);
			set => SetValue(MaximumRatingProperty, Math.Clamp(value, 0, 100));
		}

		/// <summary>Digits of the rating (<c>RatingDigits</c>): 1 gives steps of a tenth.</summary>
		public int RatingDigits { get; set; } = 1;

		/// <summary>The rating, 0 to <see cref="MaximumRating"/>, rounded to <see cref="RatingDigits"/>.</summary>
		public double Rating
		{
			get => GetValue(RatingProperty);
			set => SetValue(RatingProperty, value);
		}

		/// <summary>Also draw the number in a rounded frame at the right (<c>DrawText</c>).</summary>
		public bool DrawText
		{
			get => GetValue(DrawTextProperty);
			set => SetValue(DrawTextProperty, value);
		}

		/// <summary>Draw the frame and the field background (<c>DrawBorder</c>).</summary>
		public bool DrawBorder
		{
			get => GetValue(DrawBorderProperty);
			set => SetValue(DrawBorderProperty, value);
		}

		/// <summary>Put the stars in the middle of the room (<c>CenterRating</c>), else in the top left corner.</summary>
		public bool CenterRating
		{
			get => GetValue(CenterRatingProperty);
			set => SetValue(CenterRatingProperty, value);
		}

		/// <summary>True once the user changed the rating (a press, a drag, the wheel, a key); the rating menu applies the rating when it closes only if so.</summary>
		public bool UserChanged { get; set; }

		/// <summary>Raised when the user changed the rating.</summary>
		public event EventHandler RatingChanged;

		/// <summary>Where the stars are, in the coordinates of the control (empty before the first layout).</summary>
		public Rect Strip => StripFor(Bounds);

		private double Coerce(double value)
		{
			if (double.IsNaN(value))
			{
				value = 0;
			}
			return Math.Round(Math.Clamp(value, 0, MaximumRating), RatingDigits);
		}

		private Avalonia.Media.Imaging.Bitmap Star() => Icons.TryGet(StarName, ChromePaint.VariantOf(this));

		private string RatingText(double rating) => rating.ToString("N" + RatingDigits, CultureInfo.CurrentCulture);

		private FormattedText MakeText(string text, IBrush brush)
		{
			return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, ChromeFonts.ControlSize, brush);
		}

		// The room of the content: the control inflated by -2, and with the number its frame and 2 units taken off the right (DrawContent).
		private Rect StripFor(Rect control)
		{
			Rect content = new Rect(control.X + 2, control.Y + 2, Math.Max(0, control.Width - 4), Math.Max(0, control.Height - 4));
			if (DrawText)
			{
				double width = Math.Ceiling(MakeText(RatingText(MaximumRating), Brushes.Transparent).Width) + 4;
				content = new Rect(content.X, content.Y, Math.Max(0, content.Width - (width + 2)), content.Height);
			}
			Avalonia.Media.Imaging.Bitmap star = Star();
			return star == null ? default : RatingStrip.Compute(content, star.Size, MaximumRating, CenterRating);
		}

		public override void Render(DrawingContext context)
		{
			base.Render(context);
			var all = new Rect(Bounds.Size);
			if (DrawBorder)
			{
				context.DrawRectangle(ChromePaint.Brush(this, ThemeTokens.Names.Window), null, all);
				ChromePaint.Frame(context, ChromePaint.Brush(this, ThemeTokens.Names.ControlDark), all);
				if (IsKeyboardFocusWithin)
				{
					ChromePaint.Frame(context, ChromePaint.Brush(this, ThemeTokens.Names.Highlight), all.Deflate(1));
				}
			}
			Rect content = new Rect(2, 2, Math.Max(0, all.Width - 4), Math.Max(0, all.Height - 4));
			double rating = Rating;
			if (DrawText)
			{
				// the number in a frame with rounded corners, 1.5 wide, in the colour of a rated or an unrated book
				IBrush color = ChromePaint.Brush(this, rating != 0 ? ThemeTokens.Names.RatingRated : ThemeTokens.Names.RatingUnrated);
				FormattedText text = MakeText(RatingText(rating), color);
				double width = Math.Ceiling(text.Width) + 4;
				var box = new Rect(content.Right - width - 2, content.Top + (content.Height - text.Height) / 2, width, text.Height + 1);
				context.DrawRectangle(null, new Pen(color, 1.5), box, 2, 2);
				context.DrawText(text, new Point(box.X + (box.Width - text.Width) / 2, box.Y + (box.Height - text.Height) / 2));
				content = new Rect(content.X, content.Y, Math.Max(0, content.Width - (width + 2 + 2)), content.Height);
			}
			Avalonia.Media.Imaging.Bitmap star = Star();
			if (star == null)
			{
				return;
			}
			strip = RatingStrip.Compute(content, star.Size, MaximumRating, CenterRating);
			if (strip.Width <= 0)
			{
				return;
			}
			double share = Math.Clamp(rating / Math.Max(1, MaximumRating), 0, 1);
			double step = strip.Width / MaximumRating;
			double scale = strip.Height / star.Size.Height;
			DrawStars(context, star, new Rect(strip.X, strip.Y, strip.Width * share, strip.Height), step, scale, 1);
			DrawStars(context, star, new Rect(strip.X + strip.Width * share, strip.Y, strip.Width * (1 - share), strip.Height), step, scale, 0.25);
		}

		private void DrawStars(DrawingContext context, Avalonia.Media.Imaging.Bitmap star, Rect clip, double step, double scale, double alpha)
		{
			if (clip.Width <= 0 || clip.Height <= 0)
			{
				return;
			}
			using (context.PushClip(clip))
			{
				for (int i = 0; i < MaximumRating; i++)
				{
					double left = Math.Round(strip.X + i * step), right = Math.Round(strip.X + (i + 1) * step);
					ChromePaint.Image(context, star, new Rect(left, Math.Round(strip.Y), right - left, Math.Round(strip.Y + star.Size.Height * scale) - Math.Round(strip.Y)), alpha);
				}
			}
		}

		private void SetFromUser(double value)
		{
			double before = Rating;
			Rating = value;
			if (Rating != before)
			{
				UserChanged = true;
				RatingChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		private void SetFromPoint(Point point)
		{
			Rect s = Strip;
			SetFromUser(RatingStrip.RatingAt(s, point.X, MaximumRating));
		}

		private double Step => Math.Pow(10, -RatingDigits);

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			{
				return;
			}
			Focus();
			pressed = true;
			e.Pointer.Capture(this);
			SetFromPoint(e.GetPosition(this));
			e.Handled = true;
		}

		protected override void OnPointerMoved(PointerEventArgs e)
		{
			base.OnPointerMoved(e);
			if (pressed)
			{
				SetFromPoint(e.GetPosition(this));
				e.Handled = true;
			}
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			base.OnPointerReleased(e);
			if (pressed)
			{
				pressed = false;
				e.Pointer.Capture(null);
				e.Handled = true;
			}
		}

		protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
		{
			base.OnPointerWheelChanged(e);
			SetFromUser(Rating + Math.Sign(e.Delta.Y) * Step);
			e.Handled = true;
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			switch (e.Key)
			{
				case Key.Up:
				case Key.Right:
					SetFromUser(Rating + Step);
					break;
				case Key.Left:
				case Key.Down:
					SetFromUser(Rating - Step);
					break;
				case Key.PageUp:
					SetFromUser(Rating + 1);
					break;
				case Key.PageDown:
					SetFromUser(Rating - 1);
					break;
				case Key.Home:
					SetFromUser(0);
					break;
				case Key.End:
					SetFromUser(MaximumRating);
					break;
				default:
					if (e.Key >= Key.D0 && e.Key <= Key.D9 && e.KeyModifiers == KeyModifiers.None)
					{
						SetFromUser(e.Key - Key.D0);
						break;
					}
					return;
			}
			e.Handled = true;
		}

		protected override void OnGotFocus(FocusChangedEventArgs e)
		{
			base.OnGotFocus(e);
			InvalidateVisual();
		}

		protected override void OnLostFocus(FocusChangedEventArgs e)
		{
			base.OnLostFocus(e);
			InvalidateVisual();
		}
	}
}
