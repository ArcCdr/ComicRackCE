using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace ComicRack.Characterization
{
	/// <summary>
	/// The committed goldens (<c>Golden/</c> next to the sources). <c>UPDATE_GOLDENS=1 dotnet test mac/tests/ComicRack.Characterization</c>
	/// rewrites every golden that differs and prints a diff of what changed; review it before committing. Without the switch a difference fails the test
	/// and the message shows the first lines that differ.
	/// </summary>
	internal static class Goldens
	{
		public const string UpdateVariable = "UPDATE_GOLDENS";

		public static bool Update => Environment.GetEnvironmentVariable(UpdateVariable) == "1";

		/// <summary>mac/tests/ComicRack.Characterization/Golden in the source tree (found from the test assembly's folder).</summary>
		public static string Dir
		{
			get
			{
				string dir = AppContext.BaseDirectory;
				while (!string.IsNullOrEmpty(dir))
				{
					string candidate = Path.Combine(dir, "mac", "tests", "ComicRack.Characterization", "Golden");
					if (Directory.Exists(candidate))
					{
						return candidate;
					}
					dir = Path.GetDirectoryName(dir);
				}
				throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory);
			}
		}

		public static string RepoRoot => Path.GetFullPath(Path.Combine(Dir, "..", "..", "..", ".."));

		public static string PathOf(string name) => Path.Combine(Dir, name);

		public static string Read(string name) => Normalize(File.ReadAllText(PathOf(name), Encoding.UTF8));

		/// <summary>Line endings as LF and exactly one final newline: what the goldens contain.</summary>
		public static string Normalize(string text)
		{
			return text.Replace("\r\n", "\n").TrimEnd('\n') + "\n";
		}

		/// <summary>
		/// Compares <paramref name="actual"/> with the golden <paramref name="name"/>. With UPDATE_GOLDENS=1 a golden that differs (or is missing) is
		/// written and the diff is reported through <paramref name="log"/> and the error stream instead of failing.
		/// </summary>
		public static void Check(string name, string actual, Action<string> log = null)
		{
			actual = Normalize(actual);
			string file = PathOf(name);
			string expected = File.Exists(file) ? Read(name) : null;
			if (expected == actual)
			{
				return;
			}
			string diff = Diff(expected, actual);
			if (Update)
			{
				File.WriteAllText(file, actual, new UTF8Encoding(false));
				string message = $"UPDATE_GOLDENS: rewrote Golden/{name}\n{diff}";
				log?.Invoke(message);
				Console.Error.WriteLine(message);
				return;
			}
			Assert.Fail($"Golden/{name} differs from the actual result (full actual text: {SaveActual(name, actual)}). Review it, then run with {UpdateVariable}=1 to accept.\n{diff}");
		}

		/// <summary>Writes the actual text to mac/artifacts/golden/ (git-ignored) so that it can be compared with the golden by hand; returns the path.</summary>
		public static string SaveActual(string name, string actual)
		{
			string dir = Path.Combine(RepoRoot, "mac", "artifacts", "golden");
			Directory.CreateDirectory(dir);
			string file = Path.Combine(dir, name + ".actual");
			File.WriteAllText(file, Normalize(actual), new UTF8Encoding(false));
			return file;
		}

		/// <summary>The first lines that differ (common head and tail removed), as - expected / + actual.</summary>
		public static string Diff(string expected, string actual, int maxLines = 40)
		{
			if (expected == null)
			{
				return "(new golden, " + actual.Split('\n').Length + " lines)";
			}
			string[] a = expected.Split('\n');
			string[] b = actual.Split('\n');
			int head = 0;
			while (head < a.Length && head < b.Length && a[head] == b[head])
			{
				head++;
			}
			int tail = 0;
			while (tail < a.Length - head && tail < b.Length - head && a[a.Length - 1 - tail] == b[b.Length - 1 - tail])
			{
				tail++;
			}
			var sb = new StringBuilder();
			sb.Append("@@ line ").Append(head + 1).Append(" @@\n");
			int shown = 0;
			foreach (string line in a.Skip(head).Take(a.Length - head - tail))
			{
				if (shown++ >= maxLines)
				{
					sb.Append("- ...\n");
					break;
				}
				sb.Append("- ").Append(Clip(line)).Append('\n');
			}
			shown = 0;
			foreach (string line in b.Skip(head).Take(b.Length - head - tail))
			{
				if (shown++ >= maxLines)
				{
					sb.Append("+ ...\n");
					break;
				}
				sb.Append("+ ").Append(Clip(line)).Append('\n');
			}
			return sb.ToString();
		}

		private static string Clip(string line) => line.Length > 200 ? line.Substring(0, 200) + "..." : line;
	}

	/// <summary>Canonical XML: attributes sorted by name, whitespace-only text dropped, one node per line with tab indentation, LF line ends.</summary>
	internal static class CanonicalXml
	{
		public static string FromFile(string file, params string[] dropElements) => FromText(File.ReadAllText(file, Encoding.UTF8), dropElements);

		/// <param name="xml">The document.</param>
		/// <param name="dropElements">Names of elements to leave out wherever they occur (values of the run, such as a time stamp of "now").</param>
		public static string FromText(string xml, params string[] dropElements)
		{
			XDocument doc = XDocument.Parse(xml, LoadOptions.None);
			if (dropElements.Length > 0)
			{
				doc.Descendants().Where(e => dropElements.Contains(e.Name.LocalName)).ToList().ForEach(e => e.Remove());
			}
			XElement root = Canonicalize(doc.Root);
			var sb = new StringBuilder();
			var settings = new XmlWriterSettings
			{
				Indent = true,
				IndentChars = "\t",
				NewLineChars = "\n",
				NewLineHandling = NewLineHandling.Replace,
				OmitXmlDeclaration = true,
				Encoding = new UTF8Encoding(false),
			};
			using (var writer = XmlWriter.Create(sb, settings))
			{
				new XDocument(root).Save(writer);
			}
			return sb.ToString();
		}

		private static XElement Canonicalize(XElement e)
		{
			var copy = new XElement(e.Name);
			foreach (XAttribute a in e.Attributes().OrderBy(x => x.Name.ToString(), StringComparer.Ordinal))
			{
				copy.SetAttributeValue(a.Name, a.Value);
			}
			bool hasChildren = e.Elements().Any();
			foreach (XNode node in e.Nodes())
			{
				if (node is XElement child)
				{
					copy.Add(Canonicalize(child));
				}
				else if (node is XText text && !(hasChildren && string.IsNullOrWhiteSpace(text.Value)))
				{
					copy.Add(new XText(text.Value.Replace("\r\n", "\n")));
				}
			}
			return copy;
		}
	}
}
