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
	/// FlowLayoutPanel (P4.9c). The expected rectangles are worked out by hand from the flow algorithm of System.Windows.Forms.Layout.FlowLayout: a cell is the control plus its
	/// Margin (a Button is 75 x 23 with Margin 3, so a cell is 81 x 29 and the control sits 3 pixels in), a row ends when the next cell does not fit into the display
	/// rectangle or after a flow break, a row is as high as its highest cell and a control is placed in it by its anchors.
	/// </summary>
	[Collection("Forms runtime")]
	public sealed class FlowLayoutTests : IDisposable
	{
		public FlowLayoutTests()
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

			public Size LastProposal { get; private set; }

			public override Size GetPreferredSize(Size proposedSize)
			{
				LastProposal = proposedSize;
				return Preferred;
			}
		}

		private static FlowLayoutPanel Flow(int width, int height, FlowDirection direction = FlowDirection.LeftToRight)
		{
			return new FlowLayoutPanel { Size = new Size(width, height), FlowDirection = direction };
		}

		private static Button[] Buttons(FlowLayoutPanel panel, int count)
		{
			var result = new Button[count];
			panel.SuspendLayout();
			for (int i = 0; i < count; i++)
			{
				result[i] = new Button { Name = "b" + (i + 1) };
				panel.Controls.Add(result[i]);
			}
			panel.ResumeLayout(true);
			return result;
		}

		private static Rectangle Cell(int x, int y) => new Rectangle(x, y, 75, 23);

		// ---------------------------------------------------------------- flow and wrap

		[Fact]
		public void Controls_flow_from_left_to_right_one_cell_after_the_other()
		{
			FlowLayoutPanel panel = Flow(400, 100);
			Button[] b = Buttons(panel, 3);
			// cells of 81 x 29, the control 3 pixels in: x = 3, 84, 165.
			Assert.Equal(Cell(3, 3), b[0].Bounds);
			Assert.Equal(Cell(84, 3), b[1].Bounds);
			Assert.Equal(Cell(165, 3), b[2].Bounds);
		}

		[Fact]
		public void A_control_that_does_not_fit_starts_the_next_row()
		{
			FlowLayoutPanel panel = Flow(200, 200);
			Button[] b = Buttons(panel, 4);
			// two cells (162) fit in 200, a third (243) does not.
			Assert.Equal(Cell(3, 3), b[0].Bounds);
			Assert.Equal(Cell(84, 3), b[1].Bounds);
			Assert.Equal(Cell(3, 32), b[2].Bounds);
			Assert.Equal(Cell(84, 32), b[3].Bounds);
		}

		[Fact]
		public void A_row_that_is_exactly_full_does_not_wrap()
		{
			FlowLayoutPanel panel = Flow(243, 200);
			Button[] b = Buttons(panel, 4);
			Assert.Equal(Cell(165, 3), b[2].Bounds);
			Assert.Equal(Cell(3, 32), b[3].Bounds);
			panel.Width = 242;
			Assert.Equal(Cell(3, 32), b[2].Bounds);
		}

		[Fact]
		public void The_first_control_of_a_row_stays_even_when_it_is_wider_than_the_panel()
		{
			FlowLayoutPanel panel = Flow(200, 200);
			var wide = new Button { Size = new Size(300, 23) };
			var next = new Button();
			panel.Controls.Add(wide);
			panel.Controls.Add(next);
			Assert.Equal(new Rectangle(3, 3, 300, 23), wide.Bounds);
			Assert.Equal(Cell(3, 32), next.Bounds);
		}

		[Fact]
		public void Without_wrapping_everything_stays_in_one_row()
		{
			FlowLayoutPanel panel = Flow(200, 100);
			panel.WrapContents = false;
			Button[] b = Buttons(panel, 4);
			Assert.Equal(Cell(3, 3), b[0].Bounds);
			Assert.Equal(Cell(84, 3), b[1].Bounds);
			Assert.Equal(Cell(165, 3), b[2].Bounds);
			Assert.Equal(Cell(246, 3), b[3].Bounds);
			panel.WrapContents = true;
			Assert.Equal(Cell(3, 32), b[2].Bounds);
		}

		[Fact]
		public void A_flow_break_ends_the_row_after_its_control()
		{
			FlowLayoutPanel panel = Flow(400, 100);
			Button[] b = Buttons(panel, 3);
			Assert.False(panel.GetFlowBreak(b[1]));
			panel.SetFlowBreak(b[1], true);
			Assert.True(panel.GetFlowBreak(b[1]));
			Assert.Equal(Cell(84, 3), b[1].Bounds);
			Assert.Equal(Cell(3, 32), b[2].Bounds);
			panel.SetFlowBreak(b[1], false);
			Assert.Equal(Cell(165, 3), b[2].Bounds);
		}

		[Fact]
		public void A_flow_break_is_ignored_without_wrapping()
		{
			FlowLayoutPanel panel = Flow(400, 100);
			panel.WrapContents = false;
			Button[] b = Buttons(panel, 3);
			panel.SetFlowBreak(b[0], true);
			Assert.Equal(Cell(84, 3), b[1].Bounds);
		}

		[Fact]
		public void Resizing_the_panel_wraps_the_rows_again()
		{
			FlowLayoutPanel panel = Flow(400, 100);
			Button[] b = Buttons(panel, 3);
			Assert.Equal(Cell(165, 3), b[2].Bounds);
			panel.Size = new Size(200, 100);
			Assert.Equal(Cell(3, 32), b[2].Bounds);
			panel.Size = new Size(400, 100);
			Assert.Equal(Cell(165, 3), b[2].Bounds);
		}

		// ---------------------------------------------------------------- directions

		[Fact]
		public void Right_to_left_starts_at_the_right_edge_and_the_rows_go_down()
		{
			FlowLayoutPanel panel = Flow(400, 200, FlowDirection.RightToLeft);
			Button[] b = Buttons(panel, 5);
			// the cell of the first control ends at 400: x = 400 - 3 - 75 = 322, then 241, 160, 79; four cells (324) fit in 400, the fifth wraps.
			Assert.Equal(Cell(322, 3), b[0].Bounds);
			Assert.Equal(Cell(241, 3), b[1].Bounds);
			Assert.Equal(Cell(160, 3), b[2].Bounds);
			Assert.Equal(Cell(79, 3), b[3].Bounds);
			Assert.Equal(Cell(322, 32), b[4].Bounds);
		}

		[Fact]
		public void Top_down_fills_a_column_and_the_next_column_lies_to_the_right()
		{
			FlowLayoutPanel panel = Flow(400, 100, FlowDirection.TopDown);
			Button[] b = Buttons(panel, 4);
			// three cells (87) fit in 100 high, the fourth (116) does not.
			Assert.Equal(Cell(3, 3), b[0].Bounds);
			Assert.Equal(Cell(3, 32), b[1].Bounds);
			Assert.Equal(Cell(3, 61), b[2].Bounds);
			Assert.Equal(Cell(84, 3), b[3].Bounds);
		}

		[Fact]
		public void Bottom_up_starts_at_the_bottom_edge_and_the_columns_go_right()
		{
			FlowLayoutPanel panel = Flow(400, 100, FlowDirection.BottomUp);
			Button[] b = Buttons(panel, 4);
			Assert.Equal(Cell(3, 74), b[0].Bounds);
			Assert.Equal(Cell(3, 45), b[1].Bounds);
			Assert.Equal(Cell(3, 16), b[2].Bounds);
			Assert.Equal(Cell(84, 74), b[3].Bounds);
		}

		[Fact]
		public void Changing_the_direction_arranges_the_children_again()
		{
			FlowLayoutPanel panel = Flow(400, 100);
			Button[] b = Buttons(panel, 2);
			Assert.Equal(Cell(84, 3), b[1].Bounds);
			panel.FlowDirection = FlowDirection.TopDown;
			Assert.Equal(Cell(3, 32), b[1].Bounds);
			Assert.Throws<InvalidEnumArgumentException>(() => panel.FlowDirection = (FlowDirection)9);
		}

		[Fact]
		public void The_defaults_are_left_to_right_with_wrapping()
		{
			var panel = new FlowLayoutPanel();
			Assert.Equal(FlowDirection.LeftToRight, panel.FlowDirection);
			Assert.True(panel.WrapContents);
			Assert.Equal(new Size(200, 100), panel.Size);
			Assert.NotNull(panel.LayoutEngine);
			Assert.NotSame(new Panel().LayoutEngine, panel.LayoutEngine);
		}

		// ---------------------------------------------------------------- the cross axis of a row

		[Fact]
		public void A_row_is_as_high_as_its_highest_cell_and_the_anchors_place_the_others_in_it()
		{
			FlowLayoutPanel panel = Flow(600, 200);
			var tall = new Button { Size = new Size(75, 60) };
			var top = new Button();
			var bottom = new Button { Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
			var middle = new Button { Anchor = AnchorStyles.Left };
			var stretch = new Button { Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left };
			panel.SuspendLayout();
			foreach (Control c in new Control[] { tall, top, bottom, middle, stretch })
			{
				panel.Controls.Add(c);
			}
			panel.ResumeLayout(true);
			// the row is 66 high (60 + 6): the area of the controls is y = 3 to 63.
			Assert.Equal(new Rectangle(3, 3, 75, 60), tall.Bounds);
			Assert.Equal(new Rectangle(84, 3, 75, 23), top.Bounds);
			Assert.Equal(new Rectangle(165, 40, 75, 23), bottom.Bounds);
			Assert.Equal(new Rectangle(246, 21, 75, 23), middle.Bounds);
			Assert.Equal(new Rectangle(327, 3, 75, 60), stretch.Bounds);
		}

		[Fact]
		public void Dock_counts_as_the_anchors_it_stands_for()
		{
			FlowLayoutPanel panel = Flow(600, 200);
			var tall = new Button { Size = new Size(75, 60) };
			var fill = new Button { Dock = DockStyle.Fill };
			var dockTop = new Button { Dock = DockStyle.Top };
			var dockBottom = new Button { Dock = DockStyle.Bottom };
			var dockLeft = new Button { Dock = DockStyle.Left };
			panel.SuspendLayout();
			foreach (Control c in new Control[] { tall, fill, dockTop, dockBottom, dockLeft })
			{
				panel.Controls.Add(c);
			}
			panel.ResumeLayout(true);
			// the flow axis keeps the width of the control, the cross axis follows the dock: Fill and Left stretch, Top and Bottom align.
			Assert.Equal(new Rectangle(84, 3, 75, 60), fill.Bounds);
			Assert.Equal(new Rectangle(165, 3, 75, 23), dockTop.Bounds);
			Assert.Equal(new Rectangle(246, 40, 75, 23), dockBottom.Bounds);
			Assert.Equal(new Rectangle(327, 3, 75, 60), dockLeft.Bounds);
		}

		[Fact]
		public void Without_wrapping_the_row_is_as_high_as_the_panel()
		{
			FlowLayoutPanel panel = Flow(400, 100);
			panel.WrapContents = false;
			var first = new Button();
			var fill = new Button { Size = new Size(50, 10), Dock = DockStyle.Fill };
			var bottom = new Button { Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
			panel.Controls.Add(first);
			panel.Controls.Add(fill);
			panel.Controls.Add(bottom);
			Assert.Equal(Cell(3, 3), first.Bounds);
			Assert.Equal(new Rectangle(84, 3, 50, 94), fill.Bounds);
			Assert.Equal(new Rectangle(140, 74, 75, 23), bottom.Bounds);
		}

		[Fact]
		public void The_vertical_flow_aligns_by_the_left_and_right_anchors()
		{
			FlowLayoutPanel panel = Flow(400, 300, FlowDirection.TopDown);
			var wide = new Button { Size = new Size(120, 23) };
			var left = new Button();
			var right = new Button { Anchor = AnchorStyles.Top | AnchorStyles.Right };
			var stretch = new Button { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
			panel.SuspendLayout();
			foreach (Control c in new Control[] { wide, left, right, stretch })
			{
				panel.Controls.Add(c);
			}
			panel.ResumeLayout(true);
			// the column is 126 wide; its controls lie in x = 3 to 123.
			Assert.Equal(new Rectangle(3, 3, 120, 23), wide.Bounds);
			Assert.Equal(new Rectangle(3, 32, 75, 23), left.Bounds);
			Assert.Equal(new Rectangle(48, 61, 75, 23), right.Bounds);
			Assert.Equal(new Rectangle(3, 90, 120, 23), stretch.Bounds);
		}

		// ---------------------------------------------------------------- margin, padding, visibility, sizes

		[Fact]
		public void The_margin_of_a_control_makes_its_cell_larger()
		{
			FlowLayoutPanel panel = Flow(400, 100);
			var a = new Button { Margin = new Padding(10, 5, 20, 5) };
			var b = new Button();
			panel.Controls.Add(a);
			panel.Controls.Add(b);
			// the cell of a is 105 x 33; b starts at 105 and sits 3 in; a sits (10, 5) in.
			Assert.Equal(Cell(10, 5), a.Bounds);
			Assert.Equal(Cell(108, 3), b.Bounds);
		}

		[Fact]
		public void The_padding_of_the_panel_moves_the_origin_and_narrows_the_area()
		{
			FlowLayoutPanel panel = Flow(200, 200);
			panel.Padding = new Padding(10);
			Button[] b = Buttons(panel, 3);
			// the area is 180 wide: two cells (162) fit.
			Assert.Equal(Cell(13, 13), b[0].Bounds);
			Assert.Equal(Cell(94, 13), b[1].Bounds);
			Assert.Equal(Cell(13, 42), b[2].Bounds);
		}

		[Fact]
		public void A_hidden_control_takes_no_place()
		{
			FlowLayoutPanel panel = Flow(400, 100);
			Button[] b = Buttons(panel, 3);
			b[1].Visible = false;
			Assert.Equal(Cell(84, 3), b[2].Bounds);
			b[1].Visible = true;
			Assert.Equal(Cell(165, 3), b[2].Bounds);
		}

		[Fact]
		public void The_size_the_script_set_is_what_the_flow_uses_not_a_stretched_size()
		{
			FlowLayoutPanel panel = Flow(600, 200);
			var tall = new Button { Size = new Size(75, 60) };
			var fill = new Button { Dock = DockStyle.Fill };
			panel.Controls.Add(tall);
			panel.Controls.Add(fill);
			Assert.Equal(new Rectangle(84, 3, 75, 60), fill.Bounds);
			// the tall control goes: the row shrinks to the 23 pixels of the Fill button, which does not keep the 60 it was stretched to.
			panel.Controls.Remove(tall);
			Assert.Equal(Cell(3, 3), fill.Bounds);
		}

		[Fact]
		public void MaximumSize_and_MinimumSize_apply_to_a_stretched_control()
		{
			FlowLayoutPanel panel = Flow(600, 200);
			var tall = new Button { Size = new Size(75, 60) };
			var capped = new Button { Dock = DockStyle.Fill, MaximumSize = new Size(0, 40) };
			panel.Controls.Add(tall);
			panel.Controls.Add(capped);
			Assert.Equal(new Rectangle(84, 3, 75, 40), capped.Bounds);
		}

		[Fact]
		public void An_AutoSize_control_is_arranged_with_the_size_it_asks_for()
		{
			FlowLayoutPanel panel = Flow(400, 100);
			var measured = new Measured { Preferred = new Size(120, 40), AutoSize = true };
			var next = new Button();
			panel.Controls.Add(measured);
			panel.Controls.Add(next);
			Assert.Equal(new Rectangle(3, 3, 120, 40), measured.Bounds);
			// the cell of the first is 126 wide and the row 46 high.
			Assert.Equal(Cell(129, 3), next.Bounds);
			Assert.Equal(Size.Empty, measured.LastProposal);
		}

		[Fact]
		public void An_AutoSize_control_that_cannot_measure_itself_is_arranged_with_its_size()
		{
			FlowLayoutPanel panel = Flow(400, 100);
			var b = new Button { AutoSize = true };
			panel.Controls.Add(b);
			Assert.Equal(Cell(3, 3), b.Bounds);
		}

		// ---------------------------------------------------------------- the engine and its triggers

		[Fact]
		public void The_layout_engine_lays_out_on_request_and_the_panel_that_is_AutoSize_reports_that_it_sizes_itself()
		{
			FlowLayoutPanel panel = Flow(400, 100);
			Button[] b = Buttons(panel, 2);
			b[1].SetBounds(10, 10, 75, 23);
			panel.SuspendLayout();
			b[1].Size = new Size(75, 30);
			Assert.False(panel.LayoutEngine.Layout(panel, new LayoutEventArgs(null, null)));
			Assert.Equal(Cell(84, 3).Location, b[1].Location);
			panel.AutoSize = true;
			Assert.True(panel.LayoutEngine.Layout(panel, new LayoutEventArgs(null, null)));
		}

		[Fact]
		public void A_change_of_the_flow_raises_Layout_with_the_property_that_changed()
		{
			FlowLayoutPanel panel = Flow(400, 100);
			Button[] b = Buttons(panel, 2);
			var seen = new List<string>();
			panel.Layout += (s, e) => seen.Add(e.AffectedProperty);
			panel.FlowDirection = FlowDirection.RightToLeft;
			panel.WrapContents = false;
			panel.SetFlowBreak(b[0], true);
			panel.FlowDirection = FlowDirection.RightToLeft;
			Assert.Equal(new[] { "FlowDirection", "WrapContents", "FlowBreak" }, seen);
		}

		[Fact]
		public void A_panel_provides_FlowBreak_to_its_own_children_only()
		{
			FlowLayoutPanel panel = Flow(400, 100);
			Button[] b = Buttons(panel, 1);
			var extender = (IExtenderProvider)panel;
			Assert.True(extender.CanExtend(b[0]));
			Assert.False(extender.CanExtend(new Button()));
			Assert.False(extender.CanExtend("text"));
			Assert.Throws<ArgumentNullException>(() => panel.GetFlowBreak(null));
		}

		// ---------------------------------------------------------------- preferred size

		[Fact]
		public void The_preferred_size_is_the_one_row_when_nothing_limits_the_width()
		{
			FlowLayoutPanel panel = Flow(100, 100);
			Buttons(panel, 3);
			Assert.Equal(new Size(243, 29), panel.GetPreferredSize(Size.Empty));
		}

		[Fact]
		public void The_preferred_size_wraps_at_the_proposed_width_and_adds_the_padding()
		{
			FlowLayoutPanel panel = Flow(100, 100);
			Buttons(panel, 3);
			// 200 wide: two cells per row, 162 x 58.
			Assert.Equal(new Size(162, 58), panel.GetPreferredSize(new Size(200, 0)));
			panel.Padding = new Padding(5);
			Assert.Equal(new Size(172, 68), panel.GetPreferredSize(new Size(210, 0)));
		}

		[Fact]
		public void The_preferred_size_of_a_vertical_flow_wraps_at_the_proposed_height()
		{
			FlowLayoutPanel panel = Flow(100, 100, FlowDirection.TopDown);
			Buttons(panel, 4);
			Assert.Equal(new Size(81, 116), panel.GetPreferredSize(Size.Empty));
			Assert.Equal(new Size(162, 87), panel.GetPreferredSize(new Size(0, 100)));
		}

		[Fact]
		public void An_empty_panel_asks_for_its_padding_only_and_a_plain_panel_has_no_opinion()
		{
			var flow = new FlowLayoutPanel { Padding = new Padding(4) };
			Assert.Equal(new Size(8, 8), flow.GetPreferredSize(Size.Empty));
			Assert.Equal(Size.Empty, new Panel().GetPreferredSize(Size.Empty));
		}

		// ---------------------------------------------------------------- nested

		[Fact]
		public void A_flow_panel_can_be_a_child_of_a_flow_panel_and_a_docked_child_of_a_panel()
		{
			var outer = new Panel { Size = new Size(300, 200) };
			FlowLayoutPanel inner = Flow(10, 10);
			inner.Dock = DockStyle.Fill;
			outer.Controls.Add(inner);
			Button[] b = Buttons(inner, 4);
			Assert.Equal(new Rectangle(0, 0, 300, 200), inner.Bounds);
			Assert.Equal(Cell(165, 3), b[2].Bounds);
			outer.Width = 200;
			Assert.Equal(Cell(3, 32), b[2].Bounds);
		}
	}
}
