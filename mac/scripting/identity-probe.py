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

#@Name  P4.17.3 The WPF assemblies by the names scripts use at the top level
#@Hook  Startup
def wpf_assemblies():
	# the top-level lines of a plug-in that builds a WPF dialog: the names of the Framework assemblies, then the namespaces
	clr.AddReference("PresentationFramework")
	clr.AddReference("PresentationCore")
	clr.AddReference("WindowsBase")
	clr.AddReference("System.Xaml")
	import System
	from System.Windows.Markup import XamlReader
	from System.Windows.Media.Imaging import BitmapImage
	from System.Windows import Visibility, Window
	from System.Windows.Interop import WindowInteropHelper
	is_asm("XamlReader", XamlReader, "PresentationFramework 10.0.0.0")
	is_asm("BitmapImage", BitmapImage, "PresentationCore 10.0.0.0")
	is_asm("Window", Window, "PresentationFramework 10.0.0.0")
	is_asm("WindowInteropHelper", WindowInteropHelper, "PresentationFramework 10.0.0.0")
	# the types of WindowsBase are declared in the PresentationCore facade (the runtime's own WindowsBase.dll wins over an app-local file of that name)
	is_asm("Visibility", Visibility, "PresentationCore 10.0.0.0")
	is_asm("DependencyObject", System.Windows.DependencyObject, "PresentationCore 10.0.0.0")
	ok("identity: WindowsBase by name is the runtime's file (type forwards only)", System.Reflection.Assembly.Load("WindowsBase").GetName().Version.Major == 4)
	ok("identity: Visibility has the members of the enumeration", int(Visibility.Visible) == 0 and int(Visibility.Hidden) == 1 and int(Visibility.Collapsed) == 2, Visibility.Collapsed)
	try:
		BitmapImage()
		ok("identity: a WPF type throws until it is provided", False)
	except Exception as e:
		ok("identity: a WPF type throws the standard message", "not yet supported on macOS" in e.message, e.message)
	# the strong names of the Framework assemblies bind to the facades
	clr.AddReference("PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35")
	clr.AddReference("WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35")
	ok("identity: the strong names of the WPF assemblies are accepted", True)

#@Name  P4.17.3 The names of System.dll are there without a reference line
#@Hook  Startup
def default_references():
	# IronPython on the .NET Framework starts with System.dll loaded; the plug-ins write "from System import Uri" without clr.AddReference("System")
	from System import Uri, UriKind
	u = Uri("http://example.com/a/b?c=1", UriKind.Absolute)
	ok("identity: System.Uri is importable at the top of a script", u.Host == "example.com", u.Host)

#@Name  P4.17.4 Microsoft.VisualBasic FileSystem with UIOption and RecycleOption
#@Hook  Startup
def visual_basic_file_system():
	# the top-level lines of a plug-in that deletes through the Recycle Bin of Windows; nothing here touches the Trash
	clr.AddReference("Microsoft.VisualBasic")
	import System
	from System.IO import Path, File
	from Microsoft.VisualBasic.FileIO import FileSystem, UIOption, RecycleOption, UICancelOption
	is_asm("FileSystem", FileSystem, "Microsoft.VisualBasic 10.0.1.0")
	is_asm("UIOption", UIOption, "Microsoft.VisualBasic.Core")
	ok("identity: the Framework's strong name binds to the same assembly", System.Reflection.Assembly.Load("Microsoft.VisualBasic, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").GetName().Version.ToString() == "10.0.1.0")
	folder = Path.Combine(Path.GetTempPath(), "crmac-vb-" + System.Guid.NewGuid().ToString("N"))
	System.IO.Directory.CreateDirectory(folder)
	try:
		source = Path.Combine(folder, "a.cbz")
		File.WriteAllText(source, "x")
		FileSystem.CopyFile(source, Path.Combine(folder, "b.cbz"), UIOption.OnlyErrorDialogs)
		ok("identity: CopyFile with a UIOption copies", File.Exists(Path.Combine(folder, "b.cbz")))
		FileSystem.DeleteFile(source, UIOption.OnlyErrorDialogs, RecycleOption.DeletePermanently)
		ok("identity: DeleteFile with a UIOption deletes", not File.Exists(source))
		try:
			FileSystem.DeleteFile(Path.Combine(folder, "missing.cbz"), UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin)
			ok("identity: DeleteFile of a missing file throws", False)
		except System.IO.FileNotFoundException:
			ok("identity: DeleteFile of a missing file throws FileNotFoundException", True)
	finally:
		System.IO.Directory.Delete(folder, True)
