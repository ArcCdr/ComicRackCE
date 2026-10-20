using System;
using System.IO;
using System.Linq;
using System.Text;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;

namespace S6.Host
{
	/// <summary>A script file run in its own engine and scope the way PythonCommand does it (the ComicRack variable, ScriptPath, the script folder on the path).</summary>
	public sealed class PythonScript
	{
		public ScriptEngine Engine { get; }

		public ScriptScope Scope { get; }

		public MemoryStream Output { get; } = new MemoryStream();

		public PythonScript(string file, ComicRackStub comicRack)
		{
			Engine = Python.CreateEngine();
			Engine.Runtime.LoadAssembly(typeof(ArgumentNullException).Assembly);
			Engine.Runtime.LoadAssembly(typeof(Book).Assembly);
			Engine.Runtime.IO.SetOutput(Output, new UTF8Encoding(false));
			Engine.Runtime.IO.SetErrorOutput(Output, new UTF8Encoding(false));
			Engine.SetSearchPaths(Engine.GetSearchPaths().Concat(new[] { Path.GetDirectoryName(file), Path.Combine(AppContext.BaseDirectory, "Lib") }).Distinct().ToArray());
			Scope = Engine.CreateScope();
			Scope.SetVariable("ComicRack", comicRack);
			Scope.SetVariable("ScriptPath", Path.GetDirectoryName(file));
			Engine.CreateScriptSourceFromFile(file).Execute(Scope);
		}

		public T Get<T>(string name) where T : class => Scope.GetVariable<T>(name);

		public string Printed => new UTF8Encoding(false).GetString(Output.ToArray());
	}
}
