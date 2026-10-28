// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// TableLayoutPanel (P4.9c): the table layout engine of System.Windows.Forms (System.Windows.Forms.Layout.TableLayout of dotnet/winforms, MIT), reduced to what a script can see:
// every child sits in a cell (Column, Row, ColumnSpan, RowSpan; unset cells are filled in the order of Controls and by GrowStyle), the columns and rows are sized by their styles
// (Absolute: the given pixels; AutoSize: the largest control of the column; Percent: a share of what is left), and a control is placed in its cell by its anchors or dock.
// The table keeps the numbers of the cells as properties of the child (as WinForms does), so a control that moves to another table takes them along.
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms
{
	/// <summary>One row or column of a table: how it is sized (SizeType) and by how much.</summary>
	public abstract partial class TableLayoutStyle
	{
		private SizeType sizeType;
		private float size;
		internal TableLayoutSettings Owner;

		protected TableLayoutStyle()
		{
		}

		public SizeType SizeType
		{
			get => sizeType;
			set
			{
				if (value < SizeType.AutoSize || value > SizeType.Percent)
				{
					throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(SizeType));
				}
				if (sizeType != value)
				{
					sizeType = value;
					Changed();
				}
			}
		}

		internal float SizeValue
		{
			get => size;
			set
			{
				if (value < 0)
				{
					throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be greater than or equal to 0.");
				}
				if (size != value)
				{
					size = value;
					Changed();
				}
			}
		}

		internal void Changed() => Owner?.OwnerPanel?.PerformLayout(Owner.OwnerPanel, "Style");
	}

	public partial class ColumnStyle : TableLayoutStyle
	{
		public ColumnStyle()
		{
		}

		public ColumnStyle(SizeType sizeType)
		{
			SizeType = sizeType;
		}

		public ColumnStyle(SizeType sizeType, float width)
		{
			SizeType = sizeType;
			SizeValue = width;
		}

		public float Width
		{
			get => SizeValue;
			set => SizeValue = value;
		}
	}

	public partial class RowStyle : TableLayoutStyle
	{
		public RowStyle()
		{
		}

		public RowStyle(SizeType sizeType)
		{
			SizeType = sizeType;
		}

		public RowStyle(SizeType sizeType, float height)
		{
			SizeType = sizeType;
			SizeValue = height;
		}

		public float Height
		{
			get => SizeValue;
			set => SizeValue = value;
		}
	}

	/// <summary>The styles of the rows or of the columns (an IList of <see cref="TableLayoutStyle"/>); every change lays the table out again.</summary>
	public abstract partial class TableLayoutStyleCollection : IList
	{
		private readonly TableLayoutSettings owner;
		private readonly List<TableLayoutStyle> styles = new List<TableLayoutStyle>();

		internal TableLayoutStyleCollection(TableLayoutSettings owner)
		{
			this.owner = owner;
		}

		internal IReadOnlyList<TableLayoutStyle> Styles => styles;

		private void EnsureNotOwned(TableLayoutStyle style)
		{
			if (style == null)
			{
				throw new ArgumentNullException(nameof(style));
			}
			if (style.Owner != null)
			{
				throw new ArgumentException("The style already belongs to a TableLayoutPanel; create a new one.", nameof(style));
			}
		}

		private void Changed() => owner?.OwnerPanel?.PerformLayout(owner.OwnerPanel, "Style");

		private void Adopt(TableLayoutStyle style)
		{
			style.Owner = owner;
		}

		private static void Release(TableLayoutStyle style)
		{
			style.Owner = null;
		}

		public int Count => styles.Count;

		public TableLayoutStyle this[int index]
		{
			get => styles[index];
			set
			{
				if (value == null)
				{
					throw new ArgumentNullException(nameof(value));
				}
				TableLayoutStyle old = styles[index];
				if (!ReferenceEquals(old, value))
				{
					EnsureNotOwned(value);
					Release(old);
					Adopt(value);
					styles[index] = value;
				}
				Changed();
			}
		}

		public int Add(TableLayoutStyle style)
		{
			EnsureNotOwned(style);
			Adopt(style);
			styles.Add(style);
			Changed();
			return styles.Count - 1;
		}

		public void Clear()
		{
			foreach (TableLayoutStyle style in styles)
			{
				Release(style);
			}
			styles.Clear();
			Changed();
		}

		public void RemoveAt(int index)
		{
			TableLayoutStyle style = styles[index];
			styles.RemoveAt(index);
			Release(style);
			Changed();
		}

		internal void InsertStyle(int index, TableLayoutStyle style)
		{
			EnsureNotOwned(style);
			Adopt(style);
			styles.Insert(index, style);
			Changed();
		}

		internal void RemoveStyle(TableLayoutStyle style)
		{
			int index = styles.IndexOf(style);
			if (index >= 0)
			{
				RemoveAt(index);
			}
		}

		internal bool ContainsStyle(TableLayoutStyle style) => styles.Contains(style);

		internal int IndexOfStyle(TableLayoutStyle style) => styles.IndexOf(style);

		void ICollection.CopyTo(Array array, int index) => ((ICollection)styles).CopyTo(array, index);

		bool ICollection.IsSynchronized => false;

		object ICollection.SyncRoot => this;

		IEnumerator IEnumerable.GetEnumerator() => styles.GetEnumerator();

		int IList.Add(object value) => Add((TableLayoutStyle)value);

		bool IList.Contains(object value) => value is TableLayoutStyle s && styles.Contains(s);

		int IList.IndexOf(object value) => value is TableLayoutStyle s ? styles.IndexOf(s) : -1;

		void IList.Insert(int index, object value) => InsertStyle(index, (TableLayoutStyle)value);

		bool IList.IsFixedSize => false;

		bool IList.IsReadOnly => false;

		void IList.Remove(object value)
		{
			if (value is TableLayoutStyle s)
			{
				RemoveStyle(s);
			}
		}

		object IList.this[int index]
		{
			get => this[index];
			set => this[index] = (TableLayoutStyle)value;
		}
	}

	public partial class TableLayoutColumnStyleCollection : TableLayoutStyleCollection
	{
		internal TableLayoutColumnStyleCollection(TableLayoutSettings owner)
			: base(owner)
		{
		}

		public new ColumnStyle this[int index]
		{
			get => (ColumnStyle)base[index];
			set => base[index] = value;
		}

		public int Add(ColumnStyle columnStyle) => base.Add(columnStyle);

		public bool Contains(ColumnStyle columnStyle) => ContainsStyle(columnStyle);

		public int IndexOf(ColumnStyle columnStyle) => IndexOfStyle(columnStyle);

		public void Insert(int index, ColumnStyle columnStyle) => InsertStyle(index, columnStyle);

		public void Remove(ColumnStyle columnStyle) => RemoveStyle(columnStyle);
	}

	public partial class TableLayoutRowStyleCollection : TableLayoutStyleCollection
	{
		internal TableLayoutRowStyleCollection(TableLayoutSettings owner)
			: base(owner)
		{
		}

		public new RowStyle this[int index]
		{
			get => (RowStyle)base[index];
			set => base[index] = value;
		}

		public int Add(RowStyle rowStyle) => base.Add(rowStyle);

		public bool Contains(RowStyle rowStyle) => ContainsStyle(rowStyle);

		public int IndexOf(RowStyle rowStyle) => IndexOfStyle(rowStyle);

		public void Insert(int index, RowStyle rowStyle) => InsertStyle(index, rowStyle);

		public void Remove(RowStyle rowStyle) => RemoveStyle(rowStyle);
	}

	/// <summary>The position of a cell: column and row, -1 for "not set".</summary>
	public partial struct TableLayoutPanelCellPosition : IEquatable<TableLayoutPanelCellPosition>
	{
		private int row;
		private int column;

		public TableLayoutPanelCellPosition(int column, int row)
		{
			if (row < -1)
			{
				throw new ArgumentOutOfRangeException(nameof(row), row, "The value must be greater than or equal to -1.");
			}
			if (column < -1)
			{
				throw new ArgumentOutOfRangeException(nameof(column), column, "The value must be greater than or equal to -1.");
			}
			this.row = row;
			this.column = column;
		}

		public int Row
		{
			get => row;
			set => row = value;
		}

		public int Column
		{
			get => column;
			set => column = value;
		}

		public override bool Equals(object other) => other is TableLayoutPanelCellPosition p && Equals(p);

		public bool Equals(TableLayoutPanelCellPosition other) => other.row == row && other.column == column;

		public static bool operator ==(TableLayoutPanelCellPosition p1, TableLayoutPanelCellPosition p2) => p1.Equals(p2);

		public static bool operator !=(TableLayoutPanelCellPosition p1, TableLayoutPanelCellPosition p2) => !p1.Equals(p2);

		public override string ToString() => Column + "," + Row;

		public override int GetHashCode() => HashCode.Combine(column, row);
	}

	public partial class TableLayoutCellPaintEventArgs : PaintEventArgs
	{
		public TableLayoutCellPaintEventArgs(Graphics g, Rectangle clipRectangle, Rectangle cellBounds, int column, int row)
			: base(g, clipRectangle)
		{
			CellBounds = cellBounds;
			Column = column;
			Row = row;
		}

		public Rectangle CellBounds { get; }

		public int Column { get; }

		public int Row { get; }
	}

	/// <summary>The counts, styles and growth of a TableLayoutPanel, and the cells of its children.</summary>
	public sealed partial class TableLayoutSettings : LayoutSettings
	{
		private readonly TableLayoutPanel owner;
		private readonly TableLayoutColumnStyleCollection columnStyles;
		private readonly TableLayoutRowStyleCollection rowStyles;
		private int columnCount;
		private int rowCount;
		private TableLayoutPanelGrowStyle growStyle = TableLayoutPanelGrowStyle.AddRows;

		internal TableLayoutSettings(TableLayoutPanel owner)
		{
			this.owner = owner;
			columnStyles = new TableLayoutColumnStyleCollection(this);
			rowStyles = new TableLayoutRowStyleCollection(this);
		}

		internal TableLayoutPanel OwnerPanel => owner;

		public override LayoutEngine LayoutEngine => TableLayout.Instance;

		public int ColumnCount
		{
			get => columnCount;
			set
			{
				if (value < 0)
				{
					throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be greater than or equal to 0.");
				}
				if (columnCount != value)
				{
					columnCount = value;
					owner?.PerformLayout(owner, "ColumnCount");
				}
			}
		}

		public int RowCount
		{
			get => rowCount;
			set
			{
				if (value < 0)
				{
					throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be greater than or equal to 0.");
				}
				if (rowCount != value)
				{
					rowCount = value;
					owner?.PerformLayout(owner, "RowCount");
				}
			}
		}

		public TableLayoutPanelGrowStyle GrowStyle
		{
			get => growStyle;
			set
			{
				if (value < TableLayoutPanelGrowStyle.FixedSize || value > TableLayoutPanelGrowStyle.AddColumns)
				{
					throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(TableLayoutPanelGrowStyle));
				}
				if (growStyle != value)
				{
					growStyle = value;
					owner?.PerformLayout(owner, "GrowStyle");
				}
			}
		}

		public TableLayoutColumnStyleCollection ColumnStyles => columnStyles;

		public TableLayoutRowStyleCollection RowStyles => rowStyles;

		/// <summary>The layout found that the table needs more columns and rows than the properties say: the counts follow (they never shrink by themselves).</summary>
		internal void GrowTo(int columns, int rows)
		{
			columnCount = Math.Max(columnCount, columns);
			rowCount = Math.Max(rowCount, rows);
		}

		public TableLayoutPanelCellPosition GetCellPosition(object control)
		{
			TableLayout.Cell cell = TableLayout.Read(CellLayout.AsControl(control));
			return new TableLayoutPanelCellPosition(cell.Column, cell.Row);
		}

		public void SetCellPosition(object control, TableLayoutPanelCellPosition cellPosition)
		{
			Control c = CellLayout.AsControl(control);
			if (cellPosition.Column < -1 || cellPosition.Row < -1)
			{
				throw new ArgumentOutOfRangeException(nameof(cellPosition), cellPosition, "The value must be greater than or equal to -1.");
			}
			TableLayout.Cell cell = TableLayout.Write(c);
			cell.Column = cellPosition.Column;
			cell.Row = cellPosition.Row;
			c.ParentInternal?.PerformLayout(c, "TableIndex");
		}

		public int GetColumn(object control) => TableLayout.Read(CellLayout.AsControl(control)).Column;

		public void SetColumn(object control, int column)
		{
			Control c = CellLayout.AsControl(control);
			if (column < -1)
			{
				throw new ArgumentOutOfRangeException(nameof(column), column, "The value must be greater than or equal to -1.");
			}
			TableLayout.Write(c).Column = column;
			c.ParentInternal?.PerformLayout(c, "TableIndex");
		}

		public int GetRow(object control) => TableLayout.Read(CellLayout.AsControl(control)).Row;

		public void SetRow(object control, int row)
		{
			Control c = CellLayout.AsControl(control);
			if (row < -1)
			{
				throw new ArgumentOutOfRangeException(nameof(row), row, "The value must be greater than or equal to -1.");
			}
			TableLayout.Write(c).Row = row;
			c.ParentInternal?.PerformLayout(c, "TableIndex");
		}

		public int GetColumnSpan(object control) => TableLayout.Read(CellLayout.AsControl(control)).ColumnSpan;

		public void SetColumnSpan(object control, int value)
		{
			Control c = CellLayout.AsControl(control);
			if (value < 1)
			{
				throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be greater than or equal to 1.");
			}
			TableLayout.Write(c).ColumnSpan = value;
			c.ParentInternal?.PerformLayout(c, "ColumnSpan");
		}

		public int GetRowSpan(object control) => TableLayout.Read(CellLayout.AsControl(control)).RowSpan;

		public void SetRowSpan(object control, int value)
		{
			Control c = CellLayout.AsControl(control);
			if (value < 1)
			{
				throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be greater than or equal to 1.");
			}
			TableLayout.Write(c).RowSpan = value;
			c.ParentInternal?.PerformLayout(c, "RowSpan");
		}
	}

	/// <summary>The children of a table panel: <c>Add(control, column, row)</c> puts a control into a cell.</summary>
	public partial class TableLayoutControlCollection : Control.ControlCollection
	{
		private readonly TableLayoutPanel container;

		public TableLayoutControlCollection(TableLayoutPanel container)
			: base(container)
		{
			this.container = container;
		}

		public TableLayoutPanel Container => container;

		public virtual void Add(Control control, int column, int row)
		{
			if (control == null)
			{
				throw new ArgumentNullException(nameof(control));
			}
			if (column < -1 || row < -1)
			{
				throw new ArgumentOutOfRangeException(column < -1 ? nameof(column) : nameof(row), "The value must be greater than or equal to -1.");
			}
			// The cell is set before the control is added, so that the table is laid out once with the right cell.
			TableLayout.Cell cell = TableLayout.Write(control);
			cell.Column = column;
			cell.Row = row;
			Add(control);
		}
	}

	/// <summary>A panel that arranges its children in a grid of cells.</summary>
	public partial class TableLayoutPanel : Panel, System.ComponentModel.IExtenderProvider
	{
		private TableLayoutSettings settings;
		private TableLayoutPanelCellBorderStyle cellBorderStyle;
		private TableLayoutCellPaintEventHandler cellPaint;

		public TableLayoutPanel()
		{
		}

		internal TableLayoutSettings Settings => settings ?? (settings = new TableLayoutSettings(this));

		public override LayoutEngine LayoutEngine => TableLayout.Instance;

		protected override ControlCollection CreateControlsInstance() => new TableLayoutControlCollection(this);

		public new TableLayoutControlCollection Controls => (TableLayoutControlCollection)base.Controls;

		public TableLayoutSettings LayoutSettings
		{
			get => Settings;
			set
			{
				if (value != null && ReferenceEquals(value, settings))
				{
					return;
				}
				throw new NotSupportedException("Only the settings a TableLayoutPanel gave out can be assigned.");
			}
		}

		public int ColumnCount
		{
			get => Settings.ColumnCount;
			set => Settings.ColumnCount = value;
		}

		public int RowCount
		{
			get => Settings.RowCount;
			set => Settings.RowCount = value;
		}

		public TableLayoutPanelGrowStyle GrowStyle
		{
			get => Settings.GrowStyle;
			set => Settings.GrowStyle = value;
		}

		public TableLayoutColumnStyleCollection ColumnStyles => Settings.ColumnStyles;

		public TableLayoutRowStyleCollection RowStyles => Settings.RowStyles;

		public TableLayoutPanelCellBorderStyle CellBorderStyle
		{
			get => cellBorderStyle;
			set
			{
				if (value < TableLayoutPanelCellBorderStyle.None || value > TableLayoutPanelCellBorderStyle.OutsetPartial)
				{
					throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(TableLayoutPanelCellBorderStyle));
				}
				if (cellBorderStyle != value)
				{
					cellBorderStyle = value;
					Notify("CellBorderStyle");
					PerformLayout(this, "CellBorderStyle");
					Invalidate();
				}
			}
		}

		/// <summary>The width of the lines between and around the cells.</summary>
		internal int CellBorderWidth
		{
			get
			{
				switch (cellBorderStyle)
				{
				case TableLayoutPanelCellBorderStyle.Single:
					return 1;
				case TableLayoutPanelCellBorderStyle.Inset:
				case TableLayoutPanelCellBorderStyle.Outset:
					return 2;
				case TableLayoutPanelCellBorderStyle.InsetDouble:
				case TableLayoutPanelCellBorderStyle.OutsetDouble:
				case TableLayoutPanelCellBorderStyle.OutsetPartial:
					return 3;
				default:
					return 0;
				}
			}
		}

		public event TableLayoutCellPaintEventHandler CellPaint
		{
			add => cellPaint += value;
			remove => cellPaint -= value;
		}

		protected virtual void OnCellPaint(TableLayoutCellPaintEventArgs e)
		{
			cellPaint?.Invoke(this, e);
		}

		public TableLayoutPanelCellPosition GetCellPosition(Control control) => Settings.GetCellPosition(control);

		public void SetCellPosition(Control control, TableLayoutPanelCellPosition position) => Settings.SetCellPosition(control, position);

		public int GetColumn(Control control) => Settings.GetColumn(control);

		public void SetColumn(Control control, int column) => Settings.SetColumn(control, column);

		public int GetRow(Control control) => Settings.GetRow(control);

		public void SetRow(Control control, int row) => Settings.SetRow(control, row);

		public int GetColumnSpan(Control control) => Settings.GetColumnSpan(control);

		public void SetColumnSpan(Control control, int value) => Settings.SetColumnSpan(control, value);

		public int GetRowSpan(Control control) => Settings.GetRowSpan(control);

		public void SetRowSpan(Control control, int value) => Settings.SetRowSpan(control, value);

		/// <summary>The cell a control really sits in (the cell given with SetCellPosition, or the one the table found for it); (-1, -1) for a control that is not laid out here.</summary>
		public TableLayoutPanelCellPosition GetPositionFromControl(Control control)
		{
			TableLayout.Plan plan = TableLayout.Compute(this, DisplayRectangle, false, Size.Empty);
			foreach (TableLayout.Placed p in plan.Items)
			{
				if (ReferenceEquals(p.Control, control))
				{
					return new TableLayoutPanelCellPosition(p.Column, p.Row);
				}
			}
			return new TableLayoutPanelCellPosition(-1, -1);
		}

		/// <summary>The control that covers a cell (a spanning control covers every cell it spans); null for an empty cell.</summary>
		public Control GetControlFromPosition(int column, int row)
		{
			TableLayout.Plan plan = TableLayout.Compute(this, DisplayRectangle, false, Size.Empty);
			foreach (TableLayout.Placed p in plan.Items)
			{
				if (column >= p.Column && column < p.Column + p.ColumnSpan && row >= p.Row && row < p.Row + p.RowSpan)
				{
					return p.Control;
				}
			}
			return null;
		}

		/// <summary>The widths of the columns as the last layout gave them (without the lines between the cells).</summary>
		public int[] GetColumnWidths() => (int[])TableLayout.Compute(this, DisplayRectangle, true, Size.Empty).ColumnWidths.Clone();

		/// <summary>The heights of the rows as the last layout gave them (without the lines between the cells).</summary>
		public int[] GetRowHeights() => (int[])TableLayout.Compute(this, DisplayRectangle, true, Size.Empty).RowHeights.Clone();

		bool System.ComponentModel.IExtenderProvider.CanExtend(object extendee) => extendee is Control c && c.ParentInternal == this;
	}
}

namespace System.Windows.Forms.Layout
{
	/// <summary>The engine of TableLayoutPanel.</summary>
	internal sealed class TableLayout : LayoutEngine
	{
		public static readonly TableLayout Instance = new TableLayout();

		/// <summary>The cell numbers a control carries (the table attached properties of WinForms).</summary>
		internal sealed class Cell
		{
			public int Column = -1;
			public int Row = -1;
			public int ColumnSpan = 1;
			public int RowSpan = 1;
		}

		private static readonly ConditionalWeakTable<Control, Cell> Cells = new ConditionalWeakTable<Control, Cell>();

		private static readonly Cell Default = new Cell();

		private TableLayout()
		{
		}

		internal static Cell Read(Control control) => Cells.TryGetValue(control, out Cell cell) ? cell : Default;

		internal static Cell Write(Control control)
		{
			if (control == null)
			{
				throw new ArgumentNullException(nameof(control));
			}
			return Cells.GetOrCreateValue(control);
		}

		/// <summary>A child with its cell, its place after the table assigned the free cells, and the bounds of that cell.</summary>
		internal sealed class Placed
		{
			public Control Control;
			public int Column;
			public int Row;
			public int ColumnSpan;
			public int RowSpan;
			public Rectangle CellBounds;
		}

		internal sealed class Plan
		{
			public int Columns;
			public int Rows;
			public int[] ColumnWidths;
			public int[] RowHeights;
			public int Border;
			public List<Placed> Items = new List<Placed>();
			public Size Needed;
		}

		public override bool Layout(object container, LayoutEventArgs layoutEventArgs)
		{
			var panel = (TableLayoutPanel)container;
			if (panel.IsDisposed)
			{
				return false;
			}
			Plan plan = Compute(panel, panel.DisplayRectangle, true, Size.Empty);
			panel.Settings.GrowTo(plan.Columns, plan.Rows);
			Rectangle display = panel.DisplayRectangle;
			var rects = new Rectangle[plan.Items.Count];
			for (int i = 0; i < rects.Length; i++)
			{
				Placed p = plan.Items[i];
				Control c = p.Control;
				Padding m = c.Margin;
				Rectangle content = new Rectangle(p.CellBounds.X + m.Left, p.CellBounds.Y + m.Top, Math.Max(0, p.CellBounds.Width - m.Horizontal), Math.Max(0, p.CellBounds.Height - m.Vertical));
				Size size = CellLayout.ElementSize(c, new Size(content.Width, 0));
				AnchorStyles anchor = CellLayout.UnifiedAnchor(c);
				CellLayout.Align(content.X, content.Width, size.Width, CellLayout.Has(anchor, AnchorStyles.Left), CellLayout.Has(anchor, AnchorStyles.Right), out int x, out int width);
				CellLayout.Align(content.Y, content.Height, size.Height, CellLayout.Has(anchor, AnchorStyles.Top), CellLayout.Has(anchor, AnchorStyles.Bottom), out int y, out int height);
				rects[i] = CellLayout.Constrain(c, new Rectangle(x, y, width, height));
			}
			for (int i = 0; i < rects.Length; i++)
			{
				Control c = plan.Items[i].Control;
				if (c.Bounds != rects[i])
				{
					c.SetBoundsByLayout(rects[i]);
				}
			}
			return panel.AutoSizeValue;
		}

		internal override bool TryGetPreferredSize(Control container, Size proposed, out Size size)
		{
			if (container is TableLayoutPanel panel)
			{
				size = Compute(panel, Rectangle.Empty, false, proposed).Needed;
				return true;
			}
			size = Size.Empty;
			return false;
		}

		private static float StyleSize(IReadOnlyList<TableLayoutStyle> styles, int index, out SizeType type)
		{
			if (index < styles.Count)
			{
				type = styles[index].SizeType;
				return styles[index].SizeValue;
			}
			type = SizeType.AutoSize;
			return 0;
		}

		/// <summary>Assigns the cells, sizes the columns and rows and gives every visible child the bounds of its cell. With <paramref name="layout"/> false the table is measured: percent styles count as auto-size.</summary>
		internal static Plan Compute(TableLayoutPanel panel, Rectangle display, bool layout, Size proposed)
		{
			TableLayoutSettings settings = panel.Settings;
			var plan = new Plan { Border = panel.CellBorderWidth };
			Control[] children = CellLayout.Participants(panel);
			Assign(plan, settings, children);
			int border = plan.Border;
			int columns = plan.Columns;
			int rows = plan.Rows;
			plan.ColumnWidths = new int[columns];
			plan.RowHeights = new int[rows];
			// Columns.
			var columnTypes = new SizeType[columns];
			var columnSizes = new float[columns];
			for (int j = 0; j < columns; j++)
			{
				columnSizes[j] = StyleSize(settings.ColumnStyles.Styles, j, out columnTypes[j]);
			}
			var prefWidths = new Dictionary<Placed, int>();
			foreach (Placed p in plan.Items)
			{
				prefWidths[p] = CellLayout.ElementSize(p.Control, Size.Empty).Width + p.Control.Margin.Horizontal;
			}
			SizeAxis(plan.ColumnWidths, columnTypes, columnSizes, plan.Items, p => p.Column, p => p.ColumnSpan, p => prefWidths[p], border, layout ? display.Width : 0, layout);
			// Rows: the height of an auto-size control may depend on the width of its cell.
			foreach (Placed p in plan.Items)
			{
				p.CellBounds = new Rectangle(0, 0, SpanSize(plan.ColumnWidths, p.Column, p.ColumnSpan, border), 0);
			}
			var rowTypes = new SizeType[rows];
			var rowSizes = new float[rows];
			for (int i = 0; i < rows; i++)
			{
				rowSizes[i] = StyleSize(settings.RowStyles.Styles, i, out rowTypes[i]);
			}
			var prefHeights = new Dictionary<Placed, int>();
			foreach (Placed p in plan.Items)
			{
				Padding m = p.Control.Margin;
				prefHeights[p] = CellLayout.ElementSize(p.Control, new Size(Math.Max(0, p.CellBounds.Width - m.Horizontal), 0)).Height + m.Vertical;
			}
			SizeAxis(plan.RowHeights, rowTypes, rowSizes, plan.Items, p => p.Row, p => p.RowSpan, p => prefHeights[p], border, layout ? display.Height : 0, layout);
			// Positions.
			var columnStarts = Starts(plan.ColumnWidths, border, display.X);
			var rowStarts = Starts(plan.RowHeights, border, display.Y);
			foreach (Placed p in plan.Items)
			{
				p.CellBounds = new Rectangle(columnStarts[p.Column], rowStarts[p.Row], SpanSize(plan.ColumnWidths, p.Column, p.ColumnSpan, border), SpanSize(plan.RowHeights, p.Row, p.RowSpan, border));
			}
			plan.Needed = new Size(Total(plan.ColumnWidths, border), Total(plan.RowHeights, border));
			return plan;
		}

		private static int Total(int[] sizes, int border)
		{
			if (sizes.Length == 0)
			{
				return 0;
			}
			int total = border;
			foreach (int s in sizes)
			{
				total += s + border;
			}
			return total;
		}

		private static int[] Starts(int[] sizes, int border, int origin)
		{
			var starts = new int[sizes.Length];
			int at = origin + border;
			for (int i = 0; i < sizes.Length; i++)
			{
				starts[i] = at;
				at += sizes[i] + border;
			}
			return starts;
		}

		/// <summary>The extent of the cells [start, start + span): their sizes and the lines between them.</summary>
		private static int SpanSize(int[] sizes, int start, int span, int border)
		{
			int total = 0;
			int end = Math.Min(sizes.Length, start + span);
			for (int i = start; i < end; i++)
			{
				total += sizes[i];
			}
			return total + Math.Max(0, end - start - 1) * border;
		}

		/// <summary>
		/// Sizes the columns (or rows): absolute ones by their value, auto-size ones by the largest single-cell control, then spanning controls make auto-size columns
		/// they cover wider when they need more (narrow spans first, the missing pixels spread over the auto-size columns, the rest to the last), then percent
		/// ones share what is left (the integer part of their share, the last percent column takes the rest). Measuring counts percent columns as auto-size.
		/// </summary>
		private static void SizeAxis(int[] sizes, SizeType[] types, float[] values, List<Placed> items, Func<Placed, int> start, Func<Placed, int> span, Func<Placed, int> preferred, int border, int available, bool layout)
		{
			int count = sizes.Length;
			bool Fixed(int i) => types[i] == SizeType.Absolute;
			bool Content(int i) => types[i] == SizeType.AutoSize || (!layout && types[i] == SizeType.Percent);
			for (int i = 0; i < count; i++)
			{
				sizes[i] = Fixed(i) ? (int)values[i] : 0;
			}
			foreach (Placed p in items)
			{
				if (span(p) == 1 && Content(start(p)))
				{
					sizes[start(p)] = Math.Max(sizes[start(p)], preferred(p));
				}
			}
			var spanning = new List<Placed>();
			foreach (Placed p in items)
			{
				if (span(p) > 1)
				{
					spanning.Add(p);
				}
			}
			spanning.Sort((a, b) => span(a).CompareTo(span(b)));
			foreach (Placed p in spanning)
			{
				int first = start(p);
				int last = Math.Min(count, first + span(p));
				int deficit = preferred(p) - SpanSize(sizes, first, last - first, border);
				if (deficit <= 0)
				{
					continue;
				}
				var targets = new List<int>();
				for (int i = first; i < last; i++)
				{
					if (Content(i))
					{
						targets.Add(i);
					}
				}
				if (targets.Count == 0)
				{
					continue;
				}
				int share = deficit / targets.Count;
				foreach (int i in targets)
				{
					sizes[i] += share;
				}
				sizes[targets[targets.Count - 1]] += deficit - share * targets.Count;
			}
			if (!layout)
			{
				return;
			}
			float totalPercent = 0;
			int used = count == 0 ? 0 : border * (count + 1);
			for (int i = 0; i < count; i++)
			{
				if (types[i] == SizeType.Percent)
				{
					totalPercent += values[i];
				}
				else
				{
					used += sizes[i];
				}
			}
			int remaining = Math.Max(0, available - used);
			int lastPercent = -1;
			for (int i = 0; i < count; i++)
			{
				if (types[i] == SizeType.Percent)
				{
					lastPercent = i;
				}
			}
			int given = 0;
			for (int i = 0; i < count; i++)
			{
				if (types[i] != SizeType.Percent)
				{
					continue;
				}
				if (totalPercent <= 0)
				{
					sizes[i] = 0;
				}
				else if (i == lastPercent)
				{
					sizes[i] = Math.Max(0, remaining - given);
				}
				else
				{
					sizes[i] = (int)(remaining * (double)values[i] / totalPercent);
					given += sizes[i];
				}
			}
		}

		/// <summary>
		/// Gives every visible child its cell. Children with a column and a row take them; the others are placed in the order of Controls in the first free cells
		/// (row by row for AddRows and FixedSize, column by column for AddColumns); the table grows as GrowStyle says, and a FixedSize table that is full throws.
		/// </summary>
		private static void Assign(Plan plan, TableLayoutSettings settings, Control[] children)
		{
			int declaredColumns = settings.ColumnCount;
			int declaredRows = settings.RowCount;
			TableLayoutPanelGrowStyle grow = settings.GrowStyle;
			var occupied = new HashSet<(int, int)>();
			var placed = new Placed[children.Length];
			bool Free(int column, int row, int cs, int rs)
			{
				for (int r = row; r < row + rs; r++)
				{
					for (int c = column; c < column + cs; c++)
					{
						if (occupied.Contains((c, r)))
						{
							return false;
						}
					}
				}
				return true;
			}
			void Take(Placed p)
			{
				for (int r = p.Row; r < p.Row + p.RowSpan; r++)
				{
					for (int c = p.Column; c < p.Column + p.ColumnSpan; c++)
					{
						occupied.Add((c, r));
					}
				}
			}
			void CheckFits(Placed p)
			{
				if (grow == TableLayoutPanelGrowStyle.FixedSize && (p.Column + p.ColumnSpan > declaredColumns || p.Row + p.RowSpan > declaredRows))
				{
					throw new ArgumentException("TableLayoutPanel cannot hold the control: the table is full and its GrowStyle is FixedSize.");
				}
			}
			for (int i = 0; i < children.Length; i++)
			{
				Cell cell = Read(children[i]);
				var p = new Placed { Control = children[i], Column = cell.Column, Row = cell.Row, ColumnSpan = Math.Max(1, cell.ColumnSpan), RowSpan = Math.Max(1, cell.RowSpan) };
				placed[i] = p;
				if (p.Column >= 0 && p.Row >= 0)
				{
					CheckFits(p);
					Take(p);
				}
			}
			int cursorColumn = 0;
			int cursorRow = 0;
			int columnLimit = grow == TableLayoutPanelGrowStyle.AddRows ? Math.Max(declaredColumns, 1) : grow == TableLayoutPanelGrowStyle.FixedSize ? declaredColumns : int.MaxValue;
			int rowLimit = grow == TableLayoutPanelGrowStyle.AddColumns ? Math.Max(declaredRows, 1) : grow == TableLayoutPanelGrowStyle.FixedSize ? declaredRows : int.MaxValue;
			foreach (Placed p in placed)
			{
				if (p.Column >= 0 && p.Row >= 0)
				{
					continue;
				}
				if (p.Column >= 0)
				{
					// Only the column is known: the first free row of it.
					int row = 0;
					while (!Free(p.Column, row, p.ColumnSpan, p.RowSpan))
					{
						row++;
					}
					p.Row = row;
				}
				else if (p.Row >= 0)
				{
					int column = 0;
					while (!Free(column, p.Row, p.ColumnSpan, p.RowSpan))
					{
						column++;
					}
					p.Column = column;
				}
				else if (grow == TableLayoutPanelGrowStyle.AddColumns)
				{
					int limit = Math.Max(rowLimit, p.RowSpan);
					int column = cursorColumn;
					int row = cursorRow;
					while (row + p.RowSpan > limit || !Free(column, row, p.ColumnSpan, p.RowSpan))
					{
						row++;
						if (row + p.RowSpan > limit)
						{
							row = 0;
							column++;
						}
					}
					p.Column = column;
					p.Row = row;
					cursorColumn = column;
					cursorRow = row + p.RowSpan;
				}
				else
				{
					int limit = Math.Max(columnLimit, p.ColumnSpan);
					int column = cursorColumn;
					int row = cursorRow;
					while (column + p.ColumnSpan > limit || !Free(column, row, p.ColumnSpan, p.RowSpan))
					{
						column++;
						if (column + p.ColumnSpan > limit)
						{
							column = 0;
							row++;
						}
						if (grow == TableLayoutPanelGrowStyle.FixedSize && row + p.RowSpan > rowLimit)
						{
							break;
						}
					}
					p.Column = column;
					p.Row = row;
					cursorColumn = column + p.ColumnSpan;
					cursorRow = row;
				}
				CheckFits(p);
				Take(p);
			}
			int columns = declaredColumns;
			int rows = declaredRows;
			foreach (Placed p in placed)
			{
				columns = Math.Max(columns, p.Column + p.ColumnSpan);
				rows = Math.Max(rows, p.Row + p.RowSpan);
				plan.Items.Add(p);
			}
			plan.Columns = columns;
			plan.Rows = rows;
		}
	}
}
