using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace ComicRack.Mac.Host.Scripting.Scan
{
	/// <summary>
	/// The script compatibility scanner (P4.6a, MACOS_NATIVE_PATH.md section 5.9.9): scans script files (<c>.py</c>), .NET assemblies (<c>.dll</c>, <c>.exe</c>), HTML files and plugin packages
	/// (<c>.crplugin</c> or <c>.zip</c>, read in memory, never unpacked) below the given paths, and builds the ranked usage table of the WinForms and GDI+ surface.
	/// It knows nothing about single plugins: the same scan runs for the bundled samples, a plugin package, and a corpus folder.
	/// </summary>
	public sealed class ScriptScanner
	{
		public const long MaxFileBytes = 64L * 1024 * 1024;

		private const int MaxPackageEntries = 20000;

		private static readonly string[] PackageExtensions = { ".crplugin", ".zip" };

		private readonly ApiCatalog catalog;
		private readonly PythonScanner python;
		private readonly AssemblyScanner assemblies;

		public ScriptScanner(ApiCatalog catalog)
		{
			this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
			python = new PythonScanner(catalog);
			assemblies = new AssemblyScanner(catalog);
		}

		public ApiCatalog Catalog => catalog;

		/// <summary>Scans the files and folders; the result lists every unit in a stable order.</summary>
		public ScanReport Scan(IEnumerable<string> paths)
		{
			var units = new List<ScanUnit>();
			var bundled = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
			var inputs = new List<string>();
			foreach (string input in paths)
			{
				string full = Path.GetFullPath(input);
				inputs.Add(input.Replace('\\', '/'));
				if (Directory.Exists(full))
				{
					ScanDirectory(full, units, bundled);
				}
				else if (File.Exists(full))
				{
					ScanFileEntry(full, Path.GetDirectoryName(full), units, bundled);
				}
				else
				{
					throw new FileNotFoundException("Nothing to scan at " + input, full);
				}
			}
			foreach (ScanUnit unit in units)
			{
				ReferenceChecks(unit, bundled.TryGetValue(unit.Package ?? string.Empty, out var set) ? set : null);
			}
			units.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
			return new ScanReport(catalog, inputs, units);
		}

		/// <summary>Scans one script given as text (for tests and for a report at install time).</summary>
		public ScanUnit ScanPython(string source, string path = "script.py")
		{
			var unit = new ScanUnit { Path = path, Package = Path.GetFileNameWithoutExtension(path) };
			python.Scan(source, unit);
			ReferenceChecks(unit, null);
			return unit;
		}

		/// <summary>Scans the bytes of an assembly.</summary>
		public ScanUnit ScanAssembly(byte[] data, string path = "library.dll")
		{
			var unit = new ScanUnit { Path = path, Package = Path.GetFileNameWithoutExtension(path) };
			assemblies.Scan(data, unit);
			ReferenceChecks(unit, null);
			unit.Tier = unit.Parsed ? ApiTiers.ForUnit(catalog, unit) : unit.Tier;
			return unit;
		}

		private void ScanDirectory(string root, List<ScanUnit> units, Dictionary<string, HashSet<string>> bundled)
		{
			foreach (string file in EnumerateFiles(root))
			{
				ScanFileEntry(file, root, units, bundled);
			}
		}

		private static IEnumerable<string> EnumerateFiles(string root)
		{
			var pending = new Stack<string>();
			pending.Push(root);
			var found = new List<string>();
			while (pending.Count > 0)
			{
				string dir = pending.Pop();
				foreach (string sub in Directory.GetDirectories(dir))
				{
					string name = Path.GetFileName(sub);
					if (name == ".git" || name == "__pycache__" || name == "node_modules")
					{
						continue;
					}
					pending.Push(sub);
				}
				found.AddRange(Directory.GetFiles(dir));
			}
			found.Sort(StringComparer.Ordinal);
			return found;
		}

		private static string Relative(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');

		private static string PackageOf(string relative)
		{
			int slash = relative.IndexOf('/');
			return slash > 0 ? relative.Substring(0, slash) : Path.GetFileNameWithoutExtension(relative);
		}

		private void ScanFileEntry(string file, string root, List<ScanUnit> units, Dictionary<string, HashSet<string>> bundled)
		{
			string extension = Path.GetExtension(file).ToLowerInvariant();
			string relative = Relative(root, file);
			string package = PackageOf(relative);
			if (PackageExtensions.Contains(extension))
			{
				ScanPackage(file, relative, Path.GetFileNameWithoutExtension(file), units, bundled);
				return;
			}
			if (!IsScannable(extension))
			{
				return;
			}
			var info = new FileInfo(file);
			if (info.Length > MaxFileBytes)
			{
				units.Add(new ScanUnit { Path = relative, Package = package, Kind = KindOf(extension), Parsed = false, Tier = "?", Error = "The file is larger than " + MaxFileBytes / (1024 * 1024) + " MB and was not scanned." });
				return;
			}
			ScanContent(File.ReadAllBytes(file), extension, relative, package, units, bundled);
		}

		private void ScanPackage(string file, string relative, string package, List<ScanUnit> units, Dictionary<string, HashSet<string>> bundled)
		{
			try
			{
				using ZipArchive zip = ZipFile.OpenRead(file);
				if (zip.Entries.Count > MaxPackageEntries)
				{
					units.Add(new ScanUnit { Path = relative, Package = package, Kind = "package", Parsed = false, Tier = "?", Error = "The package has more than " + MaxPackageEntries + " entries and was not scanned." });
					return;
				}
				foreach (ZipArchiveEntry entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
				{
					string extension = Path.GetExtension(entry.FullName).ToLowerInvariant();
					if (entry.Name.Length == 0 || !IsScannable(extension))
					{
						continue;
					}
					string path = relative + "!/" + entry.FullName.Replace('\\', '/');
					if (entry.Length > MaxFileBytes)
					{
						units.Add(new ScanUnit { Path = path, Package = package, Kind = KindOf(extension), Parsed = false, Tier = "?", Error = "The entry is larger than " + MaxFileBytes / (1024 * 1024) + " MB and was not scanned." });
						continue;
					}
					using Stream s = entry.Open();
					using var buffer = new MemoryStream();
					s.CopyTo(buffer);
					ScanContent(buffer.ToArray(), extension, path, package, units, bundled);
				}
			}
			catch (InvalidDataException ex)
			{
				units.Add(new ScanUnit { Path = relative, Package = package, Kind = "package", Parsed = false, Tier = "?", Error = "Not a plugin package: " + ex.Message });
			}
		}

		private static bool IsScannable(string extension) => extension == ".py" || extension == ".dll" || extension == ".exe" || extension == ".html" || extension == ".htm";

		private static string KindOf(string extension) => extension == ".py" ? "python" : extension == ".dll" || extension == ".exe" ? "assembly" : "html";

		private void ScanContent(byte[] data, string extension, string path, string package, List<ScanUnit> units, Dictionary<string, HashSet<string>> bundled)
		{
			var unit = new ScanUnit { Path = path, Package = package, Kind = KindOf(extension) };
			switch (extension)
			{
			case ".py":
				python.Scan(Decode(data), unit);
				break;
			case ".dll":
			case ".exe":
				assemblies.Scan(data, unit);
				if (!bundled.TryGetValue(package, out HashSet<string> set))
				{
					bundled[package] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				}
				set.Add(Path.GetFileNameWithoutExtension(path));
				break;
			default:
				HtmlChecks.Check(Decode(data), 1, unit);
				unit.Tier = "none";
				break;
			}
			units.Add(unit);
		}

		/// <summary>Source text: UTF-8 (with or without byte order mark) when valid, else Latin-1 (the encoding of most Python 2 plugins).</summary>
		internal static string Decode(byte[] data)
		{
			try
			{
				string text = new UTF8Encoding(false, true).GetString(data);
				return text.Length > 0 && text[0] == '﻿' ? text.Substring(1) : text;
			}
			catch (DecoderFallbackException)
			{
				return Encoding.Latin1.GetString(data);
			}
		}

		/// <summary>Findings about the assemblies a unit references, once the files of its package are known (an assembly that ships with the plugin is not missing).</summary>
		private static void ReferenceChecks(ScanUnit unit, HashSet<string> bundledAssemblies)
		{
			foreach (ScanReference r in unit.References.ToList())
			{
				string kind = r.Kind;
				if (bundledAssemblies != null && bundledAssemblies.Contains(r.Name) && kind != "wpf" && kind != "com" && kind != "windows")
				{
					kind = "bundled";
				}
				ScanReference updated = new ScanReference { Name = r.Name, Kind = kind, Line = r.Line, Guarded = r.Guarded };
				unit.References[unit.References.IndexOf(r)] = updated;
				switch (kind)
				{
				case "wpf":
					unit.AddFinding(ScanFinding.WindowsOnly("wpf", r.Name, r.Line, "Windows Presentation Foundation", guarded: r.Guarded));
					break;
				case "com":
					unit.AddFinding(ScanFinding.WindowsOnly("com", r.Name, r.Line, "COM interop", guarded: r.Guarded));
					break;
				case "windows":
					unit.AddFinding(ScanFinding.WindowsOnly("windows-assembly", r.Name, r.Line, "a Windows only assembly", guarded: r.Guarded));
					break;
				case "framework-only":
					unit.AddFinding(new ScanFinding
					{
						Severity = "warning",
						Kind = "framework-only-assembly",
						Symbol = r.Name,
						Line = r.Line,
						Message = "References `" + r.Name + "`, which exists only in the .NET Framework and is not part of .NET 10" + (r.Line > 0 ? " (line " + r.Line + ")" : string.Empty) + "; what uses it fails on macOS until the layer provides it.",
					});
					break;
				case "core-missing":
					unit.AddFinding(new ScanFinding
					{
						Severity = "warning",
						Kind = "missing-assembly",
						Symbol = r.Name,
						Line = r.Line,
						Message = "References `" + r.Name + "`, which belongs to the Windows program and is not part of the macOS build" + (r.Line > 0 ? " (line " + r.Line + ")" : string.Empty) + ".",
					});
					break;
				}
			}
			if (unit.Findings.Any(f => f.Severity == "error"))
			{
				unit.Tier = "C";
			}
		}
	}
}
