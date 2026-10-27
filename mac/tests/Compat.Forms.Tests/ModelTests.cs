using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using cYo.Compat.Forms;
using Xunit;

namespace Compat.Forms.Tests
{
	[CollectionDefinition("Forms runtime", DisableParallelization = true)]
	public sealed class FormsRuntimeCollection
	{
	}

	/// <summary>An ordered list of "Source.Event" entries; the expected order is compared as a list (the event traces of P4.9a).</summary>
	internal sealed class Trace
	{
		private readonly List<string> entries = new List<string>();

		public IReadOnlyList<string> Entries
		{
			get
			{
				lock (entries)
				{
					return entries.ToArray();
				}
			}
		}

		public void Add(string entry)
		{
			lock (entries)
			{
				entries.Add(entry);
			}
		}

		public void Clear()
		{
			lock (entries)
			{
				entries.Clear();
			}
		}

		public EventHandler Handler(string name) => (s, e) => Add(name);

		/// <summary>Records every event of <paramref name="c"/> under "name.Event".</summary>
		public void Watch(Control c, string name, params string[] events)
		{
			foreach (string ev in events)
			{
				switch (ev)
				{
				case "Click": c.Click += Handler(name + ".Click"); break;
				case "TextChanged": c.TextChanged += Handler(name + ".TextChanged"); break;
				case "Move": c.Move += Handler(name + ".Move"); break;
				case "LocationChanged": c.LocationChanged += Handler(name + ".LocationChanged"); break;
				case "Resize": c.Resize += Handler(name + ".Resize"); break;
				case "SizeChanged": c.SizeChanged += Handler(name + ".SizeChanged"); break;
				case "ClientSizeChanged": c.ClientSizeChanged += Handler(name + ".ClientSizeChanged"); break;
				case "VisibleChanged": c.VisibleChanged += Handler(name + ".VisibleChanged"); break;
				case "EnabledChanged": c.EnabledChanged += Handler(name + ".EnabledChanged"); break;
				case "ParentChanged": c.ParentChanged += Handler(name + ".ParentChanged"); break;
				case "HandleCreated": c.HandleCreated += Handler(name + ".HandleCreated"); break;
				case "HandleDestroyed": c.HandleDestroyed += Handler(name + ".HandleDestroyed"); break;
				case "Disposed": c.Disposed += Handler(name + ".Disposed"); break;
				case "BackColorChanged": c.BackColorChanged += Handler(name + ".BackColorChanged"); break;
				case "ForeColorChanged": c.ForeColorChanged += Handler(name + ".ForeColorChanged"); break;
				case "RightToLeftChanged": c.RightToLeftChanged += Handler(name + ".RightToLeftChanged"); break;
				case "Enter": c.Enter += Handler(name + ".Enter"); break;
				case "Leave": c.Leave += Handler(name + ".Leave"); break;
				case "GotFocus": c.GotFocus += Handler(name + ".GotFocus"); break;
				case "LostFocus": c.LostFocus += Handler(name + ".LostFocus"); break;
				case "Validating": c.Validating += (s, e) => Add(name + ".Validating"); break;
				case "Validated": c.Validated += Handler(name + ".Validated"); break;
				case "Layout": c.Layout += (s, e) => Add(name + ".Layout"); break;
				case "ControlAdded": c.ControlAdded += (s, e) => Add(name + ".ControlAdded(" + e.Control.Name + ")"); break;
				case "ControlRemoved": c.ControlRemoved += (s, e) => Add(name + ".ControlRemoved(" + e.Control.Name + ")"); break;
				case "TabIndexChanged": c.TabIndexChanged += Handler(name + ".TabIndexChanged"); break;
				case "DockChanged": c.DockChanged += Handler(name + ".DockChanged"); break;
				default: throw new ArgumentException("unknown event " + ev);
				}
			}
		}

		public void Watch(Form f, string name)
		{
			f.Load += Handler(name + ".Load");
			f.Shown += Handler(name + ".Shown");
			f.Activated += Handler(name + ".Activated");
			f.Deactivate += Handler(name + ".Deactivate");
			f.FormClosing += (s, e) => Add(name + ".FormClosing(" + e.CloseReason + ")");
			f.FormClosed += (s, e) => Add(name + ".FormClosed(" + e.CloseReason + ")");
			f.Closing += (s, e) => Add(name + ".Closing");
			f.Closed += (s, e) => Add(name + ".Closed");
		}
	}

	/// <summary>
	/// The object model of P4.9a on the null backend: state, hierarchy, and the events a change raises, as ordered lists. The orders are those of dotnet/winforms (Control, Form,
	/// ControlCollection, CheckBox, RadioButton, ComboBox, NumericUpDown ...); the Windows recorder of P4.9g compares them with real traces.
	/// </summary>
	[Collection("Forms runtime")]
	public sealed class ModelTests : IDisposable
	{
		private readonly NullFormsBackend backend;
		private readonly Trace trace = new Trace();
		private readonly System.Globalization.CultureInfo culture;

		public ModelTests()
		{
			culture = Thread.CurrentThread.CurrentCulture;
			Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
			FormsRuntime.Reset();
			backend = new NullFormsBackend { ThreadAffinity = true };
			FormsRuntime.Backend = backend;
		}

		public void Dispose()
		{
			FormsRuntime.Reset();
			Thread.CurrentThread.CurrentCulture = culture;
		}

		// ---------------------------------------------------------------- state of a control

		[Fact]
		public void A_new_control_has_the_defaults_of_the_framework()
		{
			var c = new Control();
			Assert.Equal(string.Empty, c.Text);
			Assert.Equal(string.Empty, c.Name);
			Assert.True(c.Visible);
			Assert.True(c.Enabled);
			Assert.True(c.TabStop);
			Assert.Equal(0, c.TabIndex);
			Assert.Equal(AnchorStyles.Top | AnchorStyles.Left, c.Anchor);
			Assert.Equal(DockStyle.None, c.Dock);
			Assert.Equal(new Padding(3), c.Margin);
			Assert.Equal(Padding.Empty, c.Padding);
			Assert.Equal(Size.Empty, c.Size);
			Assert.False(c.IsHandleCreated);
			Assert.False(c.Created);
			Assert.False(c.IsDisposed);
			Assert.Null(c.Parent);
			Assert.False(c.HasChildren);
			Assert.True(c.CausesValidation);
			Assert.Equal(Color.FromArgb(240, 240, 240), c.BackColor);
			Assert.Equal(Color.FromArgb(0, 0, 0), c.ForeColor);
			Assert.Equal(RightToLeft.No, c.RightToLeft);
		}

		[Fact]
		public void The_constructors_with_text_and_bounds_set_them_and_a_parent_adopts_the_control()
		{
			var panel = new Panel();
			var c = new Control(panel, "caption", 10, 20, 30, 40);
			Assert.Equal("caption", c.Text);
			Assert.Equal(new Rectangle(10, 20, 30, 40), c.Bounds);
			Assert.Same(panel, c.Parent);
			Assert.True(panel.Controls.Contains(c));
			Assert.Equal("t", new Control("t").Text);
		}

		[Fact]
		public void Text_raises_TextChanged_once_per_change_and_not_for_the_same_value()
		{
			var c = new Control();
			trace.Watch(c, "c", "TextChanged");
			c.Text = "a";
			c.Text = "a";
			c.Text = null;
			Assert.Equal(new[] { "c.TextChanged", "c.TextChanged" }, trace.Entries);
			Assert.Equal(string.Empty, c.Text);
		}

		[Fact]
		public void A_location_change_raises_Move_then_LocationChanged_and_a_size_change_Resize_SizeChanged_ClientSizeChanged()
		{
			var c = new Control { Size = new Size(10, 10) };
			trace.Watch(c, "c", "Move", "LocationChanged", "Resize", "SizeChanged", "ClientSizeChanged");
			c.Location = new Point(5, 6);
			Assert.Equal(new[] { "c.Move", "c.LocationChanged" }, trace.Entries);
			trace.Clear();
			c.Size = new Size(20, 30);
			Assert.Equal(new[] { "c.Resize", "c.SizeChanged", "c.ClientSizeChanged" }, trace.Entries);
			trace.Clear();
			c.Bounds = new Rectangle(1, 2, 40, 50);
			Assert.Equal(new[] { "c.Move", "c.LocationChanged", "c.Resize", "c.SizeChanged", "c.ClientSizeChanged" }, trace.Entries);
			trace.Clear();
			c.Bounds = new Rectangle(1, 2, 40, 50);
			Assert.Empty(trace.Entries);
		}

		[Fact]
		public void The_bounds_properties_agree()
		{
			var c = new Control();
			c.SetBounds(10, 20, 30, 40);
			Assert.Equal((10, 20, 30, 40, 40, 60), (c.Left, c.Top, c.Width, c.Height, c.Right, c.Bottom));
			c.Width = 100;
			c.Left = 1;
			Assert.Equal(new Rectangle(1, 20, 100, 40), c.Bounds);
			c.SetBounds(0, 0, 5, 5, BoundsSpecified.Size);
			Assert.Equal(new Rectangle(1, 20, 5, 5), c.Bounds);
			Assert.Equal(new Rectangle(0, 0, 5, 5), c.ClientRectangle);
			c.MinimumSize = new Size(50, 50);
			Assert.Equal(new Size(50, 50), c.Size);
			c.MaximumSize = new Size(60, 60);
			c.Size = new Size(200, 200);
			Assert.Equal(new Size(60, 60), c.Size);
		}

		[Fact]
		public void A_form_has_the_frame_of_the_framework_between_its_size_and_its_client_size()
		{
			var f = new Form();
			Assert.Equal(new Size(300, 300), f.Size);
			Assert.Equal(new Size(284, 261), f.ClientSize);
			f.ClientSize = new Size(243, 154);
			Assert.Equal(new Size(259, 193), f.Size);
			f.FormBorderStyle = FormBorderStyle.FixedDialog;
			Assert.Equal(new Size(243, 154), f.ClientSize);
			f.FormBorderStyle = FormBorderStyle.None;
			Assert.Equal(new Size(243, 154), f.Size);
		}

		[Fact]
		public void Points_convert_between_client_and_screen_coordinates_through_the_chain_of_parents()
		{
			var f = new Form { Location = new Point(100, 100) };
			var p = new Panel { Location = new Point(10, 20) };
			var b = new Button { Location = new Point(5, 5) };
			f.Controls.Add(p);
			p.Controls.Add(b);
			Assert.Equal(new Point(100 + 8 + 10 + 5, 100 + 31 + 20 + 5), b.PointToScreen(Point.Empty));
			Assert.Equal(Point.Empty, b.PointToClient(b.PointToScreen(Point.Empty)));
		}

		// ---------------------------------------------------------------- hierarchy

		[Fact]
		public void Adding_a_control_raises_ParentChanged_of_the_child_then_ControlAdded_of_the_parent()
		{
			var panel = new Panel { Name = "panel" };
			var b = new Button { Name = "b" };
			trace.Watch(panel, "panel", "ControlAdded", "ControlRemoved", "Layout");
			trace.Watch(b, "b", "ParentChanged");
			panel.Controls.Add(b);
			Assert.Equal(new[] { "b.ParentChanged", "panel.Layout", "panel.ControlAdded(b)" }, trace.Entries);
			Assert.Same(panel, b.Parent);
			trace.Clear();
			panel.Controls.Remove(b);
			Assert.Equal(new[] { "b.ParentChanged", "panel.Layout", "panel.ControlRemoved(b)" }, trace.Entries);
			Assert.Null(b.Parent);
		}

		[Fact]
		public void Setting_Parent_adds_to_the_collection_and_moving_between_parents_removes_from_the_old_one()
		{
			var a = new Panel { Name = "a" };
			var b = new Panel { Name = "b" };
			var c = new Label { Name = "c" };
			c.Parent = a;
			Assert.Equal(1, a.Controls.Count);
			trace.Watch(a, "a", "ControlRemoved");
			trace.Watch(b, "b", "ControlAdded");
			c.Parent = b;
			Assert.Equal(new[] { "a.ControlRemoved(c)", "b.ControlAdded(c)" }, trace.Entries);
			Assert.Equal(0, a.Controls.Count);
			c.Parent = null;
			Assert.Equal(0, b.Controls.Count);
		}

		[Fact]
		public void Tab_indexes_are_assigned_in_order_of_adding_and_an_explicit_one_is_kept()
		{
			var panel = new Panel();
			var a = new Button();
			var b = new Button();
			var c = new Button { TabIndex = 7 };
			panel.Controls.AddRange(new Control[] { a, b, c });
			Assert.Equal(new[] { 0, 1, 7 }, new[] { a.TabIndex, b.TabIndex, c.TabIndex });
			var d = new Button();
			panel.Controls.Add(d);
			Assert.Equal(8, d.TabIndex);
			Assert.Throws<ArgumentOutOfRangeException>(() => a.TabIndex = -1);
		}

		[Fact]
		public void Adding_a_child_again_moves_it_to_the_back_and_the_z_order_methods_reorder()
		{
			var panel = new Panel();
			Control a = new Button { Name = "a" }, b = new Button { Name = "b" }, c = new Button { Name = "c" };
			panel.Controls.AddRange(new[] { a, b, c });
			Assert.Equal(new[] { "a", "b", "c" }, panel.Controls.Cast<Control>().Select(x => x.Name));
			panel.Controls.Add(a);
			Assert.Equal(new[] { "b", "c", "a" }, panel.Controls.Cast<Control>().Select(x => x.Name));
			a.BringToFront();
			Assert.Equal(new[] { "a", "b", "c" }, panel.Controls.Cast<Control>().Select(x => x.Name));
			a.SendToBack();
			Assert.Equal(new[] { "b", "c", "a" }, panel.Controls.Cast<Control>().Select(x => x.Name));
			panel.Controls.SetChildIndex(c, 0);
			Assert.Equal(new[] { "c", "b", "a" }, panel.Controls.Cast<Control>().Select(x => x.Name));
			Assert.Equal(2, panel.Controls.GetChildIndex(a));
			Assert.Throws<ArgumentException>(() => panel.Controls.GetChildIndex(new Button()));
		}

		[Fact]
		public void The_collection_looks_children_up_by_name_and_refuses_cycles_and_top_level_controls()
		{
			var form = new Form();
			var panel = new Panel { Name = "Panel" };
			var inner = new TextBox { Name = "Inner" };
			form.Controls.Add(panel);
			panel.Controls.Add(inner);
			Assert.Same(panel, form.Controls["panel"]);
			Assert.Same(inner, panel.Controls["INNER"]);
			Assert.Null(form.Controls["Inner"]);
			Assert.True(form.Controls.ContainsKey("Panel"));
			Assert.Equal(new Control[] { inner }, form.Controls.Find("Inner", true));
			Assert.Empty(form.Controls.Find("Inner", false));
			Assert.Equal(0, form.Controls.IndexOfKey("panel"));
			Assert.Throws<ArgumentException>(() => panel.Controls.Add(form));
			Assert.Throws<ArgumentException>(() => inner.Controls.Add(panel));
			Assert.Throws<ArgumentException>(() => panel.Controls.Add(panel));
			form.Controls.RemoveByKey("panel");
			Assert.Equal(0, form.Controls.Count);
			Assert.True(panel.Contains(inner));
			Assert.False(form.Contains(inner));
		}

		[Fact]
		public void Clear_removes_every_child_and_resumes_the_layout_once()
		{
			var panel = new Panel { Name = "panel" };
			panel.Controls.AddRange(new Control[] { new Label { Name = "x" }, new Label { Name = "y" } });
			trace.Watch(panel, "panel", "ControlRemoved", "Layout");
			panel.Controls.Clear();
			Assert.Equal(new[] { "panel.ControlRemoved(y)", "panel.ControlRemoved(x)", "panel.Layout" }, trace.Entries);
		}

		[Fact]
		public void FindForm_TopLevelControl_and_ParentForm_walk_up()
		{
			var form = new Form();
			var panel = new UserControl();
			var button = new Button();
			form.Controls.Add(panel);
			panel.Controls.Add(button);
			Assert.Same(form, button.FindForm());
			Assert.Same(form, form.FindForm());
			Assert.Same(form, button.TopLevelControl);
			Assert.Same(form, panel.ParentForm);
			Assert.Null(form.ParentForm);
			Assert.Same(panel, button.GetContainerControl());
		}

		// ---------------------------------------------------------------- Visible, Enabled and the ambient properties

		[Fact]
		public void Visible_of_a_child_follows_its_parents_but_the_flag_is_its_own()
		{
			var form = new Form();
			var child = new Button();
			form.Controls.Add(child);
			Assert.False(form.Visible);
			Assert.False(child.Visible);
			form.Visible = true;
			Assert.True(child.Visible);
			child.Visible = false;
			form.Hide();
			form.Show();
			Assert.False(child.Visible);
			child.Visible = true;
			Assert.True(child.Visible);
		}

		[Fact]
		public void EnabledChanged_goes_to_the_children_that_are_enabled_themselves()
		{
			var panel = new Panel { Name = "panel" };
			var on = new Button { Name = "on" };
			var off = new Button { Name = "off", Enabled = false };
			panel.Controls.AddRange(new Control[] { on, off });
			trace.Watch(panel, "panel", "EnabledChanged");
			trace.Watch(on, "on", "EnabledChanged");
			trace.Watch(off, "off", "EnabledChanged");
			panel.Enabled = false;
			Assert.Equal(new[] { "panel.EnabledChanged", "on.EnabledChanged" }, trace.Entries);
			Assert.False(on.Enabled);
			Assert.False(off.Enabled);
			trace.Clear();
			panel.Enabled = false;
			Assert.Empty(trace.Entries);
			panel.Enabled = true;
			Assert.True(on.Enabled);
			Assert.False(off.Enabled);
		}

		[Fact]
		public void Colors_are_inherited_until_a_control_sets_its_own_and_the_children_hear_about_it()
		{
			var panel = new Panel { Name = "panel" };
			var inherits = new Label { Name = "inherits" };
			var own = new Label { Name = "own", ForeColor = Color.Red };
			panel.Controls.AddRange(new Control[] { inherits, own });
			trace.Watch(inherits, "inherits", "ForeColorChanged", "BackColorChanged");
			trace.Watch(own, "own", "ForeColorChanged", "BackColorChanged");
			panel.ForeColor = Color.Blue;
			Assert.Equal(new[] { "inherits.ForeColorChanged" }, trace.Entries);
			Assert.Equal(Color.Blue, inherits.ForeColor);
			Assert.Equal(Color.Red, own.ForeColor);
			trace.Clear();
			panel.BackColor = Color.Yellow;
			Assert.Equal(new[] { "inherits.BackColorChanged", "own.BackColorChanged" }, trace.Entries);
			Assert.Equal(Color.Yellow, own.BackColor);
			panel.ResetBackColor();
			Assert.Equal(Control.DefaultBackColor, inherits.BackColor);
		}

		[Fact]
		public void A_control_that_moves_to_a_parent_takes_the_ambient_values_of_the_new_parent()
		{
			var panel = new Panel { ForeColor = Color.Green, RightToLeft = RightToLeft.Yes };
			var c = new Label { Name = "c" };
			trace.Watch(c, "c", "ParentChanged", "ForeColorChanged", "RightToLeftChanged", "BackColorChanged");
			panel.Controls.Add(c);
			Assert.Equal(new[] { "c.ParentChanged", "c.ForeColorChanged", "c.RightToLeftChanged" }, trace.Entries);
			Assert.Equal(RightToLeft.Yes, c.RightToLeft);
		}

		[Fact]
		public void Text_boxes_and_lists_have_the_window_colors_and_do_not_inherit_the_form_colors()
		{
			var form = new Form { BackColor = Color.Gray, ForeColor = Color.White };
			var box = new TextBox();
			var list = new ListBox();
			var label = new Label();
			form.Controls.AddRange(new Control[] { box, list, label });
			Assert.Equal(Color.FromArgb(255, 255, 255), box.BackColor);
			Assert.Equal(Color.FromArgb(0, 0, 0), list.ForeColor);
			Assert.Equal(Color.Gray, label.BackColor);
			Assert.Equal(Color.White, label.ForeColor);
			box.ReadOnly = true;
			Assert.Equal(Control.DefaultBackColor, box.BackColor);
		}

		// ---------------------------------------------------------------- handles, show and close

		[Fact]
		public void Showing_a_form_raises_the_events_in_the_order_of_the_framework()
		{
			var form = new Form { Name = "form" };
			var button = new Button { Name = "button" };
			form.Controls.Add(button);
			trace.Watch(form, "form");
			trace.Watch(form, "form", "HandleCreated", "VisibleChanged");
			trace.Watch(button, "button", "HandleCreated", "VisibleChanged");
			bool visibleInLoad = true;
			form.Load += (s, e) => visibleInLoad = form.Visible;
			form.Show();
			Assert.False(visibleInLoad, "Visible is still false in Load");
			Assert.Equal(new[]
			{
				"form.HandleCreated", "button.HandleCreated",
				"form.Load",
				"form.VisibleChanged", "button.VisibleChanged",
				"form.Activated",
			}, trace.Entries);
			Assert.True(form.Visible);
			Assert.True(form.IsHandleCreated && button.IsHandleCreated && form.Created && button.Created);
			Assert.Same(form, Form.ActiveForm);
			backend.Pump();
			Assert.Equal("form.Shown", trace.Entries.Last());
			form.Hide();
			form.Show();
			backend.Pump();
			Assert.Equal(1, trace.Entries.Count(e => e == "form.Load"));
			Assert.Equal(1, trace.Entries.Count(e => e == "form.Shown"));
			Assert.Equal(2, trace.Entries.Count(e => e == "form.Activated"));
		}

		[Fact]
		public void The_peers_are_created_parent_first_and_told_about_changes()
		{
			var form = new Form();
			var button = new Button();
			form.Controls.Add(button);
			form.Text = "before";
			Assert.Empty(backend.Peers);
			form.Show();
			Assert.Equal(new Control[] { form, button }, backend.Peers.Select(p => p.Control).ToArray());
			NullPeer formPeer = backend.Peers[0];
			NullPeer buttonPeer = backend.Peers[1];
			Assert.Contains("Show", formPeer.Log);
			Assert.Contains("Activate", formPeer.Log);
			button.Text = "OK";
			button.Enabled = false;
			Assert.Contains("PropertyChanged Text", buttonPeer.Log);
			Assert.Contains("PropertyChanged Enabled", buttonPeer.Log);
			form.Controls.Add(new Label());
			Assert.Contains("ChildrenChanged", formPeer.Log);
			Assert.Equal(3, backend.Peers.Count);
		}

		[Fact]
		public void A_change_from_another_thread_reaches_the_peer_on_the_ui_thread()
		{
			var form = new Form();
			var button = new Button();
			form.Controls.Add(button);
			form.Show();
			NullPeer peer = backend.Peers[1];
			int writer = -1;
			var thread = new Thread(() =>
			{
				writer = Environment.CurrentManagedThreadId;
				button.Text = "from a worker";
			});
			thread.Start();
			thread.Join();
			Assert.Equal("from a worker", button.Text);
			Assert.DoesNotContain("PropertyChanged Text", peer.Log);
			int pumped = backend.Pump();
			Assert.True(pumped >= 1, string.Join(",", peer.Log));
			Assert.Equal(1, peer.Log.Count(l => l == "PropertyChanged Text"));
			Assert.Contains("PropertyChanged Text", peer.Log);
			Assert.NotEqual(writer, Environment.CurrentManagedThreadId);
		}

		[Fact]
		public void Close_raises_FormClosing_then_FormClosed_and_disposes_a_modeless_form()
		{
			var form = new Form { Name = "form" };
			var button = new Button { Name = "button" };
			form.Controls.Add(button);
			trace.Watch(form, "form");
			trace.Watch(form, "form", "HandleDestroyed", "Disposed");
			trace.Watch(button, "button", "HandleDestroyed", "Disposed");
			form.Show();
			backend.Pump();
			trace.Clear();
			form.Close();
			Assert.Equal(new[]
			{
				"form.Closing", "form.FormClosing(UserClosing)",
				"form.Closed", "form.FormClosed(UserClosing)",
				"form.Deactivate",
				"form.HandleDestroyed", "button.HandleDestroyed", "button.Disposed", "form.Disposed",
			}, trace.Entries);
			Assert.True(form.IsDisposed);
			Assert.True(button.IsDisposed);
			Assert.Null(Form.ActiveForm);
			Assert.True(backend.Peers.All(p => p.Disposed));
		}

		[Fact]
		public void A_cancelled_FormClosing_keeps_the_form_open()
		{
			var form = new Form { Name = "form" };
			trace.Watch(form, "form");
			form.FormClosing += (s, e) => e.Cancel = true;
			form.Show();
			backend.Pump();
			trace.Clear();
			form.Close();
			Assert.Equal(new[] { "form.Closing", "form.FormClosing(UserClosing)" }, trace.Entries);
			Assert.False(form.IsDisposed);
			Assert.True(form.Visible);
			Assert.Same(form, Form.ActiveForm);
		}

		[Fact]
		public void Closing_a_form_that_was_never_shown_only_disposes_it()
		{
			var form = new Form { Name = "form" };
			trace.Watch(form, "form");
			form.Close();
			Assert.Empty(trace.Entries);
			Assert.True(form.IsDisposed);
			Assert.Throws<ObjectDisposedException>(() => form.Show());
		}

		[Fact]
		public void Showing_a_second_form_deactivates_the_first()
		{
			var first = new Form { Name = "first" };
			var second = new Form { Name = "second" };
			trace.Watch(first, "first");
			trace.Watch(second, "second");
			first.Show();
			trace.Clear();
			second.Show();
			Assert.Equal(new[] { "first.Deactivate", "second.Load", "second.Activated" }.OrderBy(x => x).ToArray(), trace.Entries.OrderBy(x => x).ToArray());
			Assert.Equal("first.Deactivate", trace.Entries.Single(e => e.EndsWith("Deactivate")));
			Assert.True(trace.Entries.ToList().IndexOf("first.Deactivate") < trace.Entries.ToList().IndexOf("second.Activated"));
			Assert.Same(second, Form.ActiveForm);
			first.Activate();
			Assert.Same(first, Form.ActiveForm);
			Assert.Equal("first.Activated", trace.Entries.Last());
		}

		[Fact]
		public void Closing_an_owner_closes_its_owned_forms_first_and_StartPosition_centres_the_form()
		{
			var owner = new Form { Name = "owner" };
			var owned = new Form { Name = "owned", Owner = owner, StartPosition = FormStartPosition.CenterScreen };
			owner.Show();
			owned.Show();
			Assert.Equal(new Point((1920 - 300) / 2, (1080 - 300) / 2), owned.Location);
			Assert.Equal(new[] { owned }, owner.OwnedForms);
			trace.Watch(owner, "owner");
			trace.Watch(owned, "owned");
			trace.Clear();
			owner.Close();
			var list = trace.Entries.ToList();
			Assert.True(list.IndexOf("owned.FormClosing(FormOwnerClosing)") < list.IndexOf("owner.FormClosing(UserClosing)"));
			Assert.True(owned.IsDisposed && owner.IsDisposed);
		}

		[Fact]
		public void Dispose_removes_a_control_from_its_parent()
		{
			var panel = new Panel { Name = "panel" };
			var b = new Button { Name = "b" };
			panel.Controls.Add(b);
			trace.Watch(panel, "panel", "ControlRemoved");
			b.Dispose();
			Assert.Equal(new[] { "panel.ControlRemoved(b)" }, trace.Entries);
			Assert.Equal(0, panel.Controls.Count);
			Assert.True(b.IsDisposed);
		}

		// ---------------------------------------------------------------- layout counters

		[Fact]
		public void SuspendLayout_defers_the_Layout_event_until_ResumeLayout()
		{
			var panel = new Panel { Name = "panel" };
			trace.Watch(panel, "panel", "Layout");
			panel.SuspendLayout();
			panel.PerformLayout();
			panel.PerformLayout();
			Assert.Empty(trace.Entries);
			panel.ResumeLayout(false);
			Assert.Empty(trace.Entries);
			panel.SuspendLayout();
			panel.PerformLayout();
			panel.ResumeLayout(true);
			Assert.Equal(new[] { "panel.Layout" }, trace.Entries);
			panel.SuspendLayout();
			panel.SuspendLayout();
			panel.PerformLayout();
			panel.ResumeLayout();
			Assert.Equal(1, trace.Entries.Count);
			panel.ResumeLayout();
			Assert.Equal(2, trace.Entries.Count);
		}

		[Fact]
		public void The_Layout_event_carries_the_affected_control_and_property()
		{
			var panel = new Panel();
			var b = new Button();
			panel.Controls.Add(b);
			LayoutEventArgs seen = null;
			panel.Layout += (s, e) => seen = e;
			b.Dock = DockStyle.Fill;
			Assert.Same(b, seen.AffectedControl);
			Assert.Equal("Dock", seen.AffectedProperty);
			b.Location = new Point(3, 3);
			Assert.Equal("Bounds", seen.AffectedProperty);
		}

		// ---------------------------------------------------------------- focus

		private (Form form, TextBox a, TextBox b) FocusForm()
		{
			var form = new Form { Name = "form" };
			var a = new TextBox { Name = "a" };
			var b = new TextBox { Name = "b" };
			form.Controls.AddRange(new Control[] { a, b });
			form.Show();
			backend.Pump();
			return (form, a, b);
		}

		[Fact]
		public void Showing_a_form_focuses_its_first_control_in_tab_order()
		{
			var (form, a, b) = FocusForm();
			Assert.True(a.Focused);
			Assert.False(b.Focused);
			Assert.Same(a, form.ActiveControl);
			Assert.True(form.ContainsFocus);
		}

		[Fact]
		public void Focus_raises_Enter_GotFocus_on_the_new_control_then_LostFocus_Leave_Validating_Validated_on_the_old_one()
		{
			var (form, a, b) = FocusForm();
			trace.Watch(a, "a", "Enter", "GotFocus", "Leave", "LostFocus", "Validating", "Validated");
			trace.Watch(b, "b", "Enter", "GotFocus", "Leave", "LostFocus", "Validating", "Validated");
			Assert.True(b.Focus());
			Assert.Equal(new[] { "b.Enter", "b.GotFocus", "a.LostFocus", "a.Leave", "a.Validating", "a.Validated" }, trace.Entries);
			Assert.True(b.Focused);
			Assert.False(a.Focused);
		}

		[Fact]
		public void Select_raises_Enter_GotFocus_then_Leave_Validating_Validated_LostFocus()
		{
			var (form, a, b) = FocusForm();
			trace.Watch(a, "a", "Enter", "GotFocus", "Leave", "LostFocus", "Validating", "Validated");
			trace.Watch(b, "b", "Enter", "GotFocus", "Leave", "LostFocus", "Validating", "Validated");
			b.Select();
			Assert.Equal(new[] { "b.Enter", "b.GotFocus", "a.Leave", "a.Validating", "a.Validated", "a.LostFocus" }, trace.Entries);
			Assert.Same(b, form.ActiveControl);
		}

		[Fact]
		public void A_cancelled_Validating_keeps_the_focus_on_the_old_control()
		{
			var (form, a, b) = FocusForm();
			a.Validating += (s, e) => e.Cancel = true;
			Assert.False(b.Focus());
			Assert.True(a.Focused);
			Assert.False(b.Focused);
			b.CausesValidation = false;
			Assert.True(b.Focus());
			Assert.True(b.Focused);
		}

		[Fact]
		public void A_disabled_or_hidden_control_cannot_be_focused_and_loses_the_focus()
		{
			var (form, a, b) = FocusForm();
			Assert.True(a.Focused);
			a.Enabled = false;
			Assert.False(a.Focused);
			Assert.False(a.Focus());
			Assert.False(a.CanFocus);
			b.Visible = false;
			Assert.False(b.CanFocus);
			Assert.False(new TextBox().Focus());
		}

		[Fact]
		public void Enter_and_Leave_run_through_the_containers_that_gain_or_lose_the_focus()
		{
			var form = new Form { Name = "form" };
			var panel = new Panel { Name = "panel" };
			var inner = new TextBox { Name = "inner" };
			var outer = new TextBox { Name = "outer" };
			form.Controls.AddRange(new Control[] { outer, panel });
			panel.Controls.Add(inner);
			form.Show();
			outer.Select();
			trace.Watch(panel, "panel", "Enter", "Leave");
			trace.Watch(inner, "inner", "Enter", "Leave");
			trace.Watch(outer, "outer", "Enter", "Leave");
			inner.Select();
			Assert.Equal(new[] { "panel.Enter", "inner.Enter", "outer.Leave" }, trace.Entries);
			trace.Clear();
			outer.Select();
			Assert.Equal(new[] { "outer.Enter", "inner.Leave", "panel.Leave" }, trace.Entries);
		}

		[Fact]
		public void SelectNextControl_walks_the_tab_order()
		{
			var form = new Form();
			var a = new TextBox { Name = "a", TabIndex = 2 };
			var b = new TextBox { Name = "b", TabIndex = 0 };
			var c = new Label { Name = "c", TabIndex = 1 };
			form.Controls.AddRange(new Control[] { a, b, c });
			form.Show();
			Assert.Same(b, form.ActiveControl);
			Assert.True(form.SelectNextControl(b, true, true, true, false));
			Assert.Same(a, form.ActiveControl);
			Assert.False(form.SelectNextControl(a, true, true, true, false));
			Assert.True(form.SelectNextControl(a, true, true, true, true));
			Assert.Same(b, form.ActiveControl);
			Assert.Same(a, form.GetNextControl(c, true));
			Assert.Same(b, form.GetNextControl(c, false));
		}

		[Fact]
		public void ActiveControl_must_be_a_descendant()
		{
			var form = new Form();
			var inside = new TextBox();
			var outside = new TextBox();
			form.Controls.Add(inside);
			form.ActiveControl = inside;
			Assert.Same(inside, form.ActiveControl);
			Assert.Throws<ArgumentException>(() => form.ActiveControl = outside);
			form.Controls.Remove(inside);
			Assert.Null(form.ActiveControl);
		}

		// ---------------------------------------------------------------- the buttons

		[Fact]
		public void A_click_on_a_button_sets_the_DialogResult_of_the_form_before_Click_is_raised()
		{
			var form = new Form();
			var ok = new Button { DialogResult = DialogResult.OK };
			form.Controls.Add(ok);
			DialogResult atClick = DialogResult.None;
			ok.Click += (s, e) => atClick = form.DialogResult;
			form.Show();
			ok.PerformClick();
			Assert.Equal(DialogResult.OK, atClick);
			Assert.Equal(DialogResult.OK, form.DialogResult);
		}

		[Fact]
		public void PerformClick_does_nothing_for_a_button_that_cannot_be_selected()
		{
			var form = new Form();
			var b = new Button();
			form.Controls.Add(b);
			int clicks = 0;
			b.Click += (s, e) => clicks++;
			b.PerformClick();
			Assert.Equal(0, clicks);
			form.Show();
			b.PerformClick();
			Assert.Equal(1, clicks);
			b.Enabled = false;
			b.PerformClick();
			Assert.Equal(1, clicks);
		}

		[Fact]
		public void A_check_box_raises_CheckedChanged_and_CheckStateChanged_also_for_a_change_by_the_script()
		{
			var box = new CheckBox { Name = "box" };
			box.CheckedChanged += trace.Handler("box.CheckedChanged");
			box.CheckStateChanged += trace.Handler("box.CheckStateChanged");
			box.Checked = true;
			Assert.Equal(new[] { "box.CheckedChanged", "box.CheckStateChanged" }, trace.Entries);
			trace.Clear();
			box.Checked = true;
			Assert.Empty(trace.Entries);
			box.CheckState = CheckState.Indeterminate;
			Assert.Equal(new[] { "box.CheckStateChanged" }, trace.Entries);
			Assert.True(box.Checked);
			trace.Clear();
			box.CheckState = CheckState.Unchecked;
			Assert.Equal(new[] { "box.CheckedChanged", "box.CheckStateChanged" }, trace.Entries);
		}

		[Fact]
		public void A_click_on_a_check_box_changes_the_state_before_Click()
		{
			var form = new Form();
			var box = new CheckBox { ThreeState = true };
			form.Controls.Add(box);
			bool checkedAtClick = false;
			box.Click += (s, e) => checkedAtClick = box.Checked;
			box.CheckedChanged += trace.Handler("CheckedChanged");
			box.Click += trace.Handler("Click");
			form.Show();
			box.PeerClick();
			Assert.True(checkedAtClick);
			Assert.Equal(new[] { "CheckedChanged", "Click" }, trace.Entries);
			box.PeerClick();
			Assert.Equal(CheckState.Indeterminate, box.CheckState);
			box.PeerClick();
			Assert.Equal(CheckState.Unchecked, box.CheckState);
			box.AutoCheck = false;
			box.PeerClick();
			Assert.Equal(CheckState.Unchecked, box.CheckState);
		}

		[Fact]
		public void Checking_a_radio_button_unchecks_the_others_of_its_container_first()
		{
			var panel = new Panel();
			var a = new RadioButton { Name = "a", Checked = true };
			var b = new RadioButton { Name = "b" };
			var other = new Panel();
			var c = new RadioButton { Name = "c", Checked = true };
			panel.Controls.AddRange(new Control[] { a, b });
			other.Controls.Add(c);
			a.CheckedChanged += trace.Handler("a.CheckedChanged");
			b.CheckedChanged += trace.Handler("b.CheckedChanged");
			c.CheckedChanged += trace.Handler("c.CheckedChanged");
			b.Checked = true;
			Assert.Equal(new[] { "a.CheckedChanged", "b.CheckedChanged" }, trace.Entries);
			Assert.False(a.Checked);
			Assert.True(b.Checked);
			Assert.True(c.Checked);
		}

		// ---------------------------------------------------------------- text, lists, numbers

		[Fact]
		public void A_text_box_raises_TextChanged_for_the_script_and_keeps_selection_and_Modified()
		{
			var t = new TextBox { Name = "t" };
			trace.Watch(t, "t", "TextChanged");
			t.ModifiedChanged += trace.Handler("t.ModifiedChanged");
			t.Text = "hello world";
			Assert.Equal(new[] { "t.TextChanged" }, trace.Entries);
			Assert.False(t.Modified);
			t.Select(6, 5);
			Assert.Equal("world", t.SelectedText);
			t.SelectedText = "there";
			Assert.Equal("hello there", t.Text);
			Assert.True(t.Modified);
			Assert.Equal(11, t.SelectionStart);
			Assert.Equal(0, t.SelectionLength);
			t.AppendText("!");
			Assert.Equal("hello there!", t.Text);
			t.Lines = new[] { "one", "two" };
			Assert.Equal("one\r\ntwo", t.Text);
			Assert.Equal(new[] { "one", "two" }, t.Lines);
			t.Clear();
			Assert.Equal(string.Empty, t.Text);
			Assert.False(t.Modified);
			Assert.Throws<ArgumentOutOfRangeException>(() => t.SelectionStart = -1);
		}

		[Fact]
		public void A_list_box_raises_SelectedValueChanged_then_SelectedIndexChanged_for_a_change_by_the_script()
		{
			var list = new ListBox { Name = "list" };
			list.Items.AddRange(new object[] { "a", "b", "c" });
			list.SelectedValueChanged += trace.Handler("list.SelectedValueChanged");
			list.SelectedIndexChanged += trace.Handler("list.SelectedIndexChanged");
			list.SelectedIndex = 1;
			Assert.Equal(new[] { "list.SelectedValueChanged", "list.SelectedIndexChanged" }, trace.Entries);
			Assert.Equal("b", list.SelectedItem);
			trace.Clear();
			list.SelectedIndex = 1;
			Assert.Empty(trace.Entries);
			list.Items.RemoveAt(0);
			Assert.Equal(0, list.SelectedIndex);
			list.Items.Remove("b");
			Assert.Equal(-1, list.SelectedIndex);
			Assert.Equal(2, trace.Entries.Count);
			Assert.Throws<ArgumentOutOfRangeException>(() => list.SelectedIndex = 5);
		}

		[Fact]
		public void A_multi_select_list_box_tracks_the_selected_indexes()
		{
			var list = new ListBox { SelectionMode = SelectionMode.MultiExtended };
			list.Items.AddRange(new object[] { "a", "b", "c", "d" });
			int changes = 0;
			list.SelectedIndexChanged += (s, e) => changes++;
			list.SetSelected(1, true);
			list.SetSelected(3, true);
			list.SetSelected(3, true);
			Assert.Equal(2, changes);
			Assert.Equal(new[] { 1, 3 }, list.SelectedIndices.Cast<int>());
			Assert.Equal(new object[] { "b", "d" }, list.SelectedItems.Cast<object>());
			Assert.True(list.GetSelected(1));
			list.Items.RemoveAt(0);
			Assert.Equal(new[] { 0, 2 }, list.SelectedIndices.Cast<int>());
			list.ClearSelected();
			Assert.Equal(0, list.SelectedItems.Count);
			Assert.Equal(-1, list.SelectedIndex);
			list.SelectionMode = SelectionMode.None;
			Assert.Throws<ArgumentException>(() => list.SelectedIndex = 0);
		}

		[Fact]
		public void A_sorted_list_inserts_in_order_and_FindString_looks_by_prefix()
		{
			var list = new ListBox { Sorted = true };
			list.Items.Add("pear");
			list.Items.Add("apple");
			list.Items.Add("fig");
			Assert.Equal(new object[] { "apple", "fig", "pear" }, list.Items.Cast<object>());
			Assert.Equal(2, list.FindString("pe"));
			Assert.Equal(-1, list.FindStringExact("pe"));
			Assert.Equal(1, list.FindStringExact("FIG"));
		}

		[Fact]
		public void A_combo_box_updates_its_text_before_it_raises_SelectedIndexChanged()
		{
			var combo = new ComboBox { Name = "combo", DropDownStyle = ComboBoxStyle.DropDownList };
			combo.Items.AddRange(new object[] { "one", "two" });
			combo.TextChanged += trace.Handler("combo.TextChanged");
			combo.SelectedValueChanged += trace.Handler("combo.SelectedValueChanged");
			combo.SelectedIndexChanged += trace.Handler("combo.SelectedIndexChanged");
			combo.SelectedIndex = 1;
			Assert.Equal(new[] { "combo.TextChanged", "combo.SelectedValueChanged", "combo.SelectedIndexChanged" }, trace.Entries);
			Assert.Equal("two", combo.Text);
			trace.Clear();
			combo.Text = "one";
			Assert.Equal(0, combo.SelectedIndex);
			Assert.Equal(new[] { "combo.TextChanged", "combo.SelectedValueChanged", "combo.SelectedIndexChanged" }, trace.Entries);
			trace.Clear();
			combo.Items.Clear();
			Assert.Equal(-1, combo.SelectedIndex);
			Assert.Equal(string.Empty, combo.Text);
		}

		[Fact]
		public void A_combo_box_with_an_edit_field_keeps_text_that_is_not_an_item()
		{
			var combo = new ComboBox();
			combo.Items.Add("one");
			combo.Text = "free text";
			Assert.Equal("free text", combo.Text);
			Assert.Equal(-1, combo.SelectedIndex);
			combo.SelectedIndex = 0;
			Assert.Equal("one", combo.Text);
			combo.SelectAll();
			Assert.Equal("one", combo.SelectedText);
		}

		[Fact]
		public void A_bound_list_shows_the_items_of_the_data_source_by_DisplayMember()
		{
			var data = new List<Pair> { new Pair { Name = "first", Id = 10 }, new Pair { Name = "second", Id = 20 } };
			var combo = new ComboBox { DataSource = data, DisplayMember = "Name", ValueMember = "Id" };
			combo.SelectedIndex = 1;
			Assert.Equal("second", combo.Text);
			Assert.Equal(20, combo.SelectedValue);
			combo.SelectedValue = 10;
			Assert.Equal(0, combo.SelectedIndex);
			Assert.Throws<ArgumentException>(() => combo.Items.Add("x"));
		}

		public sealed class Pair
		{
			public string Name { get; set; }

			public int Id { get; set; }
		}

		[Fact]
		public void A_numeric_up_down_range_checks_the_value_and_raises_ValueChanged_before_TextChanged()
		{
			var n = new NumericUpDown { Name = "n", Minimum = 1, Maximum = 10 };
			Assert.Equal(1m, n.Value);
			Assert.Equal("1", n.Text);
			n.ValueChanged += trace.Handler("n.ValueChanged");
			n.TextChanged += trace.Handler("n.TextChanged");
			n.Value = 5;
			Assert.Equal(new[] { "n.ValueChanged", "n.TextChanged" }, trace.Entries);
			Assert.Equal("5", n.Text);
			Assert.Throws<ArgumentOutOfRangeException>(() => n.Value = 11);
			Assert.Throws<ArgumentOutOfRangeException>(() => n.Value = 0);
			n.Maximum = 4;
			Assert.Equal(4m, n.Value);
			n.Minimum = 6;
			Assert.Equal(6m, n.Maximum);
			Assert.Equal(6m, n.Value);
		}

		[Fact]
		public void A_numeric_up_down_formats_steps_and_parses_what_the_user_typed()
		{
			var n = new NumericUpDown { DecimalPlaces = 2, Increment = 0.25m, Maximum = 1000 };
			n.Value = 3;
			Assert.Equal("3.00", n.Text);
			n.UpButton();
			Assert.Equal(3.25m, n.Value);
			n.DownButton();
			n.DownButton();
			Assert.Equal(2.75m, n.Value);
			n.ThousandsSeparator = true;
			n.Value = 1000;
			Assert.Equal("1,000.00", n.Text);
			n.ThousandsSeparator = false;
			n.DecimalPlaces = 0;
			n.UserChangedText("42");
			Assert.Equal(42m, n.Value);
			n.UserChangedText("not a number");
			n.CommitEdit();
			Assert.Equal(42m, n.Value);
			Assert.Equal("42", n.Text);
			n.Hexadecimal = true;
			Assert.Equal("2A", n.Text);
			Assert.Throws<ArgumentOutOfRangeException>(() => n.Increment = -1);
		}

		// ---------------------------------------------------------------- exceptions in handlers

		[Fact]
		public void An_exception_from_a_peer_callback_goes_to_ThreadException_or_is_recorded()
		{
			var seen = new List<Exception>();
			ThreadExceptionEventHandler handler = (s, e) => seen.Add(e.Exception);
			Application.ThreadException += handler;
			try
			{
				FormsRuntime.Guard(() => throw new InvalidOperationException("from a handler"));
			}
			finally
			{
				Application.ThreadException -= handler;
			}
			Assert.Equal("from a handler", Assert.Single(seen).Message);
			var heard = new List<Exception>();
			FormsRuntime.UnhandledException += heard.Add;
			FormsRuntime.Guard(() => throw new InvalidOperationException("nobody listens"));
			FormsRuntime.UnhandledException -= heard.Add;
			Assert.Equal("nobody listens", Assert.Single(heard).Message);
			Assert.Equal("nobody listens", Assert.Single(FormsRuntime.Unhandled).Message);
		}

		[Fact]
		public void An_exception_in_an_event_handler_raised_by_a_setter_reaches_the_script_that_set_the_value()
		{
			var c = new Control();
			c.TextChanged += (s, e) => throw new InvalidOperationException("handler");
			Assert.Throws<InvalidOperationException>(() => c.Text = "x");
			Assert.Equal("x", c.Text);
		}

		// ---------------------------------------------------------------- the backend

		[Fact]
		public void The_modal_loop_runs_until_Exit_and_remembers_an_exit_requested_before_it_runs()
		{
			var loop = new ModalLoop();
			int ran = 0;
			backend.Post(() => ran++);
			backend.Post(loop.Exit);
			backend.Post(() => ran++);
			backend.RunModal(loop);
			Assert.Equal(1, ran);
			Assert.True(loop.ExitRequested);
			Assert.Equal(1, backend.Pump());
			var early = new ModalLoop();
			early.Exit();
			backend.RunModal(early);
			Assert.Equal(0, backend.ModalDepth);
		}

		[Fact]
		public void The_null_backend_leaves_a_modal_loop_that_has_nothing_to_run_unless_told_to_wait()
		{
			var loop = new ModalLoop();
			backend.RunModal(loop);
			Assert.False(loop.ExitRequested);
			backend.ModalExitsWhenIdle = false;
			var waiting = new ModalLoop();
			var worker = new Thread(() =>
			{
				Thread.Sleep(10);
				backend.Post(() => waiting.Exit());
			});
			worker.Start();
			backend.RunModal(waiting);
			worker.Join();
			Assert.True(waiting.ExitRequested);
		}

		[Fact]
		public void Invoke_from_another_thread_runs_on_the_ui_thread_and_returns_its_exception()
		{
			int ui = Environment.CurrentManagedThreadId;
			int ranOn = -1;
			Exception thrown = null;
			var worker = new Thread(() =>
			{
				backend.Invoke(() => ranOn = Environment.CurrentManagedThreadId);
				try
				{
					backend.Invoke(() => throw new InvalidOperationException("on the ui thread"));
				}
				catch (Exception e)
				{
					thrown = e;
				}
			});
			worker.Start();
			while (worker.IsAlive)
			{
				backend.Pump();
				Thread.Yield();
			}
			worker.Join();
			backend.Pump();
			Assert.Equal(ui, ranOn);
			Assert.Equal("on the ui thread", thrown?.Message);
			Assert.True(backend.CheckAccess());
		}

		[Fact]
		public void Without_thread_affinity_a_worker_never_waits_and_Shown_follows_Activated()
		{
			var headless = new NullFormsBackend();
			FormsRuntime.Backend = headless;
			var form = new Form { Name = "form" };
			form.Controls.Add(new Button());
			trace.Watch(form, "form");
			Exception error = null;
			var worker = new Thread(() =>
			{
				try
				{
					form.Show();
					form.Text = "from a worker";
					form.Close();
				}
				catch (Exception e)
				{
					error = e;
				}
			});
			worker.Start();
			Assert.True(worker.Join(TimeSpan.FromSeconds(20)), "a script thread hung in the null backend");
			Assert.Null(error);
			Assert.Equal(new[] { "form.Load", "form.Activated", "form.Shown", "form.Closing", "form.FormClosing(UserClosing)", "form.Closed", "form.FormClosed(UserClosing)", "form.Deactivate" }, trace.Entries);
			Assert.True(headless.CheckAccess());
		}

		[Fact]
		public void Device_state_comes_from_the_backend()
		{
			backend.ModifierKeys = Keys.Control | Keys.Shift;
			backend.MouseButtons = MouseButtons.Left;
			backend.MousePosition = new Point(7, 8);
			Assert.Equal(Keys.Control | Keys.Shift, Control.ModifierKeys);
			Assert.Equal(MouseButtons.Left, Control.MouseButtons);
			Assert.Equal(new Point(7, 8), Control.MousePosition);
		}

		[Fact]
		public void A_script_can_derive_from_Control_and_override_the_events_it_needs()
		{
			var c = new Derived();
			c.Text = "x";
			c.Size = new Size(3, 4);
			Assert.Equal(new[] { "OnTextChanged", "OnResize", "OnSizeChanged" }.OrderBy(x => x), c.Calls.Distinct().OrderBy(x => x));
		}

		private sealed class Derived : Control
		{
			public readonly List<string> Calls = new List<string>();

			protected override void OnTextChanged(EventArgs e)
			{
				Calls.Add("OnTextChanged");
				base.OnTextChanged(e);
			}

			protected override void OnResize(EventArgs e)
			{
				Calls.Add("OnResize");
				base.OnResize(e);
			}

			protected override void OnSizeChanged(EventArgs e)
			{
				Calls.Add("OnSizeChanged");
				base.OnSizeChanged(e);
			}
		}
	}
}
