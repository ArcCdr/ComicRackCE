using System;
using ComicRack.Mac.Host.Library;
using Xunit;

namespace ComicRack.Mac.Tests.Host
{
	public class PathRemapTests
	{
		[Theory]
		[InlineData(@"S:\media\BD\", "/m/BD/", @"S:\media\BD\Series\a.cbz", "/m/BD/Series/a.cbz")]
		[InlineData(@"S:\media\BD", "/m/BD", @"S:\media\BD\Series\a.cbz", "/m/BD/Series/a.cbz")]
		[InlineData(@"S:\media\BD\", "/m/BD/", @"s:\MEDIA\bd\Series\a.cbz", "/m/BD/Series/a.cbz")]
		[InlineData(@"S:\media\BD", "/m/BD", @"S:\media\BD", "/m/BD")]
		[InlineData(@"S:\media\BD\", "/m/BD/", @"S:\media\BD\a\b\c\d.cbz", "/m/BD/a/b/c/d.cbz")]
		[InlineData(@"S:\media\BD\", "/", @"S:\media\BD\a.cbz", "/a.cbz")]
		[InlineData(@"S:\media\BD\", "/m/B D (x)/", @"S:\media\BD\sé ries\ü.cbz", "/m/B D (x)/sé ries/ü.cbz")]
		public void Maps_a_prefix(string from, string to, string path, string expected)
		{
			Assert.Equal(expected, new PathRemapRule(from, to).Map(path));
		}

		[Theory]
		[InlineData(@"S:\media\BD\", @"S:\media\BDX\a.cbz")]
		[InlineData(@"S:\media\BD\", @"S:\media\B")]
		[InlineData(@"S:\media\BD\", @"D:\media\BD\a.cbz")]
		[InlineData(@"S:\media\BD\", "")]
		public void Does_not_map_a_path_outside_the_prefix(string from, string path)
		{
			Assert.Null(new PathRemapRule(from, "/m").Map(path));
		}

		[Fact]
		public void The_first_matching_rule_wins()
		{
			var rules = new[] { new PathRemapRule(@"S:\media\BD\", "/a/"), new PathRemapRule(@"S:\media\", "/b/"), new PathRemapRule(@"S:\other\", "/c/") };
			Assert.Equal("/a/x.cbz", PathRemapRule.MapFirst(rules, @"S:\media\BD\x.cbz"));
			Assert.Equal("/b/Comics/x.cbz", PathRemapRule.MapFirst(rules, @"S:\media\Comics\x.cbz"));
			Assert.Equal("/c/x.cbz", PathRemapRule.MapFirst(rules, @"S:\other\x.cbz"));
			Assert.Null(PathRemapRule.MapFirst(rules, @"D:\x.cbz"));
		}

		[Fact]
		public void Parses_lines_and_skips_comments_and_blank_lines()
		{
			var rules = PathRemapRule.ParseLines("# my library\r\n\r\n S:\\media\\BD\\=/m/BD/ \r\nT:\\x=/y\n");
			Assert.Equal(2, rules.Count);
			Assert.Equal(@"S:\media\BD\", rules[0].From);
			Assert.Equal("/m/BD/", rules[0].To);
			Assert.Equal("/y", rules[1].To);
			Assert.Empty(PathRemapRule.ParseLines(null));
		}

		[Theory]
		[InlineData("no equals sign")]
		[InlineData("=/only/target")]
		[InlineData(@"S:\only\source=")]
		[InlineData("   =   ")]
		public void Rejects_a_malformed_mapping_with_a_message(string line)
		{
			var ex = Assert.Throws<FormatException>(() => PathRemapRule.ParseLines(line));
			Assert.Contains("mapping", ex.Message);
		}
	}
}
