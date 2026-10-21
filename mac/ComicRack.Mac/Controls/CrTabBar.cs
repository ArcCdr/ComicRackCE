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
using Avalonia.Threading;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>One tab of a <see cref="CrTabBar"/> (<c>TabBarItem</c>).</summary>
	public sealed class CrTabItem
	{
		private string text = string.Empty;
		private bool canClose = true;
		private string iconName;
		private bool showText = true;
		private bool adjustWidth = true;
		private double minimumWidth = ChromeMetrics.TabMinWidth;
		private string toolTipText;
		private bool selectable = true;

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

		/// <summary>False hides the text (the New Tab button shows only its image, <c>ShowText</c>).</summary>
		public bool ShowText
		{
			get => showText;
			set => Set(ref showText, value);
		}

		/// <summary>False keeps the width of the tab when the bar is too narrow (<c>AdjustWidth</c>).</summary>
		public bool AdjustWidth
		{
			get => adjustWidth;
			set => Set(ref adjustWidth, value);
		}

		/// <summary>The narrowest the tab gets when the bar is too narrow (<c>MinimumWidth</c>): 60, 100 for a book tab, 32 for the New Tab button.</summary>
		public double MinimumWidth
		{
			get => minimumWidth;
			set => Set(ref minimumWidth, value);
		}

		/// <summary>The tool tip of the tab (<c>ToolTipText</c>).</summary>
		public string ToolTipText
		{
			get => toolTipText;
			set => Set(ref toolTipText, value);
		}

		/// <summary>False makes the tab a button: a click raises <see cref="Clicked"/> and never selects it (the New Tab button).</summary>
		public bool Selectable
		{
			get => selectable;
			set => Set(ref selectable, value);
		}

		/// <summary>Raised by a click on a tab that is not <see cref="Selectable"/>.</summary>
		public event EventHandler Clicked;

		internal void RaiseClicked() => Clicked?.Invoke(this, EventArgs.Empty);

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
	/// geometry is computed by <see cref="ComputeLayout"/> as <c>TabBar.LayoutTabs</c> does (text width + 6 + image + close box, then shrunk to fit, each tab
	/// to its own <see cref="CrTabItem.MinimumWidth"/>). When the tabs do not fit even then, the bar scrolls: arrow buttons at both ends of the tab area move
	/// the tabs by 20 units while they are pressed, and a drop-down button at the right end lists every tab (<see cref="OpenTabList"/>).
	/// Raises <see cref="SelectedTabChanged"/> on a click and <see cref="TabCloseRequested"/> on a click on the close box (the owner removes the tab); a
	/// click on a tab that is not <see cref="CrTabItem.Selectable"/> (the New Tab button) raises that item's <see cref="CrTabItem.Clicked"/>. A right click selects the
	/// tab and opens <see cref="TabMenu"/>.
	/// </summary>
	public class CrTabBar : Control, IChromeElement
	{
		public static readonly StyledProperty<int> SelectedIndexProperty = AvaloniaProperty.Register<CrTabBar, int>(nameof(SelectedIndex), -1);

		private const double CloseBoxSize = 16;

		/// <summary>Width of a scroll arrow and of the drop-down button (<c>SystemInformation.VerticalScrollBarWidth</c>, 16 at 96 DPI).</summary>
		public const double ButtonWidth = 16;

		/// <summary>The distance one press of a scroll arrow moves the tabs (<c>scrollTimer_Tick</c>).</summary>
		public const double ScrollStep = 20;

		private enum Button
		{
			None,
			Left,
			Right,
			DropDown,
		}

		private readonly ObservableCollection<CrTabItem> tabs = new ObservableCollection<CrTabItem>();
		private Control slot;
		private int hotIndex = -1;
		private int closeHot = -1;
		private IReadOnlyList<TabGeometry> geometry = new TabGeometry[0];
		private double tabsOffset;
		private bool arrowsVisible;
		private Rect viewport;
		private Button hotButton;
		private Button pressedButton;
		private bool ensureSelected;
		private DispatcherTimer scrollTimer;

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

		/// <summary>The menu of a right click on a tab (<c>ContextMenu</c> of the items); the owner fills it when <see cref="TabMenuOpening"/> is raised.</summary>
		public MenuFlyout TabMenu { get; set; }

		/// <summary>The geometry of the tabs after the last arrange.</summary>
		public IReadOnlyList<TabGeometry> Geometry => geometry;

		/// <summary>True while the tabs do not fit and the scroll arrows and the drop-down button are shown.</summary>
		public bool ArrowsVisible => arrowsVisible;

		/// <summary>How far the tabs are scrolled to the left (0 or negative).</summary>
		public double TabsOffset => tabsOffset;

		/// <summary>Where the tabs show while the arrows are visible (a tab is clipped to it); the whole tab area otherwise.</summary>
		public Rect TabViewport => viewport;

		public Rect LeftArrowBounds => arrowsVisible ? new Rect(0, ButtonTop, ButtonWidth, ButtonHeight) : default;

		public Rect RightArrowBounds => arrowsVisible ? new Rect(DropDownBounds.X + 1 - ButtonWidth, ButtonTop, ButtonWidth, ButtonHeight) : default;

		public Rect DropDownBounds => arrowsVisible ? new Rect(Math.Max(0, Bounds.Width - SlotWidth) - ButtonWidth, ButtonTop, ButtonWidth, ButtonHeight) : default;

		private static double ButtonTop => ChromeMetrics.TabTopPadding;

		private static double ButtonHeight => ChromeMetrics.TabBarHeight - ChromeMetrics.TabTopPadding - ChromeMetrics.TabBottomPadding + 1;

		public event EventHandler<TabEventArgs> SelectedTabChanged;

		public event EventHandler<TabEventArgs> TabCloseRequested;

		/// <summary>Raised before <see cref="TabMenu"/> opens for a tab (<c>tabContextMenu_Opening</c>): the tab is selected already.</summary>
		public event EventHandler<TabEventArgs> TabMenuOpening;

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
			if (SelectedIndex < 0 && tabs.Count > 0 && tabs[0].Selectable)
			{
				SelectedIndex = 0;
			}
			hotIndex = Math.Min(hotIndex, tabs.Count - 1);
			ensureSelected = true;
			InvalidateMeasure();
			InvalidateArrange();
			InvalidateVisual();
		}

		private void OnTabChanged(object sender, EventArgs e)
		{
			InvalidateArrange();
			InvalidateVisual();
		}

		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
		{
			base.OnPropertyChanged(change);
			if (change.Property == SelectedIndexProperty)
			{
				ensureSelected = true;
			}
		}

		private Typeface TabTypeface => new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, FontWeight.Normal);

		private FormattedText Format(string text)
		{
			return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, TabTypeface, TextElement.GetFontSize(this), ChromePaint.Brush(this, ThemeTokens.Names.ControlText));
		}

		private Bitmap CloseImage => Icons.TryGet("Close", ChromePaint.VariantOf(this));

		private double[] NaturalWidths()
		{
			var widths = new double[tabs.Count];
			for (int i = 0; i < tabs.Count; i++)
			{
				CrTabItem tab = tabs[i];
				double w = (tab.ShowText ? Math.Ceiling(Format(tab.Text).Width) : 0) + 4 + 2;
				if (!string.IsNullOrEmpty(tab.IconName))
				{
					w += ChromeMetrics.ToolIconSize;
				}
				if (tab.CanClose && CloseImage != null)
				{
					w += CloseBoxSize + 4;
				}
				widths[i] = Math.Min(ChromeMetrics.TabMaxWidth, Math.Max(tab.MinimumWidth, w));
			}
			return widths;
		}

		/// <summary>
		/// <c>TabBar.LayoutTabs</c>: the tab rectangles in a bar of <paramref name="barWidth"/> whose right <paramref name="slotWidth"/> units belong
		/// to the toolbar slot. A tab is its text plus 6 plus image plus close box plus 4, at least its minimum width (60) and at most 250; when they do not fit they
		/// shrink in proportion (never below the minimum of the tab). The drop-down width is reserved at the right of the tab area as in the original; when the tabs
		/// still do not fit, the scroll arrows take 16 units at each end and the tabs are scrolled by <see cref="TabsOffset"/>.
		/// </summary>
		public IReadOnlyList<TabGeometry> ComputeLayout(double barWidth, double slotWidth)
		{
			double indent = ChromeMetrics.TabLeftIndent;
			double[] widths = NaturalWidths();
			double areaRight = Math.Max(0, barWidth - slotWidth) - ButtonWidth;
			double available = Math.Max(0, areaRight);
			double total = widths.Sum() - Math.Max(0, widths.Length - 1);
			double over = indent + total - available;
			if (over > 0)
			{
				double shrinkable = 0;
				for (int i = 0; i < widths.Length; i++)
				{
					if (tabs[i].AdjustWidth && widths[i] > tabs[i].MinimumWidth)
					{
						shrinkable += widths[i];
					}
				}
				double factor = shrinkable > 0 ? Math.Max(0, shrinkable - (over + 10)) / shrinkable : 0;
				for (int i = 0; i < widths.Length; i++)
				{
					if (tabs[i].AdjustWidth && widths[i] > tabs[i].MinimumWidth)
					{
						widths[i] = Math.Max(tabs[i].MinimumWidth, Math.Floor(widths[i] * factor));
					}
				}
				total = widths.Sum() - Math.Max(0, widths.Length - 1);
			}
			arrowsVisible = indent + total > available && widths.Length > 0;
			double left = 0, right = areaRight;
			if (arrowsVisible)
			{
				left += ButtonWidth;
				right -= ButtonWidth;
			}
			viewport = new Rect(left, 0, Math.Max(0, right - left), ChromeMetrics.TabBarHeight);
			double minOffset = Math.Min(0, (right - left) - (indent + total));
			if (!arrowsVisible)
			{
				tabsOffset = 0;
			}
			else
			{
				tabsOffset = Math.Max(minOffset, Math.Min(0, tabsOffset));
				if (ensureSelected)
				{
					tabsOffset = KeepSelectedInView(widths, left, right, indent, minOffset);
				}
			}
			ensureSelected = false;
			double contentTop = ChromeMetrics.TabTopPadding;
			double contentHeight = ChromeMetrics.TabBarHeight - ChromeMetrics.TabTopPadding - ChromeMetrics.TabBottomPadding;
			var result = new TabGeometry[tabs.Count];
			double x = left + tabsOffset + indent;
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

		// TabBar.EnsureVisible: the offset that brings the selected tab into the viewport (the least movement).
		private double KeepSelectedInView(double[] widths, double left, double right, double indent, double minOffset)
		{
			int index = SelectedIndex;
			if (index < 0 || index >= widths.Length)
			{
				return tabsOffset;
			}
			double start = indent;
			for (int i = 0; i < index; i++)
			{
				start += widths[i] - 1;
			}
			double end = start + widths[index];
			double offset = tabsOffset;
			if (left + offset + start < left)
			{
				// the first tab brings its left indent along
				offset = index == 0 ? 0 : -start;
			}
			if (left + offset + end > right)
			{
				offset = right - left - end;
			}
			return Math.Max(minOffset, Math.Min(0, offset));
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

		private void Relayout()
		{
			geometry = ComputeLayout(Bounds.Width, SlotWidth);
			InvalidateVisual();
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
			if (arrowsVisible)
			{
				using (context.PushClip(viewport))
				{
					RenderTabs(context, border);
				}
			}
			else
			{
				RenderTabs(context, border);
			}
			if (arrowsVisible)
			{
				RenderButton(context, LeftArrowBounds, "SimpleArrowLeft", Button.Left, border);
				RenderButton(context, RightArrowBounds, "SimpleArrowRight", Button.Right, border);
				RenderButton(context, DropDownBounds, "SimpleArrowDown", Button.DropDown, border);
			}
			base.Render(context);
		}

		private void RenderTabs(DrawingContext context, IBrush border)
		{
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
		}

		// DrawButton: a button looks like a tab (hot: the hot fill, pressed: the selected fill) with its image centred and the base line under it.
		private void RenderButton(DrawingContext context, Rect bounds, string icon, Button which, IBrush border)
		{
			string fill = pressedButton == which ? ThemeTokens.Names.TabBarSelectedBack : hotButton == which ? ThemeTokens.Names.ButtonHoverBack : ThemeTokens.Names.TabPageOther;
			context.DrawRectangle(ChromePaint.Brush(this, fill), null, bounds);
			IBrush frame = pressedButton != which && hotButton == which ? ChromePaint.Brush(this, ThemeTokens.Names.ButtonHoverBorder) : border;
			if (!ChromePaint.IsNative(this))
			{
				// the Native variant has no frames: the fills tell the states apart
				ChromePaint.Frame(context, frame, bounds, Sides.Left | Sides.Top | Sides.Right);
				ChromePaint.Frame(context, border, new Rect(bounds.X, bounds.Bottom - 1, bounds.Width, 1));
			}
			Bitmap image = Icons.TryGet("cYo.Common.Windows:" + icon, ChromePaint.VariantOf(this)) ?? Icons.TryGet(icon, ChromePaint.VariantOf(this));
			if (image != null)
			{
				ChromePaint.Icon(this, context, image, new Rect(Math.Floor(bounds.X + (bounds.Width - image.Size.Width) / 2), Math.Floor(bounds.Y + (bounds.Height - image.Size.Height) / 2), image.Size.Width, image.Size.Height));
			}
		}

		private void RenderTab(DrawingContext context, int index, IBrush border)
		{
			CrTabItem tab = tabs[index];
			Rect bounds = geometry[index].Bounds;
			bool selected = index == SelectedIndex;
			bool hot = index == hotIndex;
			string fill = selected ? ThemeTokens.Names.TabBarSelectedBack : hot ? ThemeTokens.Names.ButtonHoverBack : ThemeTokens.Names.TabPageOther;
			bool native = ChromePaint.IsNative(this);
			if (native)
			{
				// Native: rounded tops and no frame; the selected tab is the pressed fill, the hot one the hot fill
				context.DrawRectangle(ChromePaint.Brush(this, fill), null, new RoundedRect(bounds, new CornerRadius(ToolPaint.NativeRadius, ToolPaint.NativeRadius, 0, 0)));
			}
			else
			{
				context.DrawRectangle(ChromePaint.Brush(this, fill), null, bounds);
				IBrush frame = !selected && hot ? ChromePaint.Brush(this, ThemeTokens.Names.ButtonHoverBorder) : border;
				// the tab has no bottom edge: the selected one runs into the page, the others into the base line
				ChromePaint.Frame(context, frame, bounds, Sides.Left | Sides.Top | Sides.Right);
			}
			double x = bounds.Left + 3;
			Bitmap icon = string.IsNullOrEmpty(tab.IconName) ? null : Icons.TryGet(tab.IconName, ChromePaint.VariantOf(this));
			if (icon != null)
			{
				ChromePaint.Icon(this, context, icon, new Rect(x, Math.Floor(bounds.Top + (bounds.Height - icon.Size.Height) / 2), icon.Size.Width, icon.Size.Height));
				x += ChromeMetrics.ToolIconSize;
			}
			if (tab.ShowText)
			{
				double textRight = bounds.Right - 2 - (geometry[index].HasClose ? CloseBoxSize : 0);
				FormattedText text = Format(tab.Text);
				text.MaxTextWidth = Math.Max(0, textRight - x);
				text.MaxTextHeight = text.Height;
				text.Trimming = TextTrimming.CharacterEllipsis;
				double textTop = Math.Floor(bounds.Top + 2 + Math.Max(0, (bounds.Height - 2 - text.Height) / 2));
				context.DrawText(text, new Point(x, textTop));
			}
			Rect close = geometry[index].CloseBounds;
			if (geometry[index].HasClose)
			{
				// the close box is dimmed (-0.8 brightness in the original) until the pointer is over it
				ChromePaint.Icon(this, context, CloseImage, close, closeHot == index ? 1 : 0.35);
			}
		}

		/// <summary>The tab at a point (0-based), or -1. A point under a scroll arrow or the drop-down button is on no tab.</summary>
		public int HitTest(Point point)
		{
			if (arrowsVisible && !viewport.Contains(point))
			{
				return -1;
			}
			// the selected tab is drawn last, so it wins where tabs overlap
			int count = Math.Min(geometry.Count, tabs.Count);
			if (SelectedIndex >= 0 && SelectedIndex < count && geometry[SelectedIndex].Bounds.Contains(point))
			{
				return SelectedIndex;
			}
			for (int i = 0; i < count; i++)
			{
				if (geometry[i].Bounds.Contains(point))
				{
					return i;
				}
			}
			return -1;
		}

		private Button HitButton(Point point)
		{
			if (!arrowsVisible)
			{
				return Button.None;
			}
			if (DropDownBounds.Contains(point))
			{
				return Button.DropDown;
			}
			if (LeftArrowBounds.Contains(point))
			{
				return Button.Left;
			}
			return RightArrowBounds.Contains(point) ? Button.Right : Button.None;
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
			Button button = HitButton(point);
			if (index != hotIndex || close != closeHot || button != hotButton)
			{
				hotIndex = index;
				closeHot = close;
				hotButton = button;
				geometry = ComputeLayout(Bounds.Width, SlotWidth);
				InvalidateVisual();
			}
			ToolTip.SetTip(this, index >= 0 ? tabs[index].ToolTipText : null);
		}

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			PointerPoint pointerPoint = e.GetCurrentPoint(this);
			Point point = e.GetPosition(this);
			if (pointerPoint.Properties.IsRightButtonPressed)
			{
				if (OpenTabMenu(point))
				{
					e.Handled = true;
				}
				return;
			}
			if (!pointerPoint.Properties.IsLeftButtonPressed)
			{
				return;
			}
			Button button = HitButton(point);
			if (button != Button.None)
			{
				PressButton(button);
				e.Pointer.Capture(this);
				e.Handled = true;
				return;
			}
			Click(point);
			e.Handled = true;
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			base.OnPointerReleased(e);
			if (pressedButton != Button.None)
			{
				e.Pointer.Capture(null);
				ReleaseButton();
			}
		}

		/// <summary>A click at a point: the close box asks to close its tab, another place on a tab selects it (a button tab raises its Clicked event instead).</summary>
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
			if (!tabs[index].Selectable)
			{
				tabs[index].RaiseClicked();
				return;
			}
			if (index != SelectedIndex)
			{
				SelectedIndex = index;
				geometry = ComputeLayout(Bounds.Width, SlotWidth);
				SelectedTabChanged?.Invoke(this, new TabEventArgs(index, tabs[index]));
			}
		}

		// ---- scrolling and the list of tabs

		/// <summary>Moves the tabs by <paramref name="delta"/> units (positive shows tabs further left); clamped to the ends. Does nothing while the tabs fit.</summary>
		public void ScrollBy(double delta)
		{
			if (!arrowsVisible)
			{
				return;
			}
			tabsOffset += delta;
			Relayout();
		}

		/// <summary>Presses a scroll arrow or the drop-down button as the mouse does (a click on a scroll arrow moves the tabs once, holding it repeats every 25 ms).</summary>
		private void PressButton(Button button)
		{
			pressedButton = button;
			InvalidateVisual();
			if (button == Button.DropDown)
			{
				return;
			}
			StepScroll(button);
			scrollTimer = scrollTimer ?? new DispatcherTimer(TimeSpan.FromMilliseconds(25), DispatcherPriority.Input, (sender, ev) =>
			{
				if (pressedButton == Button.Left || pressedButton == Button.Right)
				{
					StepScroll(pressedButton);
				}
			});
			scrollTimer.Start();
		}

		private void StepScroll(Button button)
		{
			ScrollBy(button == Button.Left ? ScrollStep : -ScrollStep);
		}

		private void ReleaseButton()
		{
			Button released = pressedButton;
			pressedButton = Button.None;
			scrollTimer?.Stop();
			InvalidateVisual();
			if (released == Button.DropDown && hotButton == Button.DropDown)
			{
				OpenTabList();
			}
		}

		/// <summary>Presses and releases a scroll arrow or the drop-down button in one go (tests).</summary>
		public void ClickButton(bool left, bool right, bool dropDown)
		{
			Button button = left ? Button.Left : right ? Button.Right : dropDown ? Button.DropDown : Button.None;
			if (button == Button.None || !arrowsVisible)
			{
				return;
			}
			if (button == Button.DropDown)
			{
				OpenTabList();
				return;
			}
			StepScroll(button);
		}

		/// <summary>The drop-down button (<c>ShowTabDropDown</c>): a menu with every tab, the selected one marked; a choice selects the tab.</summary>
		public MenuFlyout OpenTabList(bool show = true)
		{
			var menu = new MenuFlyout();
			for (int i = 0; i < tabs.Count; i++)
			{
				int index = i;
				CrTabItem tab = tabs[i];
				if (!tab.Selectable)
				{
					continue;
				}
				var item = new MenuItem { Header = tab.Text, ToggleType = MenuItemToggleType.CheckBox, IsChecked = index == SelectedIndex };
				item.Click += (sender, e) =>
				{
					if (index != SelectedIndex)
					{
						SelectedIndex = index;
						Relayout();
						SelectedTabChanged?.Invoke(this, new TabEventArgs(index, tabs[index]));
					}
				};
				menu.Items.Add(item);
			}
			if (show && menu.Items.Count > 0 && VisualRoot != null)
			{
				menu.ShowAt(this);
			}
			return menu;
		}

		// ---- the tab menu

		/// <summary>A right click at a point: selects the tab and opens <see cref="TabMenu"/>. Returns true if a tab was hit.</summary>
		public bool OpenTabMenu(Point point)
		{
			int index = HitTest(point);
			if (index < 0 || !tabs[index].Selectable)
			{
				return false;
			}
			if (index != SelectedIndex)
			{
				SelectedIndex = index;
				geometry = ComputeLayout(Bounds.Width, SlotWidth);
				SelectedTabChanged?.Invoke(this, new TabEventArgs(index, tabs[index]));
			}
			TabMenuOpening?.Invoke(this, new TabEventArgs(index, tabs[index]));
			if (TabMenu != null && VisualRoot != null)
			{
				TabMenu.ShowAt(this, true);
			}
			return true;
		}
	}
}
