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
	/// label and an optional counter pill (<see cref="CrGaugePill"/>). The view only names which members of its node objects carry what
	/// (<see cref="IconMember"/>, <see cref="TextMember"/>, <see cref="CountMember"/>, <see cref="CountVisibleMember"/>, <see cref="ToolTipMember"/>,
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

		public static readonly StyledProperty<string> CountMemberProperty = AvaloniaProperty.Register<CrTreeView, string>(nameof(CountMember));

		public static readonly StyledProperty<string> CountVisibleMemberProperty = AvaloniaProperty.Register<CrTreeView, string>(nameof(CountVisibleMember));

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

		/// <summary>The member with the text of the counter pill.</summary>
		public string CountMember
		{
			get => GetValue(CountMemberProperty);
			set => SetValue(CountMemberProperty, value);
		}

		/// <summary>The member (a bool) that says whether the pill shows; without it the pill shows whenever the count text is not empty.</summary>
		public string CountVisibleMember
		{
			get => GetValue(CountVisibleMemberProperty);
			set => SetValue(CountVisibleMemberProperty, value);
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
			var row = new Grid { ColumnSpacing = RowSpacing };
			row.ColumnDefinitions.Add(new ColumnDefinition(IconColumnWidth, GridUnitType.Pixel));
			row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
			row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
			row.DataContext = node;
			if (!string.IsNullOrEmpty(IconMember))
			{
				var icon = new CrIcon { VerticalAlignment = VerticalAlignment.Center };
				icon.Bind(CrIcon.IconNameProperty, new Binding(IconMember));
				row.Children.Add(icon);
			}
			var label = new TextBlock { TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
			if (!string.IsNullOrEmpty(TextMember))
			{
				label.Bind(TextBlock.TextProperty, new Binding(TextMember));
			}
			Grid.SetColumn(label, 1);
			row.Children.Add(label);
			if (!string.IsNullOrEmpty(CountMember))
			{
				var pill = new CrGaugePill { Margin = new Thickness(4, 0, 2, 0) };
				pill.Bind(CrGaugePill.TextProperty, new Binding(CountMember));
				if (!string.IsNullOrEmpty(CountVisibleMember))
				{
					pill.Bind(IsVisibleProperty, new Binding(CountVisibleMember));
				}
				Grid.SetColumn(pill, 2);
				row.Children.Add(pill);
			}
			if (!string.IsNullOrEmpty(ToolTipMember))
			{
				row.Bind(ToolTip.TipProperty, new Binding(ToolTipMember));
			}
			return row;
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
}
