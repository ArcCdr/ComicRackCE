using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>
	/// The pass B CacheManager (mac/ComicRack.Engine.Core/Replacements/PassB/CacheManager.cs) is a copy of the original with one
	/// line changed. If the original changes, this fails: run mac/tools/replacement-sync.sh ComicRack.Engine/CacheManager.cs (it merges the change into the copy and records the new hash in mac/replacements.json).
	/// </summary>
	public class OriginalSourceDriftTests
	{
		// SHA-256 of ComicRack.Engine/CacheManager.cs without a byte order mark and with LF line endings, recorded in mac/replacements.json
		// (ReplacementManifestTests checks every entry; this test keeps the CacheManager message and the line-exact test below next to each other).
		private static string CacheManagerHash()
		{
			ManifestEntry entry = ReplacementManifest.Load(ReplacementManifest.ManifestPath(RepoRoot(), allowOverride: false))
				.Single(e => e.Original == "ComicRack.Engine/CacheManager.cs");
			return entry.Sha256;
		}

		private static string RepoRoot()
		{
			string dir = AppContext.BaseDirectory;
			while (dir != null && !File.Exists(Path.Combine(dir, "ComicRack.Engine", "CacheManager.cs")))
			{
				dir = Path.GetDirectoryName(dir);
			}
			Assert.True(dir != null, "The repository root was not found above " + AppContext.BaseDirectory);
			return dir;
		}

		private static string Normalize(string path)
		{
			return File.ReadAllText(path, Encoding.UTF8).TrimStart('﻿').Replace("\r\n", "\n");
		}

		private static string Hash(string text)
		{
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
		}

		[Fact]
		public void The_original_CacheManager_is_unchanged()
		{
			Assert.Equal(CacheManagerHash(), Hash(Normalize(Path.Combine(RepoRoot(), "ComicRack.Engine", "CacheManager.cs"))));
		}

		[Fact]
		public void The_replacement_differs_from_the_original_only_by_the_ImagePool_line_and_the_header()
		{
			string original = Normalize(Path.Combine(RepoRoot(), "ComicRack.Engine", "CacheManager.cs"));
			string replacement = Normalize(Path.Combine(RepoRoot(), "mac", "ComicRack.Engine.Core", "Replacements", "PassB", "CacheManager.cs"));
			int body = replacement.IndexOf("using System;", StringComparison.Ordinal);
			Assert.True(body > 0);
			string expected = original.Replace("ImagePool = new ImagePool(MemoryThumbnailCacheSize", "ImagePool = new MacImagePool(MemoryThumbnailCacheSize");
			Assert.NotEqual(original, expected);
			Assert.Equal(expected, replacement.Substring(body));
		}
	}
}
