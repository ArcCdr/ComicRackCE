using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using cYo.Projects.ComicRack.Plugins;

namespace ComicRack.Mac.Host.Scripting.Scan
{
	/// <summary>
	/// The facts of a plugin package that need the plugin host (P4.17): the package is installed through the real install path (<see cref="PluginPackages"/>) into a throw-away data folder, the commands it
	/// registers are listed, and the top level of each script that has commands is run, which is what the host does when a command is first used (imports, <c>clr.AddReference</c> lines, class
	/// definitions). Nothing else of the plugin runs. The package is untrusted: it is only read, and only the host runs its code.
	/// </summary>
	public static class PluginProbe
	{
		public static PluginFacts Probe(string packageFile)
		{
			string full = Path.GetFullPath(packageFile);
			string folder = Path.GetDirectoryName(full);
			var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			string sha;
			using (var stream = File.OpenRead(full))
			{
				sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
			}
			ReadSourceFile(Path.Combine(folder, "SOURCE.txt"), facts);
			Dictionary<string, string> ini = ReadIni(full);

			var commands = new List<GapCommand>();
			var checks = new List<GapLoadCheck>();
			string temp = Path.Combine(Path.GetTempPath(), "crgap-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(temp);
			try
			{
				MacPaths paths = MacPaths.Under(temp);
				ScriptFolders folders = ScriptFolders.For(paths);
				// no bundled scripts: only the package is in the host
				folders = new ScriptFolders(null, folders.User);
				using (MacHost host = MacHost.Open(new MacHostOptions { Paths = paths, DatabaseReadOnly = true, Scripting = true, ScriptFolders = folders }))
				{
					var packages = new PluginPackages(folders, host.Scripts);
					string name = PluginPackages.NameOf(full);
					if (name != null && packages.InstallNow(full))
					{
						string installed = Path.Combine(packages.PackagesFolder, name);
						var console = new StringWriter();
						host.Scripts.AttachConsole(console);
						foreach (string script in Directory.GetFiles(installed, "*.py", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
						{
							string relative = Path.GetRelativePath(installed, script).Replace('\\', '/');
							IReadOnlyList<Command> found = host.Scripts.LoadCommands(script);
							if (found.Count == 0)
							{
								continue;
							}
							foreach (Command c in found)
							{
								commands.Add(new GapCommand { Name = c.Name, Key = c.Key, Hook = c.Hook, Enabled = c.Enabled, File = relative });
							}
							string message = null;
							try
							{
								foreach (PythonCommand command in found.OfType<PythonCommand>().Take(1))
								{
									command.PreCompile(handleException: false);
								}
							}
							catch (Exception ex)
							{
								Exception inner = ex;
								while (inner.InnerException != null && (inner is System.Reflection.TargetInvocationException || inner is AggregateException))
								{
									inner = inner.InnerException;
								}
								message = (inner.GetType().Name + ": " + inner.Message).Replace("\r", " ").Replace("\n", " ");
							}
							checks.Add(new GapLoadCheck { File = relative, Ok = message == null, Message = message, Commands = found.Count });
						}
					}
				}
			}
			finally
			{
				try
				{
					Directory.Delete(temp, recursive: true);
				}
				catch (Exception)
				{
				}
			}
			facts.TryGetValue("url", out string url);
			facts.TryGetValue("licence", out string licence);
			facts.TryGetValue("pin", out string pin);
			return new PluginFacts
			{
				Id = Path.GetFileNameWithoutExtension(full),
				PackageFile = Path.GetFileName(full),
				Sha256 = sha,
				Size = new FileInfo(full).Length,
				Url = url,
				Licence = licence,
				Pin = pin,
				Ini = ini,
				Commands = commands.OrderBy(c => c.File, StringComparer.Ordinal).ThenBy(c => c.Name, StringComparer.Ordinal).ToList(),
				LoadChecks = checks,
			};
		}

		private static void ReadSourceFile(string file, Dictionary<string, string> into)
		{
			if (!File.Exists(file))
			{
				return;
			}
			foreach (string line in File.ReadAllLines(file))
			{
				int colon = line.IndexOf(':');
				if (colon > 0)
				{
					into[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
				}
			}
		}

		private static Dictionary<string, string> ReadIni(string package)
		{
			var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				using ZipArchive zip = ZipFile.OpenRead(package);
				ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, "Package.ini", StringComparison.OrdinalIgnoreCase));
				if (entry == null)
				{
					return result;
				}
				using var reader = new StreamReader(entry.Open());
				string line;
				while ((line = reader.ReadLine()) != null)
				{
					int eq = line.IndexOf('=');
					if (eq > 0)
					{
						result[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
					}
				}
			}
			catch (InvalidDataException)
			{
			}
			return result;
		}
	}
}
