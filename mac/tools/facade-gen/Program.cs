using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FacadeGen
{
	/// <summary>Where to read and what to write.</summary>
	public sealed class GeneratorOptions
	{
		/// <summary>The folder with mac/ComicRack.Mac.slnx (found from the current directory when null).</summary>
		public string Root { get; set; }

		/// <summary>The ref/net10.0 folder of the Windows Desktop reference pack (found in the NuGet cache when null).</summary>
		public string ReferenceDirectory { get; set; }

		/// <summary>Where the generated files go; null = Generated under the shim projects. Tests set a temporary folder.</summary>
		public string FormsOutput { get; set; }

		public string DrawingOutput { get; set; }

		/// <summary>Where the files of the WPF facades go, one folder per assembly name; null = Generated under each shim project (<see cref="Generator.WpfProjectName"/>).</summary>
		public string WpfOutputRoot { get; set; }
	}

	/// <summary>The generated files of one facade assembly of the WPF set.</summary>
	public sealed class ShimOutput
	{
		public string Assembly { get; init; }

		public string Output { get; init; }

		public Dictionary<string, string> Files { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
	}

	/// <summary>The result of one run: the files of each shim project (relative path to text), counters and findings.</summary>
	public sealed class GeneratorResult
	{
		public Dictionary<string, string> FormsFiles { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

		public Dictionary<string, string> DrawingFiles { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

		public List<string> Problems { get; } = new List<string>();

		public List<string> Summary { get; } = new List<string>();

		public string FormsOutput { get; set; }

		public string DrawingOutput { get; set; }

		/// <summary>The WPF facades (System.Xaml, WindowsBase, PresentationCore, PresentationFramework), in dependency order.</summary>
		public List<ShimOutput> Wpf { get; } = new List<ShimOutput>();
	}

	public static class Generator
	{
		public const string ForwardsFile = "Forwards.g.cs";

		/// <summary>The folder name (under mac/) of the shim project of a WPF assembly.</summary>
		public static string WpfProjectName(string assembly) => "cYo.Compat." + assembly.Replace("System.", "");

		public const string OmittedFile = "omitted.txt";

		public static string FindRoot(string start)
		{
			for (string dir = Path.GetFullPath(start ?? Directory.GetCurrentDirectory()); dir != null; dir = Path.GetDirectoryName(dir))
			{
				if (File.Exists(Path.Combine(dir, "mac", "ComicRack.Mac.slnx")))
				{
					return dir;
				}
			}
			return null;
		}

		/// <summary>Reads the reference assemblies and the hand-written shim sources and builds the generated files in memory (nothing is written).</summary>
		public static GeneratorResult Generate(GeneratorOptions options)
		{
			string root = options.Root ?? FindRoot(null) ?? throw new InvalidOperationException("Cannot find mac/ComicRack.Mac.slnx above the current directory.");
			string referenceDirectory = ReferenceSurface.FindReferenceDirectory(options.ReferenceDirectory)
				?? throw new FileNotFoundException("Microsoft.WindowsDesktop.App.Ref pack not found. Run a pass A build first: dotnet build mac/ComicRack.Plugins.Core/ComicRack.Plugins.Core.csproj -p:CensusPass=A");
			var result = new GeneratorResult
			{
				FormsOutput = options.FormsOutput ?? Path.Combine(root, "mac", "cYo.Compat.Forms", "Generated"),
				DrawingOutput = options.DrawingOutput ?? Path.Combine(root, "mac", "cYo.Compat.Drawing", "Generated"),
			};
			using (var surface = new ReferenceSurface(referenceDirectory))
			{
				Run(surface, Path.Combine(root, "mac", "cYo.Compat.Forms"), ReferenceSurface.FormsAssemblies, "Forms", result.FormsFiles, result);
				Run(surface, Path.Combine(root, "mac", "cYo.Compat.Drawing"), ReferenceSurface.DrawingAssemblies, "Drawing", result.DrawingFiles, result);
				foreach (var (assembly, references) in ReferenceSurface.WpfFacades)
				{
					string project = WpfProjectName(assembly);
					var shim = new ShimOutput
					{
						Assembly = assembly,
						Output = options.WpfOutputRoot != null ? Path.Combine(options.WpfOutputRoot, assembly) : Path.Combine(root, "mac", project, "Generated"),
					};
					Run(surface, Path.Combine(root, "mac", project), references, assembly, shim.Files, result);
					AddForwards(surface, references, shim.Files);
					result.Wpf.Add(shim);
				}
			}
			return result;
		}

		/// <summary>The type forwards of the reference assemblies of one facade that lead to the runtime or to another facade (WindowsBase forwards ObservableCollection to System.ObjectModel and the markup types to System.Xaml), as assembly attributes.</summary>
		private static void AddForwards(ReferenceSurface surface, string[] references, Dictionary<string, string> files)
		{
			var skipped = new List<string>();
			var lines = new SortedSet<string>(StringComparer.Ordinal);
			foreach (string assembly in references)
			{
				foreach (var (ns, name, target) in surface.ForwardsOf(assembly, skipped))
				{
					if (references.Contains(target))
					{
						continue; // the type is declared in this facade
					}
					Type type = surface.Context.LoadFromAssemblyName(target).GetType(ns + "." + name, throwOnError: false);
					if (type == null || !ReferenceSurface.IsVisible(type))
					{
						skipped.Add(ns + "." + name + " (" + target + ": type not found)");
						continue;
					}
					lines.Add("[assembly: global::System.Runtime.CompilerServices.TypeForwardedTo(typeof(global::" + ns + "." + Names.StripArity(name) + Arity(type) + "))]\n");
				}
			}
			var sb = new StringBuilder();
			sb.Append("// <auto-generated>\n// Generated by mac/tools/facade-gen. The type forwards of " + string.Join(", ", references) + " (reference assemblies) that lead to the runtime or to another facade.\n// </auto-generated>\n");
			sb.Append("#nullable disable\n\n");
			foreach (string line in lines)
			{
				sb.Append(line);
			}
			files[ForwardsFile] = sb.ToString();
			files[OmittedFile] += "# Type forwards that lead to assemblies outside the runtime and the facades\n" + string.Join("", skipped.Distinct().OrderBy(x => x, StringComparer.Ordinal).Select(x => "forward " + x + "\n"));
		}

		private static string Arity(Type t) => t.IsGenericTypeDefinition ? "<" + new string(',', t.GetGenericArguments().Length - 1) + ">" : "";

		private static void Run(ReferenceSurface surface, string projectDirectory, string[] assemblies, string label, Dictionary<string, string> files, GeneratorResult result)
		{
			HandWrittenIndex hand = Directory.Exists(projectDirectory) ? HandWrittenIndex.Scan(projectDirectory) : new HandWrittenIndex();
			var emitter = new Emitter(surface, hand) { ForwardToImplementedBase = label == "Forms" };
			List<Type> types = surface.TypesOf(assemblies);
			foreach (Type t in types)
			{
				var file = emitter.EmitFile(t);
				if (file != null)
				{
					files[file.Value.Path] = file.Value.Text;
				}
			}
			files["FacadeStub.g.cs"] = Emitter.StubFile();
			files[OmittedFile] = "# Types, interfaces and members of the reference assemblies that this facade does not declare (written by mac/tools/facade-gen).\n" + emitter.OmittedText();
			foreach (var group in files.Keys.GroupBy(k => k.ToLowerInvariant()).Where(g => g.Count() > 1))
			{
				emitter.AddProblem("file names that differ only in case (a case-insensitive file system would merge them): " + string.Join(", ", group));
			}
			result.Problems.AddRange(emitter.Problems.Select(p => label + ": " + p));
			result.Summary.Add($"{label}: {types.Count} reference types, {emitter.Stats.Types} generated declarations, {emitter.Stats.Members} members, "
				+ $"{emitter.Stats.PartialMerges} partial hand-written types completed, {emitter.Stats.HandWrittenComplete} hand-written types left alone, {emitter.Omitted.Count} omitted"
					+ (emitter.ForwardToImplementedBase ? $", {emitter.Stats.Forwards} forwarded to an implemented base member" : ""));
		}

		/// <summary>Writes the files into the output folder: only the files whose text changed, and removes the .g.cs files that are no longer generated.</summary>
		public static int Write(string outputDirectory, Dictionary<string, string> files, out int removed)
		{
			string full = Path.GetFullPath(outputDirectory);
			if (Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) != "Generated" && !full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal))
			{
				throw new InvalidOperationException("Refusing to write into " + outputDirectory + ": the folder is not named Generated and not under the temp folder.");
			}
			int written = 0;
			removed = 0;
			Directory.CreateDirectory(outputDirectory);
			var keep = new HashSet<string>(files.Keys.Select(k => Path.GetFullPath(Path.Combine(outputDirectory, k))), StringComparer.Ordinal);
			foreach (var pair in files)
			{
				string path = Path.GetFullPath(Path.Combine(outputDirectory, pair.Key));
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				if (!File.Exists(path) || File.ReadAllText(path) != pair.Value)
				{
					File.WriteAllText(path, pair.Value, new UTF8Encoding(false));
					written++;
				}
			}
			foreach (string existing in Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories))
			{
				if (!keep.Contains(Path.GetFullPath(existing)))
				{
					File.Delete(existing);
					removed++;
				}
			}
			foreach (string dir in Directory.GetDirectories(outputDirectory, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
			{
				if (!Directory.EnumerateFileSystemEntries(dir).Any())
				{
					Directory.Delete(dir);
				}
			}
			return written;
		}

		/// <summary>The paths (relative) in which the output folder differs from the files: missing, changed and extra ones.</summary>
		public static List<string> Compare(string outputDirectory, Dictionary<string, string> files)
		{
			var differences = new List<string>();
			foreach (var pair in files.OrderBy(p => p.Key, StringComparer.Ordinal))
			{
				string path = Path.Combine(outputDirectory, pair.Key);
				if (!File.Exists(path))
				{
					differences.Add("missing " + pair.Key);
				}
				else if (File.ReadAllText(path) != pair.Value)
				{
					differences.Add("changed " + pair.Key);
				}
			}
			if (Directory.Exists(outputDirectory))
			{
				var expected = new HashSet<string>(files.Keys.Select(k => k.Replace('/', Path.DirectorySeparatorChar)), StringComparer.Ordinal);
				foreach (string existing in Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
				{
					string relative = Path.GetRelativePath(outputDirectory, existing);
					if (!expected.Contains(relative))
					{
						differences.Add("extra " + relative.Replace(Path.DirectorySeparatorChar, '/'));
					}
				}
			}
			return differences;
		}
	}

	internal static class Program
	{
		private static int Main(string[] args)
		{
			var options = new GeneratorOptions();
			bool check = false;
			for (int i = 0; i < args.Length; i++)
			{
				switch (args[i])
				{
				case "--root" when i + 1 < args.Length:
					options.Root = args[++i];
					break;
				case "--reference-pack" when i + 1 < args.Length:
					options.ReferenceDirectory = args[++i];
					break;
				case "--forms-out" when i + 1 < args.Length:
					options.FormsOutput = args[++i];
					break;
				case "--drawing-out" when i + 1 < args.Length:
					options.DrawingOutput = args[++i];
					break;
				case "--wpf-out" when i + 1 < args.Length:
					options.WpfOutputRoot = args[++i];
					break;
				case "--check":
					check = true;
					break;
				default:
					Console.Error.WriteLine("Usage: facade-gen [--check] [--root DIR] [--reference-pack DIR] [--forms-out DIR] [--drawing-out DIR] [--wpf-out DIR]");
					Console.Error.WriteLine("  Generates mac/cYo.Compat.Forms/Generated and mac/cYo.Compat.Drawing/Generated from the Windows Desktop reference assemblies.");
					Console.Error.WriteLine("  --check writes nothing and exits 1 when the generated folders differ from what the generator produces now.");
					return 2;
				}
			}
			GeneratorResult result;
			try
			{
				result = Generator.Generate(options);
			}
			catch (FileNotFoundException e)
			{
				Console.Error.WriteLine("facade-gen: " + e.Message);
				return 3;
			}
			foreach (string line in result.Summary)
			{
				Console.WriteLine(line);
			}
			if (result.Problems.Count > 0)
			{
				Console.Error.WriteLine("facade-gen: " + result.Problems.Count + " problem(s) in the hand-written shim sources:");
				foreach (string p in result.Problems)
				{
					Console.Error.WriteLine("  " + p);
				}
				return 4;
			}
			if (check)
			{
				var differences = Generator.Compare(result.FormsOutput, result.FormsFiles).Select(d => "Forms: " + d)
					.Concat(Generator.Compare(result.DrawingOutput, result.DrawingFiles).Select(d => "Drawing: " + d))
					.Concat(result.Wpf.SelectMany(w => Generator.Compare(w.Output, w.Files).Select(d => w.Assembly + ": " + d))).ToList();
				if (differences.Count > 0)
				{
					Console.Error.WriteLine("facade-gen --check: " + differences.Count + " difference(s); run: dotnet run --project mac/tools/facade-gen");
					foreach (string d in differences.Take(30))
					{
						Console.Error.WriteLine("  " + d);
					}
					return 1;
				}
				Console.WriteLine("facade-gen --check: the generated folders are up to date");
				return 0;
			}
			int written = Generator.Write(result.FormsOutput, result.FormsFiles, out int removedForms) + Generator.Write(result.DrawingOutput, result.DrawingFiles, out int removedDrawing);
			int removedWpf = 0;
			foreach (ShimOutput w in result.Wpf)
			{
				written += Generator.Write(w.Output, w.Files, out int removed);
				removedWpf += removed;
			}
			Console.WriteLine($"facade-gen: {written} file(s) written, {removedForms + removedDrawing + removedWpf} removed");
			return 0;
		}
	}
}
