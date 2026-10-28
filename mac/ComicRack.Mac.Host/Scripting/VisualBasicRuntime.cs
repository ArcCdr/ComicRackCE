using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ComicRack.Mac.Host.Scripting
{
	/// <summary>
	/// <c>Microsoft.VisualBasic</c> by name (<c>clr.AddReference('Microsoft.VisualBasic')</c>, the reference of a compiled plug-in) is the assembly of this program (mac/cYo.Compat.VisualBasic, version
	/// 10.0.1.0) and not the shared framework's file of that name, whose <c>FileSystem</c> refuses every <c>UIOption</c> overload on macOS (P4.17.4, mac/reports/vb-filesystem.md). The host
	/// chooses by assembly version, so <c>mac/Directory.Build.targets</c> writes the version into the deps entry of every output folder. This class checks that the choice was made
	/// (an executable that does not carry the entry keeps the framework's behaviour and says so in <see cref="Source"/>) and hands the program's Trash and dialog to the assembly.
	/// </summary>
	public static class VisualBasicRuntime
	{
		private const string AssemblyName = "Microsoft.VisualBasic";
		private static readonly object Gate = new object();
		private static string source;

		/// <summary>"program" when the name resolves to this program's assembly, "framework" when it resolves to the shared framework's file.</summary>
		public static string Source
		{
			get
			{
				lock (Gate)
				{
					if (source == null)
					{
						source = Probe();
					}
					return source;
				}
			}
		}

		/// <summary>True when <c>Microsoft.VisualBasic</c> is this program's assembly.</summary>
		public static bool IsProgramAssembly => Source == "program";

		private static string Probe()
		{
			try
			{
				Assembly assembly = Assembly.Load(new AssemblyName(AssemblyName));
				return assembly.GetType("ComicRack.Compat.VisualBasic.FileSystemServices", throwOnError: false) != null ? "program" : "framework";
			}
			catch (Exception ex) when (ex is System.IO.IOException || ex is BadImageFormatException)
			{
				return "framework";
			}
		}

		/// <summary>Hands the program's Trash and dialog to the assembly; dispose the result to take them out again. Call only when <see cref="IsProgramAssembly"/>.</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static IDisposable Connect(Func<IScriptUi> ui)
		{
			// a separate method: the JIT binds the types named here, which only works when the assembly is the program's
			return new Link(new MacTrash(), new ScriptErrorDialog(ui));
		}

		private sealed class Link : IDisposable
		{
			private readonly ComicRack.Compat.VisualBasic.ITrash trash;
			private readonly ComicRack.Compat.VisualBasic.IFileSystemUi dialog;

			public Link(ComicRack.Compat.VisualBasic.ITrash trash, ComicRack.Compat.VisualBasic.IFileSystemUi dialog)
			{
				this.trash = trash;
				this.dialog = dialog;
				ComicRack.Compat.VisualBasic.FileSystemServices.Trash = trash;
				ComicRack.Compat.VisualBasic.FileSystemServices.Ui = dialog;
			}

			public void Dispose()
			{
				if (ReferenceEquals(ComicRack.Compat.VisualBasic.FileSystemServices.Trash, trash))
				{
					ComicRack.Compat.VisualBasic.FileSystemServices.Trash = null;
				}
				if (ReferenceEquals(ComicRack.Compat.VisualBasic.FileSystemServices.Ui, dialog))
				{
					ComicRack.Compat.VisualBasic.FileSystemServices.Ui = null;
				}
			}
		}
	}

	/// <summary>The Trash behind <c>RecycleOption.SendToRecycleBin</c>: <c>NSFileManager trashItem</c> (<see cref="Platform.MacShell.MoveToTrash"/>).</summary>
	internal sealed class MacTrash : ComicRack.Compat.VisualBasic.ITrash
	{
		public void MoveToTrash(string fullPath) => Platform.MacShell.MoveToTrash(fullPath);
	}

	/// <summary>The error dialog of <c>UIOption.OnlyErrorDialogs</c> and <c>AllDialogs</c>: the script UI of the program.</summary>
	internal sealed class ScriptErrorDialog : ComicRack.Compat.VisualBasic.IFileSystemUi
	{
		private readonly Func<IScriptUi> ui;

		public ScriptErrorDialog(Func<IScriptUi> ui)
		{
			this.ui = ui;
		}

		public void ShowError(string message) => ui().ShowError(message);
	}
}
