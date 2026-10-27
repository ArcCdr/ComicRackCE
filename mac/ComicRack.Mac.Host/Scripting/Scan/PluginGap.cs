using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ComicRack.Mac.Host.Scripting.Scan
{
	/// <summary>One line of a gap table: something a plugin reaches and whether the macOS build has it.</summary>
	public sealed class GapRow
	{
		public string Name { get; init; }

		public int Uses { get; init; }

		/// <summary>The number of scripts or assemblies of the plugin that use it.</summary>
		public int Units { get; init; }

		/// <summary>real (present and working), stub (present, throws NotSupportedException), partial (some overloads or accessors are stubs, or by-name candidates differ), missing (not there), differs (there, behaves differently on macOS), plugin (defined by the plugin itself), python (a member of a Python built-in type), dynamic (no known type has it).</summary>
		public string Status { get; init; }

		/// <summary>facade, .NET 10, program (Core), package (a file of the plugin), builtin, stdlib, none.</summary>
		public string Source { get; init; }

		/// <summary>The task that implements it (stub, partial, missing, differs rows), from the gap rules.</summary>
		public string Owner { get; init; }

		public string Note { get; init; }

		/// <summary>Types that may be the receiver (by-name rows).</summary>
		public string Candidates { get; init; }

		/// <summary>True for the statuses that need a task.</summary>
		public bool NeedsTask => Status == "stub" || Status == "partial" || Status == "missing" || Status == "differs";
	}

	public sealed class GapCommand
	{
		public string Name { get; init; }

		public string Key { get; init; }

		public string Hook { get; init; }

		public bool Enabled { get; init; }

		public string File { get; init; }
	}

	public sealed class GapLoadCheck
	{
		public string File { get; init; }

		public bool Ok { get; init; }

		public string Message { get; init; }

		public int Commands { get; init; }
	}

	/// <summary>What the report says about a plugin besides the scan: the package, the commands it registers and whether its scripts load in the plugin host.</summary>
	public sealed class PluginFacts
	{
		public string Id { get; init; }

		public string PackageFile { get; init; }

		public string Sha256 { get; init; }

		public long Size { get; init; }

		public string Url { get; init; }

		public string Licence { get; init; }

		public string Pin { get; init; }

		public IReadOnlyDictionary<string, string> Ini { get; init; } = new Dictionary<string, string>();

		public IReadOnlyList<GapCommand> Commands { get; init; } = Array.Empty<GapCommand>();

		public IReadOnlyList<GapLoadCheck> LoadChecks { get; init; } = Array.Empty<GapLoadCheck>();
	}

	public sealed class GapFinding
	{
		public string Severity { get; init; }

		public string Kind { get; init; }

		public string Symbol { get; init; }

		public int Units { get; init; }

		public string Message { get; init; }

		/// <summary>The task that handles the finding (rule line <c>owner finding:&lt;kind&gt; &lt;task&gt;</c>), or null.</summary>
		public string Task { get; init; }
	}

	/// <summary>The gap table of one plugin.</summary>
	public sealed class PluginGap
	{
		public PluginFacts Facts { get; init; }

		public int Units { get; init; }

		public int PythonUnits { get; init; }

		public int AssemblyUnits { get; init; }

		public int NativeFiles { get; init; }

		public string Tier { get; init; }

		public IReadOnlyList<string> Hooks { get; init; }

		/// <summary>Typed members of System.Windows.Forms and System.Drawing.</summary>
		public IReadOnlyList<GapRow> Surface { get; init; }

		/// <summary>Member accesses on values whose type the scan could not tell, by name.</summary>
		public IReadOnlyList<GapRow> ByName { get; init; }

		/// <summary>Types and members of .NET 10 (System.*, Microsoft.*).</summary>
		public IReadOnlyList<GapRow> Framework { get; init; }

		/// <summary>Types and members of the Core (ComicRack.Engine, ComicRack.Plugins, cYo.Common).</summary>
		public IReadOnlyList<GapRow> Core { get; init; }

		public IReadOnlyList<GapRow> Modules { get; init; }

		public IReadOnlyList<GapRow> References { get; init; }

		public IReadOnlyList<GapFinding> Findings { get; init; }

		public IEnumerable<GapRow> AllRows => Surface.Concat(ByName).Concat(Framework).Concat(Core).Concat(Modules).Concat(References);
	}

	/// <summary>The gap report: per plugin, what it reaches and what the macOS build has, with a status for every row (P4.17).</summary>
	public sealed class PluginGapReport
	{
		public IReadOnlyList<PluginGap> Plugins { get; init; }

		public string ReferenceSource { get; init; }

		public int ReferenceTypes { get; init; }

		public string Runtime { get; init; }

		public int IndexedAssemblies { get; init; }
	}

	/// <summary>
	/// Builds the gap table of a scanned plugin (P4.17, R11): every API the plugin reaches, classified by what the macOS build has. Generic: it knows no plugin. The scan supplies the reached names (typed members of the
	/// WinForms and GDI+ surface, names of .NET types and members, Python modules, referenced assemblies, accesses on values of unknown type); <see cref="TypeIndex"/> says what the facades, the Core and the runtime
	/// contain and whether a facade member is a stub; <see cref="GapRules"/> says which task implements what is missing and which present members behave differently.
	/// </summary>
	public static class PluginGapBuilder
	{
		private static readonly HashSet<string> PythonBuiltinMembers = new HashSet<string>(StringComparer.Ordinal)
		{
			// str, unicode
			"join", "split", "rsplit", "strip", "lstrip", "rstrip", "replace", "find", "rfind", "index", "rindex", "startswith", "endswith", "lower", "upper", "title", "capitalize", "format", "encode", "decode",
			"count", "splitlines", "zfill", "ljust", "rjust", "center", "isdigit", "isalpha", "isalnum", "isspace", "islower", "isupper", "partition", "rpartition", "translate", "swapcase", "expandtabs",
			// list, dict, set, tuple
			"append", "extend", "insert", "remove", "pop", "sort", "reverse", "keys", "values", "items", "get", "has_key", "setdefault", "update", "clear", "copy", "iteritems", "iterkeys", "itervalues",
			"popitem", "fromkeys", "add", "discard", "union", "intersection", "difference", "issubset", "issuperset",
			// file and stream objects of Python
			"read", "write", "close", "readline", "readlines", "seek", "tell", "flush", "writelines", "next",
			// re match and pattern objects
			"group", "groups", "groupdict", "start", "end", "span", "match", "search", "sub", "subn", "findall", "finditer", "compile", "pattern", "flags",
			// exceptions and objects
			"message", "args", "__class__", "__name__", "__dict__", "__doc__", "__file__", "__module__",
			// datetime, time, collections, itertools
			"strftime", "strptime", "total_seconds", "isoformat", "timetuple", "toordinal", "weekday", "year", "month", "day", "hour", "minute", "second", "microsecond", "days", "seconds",
			"most_common", "namedtuple", "OrderedDict", "defaultdict", "deque", "appendleft", "popleft",
		};

		/// <summary>Builds the table of one plugin from the scan of its files.</summary>
		public static PluginGap Build(ScanReport scan, PluginFacts facts, TypeIndex index, GapRules rules, ApiCatalog surface, PythonModuleCatalog modules)
		{
			IReadOnlyList<ScanUnit> units = scan.Units;
			var usedSurface = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (ScanUnit u in units)
			{
				foreach (KeyValuePair<string, int> t in u.Types)
				{
					usedSurface[t.Key] = (usedSurface.TryGetValue(t.Key, out int n) ? n : 0) + t.Value;
				}
			}

			List<GapRow> surfaceRows = SurfaceRows(units, index, rules);
			ForeignRows(units, index, surface, rules, out List<GapRow> framework, out List<GapRow> core, out HashSet<IndexedType> foreignTypes, out HashSet<string> importedNamespaces);
			var used = new HashSet<IndexedType>(foreignTypes);
			foreach (string full in usedSurface.Keys)
			{
				IndexedType t = index.Find(full);
				if (t != null)
				{
					used.Add(t);
				}
			}
			List<GapRow> byName = ByNameRows(units, index, rules, used, importedNamespaces, modules);
			List<GapRow> moduleRows = ModuleRows(units, modules, rules);
			List<GapRow> referenceRows = ReferenceRows(units, index, rules);

			var findings = units.SelectMany(u => u.Findings.Select(f => (unit: u, finding: f)))
				.GroupBy(x => (x.finding.Severity, x.finding.Kind, x.finding.Symbol))
				.Select(g => new GapFinding { Severity = g.Key.Severity, Kind = g.Key.Kind, Symbol = g.Key.Symbol, Units = g.Select(x => x.unit).Distinct().Count(), Message = g.First().finding.Message, Task = g.Key.Severity == "info" ? null : rules.OwnerOf("finding:" + g.Key.Kind, null) })
				.OrderBy(f => f.Severity == "error" ? 0 : f.Severity == "warning" ? 1 : 2).ThenBy(f => f.Kind, StringComparer.Ordinal).ThenBy(f => f.Symbol, StringComparer.Ordinal)
				.ToList();

			string tier = units.Any(u => u.Tier == "C") ? "C" : units.Any(u => u.Tier == "B") ? "B" : units.Any(u => u.Tier == "A") ? "A" : units.Any(u => u.Tier == "?") ? "?" : "none";
			return new PluginGap
			{
				Facts = facts,
				Units = units.Count,
				PythonUnits = units.Count(u => u.Kind == "python"),
				AssemblyUnits = units.Count(u => u.Kind == "assembly"),
				NativeFiles = units.Count(u => u.Kind == "native"),
				Tier = tier,
				Hooks = units.SelectMany(u => u.Hooks).Distinct(StringComparer.Ordinal).OrderBy(h => h, StringComparer.Ordinal).ToList(),
				Surface = surfaceRows,
				ByName = byName,
				Framework = framework,
				Core = core,
				Modules = moduleRows,
				References = referenceRows,
				Findings = findings,
			};
		}

		private static IEnumerable<T> Ranked<T>(IEnumerable<T> rows, Func<T, int> uses, Func<T, string> name) => rows.OrderByDescending(uses).ThenBy(name, StringComparer.Ordinal);

		// ------------------------------------------------------------------ typed members of the WinForms and GDI+ surface

		private static List<GapRow> SurfaceRows(IReadOnlyList<ScanUnit> units, TypeIndex index, GapRules rules)
		{
			var uses = new Dictionary<string, (int uses, int units)>(StringComparer.Ordinal);
			foreach (ScanUnit u in units)
			{
				foreach (KeyValuePair<string, int> m in u.Members)
				{
					uses.TryGetValue(m.Key, out var old);
					uses[m.Key] = (old.uses + m.Value, old.units + 1);
				}
			}
			var rows = new List<GapRow>();
			foreach (KeyValuePair<string, (int uses, int units)> pair in uses)
			{
				int sep = pair.Key.IndexOf("::", StringComparison.Ordinal);
				string type = pair.Key.Substring(0, sep);
				string member = pair.Key.Substring(sep + 2);
				rows.Add(Classify(index, rules, type, member, pair.Value.uses, pair.Value.units, DisplayType(type) + "." + member));
			}
			return Ranked(rows, r => r.Uses, r => r.Name).ToList();
		}

		private static string DisplayType(string full)
		{
			foreach (string ns in new[] { "System.Windows.Forms.", "System.Drawing." })
			{
				if (full.StartsWith(ns, StringComparison.Ordinal))
				{
					return full.Substring(ns.Length).Replace('+', '.');
				}
			}
			return full.Replace('+', '.');
		}

		/// <summary>The row for a member of a type, by what the index says.</summary>
		private static GapRow Classify(TypeIndex index, GapRules rules, string type, string member, int uses, int units, string name)
		{
			IndexedType t = index.Find(type);
			string status;
			string source = "none";
			if (t == null)
			{
				status = "missing";
			}
			else
			{
				source = SourceOf(t);
				if (string.IsNullOrEmpty(member))
				{
					status = "real";
				}
				else
				{
					IndexedMember m = index.FindMember(t, member, out IndexedType declaring);
					if (m == null)
					{
						status = "missing";
					}
					else
					{
						status = m.Status;
						source = SourceOf(declaring);
					}
				}
			}
			string note = rules.NoteOf(type, member, out string forced);
			if (forced != null && status != "missing")
			{
				status = forced;
			}
			string owner = status == "real" ? null : rules.OwnerOf(type, member);
			return new GapRow { Name = name, Uses = uses, Units = units, Status = status, Source = source, Owner = owner, Note = note };
		}

		private static string SourceOf(IndexedType t) => t == null ? "none" : t.Origin == "program" ? (IsFacade(t.Assembly) ? "facade" : "program") : ".NET 10";

		private static bool IsFacade(string assembly) => assembly == "System.Windows.Forms" || assembly == "System.Drawing.Common" || assembly == "System.Web.Extensions" || assembly == "cYo.Common.Windows";

		// ------------------------------------------------------------------ .NET types and members outside the surface

		private static void ForeignRows(IReadOnlyList<ScanUnit> units, TypeIndex index, ApiCatalog surface, GapRules rules, out List<GapRow> framework, out List<GapRow> core, out HashSet<IndexedType> types, out HashSet<string> imported)
		{
			var names = new Dictionary<string, (int uses, int units)>(StringComparer.Ordinal);
			foreach (ScanUnit u in units)
			{
				foreach (KeyValuePair<string, int> f in u.Foreign)
				{
					string key = f.Key.Replace('+', '.');
					names.TryGetValue(key, out var old);
					names[key] = (old.uses + f.Value, old.units + 1);
				}
			}
			framework = new List<GapRow>();
			core = new List<GapRow>();
			types = new HashSet<IndexedType>();
			imported = new HashSet<string>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, (int uses, int units)> pair in names)
			{
				string full = pair.Key;
				bool isNamespace = index.IsNamespaceOrPrefix(full) || surface.IsNamespace(full);
				if (isNamespace && index.Find(full) == null)
				{
					imported.Add(full);
					continue;
				}
				GapRow row;
				IndexedType t = index.Find(full);
				if (t != null)
				{
					types.Add(t);
					row = Classify(index, rules, t.FullName, null, pair.Value.uses, pair.Value.units, t.FullName.Replace('+', '.'));
				}
				else
				{
					row = null;
					string rest = full;
					int dot;
					while (row == null && (dot = rest.LastIndexOf('.')) > 0)
					{
						rest = rest.Substring(0, dot);
						IndexedType owner = index.Find(rest);
						if (owner != null)
						{
							// a deeper chain (Type.Member.Other): the first member after the type is what the metadata can check
							string first = full.Substring(owner.FullName.Length + 1).Split('.')[0];
							types.Add(owner);
							row = Classify(index, rules, owner.FullName, first, pair.Value.uses, pair.Value.units, owner.FullName.Replace('+', '.') + "." + first);
						}
					}
					if (row == null)
					{
						string note = rules.NoteOf(full, null, out _);
						row = new GapRow { Name = full, Uses = pair.Value.uses, Units = pair.Value.units, Status = "missing", Source = "none", Owner = rules.OwnerOf(full, null), Note = note };
					}
				}
				bool isCore = full.StartsWith("cYo.", StringComparison.Ordinal) || full.StartsWith("ComicRack.", StringComparison.Ordinal);
				(isCore ? core : framework).Add(row);
			}
			framework = MergeByName(framework);
			core = MergeByName(core);
		}

		/// <summary>Two keys can give one row (<c>System.IO.File</c> reached by an import and by a call).</summary>
		private static List<GapRow> MergeByName(List<GapRow> rows)
		{
			return Ranked(rows.GroupBy(r => r.Name).Select(g => g.First() == g.Last() ? g.First() : new GapRow
			{
				Name = g.Key,
				Uses = g.Sum(r => r.Uses),
				Units = g.Max(r => r.Units),
				Status = g.First().Status,
				Source = g.First().Source,
				Owner = g.First().Owner,
				Note = g.First().Note,
			}), r => r.Uses, r => r.Name).ToList();
		}

		// ------------------------------------------------------------------ member accesses on values of unknown type

		private static List<GapRow> ByNameRows(IReadOnlyList<ScanUnit> units, TypeIndex index, GapRules rules, HashSet<IndexedType> used, HashSet<string> imported, PythonModuleCatalog modules)
		{
			var counts = new Dictionary<string, (int uses, int units)>(StringComparer.Ordinal);
			var defined = new HashSet<string>(StringComparer.Ordinal);
			foreach (ScanUnit u in units)
			{
				foreach (KeyValuePair<string, int> n in u.UntypedNames)
				{
					counts.TryGetValue(n.Key, out var old);
					counts[n.Key] = (old.uses + n.Value, old.units + 1);
				}
				if (!IsLibraryCopy(u, modules))
				{
					defined.UnionWith(u.Defined);
				}
			}
			bool InCore(IndexedType t) => t.Origin == "program" && !IsFacade(t.Assembly) && (t.Assembly.StartsWith("ComicRack.", StringComparison.Ordinal) || t.Assembly.StartsWith("cYo.", StringComparison.Ordinal));
			bool InSurface(IndexedType t) => t.Origin == "program" && (t.Assembly == "System.Windows.Forms" || t.Assembly == "System.Drawing.Common");
			bool InImported(IndexedType t) => t.Origin == "runtime" && t.FullName.IndexOf('+') < 0 && imported.Contains(t.FullName.Substring(0, Math.Max(0, t.FullName.LastIndexOf('.'))));
			var importedKey = string.Join("|", imported.OrderBy(x => x, StringComparer.Ordinal));
			var rows = new List<GapRow>();
			foreach (KeyValuePair<string, (int uses, int units)> pair in counts)
			{
				string name = pair.Key;
				GapRow row = null;
				if (PythonBuiltinMembers.Contains(name))
				{
					row = new GapRow { Name = name, Uses = pair.Value.uses, Units = pair.Value.units, Status = "python", Source = "builtin" };
				}
				else if (defined.Contains(name))
				{
					row = new GapRow { Name = name, Uses = pair.Value.uses, Units = pair.Value.units, Status = "plugin", Source = "package" };
				}
				else
				{
					var groups = new (string label, IEnumerable<(IndexedType type, IndexedMember member)> found)[]
					{
						("used types", used.Select(t => (type: t, member: index.FindMember(t, name, out IndexedType d) is IndexedMember m ? (type: d, member: m) : (type: (IndexedType)null, member: (IndexedMember)null))).Where(x => x.member.member != null).Select(x => (x.member.type, x.member.member))),
						("Core", index.TypesDeclaring("core", InCore, name).Select(t => (t, t.Members[name]))),
						("imported namespaces", index.TypesDeclaring("imported:" + importedKey, InImported, name).Select(t => (t, t.Members[name]))),
						("WinForms and GDI+ surface", index.TypesDeclaring("surface", InSurface, name).Select(t => (t, t.Members[name]))),
					};
					foreach ((string label, IEnumerable<(IndexedType type, IndexedMember member)> found) group in groups)
					{
						List<(IndexedType type, IndexedMember member)> list = group.found.GroupBy(x => x.type.FullName).Select(g => g.First()).ToList();
						if (list.Count == 0)
						{
							continue;
						}
						int stubs = list.Count(x => x.member.Status == "stub");
						int real = list.Count(x => x.member.Status == "real");
						string status = stubs == 0 && list.All(x => x.member.Status == "real") ? "real" : real == 0 && stubs == list.Count ? "stub" : "partial";
						var stubTypes = list.Where(x => x.member.Status != "real").ToList();
						string owner = null;
						foreach ((IndexedType type, IndexedMember member) c in stubTypes)
						{
							owner ??= rules.OwnerOf(c.type.FullName, name);
						}
						string candidates = string.Join(", ", list.Take(3).Select(x => DisplayType(x.type.FullName))) + (list.Count > 3 ? ", ... (" + list.Count + " types)" : string.Empty);
						row = new GapRow
						{
							Name = name,
							Uses = pair.Value.uses,
							Units = pair.Value.units,
							Status = status,
							Source = group.label == "Core" ? "program" : group.label == "imported namespaces" ? ".NET 10" : group.label == "used types" ? SourceOf(list[0].type) : "facade",
							Owner = status == "real" ? null : owner,
							Candidates = group.label + ": " + candidates,
						};
						break;
					}
				}
				row ??= new GapRow { Name = name, Uses = pair.Value.uses, Units = pair.Value.units, Status = "dynamic", Source = "none", Candidates = "no known type has a member of this name" };
				rows.Add(row);
			}
			return Ranked(rows, r => r.Uses, r => r.Name).ToList();
		}

		// ------------------------------------------------------------------ Python modules and referenced assemblies

		private static List<GapRow> ModuleRows(IReadOnlyList<ScanUnit> units, PythonModuleCatalog modules, GapRules rules)
		{
			var counts = new Dictionary<string, (int uses, int units)>(StringComparer.Ordinal);
			foreach (ScanUnit u in units)
			{
				if (IsLibraryCopy(u, modules))
				{
					continue; // a copy of a standard library module in the package: its own imports (guarded platform modules, ...) are the library's, not the plugin's
				}
				foreach (KeyValuePair<string, int> m in u.Modules)
				{
					string root = m.Key.Split('.')[0];
					counts.TryGetValue(root, out var old);
					counts[root] = (old.uses + m.Value, old.units + 1);
				}
			}
			var bundledFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (ScanUnit u in units)
			{
				string path = u.Path;
				int bang = path.IndexOf("!/", StringComparison.Ordinal);
				string inner = bang >= 0 ? path.Substring(bang + 2) : path;
				if (inner.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
				{
					string module = inner.Substring(0, inner.Length - 3).Replace('/', '.');
					bundledFiles.Add(module);
					int dot = module.LastIndexOf('.');
					if (dot >= 0)
					{
						bundledFiles.Add(module.Substring(dot + 1));
					}
				}
			}
			var rows = new List<GapRow>();
			foreach (KeyValuePair<string, (int uses, int units)> pair in counts)
			{
				string kind = bundledFiles.Contains(pair.Key) ? "package" : modules.Classify(pair.Key);
				string note = null;
				if (kind == "package" && modules.Classify(pair.Key) != "missing")
				{
					note = "the package carries its own copy; the standard library module of the same name is shadowed";
				}
				rows.Add(new GapRow { Name = pair.Key, Uses = pair.Value.uses, Units = pair.Value.units, Status = kind == "missing" ? "missing" : "real", Source = kind, Note = note, Owner = kind == "missing" ? rules.OwnerOf(pair.Key, null) : null });
			}
			return Ranked(rows, r => r.Uses, r => r.Name).ToList();
		}

		/// <summary>A top-level file of the package that has the name of a standard library module (a plugin may carry its own copy of the Python 2.7 library).</summary>
		internal static bool IsLibraryCopy(ScanUnit u, PythonModuleCatalog modules)
		{
			if (u.Kind != "python")
			{
				return false;
			}
			string path = u.Path;
			int bang = path.IndexOf("!/", StringComparison.Ordinal);
			string inner = bang >= 0 ? path.Substring(bang + 2) : path;
			return inner.EndsWith(".py", StringComparison.OrdinalIgnoreCase) && inner.IndexOf('/') < 0 && modules.Classify(inner.Substring(0, inner.Length - 3)) == "stdlib";
		}

		private static List<GapRow> ReferenceRows(IReadOnlyList<ScanUnit> units, TypeIndex index, GapRules rules)
		{
			var counts = new Dictionary<string, (int units, string kind)>(StringComparer.OrdinalIgnoreCase);
			foreach (ScanUnit u in units)
			{
				foreach (ScanReference r in u.References)
				{
					string name = r.Name.Split(',')[0].Trim();
					counts.TryGetValue(name, out var old);
					counts[name] = (old.units + 1, r.Kind);
				}
			}
			var rows = new List<GapRow>();
			foreach (KeyValuePair<string, (int units, string kind)> pair in counts)
			{
				string status;
				string source;
				string note = null;
				if (pair.Value.kind == "bundled")
				{
					status = "real";
					source = "package";
				}
				else if (index.Assemblies.TryGetValue(pair.Key, out string origin))
				{
					status = "real";
					source = origin == "program" ? (IsFacade(pair.Key) ? "facade" : "program") : ".NET 10";
				}
				else
				{
					status = "missing";
					source = "none";
					note = pair.Value.kind == "wpf" ? "Windows Presentation Foundation" : pair.Value.kind == "framework-only" ? "exists only in the .NET Framework" : pair.Value.kind == "core-missing" ? "belongs to the Windows program" : null;
				}
				rows.Add(new GapRow { Name = pair.Key, Uses = pair.Value.units, Units = pair.Value.units, Status = status, Source = source, Note = note, Owner = status == "missing" ? rules.OwnerOf(pair.Key, null) : null });
			}
			return Ranked(rows, r => r.Uses, r => r.Name).ToList();
		}
	}
}
