using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ComicRack.Mac.Host.Scripting.Scan
{
	public sealed class TypeUsage
	{
		public string Type { get; init; }

		public string Name { get; init; }

		public string Tier { get; init; }

		/// <summary>True when the type is in the catalog (the surface the layer has to provide).</summary>
		public bool Known { get; init; }

		/// <summary>Units (scripts and assemblies) that use it.</summary>
		public int Scripts { get; init; }

		public int Packages { get; init; }

		public int Uses { get; init; }
	}

	public sealed class MemberUsage
	{
		public string Type { get; init; }

		public string TypeName { get; init; }

		public string Member { get; init; }

		/// <summary>method, property, field, event, constructor, type or unknown (not in the catalog).</summary>
		public string Kind { get; init; }

		public string Tier { get; init; }

		public int Scripts { get; init; }

		public int Packages { get; init; }

		public int Uses { get; init; }

		public IReadOnlyList<string> Signatures { get; init; }

		/// <summary><c>Type.member</c>, the key of the ranking.</summary>
		public string Key => TypeName + "." + Member;
	}

	public sealed class ReferenceUsage
	{
		public string Name { get; init; }

		public string Kind { get; init; }

		public int Scripts { get; init; }
	}

	/// <summary>A member name used on a value whose type the scan could not tell.</summary>
	public sealed class UntypedUsage
	{
		public string Name { get; init; }

		/// <summary>True when some type of the surface has a member of this name (the access may well be one of the surface).</summary>
		public bool InSurface { get; init; }

		public int Scripts { get; init; }

		public int Uses { get; init; }
	}

	/// <summary>The result of a scan: the units and the ranked usage tables <c>type.member -> #scripts</c> (the implementation backlog of the compatibility layer).</summary>
	public sealed class ScanReport
	{
		public const int SchemaVersion = 1;

		public ScanReport(ApiCatalog catalog, IReadOnlyList<string> inputs, IReadOnlyList<ScanUnit> units)
		{
			Catalog = catalog;
			Inputs = inputs;
			Units = units;
			Build();
		}

		public ApiCatalog Catalog { get; }

		public IReadOnlyList<string> Inputs { get; }

		public IReadOnlyList<ScanUnit> Units { get; }

		public IReadOnlyList<TypeUsage> Types { get; private set; }

		public IReadOnlyList<MemberUsage> Members { get; private set; }

		public IReadOnlyList<ReferenceUsage> References { get; private set; }

		/// <summary>Member names on values of unknown type that some type of the surface also has, ranked: the name only ranking for what typing could not resolve.</summary>
		public IReadOnlyList<UntypedUsage> UntypedMembers { get; private set; }

		/// <summary>The number of units per tier (none, A, B, C and ? for the ones that could not be read).</summary>
		public IReadOnlyDictionary<string, int> Tiers { get; private set; }

		public int Packages { get; private set; }

		private string ShortName(string full)
		{
			ApiType t = Catalog.Type(full);
			if (t != null)
			{
				return t.Name;
			}
			int plus = full.IndexOf('+');
			string outer = plus < 0 ? full : full.Substring(0, plus);
			int dot = outer.LastIndexOf('.');
			return dot < 0 ? full : full.Substring(dot + 1) + (plus < 0 ? string.Empty : full.Substring(plus).Replace('+', '.'));
		}

		private void Build()
		{
			var types = new Dictionary<string, (HashSet<string> units, HashSet<string> packages, int uses)>(StringComparer.Ordinal);
			var members = new Dictionary<string, (HashSet<string> units, HashSet<string> packages, int uses, SortedSet<string> signatures)>(StringComparer.Ordinal);
			var references = new Dictionary<string, (string kind, HashSet<string> units)>(StringComparer.Ordinal);
			foreach (ScanUnit u in Units)
			{
				foreach (KeyValuePair<string, int> t in u.Types)
				{
					if (!types.TryGetValue(t.Key, out var e))
					{
						types[t.Key] = e = (new HashSet<string>(), new HashSet<string>(), 0);
					}
					e.units.Add(u.Path);
					e.packages.Add(u.Package);
					types[t.Key] = (e.units, e.packages, e.uses + t.Value);
				}
				foreach (KeyValuePair<string, int> m in u.Members)
				{
					if (!members.TryGetValue(m.Key, out var e))
					{
						members[m.Key] = e = (new HashSet<string>(), new HashSet<string>(), 0, new SortedSet<string>(StringComparer.Ordinal));
					}
					e.units.Add(u.Path);
					e.packages.Add(u.Package);
					if (u.Signatures.TryGetValue(m.Key, out SortedSet<string> sigs))
					{
						e.signatures.UnionWith(sigs);
					}
					members[m.Key] = (e.units, e.packages, e.uses + m.Value, e.signatures);
				}
				foreach (ScanReference r in u.References)
				{
					if (!references.TryGetValue(r.Name, out var e))
					{
						references[r.Name] = e = (r.Kind, new HashSet<string>());
					}
					e.units.Add(u.Path);
				}
			}

			Types = types.Select(t =>
			{
				ApiType type = Catalog.Type(t.Key);
				return new TypeUsage
				{
					Type = t.Key,
					Name = ShortName(t.Key),
					Tier = ApiTiers.Of(Catalog, type),
					Known = type != null,
					Scripts = t.Value.units.Count,
					Packages = t.Value.packages.Count,
					Uses = t.Value.uses,
				};
			}).OrderByDescending(t => t.Scripts).ThenByDescending(t => t.Packages).ThenByDescending(t => t.Uses).ThenBy(t => t.Type, StringComparer.Ordinal).ToList();

			Members = members.Select(m =>
			{
				int sep = m.Key.IndexOf("::", StringComparison.Ordinal);
				string typeName = m.Key.Substring(0, sep);
				string member = m.Key.Substring(sep + 2);
				ApiType type = Catalog.Type(typeName);
				string kind = "unknown";
				if (type != null && type.Members.TryGetValue(member, out List<ApiMember> list))
				{
					kind = list[0].Kind == ApiMemberKind.NestedType ? "type" : list[0].Kind.ToString().ToLowerInvariant();
				}
				return new MemberUsage
				{
					Type = typeName,
					TypeName = ShortName(typeName),
					Member = member,
					Kind = kind,
					Tier = ApiTiers.Of(Catalog, type),
					Scripts = m.Value.units.Count,
					Packages = m.Value.packages.Count,
					Uses = m.Value.uses,
					Signatures = m.Value.signatures.ToList(),
				};
			}).OrderByDescending(m => m.Scripts).ThenByDescending(m => m.Packages).ThenByDescending(m => m.Uses).ThenBy(m => m.Type, StringComparer.Ordinal).ThenBy(m => m.Member, StringComparer.Ordinal).ToList();

			References = references.Select(r => new ReferenceUsage { Name = r.Key, Kind = r.Value.kind, Scripts = r.Value.units.Count })
				.OrderByDescending(r => r.Scripts).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();

			var untyped = new Dictionary<string, (HashSet<string> units, int uses)>(StringComparer.Ordinal);
			foreach (ScanUnit u in Units)
			{
				foreach (KeyValuePair<string, int> n in u.UntypedNames)
				{
					if (!untyped.TryGetValue(n.Key, out var e))
					{
						untyped[n.Key] = e = (new HashSet<string>(), 0);
					}
					e.units.Add(u.Path);
					untyped[n.Key] = (e.units, e.uses + n.Value);
				}
			}
			var surfaceNames = new HashSet<string>(StringComparer.Ordinal);
			foreach (ApiType t in Catalog.Types)
			{
				surfaceNames.UnionWith(t.Members.Keys);
			}
			UntypedMembers = untyped.Where(n => surfaceNames.Contains(n.Key))
				.Select(n => new UntypedUsage { Name = n.Key, InSurface = true, Scripts = n.Value.units.Count, Uses = n.Value.uses })
				.OrderByDescending(n => n.Scripts).ThenByDescending(n => n.Uses).ThenBy(n => n.Name, StringComparer.Ordinal).ToList();

			var tiers = new SortedDictionary<string, int>(StringComparer.Ordinal) { ["none"] = 0, ["A"] = 0, ["B"] = 0, ["C"] = 0, ["?"] = 0 };
			foreach (ScanUnit u in Units)
			{
				tiers[u.Tier] = (tiers.TryGetValue(u.Tier, out int n) ? n : 0) + 1;
			}
			Tiers = tiers;
			Packages = Units.Select(u => u.Package).Distinct(StringComparer.Ordinal).Count();
		}

		// ---------------------------------------------------------------- output

		/// <summary>The report as text: a summary, then tab separated rows of the ranked tables (the first <paramref name="top"/> of each) and every finding of the units.</summary>
		public void WriteText(TextWriter w, int top = 50)
		{
			int python = Units.Count(u => u.Kind == "python");
			int assemblies = Units.Count(u => u.Kind == "assembly");
			int html = Units.Count(u => u.Kind == "html");
			w.WriteLine("script-scan: " + Units.Count + " units (" + python + " python, " + assemblies + " assemblies, " + html + " html) in " + Packages + " packages");
			w.WriteLine("catalog: " + Catalog.TypeCount + " types, " + Catalog.Source + (Catalog.IsComplete ? string.Empty : " (the surface is incomplete: names it lacks are not resolved)"));
			w.WriteLine("tiers: none " + Tiers["none"] + ", A " + Tiers["A"] + ", B " + Tiers["B"] + ", C " + Tiers["C"] + (Tiers["?"] > 0 ? ", unreadable " + Tiers["?"] : string.Empty));
			w.WriteLine();
			w.WriteLine("types (" + Types.Count + "): scripts, packages, uses, tier, type");
			foreach (TypeUsage t in Types.Take(top))
			{
				w.WriteLine("type\t" + t.Scripts + "\t" + t.Packages + "\t" + t.Uses + "\t" + t.Tier + "\t" + t.Name + (t.Known ? string.Empty : " (not in the catalog)"));
			}
			w.WriteLine();
			w.WriteLine("members (" + Members.Count + "): scripts, packages, uses, tier, kind, type.member");
			foreach (MemberUsage m in Members.Take(top))
			{
				w.WriteLine("member\t" + m.Scripts + "\t" + m.Packages + "\t" + m.Uses + "\t" + m.Tier + "\t" + m.Kind + "\t" + m.Key);
			}
			w.WriteLine();
			w.WriteLine("untyped (" + UntypedMembers.Count + "): member names on values of unknown type that the surface also has: scripts, uses, name");
			foreach (UntypedUsage n in UntypedMembers.Take(top))
			{
				w.WriteLine("untyped\t" + n.Scripts + "\t" + n.Uses + "\t" + n.Name);
			}
			w.WriteLine();
			w.WriteLine("references (" + References.Count + "): scripts, kind, assembly");
			foreach (ReferenceUsage r in References.Take(top))
			{
				w.WriteLine("reference\t" + r.Scripts + "\t" + r.Kind + "\t" + r.Name);
			}
			w.WriteLine();
			int errors = Units.Sum(u => u.Findings.Count(f => f.Severity == "error"));
			int warnings = Units.Sum(u => u.Findings.Count(f => f.Severity == "warning"));
			w.WriteLine("findings: " + errors + " errors (Tier C), " + warnings + " warnings");
			foreach (ScanUnit u in Units)
			{
				foreach (ScanFinding f in u.Findings.OrderBy(f => f.Severity == "error" ? 0 : 1).ThenBy(f => f.Line))
				{
					w.WriteLine(f.Severity + "\t" + u.Path + "\t" + (f.Line > 0 ? f.Line.ToString() : "-") + "\t" + f.Kind + "\t" + f.Message);
				}
			}
			w.WriteLine();
			w.WriteLine("units: tier, kind, untyped accesses, path");
			foreach (ScanUnit u in Units)
			{
				w.WriteLine("unit\t" + u.Tier + "\t" + u.Kind + "\t" + u.Untyped + "\t" + u.Path + (u.Error != null ? "\t" + u.Error : string.Empty));
			}
		}

		/// <summary>The report as JSON (schema version 1; ordered, without times or machine paths, so equal scans give equal files).</summary>
		public string ToJson()
		{
			using var stream = new MemoryStream();
			using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
			{
				w.WriteStartObject();
				w.WriteNumber("schema", SchemaVersion);
				w.WriteString("tool", "crcli script-scan");
				w.WriteStartObject("catalog");
				w.WriteString("source", Catalog.Source);
				w.WriteBoolean("complete", Catalog.IsComplete);
				w.WriteNumber("types", Catalog.TypeCount);
				w.WriteEndObject();
				w.WriteStartArray("inputs");
				foreach (string i in Inputs)
				{
					w.WriteStringValue(i);
				}
				w.WriteEndArray();
				w.WriteStartObject("summary");
				w.WriteNumber("units", Units.Count);
				w.WriteNumber("packages", Packages);
				w.WriteNumber("python", Units.Count(u => u.Kind == "python"));
				w.WriteNumber("assemblies", Units.Count(u => u.Kind == "assembly"));
				w.WriteNumber("html", Units.Count(u => u.Kind == "html"));
				w.WriteNumber("unreadable", Units.Count(u => !u.Parsed));
				w.WriteStartObject("tiers");
				foreach (KeyValuePair<string, int> t in Tiers)
				{
					w.WriteNumber(t.Key, t.Value);
				}
				w.WriteEndObject();
				w.WriteEndObject();

				w.WriteStartArray("types");
				foreach (TypeUsage t in Types)
				{
					w.WriteStartObject();
					w.WriteString("type", t.Type);
					w.WriteString("name", t.Name);
					w.WriteString("tier", t.Tier);
					w.WriteBoolean("known", t.Known);
					w.WriteNumber("scripts", t.Scripts);
					w.WriteNumber("packages", t.Packages);
					w.WriteNumber("uses", t.Uses);
					w.WriteEndObject();
				}
				w.WriteEndArray();

				w.WriteStartArray("members");
				foreach (MemberUsage m in Members)
				{
					w.WriteStartObject();
					w.WriteString("key", m.Key);
					w.WriteString("type", m.Type);
					w.WriteString("member", m.Member);
					w.WriteString("kind", m.Kind);
					w.WriteString("tier", m.Tier);
					w.WriteNumber("scripts", m.Scripts);
					w.WriteNumber("packages", m.Packages);
					w.WriteNumber("uses", m.Uses);
					if (m.Signatures.Count > 0)
					{
						w.WriteStartArray("signatures");
						foreach (string s in m.Signatures)
						{
							w.WriteStringValue(s);
						}
						w.WriteEndArray();
					}
					w.WriteEndObject();
				}
				w.WriteEndArray();

				w.WriteStartArray("untypedMembers");
				foreach (UntypedUsage n in UntypedMembers)
				{
					w.WriteStartObject();
					w.WriteString("name", n.Name);
					w.WriteNumber("scripts", n.Scripts);
					w.WriteNumber("uses", n.Uses);
					w.WriteEndObject();
				}
				w.WriteEndArray();

				w.WriteStartArray("references");
				foreach (ReferenceUsage r in References)
				{
					w.WriteStartObject();
					w.WriteString("name", r.Name);
					w.WriteString("kind", r.Kind);
					w.WriteNumber("scripts", r.Scripts);
					w.WriteEndObject();
				}
				w.WriteEndArray();

				w.WriteStartArray("units");
				foreach (ScanUnit u in Units)
				{
					w.WriteStartObject();
					w.WriteString("path", u.Path);
					w.WriteString("package", u.Package);
					w.WriteString("kind", u.Kind);
					w.WriteString("tier", u.Tier);
					w.WriteBoolean("parsed", u.Parsed);
					if (u.Error != null)
					{
						w.WriteString("error", u.Error);
					}
					w.WriteNumber("untyped", u.Untyped);
					w.WriteStartArray("hooks");
					foreach (string h in u.Hooks)
					{
						w.WriteStringValue(h);
					}
					w.WriteEndArray();
					w.WriteStartArray("references");
					foreach (ScanReference r in u.References)
					{
						w.WriteStartObject();
						w.WriteString("name", r.Name);
						w.WriteString("kind", r.Kind);
						if (r.Line > 0)
						{
							w.WriteNumber("line", r.Line);
						}
						w.WriteEndObject();
					}
					w.WriteEndArray();
					w.WriteStartObject("types");
					foreach (KeyValuePair<string, int> t in u.Types)
					{
						w.WriteNumber(t.Key, t.Value);
					}
					w.WriteEndObject();
					w.WriteStartObject("members");
					foreach (KeyValuePair<string, int> m in u.Members)
					{
						w.WriteNumber(m.Key, m.Value);
					}
					w.WriteEndObject();
					w.WriteStartArray("findings");
					foreach (ScanFinding f in u.Findings.OrderBy(f => f.Severity == "error" ? 0 : f.Severity == "warning" ? 1 : 2).ThenBy(f => f.Line).ThenBy(f => f.Symbol, StringComparer.Ordinal))
					{
						w.WriteStartObject();
						w.WriteString("severity", f.Severity);
						w.WriteString("kind", f.Kind);
						w.WriteString("symbol", f.Symbol);
						if (f.Line > 0)
						{
							w.WriteNumber("line", f.Line);
						}
						w.WriteString("message", f.Message);
						w.WriteEndObject();
					}
					w.WriteEndArray();
					w.WriteEndObject();
				}
				w.WriteEndArray();
				w.WriteEndObject();
			}
			return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
		}
	}
}
