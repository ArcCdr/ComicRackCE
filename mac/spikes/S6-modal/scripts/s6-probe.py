# Spike S6 probe: Python subclasses of the facade Control, owner draw, handlers that open dialogs, handlers that fail.
# It uses the same import lines as the bundled scripts; cYo.Compat.Drawing is named because the Drawing identity is measured separately.
import clr
clr.AddReferenceByPartialName("System.Windows.Forms")
clr.AddReferenceByPartialName("System.Drawing")
clr.AddReference("cYo.Compat.Drawing")
from System.Windows.Forms import *
from System.Drawing import *
from System.Threading import Thread, ThreadStart

class Swatch(Control):
	"""A control that paints itself: OnPaint and OnResize are overridden in Python."""
	def __init__(self):
		self.paints = 0
		self.resizes = 0
		self.clip = None
		self.red = 220
		self.blue = 30
		self.fail = False
		self.base_call = ""

	def OnPaint(self, e):
		self.paints += 1
		self.clip = e.ClipRectangle
		if self.fail:
			raise ValueError("OnPaint failed on purpose")
		br = SolidBrush(Color.FromArgb(255, self.red, 30, self.blue))
		e.Graphics.FillRectangle(br, Rectangle(10, 10, 50, 40))
		br.Dispose()
		try:
			Control.OnPaint(self, e)
			self.base_call = "ok"
		except Exception, ex:
			self.base_call = "raised " + str(ex)

	def OnResize(self, e):
		self.resizes += 1
		Control.OnResize(self, e)

def make_swatch():
	s = Swatch()
	s.Location = Point(20, 20)
	s.Size = Size(120, 80)
	return s

def make_paint_event_control():
	"""A plain Control with a Paint handler assigned from Python."""
	c = Control()
	c.Location = Point(20, 110)
	c.Size = Size(120, 50)
	return c

def paint_handler(sender, e):
	br = SolidBrush(Color.FromArgb(255, 20, 160, 40))
	e.Graphics.FillRectangle(br, Rectangle(0, 0, 40, 30))
	br.Dispose()
	paint_handler.calls = getattr(paint_handler, "calls", 0) + 1

def attach_paint_handler(c):
	c.Paint += paint_handler

def build_ask_form():
	"""A dialog whose Click handler shows a MessageBox (a modal loop inside an event handler inside a modal loop)."""
	f = Form()
	f.Name = "ask"
	f.Text = "Ask"
	f.ClientSize = Size(240, 100)
	f.FormBorderStyle = FormBorderStyle.FixedDialog
	ask = Button()
	ask.Name = "askButton"
	ask.Text = "Ask"
	ask.Location = Point(20, 20)
	ask.Size = Size(80, 26)
	bad = Button()
	bad.Name = "badButton"
	bad.Text = "Fail"
	bad.Location = Point(120, 20)
	bad.Size = Size(80, 26)
	done = Button()
	done.Name = "doneButton"
	done.Text = "Done"
	done.DialogResult = DialogResult.OK
	done.Location = Point(120, 60)
	done.Size = Size(80, 26)
	f.AcceptButton = done
	def on_ask(sender, args):
		f.Tag = MessageBox.Show("Really do it?", "Confirm", MessageBoxButtons.YesNo)
	def on_bad(sender, args):
		raise ValueError("handler failed on purpose")
	ask.Click += on_ask
	bad.Click += on_bad
	f.Controls.Add(ask)
	f.Controls.Add(bad)
	f.Controls.Add(done)
	return f

def run_in_thread(fn):
	"""Runs fn on a new .NET thread (like a script worker) and waits for it."""
	result = []
	def body():
		result.append(fn())
	t = Thread(ThreadStart(body))
	t.Start()
	t.Join()
	return result[0] if result else None
