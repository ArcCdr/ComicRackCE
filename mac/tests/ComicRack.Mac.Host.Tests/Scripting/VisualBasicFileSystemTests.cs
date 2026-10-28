using System;
using System.Collections.Generic;
using System.IO;
using ComicRack.Compat.VisualBasic;
using ComicRack.Mac.Host.Scripting;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>
	/// P4.17.4: a script reaches <c>Microsoft.VisualBasic.FileIO.FileSystem</c> with <c>UIOption</c> and <c>RecycleOption</c> in the plugin host, the host hands its Trash and its dialog to the assembly, and
	/// takes them out again. The real Trash is replaced by a fake one in every test that deletes.
	/// </summary>
	public class VisualBasicFileSystemTests
	{
		private sealed class FakeTrash : ITrash
		{
			public List<string> Moved { get; } = new List<string>();

			public Exception Fail { get; set; }

			public void MoveToTrash(string fullPath)
			{
				if (Fail != null)
				{
					throw Fail;
				}
				Moved.Add(fullPath);
				File.Delete(fullPath);
			}
		}

		private const string DeleteScript = @"
import clr
clr.AddReference('Microsoft.VisualBasic')
from Microsoft.VisualBasic.FileIO import FileSystem, UIOption, RecycleOption

#@Name Delete
#@Hook Books
def Delete(books):
	try:
		FileSystem.DeleteFile(r'@FILE@', UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin)
		print 'deleted'
	except Exception as e:
		print 'failed', e.message
";

		private static (string output, HeadlessScriptUi ui, FakeTrash trash) Run(string file, Action<FakeTrash> arrange)
		{
			using (var temp = new TempFolder())
			{
				string dir = ScriptTestSupport.WriteScripts(temp, "scripts", ("delete.py", DeleteScript.Replace("@FILE@", file)));
				var ui = new HeadlessScriptUi();
				using (MacHost host = ScriptTestSupport.Open(temp, ui: ui, scriptFolders: dir))
				{
					ScriptTestSupport.Add(host, ScriptTestSupport.Book("A", "1"));
					var trash = new FakeTrash();
					arrange(trash);
					FileSystemServices.Trash = trash; // the fake replaces the program's Trash after the host has connected it
					var output = new StringWriter();
					var error = new StringWriter();
					int code = ScriptRunner.Run(host, Path.Combine(dir, "delete.py"), "Books", null, output, error);
					Assert.True(code == 0, output.ToString() + error);
					return (output.ToString(), ui, trash);
				}
			}
		}

		[Fact]
		public void A_script_sends_a_file_to_the_Trash_with_the_arguments_of_the_Library_Organizer()
		{
			string file = Path.Combine(Path.GetTempPath(), "crmac-vb-" + Guid.NewGuid().ToString("N") + ".cbz");
			File.WriteAllText(file, "x");
			try
			{
				(string output, HeadlessScriptUi ui, FakeTrash trash) = Run(file, t => { });
				Assert.Contains("deleted", output);
				Assert.False(File.Exists(file));
				Assert.Empty(ui.Requests.Where_ShowError());
				Assert.Equal(new[] { file }, trash.Moved.ToArray());
			}
			finally
			{
				File.Delete(file);
			}
		}

		[Fact]
		public void When_the_Trash_fails_the_script_sees_the_exception_the_dialog_is_asked_and_the_file_stays()
		{
			string file = Path.Combine(Path.GetTempPath(), "crmac-vb-" + Guid.NewGuid().ToString("N") + ".cbz");
			File.WriteAllText(file, "x");
			try
			{
				(string output, HeadlessScriptUi ui, FakeTrash trash) = Run(file, t => t.Fail = new IOException("Trash is full"));
				Assert.Contains("failed System.IO.IOException: Trash is full", output);
				Assert.True(File.Exists(file));
				Assert.Equal(new[] { "ShowError: Trash is full" }, ui.Requests.Where_ShowError());
			}
			finally
			{
				File.Delete(file);
			}
		}

		[Fact]
		public void The_host_connects_the_Trash_and_the_dialog_and_takes_them_out_when_it_is_disposed()
		{
			using (var temp = new TempFolder())
			{
				Assert.True(VisualBasicRuntime.IsProgramAssembly, "Microsoft.VisualBasic resolves to " + VisualBasicRuntime.Source);
				using (MacHost host = ScriptTestSupport.Open(temp))
				{
					Assert.Equal("MacTrash", FileSystemServices.Trash?.GetType().Name);
					Assert.Equal("ScriptErrorDialog", FileSystemServices.Ui?.GetType().Name);
					FileSystemServices.Ui.ShowError("hello");
					Assert.Contains("ShowError: hello", ((HeadlessScriptUi)host.Scripts.Ui).Requests);
				}
				Assert.Null(FileSystemServices.Trash);
				Assert.Null(FileSystemServices.Ui);
			}
		}

		[Fact]
		public void The_dialog_goes_to_the_script_UI_the_app_sets_later()
		{
			using (var temp = new TempFolder())
			using (MacHost host = ScriptTestSupport.Open(temp))
			{
				var later = new HeadlessScriptUi();
				host.Scripts.Ui = later;
				FileSystemServices.Ui.ShowError("later");
				Assert.Contains("ShowError: later", later.Requests);
			}
		}
	}

	internal static class RequestsExtensions
	{
		public static IEnumerable<string> Where_ShowError(this IReadOnlyList<string> requests)
		{
			foreach (string r in requests)
			{
				if (r.StartsWith("ShowError: ", StringComparison.Ordinal))
				{
					yield return r;
				}
			}
		}
	}
}
