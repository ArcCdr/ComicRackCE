using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ApiInventory
{
	/// <summary>
	/// Lists the System.Drawing, System.Windows.Forms and P/Invoke members that the Core assemblies use, by reading the
	/// metadata and IL of the pass A build output (and the portable PDBs for the file names).
	/// Usage: dotnet run --project mac/tools/api-inventory [-- [--out file.md] [--no-build] [--files N]]
	///        dotnet run --project mac/tools/api-inventory -- --compare mac/reports/api-baseline.json [--json out.json] [--no-build]   (exit 1 = new platform API)
	///        dotnet run --project mac/tools/api-inventory -- --write-baseline [file]   (default mac/reports/api-baseline.json)
	/// </summary>
	internal static class Program
	{
		private static readonly string[] Assemblies = { "cYo.Common.Core", "ComicRack.Engine.Core", "ComicRack.Plugins.Core" };

		private const string BaselineRelative = "reports/api-baseline.json";

		private static int Main(string[] args)
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			string outFile = null, compareFile = null, writeBaseline = null, jsonOut = null;
			bool build = true;
			int filesPerRow = 5;
			for (int i = 0; i < args.Length; i++)
			{
				if (args[i] == "--out" && i + 1 < args.Length)
					outFile = args[++i];
				else if (args[i] == "--no-build")
					build = false;
				else if (args[i] == "--files" && i + 1 < args.Length)
					filesPerRow = int.Parse(args[++i]);
				else if (args[i] == "--compare" && i + 1 < args.Length)
					compareFile = args[++i];
				else if (args[i] == "--json" && i + 1 < args.Length)
					jsonOut = args[++i];
				else if (args[i] == "--write-baseline")
					writeBaseline = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "";
				else
				{
					Console.Error.WriteLine("Usage: api-inventory [--out report.md] [--no-build] [--files N] | --compare <baseline.json> [--json out.json] [--no-build] | --write-baseline [file] [--no-build]");
					return 2;
				}
			}
			if (compareFile != null && writeBaseline != null)
			{
				Console.Error.WriteLine("--compare and --write-baseline exclude each other.");
				return 2;
			}

			string macDir = FindMacDir();
			if (macDir == null)
			{
				Console.Error.WriteLine("Cannot find mac/ComicRack.Mac.slnx above the current directory.");
				return 2;
			}
			string repoDir = Path.GetDirectoryName(macDir);
			outFile ??= Path.Combine(macDir, "reports", "api-inventory.md");

			ApiSet baseline = null;
			if (compareFile != null)
			{
				if (!File.Exists(compareFile))
				{
					Console.Error.WriteLine("Baseline not found: " + compareFile);
					return 2;
				}
				try
				{
					baseline = ApiSet.Load(compareFile);
				}
				catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException)
				{
					Console.Error.WriteLine($"Baseline {compareFile} is not valid: {e.Message}");
					return 2;
				}
			}

			string project = "ComicRack.Plugins.Core/ComicRack.Plugins.Core.csproj";

			// A clean, non-merged build: the census builds leave "merged" assemblies (Common compiled into Engine) in the same folder.
			if (build)
			{
				Console.WriteLine("Building pass A (clean) ...");
				var (code, _, err) = Run(macDir, "dotnet", "build", project, "-p:CensusPass=A", "-c", "Debug", "--no-incremental", "-nologo", "-v:q", "-clp:ErrorsOnly");
				if (code != 0)
				{
					Console.Error.WriteLine("Pass A does not build:\n" + err);
					return 1;
				}
			}
			string binDir = Path.Combine(macDir, "ComicRack.Plugins.Core", "bin", "Debug", "net10.0-windows");

			var inv = new Inventory();
			var scanners = new List<AssemblyScanner>();
			try
			{
				foreach (var name in Assemblies)
					scanners.Add(new AssemblyScanner(inv, Path.Combine(binDir, name + ".dll"), repoDir));

				var seen = new Dictionary<string, string>(StringComparer.Ordinal);
				foreach (var s in scanners)
					foreach (var t in s.DefinedTypeNames().Where(n => !n.Contains('<') && !n.Contains("PrivateImplementationDetails")))
						if (seen.TryGetValue(t, out var other) && other != s.Name)
							throw new InvalidOperationException($"Type {t} is defined in {other} and {s.Name}: the output is a merged census build. Run without --no-build.");
						else
							seen[t] = s.Name;

				foreach (var s in scanners)
					s.CollectPInvokes();
				foreach (var s in scanners)
					s.Scan();
				foreach (var s in scanners)
					s.ResolveDeclFiles();

				var refAssemblies = ReferenceAssemblies(macDir);
				int classified = RefKinds.Apply(inv, refAssemblies);
				Console.WriteLine($"Types: {inv.Types.Count} ({classified} classified from the reference assemblies); members: {inv.Members.Count}; P/Invoke: {inv.PInvokes.Count}");

				if (writeBaseline != null)
				{
					string file = writeBaseline.Length > 0 ? writeBaseline : Path.Combine(macDir, BaselineRelative);
					var set = ApiSet.FromInventory(inv);
					set.Save(file);
					Console.WriteLine($"Wrote {file}: {set.Types.Count} types, {set.Members.Count} members, {set.DllImports.Count} DllImports.");
					return 0;
				}

				if (baseline != null)
					return Compare(macDir, repoDir, inv, baseline, compareFile, jsonOut, build);

				var md = Report.Render(inv, scanners, repoDir, filesPerRow);
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile)));
				File.WriteAllText(outFile, md);
				Console.WriteLine("Wrote " + outFile);

				string jsonDir = Path.Combine(macDir, "artifacts", "api-inventory");
				Directory.CreateDirectory(jsonDir);
				File.WriteAllText(Path.Combine(jsonDir, "inventory.json"), Report.Json(inv));
				Console.WriteLine("Wrote " + Path.Combine(jsonDir, "inventory.json"));
			}
			finally
			{
				foreach (var s in scanners)
					s.Dispose();
			}
			return 0;
		}

		/// <summary>New platform API against the baseline: exit 0 when there is none, 1 when there is some, 2 when the check itself failed.</summary>
		private static int Compare(string macDir, string repoDir, Inventory inv, ApiSet baseline, string baselineFile, string jsonOut, bool build)
		{
			var current = ApiSet.FromInventory(inv);
			var delta = ApiSet.Compare(current, baseline);
			if (!delta.IsEmpty)
			{
				if (build)
				{
					Console.WriteLine("Building the shims ...");
					var (code, _, err) = Run(macDir, "dotnet", "build", "cYo.Compat.Forms/cYo.Compat.Forms.csproj", "-nologo", "-v:q", "-clp:ErrorsOnly");
					if (code != 0)
					{
						Console.Error.WriteLine("The shims do not build:\n" + err);
						return 2;
					}
				}
				var (drawingDll, formsDll) = ShimCoverage.Locate(macDir);
				using (var coverage = new ShimCoverage(drawingDll, formsDll))
					delta.Evaluate(coverage);
				if (delta.DllImports.Count > 0)
					delta.EvaluateLinked(PassBItems.Query(repoDir));
			}
			Console.Write(delta.Render(Path.GetFileName(baselineFile)));
			if (jsonOut != null)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonOut)));
				File.WriteAllText(jsonOut, delta.ToJson());
			}
			return delta.IsEmpty ? 0 : 1;
		}

		private static IEnumerable<string> ReferenceAssemblies(string macDir)
		{
			var (code, stdout, err) = Run(macDir, "dotnet", "msbuild", "-restore", "ComicRack.Plugins.Core/ComicRack.Plugins.Core.csproj", "-p:CensusPass=A",
				"-t:ResolveReferences;FindReferenceAssembliesForReferences", "-getItem:ReferencePathWithRefAssemblies", "-nologo");
			if (code != 0)
				throw new InvalidOperationException("msbuild item query failed: " + err + stdout);
			int start = stdout.StartsWith("{", StringComparison.Ordinal) ? 0 : stdout.IndexOf("\n{", StringComparison.Ordinal) + 1;
			using var doc = JsonDocument.Parse(stdout[start..]);
			return doc.RootElement.GetProperty("Items").GetProperty("ReferencePathWithRefAssemblies").EnumerateArray()
				.Select(e => e.GetProperty("FullPath").GetString()).ToList();
		}

		private static (int Code, string Out, string Err) Run(string dir, string exe, params string[] args)
		{
			var psi = new ProcessStartInfo(exe) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
			foreach (var a in args)
				psi.ArgumentList.Add(a);
			using var p = Process.Start(psi)!;
			var err = p.StandardError.ReadToEndAsync();
			string o = p.StandardOutput.ReadToEnd();
			p.WaitForExit();
			return (p.ExitCode, o, err.Result);
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
					if (File.Exists(Path.Combine(dir.FullName, "mac", "ComicRack.Mac.slnx")))
						return Path.Combine(dir.FullName, "mac");
					dir = dir.Parent;
				}
			}
			return null;
		}
	}
}
