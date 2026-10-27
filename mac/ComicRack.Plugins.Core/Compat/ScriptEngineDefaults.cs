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
		}
	}
}
