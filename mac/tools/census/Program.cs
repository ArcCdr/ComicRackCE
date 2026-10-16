using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Census
{
	/// <summary>
	/// Builds the shadow projects for one census pass, parses the compiler errors and writes a markdown report.
	/// Usage: dotnet run --project mac/tools/census -- --pass A|B [--out file.md]
	/// </summary>
	internal static class Program
	{
		private sealed record BuildSpec(string Project, bool Merged, string Why);

		private sealed record Diag(string File, int Line, int Col, string Code, string Message);

		private sealed record BuildResult(BuildSpec Spec, int ExitCode, int RawErrors, string LogPath, double Seconds)
		{
			public int FullErrors { get; set; } = -1;
			public int Files { get; set; }
			public double FullSeconds { get; set; }
		}

		// Common is built alone (so its own AssemblyInfo is covered); Engine and Plugins are built "merged"
		// (upstream sources compiled in, no ProjectReference) so that errors in Common do not hide errors downstream.
		private static readonly BuildSpec[] Builds =
		{
			new("cYo.Common.Core/cYo.Common.Core.csproj", false, "Common sources + Common AssemblyInfo"),
			new("ComicRack.Engine.Core/ComicRack.Engine.Core.csproj", true, "Common + Engine sources, Engine AssemblyInfo"),
			new("ComicRack.Plugins.Core/ComicRack.Plugins.Core.csproj", true, "Common + Engine + Plugins sources, Plugins AssemblyInfo"),
		};

		private static readonly Regex DiagRegex = new(
			@"^\s*(?:\d+(?::\d+)?>)?(?<file>.+?)\((?<l>\d+),(?<c>\d+)(?:,\d+,\d+)?\): error (?<code>[A-Za-z]+\d+): (?<msg>.*?)(?: \[(?<proj>[^\]]+)\])?\s*$",
			RegexOptions.Compiled);

		private static readonly Regex OtherErrorRegex = new(
			@"^\s*(?:\d+(?::\d+)?>)?(?<where>.*?): error (?<code>[A-Za-z]+\d+): (?<msg>.*?)(?: \[(?<proj>[^\]]+)\])?\s*$",
			RegexOptions.Compiled);

		private static int Main(string[] args)
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			string pass = null;
			string outFile = null;
			for (int i = 0; i < args.Length; i++)
			{
				if (args[i] == "--pass" && i + 1 < args.Length)
					pass = args[++i].ToUpperInvariant();
				else if (args[i] == "--out" && i + 1 < args.Length)
					outFile = args[++i];
			}
			if (pass != "A" && pass != "B")
			{
				Console.Error.WriteLine("Usage: census --pass A|B [--out file.md]");
				return 2;
			}

			string macDir = FindMacDir();
			if (macDir == null)
			{
				Console.Error.WriteLine("Cannot find mac/ComicRack.Mac.slnx above the current directory.");
				return 2;
			}
			string repoDir = Path.GetDirectoryName(macDir);
			outFile ??= Path.Combine(macDir, "reports", $"census-{pass}.md");
			string logDir = Path.Combine(macDir, "artifacts", "census");
			Directory.CreateDirectory(logDir);
			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile)));

			var results = new List<BuildResult>();
			var cscDiags = new HashSet<Diag>();
			var fullDiags = new HashSet<Diag>();
			var other = new HashSet<string>();
			foreach (var spec in Builds)
			{
				var res = RunBuild(macDir, logDir, pass, spec, cscDiags, other);
				results.Add(res);
				Console.WriteLine($"[{pass}] {spec.Project}: msbuild exit {res.ExitCode}, {res.RawErrors} error lines, {res.Seconds.ToString("F1", CultureInfo.InvariantCulture)}s");
				try
				{
					RunFull(macDir, pass, spec, res, fullDiags);
					Console.WriteLine($"[{pass}] {spec.Project}: full Roslyn diagnostics {res.FullErrors} errors over {res.Files} files, {res.FullSeconds:F1}s");
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine($"[{pass}] full diagnostics failed for {spec.Project}: {ex.Message}");
				}
			}

			bool haveFull = results.All(r => r.FullErrors >= 0);
			var diags = haveFull ? fullDiags : cscDiags;
			var buckets = LoadBuckets(macDir);
			string md = Render(pass, repoDir, results, diags, cscDiags.Count, haveFull, other, buckets);
			File.WriteAllText(outFile, md);
			// Raw list of every diagnostic (git-ignored artifacts folder), for working through the errors.
			File.WriteAllLines(Path.Combine(logDir, $"diagnostics.{pass}.txt"), diags
				.OrderBy(d => d.File, StringComparer.Ordinal).ThenBy(d => d.Line)
				.Select(d => $"{(d.File.StartsWith(repoDir + "/", StringComparison.Ordinal) ? d.File[(repoDir.Length + 1)..] : d.File)}({d.Line},{d.Col}): {d.Code}: {d.Message}"));
			Console.WriteLine($"[{pass}] msbuild/csc (declaration phase) unique errors: {cscDiags.Count}");
			Console.WriteLine($"[{pass}] TOTAL unique errors (full Roslyn diagnostics): {diags.Count}; other (non-compiler) errors: {other.Count}");
			Console.WriteLine($"Wrote {outFile}");
			return 0;
		}

		private static string FindMacDir()
		{
			foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
			{
				var dir = new DirectoryInfo(start);
				while (dir != null)
				{
					if (File.Exists(Path.Combine(dir.FullName, "ComicRack.Mac.slnx")))
						return dir.FullName;
					var nested = Path.Combine(dir.FullName, "mac", "ComicRack.Mac.slnx");
					if (File.Exists(nested))
						return Path.Combine(dir.FullName, "mac");
					dir = dir.Parent;
				}
			}
			return null;
		}

		private static BuildResult RunBuild(string macDir, string logDir, string pass, BuildSpec spec, HashSet<Diag> diags, HashSet<string> other)
		{
			string name = Path.GetFileNameWithoutExtension(spec.Project);
			string log = Path.Combine(logDir, $"{name}.{pass}.errors.log");
			if (File.Exists(log))
				File.Delete(log);
			var psi = new ProcessStartInfo("dotnet")
			{
				WorkingDirectory = macDir,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};
			foreach (var a in new[] { "build", spec.Project, $"-p:CensusPass={pass}", "--no-incremental", "-nologo", "-v:q", "-m:1", $"-flp:errorsonly;logfile={log}" })
				psi.ArgumentList.Add(a);
			if (spec.Merged)
				psi.ArgumentList.Add("-p:CensusMerged=true");

			var sw = Stopwatch.StartNew();
			using var p = Process.Start(psi)!;
			// Drain both streams so the child never blocks; the file logger holds the data we need.
			p.StandardOutput.ReadToEndAsync();
			p.StandardError.ReadToEndAsync();
			p.WaitForExit();
			sw.Stop();

			int raw = 0;
			if (File.Exists(log))
			{
				foreach (var line in File.ReadLines(log))
				{
					var m = DiagRegex.Match(line);
					if (m.Success && m.Groups["code"].Value.StartsWith("CS", StringComparison.Ordinal))
					{
						raw++;
						diags.Add(new Diag(
							Path.GetFullPath(m.Groups["file"].Value.Trim()),
							int.Parse(m.Groups["l"].Value),
							int.Parse(m.Groups["c"].Value),
							m.Groups["code"].Value,
							m.Groups["msg"].Value.Trim()));
						continue;
					}
					var o = OtherErrorRegex.Match(line);
					if (o.Success)
					{
						raw++;
						other.Add($"{o.Groups["code"].Value}: {o.Groups["msg"].Value.Trim()} ({name})");
					}
				}
			}
			return new BuildResult(spec, p.ExitCode, raw, log, sw.Elapsed.TotalSeconds);
		}

		// csc stops before binding method bodies when declaration errors exist, so the msbuild error list is a lower bound.
		// This asks MSBuild for the exact inputs of the compile and asks Roslyn for ALL diagnostics, including method bodies.
		private static void RunFull(string macDir, string pass, BuildSpec spec, BuildResult res, HashSet<Diag> sink)
		{
			var sw = Stopwatch.StartNew();
			var psi = new ProcessStartInfo("dotnet") { WorkingDirectory = macDir, RedirectStandardOutput = true, RedirectStandardError = true };
			foreach (var a in new[]
			{
				"msbuild", spec.Project, $"-p:CensusPass={pass}", "-t:ResolveReferences;FindReferenceAssembliesForReferences",
				"-getItem:Compile,ReferencePathWithRefAssemblies", "-getProperty:DefineConstants,LangVersion", "-nologo",
			})
				psi.ArgumentList.Add(a);
			if (spec.Merged)
				psi.ArgumentList.Add("-p:CensusMerged=true");
			using var p = Process.Start(psi)!;
			var errTask = p.StandardError.ReadToEndAsync();
			string stdout = p.StandardOutput.ReadToEnd();
			p.WaitForExit();
			errTask.Wait();
			int start = stdout.IndexOf("\n{", StringComparison.Ordinal);
			start = stdout.StartsWith("{", StringComparison.Ordinal) ? 0 : start + 1;
			if (p.ExitCode != 0 || start < 0)
				throw new InvalidOperationException("msbuild item query failed: " + stdout.Trim());
			using var doc = JsonDocument.Parse(stdout[start..]);
			var items = doc.RootElement.GetProperty("Items");
			var files = items.GetProperty("Compile").EnumerateArray().Select(e => e.GetProperty("FullPath").GetString()).ToList();
			var refs = items.GetProperty("ReferencePathWithRefAssemblies").EnumerateArray()
				.Select(e => e.GetProperty("FullPath").GetString()).ToList();
			var props = doc.RootElement.GetProperty("Properties");
			var defines = props.GetProperty("DefineConstants").GetString().Split(';', StringSplitOptions.RemoveEmptyEntries);
			var lv = LanguageVersionFacts.TryParse(props.GetProperty("LangVersion").GetString(), out var parsed) ? parsed : LanguageVersion.Latest;

			var parseOptions = new CSharpParseOptions(lv, preprocessorSymbols: defines);
			var trees = new SyntaxTree[files.Count];
			Parallel.For(0, files.Count, i =>
			{
				using var fs = File.OpenRead(files[i]);
				trees[i] = CSharpSyntaxTree.ParseText(SourceText.From(fs), parseOptions, files[i]);
			});
			var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
				nullableContextOptions: NullableContextOptions.Disable);
			var comp = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(spec.Project), trees,
				refs.Select(r => MetadataReference.CreateFromFile(r)), options);

			int n = 0;
			foreach (var d in comp.GetDiagnostics())
			{
				if (d.Severity != DiagnosticSeverity.Error || !d.Id.StartsWith("CS", StringComparison.Ordinal))
					continue;
				var span = d.Location.GetLineSpan();
				sink.Add(new Diag(span.Path ?? "", span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
					d.Id, d.GetMessage(CultureInfo.InvariantCulture)));
				n++;
			}
			res.FullErrors = n;
			res.Files = files.Count;
			res.FullSeconds = sw.Elapsed.TotalSeconds;
		}

		// Extracts the "missing symbol" a diagnostic is about, so identical gaps group together.
		private static string Symbol(Diag d)
		{
			var q = Regex.Matches(d.Message, "'([^']*)'").Select(m => m.Groups[1].Value).ToList();
			switch (d.Code)
			{
				case "CS0234" or "CS1069" when q.Count >= 2:
					return q[1] + "." + q[0];
				case "CS0246" or "CS0103" or "CS0122" or "CS0103" or "CS0841" when q.Count >= 1:
					return q[0];
				case "CS1061" or "CS0117" or "CS0539" when q.Count >= 2:
					return q[0] + "." + q[1];
				case "CS0012" when q.Count >= 2:
					return q[1];
				case "CS0518" or "CS0400" when q.Count >= 1:
					return q[0];
			}
			if (q.Count >= 1)
				return string.Join(" / ", q.Take(2));
			return d.Message.Length > 70 ? d.Message[..70] + "..." : d.Message;
		}

		private static string Esc(string s) => s.Replace("|", "\\|");

		private static string Code(string s) => "`" + s.Replace("`", "'").Replace("|", "\\|") + "`";

		private static string Area(string relFile)
		{
			if (relFile.StartsWith("cYo.Common/", StringComparison.Ordinal)) return "cYo.Common";
			if (relFile.StartsWith("ComicRack.Engine/", StringComparison.Ordinal)) return "ComicRack.Engine";
			if (relFile.StartsWith("ComicRack.Plugins/", StringComparison.Ordinal)) return "ComicRack.Plugins";
			return "other";
		}

		// Which API family a diagnostic is about. The type-name sets come from the reference assemblies that pass A resolves.
		private sealed class Buckets
		{
			public HashSet<string> Drawing = new();
			public HashSet<string> Forms = new();

			public static readonly string[] Names =
			{
				"System.Drawing", "System.Windows.Forms", "cYo.Common.Windows / Presentation", "WCF / System.Web.Services", "Win32 and security APIs (SystemEvents, permissions)", "other",
			};

			public string Of(Diag d)
			{
				string sym = Symbol(d);
				string head = sym.Split('.', ' ')[0];
				string text = sym + " " + d.Message;
				if (text.Contains("System.Drawing", StringComparison.Ordinal) || Drawing.Contains(head))
					return Names[0];
				if (text.Contains("System.Windows.Forms", StringComparison.Ordinal) || text.Contains("IDataObject", StringComparison.Ordinal) || Forms.Contains(head))
					return Names[1];
				if (text.Contains("cYo.Common.Windows", StringComparison.Ordinal) || text.Contains("cYo.Common.Presentation", StringComparison.Ordinal))
					return Names[2];
				if (text.Contains("ServiceModel", StringComparison.Ordinal) || text.Contains("System.Web.Services", StringComparison.Ordinal) || text.Contains("ServiceContract", StringComparison.Ordinal))
					return Names[3];
				if (text.Contains("Microsoft.Win32", StringComparison.Ordinal) || text.Contains("System.Security.Permissions", StringComparison.Ordinal)
					|| head is "SystemEvents" or "UserPreferenceCategory" or "UIPermissionWindow")
					return Names[4];
				return Names[5];
			}
		}

		private static Buckets LoadBuckets(string macDir)
		{
			var b = new Buckets();
			try
			{
				var psi = new ProcessStartInfo("dotnet") { WorkingDirectory = macDir, RedirectStandardOutput = true, RedirectStandardError = true };
				foreach (var a in new[]
				{
					"msbuild", "-restore", "ComicRack.Plugins.Core/ComicRack.Plugins.Core.csproj", "-p:CensusPass=A",
					"-t:ResolveReferences;FindReferenceAssembliesForReferences", "-getItem:ReferencePathWithRefAssemblies", "-nologo",
				})
					psi.ArgumentList.Add(a);
				using var p = Process.Start(psi)!;
				var errTask = p.StandardError.ReadToEndAsync();
				string stdout = p.StandardOutput.ReadToEnd();
				p.WaitForExit();
				errTask.Wait();
				if (p.ExitCode != 0)
					throw new InvalidOperationException("msbuild item query failed: " + stdout.Trim());
				int start = stdout.StartsWith("{", StringComparison.Ordinal) ? 0 : stdout.IndexOf("\n{", StringComparison.Ordinal) + 1;
				using var doc = JsonDocument.Parse(stdout[start..]);
				foreach (var e in doc.RootElement.GetProperty("Items").GetProperty("ReferencePathWithRefAssemblies").EnumerateArray())
				{
					string path = e.GetProperty("FullPath").GetString();
					var set = Path.GetFileNameWithoutExtension(path) switch
					{
						"System.Drawing.Common" or "System.Drawing.Primitives" or "System.Drawing" => b.Drawing,
						"System.Windows.Forms" or "System.Windows.Forms.Primitives" => b.Forms,
						_ => null,
					};
					if (set == null)
						continue;
					using var pe = new PEReader(File.OpenRead(path));
					var md = pe.GetMetadataReader();
					foreach (var th in md.TypeDefinitions)
					{
						var td = md.GetTypeDefinition(th);
						if ((td.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public)
							set.Add(md.GetString(td.Name));
					}
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine("Buckets: reference assemblies not available, using name matching only: " + ex.Message);
			}
			return b;
		}

		private static string Render(string pass, string repoDir, List<BuildResult> results, HashSet<Diag> diags, int cscCount, bool full, HashSet<string> other, Buckets buckets)
		{
			string Rel(string f) => f.StartsWith(repoDir + "/", StringComparison.Ordinal) ? f[(repoDir.Length + 1)..] : f;

			var sb = new StringBuilder();
			string tfm = pass == "A" ? "net10.0-windows (UseWindowsForms, EnableWindowsTargeting)" : "net10.0";
			sb.AppendLine($"# Compile census, pass {pass}");
			sb.AppendLine();
			sb.AppendLine($"- **Pass {pass}:** `{tfm}`. " + (pass == "A"
				? "Shows only the .NET Framework to .NET 10 gaps; WinForms and System.Drawing still resolve."
				: "The real target. Shows everything, including the Windows-only APIs."));
			sb.AppendLine($"- **Generated by:** `dotnet run --project mac/tools/census -- --pass {pass}`");
			sb.AppendLine($"- **Commit:** `{Git(repoDir, "rev-parse --short HEAD")}`; **SDK:** `{Git(repoDir, null, "dotnet", "--version")}`");
			sb.AppendLine();
			sb.AppendLine($"## Total errors: {diags.Count}");
			sb.AppendLine();
			sb.AppendLine("Unique compiler errors (`CS*`), de-duplicated by file, position, code and message. Nothing was fixed or shimmed for this pass.");
			if (other.Count > 0)
				sb.AppendLine($" **Plus {other.Count} non-compiler errors, listed at the end.**");
			sb.AppendLine();
			sb.AppendLine("Method:");
			sb.AppendLine();
			sb.AppendLine("- Errors in Common must not hide errors in Engine or Plugins, so the Engine and Plugins builds compile the upstream sources in directly (`CensusMerged=true`, no `ProjectReference`). The results of the three builds are merged and de-duplicated.");
			if (full)
			{
				sb.AppendLine($"- **The tables below use the full Roslyn diagnostics ({diags.Count} errors).** `dotnet build` (csc) stops before it binds method bodies when declaration-level errors exist (missing types in usings, signatures, base types), so it reports only **{cscCount}** of them (declaration phase). The tool asks MSBuild for the exact compile inputs (`-getItem:Compile,ReferencePathWithRefAssemblies`) and asks Roslyn for every diagnostic, method bodies included.");
				sb.AppendLine("- Errors that only appear once a type is fixed (for example a member lookup on a type that is currently unresolved) are still not counted, so the total remains a lower bound on the work, but much closer to it.");
			}
			else
			{
				sb.AppendLine($"- The tables use the msbuild/csc error list ({cscCount}). csc stops before it binds method bodies when declaration-level errors exist, so the total is a **lower bound**. The full-diagnostics step failed; see the console output of the tool.");
			}
			sb.AppendLine();

			sb.AppendLine("### Builds run");
			sb.AppendLine();
			sb.AppendLine("| Project | Mode | msbuild exit code | msbuild (csc) error lines | Roslyn full errors | Source files | Seconds |");
			sb.AppendLine("|---|---|---|---|---|---|---|");
			foreach (var r in results)
				sb.AppendLine($"| `{r.Spec.Project}` | {(r.Spec.Merged ? "merged" : "alone")}: {r.Spec.Why} | {r.ExitCode} | {r.RawErrors} | {(r.FullErrors >= 0 ? r.FullErrors.ToString(CultureInfo.InvariantCulture) : "n/a")} | {r.Files} | {(r.Seconds + r.FullSeconds).ToString("F1", CultureInfo.InvariantCulture)} |");
			sb.AppendLine();

			sb.AppendLine("### Errors per area");
			sb.AppendLine();
			sb.AppendLine("| Area | Errors | Files |");
			sb.AppendLine("|---|---|---|");
			foreach (var g in diags.GroupBy(d => Area(Rel(d.File))).OrderByDescending(g => g.Count()))
				sb.AppendLine($"| {g.Key} | {g.Count()} | {g.Select(d => d.File).Distinct().Count()} |");
			sb.AppendLine();

			sb.AppendLine("### Errors per bucket");
			sb.AppendLine();
			sb.AppendLine("By the API family the diagnostic is about (type names are matched against the System.Drawing and System.Windows.Forms reference assemblies of the .NET 10 Windows Desktop pack).");
			sb.AppendLine();
			sb.AppendLine("| Bucket | Errors | Files |");
			sb.AppendLine("|---|---|---|");
			foreach (var name in Buckets.Names)
			{
				var g = diags.Where(d => buckets.Of(d) == name).ToList();
				sb.AppendLine($"| {name} | {g.Count} | {g.Select(d => d.File).Distinct().Count()} |");
			}
			sb.AppendLine();

			sb.AppendLine("### Errors per code");
			sb.AppendLine();
			sb.AppendLine("| Code | Errors | Example message |");
			sb.AppendLine("|---|---|---|");
			foreach (var g in diags.GroupBy(d => d.Code).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
			{
				string ex = g.OrderBy(d => d.File, StringComparer.Ordinal).First().Message;
				sb.AppendLine($"| {g.Key} | {g.Count()} | {Esc(ex.Length > 110 ? ex[..110] + "..." : ex)} |");
			}
			sb.AppendLine();

			sb.AppendLine("## Per-symbol table");
			sb.AppendLine();
			sb.AppendLine("Grouped by the missing symbol (type, namespace, name or member) that the diagnostic names, most frequent first.");
			sb.AppendLine();
			sb.AppendLine("| Symbol | Codes | Errors | Files | Example file |");
			sb.AppendLine("|---|---|---|---|---|");
			var bySymbol = diags.GroupBy(Symbol)
				.OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal);
			foreach (var g in bySymbol)
			{
				string codes = string.Join(", ", g.Select(d => d.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal));
				var ex = g.OrderBy(d => d.File, StringComparer.Ordinal).ThenBy(d => d.Line).First();
				sb.AppendLine($"| {Code(g.Key)} | {codes} | {g.Count()} | {g.Select(d => d.File).Distinct().Count()} | {Code($"{Rel(ex.File)}:{ex.Line}")} |");
			}
			sb.AppendLine();

			sb.AppendLine("## Per-file table");
			sb.AppendLine();
			sb.AppendLine("| File | Errors | Codes |");
			sb.AppendLine("|---|---|---|");
			foreach (var g in diags.GroupBy(d => d.File).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
			{
				var codes = string.Join(", ", g.GroupBy(d => d.Code).OrderByDescending(c => c.Count())
					.ThenBy(c => c.Key, StringComparer.Ordinal).Select(c => $"{c.Key} x{c.Count()}"));
				sb.AppendLine($"| {Code(Rel(g.Key))} | {g.Count()} | {codes} |");
			}
			sb.AppendLine();

			if (other.Count > 0)
			{
				sb.AppendLine("## Non-compiler errors");
				sb.AppendLine();
				foreach (var o in other.OrderBy(x => x, StringComparer.Ordinal))
					sb.AppendLine($"- {Esc(o)}");
				sb.AppendLine();
			}
			return sb.ToString();
		}

		private static string Git(string repoDir, string gitArgs, string exe = "git", string singleArg = null)
		{
			try
			{
				var psi = new ProcessStartInfo(exe) { WorkingDirectory = repoDir, RedirectStandardOutput = true, RedirectStandardError = true };
				if (singleArg != null)
					psi.ArgumentList.Add(singleArg);
				else
					foreach (var a in gitArgs.Split(' '))
						psi.ArgumentList.Add(a);
				using var p = Process.Start(psi)!;
				string o = p.StandardOutput.ReadToEnd().Trim();
				p.WaitForExit();
				return o;
			}
			catch
			{
				return "unknown";
			}
		}
	}
}
