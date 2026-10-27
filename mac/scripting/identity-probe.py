# Probe of P4.8 (assembly identity). Run by `--script-selftest FILE` of ComicRack.Mac (also in the signed bundle, see mac/tools/bundle-selftest.sh) and by the tests.
# Every function with a Startup hook header is one scenario in a fresh IronPython engine. Lines that start with PASS or FAIL are checks.
# The scripts use the names of the originals only: the reference lines below are the ones of the scripts that ship with the program.
import clr

def ok(name, cond, detail=""):
	print ("PASS " if cond else "FAIL ") + name + ((": " + str(detail)) if detail != "" else "")

def asm(t):
	a = clr.GetClrType(t).Assembly.GetName()
	return a.Name + " " + str(a.Version)

def is_asm(label, t, expected):
	actual = asm(t)
	ok("identity: " + label + " is " + expected, actual.startswith(expected), actual)

#@Name  P4.8 System.Windows.Forms and System.Drawing by partial name
#@Hook  Startup
def partial_names():
	clr.AddReferenceByPartialName("System.Windows.Forms")
	clr.AddReferenceByPartialName("System.Drawing")
	from System.Windows.Forms import Form, Control, MessageBox, Button, DialogResult
	from System.Drawing import Bitmap, Graphics, Font, Color, Point, Size, Rectangle
	is_asm("Form", Form, "System.Windows.Forms 10.0.0.0")
	is_asm("Control", Control, "System.Windows.Forms 10.0.0.0")
	is_asm("MessageBox", MessageBox, "System.Windows.Forms 10.0.0.0")
	is_asm("Button", Button, "System.Windows.Forms 10.0.0.0")
	is_asm("DialogResult", DialogResult, "System.Windows.Forms 10.0.0.0")
	is_asm("Bitmap", Bitmap, "System.Drawing.Common 10.0.0.0")
	is_asm("Graphics", Graphics, "System.Drawing.Common 10.0.0.0")
	is_asm("Font", Font, "System.Drawing.Common 10.0.0.0")
	# Color, Point, Size and Rectangle are types of the framework (System.Drawing.Primitives), shared with the facade
	is_asm("Color", Color, "System.Drawing.Primitives")
	is_asm("Point", Point, "System.Drawing.Primitives")

#@Name  P4.8 System.Windows.Forms and System.Drawing by exact reference
#@Hook  Startup
def exact_names():
	clr.AddReference("System.Windows.Forms")
	clr.AddReference("System.Drawing")
	from System.Windows.Forms import Form
	from System.Drawing import Bitmap
	is_asm("Form (AddReference)", Form, "System.Windows.Forms 10.0.0.0")
	is_asm("Bitmap (AddReference)", Bitmap, "System.Drawing.Common 10.0.0.0")
	# the strong names of the .NET Framework assemblies bind to the facades (the public key token is ignored)
	clr.AddReference("System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")
	clr.AddReference("System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")
	ok("identity: the strong names of the Framework assemblies are accepted", True)

#@Name  P4.8 Core assemblies by the names of the originals
#@Hook  Startup
def core_names():
	clr.AddReferenceByPartialName("ComicRack.Engine")
	clr.AddReferenceByPartialName("ComicRack.Plugins")
	clr.AddReferenceByPartialName("cYo.Common")
	clr.AddReferenceByPartialName("cYo.Common.Windows")
	from cYo.Projects.ComicRack.Engine import ComicBook, IRefreshDisplay
	from cYo.Projects.ComicRack.Plugins import PluginEngine
	from cYo.Common.Reflection import DuckTyping
	from cYo.Common.Windows.Forms import FormUtility
	is_asm("ComicBook", ComicBook, "ComicRack.Engine")
	is_asm("PluginEngine", PluginEngine, "ComicRack.Plugins")
	is_asm("DuckTyping", DuckTyping, "cYo.Common ")
	is_asm("FormUtility", FormUtility, "cYo.Common.Windows")
	ok("identity: IRefreshDisplay is reachable", IRefreshDisplay is not None)

#@Name  P4.8 a script class derives from a facade control
#@Hook  Startup
def derived_class():
	clr.AddReferenceByPartialName("System.Windows.Forms")
	clr.AddReferenceByPartialName("System.Drawing")
	from System.Windows.Forms import Control
	class UIControl(Control):
		def ShowInfo(self, books):
			pass
	ok("identity: class UIControl(Control) is defined", True, UIControl.__bases__[0].__name__)

#@Name  P4.17 System.Web.Extensions (JavaScriptSerializer) by the names of the Framework
#@Hook  Startup
def web_extensions():
	clr.AddReference("System.Web.Extensions")
	import System
	text = System.Web.Script.Serialization.JavaScriptSerializer().Serialize({"url": "http://x/a"})
	ok("identity: JavaScriptSerializer serializes a dictionary", text == '{"url":"http://x/a"}', text)
	reply = System.Web.Script.Serialization.JavaScriptSerializer().Deserialize('{"html": "<b>"}', System.Collections.Generic.Dictionary[str, System.Object])
	found, value = reply.TryGetValue("html")
	ok("identity: JavaScriptSerializer deserializes into Dictionary[str, object]", found and value == "<b>", value)
	a = clr.GetClrType(System.Web.Script.Serialization.JavaScriptSerializer).Assembly.GetName()
	ok("identity: JavaScriptSerializer is in System.Web.Extensions 10.0.0.0", a.Name + " " + str(a.Version) == "System.Web.Extensions 10.0.0.0", a.Name + " " + str(a.Version))
