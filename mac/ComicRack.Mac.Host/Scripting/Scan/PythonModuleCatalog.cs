using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;

namespace ComicRack.Mac.Host.Scripting.Scan
{
	/// <summary>Which Python modules a script can import in the plugin host: the built-in modules of IronPython and the Python 2.7 standard library next to the program (<c>Lib</c>).</summary>
	public sealed class PythonModuleCatalog
	{
		private readonly HashSet<string> builtin;
		private readonly string lib;

		private PythonModuleCatalog(HashSet<string> builtin, string lib)
		{
			this.builtin = builtin;
			this.lib = lib;
		}

		public static PythonModuleCatalog Create(string programFolder = null)
		{
			programFolder ??= AppContext.BaseDirectory;
			ScriptEngine engine = Python.CreateEngine();
			var names = new HashSet<string>(StringComparer.Ordinal);
			dynamic sys = Python.GetSysModule(engine);
			foreach (object n in sys.builtin_module_names)
			{
				names.Add(Convert.ToString(n));
			}
			names.Add("clr");
			return new PythonModuleCatalog(names, Path.Combine(programFolder, "Lib"));
		}

		/// <summary><c>builtin</c>, <c>stdlib</c> or <c>missing</c> for the top-level name of a module.</summary>
		public string Classify(string module)
		{
			if (builtin.Contains(module))
			{
				return "builtin";
			}
			if (File.Exists(Path.Combine(lib, module + ".py")) || File.Exists(Path.Combine(lib, module, "__init__.py")))
			{
				return "stdlib";
			}
			return "missing";
		}
	}
}
