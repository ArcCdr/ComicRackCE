# Probe script of spike S6 (P4.2), run by the S4 console (ScriptSelfTest) in a copy of its output whose assemblies were renamed by s6-rebind.
# Every function with a Startup hook header is one scenario and runs in a fresh IronPython engine (its own clr references).
# PASS/FAIL lines are checks of the identity decision, RES lines are observations.
import clr
import sys

def ok(name, cond, detail=""):
	print ("PASS " if cond else "FAIL ") + name + ((": " + str(detail)) if detail != "" else "")

def obs(name, cond, detail=""):
	# an observation about the platform, not a check: written as RES so that it never counts as a failure
	print "RES " + name + " = " + ("yes" if cond else "no") + ((" (" + str(detail) + ")") if detail != "" else "")

def res(label, value):
	print "RES " + label + " = " + str(value)

def err(e):
	return e.__class__.__name__ + ": " + str(e).replace("\n", " ")[:150]

def asm_of(t):
	a = clr.GetClrType(t).Assembly.GetName()
	return a.Name + " " + str(a.Version)

def add_ref(fn_name, arg):
	try:
		getattr(clr, fn_name)(arg)
		res(fn_name + "('" + arg + "')", "ok")
		return True
	except Exception, e:
		res(fn_name + "('" + arg + "')", "ERROR " + err(e))
		return False

def where(ns, name):
	# which assembly gives ns.name, or the error
	try:
		m = __import__(ns, fromlist=[name])
		res("from " + ns + " import " + name, asm_of(getattr(m, name)))
		return True
	except Exception, e:
		res("from " + ns + " import " + name, "ERROR " + err(e))
		return False

def make(label, code, ns):
	try:
		v = eval(code, ns)
		obs("create " + label, v is not None, v.GetType().FullName)
		return True
	except Exception, e:
		obs("create " + label, False, err(e))
		return False

DRAWING_NAMES = ["Point", "Size", "Rectangle", "Color", "Bitmap", "Graphics", "SolidBrush", "Font", "Pen", "Image"]
FORMS_NAMES = ["Form", "Control", "Label", "NumericUpDown", "ListBox", "MessageBox", "DockStyle", "DialogResult", "Button", "TextBox", "ComboBox", "CheckBox", "Keys"]


#@Name  S6 reference lines of Sample.py and Autonumber.py, unmodified
#@Hook  Startup
def unmodified_references():
	ns = {}
	try:
		clr.AddReferenceByPartialName("System.Windows.Forms")
		clr.AddReferenceByPartialName("System.Drawing")
		exec "from System.Windows.Forms import *\nfrom System.Drawing import *" in ns
		ok("the two AddReferenceByPartialName lines and both star imports work", True)
	except Exception, e:
		ok("the two AddReferenceByPartialName lines and both star imports work", False, err(e))
		return
	for n in ["Form", "Control", "DialogResult"]:
		res(n + " from", asm_of(ns[n]))
	for n in ["Point", "Size", "SizeF", "Rectangle", "Color"]:
		res(n + " from", asm_of(ns[n]))
	for n in ["Bitmap", "Graphics", "SolidBrush", "Font", "Pen", "Image"]:
		res(n + " from", asm_of(ns[n]))
	ok("Form is the Forms stand-in", clr.GetClrType(ns["Form"]).Assembly.GetName().Name == "System.Windows.Forms", asm_of(ns["Form"]))
	try:
		b = ns["Bitmap"](10, 10)
		g = ns["Graphics"].FromImage(b)
		br = ns["SolidBrush"](ns["Color"].FromArgb(128, 255, 255, 255))
		g.FillRectangle(br, ns["Rectangle"](0, 0, 5, 5))
		g.Dispose()
		ok("Bitmap, Graphics.FromImage, SolidBrush, FillRectangle work (FadeReadThumbnails of Sample.py)", True, asm_of(ns["Bitmap"]))
	except Exception, e:
		ok("Bitmap, Graphics.FromImage, SolidBrush, FillRectangle work (FadeReadThumbnails of Sample.py)", False, err(e))

#@Name  S6 Control subclass after the unmodified reference lines
#@Hook  Startup
def control_subclass():
	clr.AddReferenceByPartialName("System.Windows.Forms")
	clr.AddReferenceByPartialName("System.Drawing")
	ns = {}
	exec "from System.Windows.Forms import *\nfrom System.Drawing import *" in ns
	try:
		exec "class UIControl(Control):\n\tdef __init__(self):\n\t\tself.n = 1\n\tdef OnPaint(self, e):\n\t\tpass\n" in ns
		c = ns["UIControl"]()
		ok("class UIControl(Control) with an OnPaint override can be created", True, c.GetType().FullName)
	except Exception, e:
		ok("class UIControl(Control) with an OnPaint override can be created", False, err(e))

#@Name  S6 full assembly names as a script written for the .NET Framework may use them
#@Hook  Startup
def full_names():
	ok("AddReference with the System.Windows.Forms strong name", add_ref("AddReference", "System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"))
	ok("AddReference with the System.Drawing strong name", add_ref("AddReference", "System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"))
	ok("AddReferenceByName with the System.Windows.Forms strong name", add_ref("AddReferenceByName", "System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"))
	ns = {}
	exec "from System.Windows.Forms import Form\nfrom System.Drawing import Bitmap" in ns
	res("Form from", asm_of(ns["Form"]))
	res("Bitmap from", asm_of(ns["Bitmap"]))

#@Name  S6 names of the Core as scripts write them
#@Hook  Startup
def core_names():
	ok("AddReferenceByPartialName('ComicRack.Engine') (RefreshView.py)", add_ref("AddReferenceByPartialName", "ComicRack.Engine"))
	ok("AddReferenceByPartialName('ComicRack.Plugins')", add_ref("AddReferenceByPartialName", "ComicRack.Plugins"))
	ok("AddReferenceByPartialName('cYo.Common')", add_ref("AddReferenceByPartialName", "cYo.Common"))
	ns = {}
	try:
		exec "from cYo.Projects.ComicRack.Engine import ComicBook\nfrom cYo.Common.Reflection import DuckTyping\nb = ComicBook()" in ns
		ok("ComicBook from cYo.Projects.ComicRack.Engine can be created", True, asm_of(ns["ComicBook"]))
	except Exception, e:
		ok("ComicBook from cYo.Projects.ComicRack.Engine can be created", False, err(e))

#@Name  S6 System.Drawing alone (a script without the Forms line)
#@Hook  Startup
def drawing_alone():
	clr.AddReference("System.Drawing")
	ns = {}
	exec "from System.Drawing import *" in ns
	try:
		b = ns["Bitmap"](4, 4)
		ok("System.Drawing alone: Bitmap(4, 4) works", True, asm_of(ns["Bitmap"]))
	except Exception, e:
		ok("System.Drawing alone: Bitmap(4, 4) works", False, err(e))

#@Name  S6 System.Drawing.Common by its name
#@Hook  Startup
def drawing_common():
	clr.AddReference("System.Drawing.Common")
	ns = {}
	exec "from System.Drawing import *" in ns
	try:
		b = ns["Bitmap"](4, 4)
		ok("System.Drawing.Common: Bitmap(4, 4) works", True, asm_of(ns["Bitmap"]))
	except Exception, e:
		ok("System.Drawing.Common: Bitmap(4, 4) works", False, err(e))
