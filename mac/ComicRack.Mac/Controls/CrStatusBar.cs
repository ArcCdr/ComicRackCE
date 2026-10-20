using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// The status bar of the original (C-SB-1, <c>StatusStrip</c>, <c>MainForm.Designer.cs:1799-1818</c>): 24 high, a top to bottom gradient from the
	/// <c>Cr.ToolStatusStripGradient*</c> tokens with a one unit top border, a text segment that takes the remaining width, and fixed segments at the
	/// right: 20 x 19 icon slots with sunken borders (<see cref="CrStatusSlot"/>) and a slot for the size slider. Add the segments with
	/// <see cref="AddRight"/> (the first one added is the rightmost) and <see cref="SetSpring"/>.
	/// </summary>
	public class CrStatusBar : Border
	{
		private readonly DockPanel panel = new DockPanel { LastChildFill = true };

		public CrStatusBar()
		{
			Height = ChromeMetrics.StatusBarHeight;
			BorderThickness = new Thickness(0, 1, 0, 0);
			Focusable = false;
			TextElement.SetFontSize(this, ChromeFonts.ControlSize);
			Child = panel;
			ActualThemeVariantChanged += (sender, e) => ApplyTokens();
			AttachedToVisualTree += (sender, e) => ApplyTokens();
		}

		/// <summary>The segments, in dock order (see <see cref="DockPanel"/>).</summary>
		[Avalonia.Metadata.Content]
		public Avalonia.Controls.Controls Children => panel.Children;

		private void ApplyTokens()
		{
			Background = ChromePaint.VerticalGradient(this, ThemeTokens.Names.ToolStatusStripGradientBegin, ThemeTokens.Names.ToolStatusStripGradientEnd);
			BorderBrush = ChromePaint.Brush(this, ThemeTokens.Names.ToolStatusStripBorder);
		}

		/// <summary>Adds a fixed segment at the right end, left of the ones added before.</summary>
		public void AddRight(Control segment)
		{
			DockPanel.SetDock(segment, Dock.Right);
			panel.Children.Add(segment);
		}

		/// <summary>Adds a fixed segment at the left end, right of the ones added before.</summary>
		public void AddLeft(Control segment)
		{
			DockPanel.SetDock(segment, Dock.Left);
			panel.Children.Add(segment);
		}

		/// <summary>Sets the segment that takes the remaining width (<c>ToolStripStatusLabel.Spring</c>); it must be added last.</summary>
		public void SetSpring(Control segment)
		{
			segment.VerticalAlignment = VerticalAlignment.Center;
			if (segment.Margin == default)
			{
				segment.Margin = new Thickness(4, 0, 4, 0);
			}
			panel.Children.Add(segment);
		}
	}

	/// <summary>
	/// A fixed status bar segment, 20 x 19, one unit above its centre line (3 above and 2 below in the 24 unit bar). With <see cref="Sunken"/>
	/// (the default) it has the sunken 3D border of the activity icons: <c>ButtonShadow</c> on the top and left, <c>ButtonHighlight</c> on the
	/// bottom and right. A slot can host an icon (<see cref="IconName"/>) or any control (the slider).
	/// </summary>
	public class CrStatusSlot : Decorator
	{
		public static readonly StyledProperty<bool> SunkenProperty = AvaloniaProperty.Register<CrStatusSlot, bool>(nameof(Sunken), true);

		public static readonly StyledProperty<string> IconNameProperty = AvaloniaProperty.Register<CrStatusSlot, string>(nameof(IconName));

		static CrStatusSlot()
		{
			AffectsRender<CrStatusSlot>(SunkenProperty, IconNameProperty);
		}

		public CrStatusSlot()
		{
			Width = ChromeMetrics.StatusSlotWidth;
			Height = ChromeMetrics.StatusSlotHeight;
			Margin = new Thickness(0, 3, 0, 2);
			ActualThemeVariantChanged += (sender, e) => InvalidateVisual();
		}

		public bool Sunken
		{
			get => GetValue(SunkenProperty);
			set => SetValue(SunkenProperty, value);
		}

		/// <summary>An image of the original drawn in the middle of the slot.</summary>
		public string IconName
		{
			get => GetValue(IconNameProperty);
			set => SetValue(IconNameProperty, value);
		}

		public override void Render(DrawingContext context)
		{
			var area = new Rect(Bounds.Size);
			context.DrawRectangle(Brushes.Transparent, null, area);
			if (!string.IsNullOrEmpty(IconName))
			{
				var icon = Icons.TryGet(IconName, ChromePaint.VariantOf(this));
				if (icon != null)
				{
					ChromePaint.Image(context, icon, ChromePaint.Centered(icon.Size, area));
				}
			}
			if (Sunken)
			{
				ChromePaint.Frame(context, ChromePaint.Brush(this, ThemeTokens.Names.ButtonShadow), area, Sides.Left | Sides.Top);
				ChromePaint.Frame(context, ChromePaint.Brush(this, ThemeTokens.Names.ButtonHighlight), area, Sides.Right | Sides.Bottom);
			}
		}
	}
}
