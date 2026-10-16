using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Compat.Forms.Tests
{
	/// <summary>Locates the WinForms reference assembly in the Microsoft.WindowsDesktop.App.Ref pack (downloaded by a pass A build).</summary>
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
				.Select(x => Path.Combine(x.dir, "ref", "net10.0", "System.Windows.Forms.dll"))
				.FirstOrDefault(File.Exists);
			return best ?? throw new FileNotFoundException("System.Windows.Forms.dll not found under " + root);
		}

		/// <summary>A load context that sees the reference assembly, the shim and the runtime (for System.Runtime and System.Drawing.Primitives).</summary>
		public static MetadataLoadContext CreateContext(string shimPath, out Assembly reference, out Assembly shim)
		{
			string formsRef = Find();
			string refDir = Path.GetDirectoryName(formsRef);
			var paths = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
				.Concat(Directory.GetFiles(refDir, "*.dll").Where(f => !File.Exists(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), Path.GetFileName(f)))))
				.Append(shimPath)
				.Concat(Directory.GetFiles(Path.GetDirectoryName(shimPath), "cYo.Compat.Drawing.dll")); // Cursor.Draw(Graphics, Rectangle) names the Drawing stand-in
			var context = new MetadataLoadContext(new PathAssemblyResolver(paths), "System.Private.CoreLib");
			reference = context.LoadFromAssemblyPath(formsRef);
			shim = context.LoadFromAssemblyPath(shimPath);
			return context;
		}
	}
}
