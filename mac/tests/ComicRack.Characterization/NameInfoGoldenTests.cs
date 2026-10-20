using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using cYo.Projects.ComicRack.Engine;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Characterization
{
	/// <summary>
	/// <see cref="ComicNameInfo"/>: what the Core reads from a file name (series, number, volume, count, year, format, cover count), with the new and the
	/// legacy parser, for 200 names in <c>Golden/FileNames.txt</c> (P0.7a).
	/// </summary>
	public class NameInfoGoldenTests
	{
		private const string Names = "FileNames.txt";
		private const string Golden = "NameInfo.json";

		private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

		private readonly ITestOutputHelper output;

		public NameInfoGoldenTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		private static string[] ReadNames()
		{
			return File.ReadAllLines(Goldens.PathOf(Names), Encoding.UTF8).Where(l => l.Length > 0).ToArray();
		}

		private static object Describe(ComicNameInfo i) => new
		{
			series = i.Series,
			title = i.Title,
			number = i.Number,
			format = i.Format,
			volume = i.Volume,
			count = i.Count,
			year = i.Year,
			coverCount = i.CoverCount,
		};

		/// <summary>Runs <paramref name="action"/> with the given culture as the current one and restores the old one.</summary>
		private static T WithCulture<T>(CultureInfo culture, Func<T> action)
		{
			CultureInfo old = Thread.CurrentThread.CurrentCulture;
			Thread.CurrentThread.CurrentCulture = culture;
			try
			{
				return action();
			}
			finally
			{
				Thread.CurrentThread.CurrentCulture = old;
			}
		}

		private static string Render(string[] names)
		{
			var sb = new StringBuilder("[\n");
			for (int i = 0; i < names.Length; i++)
			{
				var entry = new
				{
					path = names[i],
					newParser = Describe(ComicNameInfo.FromFilePath(names[i], legacy: false)),
					legacyParser = Describe(ComicNameInfo.FromFilePath(names[i], legacy: true)),
				};
				sb.Append("  ").Append(JsonSerializer.Serialize(entry, Json)).Append(i < names.Length - 1 ? ",\n" : "\n");
			}
			return sb.Append("]\n").ToString();
		}

		[Fact]
		public void The_name_list_has_200_distinct_names()
		{
			string[] names = ReadNames();
			Assert.Equal(200, names.Length);
			Assert.Equal(200, names.Distinct(StringComparer.Ordinal).Count());
		}

		[Fact]
		public void Both_parsers_give_the_golden_result_for_every_name()
		{
			string[] names = ReadNames();
			string actual = WithCulture(CultureInfo.InvariantCulture, () => Render(names));
			Goldens.Check(Golden, actual, output.WriteLine);
		}

		[Fact]
		public void The_new_parser_does_not_depend_on_the_culture()
		{
			string[] names = ReadNames();
			string Run(string culture) => WithCulture(CultureInfo.GetCultureInfo(culture), () => string.Join("\n", names.Select(n => JsonSerializer.Serialize(Describe(ComicNameInfo.FromFilePath(n, legacy: false)), Json))));
			string invariant = Run("");
			Assert.Equal(invariant, Run("fr-FR"));
			Assert.Equal(invariant, Run("de-DE"));
			Assert.Equal(invariant, Run("ja-JP"));
		}

		[Fact]
		public void The_default_parser_follows_the_engine_setting()
		{
			// A name the two parsers read differently (the new one: series Batman, number 12, year 1940; the legacy one: series "Batman 012",
			// number 1940), so that ignoring the setting, or swapping the two parsers, cannot pass.
			string path = "/comics/Batman/Batman.012.1940.cbz";
			string neu = Serialize(ComicNameInfo.FromFilePath(path, legacy: false));
			string old = Serialize(ComicNameInfo.FromFilePath(path, legacy: true));
			Assert.NotEqual(neu, old);
			bool legacy = EngineConfiguration.Default.LegacyFilenameParser;
			try
			{
				EngineConfiguration.Default.LegacyFilenameParser = false;
				Assert.Equal(neu, Serialize(ComicNameInfo.FromFilePath(path)));
				EngineConfiguration.Default.LegacyFilenameParser = true;
				Assert.Equal(old, Serialize(ComicNameInfo.FromFilePath(path)));
			}
			finally
			{
				EngineConfiguration.Default.LegacyFilenameParser = legacy;
			}
		}

		private static string Serialize(ComicNameInfo info) => WithCulture(CultureInfo.InvariantCulture, () => JsonSerializer.Serialize(Describe(info), Json));

		[Theory]
		[InlineData("/comics/Batman/Batman 012 (1940).cbz", "Batman", "12", 1940, -1, -1)]
		[InlineData("/comics/Batman/Batman v2 #012 (2011).cbz", "Batman", "12", 2011, 2, -1)]
		[InlineData("/comics/Batman/Batman 12 (of 52) (2011) (c2c).cbz", "Batman", "12", 2011, -1, 52)]
		[InlineData("/comics/Batman/Batman_012_(1940).cbz", "Batman", "12", 1940, -1, -1)]
		[InlineData("/comics/Misc/Series Name/001.cbz", "Series Name", "1", -1, -1, -1)]
		public void Typical_names_are_read_as_a_person_would_read_them(string path, string series, string number, int year, int volume, int count)
		{
			ComicNameInfo info = WithCulture(CultureInfo.InvariantCulture, () => ComicNameInfo.FromFilePath(path, legacy: false));
			Assert.Equal(series, info.Series);
			Assert.Equal(number, info.Number);
			Assert.Equal(year, info.Year);
			Assert.Equal(volume, info.Volume);
			Assert.Equal(count, info.Count);
		}
	}
}
