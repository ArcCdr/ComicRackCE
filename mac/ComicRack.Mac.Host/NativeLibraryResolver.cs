using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ComicRack.Mac.Host
{
	/// <summary>
	/// Maps the native library name that the Core's PDF reader imports ("pdfium", see the pass B PdfiumReaderEngine; the
	/// PDFiumSharp wrapper's own "pdfium_x64"/"pdfium_x86" names are accepted too) to the libpdfium.dylib of the
	/// bblanchon.PDFium.macOS package, which sits in the application's native folder.
	///
	/// It is also the Windows P/Invoke tripwire (P1.2). The resolver is installed on every Core assembly. A call into a Windows
	/// system library (kernel32, user32, shell32, ...) cannot work on macOS, and the Core often swallows the resulting
	/// DllNotFoundException (the FileSystemEnumerable lesson of M1.5b). So the first call per library and entry point is logged
	/// as "WARN Windows API &lt;module&gt;!&lt;entry&gt; called from &lt;managed stack&gt;" and every call increments
	/// <see cref="WindowsCalls"/>; the tests assert that the paths of the v1 feature set leave it at zero. The call itself is not
	/// changed: the resolver still returns "not found" and the runtime raises the exception as before.
	/// </summary>
	public static class NativeLibraryResolver
	{
		private const string LibraryFile = "libpdfium.dylib";

		private static readonly object gate = new object();
		private static bool installed;
		private static int windowsCalls;
		private static readonly ConcurrentDictionary<string, WindowsCall> windowsCallsSeen = new ConcurrentDictionary<string, WindowsCall>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Windows system libraries (name without ".dll", lower case) that a call from the Core must never reach on macOS.</summary>
		private static readonly HashSet<string> WindowsModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"kernel32", "user32", "shell32", "gdi32", "shlwapi", "ole32", "uxtheme", "dwmapi", "credui",
			"advapi32", "ntdll", "comctl32", "comdlg32", "oleaut32", "winmm", "psapi", "version", "imm32", "mpr", "netapi32", "setupapi", "propsys", "crypt32", "wininet",
		};

		/// <summary>Where the tripwire writes its warnings; <see cref="MacHost"/> sets it while a host is open. Null: only counted.</summary>
		public static HostLog Log { get; set; }

		/// <summary>How many calls into Windows libraries the Core made since the process started or since <see cref="ResetWindowsCalls"/> (every call, not only the first of an entry point).</summary>
		public static int WindowsCalls => Volatile.Read(ref windowsCalls);

		/// <summary>One record per Windows library and entry point seen since the last <see cref="ResetWindowsCalls"/>, with the managed stack of the first call.</summary>
		public static IReadOnlyList<WindowsCall> WindowsCallRecords => windowsCallsSeen.Values.OrderBy(c => c.Module).ThenBy(c => c.Entry).ToList();

		/// <summary>Sets <see cref="WindowsCalls"/> to zero and forgets the records (tests; a warning is logged again for an entry point seen before).</summary>
		public static void ResetWindowsCalls()
		{
			Volatile.Write(ref windowsCalls, 0);
			windowsCallsSeen.Clear();
		}

		/// <summary>A Windows library and entry point that the Core called.</summary>
		public sealed class WindowsCall
		{
			public string Module { get; init; }

			public string Entry { get; init; }

			/// <summary>The managed stack of the first call.</summary>
			public string Stack { get; init; }

			internal int count;

			/// <summary>How many times it was called.</summary>
			public int Count => Volatile.Read(ref count);

			public override string ToString() => Module + "!" + Entry;
		}

		/// <summary>The module of a Windows library name ("user32.dll", "USER32", "C:\\Windows\\System32\\kernel32.dll" give "user32", "user32", "kernel32"), or null if it is not a Windows system library.</summary>
		public static string WindowsModuleOf(string libraryName)
		{
			if (string.IsNullOrEmpty(libraryName))
			{
				return null;
			}
			string n = libraryName.Substring(libraryName.LastIndexOfAny(new[] { '\\', '/' }) + 1);
			if (n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
			{
				n = n.Substring(0, n.Length - 4);
			}
			return WindowsModules.Contains(n) ? n.ToLowerInvariant() : null;
		}

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

		/// <summary>The assemblies whose P/Invokes the resolver sees: the three Core assemblies and the two stand-in libraries.</summary>
		public static Assembly[] CoreAssemblies()
		{
			return new[]
			{
				typeof(cYo.Projects.ComicRack.Engine.EngineConfiguration).Assembly, // ComicRack.Engine
				typeof(cYo.Common.Threading.ThreadUtility).Assembly, // cYo.Common
				typeof(cYo.Projects.ComicRack.Plugins.ComicBookPluginMatcher).Assembly, // ComicRack.Plugins
				typeof(System.Windows.Forms.Keys).Assembly, // System.Windows.Forms (the facade)
				typeof(System.Drawing.Bitmap).Assembly, // System.Drawing.Common (the facade)
			}.Distinct().ToArray();
		}

		/// <summary>Installs the resolver once per process on every Core assembly. Safe to call repeatedly; does nothing off macOS.</summary>
		public static void Install()
		{
			lock (gate)
			{
				if (installed || !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				{
					return;
				}
				foreach (Assembly assembly in CoreAssemblies())
				{
					NativeLibrary.SetDllImportResolver(assembly, Resolve);
				}
				installed = true;
			}
		}

		private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
		{
			if (!IsPdfiumName(libraryName))
			{
				string module = WindowsModuleOf(libraryName);
				if (module != null)
				{
					RecordWindowsCall(module);
				}
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

		private static void RecordWindowsCall(string module)
		{
			try
			{
				Interlocked.Increment(ref windowsCalls);
				var trace = new StackTrace(2, fNeedFileInfo: false);
				string entry = EntryPointOf(trace, module);
				string key = module + "!" + entry;
				if (windowsCallsSeen.TryGetValue(key, out WindowsCall known))
				{
					Interlocked.Increment(ref known.count);
					return;
				}
				var call = new WindowsCall { Module = module, Entry = entry, Stack = FormatStack(trace) };
				call.count = 1;
				if (!windowsCallsSeen.TryAdd(key, call))
				{
					Interlocked.Increment(ref windowsCallsSeen[key].count);
					return;
				}
				Log?.Write("WARN", "Windows API " + key + " called from" + Environment.NewLine + call.Stack);
			}
			catch (Exception)
			{
				// The tripwire never changes what the call does.
			}
		}

		/// <summary>The entry point: the declared extern method of the stack that imports the module, else the method that called into the runtime.</summary>
		private static string EntryPointOf(StackTrace trace, string module)
		{
			MethodBase first = null;
			foreach (StackFrame frame in trace.GetFrames())
			{
				MethodBase method = frame.GetMethod();
				if (method == null)
				{
					continue;
				}
				first = first ?? method;
				if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
				{
					DllImportAttribute import = method.GetCustomAttribute<DllImportAttribute>();
					if (import != null && string.Equals(WindowsModuleOf(import.Value), module, StringComparison.OrdinalIgnoreCase))
					{
						return string.IsNullOrEmpty(import.EntryPoint) ? method.Name : import.EntryPoint;
					}
				}
			}
			return first == null ? "(unknown)" : first.Name;
		}

		private static string FormatStack(StackTrace trace)
		{
			var text = new StringBuilder();
			int shown = 0;
			foreach (StackFrame frame in trace.GetFrames())
			{
				MethodBase method = frame.GetMethod();
				if (method == null || (shown == 0 && method.DeclaringType?.Namespace == "System.Runtime.InteropServices"))
				{
					continue; // the runtime's own callback frame above the extern method
				}
				if (shown++ == 40)
				{
					text.AppendLine("   ...");
					break;
				}
				text.Append("   at ").Append(method.DeclaringType?.FullName).Append('.').Append(method.Name).AppendLine();
			}
			return text.ToString().TrimEnd();
		}
	}
}
