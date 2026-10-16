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
	/// </summary>
	internal static class Program
	{
		private static readonly string[] Assemblies = { "cYo.Common.Core", "ComicRack.Engine.Core", "ComicRack.Plugins.Core" };

		private static int Main(string[] args)
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			string outFile = null;
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
			}

			string macDir = FindMacDir();
			if (macDir == null)
			{
				Console.Error.WriteLine("Cannot find mac/ComicRack.Mac.slnx above the current directory.");
				return 2;
			}
			string repoDir = Path.GetDirectoryName(macDir);
			outFile ??= Path.Combine(macDir, "reports", "api-inventory.md");
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

				var refAssemblies = ReferenceAssemblies(macDir);
				int classified = RefKinds.Apply(inv, refAssemblies);
				Console.WriteLine($"Types: {inv.Types.Count} ({classified} classified from the reference assemblies); members: {inv.Members.Count}; P/Invoke: {inv.PInvokes.Count}");

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
