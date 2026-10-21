using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>Where a toolbar item sits (<c>ToolStripItem.Alignment</c>): from the left end, or from the right end.</summary>
	public enum ToolBarAlignment
	{
		Left,
		Right,
	}

	/// <summary>Whether a toolbar item may move to the overflow drop-down (<c>ToolStripItem.Overflow</c>).</summary>
	public enum ToolBarOverflow
	{
		/// <summary>It moves to the drop-down when there is no room for it (the default).</summary>
		AsNeeded,

		/// <summary>It stays in the bar however narrow the bar is.</summary>
		Never,

		/// <summary>It is always in the drop-down.</summary>
		Always,
	}

	/// <summary>
	/// The layout of a toolbar of the original, <c>ToolStripLayoutStyle.HorizontalStackWithOverflow</c>, without any control: items stack from the left,
	/// the right aligned ones from the right; when the items do not all fit, the bar shows the overflow button at its right end and the items that come
	/// last and may overflow (<see cref="ToolBarOverflow.AsNeeded"/>) go to its drop-down, in order, from the first one that does not fit on.
	/// </summary>
	public static class ToolBarLayout
	{
		/// <summary>The width of the overflow button (<c>ToolStripOverflowButton</c>).</summary>
		public const double OverflowButtonWidth = 16;

		public readonly struct Item
		{
			public Item(double width, ToolBarAlignment alignment, ToolBarOverflow overflow)
			{
				Width = width;
				Alignment = alignment;
				Overflow = overflow;
			}

			public double Width { get; }

			public ToolBarAlignment Alignment { get; }

			public ToolBarOverflow Overflow { get; }
		}

		public sealed class Plan
		{
			public Plan(double[] x, bool[] inOverflow, bool hasOverflow)
			{
				X = x;
				InOverflow = inOverflow;
				HasOverflow = hasOverflow;
			}

			/// <summary>Left edge of each item in the bar (meaningless for an item in the drop-down).</summary>
			public double[] X { get; }

			/// <summary>True for an item that is in the drop-down.</summary>
			public bool[] InOverflow { get; }

			/// <summary>True when the overflow button is shown.</summary>
			public bool HasOverflow { get; }
		}

		public static Plan Compute(IReadOnlyList<Item> items, double width, double overflowWidth = OverflowButtonWidth)
		{
			int n = items.Count;
			var inOverflow = new bool[n];
			var x = new double[n];
			double wanted = 0;
			bool always = false;
			for (int i = 0; i < n; i++)
			{
				if (items[i].Overflow == ToolBarOverflow.Always)
				{
					always = true;
				}
				else
				{
					wanted += items[i].Width;
				}
			}
			bool hasOverflow = always || wanted > width + 0.001;
			double room = hasOverflow ? Math.Max(0, width - overflowWidth) : width;
			if (hasOverflow)
			{
				double never = 0;
				for (int i = 0; i < n; i++)
				{
					if (items[i].Overflow == ToolBarOverflow.Always)
					{
						inOverflow[i] = true;
					}
					else if (items[i].Overflow == ToolBarOverflow.Never)
					{
						never += items[i].Width;
					}
				}
				double left = room - never;
				double used = 0;
				bool full = false;
				for (int i = 0; i < n; i++)
				{
					if (items[i].Overflow != ToolBarOverflow.AsNeeded)
					{
						continue;
					}
					if (!full && used + items[i].Width <= left + 0.001)
					{
						used += items[i].Width;
					}
					else
					{
						full = true;
						inOverflow[i] = true;
					}
				}
			}
			double at = 0;
			for (int i = 0; i < n; i++)
			{
				if (!inOverflow[i] && items[i].Alignment == ToolBarAlignment.Left)
				{
					x[i] = at;
					at += items[i].Width;
				}
			}
			at = room;
			for (int i = 0; i < n; i++)
			{
				if (!inOverflow[i] && items[i].Alignment == ToolBarAlignment.Right)
				{
					at -= items[i].Width;
					x[i] = at;
				}
			}
			return new Plan(x, inOverflow, hasOverflow);
		}
	}

	/// <summary>
	/// The toolbar of the original (C-TB-1, <c>ToolStrip</c> with the professional renderer): 25 high, a top to bottom gradient from the
	/// <c>Cr.ToolToolStripGradient*</c> tokens, a one pixel bottom border, flat 16 pixel icon buttons. Children are <see cref="CrToolButton"/>,
	/// <see cref="CrSplitButton"/>, <see cref="CrToolSeparator"/> or any control (the quick search box). Not focusable, like a toolstrip.
	/// It lays its items out as <c>HorizontalStackWithOverflow</c> (<see cref="ToolBarLayout"/>): <see cref="AlignmentProperty"/> puts an item at the
	/// right end (the quick search of the browser), <see cref="OverflowProperty"/> says whether it may leave the bar; when the bar is too narrow the
	/// last items that may go are replaced by the overflow button, whose drop-down shows them.
	/// </summary>
	public class CrToolBar : Border, IChromeElement
	{
		/// <summary><c>ToolStripItem.Alignment</c> of a child.</summary>
		public static readonly AttachedProperty<ToolBarAlignment> AlignmentProperty = AvaloniaProperty.RegisterAttached<CrToolBar, Control, ToolBarAlignment>("Alignment");

		/// <summary><c>ToolStripItem.Overflow</c> of a child.</summary>
		public static readonly AttachedProperty<ToolBarOverflow> OverflowProperty = AvaloniaProperty.RegisterAttached<CrToolBar, Control, ToolBarOverflow>("Overflow");

		public static ToolBarAlignment GetAlignment(Control control) => control.GetValue(AlignmentProperty);

		public static void SetAlignment(Control control, ToolBarAlignment value) => control.SetValue(AlignmentProperty, value);

		public static ToolBarOverflow GetOverflow(Control control) => control.GetValue(OverflowProperty);

		public static void SetOverflow(Control control, ToolBarOverflow value) => control.SetValue(OverflowProperty, value);

		private readonly ItemPanel items = new ItemPanel();
		private readonly Border line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
		private readonly OverflowButton overflowButton = new OverflowButton();
		private readonly StackPanel overflowHost = new StackPanel { Orientation = Orientation.Vertical };
		private readonly Border overflowFrame = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(2) };
		private readonly Popup overflowPopup = new Popup { Placement = PlacementMode.BottomEdgeAlignedRight, IsLightDismissEnabled = true };
		private readonly List<(Control Item, int Index)> detached = new List<(Control, int)>();

		public CrToolBar()
		{
			Height = ChromeMetrics.ToolBarHeight;
			Focusable = false;
			ClipToBounds = true;
			overflowFrame.Child = overflowHost;
			overflowPopup.Child = overflowFrame;
			overflowPopup.PlacementTarget = overflowButton;
			overflowPopup.Closed += (sender, e) => ReturnOverflowItems();
			overflowFrame.AddHandler(Button.ClickEvent, (sender, e) => overflowPopup.IsOpen = false);
			overflowButton.Click += (sender, e) => ShowOverflow();
			items.OverflowChanged += (sender, e) => overflowButton.Shown = items.HasOverflow;
			Child = new Grid { Children = { items, line, overflowButton, overflowPopup } };
			ActualThemeVariantChanged += (sender, e) => ApplyTokens();
			AttachedToVisualTree += (sender, e) => ApplyTokens();
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.Toolbar;

		/// <summary>The buttons, separators and other controls, left to right.</summary>
		[Avalonia.Metadata.Content]
		public Avalonia.Controls.Controls Children => items.Children;

		/// <summary>The items that do not fit and are in the overflow drop-down (in the order of <see cref="Children"/>); empty while everything fits.</summary>
		public IReadOnlyList<Control> OverflowItems => items.OverflowItems;

		/// <summary>True while the overflow button is shown.</summary>
		public bool HasOverflow => items.HasOverflow;

		/// <summary>The overflow button (tests, the chrome gallery).</summary>
		public Control OverflowButtonControl => overflowButton;

		/// <summary>True while the overflow drop-down is open.</summary>
		public bool IsOverflowOpen => overflowPopup.IsOpen;

		/// <summary>True for a toolbar that sits on another surface and draws no background of its own (the reader toolbar in the tab bar).</summary>
		public bool Transparent
		{
			get => transparent;
			set
			{
				transparent = value;
				ApplyTokens();
			}
		}

		private bool transparent;

		/// <summary>
		/// Opens the overflow drop-down: the items that are not in the bar move into it (they are the same controls, so their menus and states work) and
		/// come back to the bar when it closes. Does nothing when everything fits.
		/// </summary>
		public void ShowOverflow()
		{
			if (overflowPopup.IsOpen || !items.HasOverflow)
			{
				return;
			}
			var moving = new List<Control>(items.OverflowItems);
			if (moving.Count == 0)
			{
				return;
			}
			items.Freeze();
			foreach (Control item in moving)
			{
				if (item is CrToolSeparator)
				{
					continue; // a separator is a vertical line: it stays out of the vertical list
				}
				detached.Add((item, items.Children.IndexOf(item)));
			}
			foreach ((Control item, int index) in detached)
			{
				items.Children.Remove(item);
				overflowHost.Children.Add(item);
			}
			overflowFrame.Background = ChromePaint.Brush(this, ThemeTokens.Names.ToolToolStripDropDownBackground);
			overflowFrame.BorderBrush = ChromePaint.Brush(this, ThemeTokens.Names.ToolMenuBorder);
			overflowPopup.IsOpen = true;
		}

		/// <summary>Closes the overflow drop-down; its items go back to the bar.</summary>
		public void HideOverflow()
		{
			overflowPopup.IsOpen = false;
			ReturnOverflowItems();
		}

		private void ReturnOverflowItems()
		{
			if (detached.Count == 0)
			{
				items.Unfreeze();
				return;
			}
			foreach ((Control item, int index) in detached)
			{
				overflowHost.Children.Remove(item);
			}
			foreach ((Control item, int index) in detached.OrderBy(d => d.Index))
			{
				items.Children.Insert(Math.Min(index, items.Children.Count), item);
			}
			detached.Clear();
			items.Unfreeze();
		}

		private void ApplyTokens()
		{
			if (transparent)
			{
				Background = null;
				line.Background = null;
				return;
			}
			Background = ChromePaint.VerticalGradient(this, ThemeTokens.Names.ToolToolStripGradientBegin, ThemeTokens.Names.ToolToolStripGradientMiddle, ThemeTokens.Names.ToolToolStripGradientEnd);
			line.Background = ChromePaint.Brush(this, ThemeTokens.Names.ToolToolStripBorder);
		}

		/// <summary>The panel that lays the items out by a <see cref="ToolBarLayout.Plan"/>.</summary>
		private sealed class ItemPanel : Panel
		{
			private readonly List<Control> overflowItems = new List<Control>();
			private readonly Dictionary<Control, double> positions = new Dictionary<Control, double>();
			private bool frozen;

			public event EventHandler OverflowChanged;

			public bool HasOverflow { get; private set; }

			public IReadOnlyList<Control> OverflowItems => overflowItems;

			/// <summary>While the drop-down is open the items it shows have left the panel: the plan of the others stays as it was.</summary>
			public void Freeze() => frozen = true;

			public void Unfreeze()
			{
				frozen = false;
				InvalidateMeasure();
			}

			protected override Size MeasureOverride(Size availableSize)
			{
				double height = double.IsInfinity(availableSize.Height) ? ChromeMetrics.ToolBarHeight : availableSize.Height;
				double total = 0;
				foreach (Control child in Children)
				{
					child.Measure(new Size(double.PositiveInfinity, height));
					total += child.DesiredSize.Width;
				}
				return new Size(double.IsInfinity(availableSize.Width) ? total : Math.Min(availableSize.Width, Math.Max(total, 0)), height);
			}

			protected override Size ArrangeOverride(Size finalSize)
			{
				if (frozen)
				{
					foreach (Control child in Children)
					{
						if (positions.TryGetValue(child, out double at))
						{
							child.Arrange(new Rect(at, 0, child.DesiredSize.Width, finalSize.Height));
						}
					}
					return finalSize;
				}
				var list = new List<ToolBarLayout.Item>(Children.Count);
				foreach (Control child in Children)
				{
					list.Add(new ToolBarLayout.Item(child.DesiredSize.Width, GetAlignment(child), GetOverflow(child)));
				}
				ToolBarLayout.Plan plan = ToolBarLayout.Compute(list, finalSize.Width);
				var hidden = new List<Control>();
				positions.Clear();
				for (int i = 0; i < Children.Count; i++)
				{
					Control child = Children[i];
					double width = child.DesiredSize.Width;
					if (plan.InOverflow[i])
					{
						// out of the bar and out of sight (the bar clips): it keeps its place in the tree, so its measure stays known for the next plan
						hidden.Add(child);
						positions[child] = -(width + 100);
						child.Arrange(new Rect(-(width + 100), 0, width, finalSize.Height));
					}
					else
					{
						positions[child] = plan.X[i];
						child.Arrange(new Rect(plan.X[i], 0, width, finalSize.Height));
					}
				}
				bool changed = plan.HasOverflow != HasOverflow || hidden.Count != overflowItems.Count || !hidden.SequenceEqual(overflowItems);
				HasOverflow = plan.HasOverflow;
				overflowItems.Clear();
				overflowItems.AddRange(hidden);
				if (changed)
				{
					OverflowChanged?.Invoke(this, EventArgs.Empty);
				}
				return finalSize;
			}
		}

		/// <summary>
		/// The overflow button (<c>ToolStripOverflowButton</c>): 16 wide, the <c>Cr.ToolOverflowButtonGradient*</c> background while at rest, the button
		/// states of the toolbar when hot or open, and a small arrow under a bar. Shown only while the bar overflows; hidden it draws nothing and takes no
		/// pointer.
		/// </summary>
		internal sealed class OverflowButton : Button
		{
			public static readonly StyledProperty<bool> ShownProperty = AvaloniaProperty.Register<OverflowButton, bool>(nameof(Shown));

			static OverflowButton()
			{
				AffectsRender<OverflowButton>(ShownProperty, IsPointerOverProperty, IsPressedProperty);
				ShownProperty.Changed.AddClassHandler<OverflowButton>((b, e) => b.IsHitTestVisible = b.Shown);
			}

			public OverflowButton()
			{
				Width = ToolBarLayout.OverflowButtonWidth;
				HorizontalAlignment = HorizontalAlignment.Right;
				VerticalAlignment = VerticalAlignment.Stretch;
				Margin = new Thickness(0, 0, 0, 1);
				Padding = new Thickness(0);
				Focusable = false;
				IsHitTestVisible = false;
				ActualThemeVariantChanged += (sender, e) => InvalidateVisual();
			}

			protected override Type StyleKeyOverride => typeof(OverflowButton);

			public bool Shown
			{
				get => GetValue(ShownProperty);
				set => SetValue(ShownProperty, value);
			}

			public override void Render(DrawingContext context)
			{
				var area = new Rect(Bounds.Size);
				context.DrawRectangle(Brushes.Transparent, null, area);
				if (!Shown)
				{
					return;
				}
				if (IsPressed)
				{
					ToolPaint.Background(context, this, area, ToolButtonState.Pressed);
				}
				else if (IsPointerOver)
				{
					ToolPaint.Background(context, this, area, ToolButtonState.Hot);
				}
				else
				{
					context.DrawRectangle(ChromePaint.VerticalGradient(this, ThemeTokens.Names.ToolOverflowButtonGradientBegin, ThemeTokens.Names.ToolOverflowButtonGradientMiddle, ThemeTokens.Names.ToolOverflowButtonGradientEnd), null, area);
				}
				// the bar over the arrow (ToolStripProfessionalRenderer.OnRenderOverflowButtonBackground)
				IBrush ink = ChromePaint.Brush(this, ThemeTokens.Names.ControlText);
				double mid = Math.Floor(area.Width / 2);
				double top = Math.Floor(area.Height / 2) - 3;
				ChromePaint.HLine(context, ink, mid - 3, top, 7);
				Bitmap glyph = Icons.TryGet("cYo.Common.Windows:SmallArrowDown", ChromePaint.VariantOf(this));
				if (glyph != null)
				{
					ChromePaint.Image(context, glyph, ChromePaint.Centered(glyph.Size, new Rect(0, top + 2, area.Width, glyph.Size.Height)));
				}
			}
		}
	}

	/// <summary>The vertical line between toolbar groups: 6 wide, a dark and a light line in the middle (<c>ToolSeparatorDark</c>, <c>ToolSeparatorLight</c>).</summary>
	public class CrToolSeparator : Control, IChromeElement
	{
		public ChromeElement Element => ChromeElement.Toolbar;

		public CrToolSeparator()
		{
			Width = ChromeMetrics.ToolSeparatorWidth;
			Height = ChromeMetrics.ToolButtonHeight;
			Margin = new Thickness(0, 1, 0, 2);
			IsHitTestVisible = false;
			ActualThemeVariantChanged += (sender, e) => InvalidateVisual();
			ChromeVariants.Bind(this);
		}

		public override void Render(DrawingContext context)
		{
			double x = Math.Floor(Bounds.Width / 2) - 1;
			ChromePaint.VLine(context, ChromePaint.Brush(this, ThemeTokens.Names.ToolSeparatorDark), x, 2, Bounds.Height - 4);
			ChromePaint.VLine(context, ChromePaint.Brush(this, ThemeTokens.Names.ToolSeparatorLight), x + 1, 2, Bounds.Height - 4);
		}
	}

	/// <summary>
	/// A toolbar button (<c>ToolStripButton</c>): a flat 16 pixel image of the original by its resource name and optional text, 23 x 22 at least.
	/// Normal is transparent; hover is the <c>ToolButtonSelectedGradient*</c> gradient with the <c>ToolButtonSelectedBorder</c> frame; pressed
	/// the pressed gradient and border; checked the <c>ToolButtonCheckedHighlight</c> fill with its border. The text of "100 %" and "0°" is shown
	/// to the right of the image.
	/// </summary>
	public class CrToolButton : Button, IChromeElement
	{
		public static readonly StyledProperty<string> IconNameProperty = AvaloniaProperty.Register<CrToolButton, string>(nameof(IconName));

		public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<CrToolButton, string>(nameof(Text));

		public static readonly StyledProperty<bool> IsCheckedProperty = AvaloniaProperty.Register<CrToolButton, bool>(nameof(IsChecked));

		public static readonly StyledProperty<bool> CheckOnClickProperty = AvaloniaProperty.Register<CrToolButton, bool>(nameof(CheckOnClick));

		static CrToolButton()
		{
			AffectsRender<CrToolButton>(IsCheckedProperty, IconNameProperty, TextProperty, IsPointerOverProperty, IsPressedProperty, IsEnabledProperty);
			AffectsMeasure<CrToolButton>(IconNameProperty, TextProperty);
		}

		public CrToolButton()
		{
			Margin = new Thickness(0, 1, 0, 2);
			Padding = new Thickness(3, 0);
			Focusable = false;
			FontSize = ChromeFonts.ControlSize;
			ActualThemeVariantChanged += (sender, e) => InvalidateVisual();
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.Toolbar;

		protected override Type StyleKeyOverride => typeof(CrToolButton);

		/// <summary>The resource name of the image in the original (<c>Resources.FitWidth</c> is <c>"FitWidth"</c>).</summary>
		public string IconName
		{
			get => GetValue(IconNameProperty);
			set => SetValue(IconNameProperty, value);
		}

		/// <summary>Text to the right of the image; null or empty shows the image only.</summary>
		public string Text
		{
			get => GetValue(TextProperty);
			set => SetValue(TextProperty, value);
		}

		/// <summary>For a toggle button (<c>ToolStripButton.Checked</c>): the pressed-in look.</summary>
		public bool IsChecked
		{
			get => GetValue(IsCheckedProperty);
			set => SetValue(IsCheckedProperty, value);
		}

		/// <summary><c>ToolStripButton.CheckOnClick</c>: a click flips <see cref="IsChecked"/>.</summary>
		public bool CheckOnClick
		{
			get => GetValue(CheckOnClickProperty);
			set => SetValue(CheckOnClickProperty, value);
		}

		/// <summary>The width reserved to the right of the content by a subclass (the arrow part of a split button).</summary>
		protected virtual double ExtraWidth => 0;

		protected override void OnClick()
		{
			if (CheckOnClick)
			{
				IsChecked = !IsChecked;
			}
			base.OnClick();
		}

		/// <summary>Shows the button in a fixed state whatever the pointer does (the chrome gallery and tests); null follows the pointer.</summary>
		public ToolButtonState? DisplayState
		{
			get => displayState;
			set
			{
				displayState = value;
				InvalidateVisual();
			}
		}

		private ToolButtonState? displayState;

		/// <summary>The state of the button as the renderer sees it.</summary>
		public ToolButtonState State
		{
			get
			{
				if (displayState.HasValue)
				{
					return displayState.Value;
				}
				if (!IsEnabled)
				{
					return ToolButtonState.Disabled;
				}
				if (IsPressed)
				{
					return ToolButtonState.Pressed;
				}
				if (IsPointerOver)
				{
					return IsChecked ? ToolButtonState.CheckedHot : ToolButtonState.Hot;
				}
				return IsChecked ? ToolButtonState.Checked : ToolButtonState.Normal;
			}
		}

		protected Bitmap IconBitmap => string.IsNullOrEmpty(IconName) ? null : Icons.TryGet(IconName, ChromePaint.VariantOf(this));

		private FormattedText MeasureText()
		{
			if (string.IsNullOrEmpty(Text))
			{
				return null;
			}
			return new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyle, FontWeight), FontSize, ChromePaint.Brush(this, ThemeTokens.Names.ControlText));
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			double width = Padding.Left + Padding.Right;
			Bitmap icon = IconBitmap;
			if (icon != null)
			{
				width += icon.Size.Width;
			}
			FormattedText text = MeasureText();
			if (text != null)
			{
				width += (icon != null ? 3 : 0) + Math.Ceiling(text.Width);
			}
			width += ExtraWidth;
			return new Size(Math.Max(ChromeMetrics.ToolButtonMinWidth, width), ChromeMetrics.ToolButtonHeight);
		}

		public override void Render(DrawingContext context)
		{
			var area = new Rect(Bounds.Size);
			context.DrawRectangle(Brushes.Transparent, null, area);
			RenderBackground(context, area);
			double right = area.Width - ExtraWidth;
			RenderContent(context, new Rect(0, 0, right, area.Height));
		}

		protected virtual void RenderBackground(DrawingContext context, Rect area)
		{
			ToolPaint.Background(context, this, area, State);
		}

		protected virtual void RenderContent(DrawingContext context, Rect area)
		{
			Bitmap icon = IconBitmap;
			FormattedText text = MeasureText();
			double opacity = IsEnabled ? 1 : 0.45;
			double contentWidth = (icon?.Size.Width ?? 0) + (text != null ? (icon != null ? 3 : 0) + Math.Ceiling(text.Width) : 0);
			double x = Math.Floor(area.X + (area.Width - contentWidth) / 2);
			if (icon != null)
			{
				double y = Math.Floor((area.Height - icon.Size.Height) / 2);
				ChromePaint.Image(context, icon, new Rect(x, y, icon.Size.Width, icon.Size.Height), opacity);
				x += icon.Size.Width + 3;
			}
			if (text != null)
			{
				text.SetForegroundBrush(ChromePaint.Brush(this, IsEnabled ? ThemeTokens.Names.ControlText : ThemeTokens.Names.GrayText));
				context.DrawText(text, new Point(x, Math.Floor((area.Height - text.Height) / 2)));
			}
		}
	}

	/// <summary>The states of a toolbar button.</summary>
	public enum ToolButtonState
	{
		Normal,
		Hot,
		Pressed,
		Checked,
		CheckedHot,
		Disabled,
	}

	/// <summary>The professional renderer's button backgrounds from the <c>Cr.Tool*</c> tokens.</summary>
	public static class ToolPaint
	{
		public static void Background(DrawingContext context, Control control, Rect area, ToolButtonState state)
		{
			switch (state)
			{
				case ToolButtonState.Hot:
					context.DrawRectangle(ChromePaint.VerticalGradient(control, ThemeTokens.Names.ToolButtonSelectedGradientBegin, ThemeTokens.Names.ToolButtonSelectedGradientMiddle, ThemeTokens.Names.ToolButtonSelectedGradientEnd), null, area);
					ChromePaint.Frame(context, ChromePaint.Brush(control, ThemeTokens.Names.ToolButtonSelectedBorder), area);
					break;
				case ToolButtonState.Pressed:
					context.DrawRectangle(ChromePaint.VerticalGradient(control, ThemeTokens.Names.ToolButtonPressedGradientBegin, ThemeTokens.Names.ToolButtonPressedGradientMiddle, ThemeTokens.Names.ToolButtonPressedGradientEnd), null, area);
					ChromePaint.Frame(context, ChromePaint.Brush(control, ThemeTokens.Names.ToolButtonPressedBorder), area);
					break;
				case ToolButtonState.Checked:
					context.DrawRectangle(ChromePaint.Brush(control, ThemeTokens.Names.ToolButtonCheckedHighlight), null, area);
					ChromePaint.Frame(context, ChromePaint.Brush(control, ThemeTokens.Names.ToolButtonCheckedHighlightBorder), area);
					break;
				case ToolButtonState.CheckedHot:
					context.DrawRectangle(ChromePaint.Brush(control, ThemeTokens.Names.ToolButtonSelectedHighlight), null, area);
					ChromePaint.Frame(context, ChromePaint.Brush(control, ThemeTokens.Names.ToolButtonSelectedHighlightBorder), area);
					break;
			}
		}
	}

	/// <summary>
	/// A toolbar drop down button (<c>ToolStripDropDownButton</c>): the image and a small arrow on the right (29 wide with a 16 pixel image), with no
	/// split line; a click opens <see cref="DropDown"/>. Without a menu (the feature is not built yet) the button is disabled by its owner.
	/// </summary>
	public class CrDropDownButton : CrToolButton
	{
		public static readonly StyledProperty<FlyoutBase> DropDownProperty = AvaloniaProperty.Register<CrDropDownButton, FlyoutBase>(nameof(DropDown));

		private bool open;

		public FlyoutBase DropDown
		{
			get => GetValue(DropDownProperty);
			set => SetValue(DropDownProperty, value);
		}

		/// <summary>Raised before the menu opens (the original's <c>DropDownOpening</c>): the handler fills the menu.</summary>
		public event EventHandler DropDownOpening;

		protected override double ExtraWidth => ChromeMetrics.DropDownArrowWidth;

		protected override void OnClick()
		{
			base.OnClick();
			FlyoutBase flyout = DropDown;
			if (flyout == null || open)
			{
				return;
			}
			DropDownOpening?.Invoke(this, EventArgs.Empty);
			open = true;
			flyout.Closed += OnClosed;
			flyout.ShowAt(this);
			InvalidateVisual();
		}

		private void OnClosed(object sender, EventArgs e)
		{
			((FlyoutBase)sender).Closed -= OnClosed;
			open = false;
			InvalidateVisual();
		}

		protected override void RenderBackground(DrawingContext context, Rect area)
		{
			ToolPaint.Background(context, this, area, open ? ToolButtonState.Pressed : State);
			Bitmap glyph = Icons.TryGet("cYo.Common.Windows:SmallArrowDown", ChromePaint.VariantOf(this));
			if (glyph != null)
			{
				var slot = new Rect(Math.Max(0, area.Width - ChromeMetrics.DropDownArrowWidth - 2), 0, ChromeMetrics.DropDownArrowWidth + 2, area.Height);
				ChromePaint.Image(context, glyph, ChromePaint.Centered(glyph.Size, slot), IsEnabled ? 1 : 0.45);
			}
		}
	}

	/// <summary>
	/// A toolbar split button (<c>ToolStripSplitButton</c>): the button part and a 14 wide arrow part (<c>SplitButton.PushButtonWidth</c>) that
	/// opens <see cref="DropDown"/>. While the pointer is over it or the menu is open a split line (<c>SplitButtonSeparatorLeft</c> and
	/// <c>SplitButtonSeparatorRight</c>, as <c>SplitButton.DrawSplitLine</c>) separates the parts. A press on the arrow part never raises Click.
	/// </summary>
	public class CrSplitButton : CrToolButton
	{
		public static readonly StyledProperty<FlyoutBase> DropDownProperty = AvaloniaProperty.Register<CrSplitButton, FlyoutBase>(nameof(DropDown));

		private bool pressedArrow;

		private bool open;

		public FlyoutBase DropDown
		{
			get => GetValue(DropDownProperty);
			set => SetValue(DropDownProperty, value);
		}

		/// <summary>Raised before the menu opens (the original's <c>DropDownOpening</c>): the handler fills the menu.</summary>
		public event EventHandler DropDownOpening;

		public bool IsDropDownOpen => open;

		protected override double ExtraWidth => ChromeMetrics.SplitArrowWidth;

		/// <summary>The arrow part in the coordinates of the control.</summary>
		public Rect ArrowBounds => new Rect(Math.Max(0, Bounds.Width - ChromeMetrics.SplitArrowWidth), 0, ChromeMetrics.SplitArrowWidth, Bounds.Height);

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && ArrowBounds.Contains(e.GetPosition(this)))
			{
				pressedArrow = true;
				e.Handled = true;
				ShowDropDown();
				return;
			}
			pressedArrow = false;
			base.OnPointerPressed(e);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.Key == Key.Down)
			{
				e.Handled = true;
				ShowDropDown();
				return;
			}
			base.OnKeyDown(e);
		}

		/// <summary>Opens the menu below the button, as the original does for a click on the arrow part or the Down key.</summary>
		public void ShowDropDown()
		{
			FlyoutBase flyout = DropDown;
			if (flyout == null || open)
			{
				return;
			}
			DropDownOpening?.Invoke(this, EventArgs.Empty);
			open = true;
			flyout.Closed += OnClosed;
			flyout.ShowAt(this);
			InvalidateVisual();
		}

		private void OnClosed(object sender, EventArgs e)
		{
			((FlyoutBase)sender).Closed -= OnClosed;
			open = false;
			pressedArrow = false;
			InvalidateVisual();
		}

		protected override void RenderBackground(DrawingContext context, Rect area)
		{
			ToolButtonState state = open ? ToolButtonState.Pressed : State;
			ToolPaint.Background(context, this, area, state);
			if (state == ToolButtonState.Hot || state == ToolButtonState.Pressed || state == ToolButtonState.CheckedHot)
			{
				Rect arrow = ArrowBounds;
				ChromePaint.VLine(context, ChromePaint.Brush(this, ThemeTokens.Names.SplitButtonSeparatorLeft), arrow.X - 1, 2, area.Height - 4);
				ChromePaint.VLine(context, ChromePaint.Brush(this, ThemeTokens.Names.SplitButtonSeparatorRight), arrow.X, 2, area.Height - 4);
			}
			Bitmap glyph = Icons.TryGet("cYo.Common.Windows:SmallArrowDown", ChromePaint.VariantOf(this));
			if (glyph != null)
			{
				Rect target = ChromePaint.Centered(glyph.Size, ArrowBounds);
				ChromePaint.Image(context, glyph, target, IsEnabled ? 1 : 0.45);
			}
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			if (pressedArrow)
			{
				e.Handled = true;
				return;
			}
			base.OnPointerReleased(e);
		}
	}
}
