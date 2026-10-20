using System;
using System.Diagnostics;
using System.Text;

namespace UpstreamScan
{
	internal static class Git
	{
		/// <summary>Runs git in <paramref name="repo"/>; returns stdout. Throws with stderr on a non-zero exit unless <paramref name="allowFail"/>.</summary>
		public static string Run(string repo, bool allowFail, params string[] args)
		{
			var psi = new ProcessStartInfo("git")
			{
				WorkingDirectory = repo,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				StandardOutputEncoding = new UTF8Encoding(false),
				StandardErrorEncoding = new UTF8Encoding(false),
			};
			psi.ArgumentList.Add("-c");
			psi.ArgumentList.Add("core.quotepath=false");
			foreach (var a in args)
				psi.ArgumentList.Add(a);
			using var p = Process.Start(psi);
			var err = p.StandardError.ReadToEndAsync();
			var output = p.StandardOutput.ReadToEnd();
			p.WaitForExit();
			if (p.ExitCode != 0 && !allowFail)
				throw new InvalidOperationException("git " + string.Join(' ', args) + " failed: " + err.Result.Trim());
			return p.ExitCode == 0 ? output : null;
		}

		public static string Show(string repo, string rev, string path)
		{
			return Run(repo, true, "show", rev + ":" + path);
		}
	}
}
