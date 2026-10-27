using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ComicRack.Mac.Host.Scripting;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>P4.4: .crplugin packages over the original PackageManager: install, list, commit, remove, replace, keep files, enable and disable. The packages are generated here.</summary>
	public class PluginPackageTests
	{
		private const string Script = "#@Name Pack Command\n#@Key PackCommand\n#@Hook Books\ndef PackCommand(books):\n\tprint 'pack', len(books)\n";

		private static string Package(TempFolder temp, string file, string name, string version, string extraIni = "", params (string name, string content)[] files)
		{
			string path = Path.Combine(temp.Path, file);
			using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
			{
				void Add(string entry, string content)
				{
					using (var w = new StreamWriter(zip.CreateEntry(entry).Open()))
					{
						w.Write(content);
					}
				}
				Add("package.ini", "Name=" + name + "\nVersion=" + version + "\nAuthor=Tester\nDescription=A test plugin\n" + extraIni);
				foreach (var f in files.Length == 0 ? new (string name, string content)[] { ("PackCommand.py", Script) } : files)
				{
					Add(f.name, f.content);
				}
			}
			return path;
		}

		private static ScriptFolders Folders(TempFolder temp) => new ScriptFolders(null, Path.Combine(temp.Path, "user-scripts"));

		[Fact]
		public void Install_stages_a_package_and_commit_puts_it_into_the_script_folder()
		{
			using (var temp = new TempFolder())
			{
				ScriptFolders folders = Folders(temp);
				var packages = new PluginPackages(folders);
				string file = Package(temp, "a.crplugin", "Pack Plugin", "1.2");
				Assert.Equal("Pack Plugin", PluginPackages.NameOf(file));
				Assert.False(packages.Exists(file));
				Assert.True(packages.Install(file));
				PluginPackageInfo pending = Assert.Single(packages.List());
				Assert.Equal(PackageManager.PackageType.PendingInstall, pending.State);
				Assert.Equal("1.2", pending.Version);
				Assert.Equal("Tester", pending.Author);
				Assert.False(File.Exists(Path.Combine(folders.User, "Pack Plugin", "PackCommand.py")));
				Assert.True(packages.Exists(file));

				packages.Commit();
				PluginPackageInfo installed = Assert.Single(packages.List());
				Assert.Equal(PackageManager.PackageType.Installed, installed.State);
				Assert.Equal(Path.Combine(folders.User, "Pack Plugin"), installed.Path);
				Assert.True(File.Exists(Path.Combine(installed.Path, "PackCommand.py")));
				Assert.False(Directory.Exists(Path.Combine(folders.User, ".Pending", "Pack Plugin")));
			}
		}

		[Fact]
		public void An_installed_package_is_loaded_by_the_next_host_and_can_be_switched_off_and_on()
		{
			using (var temp = new TempFolder())
			{
				ScriptFolders folders = Folders(temp);
				Assert.True(new PluginPackages(folders).InstallNow(Package(temp, "a.crplugin", "Pack Plugin", "1")));
				using (MacHost host = MacHost.Open(new MacHostOptions { Paths = temp.Paths, ScriptFolders = folders, DatabaseBackgroundSaveSeconds = 0 }))
				{
					var packages = new PluginPackages(folders, host.Scripts);
					Assert.Equal("PackCommand", Assert.Single(packages.CommandsOf("Pack Plugin")).Key);
					Assert.True(packages.List().Single().Enabled);
					Assert.Contains(host.Scripts.GetCommands(PluginEngine.ScriptTypeBooks), c => c.Key == "PackCommand");

					Assert.Equal(1, packages.SetEnabled("Pack Plugin", false));
					Assert.False(packages.List().Single().Enabled);
					Assert.DoesNotContain(host.Scripts.GetCommands(PluginEngine.ScriptTypeBooks), c => c.Key == "PackCommand");
					host.Scripts.StoreCommandStates();
					Assert.Contains("-PackCommand", host.Settings.Preferences.PluginsStates);

					Assert.Equal(1, packages.SetEnabled("Pack Plugin", true));
					Assert.True(packages.List().Single().Enabled);
					Assert.Equal(0, packages.SetEnabled("Pack Plugin", true));
					Assert.Equal(0, packages.SetEnabled("No such", false));
				}
			}
		}

		[Fact]
		public void Uninstall_marks_an_installed_package_and_commit_removes_it_and_a_pending_install_is_dropped()
		{
			using (var temp = new TempFolder())
			{
				ScriptFolders folders = Folders(temp);
				var packages = new PluginPackages(folders);
				Assert.True(packages.InstallNow(Package(temp, "a.crplugin", "Pack Plugin", "1")));
				Assert.True(packages.Uninstall("Pack Plugin"));
				Assert.Equal(PackageManager.PackageType.PendingRemove, packages.List().Single().State);
				Assert.True(Directory.Exists(Path.Combine(folders.User, "Pack Plugin")));
				packages.RemovePending();
				Assert.Equal(PackageManager.PackageType.Installed, packages.List().Single().State);
				Assert.True(packages.Uninstall("Pack Plugin"));
				packages.Commit();
				Assert.Empty(packages.List());
				Assert.False(Directory.Exists(Path.Combine(folders.User, "Pack Plugin")));

				Assert.True(packages.Install(Package(temp, "b.crplugin", "Other Plugin", "1")));
				Assert.True(packages.Uninstall("Other Plugin"));
				Assert.Empty(packages.List());
				Assert.False(packages.Uninstall("Other Plugin"));
			}
		}

		[Fact]
		public void Installing_again_replaces_the_files_and_keeps_only_the_listed_ones_with_full_paths()
		{
			using (var temp = new TempFolder())
			{
				ScriptFolders folders = Folders(temp);
				var packages = new PluginPackages(folders);
				Assert.True(packages.InstallNow(Package(temp, "a.crplugin", "Keeper", "1", files: new[] { ("one.py", "x=1"), ("old.txt", "old") })));
				string target = Path.Combine(folders.User, "Keeper");
				File.WriteAllText(Path.Combine(target, "settings.dat"), "mine");
				Directory.CreateDirectory(Path.Combine(target, "cache"));
				File.WriteAllText(Path.Combine(target, "cache", "c.bin"), "c");
				File.WriteAllText(Path.Combine(target, "stale.log"), "stale");

				// the original deletes with bare file names, relative to the working directory: a same named file there must survive
				string bait = Path.Combine(Directory.GetCurrentDirectory(), "stale.log");
				File.WriteAllText(bait, "bait");
				try
				{
					string v2 = Package(temp, "b.crplugin", "Keeper", "2", "KeepFiles=settings.dat, cache\n", ("two.py", "x=2"));
					Assert.True(packages.Exists(v2));
					Assert.True(packages.InstallNow(v2));
					Assert.True(File.Exists(bait), "a file in the working directory was deleted");
				}
				finally
				{
					File.Delete(bait);
				}
				Assert.False(File.Exists(Path.Combine(target, "stale.log")));
				Assert.False(File.Exists(Path.Combine(target, "old.txt")));
				Assert.False(File.Exists(Path.Combine(target, "one.py")));
				Assert.True(File.Exists(Path.Combine(target, "two.py")));
				Assert.Equal("mine", File.ReadAllText(Path.Combine(target, "settings.dat")));
				Assert.True(File.Exists(Path.Combine(target, "cache", "c.bin")));
				Assert.Equal("2", packages.List().Single().Version);
			}
		}

		[Fact]
		public void Bad_files_and_unsafe_names_are_refused()
		{
			using (var temp = new TempFolder())
			{
				ScriptFolders folders = Folders(temp);
				var packages = new PluginPackages(folders);
				string junk = Path.Combine(temp.Path, "junk.crplugin");
				File.WriteAllText(junk, "not a zip");
				Assert.False(packages.Install(junk));
				Assert.False(packages.Install(Path.Combine(temp.Path, "missing.crplugin")));
				Assert.Null(PluginPackages.NameOf(junk));
				Assert.False(packages.Install(Package(temp, "evil.crplugin", "../../escape", "1")));
				Assert.False(packages.Install(Package(temp, "evil2.crplugin", "a/b", "1")));
				Assert.Empty(packages.List());
				Assert.False(Directory.Exists(Path.Combine(temp.Path, "escape")));
			}
		}

		[Fact]
		public void A_package_with_a_script_runs_its_command_on_books()
		{
			using (var temp = new TempFolder())
			{
				ScriptFolders folders = Folders(temp);
				Assert.True(new PluginPackages(folders).InstallNow(Package(temp, "a.crplugin", "Pack Plugin", "1")));
				using (MacHost host = MacHost.Open(new MacHostOptions { Paths = temp.Paths, ScriptFolders = folders, DatabaseBackgroundSaveSeconds = 0 }))
				{
					var output = new StringWriter();
					host.Scripts.AttachConsole(output);
					Command command = host.Scripts.GetCommands(PluginEngine.ScriptTypeBooks).Single(c => c.Key == "PackCommand");
					command.Invoke(new object[] { new[] { ScriptTestSupport.Book("A", "1"), ScriptTestSupport.Book("A", "2") } });
					Assert.Contains("pack 2", output.ToString());
				}
			}
		}
	}
}
