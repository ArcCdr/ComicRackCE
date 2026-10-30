using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ComicRack.Mac.Host.Scripting;
using ComicRack.Mac.Host.Scripting.Scan;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>P4.17.5: file names that scripts join with a backslash exist as links, a file created under such a name is the real file, and install, update and uninstall leave no stray link.</summary>
	public class PathAliasTests
	{
		private static string Package(TempFolder temp, string file, string name, string ini, params (string name, string content)[] files)
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
				Add("package.ini", "Name=" + name + "\nVersion=1\nAuthor=Tester\nDescription=A test plugin\n" + ini);
				foreach ((string n, string c) in files)
				{
					Add(n, c);
				}
			}
			return path;
		}

		private static string[] Names(string folder) => Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();

		private static string Link(string path) => new FileInfo(path).LinkTarget;

		[Theory]
		[InlineData("p = d + \"\\App.Config\"", "\\App.Config")]
		[InlineData("p = d + \"\\\\home_32.png\"", "\\home_32.png")]
		[InlineData("p = d + r\"\\App.Config\"", "\\App.Config")]
		[InlineData("p = d + '\\Images\\y z.png'", "\\Images\\y z.png")]
		[InlineData("p = d + \"\\Images\\\\x.png\"", "\\Images\\x.png")]
		public void A_string_constant_with_a_backslash_and_a_file_name_is_found_as_the_program_reads_it(string line, string expected)
		{
			Assert.Equal(new[] { expected }, PathAliases.Literals(line));
		}

		[Theory]
		[InlineData("p = d + \"\\table.txt\"")]
		[InlineData("p = d + \"\\a.txt\"")]
		[InlineData("p = d + r\"\\\\server.txt\"")]
		[InlineData("# p = d + \"\\App.Config\"")]
		[InlineData("p = d + \"\\App\"")]
		[InlineData("p = d + \"/App.Config\"")]
		[InlineData("p = re.compile(r\"\\d+.\\w\")")]
		[InlineData("p = \"C:\\Temp\\x.txt\"")]
		public void An_escape_sequence_a_comment_a_regular_expression_and_a_drive_path_are_not_file_names(string line)
		{
			Assert.Empty(PathAliases.Literals(line));
		}

		[Fact]
		public void The_scanner_reports_a_name_with_a_link_as_info_and_the_others_as_warnings()
		{
			Assert.True(PathAliases.HasAlias("\\App.Config"));
			Assert.False(PathAliases.HasAlias("\\locommon.py"));
			Assert.False(PathAliases.HasAlias("\\list.xml"));
			Assert.False(PathAliases.HasAlias("\\\\x.png"));
			Assert.False(PathAliases.HasAlias("App.Config"));
		}

		[Fact]
		public void A_file_that_a_script_names_with_a_backslash_gets_a_link_beside_it_and_one_next_to_the_folder_and_a_missing_file_gets_none()
		{
			using (var temp = new TempFolder())
			{
				string root = Path.Combine(temp.Path, "scripts");
				string dir = Path.Combine(root, "Plug");
				Directory.CreateDirectory(Path.Combine(dir, "Images"));
				File.WriteAllText(Path.Combine(dir, "pic.png"), "a");
				File.WriteAllText(Path.Combine(dir, "Images", "b.png"), "b");
				File.WriteAllText(Path.Combine(dir, "other.dat"), "unused");
				File.WriteAllText(Path.Combine(dir, "x.py"), "import clr\na = d + \"\\pic.png\"\nb = d + '\\\\Images\\\\b.png'\nc = d + '\\missing.png'\nd = d + '\\\\lib.py'\n# e = d + '\\other.dat'\n");

				PathAliasReport report = PathAliases.Sync(root);
				Assert.Empty(report.Errors);
				Assert.Equal(4, report.Created);
				Assert.Equal("pic.png", Link(Path.Combine(dir, "\\pic.png")));
				Assert.Equal("Images/b.png", Link(Path.Combine(dir, "\\Images\\b.png")));
				Assert.Equal("Plug/pic.png", Link(Path.Combine(root, "Plug\\pic.png")));
				Assert.Equal("Plug/Images/b.png", Link(Path.Combine(root, "Plug\\Images\\b.png")));
				Assert.DoesNotContain("\\missing.png", Names(dir));
				Assert.DoesNotContain("\\lib.py", Names(dir));
				Assert.DoesNotContain("\\other.dat", Names(dir));
				Assert.Equal("a", File.ReadAllText(Path.Combine(dir, "\\pic.png")));
				Assert.Equal("b", File.ReadAllText(Path.Combine(root, "Plug\\Images\\b.png")));
				Assert.True(File.Exists(Path.Combine(dir, "\\pic.png")));

				// again: nothing changes
				Assert.False(PathAliases.Sync(root).Changed);
			}
		}

		[Fact]
		public void A_script_outside_a_folder_gets_no_link_next_to_the_script_folder_and_the_pending_folder_is_left_alone()
		{
			using (var temp = new TempFolder())
			{
				string root = Path.Combine(temp.Path, "scripts");
				Directory.CreateDirectory(Path.Combine(root, ".Pending", "P"));
				Directory.CreateDirectory(root);
				File.WriteAllText(Path.Combine(root, "loose.py"), "p = d + '\\pic.png'\n");
				File.WriteAllText(Path.Combine(root, "pic.png"), "a");
				File.WriteAllText(Path.Combine(root, ".Pending", "P", "x.py"), "p = d + '\\b.png'\n");
				File.WriteAllText(Path.Combine(root, ".Pending", "P", "b.png"), "b");
				Assert.Equal(1, PathAliases.Sync(root).Created);
				Assert.Equal("pic.png", Link(Path.Combine(root, "\\pic.png")));
				Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(root, ".Pending", "P")).Where(e => e.Contains('\\')));
				Assert.Empty(Directory.GetFileSystemEntries(temp.Path).Where(e => e.Contains('\\')));
			}
		}

		[Fact]
		public void A_link_stays_while_its_file_exists_and_goes_with_it_and_files_that_are_not_links_stay()
		{
			using (var temp = new TempFolder())
			{
				string root = Path.Combine(temp.Path, "scripts");
				string dir = Path.Combine(root, "Plug");
				Directory.CreateDirectory(dir);
				File.WriteAllText(Path.Combine(dir, "pic.png"), "a");
				File.WriteAllText(Path.Combine(dir, "x.py"), "p = d + '\\pic.png'\n");
				Assert.Equal(2, PathAliases.Sync(root).Created);

				File.WriteAllText(Path.Combine(dir, "x.py"), "p = 1\n");
				// a link of the user with a plain name, and a link with a backslash that points out of the folder
				File.CreateSymbolicLink(Path.Combine(dir, "mine"), "pic.png");
				File.CreateSymbolicLink(Path.Combine(dir, "\\theirs"), "/etc/hosts");
				// no script names the file any more: a plugin may build the name itself, so the links stay while the file exists
				PathAliasReport report = PathAliases.Sync(root);
				Assert.Equal(0, report.Removed);
				Assert.Equal(new[] { "\\pic.png", "\\theirs", "mine", "pic.png", "x.py" }, Names(dir));
				Assert.Equal(new[] { "Plug", "Plug\\pic.png" }, Names(root));
				Assert.Equal("pic.png", Link(Path.Combine(dir, "\\pic.png")));

				// the file goes: the link goes too (a link to nothing would make File.Exists true)
				File.WriteAllText(Path.Combine(dir, "x.py"), "p = d + '\\pic.png'\n");
				PathAliases.Sync(root);
				File.Delete(Path.Combine(dir, "pic.png"));
				PathAliases.Sync(root);
				Assert.DoesNotContain("\\pic.png", Names(dir));
				Assert.DoesNotContain("Plug\\pic.png", Names(root));
			}
		}

		[Fact]
		public void A_file_created_under_a_backslash_name_becomes_the_real_file_and_the_name_a_link_to_it()
		{
			using (var temp = new TempFolder())
			{
				string root = Path.Combine(temp.Path, "scripts");
				string dir = Path.Combine(root, "Plug");
				Directory.CreateDirectory(dir);
				File.WriteAllText(Path.Combine(dir, "x.py"), "a = d + '\\App.Config'\nb = d + '\\Sub\\\\data.bin'\n");
				Assert.Equal(0, PathAliases.Sync(root).Created);

				// the plugin creates the file under the Windows spelling (File.Create), the sibling spelling of a folder name, and a file in a new folder
				File.WriteAllText(Path.Combine(dir, "\\App.Config"), "created");
				File.WriteAllText(Path.Combine(root, "Plug\\other.txt"), "other");
				PathAliasReport report = PathAliases.Sync(root);
				Assert.Equal(2, report.Adopted);
				Assert.Empty(report.Errors);
				Assert.Equal("created", File.ReadAllText(Path.Combine(dir, "App.Config")));
				Assert.Equal("other", File.ReadAllText(Path.Combine(dir, "other.txt")));
				Assert.Equal("App.Config", Link(Path.Combine(dir, "\\App.Config")));
				Assert.Equal("Plug/App.Config", Link(Path.Combine(root, "Plug\\App.Config")));
				Assert.Equal("Plug/other.txt", Link(Path.Combine(root, "Plug\\other.txt")));
				Assert.Equal("other.txt", Link(Path.Combine(dir, "\\other.txt")));
				Assert.Equal("other", File.ReadAllText(Path.Combine(dir, "\\other.txt")));
				Assert.Equal("other", File.ReadAllText(Path.Combine(root, "Plug\\other.txt")));

				// a restart (Sync again) leaves the links: no script names \other.txt, the file exists
				Assert.False(PathAliases.Sync(root).Changed);
				Assert.Equal("Plug/other.txt", Link(Path.Combine(root, "Plug\\other.txt")));
				Assert.Equal("other.txt", Link(Path.Combine(dir, "\\other.txt")));

				// the plugin writes the settings by the other spelling: the same file
				File.WriteAllText(Path.Combine(dir, "App.Config"), "saved");
				Assert.Equal("saved", File.ReadAllText(Path.Combine(dir, "\\App.Config")));
				Assert.False(PathAliases.Sync(root).Changed);
			}
		}

		[Fact]
		public void When_both_spellings_hold_a_file_the_newer_one_stays()
		{
			using (var temp = new TempFolder())
			{
				string root = Path.Combine(temp.Path, "scripts");
				string dir = Path.Combine(root, "Plug");
				Directory.CreateDirectory(dir);
				File.WriteAllText(Path.Combine(dir, "x.py"), "a = d + '\\App.Config'\n");
				File.WriteAllText(Path.Combine(dir, "\\App.Config"), "old default");
				File.WriteAllText(Path.Combine(dir, "App.Config"), "saved by the user");
				File.SetLastWriteTimeUtc(Path.Combine(dir, "\\App.Config"), DateTime.UtcNow.AddHours(-1));
				PathAliases.Sync(root);
				Assert.Equal("saved by the user", File.ReadAllText(Path.Combine(dir, "\\App.Config")));
				Assert.Equal("App.Config", Link(Path.Combine(dir, "\\App.Config")));

				File.Delete(Path.Combine(dir, "\\App.Config"));
				File.WriteAllText(Path.Combine(dir, "\\App.Config"), "newer");
				File.SetLastWriteTimeUtc(Path.Combine(dir, "App.Config"), DateTime.UtcNow.AddHours(-1));
				PathAliases.Sync(root);
				Assert.Equal("newer", File.ReadAllText(Path.Combine(dir, "App.Config")));
			}
		}

		[Fact]
		public void A_script_that_builds_the_name_itself_finds_its_file_by_both_spellings_after_restarts_and_an_update()
		{
			using (var temp = new TempFolder())
			{
				string dir = Path.Combine(temp.Path, "hand-made");
				string plug = Path.Combine(dir, "Plug");
				Directory.CreateDirectory(plug);
				// the name is built at run time: no string constant of the script is "\\settings.dat"
				File.WriteAllText(Path.Combine(plug, "Probe.py"),
					"#@Name Probe\n#@Key Probe\n#@Hook Startup\nimport clr\nclr.AddReference('System')\nfrom System.IO import File\n" +
					"def Probe():\n" +
					"\tpath = __file__[:-len('Probe.py')]\n" +
					"\tname = 'settings' + '.dat'\n" +
					"\tif not File.Exists(path + '\\\\' + name):\n" +
					"\t\tFile.WriteAllText(path + '\\\\' + name, 'created')\n" +
					"\tprint 'config', File.ReadAllText(path + '\\\\' + name), File.Exists(path + name)\n");
				for (int start = 0; start < 3; start++)
				{
					using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
					{
						var console = new StringWriter();
						host.Scripts.AttachConsole(console);
						host.Scripts.RunStartup();
						// the first run creates it; every later start finds it under both spellings
						Assert.Contains("config created " + (start == 0 ? "False" : "True"), console.ToString());
						Assert.Equal("created", File.ReadAllText(Path.Combine(plug, start == 0 ? "\\settings.dat" : "settings.dat")));
					}
				}
				Assert.Equal("settings.dat", Link(Path.Combine(plug, "\\settings.dat")));
				Assert.Equal("Plug/settings.dat", Link(Path.Combine(dir, "Plug\\settings.dat")));
				Assert.Equal("created", File.ReadAllText(Path.Combine(plug, "\\settings.dat")));
				Assert.Equal("created", File.ReadAllText(Path.Combine(dir, "Plug\\settings.dat")));
			}
		}

		[Fact]
		public void A_file_made_under_a_name_that_no_script_names_survives_an_update_and_stays_readable_by_both_spellings()
		{
			using (var temp = new TempFolder())
			{
				var folders = new ScriptFolders(null, Path.Combine(temp.Path, "user-scripts"));
				var packages = new PluginPackages(folders);
				string target = Path.Combine(folders.User, "Pack Plugin");
				const string script = "#@Name Pack Command\n#@Key PackCommand\n#@Hook Books\ndef PackCommand(books):\n\tpass\n";

				Assert.True(packages.InstallNow(Package(temp, "a.crplugin", "Pack Plugin", "KeepFiles=cache.dat\n", ("Pack.py", script))));
				// the plugin creates its cache by the Windows spelling and the user's folder is synchronised at the next start
				File.WriteAllText(Path.Combine(target, "\\cache.dat"), "cache v1");
				PathAliases.Sync(folders.User);
				Assert.Equal("cache.dat", Link(Path.Combine(target, "\\cache.dat")));
				Assert.Equal("Pack Plugin/cache.dat", Link(Path.Combine(folders.User, "Pack Plugin\\cache.dat")));

				// a package update (new version ships another file) keeps the file and both odd names
				Assert.True(packages.InstallNow(Package(temp, "b.crplugin", "Pack Plugin", "KeepFiles=cache.dat\n", ("Pack.py", script + "\n"), ("New.png", "new"))));
				Assert.Equal("cache v1", File.ReadAllText(Path.Combine(target, "cache.dat")));
				Assert.Equal("cache v1", File.ReadAllText(Path.Combine(target, "\\cache.dat")));
				Assert.Equal("cache v1", File.ReadAllText(Path.Combine(folders.User, "Pack Plugin\\cache.dat")));
				Assert.Equal(new[] { "Pack Plugin", "Pack Plugin\\cache.dat" }, Names(folders.User).Where(n => n != ".Pending").OrderBy(n => n, StringComparer.Ordinal));

				// and a restart after the update
				Assert.False(PathAliases.Sync(folders.User).Changed);
				Assert.Equal("cache v1", File.ReadAllText(Path.Combine(target, "\\cache.dat")));

				// a file the plugin made and the update does not keep goes with its links; a name the script engine loads by itself gets no link
				File.WriteAllText(Path.Combine(target, "\\scratch.tmp"), "scratch");
				File.WriteAllText(Path.Combine(target, "\\extra.xml"), "<x/>");
				PathAliases.Sync(folders.User);
				Assert.Equal("scratch.tmp", Link(Path.Combine(target, "\\scratch.tmp")));
				Assert.False(File.Exists(Path.Combine(target, "\\extra.xml")));
				Assert.True(packages.InstallNow(Package(temp, "c.crplugin", "Pack Plugin", "KeepFiles=cache.dat\n", ("Pack.py", script))));
				Assert.False(File.Exists(Path.Combine(target, "scratch.tmp")));
				Assert.False(File.Exists(Path.Combine(target, "\\scratch.tmp")));
				Assert.False(File.Exists(Path.Combine(folders.User, "Pack Plugin\\scratch.tmp")));
				Assert.Equal("cache v1", File.ReadAllText(Path.Combine(folders.User, "Pack Plugin\\cache.dat")));

				// a file removed under its real name takes its odd names with it at the next start (a link to a missing file would make File.Exists true)
				File.Delete(Path.Combine(target, "cache.dat"));
				PathAliases.Sync(folders.User);
				Assert.False(File.Exists(Path.Combine(target, "\\cache.dat")));
				Assert.False(File.Exists(Path.Combine(folders.User, "Pack Plugin\\cache.dat")));
				Assert.Null(Link(Path.Combine(target, "\\cache.dat")));
				Assert.Equal(new[] { "Pack Plugin" }, Names(folders.User).Where(n => n != ".Pending"));

				Assert.True(packages.Uninstall("Pack Plugin"));
				packages.Commit();
				Assert.Equal(new string[0], Names(folders.User).Where(n => n != ".Pending"));
			}
		}

		private const string Script = "#@Name Pack Command\n#@Key PackCommand\n#@Hook Books\ndef PackCommand(books):\n\tpath = __file__[:-len('Pack.py')]\n\tprint 'x', path + \"\\Data.txt\"\n";

		[Fact]
		public void Install_update_and_uninstall_leave_no_stray_link_and_an_update_keeps_the_settings_file()
		{
			using (var temp = new TempFolder())
			{
				var folders = new ScriptFolders(null, Path.Combine(temp.Path, "user-scripts"));
				var packages = new PluginPackages(folders);
				string target = Path.Combine(folders.User, "Pack Plugin");

				Assert.True(packages.InstallNow(Package(temp, "a.crplugin", "Pack Plugin", "KeepFiles=App.Config\n", ("Pack.py", Script + "cfg = path + '\\App.Config'\n"), ("Data.txt", "data"), ("Old.png", "old"))));
				Assert.Equal("Data.txt", Link(Path.Combine(target, "\\Data.txt")));
				Assert.Equal("Pack Plugin/Data.txt", Link(Path.Combine(folders.User, "Pack Plugin\\Data.txt")));
				Assert.False(File.Exists(Path.Combine(target, "\\App.Config")), "the settings file does not exist yet: a link to it would make File.Exists true");

				// the plugin creates its settings file under the Windows spelling and saves them by the plain one
				File.WriteAllText(Path.Combine(target, "\\App.Config"), "created");
				File.WriteAllText(Path.Combine(target, "App.Config"), "saved");
				File.SetLastWriteTimeUtc(Path.Combine(target, "\\App.Config"), DateTime.UtcNow.AddHours(-1));

				// version 2 no longer ships Data.txt and has another name
				Assert.True(packages.InstallNow(Package(temp, "b.crplugin", "Pack Plugin", "KeepFiles=App.Config\n", ("Pack.py", Script + "cfg = path + '\\App.Config'\nimg = path + '\\New.png'\n"), ("New.png", "new"))));
				Assert.Equal(new[] { "App.Config", "New.png", "Pack.py", "\\App.Config", "\\New.png", "package.ini" }, Names(target));
				Assert.Equal("saved", File.ReadAllText(Path.Combine(target, "\\App.Config")));
				Assert.Equal(new[] { "Pack Plugin", "Pack Plugin\\App.Config", "Pack Plugin\\New.png" }, Names(folders.User).Where(n => n != ".Pending").OrderBy(n => n, StringComparer.Ordinal));

				// a plugin that only created the file under the Windows spelling keeps it through an update as well
				File.Delete(Path.Combine(target, "App.Config"));
				File.Delete(Path.Combine(target, "\\App.Config"));
				File.WriteAllText(Path.Combine(target, "\\App.Config"), "only the odd name");
				Assert.True(packages.InstallNow(Package(temp, "c.crplugin", "Pack Plugin", "KeepFiles=App.Config\n", ("Pack.py", Script + "cfg = path + '\\App.Config'\n"))));
				Assert.Equal("only the odd name", File.ReadAllText(Path.Combine(target, "App.Config")));
				Assert.Equal("App.Config", Link(Path.Combine(target, "\\App.Config")));
				Assert.Equal(new[] { "Pack Plugin", "Pack Plugin\\App.Config" }, Names(folders.User).Where(n => n != ".Pending").OrderBy(n => n, StringComparer.Ordinal));

				// uninstall: the folder and every link to it go
				Assert.True(packages.Uninstall("Pack Plugin"));
				packages.Commit();
				Assert.Equal(new string[0], Names(folders.User).Where(n => n != ".Pending"));
			}
		}

		[Fact]
		public void The_script_host_makes_the_links_of_a_folder_that_was_not_installed_as_a_package_and_a_script_finds_its_files()
		{
			using (var temp = new TempFolder())
			{
				string dir = Path.Combine(temp.Path, "hand-made");
				string plug = Path.Combine(dir, "Plug");
				Directory.CreateDirectory(plug);
				File.WriteAllText(Path.Combine(plug, "Data.txt"), "data");
				File.WriteAllText(Path.Combine(plug, "Probe.py"),
					"#@Name Probe\n#@Key Probe\n#@Hook Startup\nimport clr\nclr.AddReference('System')\nfrom System.IO import File, FileInfo\n" +
					"def Probe():\n" +
					"\tpath = __file__[:-len('Probe.py')]\n" +
					"\tprint 'inside', File.Exists(path + \"\\Data.txt\"), File.ReadAllText(path + \"\\Data.txt\")\n" +
					"\tprint 'beside', File.Exists(FileInfo(__file__).DirectoryName + \"\\Data.txt\")\n" +
					"\tif not File.Exists(path + \"\\App.Config\"):\n" +
					"\t\tFile.WriteAllText(path + \"\\App.Config\", 'created')\n" +
					"\tprint 'config', File.ReadAllText(path + \"\\App.Config\")\n");
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
				{
					var console = new StringWriter();
					host.Scripts.AttachConsole(console);
					host.Scripts.RunStartup();
					string output = console.ToString();
					Assert.Contains("inside True data", output);
					Assert.Contains("beside True", output);
					Assert.Contains("config created", output);
					Assert.True(File.Exists(Path.Combine(plug, "\\App.Config")));
				}
				// the next start moves the file created by the script to its real name
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
				{
					Assert.Equal("created", File.ReadAllText(Path.Combine(plug, "App.Config")));
					Assert.Equal("App.Config", Link(Path.Combine(plug, "\\App.Config")));
				}
			}
		}

		[Fact]
		public void The_names_exit_and_quit_exist_as_the_site_module_defines_them()
		{
			using (var temp = new TempFolder())
			{
				string dir = ScriptTestSupport.WriteScripts(temp, "scripts", ("exit.py",
					"#@Name Exit\n#@Hook Startup\ndef Exit():\n\ttry:\n\t\traise ValueError('x')\n\texcept:\n\t\texit\n\tprint 'named', repr(quit)\n\ttry:\n\t\tquit(3)\n\texcept SystemExit as e:\n\t\tprint 'raised', e.code\n"));
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
				{
					var console = new StringWriter();
					host.Scripts.AttachConsole(console);
					host.Scripts.RunStartup();
					Assert.Contains("named Use quit()", console.ToString());
					Assert.Contains("raised 3", console.ToString());
				}
			}
		}
	}
}
