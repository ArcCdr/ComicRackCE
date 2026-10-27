using System;
using System.IO;
using System.Threading;

namespace System.Windows.Forms
{
	/// <summary>
	/// Facade for the members of Application that the Core uses. The strings are the ones the Windows app reports
	/// (ComicRack/Properties/AssemblyInfo.cs: AssemblyCompany, AssemblyProduct, AssemblyInformationalVersion),
	/// because IniFile search paths and backup archive paths derive from them.
	/// </summary>
	public sealed partial class Application
	{
		private Application()
		{
		}

		private const string WindowsCompanyName = "cYo";

		private const string WindowsProductName = "ComicRack Community Edition";

		private const string WindowsProductVersion = "0.9.184";

		public static string CompanyName => WindowsCompanyName;

		public static string ProductName => WindowsProductName;

		public static string ProductVersion => WindowsProductVersion;

		public static string StartupPath => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		public static string ExecutablePath => Environment.ProcessPath ?? string.Empty;

		/// <summary>Raised only by <see cref="RaiseIdle"/>; there is no message loop in this shim.</summary>
		public static event EventHandler Idle;

		/// <summary>Never raised; there is no message loop in this shim. The handler type is the framework's System.Threading one.</summary>
#pragma warning disable CS0067
		public static event ThreadExceptionEventHandler ThreadException;
#pragma warning restore CS0067

		public static void RaiseIdle(EventArgs e)
		{
			Idle?.Invoke(null, e);
		}
	}
}
