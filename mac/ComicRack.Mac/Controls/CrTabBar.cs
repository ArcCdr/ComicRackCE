using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>One tab of a <see cref="CrTabBar"/> (<c>TabBarItem</c>).</summary>
	public sealed class CrTabItem
	{
		private string text = string.Empty;
		private bool canClose = true;
		private string iconName;

		public string Text
		{
			get => text;
			set => Set(ref text, value ?? string.Empty);
		}

		/// <summary>True shows the close box on the hot and the selected tab (<c>TabBarItem.CanClose</c>).</summary>
		public bool CanClose
		{
			get => canClose;
			set => Set(ref canClose, value);
		}

		/// <summary>An image of the original by resource name, 16 pixels, left of the text.</summary>
		public string IconName
		{
			get => iconName;
			set => Set(ref iconName, value);
		}

		/// <summary>Anything the owner attaches (the book of the tab).</summary>
		public object Tag { get; set; }

		internal event EventHandler Changed;

		private void Set<T>(ref T field, T value)
		{
			if (!EqualityComparer<T>.Default.Equals(field, value))
			{
				field = value;
				Changed?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	/// <summary>Where a tab and its close box are.</summary>
	public readonly struct TabGeometry
	{
		public TabGeometry(Rect bounds, Rect closeBounds)
		{
			Bounds = bounds;
			CloseBounds = closeBounds;
		}

		public Rect Bounds { get; }

		/// <summary>Empty unless the tab is hot or selected and can be closed.</summary>
		public Rect CloseBounds { get; }

		public bool HasClose => CloseBounds.Width > 0;
	}

	public sealed class TabEventArgs : EventArgs
	{
		public TabEventArgs(int index, CrTabItem tab)
		{
			Index = index;
			Tab = tab;
		}

		public int Index { get; }

		public CrTabItem Tab { get; }
	}

	/// <summary>
	/// The tab bar of the original (C-TAB-1, <c>TabBar</c>): 31 high, tabs 24 high from a left indent of 8, 60 to 250 wide and one unit overlapping,
	/// the selected tab in <c>TabBarSelectedBack</c> and one unit taller than the others, a close box (the original's <c>Close</c> image) on the hot
	/// and selected tab, the base line frame at the bottom, and a slot at the right that hosts a toolbar (<see cref="ToolbarSlot"/>). The tab
	/// geometry is computed by <see cref="ComputeLayout"/> as <c>TabBar.LayoutTabs</c> does (text width + 6 + image + close box, then shrunk to fit).
	/// Raises <see cref="SelectedTabChanged"/> on a click and <see cref="TabCloseRequested"/> on a click on the close box (the owner removes the tab).
	/// </summary>
	public class CrTabBar : Control, IChromeElement
	{
		public static readonly StyledProperty<int> SelectedIndexProperty = AvaloniaProperty.Register<CrTabBar, int>(nameof(SelectedIndex), -1);

		private const double CloseBoxSize = 16;

		private readonly ObservableCollection<CrTabItem> tabs = new ObservableCollection<CrTabItem>();
		private Control slot;
		private int hotIndex = -1;
		private IReadOnlyList<TabGeometry> geometry = new TabGeometry[0];

		static CrTabBar()
		{
			AffectsRender<CrTabBar>(SelectedIndexProperty);
			AffectsMeasure<CrTabBar>(SelectedIndexProperty);
			AffectsArrange<CrTabBar>(SelectedIndexProperty);
		}

		public CrTabBar()
		{
			Height = ChromeMetrics.TabBarHeight;
			Focusable = false;
			TextElement.SetFontSize(this, ChromeFonts.ControlSize);
			tabs.CollectionChanged += OnTabsChanged;
			ActualThemeVariantChanged += (sender, e) => InvalidateVisual();
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.TabBar;

		public IList<CrTabItem> Tabs => tabs;

		public int SelectedIndex
		{
			get => GetValue(SelectedIndexProperty);
			set => SetValue(SelectedIndexProperty, Math.Min(value, tabs.Count - 1));
		}

		public CrTabItem SelectedTab => SelectedIndex >= 0 && SelectedIndex < tabs.Count ? tabs[SelectedIndex] : null;

		/// <summary>The tab under the pointer, or -1.</summary>
		public int HotIndex => hotIndex;

		/// <summary>The toolbar at the right of the bar (the reader toolbar sits there); it gets the height under the base line.</summary>
		public Control ToolbarSlot
		{
			get => slot;
			set
			{
				if (ReferenceEquals(slot, value))
				{
					return;
				}
				if (slot != null)
				{
					LogicalChildren.Remove(slot);
					VisualChildren.Remove(slot);
				}
				slot = value;
				if (slot != null)
				{
					LogicalChildren.Add(slot);
					VisualChildren.Add(slot);
				}
				InvalidateMeasure();
			}
		}

		/// <summary>The geometry of the tabs after the last arrange.</summary>
		public IReadOnlyList<TabGeometry> Geometry => geometry;

		public event EventHandler<TabEventArgs> SelectedTabChanged;

		public event EventHandler<TabEventArgs> TabCloseRequested;

		private void OnTabsChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.NewItems != null)
			{
				foreach (CrTabItem item in e.NewItems)
				{
					item.Changed += OnTabChanged;
				}
			}
			if (e.OldItems != null)
			{
				foreach (CrTabItem item in e.OldItems)
				{
					item.Changed -= OnTabChanged;
				}
			}
			if (SelectedIndex >= tabs.Count)
			{
				SelectedIndex = tabs.Count - 1;
			}
			if (SelectedIndex < 0 && tabs.Count > 0)
			{
				SelectedIndex = 0;
			}
			InvalidateMeasure();
			InvalidateArrange();
			InvalidateVisual();
		}

		private void OnTabChanged(object sender, EventArgs e)
		{
			InvalidateArrange();
			InvalidateVisual();
		}

		private Typeface TabTypeface => new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, FontWeight.Normal);

		private FormattedText Format(string text)
		{
			return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, TabTypeface, TextElement.GetFontSize(this), ChromePaint.Brush(this, ThemeTokens.Names.ControlText));
		}

		private Bitmap CloseImage => Icons.TryGet("Close", ChromePaint.VariantOf(this));

		/// <summary>
		/// <c>TabBar.LayoutTabs</c>: the tab rectangles in a bar of <paramref name="barWidth"/> whose right <paramref name="slotWidth"/> units belong
		/// to the toolbar slot. A tab is its text plus 6 plus image plus close box plus 4, at least 60 and at most 250; when they do not fit they
		/// shrink in proportion (never below 60).
		/// </summary>
		public IReadOnlyList<TabGeometry> ComputeLayout(double barWidth, double slotWidth)
		{
			double available = Math.Max(0, barWidth - slotWidth);
			double indent = ChromeMetrics.TabLeftIndent;
			var widths = new double[tabs.Count];
			for (int i = 0; i < tabs.Count; i++)
			{
				CrTabItem tab = tabs[i];
				double w = Math.Ceiling(Format(tab.Text).Width) + 4 + 2;
				if (!string.IsNullOrEmpty(tab.IconName))
				{
					w += ChromeMetrics.ToolIconSize;
				}
				if (tab.CanClose && CloseImage != null)
				{
					w += CloseBoxSize + 4;
				}
				widths[i] = Math.Min(ChromeMetrics.TabMaxWidth, Math.Max(ChromeMetrics.TabMinWidth, w));
			}
			double total = widths.Sum() - Math.Max(0, widths.Length - 1);
			double over = indent + total - available;
			if (over > 0)
			{
				double shrinkable = widths.Where(w => w > ChromeMetrics.TabMinWidth).Sum();
				double factor = shrinkable > 0 ? Math.Max(0, shrinkable - (over + 10)) / shrinkable : 0;
				for (int i = 0; i < widths.Length; i++)
				{
					if (widths[i] > ChromeMetrics.TabMinWidth)
					{
						widths[i] = Math.Max(ChromeMetrics.TabMinWidth, Math.Floor(widths[i] * factor));
					}
				}
			}
			double contentTop = ChromeMetrics.TabTopPadding;
			double contentHeight = ChromeMetrics.TabBarHeight - ChromeMetrics.TabTopPadding - ChromeMetrics.TabBottomPadding;
			var result = new TabGeometry[tabs.Count];
			double x = indent;
			for (int i = 0; i < tabs.Count; i++)
			{
				bool selected = i == SelectedIndex;
				// selected: the whole content height plus one; the others 2 lower at the top and 2 shorter at the bottom (the base line is drawn)
				Rect bounds = selected
					? new Rect(x, contentTop, widths[i], contentHeight + 1)
					: new Rect(x, contentTop + 2, widths[i], contentHeight + 1 - 4);
				Rect close = default;
				if (tabs[i].CanClose && CloseImage != null && (selected || i == hotIndex))
				{
					close = new Rect(bounds.Right - CloseBoxSize - 2, bounds.Top + 2, CloseBoxSize, CloseBoxSize);
				}
				result[i] = new TabGeometry(bounds, close);
				x += widths[i] - 1;
			}
			return result;
		}

		private double SlotWidth => slot == null ? 0 : slot.DesiredSize.Width;

		protected override Size MeasureOverride(Size availableSize)
		{
			if (slot != null)
			{
				slot.Measure(new Size(double.PositiveInfinity, ChromeMetrics.TabBarHeight - ChromeMetrics.TabBottomPadding - 1));
			}
			double width = double.IsInfinity(availableSize.Width) ? ChromeMetrics.TabLeftIndent + Math.Max(0, ComputeLayout(double.MaxValue / 4, 0).LastOrDefault().Bounds.Right) + SlotWidth : availableSize.Width;
			return new Size(width, ChromeMetrics.TabBarHeight);
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			if (slot != null)
			{
				double w = Math.Min(slot.DesiredSize.Width, finalSize.Width);
				slot.Arrange(new Rect(finalSize.Width - w, 0, w, ChromeMetrics.TabBarHeight - ChromeMetrics.TabBottomPadding - 1));
			}
			geometry = ComputeLayout(finalSize.Width, SlotWidth);
			return finalSize;
		}

		public override void Render(DrawingContext context)
		{
			var area = new Rect(Bounds.Size);
			if (geometry.Count != tabs.Count)
			{
				geometry = ComputeLayout(area.Width, SlotWidth);
			}
			context.DrawRectangle(ChromePaint.Brush(this, ThemeTokens.Names.TabBarBack), null, area);
			IBrush border = ChromePaint.Brush(this, ThemeTokens.Names.TabPageBorder);
			// base line: the frame from the bottom of the tab area to the bottom of the bar
			double baseTop = ChromeMetrics.TabBarHeight - ChromeMetrics.TabBottomPadding - 1;
			ChromePaint.Frame(context, border, new Rect(0, baseTop, area.Width, ChromeMetrics.TabBarHeight - baseTop));
			for (int i = 0; i < tabs.Count; i++)
			{
				if (i != SelectedIndex)
				{
					RenderTab(context, i, border);
				}
			}
			if (SelectedIndex >= 0 && SelectedIndex < tabs.Count)
			{
				RenderTab(context, SelectedIndex, border);
			}
			base.Render(context);
		}

		private void RenderTab(DrawingContext context, int index, IBrush border)
		{
			CrTabItem tab = tabs[index];
			Rect bounds = geometry[index].Bounds;
			bool selected = index == SelectedIndex;
			bool hot = index == hotIndex;
			string fill = selected ? ThemeTokens.Names.TabBarSelectedBack : hot ? ThemeTokens.Names.ButtonHoverBack : ThemeTokens.Names.TabPageOther;
			context.DrawRectangle(ChromePaint.Brush(this, fill), null, bounds);
			IBrush frame = !selected && hot ? ChromePaint.Brush(this, ThemeTokens.Names.ButtonHoverBorder) : border;
			// the tab has no bottom edge: the selected one runs into the page, the others into the base line
			ChromePaint.Frame(context, frame, bounds, Sides.Left | Sides.Top | Sides.Right);
			double x = bounds.Left + 3;
			Bitmap icon = string.IsNullOrEmpty(tab.IconName) ? null : Icons.TryGet(tab.IconName, ChromePaint.VariantOf(this));
			if (icon != null)
			{
				ChromePaint.Image(context, icon, new Rect(x, Math.Floor(bounds.Top + (bounds.Height - icon.Size.Height) / 2), icon.Size.Width, icon.Size.Height));
				x += ChromeMetrics.ToolIconSize;
			}
			double textRight = bounds.Right - 2 - (geometry[index].HasClose ? CloseBoxSize : 0);
			FormattedText text = Format(tab.Text);
			text.MaxTextWidth = Math.Max(0, textRight - x);
			text.MaxTextHeight = text.Height;
			text.Trimming = TextTrimming.CharacterEllipsis;
			double textTop = Math.Floor(bounds.Top + 2 + Math.Max(0, (bounds.Height - 2 - text.Height) / 2));
			context.DrawText(text, new Point(x, textTop));
			Rect close = geometry[index].CloseBounds;
			if (geometry[index].HasClose)
			{
				// the close box is dimmed (-0.8 brightness in the original) until the pointer is over it
				ChromePaint.Image(context, CloseImage, close, closeHot == index ? 1 : 0.35);
			}
		}

		private int closeHot = -1;

		/// <summary>The tab at a point (0-based), or -1.</summary>
		public int HitTest(Point point)
		{
			// the selected tab is drawn last, so it wins where tabs overlap
			if (SelectedIndex >= 0 && SelectedIndex < geometry.Count && geometry[SelectedIndex].Bounds.Contains(point))
			{
				return SelectedIndex;
			}
			for (int i = 0; i < geometry.Count; i++)
			{
				if (geometry[i].Bounds.Contains(point))
				{
					return i;
				}
			}
			return -1;
		}

		protected override void OnPointerMoved(PointerEventArgs e)
		{
			base.OnPointerMoved(e);
			UpdateHot(e.GetPosition(this));
		}

		protected override void OnPointerExited(PointerEventArgs e)
		{
			base.OnPointerExited(e);
			UpdateHot(new Point(-1, -1));
		}

		/// <summary>Sets the hot tab as if the pointer were at <paramref name="point"/> (tests and the mouse handler).</summary>
		public void UpdateHot(Point point)
		{
			int index = HitTest(point);
			int close = index >= 0 && geometry[index].HasClose && geometry[index].CloseBounds.Contains(point) ? index : -1;
			if (index != hotIndex || close != closeHot)
			{
				hotIndex = index;
				closeHot = close;
				geometry = ComputeLayout(Bounds.Width, SlotWidth);
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
			Click(e.GetPosition(this));
			e.Handled = true;
		}

		/// <summary>A click at a point: the close box asks to close its tab, another place on a tab selects it.</summary>
		public void Click(Point point)
		{
			int index = HitTest(point);
			if (index < 0)
			{
				return;
			}
			if (geometry[index].HasClose && geometry[index].CloseBounds.Contains(point))
			{
				TabCloseRequested?.Invoke(this, new TabEventArgs(index, tabs[index]));
				return;
			}
			if (index != SelectedIndex)
			{
				SelectedIndex = index;
				geometry = ComputeLayout(Bounds.Width, SlotWidth);
				SelectedTabChanged?.Invoke(this, new TabEventArgs(index, tabs[index]));
			}
		}
	}
}
