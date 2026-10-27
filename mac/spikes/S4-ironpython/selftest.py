# Probe script of spike S4 (P4.1). Run by `--script-selftest FILE` (ComicRack.Mac) or by the S4-ironpython console.
# Every function with a Startup hook header is one scenario and runs in a fresh IronPython engine (its own clr references).
# Lines that start with PASS or FAIL are checks; RES lines are the rows of the resolution table in mac/reports/s4-ironpython.md.
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

#@Name  S4 environment
#@Hook  Startup
def env_variables():
	res("python version", sys.version.replace("\n", " "))
	res("ScriptPath", ScriptPath)
	ok("ComicRack is the plugin environment", ComicRack is not None, ComicRack.GetType().FullName)
	res("clr.References at start", ", ".join(sorted([a.GetName().Name for a in clr.References])))

#@Name  S4 standard library
#@Hook  Startup
def stdlib():
	res("sys.path", sys.path)
	for mod in ["os", "os.path", "re", "datetime", "sys", "string", "math", "random", "time", "codecs", "glob", "shutil", "csv", "json",
			"collections", "itertools", "urllib", "urllib2", "xml.dom.minidom", "xml.etree.ElementTree", "struct", "base64", "hashlib",
			"cStringIO", "StringIO", "tempfile", "traceback", "socket", "subprocess", "zipfile", "locale", "unicodedata", "htmlentitydefs",
			"HTMLParser", "sqlite3", "threading", "operator", "functools", "copy", "pickle", "decimal", "difflib", "fnmatch", "getpass", "platform"]:
		try:
			__import__(mod)
			res("import " + mod, "ok")
		except Exception, e:
			res("import " + mod, "ERROR " + err(e))
	try:
		import os, re, datetime
		ok("import os, re, datetime", True)
		ok("re works", re.sub(r"(\d+)", r"<\1>", "a12b3") == "a<12>b<3>")
		ok("datetime works", datetime.date(2011, 5, 6).strftime("%Y-%m-%d") == "2011-05-06")
		ok("os.path works", os.path.join("a", "b") == "a" + os.sep + "b", os.sep)
		res("os.name / sys.platform", os.name + " / " + sys.platform)
	except Exception, e:
		ok("import os, re, datetime", False, err(e))

#@Name  S4 header of Sample.py (partial names)
#@Hook  Startup
def sample_header():
	# The first lines of Sample.py, Autonumber.py and SearchAndReplace.py.
	a = add_ref("AddReferenceByPartialName", "System.Windows.Forms")
	b = add_ref("AddReferenceByPartialName", "System.Drawing")
	ns = {}
	try:
		exec "from System.Windows.Forms import *\nfrom System.Drawing import *" in ns
		obs("star imports run", True)
	except Exception, e:
		obs("star imports run", False, err(e))
	res("references after the header", ", ".join(sorted([x.GetName().Name for x in clr.References])))
	for n in DRAWING_NAMES + FORMS_NAMES:
		obs("name " + n + " is defined", n in ns)
	for n in DRAWING_NAMES:
		where("System.Drawing", n)
	for n in FORMS_NAMES:
		where("System.Windows.Forms", n)
	make("Point(1, 2)", "Point(1, 2)", ns)
	make("Bitmap(10, 10)", "Bitmap(10, 10)", ns)
	make("Form()", "Form()", ns)
	make("Label()", "Label()", ns)
	make("ListBox()", "ListBox()", ns)

#@Name  S4 without clr.AddReference
#@Hook  Startup
def no_references():
	ns = {}
	for code in ["from System.Drawing import *", "from System.Windows.Forms import *"]:
		try:
			exec code in ns
			res(code, "ok")
		except Exception, e:
			res(code, "ERROR " + err(e))
	res("references", ", ".join(sorted([x.GetName().Name for x in clr.References])))
	for n in ["Point", "Bitmap", "Graphics"]:
		where("System.Drawing", n)
	for n in ["Form", "Control", "MessageBox"]:
		where("System.Windows.Forms", n)

#@Name  S4 System.Drawing facade only
#@Hook  Startup
def drawing_facade():
	add_ref("AddReference", "System.Drawing")
	for n in ["Point", "Color", "Bitmap", "Graphics"]:
		where("System.Drawing", n)

#@Name  S4 System.Drawing.Common only
#@Hook  Startup
def drawing_common():
	add_ref("AddReference", "System.Drawing.Common")
	for n in ["Point", "Color", "Bitmap", "Graphics"]:
		where("System.Drawing", n)
	ns = {}
	exec "from System.Drawing import *" in ns
	make("Bitmap(10, 10)", "Bitmap(10, 10)", ns)

#@Name  S4 Control subclass as in Sample.py
#@Hook  Startup
def control_subclass():
	# since P4.8 the stand-ins carry the names of the originals
	add_ref("AddReference", "System.Windows.Forms")
	add_ref("AddReference", "System.Drawing")
	ns = {}
	exec "from System.Windows.Forms import *\nfrom System.Drawing import *" in ns
	try:
		exec "class UIControl(Control):\n\tdef __init__(self):\n\t\tself.n = 1\n\tdef ShowInfo(self, books):\n\t\tpass\n" in ns
		ok("class UIControl(Control) is defined", True)
		c = ns["UIControl"]()
		ok("UIControl() is created", c is not None, c.GetType().FullName)
	except Exception, e:
		ok("class UIControl(Control)", False, err(e))
	try:
		exec "class Overlay(Control):\n\tdef OnPaint(self, e):\n\t\tpass\n" in ns
		ok("class with OnPaint override is defined", True)
	except Exception, e:
		ok("class with OnPaint override is defined", False, err(e))

#@Name  S4 Core assembly names
#@Hook  Startup
def core_names():
	# RefreshView.py: clr.AddReferenceByPartialName('ComicRack.Engine') and 'cYo.Common.Windows'.
	for n in ["ComicRack.Engine", "ComicRack.Plugins", "cYo.Common", "cYo.Common.Windows"]:
		add_ref("AddReferenceByPartialName", n)
	for ns, n in [("cYo.Projects.ComicRack.Engine", "ComicBook"), ("cYo.Projects.ComicRack.Engine", "IRefreshDisplay"), ("cYo.Common.Windows.Forms", "FormUtility"),
			("cYo.Common.Reflection", "DuckTyping"), ("cYo.Common.Text", "StringUtility")]:
		where(ns, n)

#@Name  S4 DuckTyping
#@Hook  Startup
def duck_typing():
	import System
	add_ref("AddReference", "cYo.Common")
	try:
		from cYo.Common.Reflection import DuckTyping
		obs("DuckTyping is reachable", True)
	except Exception, e:
		obs("DuckTyping is reachable", False, err(e))
		return
	try:
		DuckTyping.Implement[System.IDisposable, System.IO.MemoryStream](System.IO.MemoryStream())
		res("DuckTyping.Implement[IDisposable, MemoryStream](ms)", "worked")
	except Exception, e:
		res("DuckTyping.Implement[IDisposable, MemoryStream](ms)", "ERROR " + err(e))
	try:
		DuckTyping.Implement[System.IDisposable](System.IO.MemoryStream())
		res("DuckTyping.Implement[IDisposable](ms)", "worked")
	except Exception, e:
		res("DuckTyping.Implement[IDisposable](ms)", "ERROR " + err(e))
