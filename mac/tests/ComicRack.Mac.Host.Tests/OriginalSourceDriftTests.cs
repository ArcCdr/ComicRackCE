using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>
	/// The pass B CacheManager (mac/ComicRack.Engine.Core/Replacements/PassB/CacheManager.cs) is a copy of the original with one
	/// line changed. If the original changes, this fails: port the change to the copy, then record the new hash here.
	/// </summary>
	public class OriginalSourceDriftTests
	{
		// SHA-256 of ComicRack.Engine/CacheManager.cs without a byte order mark and with LF line endings
		private const string CacheManagerHash = "ceeb0ad49cf94086f68c5a611996c9f240a5eb500130a1fbbbdcf58a053014e8";

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
			Assert.Equal(CacheManagerHash, Hash(Normalize(Path.Combine(RepoRoot(), "ComicRack.Engine", "CacheManager.cs"))));
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
