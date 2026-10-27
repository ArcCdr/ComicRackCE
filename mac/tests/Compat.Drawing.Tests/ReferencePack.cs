using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Compat.Drawing.Tests
{
	/// <summary>Locates the System.Drawing.Common reference assembly in the Microsoft.WindowsDesktop.App.Ref pack (downloaded by a pass A build).</summary>
	internal static class ReferencePack
	{
		public static string Find()
		{
			string packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
			if (string.IsNullOrEmpty(packages))
			{
				packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
			}
			string root = Path.Combine(packages, "microsoft.windowsdesktop.app.ref");
			if (!Directory.Exists(root))
			{
				throw new FileNotFoundException("Microsoft.WindowsDesktop.App.Ref pack not found in " + root + ". Run a pass A build first: dotnet build mac/ComicRack.Plugins.Core/ComicRack.Plugins.Core.csproj -p:CensusPass=A");
			}
			string best = Directory.GetDirectories(root)
				.Select(d => (dir: d, version: Version.TryParse(Path.GetFileName(d), out var v) ? v : new Version(0, 0)))
				.OrderByDescending(x => x.version)
				.Select(x => Path.Combine(x.dir, "ref", "net10.0", "System.Drawing.Common.dll"))
				.FirstOrDefault(File.Exists);
			return best ?? throw new FileNotFoundException("System.Drawing.Common.dll not found under " + root);
		}
	}
}
