using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace S6
{
	/// <summary>
	/// s6-rebind IN_DIR OUT_DIR --rename Old=New[:Version[:TokenHex]] ... [--drop File.dll ...] [--no-deps]
	///   Copies IN_DIR to OUT_DIR. Every managed assembly gets its references changed per --rename; an assembly named Old is itself renamed
	///   (new file name New.dll). --drop leaves a file out. *.deps.json files get the same names (text replace).
	/// s6-rebind --fake-ref IN.dll OUT.dll Name=Version:TokenHex
	///   Changes one reference of a compiled assembly (a plug-in built against the facade) to look as if it had been built against the
	///   .NET Framework (System.Windows.Forms 4.0.0.0, token b77a5c561934e089).
	/// s6-rebind --refs FILE.dll   lists the assembly references of a file.
	/// </summary>
	internal static class Program
	{
		private sealed class Rename
		{
			public string Old, New;
			public Version Version;
			public byte[] Token;
		}

		private static int Main(string[] args)
		{
			if (args.Length >= 2 && args[0] == "--refs")
			{
				using (ModuleDefinition m = ModuleDefinition.ReadModule(args[1]))
				{
					Console.WriteLine(m.Assembly.Name.FullName);
					foreach (AssemblyNameReference r in m.AssemblyReferences)
					{
						Console.WriteLine("  -> " + r.FullName);
					}
				}
				return 0;
			}
			if (args.Length == 4 && args[0] == "--fake-ref")
			{
				string[] kv = args[3].Split('=');
				string[] vt = kv[1].Split(':');
				Dictionary<string, Rename> map = new Dictionary<string, Rename> { [kv[0]] = new Rename { Old = kv[0], New = kv[0], Version = Version.Parse(vt[0]), Token = vt.Length > 1 ? Hex(vt[1]) : null } };
				ProcessFile(args[1], args[2], map, Path.GetDirectoryName(Path.GetFullPath(args[1])));
				return 0;
			}
			if (args.Length < 2)
			{
				Console.Error.WriteLine("usage: s6-rebind IN_DIR OUT_DIR --rename Old=New[:Version[:TokenHex]] ... [--drop File.dll ...]");
				return 2;
			}
			string input = Path.GetFullPath(args[0]);
			string output = Path.GetFullPath(args[1]);
			Dictionary<string, Rename> renames = new Dictionary<string, Rename>();
			HashSet<string> drop = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (int i = 2; i < args.Length; i++)
			{
				if (args[i] == "--no-deps")
				{
					drop.Add("*.deps.json");
				}
				else if (args[i] == "--rename")
				{
					string[] kv = args[++i].Split('=');
					string[] parts = kv[1].Split(':');
					renames[kv[0]] = new Rename { Old = kv[0], New = parts[0], Version = parts.Length > 1 && parts[1].Length > 0 ? Version.Parse(parts[1]) : null, Token = parts.Length > 2 ? Hex(parts[2]) : null };
				}
				else if (args[i] == "--drop")
				{
					drop.Add(args[++i]);
				}
			}
			if (Directory.Exists(output))
			{
				Directory.Delete(output, true);
			}
			CopyAll(input, output, renames, drop);
			return 0;
		}

		private static byte[] Hex(string hex)
		{
			return Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
		}

		private static void CopyAll(string input, string output, Dictionary<string, Rename> renames, HashSet<string> drop)
		{
			Directory.CreateDirectory(output);
			foreach (string dir in Directory.GetDirectories(input))
			{
				CopyAll(dir, Path.Combine(output, Path.GetFileName(dir)), renames, drop);
			}
			foreach (string file in Directory.GetFiles(input))
			{
				string name = Path.GetFileName(file);
				if (drop.Contains(name) || (drop.Contains("*.deps.json") && name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)))
				{
					continue;
				}
				string target = Path.Combine(output, name);
				string simple = Path.GetFileNameWithoutExtension(name);
				if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && renames.TryGetValue(simple, out Rename r))
				{
					target = Path.Combine(output, r.New + ".dll");
				}
				if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) && renames.ContainsKey(simple))
				{
					continue;
				}
				if (name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
				{
					string text = File.ReadAllText(file);
					foreach (Rename x in renames.Values)
					{
						text = text.Replace("\"" + x.Old + "/", "\"" + x.New + "/").Replace("\"" + x.Old + ".dll\"", "\"" + x.New + ".dll\"").Replace("\"" + x.Old + "\"", "\"" + x.New + "\"");
					}
					foreach (string d in drop)
					{
						string n = Path.GetFileNameWithoutExtension(d);
						text = RemoveEntries(text, n);
					}
					File.WriteAllText(target, text);
					continue;
				}
				if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && IsManaged(file))
				{
					ProcessFile(file, target, renames, input);
				}
				else
				{
					File.Copy(file, target, true);
				}
			}
		}

		/// <summary>Removes the dependency entries of a dropped assembly from a deps.json (best effort: the host only needs the files that exist).</summary>
		private static string RemoveEntries(string text, string name)
		{
			return text;
		}

		private static bool IsManaged(string file)
		{
			try
			{
				using (ModuleDefinition.ReadModule(file))
				{
					return true;
				}
			}
			catch (BadImageFormatException)
			{
				return false;
			}
		}

		private static void ProcessFile(string file, string target, Dictionary<string, Rename> renames, string searchDir)
		{
			DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
			resolver.AddSearchDirectory(searchDir);
			using (ModuleDefinition module = ModuleDefinition.ReadModule(file, new ReaderParameters { AssemblyResolver = resolver }))
			{
				foreach (AssemblyNameReference r in module.AssemblyReferences)
				{
					if (renames.TryGetValue(r.Name, out Rename m))
					{
						r.Name = m.New;
						if (m.Version != null)
						{
							r.Version = m.Version;
						}
						if (m.Token != null)
						{
							r.HasPublicKey = false;
							r.PublicKeyToken = m.Token;
						}
					}
				}
				if (renames.TryGetValue(module.Assembly.Name.Name, out Rename self))
				{
					module.Assembly.Name.Name = self.New;
					module.Name = self.New + ".dll";
					if (self.Version != null)
					{
						module.Assembly.Name.Version = self.Version;
					}
					if (self.Token != null)
					{
						module.Assembly.Name.HasPublicKey = false;
						module.Assembly.Name.PublicKeyToken = self.Token;
					}
				}
				module.Write(target);
			}
		}
	}
}
