using System;
using System.Collections.Generic;
using System.Linq;

namespace ComicRack.Mac.Host.Scripting.Scan
{
	/// <summary>Something the scanner found in a script or assembly that matters on macOS.</summary>
	public sealed class ScanFinding
	{
		/// <summary>error (the unit cannot run: Tier C), warning (may misbehave) or info.</summary>
		public string Severity { get; init; }

		/// <summary>A short machine readable kind: windows-library, p-invoke, wndproc, wpf, com, windows-module, windows-type, windows-member, windows-path, html-sync-property, parse-error, ...</summary>
		public string Kind { get; init; }

		/// <summary>The name that caused it (<c>user32.SendMessage</c>, <c>System.Windows.Controls</c>).</summary>
		public string Symbol { get; init; }

		public int Line { get; init; }

		/// <summary>The sentence shown to the user.</summary>
		public string Message { get; init; }

		/// <summary>The sentence of the compatibility report for an error: "Uses Windows-only API `x` at line N; this script cannot run on macOS".</summary>
		public static ScanFinding WindowsOnly(string kind, string symbol, int line, string what, string unit = "script", bool guarded = false)
		{
			if (guarded)
			{
				return new ScanFinding
				{
					Severity = "warning",
					Kind = kind,
					Symbol = symbol,
					Line = line,
					Message = "Uses Windows-only API `" + symbol + "`" + (line > 0 ? " at line " + line : string.Empty) + " behind a platform check or a guarded import; macOS skips it" + (what == null ? string.Empty : " (" + what + ")") + ".",
				};
			}
			return new ScanFinding
			{
				Severity = "error",
				Kind = kind,
				Symbol = symbol,
				Line = line,
				Message = "Uses Windows-only API `" + symbol + "`" + (line > 0 ? " at line " + line : string.Empty) + "; this " + unit + " cannot run on macOS" + (what == null ? string.Empty : " (" + what + ")") + ".",
			};
		}
	}

	/// <summary>An assembly or module a script or plugin names (<c>clr.AddReference</c>, assembly reference).</summary>
	public sealed class ScanReference
	{
		public string Name { get; init; }

		/// <summary>forms, drawing, core, core-missing, framework, framework-only, wpf, com, windows, bundled (a file of the plugin) or other.</summary>
		public string Kind { get; init; }

		public int Line { get; init; }

		/// <summary>True when the script names it inside a platform check or a guarded import (the finding is then a warning).</summary>
		public bool Guarded { get; init; }
	}

	/// <summary>The result for one script, assembly or HTML file.</summary>
	public sealed class ScanUnit
	{
		/// <summary>Path relative to the scanned folder (<c>pkg.crplugin!/Script.py</c> inside a package).</summary>
		public string Path { get; set; }

		/// <summary>The plugin the unit belongs to: the package file, the first sub folder, or the file itself.</summary>
		public string Package { get; set; }

		/// <summary>python, assembly, html or native.</summary>
		public string Kind { get; set; }

		public bool Parsed { get; set; } = true;

		public string Error { get; set; }

		/// <summary>none (no WinForms or GDI+), A, B or C (see <see cref="ApiTiers"/>); "?" when the unit could not be read.</summary>
		public string Tier { get; set; } = "none";

		public List<string> Hooks { get; } = new List<string>();

		public List<ScanReference> References { get; } = new List<ScanReference>();

		public List<ScanFinding> Findings { get; } = new List<ScanFinding>();

		/// <summary>Full type name to the number of uses in the unit.</summary>
		public SortedDictionary<string, int> Types { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

		/// <summary>"declaring full type::member" to the number of uses in the unit.</summary>
		public SortedDictionary<string, int> Members { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

		/// <summary>Member keys to the signatures seen (assemblies only).</summary>
		public SortedDictionary<string, SortedSet<string>> Signatures { get; } = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

		/// <summary>Member accesses on values whose type the scan could not tell (Python is dynamic): they are not in <see cref="Members"/>.</summary>
		public int Untyped { get; set; }

		/// <summary>The member names of those accesses with their counts (a ranking by name alone, for what the types could not resolve).</summary>
		public SortedDictionary<string, int> UntypedNames { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

		internal void UntypedAccess(string name)
		{
			Untyped++;
			if (name != null && !name.StartsWith("__", StringComparison.Ordinal))
			{
				UntypedNames[name] = (UntypedNames.TryGetValue(name, out int n) ? n : 0) + 1;
			}
		}

		internal void UseType(string fullName)
		{
			Types[fullName] = (Types.TryGetValue(fullName, out int n) ? n : 0) + 1;
		}

		internal void UseMember(string declaring, string member, string signature = null)
		{
			string key = declaring + "::" + member;
			Members[key] = (Members.TryGetValue(key, out int n) ? n : 0) + 1;
			if (signature != null)
			{
				if (!Signatures.TryGetValue(key, out SortedSet<string> set))
				{
					Signatures[key] = set = new SortedSet<string>(StringComparer.Ordinal);
				}
				set.Add(signature);
			}
		}

		internal bool HasFinding(string kind, string symbol, int line) => Findings.Any(f => f.Kind == kind && f.Symbol == symbol && f.Line == line);

		internal void AddFinding(ScanFinding finding)
		{
			if (!HasFinding(finding.Kind, finding.Symbol, finding.Line))
			{
				Findings.Add(finding);
			}
		}
	}
}
