using System;
using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// The library tree of the side panel (C-TREE-1, <c>LibraryTreeSkin</c>) as one control with semantic parts: every row is an original icon, the
	/// label and the counters (<see cref="CrGaugeStrip"/>). The view only names which members of its node objects carry what
	/// (<see cref="IconMember"/>, <see cref="TextMember"/>, <see cref="TotalMember"/>, <see cref="UnreadMember"/>, <see cref="NewMember"/>, <see cref="ToolTipMember"/>,
	/// <see cref="ChildrenMember"/>, <see cref="ExpandedMember"/>); the row layout, the selection look and the item metrics are the control's and its
	/// theme's, so they change in the chrome layer without a view being edited.
	/// </summary>
	public class CrTreeView : TreeView, IChromeElement
	{
		/// <summary>The width of the icon column of a row (the original's 16 pixel images).</summary>
		public const double IconColumnWidth = 16;

		/// <summary>The gap between icon, label and counter.</summary>
		public const double RowSpacing = 4;

		public static readonly StyledProperty<string> IconMemberProperty = AvaloniaProperty.Register<CrTreeView, string>(nameof(IconMember));

		public static readonly StyledProperty<string> TextMemberProperty = AvaloniaProperty.Register<CrTreeView, string>(nameof(TextMember));

		public static readonly StyledProperty<string> TotalMemberProperty = AvaloniaProperty.Register<CrTreeView, string>(nameof(TotalMember));

		public static readonly StyledProperty<string> UnreadMemberProperty = AvaloniaProperty.Register<CrTreeView, string>(nameof(UnreadMember));

		public static readonly StyledProperty<string> NewMemberProperty = AvaloniaProperty.Register<CrTreeView, string>(nameof(NewMember));

		/// <summary><c>DisplayLibraryGauges</c>: whether the counters show at all.</summary>
		public static readonly StyledProperty<bool> GaugesVisibleProperty = AvaloniaProperty.Register<CrTreeView, bool>(nameof(GaugesVisible), true);

		/// <summary><c>LibraryGauges.Total</c>.</summary>
		public static readonly StyledProperty<bool> GaugeTotalProperty = AvaloniaProperty.Register<CrTreeView, bool>(nameof(GaugeTotal), true);

		/// <summary><c>LibraryGauges.Unread</c>.</summary>
		public static readonly StyledProperty<bool> GaugeUnreadProperty = AvaloniaProperty.Register<CrTreeView, bool>(nameof(GaugeUnread), true);

		/// <summary><c>LibraryGauges.New</c>.</summary>
		public static readonly StyledProperty<bool> GaugeNewProperty = AvaloniaProperty.Register<CrTreeView, bool>(nameof(GaugeNew), true);

		/// <summary><c>LibraryGauges.Numeric</c>.</summary>
		public static readonly StyledProperty<bool> GaugeNumericProperty = AvaloniaProperty.Register<CrTreeView, bool>(nameof(GaugeNumeric), true);

		public static readonly StyledProperty<string> ToolTipMemberProperty = AvaloniaProperty.Register<CrTreeView, string>(nameof(ToolTipMember));

		public static readonly StyledProperty<string> ChildrenMemberProperty = AvaloniaProperty.Register<CrTreeView, string>(nameof(ChildrenMember));

		public static readonly StyledProperty<string> ExpandedMemberProperty = AvaloniaProperty.Register<CrTreeView, string>(nameof(ExpandedMember));

		public CrTreeView()
		{
			ItemTemplate = new FuncTreeDataTemplate<object>(BuildRow, ChildrenOf);
			ContainerPrepared += OnContainerPrepared;
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.Tree;

		protected override Type StyleKeyOverride => typeof(TreeView);

		/// <summary>The member of a node that names its original image (<see cref="CrIcon.IconName"/>).</summary>
		public string IconMember
		{
			get => GetValue(IconMemberProperty);
			set => SetValue(IconMemberProperty, value);
		}

		/// <summary>The member of a node with its label.</summary>
		public string TextMember
		{
			get => GetValue(TextMemberProperty);
			set => SetValue(TextMemberProperty, value);
		}

		/// <summary>The member (an int) with the number of books of the list; 0 or less shows no counter.</summary>
		public string TotalMember
		{
			get => GetValue(TotalMemberProperty);
			set => SetValue(TotalMemberProperty, value);
		}

		/// <summary>The member (an int) with the number of books that are not read and not new.</summary>
		public string UnreadMember
		{
			get => GetValue(UnreadMemberProperty);
			set => SetValue(UnreadMemberProperty, value);
		}

		/// <summary>The member (an int) with the number of books that are not read and were added recently.</summary>
		public string NewMember
		{
			get => GetValue(NewMemberProperty);
			set => SetValue(NewMemberProperty, value);
		}

		public bool GaugesVisible
		{
			get => GetValue(GaugesVisibleProperty);
			set => SetValue(GaugesVisibleProperty, value);
		}

		public bool GaugeTotal
		{
			get => GetValue(GaugeTotalProperty);
			set => SetValue(GaugeTotalProperty, value);
		}

		public bool GaugeUnread
		{
			get => GetValue(GaugeUnreadProperty);
			set => SetValue(GaugeUnreadProperty, value);
		}

		public bool GaugeNew
		{
			get => GetValue(GaugeNewProperty);
			set => SetValue(GaugeNewProperty, value);
		}

		public bool GaugeNumeric
		{
			get => GetValue(GaugeNumericProperty);
			set => SetValue(GaugeNumericProperty, value);
		}

		/// <summary>The member with the tool tip of the row.</summary>
		public string ToolTipMember
		{
			get => GetValue(ToolTipMemberProperty);
			set => SetValue(ToolTipMemberProperty, value);
		}

		/// <summary>The member with the child nodes (any <see cref="IEnumerable"/>).</summary>
		public string ChildrenMember
		{
			get => GetValue(ChildrenMemberProperty);
			set => SetValue(ChildrenMemberProperty, value);
		}

		/// <summary>The member (a bool, two way) that says whether the node is expanded.</summary>
		public string ExpandedMember
		{
			get => GetValue(ExpandedMemberProperty);
			set => SetValue(ExpandedMemberProperty, value);
		}

		private Control BuildRow(object node, Avalonia.Controls.INameScope scope)
		{
			var row = new CrTreeRow { DataContext = node };
			if (!string.IsNullOrEmpty(IconMember))
			{
				var icon = new CrIcon { VerticalAlignment = VerticalAlignment.Center };
				icon.Bind(CrIcon.IconNameProperty, new Binding(IconMember));
				row.Icon = icon;
			}
			var label = new TextBlock { TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
			if (!string.IsNullOrEmpty(TextMember))
			{
				label.Bind(TextBlock.TextProperty, new Binding(TextMember));
			}
			row.Label = label;
			if (!string.IsNullOrEmpty(TotalMember) || !string.IsNullOrEmpty(UnreadMember) || !string.IsNullOrEmpty(NewMember))
			{
				var gauges = new CrGaugeStrip();
				BindIfNamed(gauges, CrGaugeStrip.TotalProperty, TotalMember);
				BindIfNamed(gauges, CrGaugeStrip.UnreadProperty, UnreadMember);
				BindIfNamed(gauges, CrGaugeStrip.NewProperty, NewMember);
				gauges.Bind(IsVisibleProperty, new Binding(nameof(GaugesVisible)) { Source = this });
				gauges.Bind(CrGaugeStrip.ShowTotalProperty, new Binding(nameof(GaugeTotal)) { Source = this });
				gauges.Bind(CrGaugeStrip.ShowUnreadProperty, new Binding(nameof(GaugeUnread)) { Source = this });
				gauges.Bind(CrGaugeStrip.ShowNewProperty, new Binding(nameof(GaugeNew)) { Source = this });
				gauges.Bind(CrGaugeStrip.NumericProperty, new Binding(nameof(GaugeNumeric)) { Source = this });
				row.Gauges = gauges;
			}
			if (!string.IsNullOrEmpty(ToolTipMember))
			{
				row.Bind(ToolTip.TipProperty, new Binding(ToolTipMember));
			}
			return row;
		}

		private static void BindIfNamed(CrGaugeStrip strip, AvaloniaProperty property, string member)
		{
			if (!string.IsNullOrEmpty(member))
			{
				strip.Bind(property, new Binding(member));
			}
		}

		private IEnumerable ChildrenOf(object node)
		{
			string member = ChildrenMember;
			if (node == null || string.IsNullOrEmpty(member))
			{
				return null;
			}
			PropertyInfo property = node.GetType().GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
			return property?.GetValue(node) as IEnumerable;
		}

		private void OnContainerPrepared(object sender, ContainerPreparedEventArgs e)
		{
			if (e.Container is TreeViewItem item && !string.IsNullOrEmpty(ExpandedMember))
			{
				item.Bind(TreeViewItem.IsExpandedProperty, new Binding(ExpandedMember) { Mode = BindingMode.TwoWay });
			}
		}
	}
	/// <summary>
	/// One row of <see cref="CrTreeView"/>: icon column, label and the counters behind it. The label takes what it needs and the counters get what is
	/// left, which is the room <c>LibraryTreeSkin.DrawNodeLabel</c> measures (from the label's right edge plus 4 to the end of the row); a label that is
	/// longer than the row is trimmed and leaves nothing for them.
	/// </summary>
	public sealed class CrTreeRow : Panel, IChromeElement
	{
		public CrTreeRow()
		{
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.Tree;

		private Control icon;
		private Control label;
		private CrGaugeStrip gauges;

		public Control Icon
		{
			get => icon;
			set => Replace(ref icon, value);
		}

		public Control Label
		{
			get => label;
			set => Replace(ref label, value);
		}

		public CrGaugeStrip Gauges
		{
			get => gauges;
			set
			{
				if (gauges != null)
				{
					Children.Remove(gauges);
				}
				gauges = value;
				if (value != null)
				{
					Children.Add(value);
				}
			}
		}

		private void Replace(ref Control field, Control value)
		{
			if (field != null)
			{
				Children.Remove(field);
			}
			field = value;
			if (value != null)
			{
				Children.Add(value);
			}
		}

		private double IconSpace => icon == null ? 0 : CrTreeView.IconColumnWidth + CrTreeView.RowSpacing;

		protected override Size MeasureOverride(Size availableSize)
		{
			double height = 0;
			icon?.Measure(new Size(CrTreeView.IconColumnWidth, availableSize.Height));
			height = Math.Max(height, icon?.DesiredSize.Height ?? 0);
			label?.Measure(new Size(double.PositiveInfinity, availableSize.Height));
			double natural = label?.DesiredSize.Width ?? 0;
			height = Math.Max(height, label?.DesiredSize.Height ?? 0);
			double room = availableSize.Width - IconSpace;
			double labelWidth = double.IsInfinity(room) ? natural : Math.Min(natural, Math.Max(0, room));
			if (label != null && labelWidth < natural)
			{
				label.Measure(new Size(labelWidth, availableSize.Height));
			}
			double gaugeRoom = double.IsInfinity(room) ? double.PositiveInfinity : Math.Max(0, room - labelWidth - CrTreeView.RowSpacing);
			if (gauges != null)
			{
				gauges.Measure(new Size(gaugeRoom, double.IsInfinity(availableSize.Height) ? Math.Max(height, 1) : availableSize.Height));
				height = Math.Max(height, gauges.DesiredSize.Height);
			}
			double width = IconSpace + labelWidth + (gauges != null && gauges.DesiredSize.Width > 0 ? CrTreeView.RowSpacing + gauges.DesiredSize.Width : 0);
			return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			if (icon != null)
			{
				double h = icon.DesiredSize.Height;
				icon.Arrange(new Rect(0, Math.Max(0, (finalSize.Height - h) / 2), CrTreeView.IconColumnWidth, h));
			}
			double room = Math.Max(0, finalSize.Width - IconSpace);
			double labelWidth = label == null ? 0 : Math.Min(label.DesiredSize.Width, room);
			if (label != null)
			{
				double h = label.DesiredSize.Height;
				label.Arrange(new Rect(IconSpace, Math.Max(0, (finalSize.Height - h) / 2), labelWidth, h));
			}
			if (gauges != null)
			{
				double x = IconSpace + labelWidth + CrTreeView.RowSpacing;
				gauges.Arrange(new Rect(x, 0, Math.Max(0, finalSize.Width - x), finalSize.Height));
			}
			return finalSize;
		}
	}
}
