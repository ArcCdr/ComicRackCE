using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ComicRack.Mac.Host.Scripting.Scan
{
	/// <summary>
	/// The whole gap analysis of a list of plugin packages (P4.17): the reference surface, the index of the program and the runtime, the install and load probe in the plugin host, the scan, and the table of
	/// each plugin. Used by <c>crcli plugin-gap</c> and by the tests.
	/// </summary>
	public static class PluginGapRunner
	{
		public static PluginGapReport Build(IEnumerable<string> packages, GapRules rules, string referencePack = null)
		{
			ApiCatalog catalog = ReferenceSurface.Load(referencePack);
			using TypeIndex index = TypeIndex.ForProgram();
			PythonModuleCatalog modules = PythonModuleCatalog.Create();
			var plugins = new List<PluginGap>();
			foreach (string package in packages)
			{
				PluginFacts facts = PluginProbe.Probe(package);
				var scanner = new ScriptScanner(catalog) { ForeignTypeLookup = (ns, name) => index.Find(ns, name) != null };
				ScanReport scan = scanner.Scan(new[] { package });
				plugins.Add(PluginGapBuilder.Build(scan, facts, index, rules ?? GapRules.Empty, catalog, modules));
			}
			return new PluginGapReport
			{
				Plugins = plugins,
				ReferenceSource = Describe(catalog.Source),
				ReferenceTypes = catalog.TypeCount,
				Runtime = ".NET " + Environment.Version,
				IndexedAssemblies = index.AssemblyCount,
			};
		}

		/// <summary>The catalog source without the machine path: the version of the reference pack, or the kind of source.</summary>
		private static string Describe(string source)
		{
			Match m = Regex.Match(source ?? string.Empty, @"microsoft\.windowsdesktop\.app\.ref[/\\]([^/\\]+)[/\\]", RegexOptions.IgnoreCase);
			if (m.Success)
			{
				return "reference assemblies of the Windows Desktop pack " + m.Groups[1].Value;
			}
			return (source ?? "unknown").StartsWith("reference assemblies", StringComparison.Ordinal) ? "reference assemblies" : source ?? "unknown";
		}
	}
}
