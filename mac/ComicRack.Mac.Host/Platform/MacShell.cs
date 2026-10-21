using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ComicRack.Mac.Host.Platform
{
	/// <summary>
	/// The Mac equivalents of cYo.Common/Win32/ShellFile.cs (delete to the recycle bin) and FileExplorer.cs (show in the folder, open with
	/// the default program): Trash through NSFileManager, Reveal in Finder and Open through /usr/bin/open. None of them shows UI.
	/// </summary>
	public static class MacShell
	{
		private const string Runtime = "/usr/lib/libobjc.A.dylib";
		private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";
		private const string OpenTool = "/usr/bin/open";

		[DllImport(Runtime)]
		private static extern IntPtr objc_getClass(string name);

		[DllImport(Runtime)]
		private static extern IntPtr sel_registerName(string name);

		[DllImport(Runtime)]
		private static extern IntPtr objc_autoreleasePoolPush();

		[DllImport(Runtime)]
		private static extern void objc_autoreleasePoolPop(IntPtr pool);

		[DllImport("libSystem.dylib")]
		private static extern IntPtr dlopen(string path, int mode);

		[DllImport(Runtime, EntryPoint = "objc_msgSend")]
		private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

		[DllImport(Runtime, EntryPoint = "objc_msgSend")]
		private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);

		[DllImport(Runtime, EntryPoint = "objc_msgSend")]
		private static extern byte SendTrash(IntPtr receiver, IntPtr selector, IntPtr url, out IntPtr resultingUrl, out IntPtr error);

		private static bool foundationLoaded;

		/// <summary>True where the Objective-C runtime exists.</summary>
		public static bool IsAvailable => OperatingSystem.IsMacOS();

		/// <summary>
		/// Moves a file or folder to the Trash (the user's ~/.Trash, or the volume's .Trashes) and returns the path it now has there.
		/// Finder's "Put Back" information is not recorded by this call.
		/// </summary>
		/// <exception cref="FileNotFoundException">The path does not exist.</exception>
		/// <exception cref="IOException">The system refused.</exception>
		public static string MoveToTrash(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				throw new ArgumentException("Invalid path", nameof(path));
			}
			string full = Path.GetFullPath(path);
			if (!File.Exists(full) && !Directory.Exists(full))
			{
				throw new FileNotFoundException("Nothing to move to the Trash", full);
			}
			if (!IsAvailable)
			{
				throw new PlatformNotSupportedException("The Trash is a macOS service");
			}
			EnsureFoundation();
			IntPtr pool = objc_autoreleasePoolPush();
			try
			{
				IntPtr manager = Send(objc_getClass("NSFileManager"), sel_registerName("defaultManager"));
				IntPtr url = SendWithString(objc_getClass("NSURL"), "fileURLWithPath:", full);
				byte ok = SendTrash(manager, sel_registerName("trashItemAtURL:resultingItemURL:error:"), url, out IntPtr result, out IntPtr error);
				if (ok == 0)
				{
					string reason = error == IntPtr.Zero ? "unknown error" : ToManaged(Send(error, sel_registerName("localizedDescription")));
					throw new IOException("Cannot move '" + full + "' to the Trash: " + reason);
				}
				return result == IntPtr.Zero ? null : ToManaged(Send(result, sel_registerName("path")));
			}
			finally
			{
				objc_autoreleasePoolPop(pool);
			}
		}

		/// <summary>Shows the items selected in a Finder window (open -R). Paths that do not exist are skipped; returns false if none was left.</summary>
		public static bool Reveal(params string[] paths)
		{
			List<string> existing = Existing(paths);
			if (existing.Count == 0)
			{
				return false;
			}
			existing.Insert(0, "-R");
			return RunOpen(existing);
		}

		/// <summary>Opens the file or folder with the program the system has registered for it (open). Returns false if it does not exist or could not be started.</summary>
		public static bool OpenWithDefaultApp(string path)
		{
			List<string> existing = Existing(new[] { path });
			return existing.Count != 0 && RunOpen(existing);
		}

		private static List<string> Existing(string[] paths)
		{
			var list = new List<string>();
			if (paths == null)
			{
				return list;
			}
			foreach (string p in paths)
			{
				if (string.IsNullOrEmpty(p))
				{
					continue;
				}
				string full = Path.GetFullPath(p);
				if (File.Exists(full) || Directory.Exists(full))
				{
					list.Add(full);
				}
			}
			return list;
		}

		private static bool RunOpen(List<string> arguments)
		{
			try
			{
				var info = new ProcessStartInfo(OpenTool)
				{
					UseShellExecute = false,
					CreateNoWindow = true
				};
				foreach (string a in arguments)
				{
					info.ArgumentList.Add(a);
				}
				using (Process process = Process.Start(info))
				{
					if (process == null)
					{
						return false;
					}
					// open returns once the request is handed over to Launch Services.
					if (!process.WaitForExit(10000))
					{
						return true;
					}
					return process.ExitCode == 0;
				}
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static void EnsureFoundation()
		{
			if (!foundationLoaded)
			{
				dlopen(Foundation, 2);
				foundationLoaded = true;
			}
		}

		private static IntPtr SendWithString(IntPtr receiver, string selector, string text)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(text + "\0");
			GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
			try
			{
				IntPtr str = Send(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), pin.AddrOfPinnedObject());
				return Send(receiver, sel_registerName(selector), str);
			}
			finally
			{
				pin.Free();
			}
		}

		private static string ToManaged(IntPtr nsString)
		{
			if (nsString == IntPtr.Zero)
			{
				return string.Empty;
			}
			IntPtr utf8 = Send(nsString, sel_registerName("UTF8String"));
			return utf8 == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(utf8);
		}
	}
}
