using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ComicRack.Mac.Host.Scripting.Scan
{
	/// <summary>
	/// What can never run on macOS (Tier C, MACOS_NATIVE_PATH.md section 5.9.5 and 5.9.6): native Windows libraries, window messages and handles, WPF, COM automation, the registry,
	/// WMI, the MSHTML document model of the web browser control, and Python modules that exist only on Windows. A fact about the platforms, not about any script.
	/// </summary>
	public static class WindowsOnlyApi
	{
		private static readonly HashSet<string> WindowsLibraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"user32", "kernel32", "gdi32", "shell32", "ole32", "oleaut32", "advapi32", "ntdll", "comctl32", "comdlg32", "shlwapi", "winmm", "msvcrt", "gdiplus", "uxtheme", "dwmapi",
			"psapi", "version", "wininet", "ws2_32", "winspool", "imm32", "dbghelp", "crypt32", "secur32", "wtsapi32", "netapi32", "urlmon", "mpr", "setupapi", "shcore", "propsys",
			"d3d9", "d3d11", "dxgi", "opengl32", "glu32", "avicap32", "msi", "iphlpapi", "powrprof", "pdh", "winhttp", "mshtml", "ieframe", "wldap32", "kernelbase", "ucrtbase",
		};

		private static readonly Dictionary<string, string> TypeReasons = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["System.Windows.Forms.NativeWindow"] = "native window handles",
			["System.Windows.Forms.Message"] = "window messages",
			["System.Windows.Forms.IMessageFilter"] = "window messages",
			["System.Windows.Forms.HtmlDocument"] = "the MSHTML document model",
			["System.Windows.Forms.HtmlElement"] = "the MSHTML document model",
			["System.Windows.Forms.HtmlElementCollection"] = "the MSHTML document model",
			["System.Windows.Forms.HtmlWindow"] = "the MSHTML document model",
			["System.Windows.Forms.HtmlHistory"] = "the MSHTML document model",
			["System.Runtime.InteropServices.DllImportAttribute"] = "P/Invoke",
			["Microsoft.Win32.Registry"] = "the Windows registry",
			["Microsoft.Win32.RegistryKey"] = "the Windows registry",
			["Microsoft.Win32.SystemEvents"] = "Windows system events",
			["System.Security.Principal.WindowsIdentity"] = "Windows accounts",
			["System.Security.Principal.WindowsPrincipal"] = "Windows accounts",
		};

		private static readonly HashSet<string> WindowsOnlyNamespaces = new HashSet<string>(StringComparer.Ordinal)
		{
			"System.Management", "System.Management.Automation", "System.ServiceProcess", "Microsoft.Office.Interop",
		};

		private static readonly Dictionary<string, string[]> MemberReasons = new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			["System.Runtime.InteropServices.Marshal"] = new[]
			{
				"GetDelegateForFunctionPointer", "GetFunctionPointerForDelegate", "GetActiveObject", "BindToMoniker", "GetObjectForIUnknown", "GetIUnknownForObject", "GetComInterfaceForObject",
				"GetTypedObjectForIUnknown", "GetObjectForNativeVariant", "GetNativeVariantForObject", "GetHINSTANCE", "GetHRForException", "GetLastWin32Error", "GetHRForLastWin32Error",
			},
			["System.Type"] = new[] { "GetTypeFromProgID", "GetTypeFromCLSID" },
			["System.Windows.Forms.Control"] = new[] { "Handle", "WndProc", "DefWndProc", "CreateParams", "CreateHandle", "DestroyHandle", "RecreateHandle", "FromHandle", "FromChildHandle", "AssignHandle" },
			["System.Windows.Forms.WebBrowser"] = new[] { "Document", "DocumentStream", "ObjectForScripting", "ActiveXInstance", "Document" },
			["System.Windows.Forms.Application"] = new[] { "AddMessageFilter", "RemoveMessageFilter" },
		};

		private static readonly HashSet<string> WindowsPythonModules = new HashSet<string>(StringComparer.Ordinal)
		{
			"_winreg", "winreg", "msvcrt", "win32api", "win32con", "win32gui", "win32com", "win32process", "win32file", "win32clipboard", "win32event", "win32service", "win32pipe",
			"win32print", "win32ui", "win32pdh", "win32security", "win32net", "win32crypt", "pywintypes", "pythoncom", "winerror", "wmi", "winsound", "_subprocess", "comtypes", "_winapi",
		};

		/// <summary>Assemblies of the .NET Framework that .NET 10 does not have and the layer does not provide (web server, remoting, WCF, enterprise services, workflow, charts). <c>System.Web.Extensions</c>
		/// (JavaScriptSerializer) is provided by mac/cYo.Compat.Web since P4.17, so it is a "framework" assembly here.</summary>
		private static readonly string[] FrameworkOnlyNames =
		{
			"System.Web", "System.Web.Services", "System.Web.ApplicationServices", "System.Web.Mobile", "System.Web.RegularExpressions", "System.Runtime.Remoting",
			"System.EnterpriseServices", "System.Configuration.Install", "System.Deployment", "System.Data.OracleClient", "System.Messaging", "System.Windows.Forms.DataVisualization",
			"System.Workflow.Activities", "System.Workflow.ComponentModel", "System.Workflow.Runtime", "System.Data.Entity", "System.Data.Linq", "System.IdentityModel", "System.ServiceModel",
			"System.Runtime.Serialization.Formatters.Soap", "Microsoft.JScript", "Microsoft.Vsa", "System.Web.Extensions.Design", "System.Data.Services",
		};

		private static readonly string[] WpfNames =
		{
			"PresentationFramework", "PresentationCore", "WindowsBase", "PresentationUI", "UIAutomationClient", "UIAutomationTypes", "WindowsFormsIntegration", "System.Xaml", "ReachFramework",
		};

		private static readonly string[] ComNames =
		{
			"Microsoft.Office.Interop", "Interop.", "stdole", "office", "SHDocVw", "Microsoft.mshtml", "IWshRuntimeLibrary", "Shell32", "MSHTML", "AxInterop.", "Microsoft.Vbe.Interop",
		};

		private static readonly string[] WindowsEnvironment = { "ProgramFiles", "ProgramFiles(x86)", "SystemRoot", "windir", "SystemDrive", "ProgramData", "ComSpec" };

		private static readonly Regex BackslashFile = new Regex(@"^\\{1,2}[A-Za-z0-9_][\w .\-]*\.[A-Za-z0-9]{1,6}$", RegexOptions.CultureInvariant);

		private static readonly Regex DrivePath = new Regex(@"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant);

		private static readonly Regex UncPath = new Regex(@"^\\\\[^\\/\s]+\\[^\\/\s]+", RegexOptions.CultureInvariant);

		private static readonly Regex PercentVariable = new Regex(@"%(APPDATA|LOCALAPPDATA|PROGRAMFILES|PROGRAMFILES\(X86\)|USERPROFILE|TEMP|TMP|SYSTEMROOT|WINDIR|PROGRAMDATA|HOMEDRIVE|HOMEPATH)%", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

		// ".com" counts only behind a backslash: without one it is a domain name (www.example.com), not a DOS program.
		private static readonly Regex WindowsExecutable = new Regex(@"(^|[\\/\s""'])[\w\-. ]+\.(exe|bat|cmd|msi|lnk)(\s|$|""|')|\\[\w\-. ]+\.com(\s|$|""|')", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

		/// <summary>True when the name is a native Windows library (<c>user32</c>, <c>kernel32.dll</c>, ...).</summary>
		public static bool IsWindowsLibrary(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}
			string file = Path.GetFileName(name.Replace('\\', '/'));
			if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".drv", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".ocx", StringComparison.OrdinalIgnoreCase))
			{
				file = file.Substring(0, file.Length - 4);
			}
			return WindowsLibraries.Contains(file);
		}

		/// <summary>Why a type can never run on macOS, or null.</summary>
		public static string TypeReason(string fullName)
		{
			if (fullName == null)
			{
				return null;
			}
			if (TypeReasons.TryGetValue(fullName, out string reason))
			{
				return reason;
			}
			int dot = fullName.LastIndexOf('.');
			string ns = dot > 0 ? fullName.Substring(0, dot) : string.Empty;
			if (WindowsOnlyNamespaces.Contains(ns))
			{
				return "the Windows only namespace " + ns;
			}
			return null;
		}

		/// <summary>Why a member can never run on macOS, or null. <paramref name="declaringFullName"/> is the type that declares it.</summary>
		public static string MemberReason(string declaringFullName, string member)
		{
			if (declaringFullName != null && MemberReasons.TryGetValue(declaringFullName, out string[] names) && Array.IndexOf(names, member) >= 0)
			{
				switch (declaringFullName)
				{
				case "System.Windows.Forms.Control":
					return "Win32 window handles and messages";
				case "System.Windows.Forms.WebBrowser":
					return "the MSHTML document model";
				case "System.Windows.Forms.Application":
					return "window messages";
				default:
					return "native or COM interop";
				}
			}
			return null;
		}

		/// <summary>True when the name inside the namespace is one the tables above know (a star import binds only those names to the namespace; any other capitalised name belongs to something else).</summary>
		public static bool MentionsName(string ns, string name)
		{
			if (ns == null || name == null)
			{
				return false;
			}
			string full = ns + "." + name;
			return WindowsOnlyNamespaces.Contains(ns) || WindowsOnlyNamespaces.Contains(full) || TypeReasons.ContainsKey(full) || MemberReasons.ContainsKey(full) || TypeReason(full) != null;
		}

		/// <summary>True when the namespace holds a type or member the table above knows (so a star import from it is worth resolving).</summary>
		public static bool MentionsNamespace(string ns)
		{
			if (ns == null)
			{
				return false;
			}
			if (WindowsOnlyNamespaces.Contains(ns))
			{
				return true;
			}
			foreach (string key in TypeReasons.Keys)
			{
				if (key.StartsWith(ns + ".", StringComparison.Ordinal))
				{
					return true;
				}
			}
			foreach (string key in MemberReasons.Keys)
			{
				if (key.StartsWith(ns + ".", StringComparison.Ordinal) && key.IndexOf('.', ns.Length + 1) < 0)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>True for a Python module that exists only on Windows.</summary>
		public static bool IsWindowsPythonModule(string module)
		{
			if (module == null)
			{
				return false;
			}
			int dot = module.IndexOf('.');
			return WindowsPythonModules.Contains(dot < 0 ? module : module.Substring(0, dot));
		}

		/// <summary>The kind of an assembly a script or plugin references: forms, drawing, core, core-missing, framework, framework-only, wpf, com, windows or other.</summary>
		public static string ClassifyAssembly(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return "other";
			}
			string n = name;
			if (n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
			{
				n = n.Substring(0, n.Length - 4);
			}
			if (n.Equals("System.Windows.Forms", StringComparison.OrdinalIgnoreCase))
			{
				return "forms";
			}
			if (n.Equals("System.Drawing", StringComparison.OrdinalIgnoreCase) || n.StartsWith("System.Drawing.", StringComparison.OrdinalIgnoreCase))
			{
				return "drawing";
			}
			foreach (string wpf in WpfNames)
			{
				if (n.Equals(wpf, StringComparison.OrdinalIgnoreCase))
				{
					return "wpf";
				}
			}
			foreach (string com in ComNames)
			{
				if (n.StartsWith(com, StringComparison.OrdinalIgnoreCase))
				{
					return "com";
				}
			}
			if (n.Equals("System.Management", StringComparison.OrdinalIgnoreCase) || n.StartsWith("System.Management.", StringComparison.OrdinalIgnoreCase) || n.Equals("System.ServiceProcess", StringComparison.OrdinalIgnoreCase)
				|| n.Equals("Microsoft.Win32.Registry", StringComparison.OrdinalIgnoreCase) || n.Equals("Microsoft.Win32.SystemEvents", StringComparison.OrdinalIgnoreCase))
			{
				return "windows";
			}
			foreach (string only in FrameworkOnlyNames)
			{
				if (n.Equals(only, StringComparison.OrdinalIgnoreCase))
				{
					return "framework-only";
				}
			}
			if (n.Equals("ComicRack.Engine", StringComparison.OrdinalIgnoreCase) || n.Equals("ComicRack.Plugins", StringComparison.OrdinalIgnoreCase) || n.Equals("cYo.Common", StringComparison.OrdinalIgnoreCase))
			{
				return "core";
			}
			if (n.StartsWith("ComicRack.", StringComparison.OrdinalIgnoreCase) || n.StartsWith("cYo.", StringComparison.OrdinalIgnoreCase))
			{
				return "core-missing";
			}
			if (n.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) || n.Equals("System", StringComparison.OrdinalIgnoreCase) || n.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
				|| n.Equals("netstandard", StringComparison.OrdinalIgnoreCase) || n.Equals("Microsoft.CSharp", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Microsoft.VisualBasic", StringComparison.OrdinalIgnoreCase)
				|| n.StartsWith("IronPython", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Microsoft.Scripting", StringComparison.OrdinalIgnoreCase) || n.Equals("Microsoft.Dynamic", StringComparison.OrdinalIgnoreCase))
			{
				return "framework";
			}
			return "other";
		}

		/// <summary>The full namespace of a Windows Presentation Foundation type (<c>System.Windows.Controls</c>), as opposed to WinForms (<c>System.Windows.Forms</c>).</summary>
		public static bool IsWpfNamespace(string ns)
		{
			if (ns == null)
			{
				return false;
			}
			if (ns == "System.Windows.Forms.Integration" || ns.StartsWith("System.Windows.Forms.Integration.", StringComparison.Ordinal))
			{
				return true;
			}
			const string prefix = "System.Windows.";
			if (!ns.StartsWith(prefix, StringComparison.Ordinal))
			{
				return false;
			}
			string rest = ns.Substring(prefix.Length);
			return !(rest == "Forms" || rest.StartsWith("Forms.", StringComparison.Ordinal));
		}

		/// <summary>A message when a string constant looks like a Windows path, environment variable or program; null otherwise.</summary>
		public static string StringWarning(string text)
		{
			if (string.IsNullOrEmpty(text) || text.Length > 600)
			{
				return null;
			}
			if (DrivePath.IsMatch(text) || UncPath.IsMatch(text))
			{
				return "Windows path \"" + Shorten(text) + "\"";
			}
			if (BackslashFile.IsMatch(text))
			{
				return "Windows path separator \"" + Shorten(text) + "\"";
			}
			Match m = PercentVariable.Match(text);
			if (m.Success)
			{
				return "Windows environment variable " + m.Value;
			}
			if (text.IndexOf('\n') < 0 && WindowsExecutable.IsMatch(text))
			{
				return "Windows program \"" + Shorten(text) + "\"";
			}
			return null;
		}

		/// <summary>True for an environment variable name that exists only on Windows (the user data folder variable is mapped for scripts).</summary>
		public static bool IsWindowsEnvironmentVariable(string name) => Array.IndexOf(WindowsEnvironment, name) >= 0;

		private static string Shorten(string s) => s.Length <= 60 ? s : s.Substring(0, 57) + "...";
	}
}
