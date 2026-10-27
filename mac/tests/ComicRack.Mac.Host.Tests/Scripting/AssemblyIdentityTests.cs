using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ComicRack.Mac.Host.Scripting;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;
using cYo.Common.Windows.Forms;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>
	/// P4.8: the names scripts use for the framework and the Core resolve to the facades and the shadow assemblies as they do on Windows. The facades are built as
	/// <c>System.Windows.Forms</c> and <c>System.Drawing.Common</c> (10.0.0.0), the shadow assemblies as <c>ComicRack.Engine</c>, <c>ComicRack.Plugins</c> and <c>cYo.Common</c>, and the
	/// unmodified bundled scripts load. Nothing here names a plug-in.
	/// </summary>
	public class AssemblyIdentityTests
	{
		[Fact]
		public void The_assemblies_carry_the_names_of_the_assemblies_they_stand_for()
		{
			Assert.Equal("System.Windows.Forms", typeof(System.Windows.Forms.Control).Assembly.GetName().Name);
			Assert.Equal("System.Drawing.Common", typeof(System.Drawing.Bitmap).Assembly.GetName().Name);
			Assert.Equal(new Version(10, 0, 0, 0), typeof(System.Windows.Forms.Control).Assembly.GetName().Version);
			Assert.Equal(new Version(10, 0, 0, 0), typeof(System.Drawing.Bitmap).Assembly.GetName().Version);
			Assert.Equal("ComicRack.Engine", typeof(ComicBook).Assembly.GetName().Name);
			Assert.Equal("ComicRack.Plugins", typeof(PluginEngine).Assembly.GetName().Name);
			Assert.Equal("cYo.Common", typeof(cYo.Common.Threading.ThreadUtility).Assembly.GetName().Name);
			Assert.Equal("cYo.Common.Windows", typeof(cYo.Common.Windows.Forms.FormUtility).Assembly.GetName().Name);
		}

		[Fact]
		public void The_output_folder_holds_one_System_Drawing_Common_and_it_is_the_facade()
		{
			// IronPython depends on the real System.Drawing.Common 4.7.0, whose Bitmap throws on macOS; the facade project of the same name replaces it.
			string file = Path.Combine(AppContext.BaseDirectory, "System.Drawing.Common.dll");
			Assert.True(File.Exists(file));
			Assert.Equal(typeof(System.Drawing.Bitmap).Assembly.Location, file);
			var name = System.Reflection.AssemblyName.GetAssemblyName(file);
			Assert.Equal(new Version(10, 0, 0, 0), name.Version);
			Assert.Empty(Directory.GetFiles(AppContext.BaseDirectory, "cYo.Compat.*.dll"));
			foreach (string old in new[] { "cYo.Common.Core.dll", "ComicRack.Engine.Core.dll", "ComicRack.Plugins.Core.dll" })
			{
				Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, old)), old + " is a stale file of a build before P4.8");
			}
		}

		private const string Probe = @"
import clr
clr.AddReferenceByPartialName('System.Windows.Forms')
clr.AddReferenceByPartialName('System.Drawing')
clr.AddReference('System.Windows.Forms')
clr.AddReference('System.Drawing')
clr.AddReferenceByPartialName('ComicRack.Engine')
clr.AddReferenceByPartialName('ComicRack.Plugins')
clr.AddReferenceByPartialName('cYo.Common')
clr.AddReferenceByPartialName('cYo.Common.Windows')
from System.Windows.Forms import Form, Control, MessageBox, DialogResult
from System.Drawing import Bitmap, Color, Graphics, Font
from cYo.Projects.ComicRack.Engine import ComicBook, IRefreshDisplay
from cYo.Common.Windows.Forms import FormUtility

def asm(t):
	a = clr.GetClrType(t).Assembly.GetName()
	return a.Name + ' ' + str(a.Version)

#@Name Identity
#@Hook Books
def Identity(books):
	print 'Form', asm(Form)
	print 'Control', asm(Control)
	print 'MessageBox', asm(MessageBox)
	print 'Bitmap', asm(Bitmap)
	print 'Graphics', asm(Graphics)
	print 'Font', asm(Font)
	print 'Color', asm(Color)
	print 'ComicBook', asm(ComicBook)
	print 'FormUtility', asm(FormUtility)
";

		private static string RunProbe(string text, string hook = "Books", params string[] extraFiles)
		{
			using (var temp = new TempFolder())
			{
				string dir = ScriptTestSupport.WriteScripts(temp, "scripts", ("probe.py", text));
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
				{
					ScriptTestSupport.Add(host, ScriptTestSupport.Book("A", "1"));
					var output = new StringWriter();
					var error = new StringWriter();
					int code = ScriptRunner.Run(host, Path.Combine(dir, "probe.py"), hook, null, output, error);
					Assert.True(code == 0, output.ToString() + error);
					return output.ToString();
				}
			}
		}

		[Fact]
		public void A_script_imports_Form_Bitmap_and_Color_by_the_names_of_the_originals_and_reaches_the_facades()
		{
			string text = RunProbe(Probe);
			Assert.Contains("Form System.Windows.Forms 10.0.0.0", text);
			Assert.Contains("Control System.Windows.Forms 10.0.0.0", text);
			Assert.Contains("MessageBox System.Windows.Forms 10.0.0.0", text);
			// the framework's System.Drawing forwards to System.Drawing.Common: the facade answers (the framework one throws on macOS)
			Assert.Contains("Bitmap System.Drawing.Common 10.0.0.0", text);
			Assert.Contains("Graphics System.Drawing.Common 10.0.0.0", text);
			Assert.Contains("Font System.Drawing.Common 10.0.0.0", text);
			// Color is a type of the framework (System.Drawing.Primitives), shared with the facade
			Assert.Contains("Color System.Drawing.Primitives", text);
			Assert.Contains("ComicBook ComicRack.Engine", text);
			Assert.Contains("FormUtility cYo.Common.Windows 1.0.0.0", text);
			Assert.Single(AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "System.Drawing.Common"));
			Assert.Single(AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "System.Windows.Forms"));
		}

		[Theory]
		[InlineData("clr.AddReference('System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089')")]
		[InlineData("clr.AddReference('System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a')")]
		[InlineData("clr.AddReferenceByName('System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089')")]
		[InlineData("clr.AddReferenceByPartialName('System.Windows.Forms')")]
		[InlineData("clr.AddReference('System.Drawing.Common')")]
		public void Strong_name_spellings_of_the_framework_assemblies_resolve(string line)
		{
			string text = RunProbe("import clr\n" + line + "\n#@Name Spelling\n#@Hook Books\ndef Spelling(books):\n\tprint 'loaded'\n");
			Assert.Contains("loaded", text);
		}

		[Fact]
		public void Every_unmodified_bundled_script_loads_without_a_reference_error()
		{
			using (var temp = new TempFolder())
			{
				string dir = ScriptTestSupport.CopyOfBundledScripts(Path.Combine(temp.Path, "Scripts"));
				foreach (string file in Directory.GetFiles(ScriptTestSupport.BundledScripts))
				{
					Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(dir, Path.GetFileName(file)))); // unmodified
				}
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
				{
					int scripts = 0;
					foreach (string file in Directory.GetFiles(dir, "*.py").OrderBy(f => f, StringComparer.Ordinal))
					{
						IReadOnlyList<Command> commands = host.Scripts.LoadCommands(file);
						Assert.NotEmpty(commands);
						// the top level of the script (imports, clr.AddReference lines, class definitions) runs when the first command compiles
						foreach (PythonCommand command in commands.OfType<PythonCommand>().Take(1))
						{
							command.PreCompile(handleException: false);
						}
						scripts++;
					}
					Assert.Equal(Directory.GetFiles(ScriptTestSupport.BundledScripts, "*.py").Length, scripts);
				}
			}
		}

		private sealed class CountingRefresh : IRefreshDisplay
		{
			public int Calls;

			public void RefreshDisplay() => Calls++;
		}

		[Fact]
		public void RefreshView_py_reaches_the_refresh_service_of_the_active_window_through_cYo_Common_Windows()
		{
			using (var temp = new TempFolder())
			{
				string dir = ScriptTestSupport.CopyOfBundledScripts(Path.Combine(temp.Path, "Scripts"));
				var ui = new HeadlessScriptUi();
				var refresh = new CountingRefresh();
				ui.Services[typeof(IRefreshDisplay)] = refresh;
				using (MacHost host = ScriptTestSupport.Open(temp, ui: ui, scriptFolders: dir))
				{
					ScriptTestSupport.Add(host, ScriptTestSupport.Book("A", "1"));
					var error = new StringWriter();
					Assert.Equal(0, ScriptRunner.Run(host, Path.Combine(dir, "RefreshView.py"), "Books", null, new StringWriter(), error));
					Assert.True(string.IsNullOrEmpty(error.ToString()), error.ToString());
					Assert.Equal(1, refresh.Calls);
					Assert.Contains("FindActiveService: IRefreshDisplay", ui.Requests);
				}
				// the resolver is taken out again with the host
				Assert.Null(cYo.Common.Windows.Forms.ActiveServices.Resolver);
			}
		}

		[Fact]
		public void The_service_lookup_asks_the_program_and_answers_only_with_objects_of_the_asked_type()
		{
			var refresh = new CountingRefresh();
			var previous = cYo.Common.Windows.Forms.ActiveServices.Resolver;
			try
			{
				cYo.Common.Windows.Forms.ActiveServices.Resolver = type => type == typeof(IRefreshDisplay) ? refresh : (object)"not a service";
				Assert.Same(refresh, cYo.Common.Windows.Forms.FormUtility.FindActiveService<IRefreshDisplay>());
				Assert.Null(cYo.Common.Windows.Forms.ActiveServices.Resolve(typeof(IDisposable)));
				cYo.Common.Windows.Forms.ActiveServices.Resolver = null;
				Assert.Null(cYo.Common.Windows.Forms.ActiveServices.Resolve(typeof(IRefreshDisplay)));
			}
			finally
			{
				cYo.Common.Windows.Forms.ActiveServices.Resolver = previous;
			}
		}

		[Fact]
		public void The_dpi_scale_is_one_because_the_facades_measure_in_logical_points()
		{
			Assert.Equal(new System.Drawing.PointF(1f, 1f), cYo.Common.Windows.Forms.FormUtility.DpiScale);
			Assert.Equal(30, cYo.Common.Windows.Forms.FormUtility.ScaleDpiX(30));
			Assert.Equal(7.5f, cYo.Common.Windows.Forms.FormUtility.ScaleDpiY(7.5f));
			Assert.Equal(new System.Drawing.Size(65, 20), new System.Drawing.Size(65, 20).ScaleDpi());
			Assert.Equal(new System.Drawing.Rectangle(1, 2, 3, 4), new System.Drawing.Rectangle(1, 2, 3, 4).ScaleDpi());
		}
	}
}
