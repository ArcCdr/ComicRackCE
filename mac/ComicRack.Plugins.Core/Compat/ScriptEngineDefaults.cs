using System.Reflection;
using Microsoft.Scripting.Hosting;

namespace cYo.Projects.ComicRack.Plugins
{
	/// <summary>
	/// What an IronPython engine of the macOS build has loaded when its first script runs (called by the pass B copy of <c>PythonCommand.CreateEngine</c>).
	/// IronPython on the .NET Framework starts with mscorlib and System.dll: <c>from System import Uri</c> and <c>from System.Diagnostics import Process</c> work without
	/// <c>clr.AddReference("System")</c>. On .NET 10 those types live in assemblies of their own (System.Private.Uri, System.Diagnostics.Process, ...) that the engine only sees
	/// through the System.dll facade of the runtime, whose type forwards it enumerates. Loading that facade here is what the Framework's own start-up does.
	/// </summary>
	public static class ScriptEngineDefaults
	{
		public static void Prepare(ScriptEngine engine)
		{
			engine.Runtime.LoadAssembly(Assembly.Load("System"));
			AddExit(engine);
		}

		/// <summary>
		/// <c>exit</c> and <c>quit</c> as the Python <c>site</c> module defines them (a script such as <c>except: exit</c> evaluates the name; calling it raises SystemExit). The hosting API does not
		/// import <c>site</c>, so without this a script that names <c>exit</c> fails with a NameError (P4.17.5, found when a plugin's <c>Translate()</c> ran to its end).
		/// </summary>
		private static void AddExit(ScriptEngine engine)
		{
			const string code = "import __builtin__\n" +
				"class _Quitter(object):\n" +
				"    def __init__(self, name):\n" +
				"        self.name = name\n" +
				"    def __repr__(self):\n" +
				"        return 'Use %s() or Ctrl-Z plus Return to exit' % self.name\n" +
				"    def __call__(self, code=None):\n" +
				"        raise SystemExit(code)\n" +
				"__builtin__.exit = _Quitter('exit')\n" +
				"__builtin__.quit = _Quitter('quit')\n";
			engine.Execute(code, engine.CreateScope());
		}
	}
}
