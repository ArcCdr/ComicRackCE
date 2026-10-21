using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using cYo.Common.IO;
using cYo.Common.Localize;
using Xunit;

namespace ComicRack.Mac.Tests
{
	/// <summary>
	/// P2.6: the language bootstrap and the payload. The texts are the original's (ComicRack/Output/Languages, copied next to the test assembly like
	/// next to the executable). The tests that go through the Core's static <see cref="TR"/> restore its state; test classes of this assembly do not run in parallel.
	/// </summary>
	public sealed class LocalizationTests : IDisposable
	{
		private static readonly string Languages = Path.Combine(AppContext.BaseDirectory, "Languages");

		private readonly IVirtualFolder folderBefore = TR.ResourceFolder;
		private readonly CultureInfo uiBefore = CultureInfo.DefaultThreadCurrentUICulture;
		private readonly CultureInfo threadBefore = Thread.CurrentThread.CurrentUICulture;

		public void Dispose()
		{
			TR.LanguageResources.Clear();
			TR.DefaultCulture = null;
			TR.ResourceFolder = folderBefore;
			CultureInfo.DefaultThreadCurrentUICulture = uiBefore;
			Thread.CurrentThread.CurrentUICulture = threadBefore;
		}

		[Fact]
		public void The_payload_is_next_to_the_executable()
		{
			string[] cultures = Localization.AvailableCultures(Languages).ToArray();
			Assert.Equal(19, cultures.Length);
			Assert.Contains("de", cultures);
			Assert.Contains("zh-Hans", cultures);
			Assert.Equal(72, Directory.GetFiles(Path.Combine(Languages, "de")).Length);
			Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "DefaultLists.txt")));
			Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Scripts", "Sample.py")));
			Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Help", "ComicRack Online Manual.ini")));
			Assert.True(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Resources", "Icons"), "*.png", SearchOption.AllDirectories).Length > 700);
			Assert.True(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Resources", "Textures"), "*", SearchOption.AllDirectories).Length == 18);
		}

		[Fact]
		public void The_payload_has_no_windows_binaries_and_no_djvu_introduction()
		{
			string root = AppContext.BaseDirectory;
			Assert.Empty(Directory.GetFiles(Path.Combine(root, "Help"), "*.djvu*"));
			Assert.False(File.Exists(Path.Combine(root, "Resources", "c44.exe")));
			Assert.False(Directory.Exists(Path.Combine(root, "Resources", "x64")));
			Assert.False(Directory.Exists(Path.Combine(root, "Resources", "x86")));
		}

		[Fact]
		public void Every_translation_loads_through_the_Core()
		{
			foreach (string culture in Localization.AvailableCultures(Languages))
			{
				var dictionary = new TRDictionary { ResourceFolder = new VirtualFileFolder(Languages), DefaultCulture = new CultureInfo(culture) };
				string[] names = Directory.GetFiles(Path.Combine(Languages, culture), "*.xml")
					.Select(Path.GetFileNameWithoutExtension)
					.Where(n => n != "LanguageInfo")
					.ToArray();
				Assert.NotEmpty(names);
				Assert.True(names.Any(n => !dictionary.Load(n).IsEmpty), culture);
			}
		}

		[Fact]
		public void Croatian_files_in_windows_1250_are_read()
		{
			Localization.Apply("hr", Languages, new string[0]);
			Assert.Equal("\u010ci\u0161\u010denje svega", TR.Load("Default")["ClearAll", "Clear all"]);
		}

		[Fact]
		public void A_German_text_resolves_and_a_missing_key_falls_back_to_the_English_text()
		{
			var dictionary = new TRDictionary { ResourceFolder = new VirtualFileFolder(Languages), DefaultCulture = new CultureInfo("de") };
			TR texts = dictionary.Load("Default");
			Assert.Equal("Löschen", texts["Clear", "Clear"]);
			Assert.Equal("Alles löschen", texts["ClearAll", "Clear all"]);
			Assert.Equal("No such text", texts["NoSuchKeyInTheFile", "No such text"]);
		}

		[Fact]
		public void English_has_no_folder_and_returns_the_English_text()
		{
			var dictionary = new TRDictionary { ResourceFolder = new VirtualFileFolder(Languages), DefaultCulture = new CultureInfo("en") };
			Assert.Equal("Clear", dictionary.Load("Default")["Clear", "Clear"]);
		}

		[Fact]
		public void Apply_gives_the_Core_the_chosen_language()
		{
			string culture = Localization.Apply("de", Languages, new[] { "en-US" });
			Assert.Equal("de", culture);
			Assert.Equal("de", TR.DefaultCulture.Name);
			Assert.Equal("de", CultureInfo.DefaultThreadCurrentUICulture.Name);
			Assert.Equal("Löschen", TR.Load("Default")["Clear", "Clear"]);
			// The Core's own enum translation reads the same files.
			Assert.Equal("Alles löschen", TR.Load("Default")["ClearAll", "Clear all"]);

			culture = Localization.Apply("fr", Languages, new[] { "en-US" });
			Assert.Equal("fr", culture);
			Assert.NotEqual("Löschen", TR.Load("Default")["Clear", "Clear"]);
			Assert.NotEqual("Clear", TR.Load("Default")["Clear", "Clear"]);
		}

		[Fact]
		public void Apply_with_English_wins_over_the_system_culture()
		{
			Thread.CurrentThread.CurrentUICulture = new CultureInfo("fr-FR");
			string culture = Localization.Apply("en", Languages, new[] { "fr-FR" });
			Assert.Equal("en", culture);
			Assert.Equal("Clear", TR.Load("Default")["Clear", "Clear"]);
		}

		[Fact]
		public void Apply_without_a_translation_folder_is_English()
		{
			string empty = Path.Combine(Path.GetTempPath(), "crmac-no-languages-" + Guid.NewGuid().ToString("N"));
			string culture = Localization.Apply(null, empty, new[] { "de-DE" });
			Assert.Equal("en", culture);
			Assert.Equal("Clear", TR.Load("Default")["Clear", "Clear"]);
		}

		[Fact]
		public void Apply_uses_the_macOS_list_when_none_is_given()
		{
			string culture = Localization.Apply(null, Languages);
			Assert.True(culture == "en" || Localization.AvailableCultures(Languages).Contains(culture), culture);
		}

		[Fact]
		public void The_macOS_preferred_languages_are_read()
		{
			var list = Localization.MacPreferredLanguages();
			Assert.NotEmpty(list);
			Assert.All(list, tag => Assert.False(string.IsNullOrWhiteSpace(tag)));
		}

		private static readonly string[] Folders = { "cs-CZ", "de", "el-GR", "es", "fi", "fr", "hr", "hu", "it", "ja", "nl-BE", "pl", "pt-BR", "ru", "sk-SK", "tr", "zh", "zh-CN", "zh-Hans" };

		[Theory]
		[InlineData("de", "de")]
		[InlineData("de-DE", "de")]
		[InlineData("DE_at", "de")]
		[InlineData("fr-BE", "fr")]
		[InlineData("nl-BE", "nl-BE")]
		[InlineData("nl-NL", "nl-BE")]
		[InlineData("pt-PT", "pt-BR")]
		[InlineData("pt", "pt-BR")]
		[InlineData("cs", "cs-CZ")]
		[InlineData("zh-Hans-CN", "zh-CN")]
		[InlineData("zh-Hans", "zh-CN")]
		[InlineData("zh_hans_SG", "zh-CN")]
		[InlineData("zh-CN", "zh-CN")]
		[InlineData("zh-Hant-TW", "zh")]
		[InlineData("ja-JP", "ja")]
		[InlineData("en", "en")]
		[InlineData("en-BE", "en")]
		[InlineData("en_US", "en")]
		[InlineData("sv-SE", null)]
		[InlineData("", null)]
		[InlineData(null, null)]
		public void A_language_tag_selects_a_folder(string tag, string expected)
		{
			Assert.Equal(expected, Localization.Match(tag, Folders));
		}

		[Fact]
		public void Simplified_Chinese_is_the_complete_zh_CN_translation_whatever_the_tag_and_stays_zh_Hans_where_there_is_no_zh_CN()
		{
			Assert.Equal("zh-CN", Localization.Choose("zh-Hans", new[] { "fr" }, Folders));
			Assert.Equal("zh-CN", Localization.Choose(null, new[] { "zh-Hans-CN", "fr" }, Folders));
			// the traditional script is not it
			Assert.Equal("zh", Localization.Match("zh-Hant-HK", Folders));
			// a language folder list without zh-CN keeps the old way
			Assert.Equal("zh-Hans", Localization.Match("zh-Hans-CN", new[] { "de", "zh", "zh-Hans" }));
		}

		[Fact]
		public void The_setting_wins_then_the_first_language_the_app_can_show_then_English()
		{
			Assert.Equal("de", Localization.Choose("de", new[] { "fr-FR" }, Folders));
			Assert.Equal("en", Localization.Choose("en", new[] { "fr-FR" }, Folders));
			// A setting without a translation does not hide the system's list.
			Assert.Equal("fr", Localization.Choose("xx", new[] { "sv-SE", "fr-FR" }, Folders));
			Assert.Equal("fr", Localization.Choose(null, new[] { "sv-SE", "fr-FR", "de" }, Folders));
			// English is always available: an English first choice is not skipped for the next translation (the owner's list is en-BE, fr-BE, nl-BE).
			Assert.Equal("en", Localization.Choose(null, new[] { "en-BE", "fr-BE", "nl-BE" }, Folders));
			Assert.Equal("en", Localization.Choose(null, new[] { "sv-SE", "da" }, Folders));
			Assert.Equal("en", Localization.Choose(null, Array.Empty<string>(), Folders));
			Assert.Equal("en", Localization.Choose("de", new[] { "fr" }, Array.Empty<string>()));
		}

		[Fact]
		public void The_language_option_and_variable_are_parsed()
		{
			Assert.Equal("de", AppOptions.Parse(new[] { "--language", "de" }).Language);
			Assert.Equal("fr", AppOptions.Parse(new[] { "--language=fr" }).Language);
		}
	}
}
