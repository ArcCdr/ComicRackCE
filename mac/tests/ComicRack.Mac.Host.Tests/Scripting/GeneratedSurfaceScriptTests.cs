using System.IO;
using ComicRack.Mac.Host.Scripting;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>
	/// A script sees the generated surface of the WinForms and GDI+ facades (P4.7): every type and enum of the reference by its name, members that are not implemented yet
	/// fail with the standard message instead of an AttributeError, and a Python class can derive from a facade control and override its virtual members.
	/// </summary>
	public class GeneratedSurfaceScriptTests
	{
		[Fact]
		public void A_script_reaches_enums_types_and_stubs_of_the_whole_surface_and_derives_from_a_facade_control()
		{
			using (var temp = new TempFolder())
			{
				string forms = typeof(System.Windows.Forms.Control).Assembly.GetName().Name;
				string drawing = typeof(System.Drawing.Bitmap).Assembly.GetName().Name;
				string script = @"
import clr
clr.AddReference('" + forms + @"')
clr.AddReference('" + drawing + @"')
from System.Windows.Forms import *
from System.Drawing import *
from System.Drawing.Drawing2D import GraphicsPath, LinearGradientMode
from System.Drawing.Printing import PrintDocument

class UIControl(Control):
	def OnPaint(self, e):
		pass
	def OnResize(self, e):
		pass

#@Name Surface
#@Hook Books
def Surface(books):
	print 'enums', int(DockStyle.Fill), int(AnchorStyles.Top | AnchorStyles.Left), DialogResult.OK, FormBorderStyle.Sizable, int(LinearGradientMode.Vertical)
	print 'types', Button.__name__, ListView.__name__, DataGridView.__name__, ToolStripMenuItem.__name__, GraphicsPath.__name__, PrintDocument.__name__, Font.__name__
	for factory in (Label, Button, TextBox, ListView, OpenFileDialog, PrintDocument):
		try:
			factory()
			print 'created', factory.__name__
		except Exception, e:
			print 'ctor', e.message
	c = UIControl()
	print 'subclass', c.GetType().BaseType.Name, c.Visible
	print 'property', repr(c.Text)
	try:
		c.AccessibilityObject
	except Exception, e:
		print 'stub', e.message
	try:
		Application.Restart()
	except Exception, e:
		print 'static', e.message
	c.Dispose()
	print 'disposed', c.IsDisposed
";
				string dir = ScriptTestSupport.WriteScripts(temp, "scripts", ("surface.py", script));
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
				{
					ScriptTestSupport.Add(host, ScriptTestSupport.Book("A", "1"));
					var output = new StringWriter();
					var error = new StringWriter();
					int code = ScriptRunner.Run(host, Path.Combine(dir, "surface.py"), "Books", null, output, error);
					string text = output.ToString();
					Assert.True(code == 0, text + error);
					const string suffix = " is not yet supported on macOS (ComicRack script compatibility layer)";
					Assert.Contains("enums 5 5 OK Sizable 1", text);
					Assert.Contains("types Button ListView DataGridView ToolStripMenuItem GraphicsPath PrintDocument Font", text);
					Assert.Contains("created Label", text);
					Assert.Contains("created Button", text);
					Assert.Contains("created TextBox", text);
					Assert.Contains("ctor System.Windows.Forms.ListView.ctor" + suffix, text);
					Assert.Contains("ctor System.Windows.Forms.OpenFileDialog.ctor" + suffix, text);
					Assert.Contains("ctor System.Drawing.Printing.PrintDocument.ctor" + suffix, text);
					Assert.DoesNotContain("created ListView", text);
					Assert.Contains("subclass Control True", text);
					Assert.Contains("property ''", text);
					Assert.Contains("stub System.Windows.Forms.Control.AccessibilityObject" + suffix, text);
					Assert.Contains("static System.Windows.Forms.Application.Restart" + suffix, text);
					Assert.Contains("disposed True", text);
				}
			}
		}
	}
}
