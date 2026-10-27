using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;

namespace ComicRack.Mac.Host.Scripting
{
	/// <summary>One installed or pending plugin package.</summary>
	public sealed class PluginPackageInfo
	{
		public string Name { get; init; }
		public string Version { get; init; }
		public string Author { get; init; }
		public string Description { get; init; }
		public string HelpLink { get; init; }

		/// <summary>The folder of the package (the pending folder for a package that is not installed yet).</summary>
		public string Path { get; init; }

		/// <summary>Installed, PendingInstall (takes effect at the next start) or PendingRemove (the folder goes at the next start).</summary>
		public PackageManager.PackageType State { get; init; }

		/// <summary>True when the package has commands and all of them are enabled; null when no loaded command belongs to it (not loaded yet, or no host).</summary>
		public bool? Enabled { get; init; }
	}

	/// <summary>
	/// Plugin packages (<c>.crplugin</c>, a zip of script files and a <c>package.ini</c>) over <c>PackageManager</c> of the original engine, in the user's script folder
	/// (<see cref="ScriptFolders.User"/>; pending installs in its <c>.Pending</c> folder), the way the WinForms program does it (P4.4): <see cref="Install"/> unpacks into the
	/// pending folder, <see cref="Commit"/> (the original does it when the program starts) moves pending packages into place and removes the ones marked for removal.
	/// The original <c>CommitInstallPackage</c> deletes the files a package does not keep with bare file names, which are relative to the working directory instead of the package folder;
	/// <see cref="Commit"/> therefore does the install step itself with full paths (proposal: mac/upstream-proposals/P4.4-packagemanager-safedelete.md). The scripting layer knows nothing
	/// about single plugins: enabling and disabling works on the commands whose script file lies in the package folder.
	/// </summary>
	public sealed class PluginPackages
	{
		private readonly PackageManager manager;
		private readonly ScriptHost scripts;

		public string PackagesFolder { get; }

		public string PendingFolder { get; }

		/// <param name="folders">The script folders; the user folder holds the packages.</param>
		/// <param name="scripts">The script host whose commands enable/disable acts on; may be null.</param>
		public PluginPackages(ScriptFolders folders, ScriptHost scripts = null)
		{
			if (folders?.User == null)
			{
				throw new ArgumentException("The script folders have no user folder.", nameof(folders));
			}
			PackagesFolder = folders.User;
			PendingFolder = System.IO.Path.Combine(PackagesFolder, ".Pending");
			manager = new PackageManager(PackagesFolder, PendingFolder, commit: false);
			this.scripts = scripts;
		}

		/// <summary>The name a package file would be installed under, or null when the file is not a readable package or its name is not a plain folder name.</summary>
		public static string NameOf(string packageFile)
		{
			string name = PackageManager.Package.GetName(packageFile);
			return IsPlainName(name) ? name : null;
		}

		/// <summary>True when a package with the name of this file is installed or pending (the WinForms "replace?" question).</summary>
		public bool Exists(string packageFile)
		{
			string name = NameOf(packageFile);
			return name != null && manager.PackageExists(name);
		}

		/// <summary>Unpacks a package file into the pending folder (replacing a pending copy). False when the file is not a package. Takes effect at <see cref="Commit"/>.</summary>
		public bool Install(string packageFile)
		{
			if (!File.Exists(packageFile) || NameOf(packageFile) == null)
			{
				return false;
			}
			return manager.Install(packageFile);
		}

		/// <summary>Installs and commits at once (a tool run, a test). Existing files of the package that its KeepFiles do not list are replaced.</summary>
		public bool InstallNow(string packageFile)
		{
			if (!Install(packageFile))
			{
				return false;
			}
			Commit();
			return true;
		}

		/// <summary>Marks an installed package for removal (its folder goes at <see cref="Commit"/>) or drops a pending install. False when no such package exists.</summary>
		public bool Uninstall(string name)
		{
			PackageManager.Package package = manager.GetPackages().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
			return package != null && manager.Uninstall(package);
		}

		/// <summary>Takes back a removal mark or a pending install: what the WinForms Preferences does by closing without restarting.</summary>
		public void RemovePending() => manager.RemovePending();

		/// <summary>Removes packages marked for removal and moves pending installs into place.</summary>
		public void Commit()
		{
			var packages = manager.GetPackages();
			foreach (var package in packages.Where(p => p.PackageType == PackageManager.PackageType.PendingRemove))
			{
				DeleteFolder(package.PackagePath);
			}
			foreach (var package in packages.Where(p => p.PackageType == PackageManager.PackageType.PendingInstall))
			{
				CommitInstall(package);
			}
		}

		/// <summary>The packages, installed ones and pending ones.</summary>
		public IReadOnlyList<PluginPackageInfo> List()
		{
			return manager.GetPackages().Select(p => new PluginPackageInfo
			{
				Name = p.Name,
				Version = p.Version,
				Author = p.Author,
				Description = p.Description,
				HelpLink = p.HelpLink,
				Path = p.PackagePath,
				State = p.PackageType,
				Enabled = EnabledState(p),
			}).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
		}

		/// <summary>The commands a package provides (loaded commands whose script file lies in its folder).</summary>
		public IReadOnlyList<Command> CommandsOf(string name)
		{
			PackageManager.Package package = manager.GetPackages().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
			return package == null ? Array.Empty<Command>() : CommandsIn(package.PackagePath);
		}

		/// <summary>Switches every command of a package on or off (the state goes into the preferences with <see cref="ScriptHost.StoreCommandStates"/>). Returns how many commands changed.</summary>
		public int SetEnabled(string name, bool enabled)
		{
			int changed = 0;
			foreach (Command command in CommandsOf(name))
			{
				if (command.Enabled != enabled)
				{
					command.Enabled = enabled;
					changed++;
				}
			}
			return changed;
		}

		private bool? EnabledState(PackageManager.Package package)
		{
			if (package.PackageType == PackageManager.PackageType.PendingInstall)
			{
				return null;
			}
			IReadOnlyList<Command> commands = CommandsIn(package.PackagePath);
			return commands.Count == 0 ? null : commands.All(c => c.Enabled);
		}

		private IReadOnlyList<Command> CommandsIn(string folder)
		{
			if (scripts == null)
			{
				return Array.Empty<Command>();
			}
			string root = System.IO.Path.GetFullPath(folder).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
			return scripts.Engine.GetAllCommands()
				.OfType<PythonCommand>()
				.Where(c => !string.IsNullOrEmpty(c.ScriptFile) && System.IO.Path.GetFullPath(c.ScriptFile).StartsWith(root, StringComparison.Ordinal))
				.ToArray();
		}

		private void CommitInstall(PackageManager.Package package)
		{
			if (!IsPlainName(package.Name))
			{
				DeleteFolder(package.PackagePath);
				return;
			}
			string target = System.IO.Path.Combine(PackagesFolder, package.Name);
			try
			{
				Directory.CreateDirectory(target);
				if (package.KeepFiles != null && package.KeepFiles.Length > 0)
				{
					List<Regex> keep = package.KeepFiles
						.Select(pattern => new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase))
						.ToList();
					foreach (string file in Directory.GetFiles(target).Where(f => !keep.Any(r => r.IsMatch(System.IO.Path.GetFileName(f)))))
					{
						File.Delete(file);
					}
					foreach (string dir in Directory.GetDirectories(target).Where(d => !keep.Any(r => r.IsMatch(System.IO.Path.GetFileName(d)))))
					{
						Directory.Delete(dir, recursive: true);
					}
				}
				foreach (string file in Directory.GetFiles(package.PackagePath))
				{
					File.Copy(file, System.IO.Path.Combine(target, System.IO.Path.GetFileName(file)), overwrite: true);
				}
			}
			catch (Exception)
			{
				DeleteFolder(target);
			}
			finally
			{
				DeleteFolder(package.PackagePath);
			}
		}

		private static void DeleteFolder(string folder)
		{
			try
			{
				if (Directory.Exists(folder))
				{
					Directory.Delete(folder, recursive: true);
				}
			}
			catch (Exception)
			{
			}
		}

		private static bool IsPlainName(string name)
		{
			return !string.IsNullOrWhiteSpace(name) && name != "." && name != ".." && !name.StartsWith('.') && name.IndexOfAny(new[] { '/', '\\', ':', '\0' }) < 0;
		}
	}
}
