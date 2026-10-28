using System;
using System.IO;
using System.Threading;
using cYo.Compat.Forms;

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

		/// <summary>
		/// Raised for an exception of a handler that the facade ran for a peer (an event of the toolkit: a click, a key, a timer) and that nobody caught: the script's exception
		/// handler decides what happens, as in a WinForms message loop. Without a handler the exception is reported to <c>cYo.Compat.Forms.FormsRuntime.UnhandledException</c>.
		/// The handler type is the framework's System.Threading one.
		/// </summary>
		public static event ThreadExceptionEventHandler ThreadException;

		/// <summary>Raises <see cref="ThreadException"/>; false when nobody listens (or the handler threw).</summary>
		internal static bool RaiseThreadException(Exception e)
		{
			ThreadExceptionEventHandler handler = ThreadException;
			if (handler == null)
			{
				return false;
			}
			try
			{
				handler(null, new ThreadExceptionEventArgs(e));
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>Visual styles are the platform's own on macOS: scripts call this at the top and it has nothing to do.</summary>
		public static void EnableVisualStyles()
		{
		}

		/// <summary>Text rendering is not selectable here: scripts call this at the top and it has nothing to do.</summary>
		public static void SetCompatibleTextRenderingDefault(bool defaultValue)
		{
		}

		/// <summary>
		/// Runs what is queued for the UI thread (posted property changes, Invoke and BeginInvoke calls, timer ticks that are due, a form's Shown), then returns. Called on another thread it
		/// does nothing, as the Framework has no messages to process for a thread without windows.
		/// </summary>
		public static void DoEvents()
		{
			FormsRuntime.Backend.DoEvents();
		}

		public static void RaiseIdle(EventArgs e)
		{
			Idle?.Invoke(null, e);
		}
	}
}
