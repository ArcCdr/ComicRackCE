using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;

namespace S6.Cases
{
	/// <summary>
	/// The assembly identity matrix: which spelling of a reference line finds the facade, which assembly the types come from, and how a compiled plug-in
	/// whose reference names System.Windows.Forms 4.0.0.0 with the framework's public key token binds. No window server is needed.
	/// Each scenario runs in a new engine, as every PythonCommand does.
	/// </summary>
	public static class IdentityCases
	{
		public const string FrameworkFormsToken = "b77a5c561934e089";

		private static ScriptEngine NewEngine()
		{
			ScriptEngine engine = Python.CreateEngine();
			engine.Runtime.LoadAssembly(typeof(ArgumentNullException).Assembly);
			return engine;
		}

		public static void Run(Report report, IReadOnlyList<string> pluginPaths, string part)
		{
			report.Case = "I01";
			bool references = part == "all" || part == "references";
			if (references)
			{
				Assembly facade = typeof(System.Windows.Forms.Form).Assembly;
				report.Info("facade assembly", facade.GetName().FullName + (facade.GetName().GetPublicKeyToken() is byte[] t && t.Length > 0 ? "" : " (unsigned)"));
				report.Info("System.Drawing.Common next to the host", File.Exists(Path.Combine(AppContext.BaseDirectory, "System.Drawing.Common.dll")) ? "present" : "absent");
			}
			string[] forms =
			{
				"clr.AddReference('System.Windows.Forms')",
				"clr.AddReferenceByPartialName('System.Windows.Forms')",
				"clr.AddReference('System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=" + FrameworkFormsToken + "')",
				"clr.AddReferenceByName('System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=" + FrameworkFormsToken + "')",
				"clr.AddReferenceByPartialName('System.Windows.Forms, Version=4.0.0.0')",
			};
			foreach (string line in references ? forms : new string[0])
			{
				Scenario(report, line, "from System.Windows.Forms import Form, Control, DialogResult", "Form", expected: "System.Windows.Forms");
			}
			// File-name spellings are IronPython's own business (they need the folder on sys.path); information only.
			foreach (string line in references ? new[] { "clr.AddReference('System.Windows.Forms.dll')", "clr.AddReferenceToFile('System.Windows.Forms.dll')" } : new string[0])
			{
				Scenario(report, line, "from System.Windows.Forms import Form, Control, DialogResult", "Form", expected: null);
			}

			string[] drawing =
			{
				"clr.AddReference('System.Drawing')",
				"clr.AddReferenceByPartialName('System.Drawing')",
				"clr.AddReference('System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a')",
				"clr.AddReference('System.Drawing.Common')",
			};
			foreach (string line in references ? drawing : new string[0])
			{
				Scenario(report, line, "from System.Drawing import Point, Size, Color, Bitmap, Graphics", "Point", expected: null);
				Scenario(report, line, "from System.Drawing import Point, Size, Color, Bitmap, Graphics", "Bitmap", expected: null);
				BitmapScenario(report, line);
			}

			foreach (string plugin in pluginPaths)
			{
				PluginScenario(report, plugin, part);
			}
			report.Case = string.Empty;
		}

		private static void Scenario(Report report, string referenceLine, string importLine, string typeName, string expected)
		{
			ScriptEngine engine = NewEngine();
			ScriptScope scope = engine.CreateScope();
			string name = "'" + referenceLine + "' -> " + typeName;
			try
			{
				engine.Execute("import clr\n" + referenceLine + "\n" + importLine + "\nasm = clr.GetClrType(" + typeName + ").Assembly.GetName().FullName", scope);
				string assembly = scope.GetVariable<string>("asm");
				if (expected == null)
				{
					report.Info(name, "type from " + assembly);
				}
				else
				{
					report.Check(name, assembly.StartsWith(expected + ",", StringComparison.Ordinal), "type from " + assembly);
				}
			}
			catch (Exception e)
			{
				if (expected == null)
				{
					report.Info(name, "raised " + Ctx.Describe(e));
				}
				else
				{
					report.Check(name, false, "raised " + Ctx.Describe(e));
				}
			}
		}

		private static void BitmapScenario(Report report, string referenceLine)
		{
			ScriptEngine engine = NewEngine();
			ScriptScope scope = engine.CreateScope();
			string name = "'" + referenceLine + "' -> Bitmap(8, 8)";
			try
			{
				engine.Execute("import clr\n" + referenceLine + "\nfrom System.Drawing import *\nb = Bitmap(8, 8)\nw = b.Width\nasm = clr.GetClrType(Bitmap).Assembly.GetName().Name", scope);
				report.Info(name, "works, width " + scope.GetVariable<int>("w") + ", Bitmap from " + scope.GetVariable<string>("asm"));
			}
			catch (Exception e)
			{
				report.Info(name, "raised " + Ctx.Describe(e));
			}
		}

		/// <summary>The System.Windows.Forms reference in the metadata of a compiled plug-in: version and public key token.</summary>
		private static string DeclaredFormsReference(string path)
		{
			using (FileStream stream = File.OpenRead(path))
			using (System.Reflection.PortableExecutable.PEReader pe = new System.Reflection.PortableExecutable.PEReader(stream))
			{
				System.Reflection.Metadata.MetadataReader md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
				foreach (System.Reflection.Metadata.AssemblyReferenceHandle h in md.AssemblyReferences)
				{
					System.Reflection.Metadata.AssemblyReference r = md.GetAssemblyReference(h);
					string name = md.GetString(r.Name);
					if (name == "System.Windows.Forms")
					{
						byte[] token = md.GetBlobBytes(r.PublicKeyOrToken);
						return name + ", Version=" + r.Version + ", PublicKeyToken=" + (token.Length == 0 ? "null" : BitConverter.ToString(token).Replace("-", string.Empty).ToLowerInvariant());
					}
				}
			}
			return "(no System.Windows.Forms reference)";
		}

		private static void PluginScenario(Report report, string path, string part)
		{
			string label = Path.GetFileName(path);
			report.Info("plug-in " + label + " declares", DeclaredFormsReference(path));
			if (part == "python" || part == "all")
			{
				PluginViaPython(report, path, label);
			}
			if (part == "loadfrom" || part == "all")
			{
				PluginViaLoadFrom(report, path, label);
			}
		}

		private static void PluginViaPython(Report report, string path, string label)
		{
			// Python: clr.AddReferenceToFileAndPath, then call into the plug-in.
			ScriptEngine engine = NewEngine();
			ScriptScope scope = engine.CreateScope();
			try
			{
				engine.Execute("import clr\nclr.AddReferenceToFileAndPath(r'" + path + "')\nimport S6Plugin\nf = S6Plugin.Dialogs.MakeForm()\nasm = clr.GetClrType(type(f)).Assembly.GetName().FullName\ntext = S6Plugin.Dialogs.Describe()", scope);
				string asm = scope.GetVariable<string>("asm");
				report.Check("plug-in " + label + " via clr.AddReferenceToFileAndPath binds its System.Windows.Forms reference to the facade", asm.StartsWith("System.Windows.Forms,", StringComparison.Ordinal), "Form from " + asm + "; " + scope.GetVariable<string>("text"));
			}
			catch (Exception e)
			{
				report.Check("plug-in " + label + " via clr.AddReferenceToFileAndPath binds its System.Windows.Forms reference to the facade", false, "raised " + Ctx.Describe(e));
			}
		}

		private static void PluginViaLoadFrom(Report report, string path, string label)
		{
			// C#: Assembly.LoadFrom and reflection, as a .NET host would.
			try
			{
				Assembly plugin = Assembly.LoadFrom(path);
				object form = plugin.GetType("S6Plugin.Dialogs").GetMethod("MakeForm").Invoke(null, null);
				// By name: a typeof of the facade would load it before the plug-in had a chance to bind it.
				bool isForm = form.GetType().FullName == "System.Windows.Forms.Form" && form.GetType().Assembly.GetName().Name == "System.Windows.Forms";
				report.Check("plug-in " + label + " via Assembly.LoadFrom: its Form is the facade's Form", isForm, form.GetType().Assembly.GetName().FullName);
			}
			catch (Exception e)
			{
				report.Check("plug-in " + label + " via Assembly.LoadFrom: its Form is the facade's Form", false, "raised " + Ctx.Describe(e));
			}
		}
	}
}
