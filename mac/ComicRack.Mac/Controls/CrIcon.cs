using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// An image of the original by its resource name (<c>Resources.FitWidth</c> is <c>IconName="FitWidth"</c>), at the picture's own size in
	/// units (16 for most), swapped for the dark variant in the dark appearance. The picture is drawn centred and, on Retina, scaled by the
	/// display with high-quality filtering, as the original does at 200 %. Use it in templates and XAML where code would call <see cref="Icons.Get"/>.
	/// </summary>
	public class CrIcon : Control, IChromeElement
	{
		public static readonly StyledProperty<string> IconNameProperty = AvaloniaProperty.Register<CrIcon, string>(nameof(IconName));

		static CrIcon()
		{
			AffectsMeasure<CrIcon>(IconNameProperty);
			AffectsRender<CrIcon>(IconNameProperty, OpacityProperty);
		}

		public CrIcon()
		{
			IsHitTestVisible = false;
			ActualThemeVariantChanged += (sender, e) => InvalidateVisual();
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.Icons;

		public string IconName
		{
			get => GetValue(IconNameProperty);
			set => SetValue(IconNameProperty, value);
		}

		private Bitmap Bitmap => string.IsNullOrEmpty(IconName) ? null : Icons.TryGet(IconName, ChromePaint.VariantOf(this));

		protected override Size MeasureOverride(Size availableSize) => Bitmap?.Size ?? default;

		public override void Render(DrawingContext context)
		{
			Bitmap bitmap = Bitmap;
			if (bitmap != null)
			{
				ChromePaint.Icon(this, context, bitmap, ChromePaint.Centered(bitmap.Size, new Rect(Bounds.Size)));
			}
		}
	}

	/// <summary>
	/// The selection style of the original (<c>StyledRenderer</c>, chrome-guidelines section 2.4) as a control for item templates: square, a one
	/// unit frame and a flat fill, alpha by state (hot 92, selected 164, selected and hot 255; the fill a quarter of that), in the focused
	/// selection colour while the owning list or tree has the keyboard focus and in the unfocused grey otherwise. Place it behind the content of
	/// the item's template; it reads the selection, the pointer and the focus from the item that the template belongs to.
	/// </summary>
	public class StyledSelectionVisual : Control, IChromeElement
	{
		public static readonly StyledProperty<bool> IsSelectedProperty = AvaloniaProperty.Register<StyledSelectionVisual, bool>(nameof(IsSelected));

		public static readonly StyledProperty<bool> IsHotProperty = AvaloniaProperty.Register<StyledSelectionVisual, bool>(nameof(IsHot));

		public static readonly StyledProperty<bool> ViewFocusedProperty = AvaloniaProperty.Register<StyledSelectionVisual, bool>(nameof(ViewFocused));

		public static readonly StyledProperty<bool> ItemFocusedProperty = AvaloniaProperty.Register<StyledSelectionVisual, bool>(nameof(ItemFocused));

		private Control item;
		private Visual hotSource;
		private Control view;

		static StyledSelectionVisual()
		{
			AffectsRender<StyledSelectionVisual>(IsSelectedProperty, IsHotProperty, ViewFocusedProperty, ItemFocusedProperty);
		}

		public StyledSelectionVisual()
		{
			IsHitTestVisible = false;
			ActualThemeVariantChanged += (sender, e) => InvalidateVisual();
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.Tree;

		public bool IsSelected
		{
			get => GetValue(IsSelectedProperty);
			set => SetValue(IsSelectedProperty, value);
		}

		public bool IsHot
		{
			get => GetValue(IsHotProperty);
			set => SetValue(IsHotProperty, value);
		}

		/// <summary>True while the list or tree that owns the item has the keyboard focus (selects <c>Cr.SelectionFocused</c>).</summary>
		public bool ViewFocused
		{
			get => GetValue(ViewFocusedProperty);
			set => SetValue(ViewFocusedProperty, value);
		}

		/// <summary>True for the item that has the focus (a focused item without selection gets the frame only).</summary>
		public bool ItemFocused
		{
			get => GetValue(ItemFocusedProperty);
			set => SetValue(ItemFocusedProperty, value);
		}

		/// <summary>The style for the current state and appearance.</summary>
		public StyledSelection Style => StyledSelection.For(StyledSelection.GetState(IsSelected, IsHot, ItemFocused), ViewFocused, ChromePaint.VariantOf(this));

		/// <summary>When true (the default) the properties follow the item of the template, its pointer and its list; set false to drive them by hand.</summary>
		public bool Follow { get; set; } = true;

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);
			if (!Follow)
			{
				return;
			}
			item = TemplatedParent as Control;
			hotSource = this.GetVisualParent();
			view = null;
			for (Visual v = item?.GetVisualParent(); v != null; v = v.GetVisualParent())
			{
				if (v is TreeView || v is ListBox)
				{
					view = (Control)v;
					break;
				}
			}
			if (item != null)
			{
				item.PropertyChanged += OnSourceChanged;
			}
			if (hotSource is InputElement hot)
			{
				hot.PropertyChanged += OnSourceChanged;
			}
			if (view != null)
			{
				view.PropertyChanged += OnSourceChanged;
			}
			Sync();
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			if (item != null)
			{
				item.PropertyChanged -= OnSourceChanged;
			}
			if (hotSource is InputElement hot)
			{
				hot.PropertyChanged -= OnSourceChanged;
			}
			if (view != null)
			{
				view.PropertyChanged -= OnSourceChanged;
			}
			item = null;
			hotSource = null;
			view = null;
		}

		private void OnSourceChanged(object sender, AvaloniaPropertyChangedEventArgs e)
		{
			if (e.Property == SelectingItemsControl.IsSelectedProperty || e.Property == InputElement.IsPointerOverProperty || e.Property == InputElement.IsFocusedProperty || e.Property == InputElement.IsKeyboardFocusWithinProperty)
			{
				Sync();
			}
		}

		private void Sync()
		{
			IsSelected = item != null && item.GetValue(SelectingItemsControl.IsSelectedProperty);
			IsHot = hotSource is InputElement hot && hot.IsPointerOver;
			ItemFocused = item != null && item.IsFocused;
			ViewFocused = view != null && view.IsKeyboardFocusWithin;
		}

		public override void Render(DrawingContext context)
		{
			ChromePaint.Selection(this, context, new Rect(Bounds.Size), Style);
		}
	}
}
