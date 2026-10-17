using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ComicRack.Mac.Host
{
	/// <summary>
	/// Maps the native library name that the Core's PDF reader imports ("pdfium", see the pass B PdfiumReaderEngine; the
	/// PDFiumSharp wrapper's own "pdfium_x64"/"pdfium_x86" names are accepted too) to the libpdfium.dylib of the
	/// bblanchon.PDFium.macOS package, which sits in the application's native folder.
	/// </summary>
	public static class NativeLibraryResolver
	{
		private const string LibraryFile = "libpdfium.dylib";

		private static readonly object gate = new object();
		private static bool installed;

		/// <summary>The folders searched for libpdfium.dylib, in order.</summary>
		public static string[] SearchFolders()
		{
			string baseDir = AppContext.BaseDirectory;
			string rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
			return new[] { baseDir, Path.Combine(baseDir, "runtimes", rid, "native") };
		}

		/// <summary>True for the library names the PDF wrapper may ask for ("pdfium", "pdfium_x64", "libpdfium.dylib", ...).</summary>
		public static bool IsPdfiumName(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}
			string n = Path.GetFileName(name);
			foreach (string ext in new[] { ".dll", ".dylib", ".so" })
			{
				if (n.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
				{
					n = n.Substring(0, n.Length - ext.Length);
					break;
				}
			}
			if (n.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
			{
				n = n.Substring(3);
			}
			return string.Equals(n, "pdfium", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(n, "pdfium_x64", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(n, "pdfium_x86", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>Installs the resolver once per process on the Core assembly. Safe to call repeatedly; does nothing off macOS.</summary>
		public static void Install()
		{
			lock (gate)
			{
				if (installed || !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				{
					return;
				}
				NativeLibrary.SetDllImportResolver(typeof(cYo.Projects.ComicRack.Engine.EngineConfiguration).Assembly, Resolve);
				installed = true;
			}
		}

		private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
		{
			if (!IsPdfiumName(libraryName))
			{
				return IntPtr.Zero;
			}
			foreach (string folder in SearchFolders())
			{
				string file = Path.Combine(folder, LibraryFile);
				if (File.Exists(file) && NativeLibrary.TryLoad(file, out IntPtr handle))
				{
					return handle;
				}
			}
			return IntPtr.Zero;
		}
	}
}
