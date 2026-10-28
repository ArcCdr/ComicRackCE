// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// FlowLayoutPanel (P4.9c): the flow layout engine of System.Windows.Forms (System.Windows.Forms.Layout.FlowLayout of dotnet/winforms, MIT), written against the facade's Control.
// The children are arranged one after the other in rows (or columns) in the order of Controls; a row ends when the next control does not fit into the display rectangle
// (WrapContents) or after a control with a flow break. The cells are as large as the control plus its Margin; the cross axis of a row is as large as its tallest cell and
// every control is placed in it by its anchors (Dock counts as the anchors it stands for): Top and Bottom stretch, one of them aligns, none centers.
using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms
{
	/// <summary>The settings object behind a layout engine: the base of <see cref="FlowLayoutSettings"/> and <see cref="TableLayoutSettings"/>.</summary>
	public abstract partial class LayoutSettings
	{
		protected LayoutSettings()
		{
		}

		public virtual LayoutEngine LayoutEngine => null;
	}

	/// <summary>The flow direction and wrapping of a FlowLayoutPanel, and the flow breaks of its children.</summary>
	public partial class FlowLayoutSettings : LayoutSettings
	{
		private readonly Control owner;
		private FlowDirection flowDirection = FlowDirection.LeftToRight;
		private bool wrapContents = true;

		internal FlowLayoutSettings(Control owner)
		{
			this.owner = owner;
		}

		public FlowDirection FlowDirection
		{
			get => flowDirection;
			set
			{
				if (value < FlowDirection.LeftToRight || value > FlowDirection.BottomUp)
				{
					throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(FlowDirection));
				}
				if (flowDirection != value)
				{
					flowDirection = value;
					owner?.PerformLayout(owner, "FlowDirection");
				}
			}
		}

		public bool WrapContents
		{
			get => wrapContents;
			set
			{
				if (wrapContents != value)
				{
					wrapContents = value;
					owner?.PerformLayout(owner, "WrapContents");
				}
			}
		}

		public override LayoutEngine LayoutEngine => FlowLayout.Instance;

		public bool GetFlowBreak(object child) => FlowLayout.GetFlowBreak(FlowLayout.AsControl(child));

		public void SetFlowBreak(object child, bool value) => FlowLayout.SetFlowBreak(FlowLayout.AsControl(child), value);
	}

	/// <summary>A panel that arranges its children in a flow.</summary>
	public partial class FlowLayoutPanel : Panel, System.ComponentModel.IExtenderProvider
	{
		private FlowLayoutSettings settings;

		public FlowLayoutPanel()
		{
		}

		internal FlowLayoutSettings Settings => settings ?? (settings = new FlowLayoutSettings(this));

		public override LayoutEngine LayoutEngine => FlowLayout.Instance;

		public FlowDirection FlowDirection
		{
			get => Settings.FlowDirection;
			set => Settings.FlowDirection = value;
		}

		public bool WrapContents
		{
			get => Settings.WrapContents;
			set => Settings.WrapContents = value;
		}

		public bool GetFlowBreak(Control control) => FlowLayout.GetFlowBreak(control);

		public void SetFlowBreak(Control control, bool value) => FlowLayout.SetFlowBreak(control, value);

		bool System.ComponentModel.IExtenderProvider.CanExtend(object extendee) => extendee is Control c && c.ParentInternal == this;
	}
}

namespace System.Windows.Forms.Layout
{
	/// <summary>Helpers that the flow and the table engine share: the unified anchor of a control, alignment inside a cell, the size of an element.</summary>
	internal static class CellLayout
	{
		internal static bool Has(AnchorStyles anchor, AnchorStyles flag) => (anchor & flag) == flag;

		/// <summary>The anchors a control stands for in a cell: Dock Top is Top, Left and Right (stretched along the top), Fill is all four (LayoutUtils.GetUnifiedAnchor).</summary>
		internal static AnchorStyles UnifiedAnchor(Control c)
		{
			switch (c.DockValue)
			{
			case DockStyle.Top:
				return AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			case DockStyle.Bottom:
				return AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			case DockStyle.Left:
				return AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom;
			case DockStyle.Right:
				return AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
			case DockStyle.Fill:
				return AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			default:
				return c.AnchorValue;
			}
		}

		/// <summary>One axis of a cell: both ends stretch, the far end aligns to the end, the near end to the start and neither centers; a control larger than the cell is cut to it.</summary>
		internal static void Align(int start, int extent, int size, bool toStart, bool toEnd, out int position, out int length)
		{
			extent = Math.Max(0, extent);
			if (toStart && toEnd)
			{
				position = start;
				length = extent;
				return;
			}
			length = Math.Min(Math.Max(0, size), extent);
			if (toEnd)
			{
				position = start + extent - length;
			}
			else if (toStart)
			{
				position = start;
			}
			else
			{
				position = start + (extent - length) / 2;
			}
		}

		/// <summary>The size the layout takes for a control: what an AutoSize control asks for (when it can measure itself), else the size the script gave it; within its minimum and maximum size.</summary>
		internal static Size ElementSize(Control c, Size proposal)
		{
			Size size = Size.Empty;
			bool measured = false;
			if (c.AutoSizeValue)
			{
				size = c.GetPreferredSize(proposal);
				measured = size != Size.Empty;
			}
			if (!measured)
			{
				size = c.SpecifiedSize;
			}
			c.ApplySizeConstraints(ref size);
			return size;
		}

		/// <summary>The children that take part in a layout: visible (their own flag) and not disposed, in the order of Controls.</summary>
		internal static Control[] Participants(Control container)
		{
			Control[] all = container.Children();
			int count = 0;
			foreach (Control c in all)
			{
				if (c.VisibleFlag && !c.IsDisposed)
				{
					count++;
				}
			}
			if (count == all.Length)
			{
				return all;
			}
			var result = new Control[count];
			int index = 0;
			foreach (Control c in all)
			{
				if (c.VisibleFlag && !c.IsDisposed)
				{
					result[index++] = c;
				}
			}
			return result;
		}

		/// <summary>The bounds with the size cut to the minimum and maximum size of the control (position and the far edge stay as the alignment gave them).</summary>
		internal static Rectangle Constrain(Control c, Rectangle r)
		{
			var size = new Size(Math.Max(0, r.Width), Math.Max(0, r.Height));
			c.ApplySizeConstraints(ref size);
			return new Rectangle(r.X, r.Y, size.Width, size.Height);
		}

		/// <summary>The container as the settings of the engine see it: Control only (a panel or any other control that names this engine).</summary>
		internal static Control AsControl(object child)
		{
			if (child == null)
			{
				throw new ArgumentNullException(nameof(child));
			}
			return child as Control ?? throw new ArgumentException("The object is not a control of this layout.", nameof(child));
		}
	}

	/// <summary>The engine of FlowLayoutPanel.</summary>
	internal sealed class FlowLayout : LayoutEngine
	{
		public static readonly FlowLayout Instance = new FlowLayout();

		private static readonly ConditionalWeakTable<Control, StrongBox<bool>> Breaks = new ConditionalWeakTable<Control, StrongBox<bool>>();

		private FlowLayout()
		{
		}

		internal static Control AsControl(object child) => CellLayout.AsControl(child);

		internal static bool GetFlowBreak(Control control)
		{
			if (control == null)
			{
				throw new ArgumentNullException(nameof(control));
			}
			return Breaks.TryGetValue(control, out StrongBox<bool> box) && box.Value;
		}

		internal static void SetFlowBreak(Control control, bool value)
		{
			if (control == null)
			{
				throw new ArgumentNullException(nameof(control));
			}
			if (GetFlowBreak(control) == value)
			{
				return;
			}
			Breaks.GetOrCreateValue(control).Value = value;
			control.ParentInternal?.PerformLayout(control, "FlowBreak");
		}

		private sealed class Row
		{
			public int Start;
			public int End;
			public int Main;
			public int Cross;
		}

		private sealed class Cell
		{
			public Control Control;
			public Size Size;
			public Padding Margin;
			public int Main;
			public int Cross;
		}

		private static FlowLayoutSettings SettingsOf(Control container) => (container as FlowLayoutPanel)?.Settings;

		public override bool Layout(object container, LayoutEventArgs layoutEventArgs)
		{
			var panel = (Control)container;
			if (panel.IsDisposed)
			{
				return false;
			}
			Arrange(panel, panel.DisplayRectangle, true, Size.Empty, out _);
			return panel.AutoSizeValue;
		}

		internal override bool TryGetPreferredSize(Control container, Size proposed, out Size size)
		{
			Arrange(container, Rectangle.Empty, false, proposed, out size);
			return true;
		}

		/// <summary>Works out the rows and, when <paramref name="apply"/>, moves the children; returns the size the children need.</summary>
		private static void Arrange(Control container, Rectangle display, bool apply, Size proposed, out Size needed)
		{
			FlowLayoutSettings settings = SettingsOf(container);
			FlowDirection direction = settings?.FlowDirection ?? FlowDirection.LeftToRight;
			bool wrap = settings?.WrapContents ?? true;
			bool horizontal = direction == FlowDirection.LeftToRight || direction == FlowDirection.RightToLeft;
			Control[] children = CellLayout.Participants(container);
			int limit = apply ? (horizontal ? display.Width : display.Height) : (horizontal ? proposed.Width : proposed.Height);
			if (limit <= 0 && !apply)
			{
				limit = int.MaxValue;
			}
			var cells = new Cell[children.Length];
			for (int i = 0; i < children.Length; i++)
			{
				Control c = children[i];
				Size size = CellLayout.ElementSize(c, Size.Empty);
				Padding margin = c.Margin;
				cells[i] = new Cell
				{
					Control = c,
					Size = size,
					Margin = margin,
					Main = horizontal ? size.Width + margin.Horizontal : size.Height + margin.Vertical,
					Cross = horizontal ? size.Height + margin.Vertical : size.Width + margin.Horizontal,
				};
			}
			// Rows: a control that is not the first of its row starts a new one when it does not fit, or when the control before it carries a flow break.
			var rows = new System.Collections.Generic.List<Row>();
			Row row = null;
			for (int i = 0; i < cells.Length; i++)
			{
				bool startsRow = row == null;
				if (!startsRow && wrap)
				{
					startsRow = (long)row.Main + cells[i].Main > limit || GetFlowBreak(cells[i - 1].Control);
				}
				if (startsRow)
				{
					row = new Row { Start = i };
					rows.Add(row);
				}
				row.End = i + 1;
				row.Main += cells[i].Main;
				row.Cross = Math.Max(row.Cross, cells[i].Cross);
			}
			if (!wrap && rows.Count == 1 && apply)
			{
				// One row for everything: it is as high as the container, so that Dock and Anchor have an extent to stretch in.
				rows[0].Cross = Math.Max(rows[0].Cross, horizontal ? display.Height : display.Width);
			}
			int mainNeeded = 0;
			int crossNeeded = 0;
			foreach (Row r in rows)
			{
				mainNeeded = Math.Max(mainNeeded, r.Main);
				crossNeeded += r.Cross;
			}
			needed = horizontal ? new Size(mainNeeded, crossNeeded) : new Size(crossNeeded, mainNeeded);
			if (!apply)
			{
				return;
			}
			var rects = new Rectangle[cells.Length];
			int crossPosition = 0;
			foreach (Row r in rows)
			{
				int mainPosition = 0;
				for (int i = r.Start; i < r.End; i++)
				{
					Cell cell = cells[i];
					AnchorStyles anchor = CellLayout.UnifiedAnchor(cell.Control);
					Padding m = cell.Margin;
					int lead = horizontal ? (direction == FlowDirection.RightToLeft ? m.Right : m.Left) : (direction == FlowDirection.BottomUp ? m.Bottom : m.Top);
					int main = mainPosition + lead;
					int mainLength = horizontal ? cell.Size.Width : cell.Size.Height;
					int crossSize = horizontal ? cell.Size.Height : cell.Size.Width;
					int crossStart = crossPosition + (horizontal ? m.Top : m.Left);
					int crossExtent = r.Cross - (horizontal ? m.Vertical : m.Horizontal);
					CellLayout.Align(crossStart, crossExtent, crossSize,
						CellLayout.Has(anchor, horizontal ? AnchorStyles.Top : AnchorStyles.Left),
						CellLayout.Has(anchor, horizontal ? AnchorStyles.Bottom : AnchorStyles.Right),
						out int cross, out int crossLength);
					Rectangle rect;
					switch (direction)
					{
					case FlowDirection.RightToLeft:
						rect = new Rectangle(display.Right - main - mainLength, display.Y + cross, mainLength, crossLength);
						break;
					case FlowDirection.TopDown:
						rect = new Rectangle(display.X + cross, display.Y + main, crossLength, mainLength);
						break;
					case FlowDirection.BottomUp:
						rect = new Rectangle(display.X + cross, display.Bottom - main - mainLength, crossLength, mainLength);
						break;
					default:
						rect = new Rectangle(display.X + main, display.Y + cross, mainLength, crossLength);
						break;
					}
					rects[i] = CellLayout.Constrain(cell.Control, rect);
					mainPosition += cell.Main;
				}
				crossPosition += r.Cross;
			}
			for (int i = 0; i < cells.Length; i++)
			{
				if (cells[i].Control.Bounds != rects[i])
				{
					cells[i].Control.SetBoundsByLayout(rects[i]);
				}
			}
		}
	}
}
