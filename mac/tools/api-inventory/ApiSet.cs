using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiInventory
{
	/// <summary>A System.Drawing or System.Windows.Forms type that the Core assemblies reference.</summary>
	internal sealed class ApiType
	{
		public string Scope { get; set; }
		public string Name { get; set; }
		public string Kind { get; set; }

		[JsonIgnore]
		public string Key => Scope + "|" + Name;
	}

	/// <summary>A referenced member. The identity is the declaring type, kind, name, signature and accessors (a property that gains a setter is a new entry).</summary>
	internal sealed class ApiMember
	{
		public string Scope { get; set; }
		public string Type { get; set; }
		public string Kind { get; set; }
		public string Name { get; set; }
		public string Sig { get; set; }
		public bool IsStatic { get; set; }
		/// <summary>"get", "set" or "get,set" for properties and events; empty otherwise.</summary>
		public string Accessors { get; set; } = "";
		/// <summary>The files with the most call sites (current scans only; never written to the baseline).</summary>
		[JsonIgnore]
		public string[] Files { get; set; } = Array.Empty<string>();

		[JsonIgnore]
		public string Key => string.Join("|", Scope, Type, Kind, Name, Sig, Accessors);

		public string Display()
		{
			string t = Type;
			string stat = IsStatic && Kind != "ctor" ? "static " : "";
			string acc = Accessors.Length > 0 ? " {" + Accessors + "}" : "";
			return Kind switch
			{
				"ctor" => t + Sig,
				"field" => stat + t + "." + Sig,
				"property" or "event" => stat + t + "." + Name + Sig + acc,
				_ => stat + t + "." + Name + Sig,
			};
		}
	}

	/// <summary>A P/Invoke declaration. Call sites and files are not part of the baseline.</summary>
	internal sealed class ApiDllImport
	{
		public string Module { get; set; }
		public string EntryPoint { get; set; }
		public string Declaring { get; set; }
		public string Name { get; set; }
		/// <summary>Parameter types, e.g. "(nint, int, nint)"; tells apart overloads that import the same entry point.</summary>
		public string Sig { get; set; } = "";

		/// <summary>The file that declares it (null when the PDB has none) and the files that call it; current scans only.</summary>
		[JsonIgnore]
		public string DeclFile { get; set; }
		[JsonIgnore]
		public string[] CallFiles { get; set; } = Array.Empty<string>();

		[JsonIgnore]
		public string Key => string.Join("|", Module.ToLowerInvariant().Replace(".dll", ""), EntryPoint, Declaring, Name, Sig);

		public string Display() => $"{Module}!{EntryPoint} ({Declaring.Replace('+', '.')}.{Name})";
	}

	/// <summary>The normalized platform API surface: what is committed as <c>mac/reports/api-baseline.json</c> and what a scan produces.</summary>
	internal sealed class ApiSet
	{
		public int Version { get; set; } = 1;
		public List<ApiType> Types { get; set; } = new();
		public List<ApiMember> Members { get; set; } = new();
		public List<ApiDllImport> DllImports { get; set; } = new();

		private static readonly JsonSerializerOptions Options = new()
		{
			WriteIndented = true,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			PropertyNameCaseInsensitive = true,
			Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		};

		public static ApiSet FromInventory(Inventory inv)
		{
			var set = new ApiSet();
			foreach (var t in inv.Types.Values.Where(t => Inventory.Area(t.Scope) != null))
				set.Types.Add(new ApiType { Scope = t.Scope, Name = t.FullName, Kind = t.Kind });
			foreach (var m in inv.Members.Values.Where(m => Inventory.Area(m.Type.Scope) != null))
			{
				set.Members.Add(new ApiMember
				{
					Scope = m.Type.Scope, Type = m.Type.FullName, Kind = m.Kind, Name = m.Name, Sig = m.Sig, IsStatic = m.IsStatic,
					Accessors = string.Join(",", m.Accessors),
					Files = m.Files.OrderByDescending(f => f.Value).ThenBy(f => f.Key, StringComparer.Ordinal).Take(3).Select(f => f.Key).ToArray(),
				});
			}
			foreach (var p in inv.PInvokes)
			{
				set.DllImports.Add(new ApiDllImport
				{
					Module = p.Module, EntryPoint = p.EntryPoint, Declaring = p.Declaring, Name = p.Name, Sig = p.Sig,
					DeclFile = p.DeclFile,
					CallFiles = p.Files.Keys.OrderBy(f => f, StringComparer.Ordinal).ToArray(),
				});
			}
			set.Sort();
			return set;
		}

		/// <summary>Orders everything by its identity and drops duplicates, so the written file does not depend on the scan order.</summary>
		public void Sort()
		{
			Types = Types.GroupBy(t => t.Key).Select(g => g.First()).OrderBy(t => t.Key, StringComparer.Ordinal).ToList();
			Members = Members.GroupBy(m => m.Key).Select(g => g.First()).OrderBy(m => m.Key, StringComparer.Ordinal).ToList();
			DllImports = DllImports.GroupBy(p => p.Key).Select(g => g.First()).OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
		}

		public string ToJson() => JsonSerializer.Serialize(this, Options).Replace("\r\n", "\n") + "\n";

		public static ApiSet FromJson(string json)
		{
			var set = JsonSerializer.Deserialize<ApiSet>(json, Options) ?? throw new InvalidDataException("Empty API baseline.");
			if (set.Version != 1)
				throw new InvalidDataException("Unsupported API baseline version " + set.Version);
			return set;
		}

		public static ApiSet Load(string path) => FromJson(File.ReadAllText(path, Encoding.UTF8));

		public void Save(string path)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
			File.WriteAllText(path, ToJson(), new UTF8Encoding(false));
		}

		/// <summary>What is in <paramref name="current"/> and not in <paramref name="baseline"/>.</summary>
		public static ApiDelta Compare(ApiSet current, ApiSet baseline)
		{
			var types = new HashSet<string>(baseline.Types.Select(t => t.Key), StringComparer.Ordinal);
			var members = new HashSet<string>(baseline.Members.Select(m => m.Key), StringComparer.Ordinal);
			var imports = new HashSet<string>(baseline.DllImports.Select(p => p.Key), StringComparer.Ordinal);
			return new ApiDelta
			{
				Types = current.Types.Where(t => !types.Contains(t.Key)).OrderBy(t => t.Key, StringComparer.Ordinal).ToList(),
				Members = current.Members.Where(m => !members.Contains(m.Key)).OrderBy(m => m.Key, StringComparer.Ordinal).ToList(),
				DllImports = current.DllImports.Where(p => !imports.Contains(p.Key)).OrderBy(p => p.Key, StringComparer.Ordinal).ToList(),
			};
		}
	}

	/// <summary>The new platform API of a scan, with the answers to "does a shim cover it" and "is it linked".</summary>
	internal sealed class ApiDelta
	{
		public List<ApiType> Types { get; set; } = new();
		public List<ApiMember> Members { get; set; } = new();
		public List<ApiDllImport> DllImports { get; set; } = new();

		/// <summary>Per key: true when the shim has it.</summary>
		public Dictionary<string, bool> TypeCovered { get; } = new(StringComparer.Ordinal);
		public Dictionary<string, bool> MemberCovered { get; } = new(StringComparer.Ordinal);
		/// <summary>Per key: "yes", "no" or "unknown".</summary>
		public Dictionary<string, string> DllImportLinked { get; } = new(StringComparer.Ordinal);

		public bool IsEmpty => Types.Count == 0 && Members.Count == 0 && DllImports.Count == 0;

		public int Uncovered => Types.Count(t => !TypeCovered.GetValueOrDefault(t.Key)) + Members.Count(m => !MemberCovered.GetValueOrDefault(m.Key));

		public void Evaluate(ShimCoverage coverage)
		{
			foreach (var t in Types)
				TypeCovered[t.Key] = coverage.CoversType(t.Scope, t.Name);
			foreach (var m in Members)
				MemberCovered[m.Key] = coverage.CoversMember(m);
		}

		/// <summary>
		/// A DllImport is linked when the file that declares it is a pass B compile item. When the PDB has no file for the declaring
		/// type, one of the files that call it decides; with neither the answer is "unknown" (to be treated as linked).
		/// </summary>
		public void EvaluateLinked(ISet<string> passBItems)
		{
			foreach (var p in DllImports)
				DllImportLinked[p.Key] = LinkedState(p, passBItems);
		}

		public static string LinkedState(ApiDllImport p, ISet<string> passBItems)
		{
			if (!string.IsNullOrEmpty(p.DeclFile))
				return passBItems.Contains(p.DeclFile) ? "yes" : "no";
			if (p.CallFiles.Length > 0)
				return p.CallFiles.Any(passBItems.Contains) ? "yes" : "no";
			return "unknown";
		}

		public string Render(string baselineName)
		{
			var sb = new StringBuilder();
			if (IsEmpty)
			{
				sb.AppendLine("no new platform API");
				return sb.ToString();
			}
			sb.AppendLine($"NEW platform API against {baselineName}: {Types.Count} types, {Members.Count} members, {DllImports.Count} DllImports; {Uncovered} types and members are not covered by the shims.");
			if (Types.Count > 0)
			{
				sb.AppendLine();
				sb.AppendLine("New types:");
				foreach (var t in Types)
					sb.AppendLine($"  {t.Name.Replace('+', '.')} ({t.Kind}) [{t.Scope}] covered by the shim: {Yes(TypeCovered.GetValueOrDefault(t.Key))}");
			}
			if (Members.Count > 0)
			{
				sb.AppendLine();
				sb.AppendLine("New members:");
				foreach (var m in Members)
				{
					string files = m.Files.Length > 0 ? "  in " + string.Join(", ", m.Files) : "";
					sb.AppendLine($"  {m.Display()} [{m.Scope}] covered by the shim: {Yes(MemberCovered.GetValueOrDefault(m.Key))}{files}");
				}
			}
			if (DllImports.Count > 0)
			{
				sb.AppendLine();
				sb.AppendLine("New DllImports (linked: yes = compiled in pass B; no = the declaring file is excluded or replaced there):");
				foreach (var p in DllImports)
				{
					string where = !string.IsNullOrEmpty(p.DeclFile) ? p.DeclFile : p.CallFiles.Length > 0 ? p.CallFiles[0] : "(no file)";
					sb.AppendLine($"  {p.Display()} linked: {DllImportLinked.GetValueOrDefault(p.Key, "unknown")}  in {where}");
				}
			}
			return sb.ToString();
		}

		private static string Yes(bool b) => b ? "yes" : "no";

		public string ToJson()
		{
			var obj = new
			{
				newTypes = Types.Select(t => new { t.Scope, t.Name, t.Kind, covered = TypeCovered.GetValueOrDefault(t.Key) }),
				newMembers = Members.Select(m => new { m.Scope, m.Type, m.Kind, m.Name, m.Sig, m.Accessors, m.Files, display = m.Display(), covered = MemberCovered.GetValueOrDefault(m.Key) }),
				newDllImports = DllImports.Select(p => new { p.Module, p.EntryPoint, p.Declaring, p.Name, p.DeclFile, linked = DllImportLinked.GetValueOrDefault(p.Key, "unknown") }),
				uncovered = Uncovered,
			};
			return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) + "\n";
		}
	}
}
