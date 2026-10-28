using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Layout;
using cYo.Compat.Forms;
using Xunit;

namespace Compat.Forms.Tests
{
	/// <summary>
	/// TableLayoutPanel (P4.9c). The expected rectangles are worked out by hand from the table algorithm of System.Windows.Forms.Layout.TableLayout: a column or row is
	/// Absolute (the pixels given), AutoSize (the largest control of it, margins included) or Percent (a share of what Absolute and AutoSize leave); a control sits in its
	/// cell (minus its Margin) at the place its anchors say. A Button is 75 x 23 with Margin 3, so it needs 81 x 29.
	/// </summary>
	[Collection("Forms runtime")]
	public sealed class TableLayoutTests : IDisposable
	{
		public TableLayoutTests()
		{
			FormsRuntime.Reset();
			FormsRuntime.Backend = new NullFormsBackend { ThreadAffinity = true };
		}

		public void Dispose()
		{
			FormsRuntime.Reset();
		}

		private sealed class Measured : Button
		{
			public Size Preferred { get; set; }

			public override Size GetPreferredSize(Size proposedSize) => Preferred;
		}

		private static TableLayoutPanel Table(int width, int height, int columns, int rows)
		{
			return new TableLayoutPanel { Size = new Size(width, height), ColumnCount = columns, RowCount = rows };
		}

		private static void Absolute(TableLayoutPanel t, params float[] columns)
		{
			foreach (float c in columns)
			{
				t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, c));
			}
		}

		private static void AbsoluteRows(TableLayoutPanel t, params float[] rows)
		{
			foreach (float r in rows)
			{
				t.RowStyles.Add(new RowStyle(SizeType.Absolute, r));
			}
		}

		/// <summary>A panel that fills its cell exactly (no margin).</summary>
		private static Panel Filler()
		{
			return new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
		}

		private static void Add(TableLayoutPanel t, params Control[] controls)
		{
			t.SuspendLayout();
			foreach (Control c in controls)
			{
				t.Controls.Add(c);
			}
			t.ResumeLayout(true);
		}

		private static Button[] Buttons(TableLayoutPanel t, int count)
		{
			var result = new Button[count];
			for (int i = 0; i < count; i++)
			{
				result[i] = new Button();
			}
			Add(t, result);
			return result;
		}

		private static Rectangle B(int x, int y) => new Rectangle(x, y, 75, 23);

		// ---------------------------------------------------------------- the styles

		[Fact]
		public void Absolute_columns_and_rows_give_the_cells_their_pixels()
		{
			TableLayoutPanel t = Table(400, 300, 2, 2);
			Absolute(t, 100, 120);
			AbsoluteRows(t, 40, 50);
			Button[] b = Buttons(t, 4);
			Assert.Equal(B(3, 3), b[0].Bounds);
			Assert.Equal(B(103, 3), b[1].Bounds);
			Assert.Equal(B(3, 43), b[2].Bounds);
			Assert.Equal(B(103, 43), b[3].Bounds);
			Assert.Equal(new[] { 100, 120 }, t.GetColumnWidths());
			Assert.Equal(new[] { 40, 50 }, t.GetRowHeights());
		}

		[Fact]
		public void A_control_wider_than_its_absolute_column_is_cut_to_the_cell()
		{
			TableLayoutPanel t = Table(400, 300, 1, 1);
			Absolute(t, 50);
			AbsoluteRows(t, 20);
			Button[] b = Buttons(t, 1);
			// 50 - 6 wide and 20 - 6 high.
			Assert.Equal(new Rectangle(3, 3, 44, 14), b[0].Bounds);
		}

		[Fact]
		public void AutoSize_columns_and_rows_take_the_largest_control_with_its_margin()
		{
			TableLayoutPanel t = Table(400, 300, 2, 2);
			var button = new Button();
			var text = new TextBox();
			var tall = new Button { Size = new Size(60, 60) };
			Add(t, button, text, tall);
			// column 0: max(75, 60) + 6 = 81; column 1: 100 + 6 = 106; row 0: max(23, 20) + 6 = 29; row 1: 60 + 6 = 66.
			Assert.Equal(new[] { 81, 106 }, t.GetColumnWidths());
			Assert.Equal(new[] { 29, 66 }, t.GetRowHeights());
			Assert.Equal(B(3, 3), button.Bounds);
			Assert.Equal(new Rectangle(84, 3, 100, 20), text.Bounds);
			Assert.Equal(new Rectangle(3, 32, 60, 60), tall.Bounds);
		}

		[Fact]
		public void A_column_without_a_style_is_AutoSize()
		{
			TableLayoutPanel t = Table(400, 300, 2, 1);
			t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
			Buttons(t, 2);
			Assert.Equal(new[] { 200, 81 }, t.GetColumnWidths());
		}

		[Fact]
		public void Percent_columns_share_what_absolute_columns_leave()
		{
			TableLayoutPanel t = Table(400, 100, 3, 1);
			t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
			t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
			t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
			t.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
			Panel a = Filler();
			Panel b = Filler();
			Panel c = Filler();
			Add(t, a, b, c);
			Assert.Equal(new Rectangle(0, 0, 100, 50), a.Bounds);
			Assert.Equal(new Rectangle(100, 0, 150, 50), b.Bounds);
			Assert.Equal(new Rectangle(250, 0, 150, 50), c.Bounds);
		}

		[Fact]
		public void Absolute_auto_and_percent_columns_can_be_mixed()
		{
			TableLayoutPanel t = Table(400, 100, 3, 1);
			t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
			t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			Add(t, Filler(), new Button(), Filler());
			// 400 - 100 - 81 = 219 for the percent column.
			Assert.Equal(new[] { 100, 81, 219 }, t.GetColumnWidths());
		}

		[Fact]
		public void Percent_shares_are_whole_pixels_and_the_last_percent_column_takes_the_rest()
		{
			TableLayoutPanel t = Table(100, 100, 3, 1);
			for (int i = 0; i < 3; i++)
			{
				t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
			}
			Assert.Equal(new[] { 33, 33, 34 }, GrowTo(t, 3));
			var u = Table(101, 100, 3, 1);
			u.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
			u.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
			u.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
			// 20.2 -> 20, 30.3 -> 30, the rest 51.
			Assert.Equal(new[] { 20, 30, 51 }, GrowTo(u, 3));
		}

		private static int[] GrowTo(TableLayoutPanel t, int columns)
		{
			var panels = new Control[columns];
			for (int i = 0; i < columns; i++)
			{
				panels[i] = Filler();
			}
			Add(t, panels);
			return t.GetColumnWidths();
		}

		[Fact]
		public void Percent_columns_get_nothing_when_nothing_is_left_and_a_percent_of_zero_gets_nothing()
		{
			TableLayoutPanel t = Table(400, 100, 2, 1);
			t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500));
			t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			Add(t, Filler(), Filler());
			Assert.Equal(new[] { 500, 0 }, t.GetColumnWidths());
			TableLayoutPanel z = Table(400, 100, 1, 1);
			z.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 0));
			Add(z, Filler());
			Assert.Equal(new[] { 0 }, z.GetColumnWidths());
		}

		[Fact]
		public void Percent_rows_share_the_height_that_is_left()
		{
			TableLayoutPanel t = Table(200, 300, 1, 2);
			t.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
			t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			Panel a = Filler();
			Panel b = Filler();
			Add(t, a, b);
			Assert.Equal(new Rectangle(0, 0, 200, 50), a.Bounds);
			Assert.Equal(new Rectangle(0, 50, 200, 250), b.Bounds);
		}

		[Fact]
		public void Changing_a_style_lays_the_table_out_again()
		{
			TableLayoutPanel t = Table(400, 100, 2, 1);
			Absolute(t, 100, 100);
			Button[] b = Buttons(t, 2);
			Assert.Equal(B(103, 3), b[1].Bounds);
			t.ColumnStyles[0].Width = 200;
			Assert.Equal(B(203, 3), b[1].Bounds);
			t.ColumnStyles[0].SizeType = SizeType.AutoSize;
			Assert.Equal(B(84, 3), b[1].Bounds);
			t.ColumnStyles[1] = new ColumnStyle(SizeType.Absolute, 300);
			Assert.Equal(new[] { 81, 300 }, t.GetColumnWidths());
			t.ColumnStyles.Clear();
			Assert.Equal(new[] { 81, 81 }, t.GetColumnWidths());
		}

		[Fact]
		public void A_filled_control_in_an_AutoSize_column_does_not_feed_its_stretched_size_back()
		{
			TableLayoutPanel t = Table(400, 100, 1, 1);
			var text = new TextBox { Dock = DockStyle.Fill };
			Add(t, text);
			// the column is as wide as the text box is (100 + 6) and the row as high (20 + 6), however large the panel is.
			Assert.Equal(new[] { 106 }, t.GetColumnWidths());
			Assert.Equal(new Rectangle(3, 3, 100, 20), text.Bounds);
			t.Size = new Size(600, 300);
			t.Size = new Size(300, 50);
			Assert.Equal(new[] { 106 }, t.GetColumnWidths());
			Assert.Equal(new Rectangle(3, 3, 100, 20), text.Bounds);
		}

		// ---------------------------------------------------------------- placement in the cell

		[Fact]
		public void Anchors_place_a_control_in_the_cell_and_stretch_it()
		{
			TableLayoutPanel t = Table(200, 100, 1, 1);
			Absolute(t, 200);
			AbsoluteRows(t, 100);
			Button topLeft = new Button();
			Button horizontal = new Button { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
			Button fill = new Button { Dock = DockStyle.Fill };
			Button bottomRight = new Button { Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
			Button none = new Button { Anchor = AnchorStyles.None };
			// one at a time in the single cell (the area of the controls is x = 3 to 197, y = 3 to 97).
			var seen = new List<Rectangle>();
			foreach (Button b in new[] { topLeft, horizontal, fill, bottomRight, none })
			{
				t.Controls.Clear();
				t.Controls.Add(b);
				seen.Add(b.Bounds);
			}
			Assert.Equal(B(3, 3), seen[0]);
			Assert.Equal(new Rectangle(3, 3, 194, 23), seen[1]);
			Assert.Equal(new Rectangle(3, 3, 194, 94), seen[2]);
			Assert.Equal(B(122, 74), seen[3]);
			// centered: 3 + (194 - 75) / 2 = 62 and 3 + (94 - 23) / 2 = 38.
			Assert.Equal(B(62, 38), seen[4]);
		}

		[Fact]
		public void Dock_stands_for_the_anchors_in_a_cell()
		{
			TableLayoutPanel t = Table(200, 100, 1, 1);
			Absolute(t, 200);
			AbsoluteRows(t, 100);
			var top = new Button { Dock = DockStyle.Top };
			var left = new Button { Dock = DockStyle.Left };
			var bottom = new Button { Dock = DockStyle.Bottom };
			var right = new Button { Dock = DockStyle.Right };
			var seen = new List<Rectangle>();
			foreach (Button b in new[] { top, left, bottom, right })
			{
				t.Controls.Clear();
				t.Controls.Add(b);
				seen.Add(b.Bounds);
			}
			Assert.Equal(new Rectangle(3, 3, 194, 23), seen[0]);
			Assert.Equal(new Rectangle(3, 3, 75, 94), seen[1]);
			Assert.Equal(new Rectangle(3, 74, 194, 23), seen[2]);
			Assert.Equal(new Rectangle(122, 3, 75, 94), seen[3]);
		}

		[Fact]
		public void The_margin_of_a_control_is_kept_free_inside_its_cell()
		{
			TableLayoutPanel t = Table(200, 100, 1, 1);
			Absolute(t, 100);
			AbsoluteRows(t, 60);
			var b = new Button { Dock = DockStyle.Fill, Margin = new Padding(10, 5, 20, 15) };
			Add(t, b);
			Assert.Equal(new Rectangle(10, 5, 70, 40), b.Bounds);
		}

		[Fact]
		public void Min_and_max_size_cut_a_stretched_control()
		{
			TableLayoutPanel t = Table(200, 100, 1, 1);
			Absolute(t, 200);
			AbsoluteRows(t, 100);
			var b = new Button { Dock = DockStyle.Fill, MaximumSize = new Size(150, 0), MinimumSize = new Size(0, 10) };
			Add(t, b);
			Assert.Equal(new Rectangle(3, 3, 150, 94), b.Bounds);
		}

		[Fact]
		public void An_AutoSize_control_has_the_size_it_asks_for_and_makes_its_column_that_wide()
		{
			TableLayoutPanel t = Table(400, 100, 2, 1);
			var measured = new Measured { Preferred = new Size(120, 40), AutoSize = true };
			var next = new Button();
			Add(t, measured, next);
			Assert.Equal(new[] { 126, 81 }, t.GetColumnWidths());
			Assert.Equal(new[] { 46 }, t.GetRowHeights());
			Assert.Equal(new Rectangle(3, 3, 120, 40), measured.Bounds);
			Assert.Equal(B(129, 3), next.Bounds);
		}

		[Fact]
		public void The_padding_of_the_panel_moves_the_origin_and_narrows_the_area_for_percent_columns()
		{
			TableLayoutPanel t = Table(400, 100, 2, 1);
			t.Padding = new Padding(10, 20, 30, 0);
			t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
			t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			t.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
			Panel a = Filler();
			Panel b = Filler();
			Add(t, a, b);
			// the area is x = 10 to 370: 360 wide, 260 for the percent column.
			Assert.Equal(new Rectangle(10, 20, 100, 50), a.Bounds);
			Assert.Equal(new Rectangle(110, 20, 260, 50), b.Bounds);
		}

		[Fact]
		public void Lines_between_the_cells_take_room_when_the_border_style_is_set()
		{
			TableLayoutPanel t = Table(400, 100, 2, 1);
			Absolute(t, 100, 100);
			AbsoluteRows(t, 50);
			t.CellBorderStyle = TableLayoutPanelCellBorderStyle.Single;
			Button[] b = Buttons(t, 2);
			// a line of 1 pixel before, between and after the cells: the cells start at x = 1 and x = 102.
			Assert.Equal(B(4, 4), b[0].Bounds);
			Assert.Equal(B(105, 4), b[1].Bounds);
			t.CellBorderStyle = TableLayoutPanelCellBorderStyle.None;
			Assert.Equal(B(103, 3), b[1].Bounds);
			t.CellBorderStyle = TableLayoutPanelCellBorderStyle.InsetDouble;
			Assert.Equal(B(3 + 3 + 100 + 3, 6), b[1].Bounds);
			Assert.Throws<InvalidEnumArgumentException>(() => t.CellBorderStyle = (TableLayoutPanelCellBorderStyle)9);
		}

		// ---------------------------------------------------------------- spans

		[Fact]
		public void A_column_span_covers_the_columns_and_the_next_control_continues_after_it()
		{
			TableLayoutPanel t = Table(300, 100, 3, 2);
			Absolute(t, 100, 100, 100);
			AbsoluteRows(t, 40, 40);
			Panel wide = Filler();
			t.SuspendLayout();
			t.Controls.Add(wide);
			t.SetColumnSpan(wide, 2);
			Panel next = Filler();
			t.Controls.Add(next);
			t.ResumeLayout(true);
			Assert.Equal(new Rectangle(0, 0, 200, 40), wide.Bounds);
			Assert.Equal(new Rectangle(200, 0, 100, 40), next.Bounds);
		}

		[Fact]
		public void A_row_span_covers_the_rows()
		{
			TableLayoutPanel t = Table(300, 100, 2, 2);
			Absolute(t, 100, 100);
			AbsoluteRows(t, 40, 30);
			Panel tall = Filler();
			Panel b = Filler();
			Panel c = Filler();
			t.SuspendLayout();
			t.Controls.Add(tall);
			t.SetRowSpan(tall, 2);
			t.Controls.Add(b);
			t.Controls.Add(c);
			t.ResumeLayout(true);
			// tall takes (0,0) and (0,1); b goes to (1,0), c to (1,1).
			Assert.Equal(new Rectangle(0, 0, 100, 70), tall.Bounds);
			Assert.Equal(new Rectangle(100, 0, 100, 40), b.Bounds);
			Assert.Equal(new Rectangle(100, 40, 100, 30), c.Bounds);
		}

		[Fact]
		public void A_spanning_control_makes_the_AutoSize_columns_it_covers_wider()
		{
			TableLayoutPanel t = Table(400, 100, 2, 2);
			var button = new Button();
			var text = new TextBox();
			var wide = new Panel { Size = new Size(300, 20) };
			t.SuspendLayout();
			t.Controls.Add(button);
			t.Controls.Add(text);
			t.Controls.Add(wide);
			t.SetColumnSpan(wide, 2);
			t.ResumeLayout(true);
			// the columns are 81 and 106; the panel needs 306: the missing 119 are shared (59 each) and the odd pixel goes to the last: 140 and 166.
			Assert.Equal(new[] { 140, 166 }, t.GetColumnWidths());
			Assert.Equal(new Rectangle(3, 32, 300, 20), wide.Bounds);
		}

		[Fact]
		public void A_span_over_absolute_columns_does_not_change_them()
		{
			TableLayoutPanel t = Table(400, 100, 2, 1);
			Absolute(t, 100, 100);
			var wide = new Panel { Size = new Size(500, 20) };
			t.SuspendLayout();
			t.Controls.Add(wide);
			t.SetColumnSpan(wide, 2);
			t.ResumeLayout(true);
			Assert.Equal(new[] { 100, 100 }, t.GetColumnWidths());
			Assert.Equal(new Rectangle(3, 3, 194, 20), wide.Bounds);
		}

		[Fact]
		public void A_span_is_placed_in_the_next_row_when_it_does_not_fit_the_rest_of_the_row()
		{
			TableLayoutPanel t = Table(300, 100, 3, 1);
			Absolute(t, 100, 100, 100);
			Panel first = Filler();
			Panel second = Filler();
			t.SuspendLayout();
			t.Controls.Add(first);
			t.SetColumnSpan(first, 2);
			t.Controls.Add(second);
			t.SetColumnSpan(second, 2);
			t.ResumeLayout(true);
			Assert.Equal(new TableLayoutPanelCellPosition(0, 0), t.GetPositionFromControl(first));
			Assert.Equal(new TableLayoutPanelCellPosition(0, 1), t.GetPositionFromControl(second));
			Assert.Equal(2, t.RowCount);
		}

		// ---------------------------------------------------------------- the cells and the growth

		[Fact]
		public void Controls_fill_the_cells_row_by_row_and_the_table_adds_rows()
		{
			TableLayoutPanel t = Table(400, 300, 2, 1);
			Button[] b = Buttons(t, 5);
			var cells = new List<TableLayoutPanelCellPosition>();
			foreach (Button c in b)
			{
				cells.Add(t.GetPositionFromControl(c));
			}
			Assert.Equal(new[] { Pos(0, 0), Pos(1, 0), Pos(0, 1), Pos(1, 1), Pos(0, 2) }, cells);
			Assert.Equal(3, t.RowCount);
			Assert.Equal(2, t.ColumnCount);
			Assert.Equal(new[] { 81, 81 }, t.GetColumnWidths());
			Assert.Equal(new[] { 29, 29, 29 }, t.GetRowHeights());
			Assert.Equal(B(3, 61), b[4].Bounds);
		}

		private static TableLayoutPanelCellPosition Pos(int column, int row) => new TableLayoutPanelCellPosition(column, row);

		[Fact]
		public void With_GrowStyle_AddColumns_the_cells_are_filled_column_by_column()
		{
			TableLayoutPanel t = Table(400, 300, 0, 2);
			t.GrowStyle = TableLayoutPanelGrowStyle.AddColumns;
			Button[] b = Buttons(t, 5);
			var cells = new List<TableLayoutPanelCellPosition>();
			foreach (Button c in b)
			{
				cells.Add(t.GetPositionFromControl(c));
			}
			Assert.Equal(new[] { Pos(0, 0), Pos(0, 1), Pos(1, 0), Pos(1, 1), Pos(2, 0) }, cells);
			Assert.Equal(3, t.ColumnCount);
			Assert.Equal(2, t.RowCount);
		}

		[Fact]
		public void A_default_table_has_one_column_and_adds_rows()
		{
			var t = new TableLayoutPanel { Size = new Size(200, 200) };
			Button[] b = Buttons(t, 3);
			Assert.Equal(B(3, 3), b[0].Bounds);
			Assert.Equal(B(3, 32), b[1].Bounds);
			Assert.Equal(B(3, 61), b[2].Bounds);
			Assert.Equal(TableLayoutPanelGrowStyle.AddRows, t.GrowStyle);
			Assert.Equal(1, t.ColumnCount);
			Assert.Equal(3, t.RowCount);
		}

		[Fact]
		public void A_FixedSize_table_that_is_full_refuses_another_control()
		{
			TableLayoutPanel t = Table(400, 300, 2, 1);
			t.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
			Buttons(t, 2);
			Assert.Throws<ArgumentException>(() => t.Controls.Add(new Button()));
			var u = Table(400, 300, 2, 2);
			u.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
			Add(u, new Button(), new Button(), new Button(), new Button());
			var late = new Button();
			Assert.Throws<ArgumentException>(() => u.Controls.Add(late, 2, 0));
		}

		[Fact]
		public void Controls_with_a_cell_keep_it_and_the_others_fill_the_free_cells()
		{
			TableLayoutPanel t = Table(400, 300, 2, 2);
			var fixedCell = new Button();
			var a = new Button();
			var b = new Button();
			t.SuspendLayout();
			t.Controls.Add(a);
			t.Controls.Add(fixedCell);
			t.Controls.Add(b);
			t.SetCellPosition(fixedCell, Pos(1, 0));
			t.ResumeLayout(true);
			Assert.Equal(Pos(0, 0), t.GetPositionFromControl(a));
			Assert.Equal(Pos(1, 0), t.GetPositionFromControl(fixedCell));
			Assert.Equal(Pos(0, 1), t.GetPositionFromControl(b));
		}

		[Fact]
		public void A_cell_outside_the_counts_makes_the_table_grow()
		{
			TableLayoutPanel t = Table(400, 300, 1, 1);
			var far = new Button();
			t.Controls.Add(far, 2, 3);
			Assert.Equal(3, t.ColumnCount);
			Assert.Equal(4, t.RowCount);
			Assert.Equal(new[] { 0, 0, 81 }, t.GetColumnWidths());
			Assert.Equal(new[] { 0, 0, 0, 29 }, t.GetRowHeights());
			Assert.Equal(B(3, 3), far.Bounds);
		}

		[Fact]
		public void A_hidden_control_takes_no_cell()
		{
			TableLayoutPanel t = Table(400, 300, 3, 1);
			Button[] b = Buttons(t, 3);
			b[1].Visible = false;
			Assert.Equal(Pos(1, 0), t.GetPositionFromControl(b[2]));
			Assert.Equal(Pos(-1, -1), t.GetPositionFromControl(b[1]));
			b[1].Visible = true;
			Assert.Equal(Pos(2, 0), t.GetPositionFromControl(b[2]));
		}

		[Fact]
		public void Add_with_a_column_and_a_row_puts_the_control_into_that_cell_with_one_layout()
		{
			TableLayoutPanel t = Table(400, 300, 2, 2);
			Absolute(t, 100, 100);
			AbsoluteRows(t, 40, 40);
			var seen = new List<string>();
			t.Layout += (s, e) => seen.Add(e.AffectedProperty);
			var b = new Button();
			t.Controls.Add(b, 1, 1);
			Assert.Equal(B(103, 43), b.Bounds);
			Assert.Equal(new[] { "Parent" }, seen);
			Assert.IsType<TableLayoutControlCollection>(t.Controls);
			Assert.Same(t, t.Controls.Container);
			Assert.Equal(1, t.GetColumn(b));
			Assert.Equal(1, t.GetRow(b));
		}

		[Fact]
		public void The_counts_the_script_sets_lay_the_table_out_again()
		{
			TableLayoutPanel t = Table(400, 300, 2, 1);
			Button[] b = Buttons(t, 3);
			Assert.Equal(B(3, 32), b[2].Bounds);
			t.ColumnCount = 3;
			Assert.Equal(B(165, 3), b[2].Bounds);
			t.GrowStyle = TableLayoutPanelGrowStyle.AddColumns;
			t.ColumnCount = 0;
			t.RowCount = 2;
			Assert.Equal(B(84, 3), b[2].Bounds);
			Assert.Throws<ArgumentOutOfRangeException>(() => t.ColumnCount = -1);
			Assert.Throws<ArgumentOutOfRangeException>(() => t.RowCount = -1);
			Assert.Throws<InvalidEnumArgumentException>(() => t.GrowStyle = (TableLayoutPanelGrowStyle)7);
		}

		// ---------------------------------------------------------------- the properties of the cells

		[Fact]
		public void The_cell_properties_default_to_not_set_and_one_by_one()
		{
			TableLayoutPanel t = Table(400, 300, 2, 2);
			var b = new Button();
			Assert.Equal(-1, t.GetColumn(b));
			Assert.Equal(-1, t.GetRow(b));
			Assert.Equal(1, t.GetColumnSpan(b));
			Assert.Equal(1, t.GetRowSpan(b));
			Assert.Equal(Pos(-1, -1), t.GetCellPosition(b));
			t.SetColumn(b, 1);
			t.SetRow(b, 2);
			t.SetColumnSpan(b, 3);
			t.SetRowSpan(b, 4);
			Assert.Equal(Pos(1, 2), t.GetCellPosition(b));
			Assert.Equal(3, t.GetColumnSpan(b));
			Assert.Equal(4, t.GetRowSpan(b));
			t.SetCellPosition(b, Pos(0, 5));
			Assert.Equal(0, t.GetColumn(b));
			Assert.Equal(5, t.GetRow(b));
			Assert.Throws<ArgumentOutOfRangeException>(() => t.SetColumn(b, -2));
			Assert.Throws<ArgumentOutOfRangeException>(() => t.SetRow(b, -2));
			Assert.Throws<ArgumentOutOfRangeException>(() => t.SetColumnSpan(b, 0));
			Assert.Throws<ArgumentOutOfRangeException>(() => t.SetRowSpan(b, 0));
			Assert.Throws<ArgumentNullException>(() => t.GetColumn(null));
			Assert.Throws<ArgumentOutOfRangeException>(() => new TableLayoutPanelCellPosition(-2, 0));
		}

		[Fact]
		public void A_cell_position_compares_by_value()
		{
			Assert.True(Pos(1, 2) == Pos(1, 2));
			Assert.True(Pos(1, 2) != Pos(2, 1));
			Assert.True(Pos(1, 2).Equals((object)Pos(1, 2)));
			Assert.False(Pos(1, 2).Equals("1,2"));
			Assert.Equal("1,2", Pos(1, 2).ToString());
			Assert.Equal(Pos(1, 2).GetHashCode(), Pos(1, 2).GetHashCode());
			var p = new TableLayoutPanelCellPosition { Column = 3, Row = 4 };
			Assert.Equal(Pos(3, 4), p);
		}

		[Fact]
		public void GetControlFromPosition_finds_the_control_that_covers_a_cell()
		{
			TableLayoutPanel t = Table(300, 100, 3, 2);
			Panel wide = Filler();
			Panel other = Filler();
			t.SuspendLayout();
			t.Controls.Add(wide);
			t.SetColumnSpan(wide, 2);
			t.Controls.Add(other);
			t.ResumeLayout(true);
			Assert.Same(wide, t.GetControlFromPosition(0, 0));
			Assert.Same(wide, t.GetControlFromPosition(1, 0));
			Assert.Same(other, t.GetControlFromPosition(2, 0));
			Assert.Null(t.GetControlFromPosition(0, 1));
			Assert.Null(t.GetControlFromPosition(7, 7));
		}

		[Fact]
		public void The_cell_stays_with_the_control_when_it_moves_to_another_table()
		{
			TableLayoutPanel one = Table(300, 100, 2, 2);
			TableLayoutPanel two = Table(300, 100, 2, 2);
			var b = new Button();
			one.Controls.Add(b, 1, 1);
			two.Controls.Add(b);
			Assert.Equal(Pos(1, 1), two.GetPositionFromControl(b));
		}

		[Fact]
		public void The_settings_object_is_the_panel_and_cannot_be_replaced()
		{
			TableLayoutPanel t = Table(300, 100, 2, 3);
			TableLayoutSettings settings = t.LayoutSettings;
			Assert.Same(settings, t.LayoutSettings);
			Assert.Equal(2, settings.ColumnCount);
			Assert.Equal(3, settings.RowCount);
			Assert.Same(t.ColumnStyles, settings.ColumnStyles);
			Assert.Same(t.RowStyles, settings.RowStyles);
			Assert.NotNull(settings.LayoutEngine);
			settings.ColumnCount = 4;
			Assert.Equal(4, t.ColumnCount);
			var b = new Button();
			settings.SetColumn(b, 2);
			Assert.Equal(2, t.GetColumn(b));
			t.LayoutSettings = settings;
			Assert.Throws<NotSupportedException>(() => t.LayoutSettings = Table(1, 1, 1, 1).LayoutSettings);
			Assert.Throws<ArgumentException>(() => settings.SetColumn("text", 1));
		}

		// ---------------------------------------------------------------- the style collections

		[Fact]
		public void A_style_collection_is_a_list_of_styles_that_can_be_in_one_collection_only()
		{
			TableLayoutPanel t = Table(300, 100, 2, 2);
			var a = new ColumnStyle(SizeType.Absolute, 10);
			var b = new ColumnStyle(SizeType.Percent, 50);
			Assert.Equal(0, t.ColumnStyles.Add(a));
			Assert.Equal(1, t.ColumnStyles.Add(b));
			Assert.Equal(2, t.ColumnStyles.Count);
			Assert.True(t.ColumnStyles.Contains(a));
			Assert.Equal(1, t.ColumnStyles.IndexOf(b));
			Assert.Same(a, t.ColumnStyles[0]);
			Assert.Throws<ArgumentException>(() => t.ColumnStyles.Add(a));
			Assert.Throws<ArgumentException>(() => ((TableLayoutStyleCollection)t.RowStyles).Add(a));
			var c = new ColumnStyle();
			t.ColumnStyles.Insert(1, c);
			Assert.Equal(new[] { a, c, b }, new[] { t.ColumnStyles[0], t.ColumnStyles[1], t.ColumnStyles[2] });
			t.ColumnStyles.Remove(c);
			t.ColumnStyles.RemoveAt(0);
			Assert.Equal(1, t.ColumnStyles.Count);
			// removed styles are free again
			Assert.Equal(1, t.ColumnStyles.Add(a));
			System.Collections.IList list = t.ColumnStyles;
			Assert.False(list.IsFixedSize);
			Assert.False(list.IsReadOnly);
			Assert.True(list.Contains(b));
			Assert.Equal(2, list.Count);
			Assert.Throws<InvalidCastException>(() => list.Add("x"));
			t.ColumnStyles.Clear();
			Assert.Equal(0, t.ColumnStyles.Count);
		}

		[Fact]
		public void Styles_have_AutoSize_and_zero_as_default_and_check_their_values()
		{
			var column = new ColumnStyle();
			Assert.Equal(SizeType.AutoSize, column.SizeType);
			Assert.Equal(0f, column.Width);
			var row = new RowStyle(SizeType.Percent, 25);
			Assert.Equal(SizeType.Percent, row.SizeType);
			Assert.Equal(25f, row.Height);
			Assert.Equal(SizeType.Absolute, new RowStyle(SizeType.Absolute).SizeType);
			Assert.Throws<ArgumentOutOfRangeException>(() => column.Width = -1);
			Assert.Throws<ArgumentOutOfRangeException>(() => new RowStyle(SizeType.Absolute, -5));
			Assert.Throws<InvalidEnumArgumentException>(() => column.SizeType = (SizeType)5);
		}

		[Fact]
		public void The_row_and_column_collections_are_typed()
		{
			TableLayoutPanel t = Table(300, 100, 2, 2);
			t.RowStyles.Add(new RowStyle(SizeType.Absolute, 5));
			RowStyle r = t.RowStyles[0];
			Assert.Equal(5f, r.Height);
			Assert.True(t.RowStyles.Contains(r));
			Assert.Equal(0, t.RowStyles.IndexOf(r));
			t.RowStyles.Insert(0, new RowStyle(SizeType.Absolute, 9));
			Assert.Equal(9f, t.RowStyles[0].Height);
			t.RowStyles.Remove(r);
			Assert.Equal(1, t.RowStyles.Count);
		}

		// ---------------------------------------------------------------- preferred size and events

		[Fact]
		public void The_preferred_size_adds_the_columns_and_rows_and_counts_percent_by_content()
		{
			TableLayoutPanel t = Table(100, 100, 3, 1);
			t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
			t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			Add(t, new Panel { Size = new Size(10, 10), Margin = Padding.Empty }, new Button(), new Button());
			// 100 + 81 + 81 wide, one row of 29.
			Assert.Equal(new Size(262, 29), t.GetPreferredSize(Size.Empty));
			t.CellBorderStyle = TableLayoutPanelCellBorderStyle.Single;
			Assert.Equal(new Size(266, 31), t.GetPreferredSize(Size.Empty));
			t.Padding = new Padding(2);
			Assert.Equal(new Size(270, 35), t.GetPreferredSize(Size.Empty));
		}

		[Fact]
		public void An_empty_table_has_no_size_of_its_own()
		{
			var t = new TableLayoutPanel();
			Assert.Equal(Size.Empty, t.GetPreferredSize(Size.Empty));
			Assert.Equal(new int[0], t.GetColumnWidths());
			Assert.Equal(new int[0], t.GetRowHeights());
		}

		[Fact]
		public void A_change_of_the_table_raises_Layout_with_the_property_that_changed()
		{
			TableLayoutPanel t = Table(300, 100, 2, 2);
			var b = new Button();
			t.Controls.Add(b);
			var seen = new List<string>();
			t.Layout += (s, e) => seen.Add(e.AffectedProperty);
			t.ColumnCount = 3;
			t.RowCount = 3;
			t.GrowStyle = TableLayoutPanelGrowStyle.AddColumns;
			t.CellBorderStyle = TableLayoutPanelCellBorderStyle.Single;
			t.ColumnStyles.Add(new ColumnStyle());
			t.SetColumn(b, 1);
			t.SetRow(b, 1);
			t.SetColumnSpan(b, 2);
			t.SetRowSpan(b, 2);
			Assert.Equal(new[] { "ColumnCount", "RowCount", "GrowStyle", "CellBorderStyle", "Style", "TableIndex", "TableIndex", "ColumnSpan", "RowSpan" }, seen);
		}

		[Fact]
		public void CellPaint_has_handlers_and_arguments_and_the_panel_is_an_extender_of_its_children()
		{
			TableLayoutPanel t = Table(300, 100, 2, 2);
			var b = new Button();
			t.Controls.Add(b);
			var extender = (IExtenderProvider)t;
			Assert.True(extender.CanExtend(b));
			Assert.False(extender.CanExtend(new Button()));
			TableLayoutCellPaintEventHandler handler = (s, e) => { };
			t.CellPaint += handler;
			t.CellPaint -= handler;
			Assert.Equal(BorderStyle.None, t.BorderStyle);
		}

		[Fact]
		public void A_table_in_a_table_cell_is_laid_out_inside_its_cell()
		{
			TableLayoutPanel outer = Table(400, 200, 2, 1);
			Absolute(outer, 100, 300);
			AbsoluteRows(outer, 100);
			TableLayoutPanel inner = Table(10, 10, 1, 2);
			inner.Dock = DockStyle.Fill;
			inner.Margin = Padding.Empty;
			Absolute(inner, 50);
			AbsoluteRows(inner, 30, 30);
			Button[] b = Buttons(inner, 2);
			Add(outer, Filler(), inner);
			Assert.Equal(new Rectangle(100, 0, 300, 100), inner.Bounds);
			// a column of 50 pixels cuts the button to 44; the second row starts at 30.
			Assert.Equal(new Rectangle(3, 33, 44, 23), b[1].Bounds);
		}
	}
}
