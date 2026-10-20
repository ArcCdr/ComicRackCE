using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>The three counters of the library tree (<c>LibraryGauges</c>).</summary>
	public enum GaugeKind
	{
		Total,
		Unread,
		New,
	}

	/// <summary>One counter as it is drawn: its rectangle (relative to the left edge of the strip), the text ("" for a bar) and the corners that are rounded.</summary>
	public readonly struct GaugeCell
	{
		public GaugeCell(GaugeKind kind, Rect bounds, string text, bool roundLeft, bool roundRight)
		{
			Kind = kind;
			Bounds = bounds;
			Text = text;
			RoundLeft = roundLeft;
			RoundRight = roundRight;
		}

		public GaugeKind Kind { get; }

		public Rect Bounds { get; }

		public string Text { get; }

		public bool RoundLeft { get; }

		public bool RoundRight { get; }
	}

	/// <summary>
	/// The geometry of the counters behind a name in the library tree, the arithmetic of <c>LibraryTreeSkin.DrawNodeLabel</c>, <c>DrawMarkers</c> and
	/// <c>DrawNumberMarker</c> (<c>ComicRack/Controls/LibraryTreeSkin.cs</c>) without the drawing: which counters show, the numbers in pills when they fit in the
	/// room that is left behind the label and narrow bars (at most 6 wide, a third of the room, none when that is 2 or less) when they do not or when the
	/// numbers are switched off. Counters are placed from the right: Total, Unread, New; two pills are one apart.
	/// </summary>
	public static class GaugeStripLayout
	{
		/// <summary>The most a bar is wide.</summary>
		public const int MaxBar = 6;

		/// <summary>The widest a number pill is for a text: the text plus 4 (<c>(int)MeasureString + 4</c>).</summary>
		public static int PillWidth(double textWidth) => (int)textWidth + 4;

		/// <param name="total">The number of books of the list.</param>
		/// <param name="unread">The books not read and not new.</param>
		/// <param name="added">The books not read that are new.</param>
		/// <param name="showTotal">The Total flag of <c>LibraryGaugesFormat</c>.</param>
		/// <param name="showUnread">The Unread flag.</param>
		/// <param name="showNew">The New flag.</param>
		/// <param name="numeric">The Numeric flag.</param>
		/// <param name="available">The room behind the label in units (<c>double.PositiveInfinity</c> for unlimited).</param>
		/// <param name="height">The height of the row.</param>
		/// <param name="textWidth">The width of a text in the counter font.</param>
		/// <param name="width">The width the counters take together (0 if none).</param>
		public static IReadOnlyList<GaugeCell> Compute(int total, int unread, int added, bool showTotal, bool showUnread, bool showNew, bool numeric,
			double available, double height, Func<string, double> textWidth, out double width)
		{
			width = 0;
			if (total <= 0 && unread <= 0 && added <= 0)
			{
				return Array.Empty<GaugeCell>();
			}
			bool hasTotal = showTotal && total != 0;
			bool hasUnread = showUnread && unread != 0;
			bool hasNew = showNew && added != 0;
			if (!hasTotal && !hasUnread && !hasNew)
			{
				return Array.Empty<GaugeCell>();
			}
			// the unread counter takes the new books along when there is no counter for them
			int unreadValue = unread + (hasNew ? 0 : added);

			var kinds = new List<(GaugeKind kind, int value)>(3);
			if (hasTotal)
			{
				kinds.Add((GaugeKind.Total, total));
			}
			if (hasUnread)
			{
				kinds.Add((GaugeKind.Unread, unreadValue));
			}
			if (hasNew)
			{
				kinds.Add((GaugeKind.New, added));
			}

			double needed = 0;
			var widths = new List<int>(3);
			foreach ((GaugeKind kind, int value) in kinds)
			{
				int w = PillWidth(textWidth(value.ToString(CultureInfo.CurrentCulture)));
				widths.Add(w);
				needed += w + 1;
			}
			bool useNumbers = numeric && needed < available;
			int fixedSize = 0;
			if (!useNumbers)
			{
				fixedSize = double.IsInfinity(available) ? MaxBar : (int)Math.Min(Math.Floor(available / 3), MaxBar);
				if (fixedSize <= 2)
				{
					return Array.Empty<GaugeCell>();
				}
			}

			// widths from the right: Total is first in kinds
			double sum = 0;
			var sizes = new List<double>(kinds.Count);
			for (int i = 0; i < kinds.Count; i++)
			{
				double w = useNumbers ? widths[i] : fixedSize;
				sizes.Add(w);
				sum += w;
			}
			width = sum + (kinds.Count - 1);
			var cells = new GaugeCell[kinds.Count];
			double right = width;
			double y = 1, h = Math.Max(0, height - 2);
			for (int i = 0; i < kinds.Count; i++)
			{
				GaugeKind kind = kinds[i].kind;
				bool roundLeft, roundRight;
				switch (kind)
				{
					case GaugeKind.Total:
						roundLeft = !(hasUnread && hasNew);
						roundRight = true;
						break;
					case GaugeKind.Unread:
						roundLeft = !hasNew;
						roundRight = !hasTotal;
						break;
					default:
						roundLeft = true;
						roundRight = !(hasUnread && hasTotal);
						break;
				}
				double w = sizes[i];
				// the cells are listed left to right: New, Unread, Total
				cells[kinds.Count - 1 - i] = new GaugeCell(kind, new Rect(right - w, y, w, h), useNumbers ? kinds[i].value.ToString(CultureInfo.CurrentCulture) : string.Empty, roundLeft, roundRight);
				right -= w + 1;
			}
			return cells;
		}
	}

	/// <summary>
	/// The counters behind a list name in the library tree (<c>LibraryTreeSkin.DrawNumberMarker</c>): the number in white on a pill that fades from alpha 128
	/// at the top to alpha 192 at the bottom of the colour of its counter (<c>GaugeTotal</c> green, <c>GaugeUnread</c> orange, <c>GaugeNew</c> red; the
	/// original's gradient runs at 270 degrees), text at 0.75 times the item font, the corners rounded as the original rounds them (<see cref="GaugeStripLayout"/>).
	/// Drawn by the control itself so that the room behind the label decides between numbers and bars when the strip is laid out; the row that
	/// hosts it gives it exactly that room (<see cref="CrTreeView"/>).
	/// </summary>
	public class CrGaugeStrip : Control, IChromeElement
	{
		public static readonly StyledProperty<int> TotalProperty = AvaloniaProperty.Register<CrGaugeStrip, int>(nameof(Total));
		public static readonly StyledProperty<int> UnreadProperty = AvaloniaProperty.Register<CrGaugeStrip, int>(nameof(Unread));
		public static readonly StyledProperty<int> NewProperty = AvaloniaProperty.Register<CrGaugeStrip, int>(nameof(New));
		public static readonly StyledProperty<bool> ShowTotalProperty = AvaloniaProperty.Register<CrGaugeStrip, bool>(nameof(ShowTotal), true);
		public static readonly StyledProperty<bool> ShowUnreadProperty = AvaloniaProperty.Register<CrGaugeStrip, bool>(nameof(ShowUnread), true);
		public static readonly StyledProperty<bool> ShowNewProperty = AvaloniaProperty.Register<CrGaugeStrip, bool>(nameof(ShowNew), true);
		public static readonly StyledProperty<bool> NumericProperty = AvaloniaProperty.Register<CrGaugeStrip, bool>(nameof(Numeric), true);

		private IReadOnlyList<GaugeCell> cells = Array.Empty<GaugeCell>();
		private double cellsWidth;

		static CrGaugeStrip()
		{
			AffectsMeasure<CrGaugeStrip>(TotalProperty, UnreadProperty, NewProperty, ShowTotalProperty, ShowUnreadProperty, ShowNewProperty, NumericProperty);
			AffectsRender<CrGaugeStrip>(TotalProperty, UnreadProperty, NewProperty, ShowTotalProperty, ShowUnreadProperty, ShowNewProperty, NumericProperty);
		}

		public CrGaugeStrip()
		{
			IsHitTestVisible = false;
			VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.Tree;

		/// <summary>Books in the list (0 or less: none).</summary>
		public int Total
		{
			get => GetValue(TotalProperty);
			set => SetValue(TotalProperty, value);
		}

		/// <summary>Books not read and not new.</summary>
		public int Unread
		{
			get => GetValue(UnreadProperty);
			set => SetValue(UnreadProperty, value);
		}

		/// <summary>Books not read that were added recently.</summary>
		public int New
		{
			get => GetValue(NewProperty);
			set => SetValue(NewProperty, value);
		}

		/// <summary><c>LibraryGauges.Total</c>.</summary>
		public bool ShowTotal
		{
			get => GetValue(ShowTotalProperty);
			set => SetValue(ShowTotalProperty, value);
		}

		/// <summary><c>LibraryGauges.Unread</c>.</summary>
		public bool ShowUnread
		{
			get => GetValue(ShowUnreadProperty);
			set => SetValue(ShowUnreadProperty, value);
		}

		/// <summary><c>LibraryGauges.New</c>.</summary>
		public bool ShowNew
		{
			get => GetValue(ShowNewProperty);
			set => SetValue(ShowNewProperty, value);
		}

		/// <summary><c>LibraryGauges.Numeric</c>: numbers in the pills, else bars.</summary>
		public bool Numeric
		{
			get => GetValue(NumericProperty);
			set => SetValue(NumericProperty, value);
		}

		/// <summary>The counters as they were laid out last (left to right), for tests.</summary>
		public IReadOnlyList<GaugeCell> Cells
		{
			get
			{
				SettleToBounds();
				return cells;
			}
		}

		// The room behind the label is decided when the strip is arranged: a measure that ran later with the room of an older pass must not leave bars where the
		// row is wide. A strip that is exactly as wide as the layout it has keeps it (the numbers need one unit more than they use).
		private void SettleToBounds()
		{
			if (Bounds.Width > 0 && (Math.Abs(Bounds.Width - cellsWidth) > 0.5 || Math.Abs(Bounds.Height - measuredHeight) > 0.5))
			{
				measuredHeight = Bounds.Height;
				Recompute(Bounds.Width, Bounds.Height);
			}
		}

		private Typeface Face => new Typeface(TextElement.GetFontFamily(this));

		private double TextWidth(string text)
		{
			return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, ChromeFonts.CounterSize, ChromePaint.Brush(this, ThemeTokens.Names.GaugeTotalText)).WidthIncludingTrailingWhitespace;
		}

		private void Recompute(double available, double height)
		{
			cells = GaugeStripLayout.Compute(Total, Unread, New, ShowTotal, ShowUnread, ShowNew, Numeric, available, height, TextWidth, out cellsWidth);
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			double height = double.IsInfinity(availableSize.Height) ? ChromeFonts.LineHeight(ChromeFonts.ItemSize) + 8 : availableSize.Height;
			measuredHeight = height;
			Recompute(availableSize.Width, height);
			return new Size(Math.Min(cellsWidth, double.IsInfinity(availableSize.Width) ? cellsWidth : availableSize.Width), height);
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			// the layout was decided when the room behind the label was measured; the room it is finally given decides again when it is another one
			// (a strip that is given exactly the width it asked for keeps its layout: the numbers need one unit more than they use)
			if (Math.Abs(finalSize.Width - cellsWidth) > 0.5 || Math.Abs(finalSize.Height - measuredHeight) > 0.5)
			{
				Recompute(finalSize.Width, finalSize.Height);
			}
			return finalSize;
		}

		private double measuredHeight;

		private static Geometry Outline(Rect r, bool roundLeft, bool roundRight)
		{
			// PathUtility.CreatePath with (l, l), (r, r), (4r, 4r), (l, 2l): top left, top right, bottom right, bottom left (width, height of the radius)
			double l = roundLeft ? 1 : 0, rr = roundRight ? 1 : 0;
			double tlx = l, tly = l, trx = rr, tryy = rr, brx = 4 * rr, bry = 4 * rr, blx = l, bly = 2 * l;
			var geometry = new StreamGeometry();
			using (StreamGeometryContext c = geometry.Open())
			{
				c.BeginFigure(new Point(r.Left + tlx, r.Top), true);
				c.LineTo(new Point(r.Right - trx, r.Top));
				if (trx > 0)
				{
					c.ArcTo(new Point(r.Right, r.Top + tryy), new Size(trx, tryy), 0, false, SweepDirection.Clockwise);
				}
				c.LineTo(new Point(r.Right, r.Bottom - bry));
				if (brx > 0)
				{
					c.ArcTo(new Point(r.Right - brx, r.Bottom), new Size(brx, bry), 0, false, SweepDirection.Clockwise);
				}
				c.LineTo(new Point(r.Left + blx, r.Bottom));
				if (blx > 0)
				{
					c.ArcTo(new Point(r.Left, r.Bottom - bly), new Size(blx, bly), 0, false, SweepDirection.Clockwise);
				}
				c.LineTo(new Point(r.Left, r.Top + tly));
				if (tlx > 0)
				{
					c.ArcTo(new Point(r.Left + tlx, r.Top), new Size(tlx, tly), 0, false, SweepDirection.Clockwise);
				}
				c.EndFigure(true);
			}
			return geometry;
		}

		private static (string back, string text) Tokens(GaugeKind kind)
		{
			switch (kind)
			{
				case GaugeKind.Unread:
					return (ThemeTokens.Names.GaugeUnread, ThemeTokens.Names.GaugeUnreadText);
				case GaugeKind.New:
					return (ThemeTokens.Names.GaugeNew, ThemeTokens.Names.GaugeNewText);
				default:
					return (ThemeTokens.Names.GaugeTotal, ThemeTokens.Names.GaugeTotalText);
			}
		}

		/// <summary>The brush of a counter: its colour at alpha 128 at the top, 192 at the bottom.</summary>
		public static IBrush PillBrush(Control control, GaugeKind kind)
		{
			Color color = ChromePaint.Color(control, Tokens(kind).back);
			var stops = new[]
			{
				new ImmutableGradientStop(0, new Color(128, color.R, color.G, color.B)),
				new ImmutableGradientStop(1, new Color(192, color.R, color.G, color.B)),
			};
			return new ImmutableLinearGradientBrush(stops, 1, null, null, GradientSpreadMethod.Pad, new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative));
		}

		public override void Render(DrawingContext context)
		{
			base.Render(context);
			SettleToBounds();
			if (cells.Count == 0)
			{
				return;
			}
			double offset = Math.Max(0, Bounds.Width - cellsWidth); // right aligned in the room the row gave
			foreach (GaugeCell cell in cells)
			{
				Rect r = new Rect(cell.Bounds.X + offset, cell.Bounds.Y, cell.Bounds.Width, cell.Bounds.Height);
				context.DrawGeometry(PillBrush(this, cell.Kind), null, Outline(r, cell.RoundLeft, cell.RoundRight));
				if (cell.Text.Length > 0)
				{
					var text = new FormattedText(cell.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, ChromeFonts.CounterSize, ChromePaint.Brush(this, Tokens(cell.Kind).text));
					context.DrawText(text, new Point(r.X + Math.Floor((r.Width - text.Width) / 2), r.Y + Math.Floor((r.Height - text.Height) / 2)));
				}
			}
		}
	}
}
