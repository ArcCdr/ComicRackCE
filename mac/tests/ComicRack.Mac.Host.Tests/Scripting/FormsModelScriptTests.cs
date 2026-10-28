using System.IO;
using ComicRack.Mac.Host.Scripting;
using cYo.Compat.Forms;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>
	/// The object model of the Forms facade (P4.9a) seen from IronPython: a script builds a dialog from the usual controls, subscribes to their events with Python functions,
	/// derives from Form, Control and UserControl and overrides the virtual members, and the events come in the order of WinForms. The dialog runs on the null backend that a
	/// headless host uses.
	/// </summary>
	[Collection("Forms runtime")]
	public class FormsModelScriptTests
	{
		private static string Run(string body)
		{
			FormsRuntime.Reset();
			try
			{
				using (var temp = new TempFolder())
				{
					string script = @"
import clr
clr.AddReference('System.Windows.Forms')
clr.AddReference('System.Drawing')
from System import Array
from System.Windows.Forms import *
from System.Drawing import *

trace = []

" + body;
					string dir = ScriptTestSupport.WriteScripts(temp, "scripts", ("model.py", script));
					using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
					{
						ScriptTestSupport.Add(host, ScriptTestSupport.Book("A", "1"));
						var output = new StringWriter();
						var errors = new StringWriter();
						int code = ScriptRunner.Run(host, Path.Combine(dir, "model.py"), "Books", null, output, errors);
						Assert.True(code == 0, output + errors.ToString());
						return output.ToString();
					}
				}
			}
			finally
			{
				FormsRuntime.Reset();
			}
		}

		[Fact]
		public void A_script_builds_a_dialog_with_the_controls_of_a_script_and_gets_the_events_in_order()
		{
			string text = Run(@"
class MyForm(Form):
	def OnLoad(self, e):
		trace.append('OnLoad')
		Form.OnLoad(self, e)

	def OnFormClosing(self, e):
		trace.append('OnFormClosing %s' % e.CloseReason)
		Form.OnFormClosing(self, e)

#@Name Model
#@Hook Books
def Model(books):
	form = MyForm()
	form.Text = 'Renumber'
	form.FormBorderStyle = FormBorderStyle.FixedDialog
	form.StartPosition = FormStartPosition.CenterParent
	form.MaximizeBox = False
	form.MinimizeBox = False
	form.ClientSize = Size(243, 154)
	label = Label()
	label.Text = 'Start at:'
	label.Location = Point(12, 15)
	label.Size = Size(60, 13)
	number = NumericUpDown()
	number.Location = Point(80, 12)
	number.Size = Size(65, 20)
	number.Minimum = 1
	number.Maximum = 9999
	check = CheckBox()
	check.Text = 'Alternate series'
	check.Location = Point(12, 45)
	combo = ComboBox()
	combo.DropDownStyle = ComboBoxStyle.DropDownList
	combo.Items.AddRange(Array[object](['one', 'two', 'three']))
	ok = Button()
	ok.Text = 'OK'
	ok.DialogResult = DialogResult.OK
	ok.Location = Point(75, 120)
	cancel = Button()
	cancel.Text = 'Cancel'
	cancel.DialogResult = DialogResult.Cancel
	cancel.Location = Point(155, 120)
	form.Controls.Add(label)
	form.Controls.AddRange(Array[Control]([number, check, combo, ok, cancel]))
	form.AcceptButton = ok
	form.CancelButton = cancel

	def clicked(sender, args):
		trace.append('clicked %s' % sender.Text)
	ok.Click += clicked
	check.CheckedChanged += lambda s, e: trace.append('checked %s' % s.Checked)
	combo.SelectedIndexChanged += lambda s, e: trace.append('selected %s' % s.SelectedItem)
	number.ValueChanged += lambda s, e: trace.append('value %s' % s.Value)
	form.Shown += lambda s, e: trace.append('Shown')
	form.Activated += lambda s, e: trace.append('Activated')

	print 'sizes', form.Size.Width, form.Size.Height, form.ClientSize.Width, form.ClientSize.Height
	print 'controls', form.Controls.Count, [c.TabIndex for c in form.Controls]
	print 'texts', form.Text, label.Text, ok.Text
	form.Show()
	check.Checked = True
	combo.SelectedIndex = 2
	number.Value = 7
	ok.PerformClick()
	print 'dialogresult', form.DialogResult
	print 'visible', form.Visible, ok.Visible, form.Focused
	form.Close()
	print 'trace', ' | '.join(trace)
	print 'closed', form.IsDisposed
");
			Assert.Contains("sizes 251 185 243 154", text);
			Assert.Contains("controls 6 [0, 1, 2, 3, 4, 5]", text);
			Assert.Contains("texts Renumber Start at: OK", text);
			Assert.Contains("dialogresult OK", text);
			Assert.Contains("visible True True False", text);
			Assert.Contains("trace OnLoad | Activated | Shown | checked True | selected three | value 7 | clicked OK | OnFormClosing UserClosing", text);
			Assert.Contains("closed True", text);
		}

		[Fact]
		public void A_script_derives_from_Control_and_UserControl_and_overrides_the_events_it_uses()
		{
			string text = Run(@"
class Swatch(Control):
	def __init__(self):
		self.SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, True)
		self.Color = Color.Red

	def OnResize(self, e):
		trace.append('OnResize %d %d' % (self.Width, self.Height))
		Control.OnResize(self, e)

	def OnTextChanged(self, e):
		trace.append('OnTextChanged %s' % self.Text)
		Control.OnTextChanged(self, e)

class Info(UserControl):
	def __init__(self):
		self.Label = Label()
		self.Label.Dock = DockStyle.Top
		self.Controls.Add(self.Label)

	def ShowInfo(self, books):
		self.Label.Text = 'books: %d' % len(books)

#@Name Model
#@Hook Books
def Model(books):
	s = Swatch()
	s.Size = Size(20, 10)
	s.Text = 'swatch'
	s.Click += lambda sender, e: trace.append('Click handler')
	s.PerformLayout()
	info = Info()
	info.ShowInfo(books)
	print 'derived', s.GetType().BaseType.Name, s.Color == Color.Red, info.Label.Text, info.Controls.Count, info.Label.Parent == info
	print 'trace', ' | '.join(trace)
	print 'ambient', info.BackColor == Control.DefaultBackColor, info.Label.ForeColor == Control.DefaultForeColor
");
			Assert.Contains("derived Control True books: 1 1 True", text);
			Assert.Contains("trace OnResize 20 10 | OnTextChanged swatch", text);
			Assert.Contains("ambient True True", text);
		}

		[Fact]
		public void A_script_lays_out_a_dialog_with_Dock_and_Anchor()
		{
			string text = Run(@"
#@Name Model
#@Hook Books
def Model(books):
	form = Form()
	form.ClientSize = Size(400, 300)
	body = Panel()
	body.Size = Size(400, 260)
	body.Dock = DockStyle.Fill
	bar = Panel()
	bar.Size = Size(400, 40)
	bar.Dock = DockStyle.Bottom
	ok = Button()
	ok.Size = Size(75, 23)
	ok.Location = Point(310, 8)
	ok.Anchor = AnchorStyles.Top | AnchorStyles.Right
	name = TextBox()
	name.Location = Point(10, 10)
	name.Size = Size(300, 20)
	name.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
	form.SuspendLayout()
	body.Controls.Add(name)
	bar.Controls.Add(ok)
	form.Controls.Add(body)
	form.Controls.Add(bar)
	form.ResumeLayout(True)
	print 'first', body.Bounds, bar.Bounds, name.Bounds, ok.Bounds
	form.ClientSize = Size(500, 400)
	print 'grown', body.Bounds, bar.Bounds, name.Bounds, ok.Bounds
	bar.Dock = DockStyle.None
	print 'undocked', bar.Bounds
");
			Assert.True(text.Contains("first {X=0,Y=0,Width=400,Height=260} {X=0,Y=260,Width=400,Height=40} {X=10,Y=10,Width=300,Height=20} {X=310,Y=8,Width=75,Height=23}"), text);
			Assert.Contains("grown {X=0,Y=0,Width=500,Height=360} {X=0,Y=360,Width=500,Height=40} {X=10,Y=10,Width=400,Height=20} {X=410,Y=8,Width=75,Height=23}", text);
			Assert.Contains("undocked {X=0,Y=0,Width=400,Height=40}", text);
		}

		[Fact]
		public void A_script_lays_out_a_dialog_with_a_FlowLayoutPanel_and_a_TableLayoutPanel()
		{
			string text = Run(@"
#@Name Model
#@Hook Books
def Model(books):
	flow = FlowLayoutPanel()
	flow.Size = Size(200, 100)
	buttons = []
	for i in range(3):
		b = Button()
		flow.Controls.Add(b)
		buttons.append(b)
	flow.SetFlowBreak(buttons[0], True)
	print 'flow', buttons[0].Bounds, buttons[1].Bounds, buttons[2].Bounds, flow.GetFlowBreak(buttons[0]), flow.WrapContents, flow.FlowDirection
	flow.FlowDirection = FlowDirection.TopDown
	print 'topdown', buttons[1].Bounds
	table = TableLayoutPanel()
	table.Size = Size(300, 100)
	table.ColumnCount = 2
	table.ColumnStyles.Add(ColumnStyle(SizeType.Absolute, 100))
	table.ColumnStyles.Add(ColumnStyle(SizeType.Percent, 100))
	table.RowStyles.Add(RowStyle(SizeType.Absolute, 40))
	label = Label()
	label.Text = 'Name'
	label.Anchor = AnchorStyles.Left
	name = TextBox()
	name.Dock = DockStyle.Fill
	ok = Button()
	ok.Anchor = AnchorStyles.Right
	table.Controls.Add(label)
	table.Controls.Add(name, 1, 0)
	table.Controls.Add(ok, 1, 1)
	print 'table', label.Bounds, name.Bounds, ok.Bounds
	print 'sizes', list(table.GetColumnWidths()), list(table.GetRowHeights()), table.RowCount, table.GetRow(ok), table.GetCellPosition(label).Column
	table.Width = 400
	print 'wider', name.Bounds, ok.Bounds
	table.SetColumnSpan(name, 1)
	table.ColumnStyles[0].Width = 50
	print 'styled', label.Bounds, name.Bounds
	print 'type', table.Controls.GetType().Name, table.LayoutSettings.ColumnCount
");
			Assert.True(text.Contains("flow {X=3,Y=3,Width=75,Height=23} {X=3,Y=32,Width=75,Height=23} {X=84,Y=32,Width=75,Height=23} True True LeftToRight"), text);
			Assert.True(text.Contains("topdown {X=84,Y=3,Width=75,Height=23}"), text);
			Assert.True(text.Contains("table {X=3,Y=8,Width=94,Height=23} {X=103,Y=3,Width=194,Height=34} {X=222,Y=43,Width=75,Height=23}"), text);
			Assert.True(text.Contains("sizes [100, 200] [40, 29] 2 1 -1"), text);
			Assert.True(text.Contains("wider {X=103,Y=3,Width=294,Height=34} {X=322,Y=43,Width=75,Height=23}"), text);
			Assert.True(text.Contains("styled {X=3,Y=8,Width=44,Height=23} {X=53,Y=3,Width=344,Height=34}"), text);
			Assert.True(text.Contains("type TableLayoutControlCollection 2"), text);
		}

		[Fact]
		public void Members_that_are_not_implemented_yet_report_themselves_while_the_rest_of_a_control_keeps_working()
		{
			string text = Run(@"
#@Name Model
#@Hook Books
def Model(books):
	t = TextBox()
	t.Multiline = True
	t.Text = 'a'
	t.AppendText('b')
	t.ReadOnly = True
	print 'text', t.Text, t.ReadOnly, t.Multiline, t.Lines.Length
	try:
		t.Paste()
	except Exception, e:
		print 'paste', e.message
	l = ListBox()
	l.Items.Add('x')
	l.SelectionMode = SelectionMode.MultiExtended
	l.SetSelected(0, True)
	print 'list', l.SelectedItems.Count, l.SelectedIndices[0], l.Items[0]
");
			Assert.Contains("text ab True True 1", text);
			Assert.Contains("paste System.Windows.Forms.TextBoxBase.Paste is not yet supported on macOS (ComicRack script compatibility layer)", text);
			Assert.Contains("list 1 0 x", text);
		}
	}
}
