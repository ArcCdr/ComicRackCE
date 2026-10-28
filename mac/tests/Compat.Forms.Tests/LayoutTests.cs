using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Layout;
using cYo.Compat.Forms;
using Xunit;

namespace Compat.Forms.Tests
{
	/// <summary>
	/// Dock, Anchor, Padding, Suspend/ResumeLayout and the size limits (P4.9b). The expected rectangles are worked out by hand from the algorithm of
	/// System.Windows.Forms.Layout.DefaultLayout (docking in reverse z-order inside the display rectangle, anchors as distances to the display rectangle,
	/// stretched when both sides are anchored, moved with the far side or the middle otherwise); the numbers are in the comments.
	/// </summary>
	[Collection("Forms runtime")]
	public sealed class LayoutTests : IDisposable
	{
		public LayoutTests()
		{
			FormsRuntime.Reset();
			FormsRuntime.Backend = new NullFormsBackend { ThreadAffinity = true };
		}

		public void Dispose()
		{
			FormsRuntime.Reset();
		}

		/// <summary>A control that reports a preferred size (the measuring of real controls comes with P4.9d).</summary>
		private sealed class Measured : Button
		{
			public Size Preferred { get; set; }

			public Size LastProposal { get; private set; }

			public void UseMode(AutoSizeMode mode) => SetAutoSizeMode(mode);

			public override Size GetPreferredSize(Size proposedSize)
			{
				LastProposal = proposedSize;
				return Preferred;
			}
		}

		private static Panel PanelOf(int width, int height) => new Panel { Size = new Size(width, height) };

		private static Button Child(Control parent, int x, int y, int width, int height, AnchorStyles anchor = AnchorStyles.Top | AnchorStyles.Left)
		{
			var b = new Button { Bounds = new Rectangle(x, y, width, height), Anchor = anchor };
			parent.Controls.Add(b);
			return b;
		}

		private static Panel Docked(Control parent, DockStyle dock, int width, int height)
		{
			var p = new Panel { Size = new Size(width, height), Dock = dock };
			parent.Controls.Add(p);
			return p;
		}

		// ---------------------------------------------------------------- dock

		[Fact]
		public void A_docked_toolbar_and_a_filled_client_share_the_area()
		{
			Panel panel = PanelOf(300, 200);
			Panel fill = Docked(panel, DockStyle.Fill, 10, 10);
			Panel top = Docked(panel, DockStyle.Top, 10, 30);
			Assert.Equal(new Rectangle(0, 0, 300, 30), top.Bounds);
			Assert.Equal(new Rectangle(0, 30, 300, 170), fill.Bounds);
		}

		[Fact]
		public void The_docking_order_is_the_z_order_the_last_control_docks_first()
		{
			Panel panel = PanelOf(300, 200);
			Panel top = Docked(panel, DockStyle.Top, 10, 30);
			Panel fill = Docked(panel, DockStyle.Fill, 10, 10);
			// Fill docks first and takes everything, the toolbar docks last and lies over its upper edge.
			Assert.Equal(new Rectangle(0, 0, 300, 200), fill.Bounds);
			Assert.Equal(new Rectangle(0, 0, 300, 30), top.Bounds);
			// BringToFront puts the toolbar to index 0 (it docks last); SendToBack puts it to the end (it docks first).
			top.SendToBack();
			Assert.Equal(new Rectangle(0, 0, 300, 30), top.Bounds);
			Assert.Equal(new Rectangle(0, 30, 300, 170), fill.Bounds);
		}

		[Fact]
		public void All_five_dock_styles_cut_the_area_one_after_the_other()
		{
			Panel panel = PanelOf(300, 200);
			Panel fill = Docked(panel, DockStyle.Fill, 1, 1);
			Panel bottom = Docked(panel, DockStyle.Bottom, 1, 20);
			Panel right = Docked(panel, DockStyle.Right, 40, 1);
			Panel left = Docked(panel, DockStyle.Left, 50, 1);
			Panel top = Docked(panel, DockStyle.Top, 1, 30);
			// top (0,0,300,30); left (0,30,50,170); right (260,30,40,170); bottom (50,180,210,20); fill (50,30,210,150).
			Assert.Equal(new Rectangle(0, 0, 300, 30), top.Bounds);
			Assert.Equal(new Rectangle(0, 30, 50, 170), left.Bounds);
			Assert.Equal(new Rectangle(260, 30, 40, 170), right.Bounds);
			Assert.Equal(new Rectangle(50, 180, 210, 20), bottom.Bounds);
			Assert.Equal(new Rectangle(50, 30, 210, 150), fill.Bounds);
		}

		[Fact]
		public void The_order_of_a_side_and_a_toolbar_decides_who_reaches_the_corner()
		{
			Panel panel = PanelOf(300, 200);
			Panel fill = Docked(panel, DockStyle.Fill, 1, 1);
			Panel top = Docked(panel, DockStyle.Top, 1, 30);
			Panel left = Docked(panel, DockStyle.Left, 50, 1);
			// left docks first and takes the full height; top is cut by it.
			Assert.Equal(new Rectangle(0, 0, 50, 200), left.Bounds);
			Assert.Equal(new Rectangle(50, 0, 250, 30), top.Bounds);
			Assert.Equal(new Rectangle(50, 30, 250, 170), fill.Bounds);
		}

		[Fact]
		public void Docked_controls_follow_the_container_when_it_is_resized()
		{
			Panel panel = PanelOf(300, 200);
			Panel fill = Docked(panel, DockStyle.Fill, 1, 1);
			Panel bottom = Docked(panel, DockStyle.Bottom, 1, 20);
			panel.Size = new Size(400, 250);
			Assert.Equal(new Rectangle(0, 230, 400, 20), bottom.Bounds);
			Assert.Equal(new Rectangle(0, 0, 400, 230), fill.Bounds);
		}

		[Fact]
		public void Padding_and_DockPadding_keep_docked_controls_away_from_the_edges()
		{
			Panel panel = PanelOf(300, 200);
			panel.Padding = new Padding(10, 5, 20, 15);
			Assert.Equal(new Rectangle(10, 5, 270, 180), panel.DisplayRectangle);
			Panel fill = Docked(panel, DockStyle.Fill, 1, 1);
			Assert.Equal(new Rectangle(10, 5, 270, 180), fill.Bounds);
			Panel top = Docked(panel, DockStyle.Top, 1, 30);
			Assert.Equal(new Rectangle(10, 5, 270, 30), top.Bounds);
			Assert.Equal(new Rectangle(10, 35, 270, 150), fill.Bounds);

			panel.DockPadding.All = 4;
			Assert.Equal(new Padding(4), panel.Padding);
			Assert.Equal(4, panel.DockPadding.Left);
			Assert.Equal(new Rectangle(4, 4, 292, 30), top.Bounds);
			panel.DockPadding.Top = 0;
			Assert.Equal(new Padding(4, 0, 4, 4), panel.Padding);
			Assert.Equal(new Rectangle(4, 0, 292, 30), top.Bounds);
		}

		[Fact]
		public void Margin_does_not_change_the_dock_layout()
		{
			Panel panel = PanelOf(300, 200);
			Panel fill = Docked(panel, DockStyle.Fill, 1, 1);
			fill.Margin = new Padding(20);
			Assert.Equal(new Rectangle(0, 0, 300, 200), fill.Bounds);
		}

		[Fact]
		public void A_hidden_control_leaves_its_space_and_takes_it_back_when_shown()
		{
			Panel panel = PanelOf(300, 200);
			Panel fill = Docked(panel, DockStyle.Fill, 1, 1);
			Panel top = Docked(panel, DockStyle.Top, 1, 30);
			Assert.Equal(new Rectangle(0, 30, 300, 170), fill.Bounds);
			top.Visible = false;
			Assert.Equal(new Rectangle(0, 0, 300, 200), fill.Bounds);
			top.Visible = true;
			Assert.Equal(new Rectangle(0, 30, 300, 170), fill.Bounds);
		}

		[Fact]
		public void Dock_to_None_restores_the_bounds_the_script_set()
		{
			Panel panel = PanelOf(300, 200);
			Button b = Child(panel, 10, 20, 80, 30);
			b.Dock = DockStyle.Fill;
			Assert.Equal(new Rectangle(0, 0, 300, 200), b.Bounds);
			b.Dock = DockStyle.None;
			Assert.Equal(new Rectangle(10, 20, 80, 30), b.Bounds);
			// A part the script changes while docked is the specified part afterwards; the other parts stay.
			b.Dock = DockStyle.Left;
			b.Width = 60;
			b.Dock = DockStyle.None;
			Assert.Equal(new Rectangle(10, 20, 60, 30), b.Bounds);
		}

		[Fact]
		public void Dock_and_Anchor_exclude_each_other()
		{
			Panel panel = PanelOf(300, 200);
			Button b = Child(panel, 10, 20, 80, 30, AnchorStyles.Bottom | AnchorStyles.Right);
			b.Dock = DockStyle.Top;
			Assert.Equal(AnchorStyles.Top | AnchorStyles.Left, b.Anchor);
			Assert.Equal(new Rectangle(0, 0, 300, 30), b.Bounds);
			b.Anchor = AnchorStyles.Right;
			Assert.Equal(DockStyle.None, b.Dock);
			Assert.Equal(new Rectangle(10, 20, 80, 30), b.Bounds);
			Assert.Throws<System.ComponentModel.InvalidEnumArgumentException>(() => b.Dock = (DockStyle)7);
		}

		[Fact]
		public void Minimum_and_maximum_size_apply_to_docked_controls()
		{
			Panel panel = PanelOf(300, 200);
			Panel fill = Docked(panel, DockStyle.Fill, 1, 1);
			Panel top = Docked(panel, DockStyle.Top, 1, 30);
			top.MinimumSize = new Size(0, 50);
			Assert.Equal(new Rectangle(0, 0, 300, 50), top.Bounds);
			Assert.Equal(new Rectangle(0, 50, 300, 150), fill.Bounds);
			fill.MaximumSize = new Size(100, 0);
			Assert.Equal(new Rectangle(0, 50, 100, 150), fill.Bounds);
			// A docked control that is not AutoSize has the height it has: the old minimum stays in it.
			top.MinimumSize = Size.Empty;
			Assert.Equal(new Rectangle(0, 0, 300, 50), top.Bounds);
			Assert.Equal(new Rectangle(0, 50, 100, 150), fill.Bounds);
		}

		[Fact]
		public void Nested_containers_are_laid_out_from_the_outside_in()
		{
			Panel outer = PanelOf(300, 200);
			Panel inner = Docked(outer, DockStyle.Fill, 1, 1);
			Panel body = Docked(inner, DockStyle.Fill, 1, 1);
			Panel bar = Docked(inner, DockStyle.Bottom, 1, 25);
			Assert.Equal(new Rectangle(0, 175, 300, 25), bar.Bounds);
			Assert.Equal(new Rectangle(0, 0, 300, 175), body.Bounds);
			outer.Size = new Size(320, 220);
			Assert.Equal(new Rectangle(0, 0, 320, 220), inner.Bounds);
			Assert.Equal(new Rectangle(0, 195, 320, 25), bar.Bounds);
			Assert.Equal(new Rectangle(0, 0, 320, 195), body.Bounds);
		}

		[Fact]
		public void A_form_lays_out_in_its_client_area()
		{
			var form = new Form();
			Assert.Equal(new Size(284, 261), form.ClientSize);
			Panel status = Docked(form, DockStyle.Bottom, 1, 22);
			Panel fill = Docked(form, DockStyle.Fill, 1, 1);
			// fill was added last and docks first: it covers the whole client area and the status bar lies over its lower edge.
			Assert.Equal(new Rectangle(0, 239, 284, 22), status.Bounds);
			Assert.Equal(new Rectangle(0, 0, 284, 261), fill.Bounds);
			form.ClientSize = new Size(400, 300);
			Assert.Equal(new Rectangle(0, 278, 400, 22), status.Bounds);
			Assert.Equal(new Rectangle(0, 0, 400, 300), fill.Bounds);
		}

		// ---------------------------------------------------------------- anchor

		[Fact]
		public void Anchored_left_and_top_stays_where_it_is()
		{
			Panel panel = PanelOf(300, 200);
			Button b = Child(panel, 20, 30, 100, 40);
			panel.Size = new Size(500, 400);
			Assert.Equal(new Rectangle(20, 30, 100, 40), b.Bounds);
		}

		[Fact]
		public void Anchored_right_and_bottom_moves_with_the_far_edges()
		{
			Panel panel = PanelOf(300, 200);
			Button right = Child(panel, 20, 30, 100, 40, AnchorStyles.Top | AnchorStyles.Right);
			Button corner = Child(panel, 20, 30, 100, 40, AnchorStyles.Bottom | AnchorStyles.Right);
			Button bottom = Child(panel, 20, 30, 100, 40, AnchorStyles.Left | AnchorStyles.Bottom);
			panel.Size = new Size(400, 300);
			Assert.Equal(new Rectangle(120, 30, 100, 40), right.Bounds);
			Assert.Equal(new Rectangle(120, 130, 100, 40), corner.Bounds);
			Assert.Equal(new Rectangle(20, 130, 100, 40), bottom.Bounds);
			panel.Size = new Size(350, 250);
			Assert.Equal(new Rectangle(70, 30, 100, 40), right.Bounds);
			Assert.Equal(new Rectangle(70, 80, 100, 40), corner.Bounds);
		}

		[Fact]
		public void Anchored_on_both_sides_stretches()
		{
			Panel panel = PanelOf(300, 200);
			Button wide = Child(panel, 20, 30, 100, 40, AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right);
			Button tall = Child(panel, 20, 30, 100, 40, AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom);
			Button all = Child(panel, 20, 30, 100, 40, AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right);
			panel.Size = new Size(400, 300);
			Assert.Equal(new Rectangle(20, 30, 200, 40), wide.Bounds);
			Assert.Equal(new Rectangle(20, 30, 100, 140), tall.Bounds);
			Assert.Equal(new Rectangle(20, 30, 200, 140), all.Bounds);
		}

		[Fact]
		public void A_control_without_anchors_keeps_its_distance_to_the_middle()
		{
			Panel panel = PanelOf(300, 200);
			Button b = Child(panel, 100, 50, 100, 40, AnchorStyles.None);
			panel.Size = new Size(400, 300);
			// left = 100 - 150 + 200 = 150, top = 50 - 100 + 150 = 100.
			Assert.Equal(new Rectangle(150, 100, 100, 40), b.Bounds);
		}

		[Fact]
		public void Setting_the_anchor_after_adding_takes_the_distances_of_that_moment()
		{
			Panel panel = PanelOf(300, 200);
			var b = new Button { Bounds = new Rectangle(20, 30, 100, 40) };
			panel.Controls.Add(b);
			panel.Size = new Size(400, 300);
			Assert.Equal(new Rectangle(20, 30, 100, 40), b.Bounds);
			b.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
			panel.Size = new Size(450, 350);
			Assert.Equal(new Rectangle(70, 80, 100, 40), b.Bounds);
		}

		[Fact]
		public void Moving_an_anchored_control_updates_its_distances()
		{
			Panel panel = PanelOf(300, 200);
			Button b = Child(panel, 20, 30, 100, 40, AnchorStyles.Top | AnchorStyles.Right);
			b.Left = 50;
			panel.Width = 400;
			Assert.Equal(new Rectangle(150, 30, 100, 40), b.Bounds);
		}

		[Fact]
		public void The_distances_are_taken_when_the_control_is_added()
		{
			// The distances are taken from the container as it is when the control is added.
			Panel panel = PanelOf(300, 200);
			panel.Size = new Size(400, 300);
			Button b = Child(panel, 20, 30, 100, 40, AnchorStyles.Bottom | AnchorStyles.Right);
			panel.Size = new Size(500, 300);
			Assert.Equal(new Rectangle(120, 30, 100, 40), b.Bounds);
		}

		[Fact]
		public void A_stretched_control_gets_back_its_size_after_the_container_was_too_small()
		{
			Panel panel = PanelOf(300, 200);
			Button b = Child(panel, 20, 30, 100, 40, AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right);
			// right distance 180: at width 150 the right edge lies at -30 which is left of the left edge; the width is 0.
			panel.Width = 150;
			Assert.Equal(new Rectangle(20, 30, 0, 40), b.Bounds);
			panel.Width = 300;
			Assert.Equal(new Rectangle(20, 30, 100, 40), b.Bounds);
		}

		[Fact]
		public void The_padding_of_the_container_moves_anchored_controls_with_the_display_rectangle()
		{
			Panel panel = PanelOf(300, 200);
			Button b = Child(panel, 20, 30, 100, 40, AnchorStyles.Top | AnchorStyles.Right);
			panel.Padding = new Padding(10, 0, 30, 0);
			// The right edge of the display rectangle moved from 300 to 270 and the control keeps its distance to it: left = 20 - 30.
			Assert.Equal(new Rectangle(-10, 30, 100, 40), b.Bounds);
		}

		[Fact]
		public void Layout_is_not_run_while_it_is_suspended_and_runs_once_on_resume()
		{
			Panel panel = PanelOf(300, 200);
			Panel fill = Docked(panel, DockStyle.Fill, 1, 1);
			int layouts = 0;
			panel.Layout += (s, e) => layouts++;
			panel.SuspendLayout();
			panel.Size = new Size(400, 300);
			Panel bottom = Docked(panel, DockStyle.Bottom, 1, 20);
			Assert.Equal(new Rectangle(0, 0, 300, 200), fill.Bounds);
			Assert.Equal(0, layouts);
			panel.ResumeLayout(true);
			Assert.Equal(1, layouts);
			Assert.Equal(new Rectangle(0, 0, 400, 280), fill.Bounds);
			Assert.Equal(new Rectangle(0, 280, 400, 20), bottom.Bounds);
		}

		[Fact]
		public void ResumeLayout_false_leaves_the_children_where_they_are()
		{
			Panel panel = PanelOf(300, 200);
			Panel fill = Docked(panel, DockStyle.Fill, 1, 1);
			panel.SuspendLayout();
			panel.Size = new Size(400, 300);
			panel.ResumeLayout(false);
			Assert.Equal(new Rectangle(0, 0, 300, 200), fill.Bounds);
			panel.PerformLayout();
			Assert.Equal(new Rectangle(0, 0, 400, 300), fill.Bounds);
		}

		[Fact]
		public void Placing_the_children_does_not_start_another_layout_of_the_container()
		{
			Panel panel = PanelOf(300, 200);
			Docked(panel, DockStyle.Fill, 1, 1);
			Docked(panel, DockStyle.Top, 1, 30);
			Docked(panel, DockStyle.Left, 40, 1);
			var seen = new List<string>();
			panel.Layout += (s, e) => seen.Add(e.AffectedProperty);
			panel.Size = new Size(350, 250);
			Assert.Equal(new[] { "Bounds" }, seen);
		}

		[Fact]
		public void A_change_inside_a_Layout_handler_does_not_start_a_nested_layout()
		{
			Panel panel = PanelOf(300, 200);
			Panel fill = Docked(panel, DockStyle.Fill, 1, 1);
			int count = 0;
			panel.Layout += (s, e) =>
			{
				count++;
				fill.Width = 77;
			};
			panel.PerformLayout();
			Assert.Equal(1, count);
			// The handler ran before the engine, which docked the control again at the full width.
			Assert.Equal(300, fill.Width);
		}

		[Fact]
		public void The_container_layout_event_carries_what_changed()
		{
			Panel panel = PanelOf(300, 200);
			Panel top = Docked(panel, DockStyle.Top, 1, 30);
			var seen = new List<string>();
			panel.Layout += (s, e) => seen.Add((e.AffectedControl == top ? "top." : "?.") + e.AffectedProperty);
			top.Visible = false;
			top.Dock = DockStyle.Bottom;
			top.MinimumSize = new Size(0, 10);
			Assert.Equal(new[] { "top.Visible", "top.Dock", "top.MinimumSize" }, seen);
		}

		// ---------------------------------------------------------------- auto size

		[Fact]
		public void An_AutoSize_control_grows_to_its_preferred_size_towards_the_free_sides()
		{
			Panel panel = PanelOf(300, 200);
			var topLeft = new Measured { Bounds = new Rectangle(10, 10, 50, 20), Preferred = new Size(80, 30), AutoSize = true };
			var bottomRight = new Measured { Bounds = new Rectangle(200, 150, 50, 20), Preferred = new Size(80, 30), AutoSize = true, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
			panel.Controls.Add(topLeft);
			panel.Controls.Add(bottomRight);
			Assert.Equal(new Rectangle(10, 10, 80, 30), topLeft.Bounds);
			// grows to the left and upwards: (200 - 30, 150 - 10).
			Assert.Equal(new Rectangle(170, 140, 80, 30), bottomRight.Bounds);
		}

		[Fact]
		public void GrowOnly_does_not_shrink_and_GrowAndShrink_does()
		{
			Panel panel = PanelOf(300, 200);
			var grow = new Measured { Bounds = new Rectangle(10, 10, 100, 40), Preferred = new Size(60, 20), AutoSize = true };
			var shrink = new Measured { Bounds = new Rectangle(10, 60, 100, 40), Preferred = new Size(60, 20), AutoSize = true };
			shrink.UseMode(AutoSizeMode.GrowAndShrink);
			panel.Controls.Add(grow);
			panel.Controls.Add(shrink);
			Assert.Equal(new Size(100, 40), grow.Size);
			Assert.Equal(new Size(60, 20), shrink.Size);
		}

		[Fact]
		public void An_AutoSize_control_that_cannot_measure_itself_keeps_its_size()
		{
			Panel panel = PanelOf(300, 200);
			var b = new Button { Bounds = new Rectangle(10, 10, 100, 40), AutoSize = true };
			panel.Controls.Add(b);
			Assert.Equal(new Size(100, 40), b.Size);
		}

		[Fact]
		public void A_docked_AutoSize_control_takes_its_preferred_height_and_the_width_of_the_area()
		{
			Panel panel = PanelOf(300, 200);
			var bar = new Measured { Size = new Size(10, 99), Preferred = new Size(40, 25), AutoSize = true, Dock = DockStyle.Top };
			panel.Controls.Add(bar);
			Assert.Equal(new Rectangle(0, 0, 300, 25), bar.Bounds);
			Assert.Equal(300, bar.LastProposal.Width);
			var side = new Measured { Size = new Size(10, 99), Preferred = new Size(45, 25), AutoSize = true, Dock = DockStyle.Left };
			panel.Controls.Add(side);
			// side was added last and docks first: it takes the full height, the bar is cut by it.
			Assert.Equal(new Rectangle(0, 0, 45, 200), side.Bounds);
			Assert.Equal(new Rectangle(45, 0, 255, 25), bar.Bounds);
		}

		// ---------------------------------------------------------------- the engine object

		[Fact]
		public void A_container_has_a_layout_engine_that_lays_out_on_request()
		{
			Panel panel = PanelOf(300, 200);
			Panel fill = Docked(panel, DockStyle.Fill, 1, 1);
			LayoutEngine engine = panel.LayoutEngine;
			Assert.NotNull(engine);
			panel.SuspendLayout();
			fill.SetBounds(5, 5, 5, 5);
			Assert.Equal(new Rectangle(5, 5, 5, 5), fill.Bounds);
			Assert.False(engine.Layout(panel, new LayoutEventArgs(null, null)));
			Assert.Equal(new Rectangle(0, 0, 300, 200), fill.Bounds);
		}

		[Fact]
		public void DisplayRectangle_with_AutoScroll_covers_the_scroll_area()
		{
			var panel = PanelOf(300, 200);
			panel.AutoScroll = true;
			panel.AutoScrollMinSize = new Size(500, 100);
			Panel fill = Docked(panel, DockStyle.Fill, 1, 1);
			Assert.Equal(new Rectangle(0, 0, 500, 200), panel.DisplayRectangle);
			Assert.Equal(new Rectangle(0, 0, 500, 200), fill.Bounds);
		}

		[Fact]
		public void A_GroupBox_arranges_its_children_in_its_display_rectangle()
		{
			var group = new GroupBox { Size = new Size(200, 100) };
			Panel fill = Docked(group, DockStyle.Fill, 1, 1);
			Assert.Equal(group.DisplayRectangle, fill.Bounds);
		}
	}
}
