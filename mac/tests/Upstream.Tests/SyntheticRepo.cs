using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Upstream.Tests
{
	/// <summary>A throw-away git repository in a temp folder. Never touches the working repository.</summary>
	internal sealed class SyntheticRepo : IDisposable
	{
		public string Dir { get; }

		public SyntheticRepo()
		{
			Dir = Path.Combine(Path.GetTempPath(), "upstream-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Dir);
			Git("init", "-q", "-b", "macos");
			Git("config", "user.email", "test@example.invalid");
			Git("config", "user.name", "test");
			Git("config", "commit.gpgsign", "false");
		}

		public void Write(string path, string text)
		{
			string full = Path.Combine(Dir, path);
			Directory.CreateDirectory(Path.GetDirectoryName(full));
			File.WriteAllText(full, text, new UTF8Encoding(false));
		}

		public void Delete(string path) { File.Delete(Path.Combine(Dir, path)); }

		public void Move(string from, string to)
		{
			string dest = Path.Combine(Dir, to);
			Directory.CreateDirectory(Path.GetDirectoryName(dest));
			File.Move(Path.Combine(Dir, from), dest);
		}

		public string Commit(string message)
		{
			Git("add", "-A");
			Git("commit", "-q", "-m", message);
			return Git("rev-parse", "HEAD").Trim();
		}

		public string Git(params string[] args)
		{
			var psi = new ProcessStartInfo("git")
			{
				WorkingDirectory = Dir,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
			};
			foreach (var a in args)
				psi.ArgumentList.Add(a);
			using var p = Process.Start(psi);
			var err = p.StandardError.ReadToEndAsync();
			string output = p.StandardOutput.ReadToEnd();
			p.WaitForExit();
			if (p.ExitCode != 0)
				throw new InvalidOperationException("git " + string.Join(' ', args) + ": " + err.Result);
			return output;
		}

		public void Dispose()
		{
			try
			{
				foreach (var f in new DirectoryInfo(Dir).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
					f.Attributes = FileAttributes.Normal;
				Directory.Delete(Dir, true);
			}
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
		}
	}
}
