using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ComicRack.Mac.Host.Scripting;
using cYo.Projects.ComicRack.Plugins;
using Microsoft.Scripting.Hosting;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Acceptance
{
	/// <summary>
	/// P4.17.5: the unmodified files of the two acceptance plugins find what they join with a backslash. Acceptance tests are the only tests that may name a plugin
	/// (PluginNamesRuleTests); they skip themselves when the packages are not in mac/fixtures/plugins.
	/// </summary>
	public class PathSeparatorAcceptanceTests
	{
		private static ScriptScope Module(PythonCommand command)
		{
			command.PreCompile(handleException: false);
			return (ScriptScope)typeof(PythonCommand).GetField("scope", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(command);
		}

		private static PythonCommand First(MacHost host, string script)
		{
			return host.Scripts.LoadCommands(script).OfType<PythonCommand>().First();
		}

		private static MacHost Open(TempFolder temp, ScriptFolders folders)
		{
			return MacHost.Open(new MacHostOptions { Paths = temp.Paths, ScriptFolders = folders, Scripting = true, DatabaseBackgroundSaveSeconds = 0 });
		}

		[PluginFact(AcceptancePlugins.Bedetheque)]
		public void The_translations_are_found_and_the_settings_file_is_created_and_read_again()
		{
			using (var temp = new TempFolder())
			{
				var folders = new ScriptFolders(null, Path.Combine(temp.Path, "Scripts"));
				var packages = new PluginPackages(folders);
				Assert.True(packages.InstallNow(AcceptancePlugins.Path(AcceptancePlugins.Bedetheque)));
				string package = Path.Combine(folders.User, "Bedetheque Scraper 2");
				string script = Path.Combine(package, "BedethequeScraper2.py");

				// the links beside the files the script names
				Assert.Equal("BDTranslations.Config", new FileInfo(Path.Combine(package, "\\BDTranslations.Config")).LinkTarget);
				Assert.Equal("Bedetheque Scraper 2/BDTranslations.Config", new FileInfo(Path.Combine(folders.User, "Bedetheque Scraper 2\\BDTranslations.Config")).LinkTarget);
				Assert.False(File.Exists(Path.Combine(package, "\\App.Config")));

				using (MacHost host = Open(temp, folders))
				{
					var console = new StringWriter();
					host.Scripts.AttachConsole(console);
					ScriptScope module = Module(First(host, script));

					// Translate() ends the script with sys.exit(0) when BDTranslations.Config is not found
					module.GetVariable<Action>("Translate")();
					IList words = (IList)module.GetVariable("aWord");
					Assert.True(words.Count > 100, "translations: " + words.Count + " " + console);

					// the settings file does not exist: the script creates it (under the odd name) and reads it
					Assert.True((bool)module.GetVariable<Func<object>>("LoadSetting")() is bool, console.ToString());
					Assert.True(File.Exists(Path.Combine(package, "\\App.Config")) || File.Exists(Path.Combine(package, "App.Config")));
					Assert.Equal("FR", (string)module.GetVariable("LANGENFR"));
				}

				// the settings are saved under the plain name; the next start finds both spellings to be one file
				File.WriteAllText(Path.Combine(package, "App.Config"), "<?xml version=\"1.0\"?><configuration><LANGENFR>EN</LANGENFR></configuration>");
				using (MacHost host = Open(temp, folders))
				{
					Assert.Equal("App.Config", new FileInfo(Path.Combine(package, "\\App.Config")).LinkTarget);
					ScriptScope module = Module(First(host, script));
					module.GetVariable<Func<object>>("LoadSetting")();
					Assert.Equal("EN", (string)module.GetVariable("LANGENFR"));
				}

				// an update keeps the settings file; uninstalling leaves no link
				Assert.True(packages.InstallNow(AcceptancePlugins.Path(AcceptancePlugins.Bedetheque)));
				Assert.Contains("<LANGENFR>EN</LANGENFR>", File.ReadAllText(Path.Combine(package, "\\App.Config")));
				Assert.True(packages.Uninstall("Bedetheque Scraper 2"));
				packages.Commit();
				Assert.Empty(Directory.GetFileSystemEntries(folders.User).Where(e => !Path.GetFileName(e).StartsWith('.')));
			}
		}

		[PluginFact(AcceptancePlugins.Organizer)]
		public void The_twelve_images_of_the_configuration_dialog_load_from_the_names_with_a_backslash()
		{
			using (var temp = new TempFolder())
			{
				var folders = new ScriptFolders(null, Path.Combine(temp.Path, "Scripts"));
				var packages = new PluginPackages(folders);
				Assert.True(packages.InstallNow(AcceptancePlugins.Path(AcceptancePlugins.Organizer)));
				string package = Path.Combine(folders.User, "Library Organizer");
				string[] images = Regex.Matches(File.ReadAllText(Path.Combine(package, "configureform.py")), @"Bitmap\.FromFile\(SCRIPTDIRECTORY \+ ""\\\\([^""]+)""\)").Select(m => m.Groups[1].Value).ToArray();
				Assert.Equal(12, images.Length);

				using (MacHost host = Open(temp, folders))
				{
					string probe = Path.Combine(temp.Path, "probe.py");
					File.WriteAllText(probe,
						"#@Name Probe\n#@Hook Books\nimport clr\nclr.AddReference('System.Drawing')\nfrom System.Drawing import Bitmap\nfrom System.IO import FileInfo\n" +
						"def Probe(books):\n" +
						"\tdirectory = FileInfo(r'" + Path.Combine(package, "locommon.py") + "').DirectoryName\n" +
						string.Join("", images.Select(i => "\tb = Bitmap.FromFile(directory + \"\\\\" + i + "\")\n\tprint '" + i + "', b.Width, b.Height\n")));
					var output = new StringWriter();
					var error = new StringWriter();
					Assert.Equal(0, ScriptRunner.Run(host, probe, "Books", null, output, error));
					foreach (string image in images)
					{
						Assert.Matches(new Regex("^" + Regex.Escape(image) + " [1-9][0-9]* [1-9][0-9]*$", RegexOptions.Multiline), output.ToString());
					}
				}
			}
		}
	}
}
