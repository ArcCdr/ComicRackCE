using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>The side of a <see cref="CrSizableContainer"/> that carries the grip (<c>SizableContainer.GripPosition</c>).</summary>
	public enum GripPosition
	{
		None,
		Top,
		Left,
		Right,
		Bottom,
	}

	/// <summary>
	/// The docked panel of the original (C-SPLIT-1, <c>SizableContainer</c>): a child with a 6 unit grip on one side that shows the
	/// <c>MozillaGrip</c> image rotated to point where the panel would go, is <c>ControlLight</c> when hot and <c>Highlight</c> when pressed,
	/// resizes the panel by dragging, and collapses and expands with a 100 ms slide. The control's own extent along the grip axis is
	/// <see cref="CurrentExtent"/>: <see cref="ExpandedExtent"/> when expanded, 0 when collapsed (the grip width with
	/// <see cref="KeepGripVisible"/>), and in between while the slide runs. The slide advances through <see cref="AdvanceAnimation"/>;
	/// a timer calls it in the running app and tests call it with chosen times.
	/// </summary>
	public class CrSizableContainer : Decorator, IChromeElement
	{
		public static readonly StyledProperty<GripPosition> GripProperty = AvaloniaProperty.Register<CrSizableContainer, GripPosition>(nameof(Grip), GripPosition.Top);

		public static readonly StyledProperty<double> GripWidthProperty = AvaloniaProperty.Register<CrSizableContainer, double>(nameof(GripWidth), ChromeMetrics.GripWidth);

		public static readonly StyledProperty<bool> IsExpandedProperty = AvaloniaProperty.Register<CrSizableContainer, bool>(nameof(IsExpanded), true);

		public static readonly StyledProperty<double> ExpandedExtentProperty = AvaloniaProperty.Register<CrSizableContainer, double>(nameof(ExpandedExtent), 250);

		/// <summary><c>SizableContainer.EnableAnimation</c>: switches every slide off (tests, reduced motion).</summary>
		public static bool EnableAnimation { get; set; } = true;

		private enum GripState
		{
			None,
			Hot,
			Pressed,
		}

		private GripState state;
		private double currentExtent = 250;
		private bool extentInitialised;
		private double slideFrom;
		private double slideTo;
		private TimeSpan slideElapsed;
		private bool sliding;
		private DispatcherTimer timer;
		private Stopwatch watch;
		private Point pressPoint;
		private double pressExtent;

		static CrSizableContainer()
		{
			AffectsMeasure<CrSizableContainer>(GripProperty, GripWidthProperty, ExpandedExtentProperty, IsExpandedProperty);
			AffectsRender<CrSizableContainer>(GripProperty, IsExpandedProperty);
		}

		public CrSizableContainer()
		{
			ActualThemeVariantChanged += (sender, e) => InvalidateVisual();
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.SidePanel;

		public GripPosition Grip
		{
			get => GetValue(GripProperty);
			set => SetValue(GripProperty, value);
		}

		/// <summary>The grip thickness, 6 (<c>SizableContainer.GripWidth</c>).</summary>
		public double GripWidth
		{
			get => GetValue(GripWidthProperty);
			set => SetValue(GripWidthProperty, value);
		}

		/// <summary>Expanded or collapsed; changing it slides when <see cref="AnimateExpand"/> and <see cref="EnableAnimation"/> allow.</summary>
		public bool IsExpanded
		{
			get => GetValue(IsExpandedProperty);
			set => SetValue(IsExpandedProperty, value);
		}

		/// <summary>The extent (width for a left or right grip, height for a top or bottom grip) while expanded, grip included.</summary>
		public double ExpandedExtent
		{
			get => GetValue(ExpandedExtentProperty);
			set => SetValue(ExpandedExtentProperty, Math.Max(MinimumExtent, value));
		}

		/// <summary>The smallest extent a drag can make: the grip and a sliver of content.</summary>
		public double MinimumExtent => GripWidth + 20;

		/// <summary>True keeps the grip visible when collapsed so that the panel can be opened again.</summary>
		public bool KeepGripVisible { get; set; }

		public bool AnimateExpand { get; set; } = true;

		public int SlideMilliseconds { get; set; } = ChromeMetrics.SlideMilliseconds;

		public bool IsSliding => sliding;

		/// <summary>Shows the grip hot or pressed whatever the pointer does (the chrome gallery and tests).</summary>
		public void PreviewGrip(bool hot, bool pressed)
		{
			SetState(pressed ? GripState.Pressed : hot ? GripState.Hot : GripState.None);
		}

		public event EventHandler ExpandedChanged;

		/// <summary>The extent the control has now: the target, or a point on the slide.</summary>
		public double CurrentExtent
		{
			get
			{
				if (!extentInitialised)
				{
					currentExtent = Target;
					extentInitialised = true;
				}
				return currentExtent;
			}
		}

		private double Target => IsExpanded ? ExpandedExtent : (KeepGripVisible && Grip != GripPosition.None ? GripWidth : 0);

		private bool Vertical => Grip == GripPosition.Left || Grip == GripPosition.Right;

		/// <summary>The grip rectangle in the coordinates of the control (<c>GripRectangle</c>); empty with no grip.</summary>
		public Rect GripRectangle
		{
			get
			{
				double w = Bounds.Width, h = Bounds.Height, g = GripWidth;
				switch (Grip)
				{
					case GripPosition.Top:
						return new Rect(0, 0, w, g);
					case GripPosition.Left:
						return new Rect(0, 0, g, h);
					case GripPosition.Right:
						return new Rect(w - g, 0, g, h);
					case GripPosition.Bottom:
						return new Rect(0, h - g, w, g);
					default:
						return default;
				}
			}
		}

		/// <summary>The rectangle of the child (<c>DisplayRectangle</c>): the control without the grip.</summary>
		public Rect ContentRectangle
		{
			get
			{
				double w = Bounds.Width, h = Bounds.Height, g = GripWidth;
				switch (Grip)
				{
					case GripPosition.Top:
						return new Rect(0, g, w, Math.Max(0, h - g));
					case GripPosition.Left:
						return new Rect(g, 0, Math.Max(0, w - g), h);
					case GripPosition.Right:
						return new Rect(0, 0, Math.Max(0, w - g), h);
					case GripPosition.Bottom:
						return new Rect(0, 0, w, Math.Max(0, h - g));
					default:
						return new Rect(Bounds.Size);
				}
			}
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			double extent = CurrentExtent;
			Size content = availableSize;
			if (Child != null)
			{
				double g = Grip == GripPosition.None ? 0 : GripWidth;
				Child.Measure(Vertical ? new Size(Math.Max(0, ExpandedExtent - g), availableSize.Height) : new Size(availableSize.Width, Math.Max(0, ExpandedExtent - g)));
				content = Child.DesiredSize;
			}
			if (Vertical)
			{
				return new Size(Math.Min(extent, double.IsInfinity(availableSize.Width) ? extent : availableSize.Width), double.IsInfinity(availableSize.Height) ? content.Height : availableSize.Height);
			}
			return new Size(double.IsInfinity(availableSize.Width) ? content.Width : availableSize.Width, Math.Min(extent, double.IsInfinity(availableSize.Height) ? extent : availableSize.Height));
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			if (Child != null)
			{
				double g = Grip == GripPosition.None ? 0 : GripWidth;
				// the child keeps its expanded size and is clipped while the panel slides, like the original (the child's minimum size is fixed during the slide)
				double full = Math.Max(0, ExpandedExtent - g);
				Rect rect;
				switch (Grip)
				{
					case GripPosition.Top:
						rect = new Rect(0, finalSize.Height - full, finalSize.Width, full);
						break;
					case GripPosition.Left:
						rect = new Rect(finalSize.Width - full, 0, full, finalSize.Height);
						break;
					case GripPosition.Right:
						rect = new Rect(0, 0, full, finalSize.Height);
						break;
					case GripPosition.Bottom:
						rect = new Rect(0, 0, finalSize.Width, full);
						break;
					default:
						rect = new Rect(finalSize);
						break;
				}
				// the grip side stays attached to the visible edge: content is laid out from the side opposite to the grip
				Child.Arrange(rect);
				ClipToBounds = true;
			}
			return finalSize;
		}

		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
		{
			base.OnPropertyChanged(change);
			if (change.Property == IsExpandedProperty)
			{
				StartSlide(AnimateExpand && EnableAnimation && VisualRoot != null && SlideMilliseconds > 0);
				UpdateChildReach();
				ExpandedChanged?.Invoke(this, EventArgs.Empty);
			}
			else if (change.Property == ExpandedExtentProperty && IsExpanded && !sliding && state != GripState.Pressed)
			{
				currentExtent = ExpandedExtent;
				extentInitialised = true;
			}
			else if (change.Property == GripWidthProperty && !IsExpanded)
			{
				currentExtent = Target;
			}
			else if (change.Property == ChildProperty)
			{
				UpdateChildReach();
			}
		}

		private void StartSlide(bool animate)
		{
			double from = CurrentExtent;
			double to = Target;
			if (!animate || Math.Abs(from - to) < 0.5)
			{
				StopSlide();
				currentExtent = to;
				InvalidateMeasure();
				return;
			}
			slideFrom = from;
			slideTo = to;
			slideElapsed = TimeSpan.Zero;
			sliding = true;
			watch = Stopwatch.StartNew();
			timer = timer ?? new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (sender, e) => AdvanceAnimation(watch.Elapsed - slideElapsed));
			timer.Start();
		}

		/// <summary>Moves a running slide on by <paramref name="elapsed"/>; the last step reaches the target and ends the slide.</summary>
		public void AdvanceAnimation(TimeSpan elapsed)
		{
			if (!sliding)
			{
				return;
			}
			slideElapsed += elapsed;
			double t = Math.Min(1, slideElapsed.TotalMilliseconds / Math.Max(1, SlideMilliseconds));
			currentExtent = slideFrom + (slideTo - slideFrom) * t;
			if (t >= 1)
			{
				currentExtent = slideTo;
				StopSlide();
			}
			InvalidateMeasure();
		}

		private void StopSlide()
		{
			sliding = false;
			timer?.Stop();
			UpdateChildReach();
		}

		// A collapsed panel is clipped to nothing but its content is still there: hide it from the keyboard (Tab) and from assistive technology
		// once the slide has ended, and show it again before it slides open.
		private void UpdateChildReach()
		{
			Control child = Child;
			if (child == null)
			{
				return;
			}
			bool reachable = IsExpanded || sliding;
			if (!reachable && child.IsVisible)
			{
				child.IsVisible = false;
				hidChild = child;
			}
			else if (reachable && ReferenceEquals(hidChild, child))
			{
				child.IsVisible = true;
				hidChild = null;
			}
		}

		private Control hidChild;

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			if (sliding)
			{
				currentExtent = slideTo;
				StopSlide();
			}
		}

		public override void Render(DrawingContext context)
		{
			Rect grip = GripRectangle;
			if (grip.Width <= 0 || grip.Height <= 0)
			{
				return;
			}
			string token = state == GripState.Pressed ? ThemeTokens.Names.Highlight : state == GripState.Hot ? ThemeTokens.Names.ControlLight : ThemeTokens.Names.Control;
			context.DrawRectangle(ChromePaint.Brush(this, token), null, grip);
			Bitmap image = Icons.TryGet("MozillaGrip", ChromePaint.VariantOf(this));
			if (image == null)
			{
				return;
			}
			double angle = IsExpanded ? 180 : 0;
			switch (Grip)
			{
				case GripPosition.Left:
					angle -= 90;
					break;
				case GripPosition.Right:
					angle += 90;
					break;
				case GripPosition.Bottom:
					angle += 180;
					break;
			}
			double cx = grip.X + grip.Width / 2, cy = grip.Y + grip.Height / 2;
			Matrix m = Matrix.CreateTranslation(-image.Size.Width / 2, -image.Size.Height / 2) * Matrix.CreateRotation(angle * Math.PI / 180) * Matrix.CreateTranslation(Math.Floor(cx), Math.Floor(cy));
			using (context.PushClip(grip))
			using (context.PushTransform(m))
			{
				ChromePaint.Image(context, image, new Rect(image.Size));
			}
		}

		protected override void OnPointerMoved(PointerEventArgs e)
		{
			base.OnPointerMoved(e);
			Point p = e.GetPosition(this);
			if (state == GripState.Pressed)
			{
				Drag(p);
				return;
			}
			SetState(HitGrip(p) ? GripState.Hot : GripState.None);
		}

		protected override void OnPointerExited(PointerEventArgs e)
		{
			base.OnPointerExited(e);
			if (state != GripState.Pressed)
			{
				SetState(GripState.None);
			}
		}

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			if (sliding || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !HitGrip(e.GetPosition(this)))
			{
				return;
			}
			pressPoint = e.GetPosition(this);
			// the pointer position is relative to the control, which moves while it is resized: measure the drag in the parent's coordinates
			if (this.GetVisualParent() is Visual parent)
			{
				pressPoint = e.GetPosition(parent);
			}
			pressExtent = CurrentExtent;
			e.Pointer.Capture(this);
			SetState(GripState.Pressed);
			e.Handled = true;
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			base.OnPointerReleased(e);
			if (state == GripState.Pressed)
			{
				e.Pointer.Capture(null);
				SetState(HitGrip(e.GetPosition(this)) ? GripState.Hot : GripState.None);
			}
		}

		private bool HitGrip(Point p)
		{
			Rect grip = GripRectangle;
			return grip.Width > 0 && grip.Height > 0 && grip.Contains(p);
		}

		private void SetState(GripState value)
		{
			if (state != value)
			{
				state = value;
				InvalidateVisual();
			}
		}

		private void Drag(Point local)
		{
			Point p = this.GetVisualParent() is Visual parent ? this.TranslatePoint(local, parent) ?? local : local;
			double delta;
			switch (Grip)
			{
				case GripPosition.Top:
					delta = pressPoint.Y - p.Y;
					break;
				case GripPosition.Left:
					delta = pressPoint.X - p.X;
					break;
				case GripPosition.Right:
					delta = p.X - pressPoint.X;
					break;
				case GripPosition.Bottom:
					delta = p.Y - pressPoint.Y;
					break;
				default:
					return;
			}
			SetExtent(pressExtent + delta);
		}

		/// <summary>Sets the expanded extent as a drag does (clamped to <see cref="MinimumExtent"/>).</summary>
		public void SetExtent(double extent)
		{
			extent = Math.Max(MinimumExtent, extent);
			currentExtent = extent;
			extentInitialised = true;
			SetCurrentValue(ExpandedExtentProperty, extent);
			currentExtent = extent;
			InvalidateMeasure();
		}

		/// <summary>A drag from the grip centre by <paramref name="delta"/> units along the axis (positive grows the panel); used by tests.</summary>
		public void DragBy(double delta)
		{
			SetExtent(CurrentExtent + delta);
		}
	}
}
