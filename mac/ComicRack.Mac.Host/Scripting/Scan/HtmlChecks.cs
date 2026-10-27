using System;
using System.Text.RegularExpressions;

namespace ComicRack.Mac.Host.Scripting.Scan
{
	/// <summary>
	/// The checks of HTML that a plugin shows in a web panel (MACOS_NATIVE_PATH.md section 5.9.8): the <c>window.external</c> bridge forwards method calls to the host, but WebKit messaging
	/// is asynchronous, so a property read that is not a call can only be answered from a snapshot taken when the page loads.
	/// </summary>
	public static class HtmlChecks
	{
		private static readonly Regex External = new Regex(@"window\.external((?:\.[A-Za-z_]\w*)+)(\s*\()?", RegexOptions.CultureInvariant);

		/// <summary>Adds a warning per synchronous property read of <c>window.external</c> in the text; <paramref name="firstLine"/> is the line of the text's first character.</summary>
		public static void Check(string text, int firstLine, ScanUnit unit)
		{
			foreach (Match m in External.Matches(text))
			{
				if (m.Groups[2].Success)
				{
					continue;
				}
				int lineNumber = firstLine;
				for (int i = 0; i < m.Index; i++)
				{
					if (text[i] == '\n')
					{
						lineNumber++;
					}
				}
				string symbol = "window.external" + m.Groups[1].Value;
				unit.AddFinding(new ScanFinding
				{
					Severity = "warning",
					Kind = "html-sync-property",
					Symbol = symbol,
					Line = lineNumber,
					Message = "Reads `" + symbol + "` as a property at line " + lineNumber + "; in a web panel on macOS the value comes from a snapshot taken when the page loads (method calls work).",
				});
			}
		}
	}
}
