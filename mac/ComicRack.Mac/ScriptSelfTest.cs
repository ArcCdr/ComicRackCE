using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;

namespace ComicRack.Mac
{
	/// <summary>
	/// <c>--script-selftest FILE</c> (hidden option, spike S4): runs the Python probe script FILE and some built-in checks of the
	/// scripting engine in the process that runs it (the dev build, or the signed bundle with the hardened runtime), prints one
	/// line per check (<c>PASS</c>, <c>FAIL</c> or <c>INFO</c>) and returns 0 if no line is a FAIL. The spike console project
	/// (<c>mac/spikes/S4-ironpython</c>) compiles this same file, so both processes run the same code.
	///
	/// Built-in checks: a smart-list expression through <see cref="PythonCommand.CompileExpression{T}"/> and through
	/// <see cref="ComicBookExpressionMatcher"/>, then every command that <see cref="PythonPluginInitializer"/> finds in
	/// <c>Sample.py</c> (next to FILE, or in the folder named by the variable COMICRACK_SCRIPTS_DIR) is initialized and the
	/// headless hooks ParseComicPath, CreateBookList, NetSearch, Books, ComicInfoHtml and ReaderResized are invoked.
	/// FILE itself is run as a script with the hook <c>Startup</c> and the method <c>selftest</c>; it prints its own lines.
	/// </summary>
	public static class ScriptSelfTest
	{
		public const string ScriptsDirVariable = "COMICRACK_SCRIPTS_DIR";

		/// <summary>
		/// Set to 1 to run a copy of Sample.py (and Sample.xml) whose two clr.AddReferenceByPartialName lines name the stand-in assemblies
		/// (cYo.Compat.Forms, cYo.Compat.Drawing). It simulates the best case of the identity question: the names that scripts use resolve.
		/// </summary>
		public const string RewriteRefsVariable = "COMICRACK_SELFTEST_REWRITE_REFS";

		/// <summary>Set to 1 to install the experimental assembly identity hook (see <see cref="InstallResolver"/>) before the scripts run.</summary>
		public const string ResolverVariable = "COMICRACK_SELFTEST_RESOLVER";

		private sealed class Report
		{
			private readonly TextWriter output;
			public int Failures;

			public Report(TextWriter output)
			{
				this.output = output;
			}

			public void Pass(string name, string detail = null) => Line("PASS", name, detail);

			public void Fail(string name, string detail = null)
			{
				Failures++;
				Line("FAIL", name, detail);
			}

			public void Info(string name, string detail = null) => Line("INFO", name, detail);

			public void Check(string name, bool ok, string detail = null)
			{
				if (ok)
				{
					Pass(name, detail);
				}
				else
				{
					Fail(name, detail);
				}
			}

			private void Line(string kind, string name, string detail)
			{
				output.WriteLine(kind + " " + name + (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail));
			}
		}

		public static int Run(string file, TextWriter output)
		{
			Report report = new Report(output);
			string full = Path.GetFullPath(file);
			report.Info("runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " " + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
			if (!File.Exists(full))
			{
				report.Fail("probe file", "not found: " + full);
				return 1;
			}
			Guarded(report, "expression", () => ExpressionChecks(report));
			string scriptsDir = Environment.GetEnvironmentVariable(ScriptsDirVariable);
			if (string.IsNullOrEmpty(scriptsDir))
			{
				scriptsDir = Path.GetDirectoryName(full);
			}
			if (Environment.GetEnvironmentVariable(ResolverVariable) == "1")
			{
				InstallResolver(report);
			}
			PluginEnvironment env = new PluginEnvironment(null, null, null, null, new Config(Path.Combine(scriptsDir, "Lib")), null, new ThemeStub());
			Guarded(report, "probe script", () => RunProbeScript(report, env, full));
			string sample = Path.Combine(scriptsDir, "Sample.py");
			if (File.Exists(sample))
			{
				Guarded(report, "Sample.py", () => SampleChecks(report, env, sample));
			}
			else
			{
				report.Info("Sample.py", "not found in " + scriptsDir + " (skipped)");
			}
			report.Info("result", report.Failures == 0 ? "all checks passed" : report.Failures + " failed");
			return report.Failures == 0 ? 0 : 1;
		}

		/// <summary>
		/// Experiment for the identity question (S6 decides): answer a failed load of the simple names System.Windows.Forms and
		/// System.Drawing.Common with the stand-in assemblies, so that clr.AddReference('System.Windows.Forms') works for scripts.
		/// </summary>
		private static void InstallResolver(Report report)
		{
			System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) =>
			{
				switch (name.Name)
				{
					case "System.Windows.Forms":
						return typeof(System.Windows.Forms.Control).Assembly;
					case "System.Drawing.Common":
						return typeof(System.Drawing.Bitmap).Assembly;
					default:
						return null;
				}
			};
			report.Info("resolver", "AssemblyLoadContext.Default.Resolving maps System.Windows.Forms and System.Drawing.Common to the stand-ins");
		}

		private static void Guarded(Report report, string name, Action body)
		{
			try
			{
				body();
			}
			catch (Exception e)
			{
				report.Fail(name, "exception " + Describe(e));
			}
		}

		private static string Describe(Exception e)
		{
			StringBuilder sb = new StringBuilder();
			for (Exception x = e; x != null; x = x.InnerException)
			{
				if (sb.Length > 0)
				{
					sb.Append(" <- ");
				}
				sb.Append(x.GetType().Name).Append(": ").Append(x.Message.Replace('\n', ' ').Replace('\r', ' '));
			}
			return sb.ToString();
		}

		private static ComicBook NewBook(string series, int year, string number)
		{
			return new ComicBook { Series = series, Year = year, Number = number };
		}

		private static void ExpressionChecks(Report report)
		{
			// The compiled form that ComicBookExpressionMatcher builds from "{Series} == 'Batman'".
			Func<ComicBook, IComicBookStatsProvider, bool> f = PythonCommand.CompileExpression<Func<ComicBook, IComicBookStatsProvider, bool>>(
				"__book.ShadowSeries == 'Batman' and __book.ShadowYear > 2000", "__book", "__bookStats");
			if (f == null)
			{
				report.Fail("CompileExpression", "returned null");
				return;
			}
			ComicBook hit = NewBook("Batman", 2011, "1");
			ComicBook missSeries = NewBook("Superman", 2011, "1");
			ComicBook missYear = NewBook("Batman", 1990, "1");
			report.Check("CompileExpression", f(hit, null) && !f(missSeries, null) && !f(missYear, null), "delegate gives true/false/false");
			// Python 2 integer division is kept (an IronPython 2.7 property, not .NET's).
			Func<int, int, object> div = PythonCommand.CompileExpression<Func<int, int, object>>("a / b", "a", "b");
			report.Check("Python 2 integer division", div != null && Convert.ToInt32(div(7, 2)) == 3, div == null ? "null" : "7 / 2 = " + div(7, 2));
			// A syntax error gives null (the matcher then reports an error list), not an exception.
			Func<int, object> bad = PythonCommand.CompileExpression<Func<int, object>>("a +* 2", "a");
			report.Check("syntax error", bad == null, "null delegate");

			try
			{
				cYo.Common.Reflection.DuckTyping.Implement<IDisposable>(new MemoryStream());
				report.Info("DuckTyping.Implement", "worked");
			}
			catch (Exception e)
			{
				report.Info("DuckTyping.Implement", "raised " + Describe(e) + " (the CodeDom generator is excluded on net10; no bundled script uses it)");
			}

			ComicBookExpressionMatcher matcher = new ComicBookExpressionMatcher { MatchValue = "{Series} == 'Batman' and {Year} > 2000" };
			ComicBook[] matched = matcher.Match(new[] { hit, missSeries, missYear }).ToArray();
			report.Check("ComicBookExpressionMatcher", matched.Length == 1 && matched[0] == hit, matched.Length + " of 3 books match (1 expected)");
		}

		private static void RunProbeScript(Report report, PluginEnvironment env, string file)
		{
			// Every command of the probe file (functions with a "#@Hook Startup" header) runs in a new engine and scope, so each one
			// sees a fresh set of clr references.
			Command[] commands = new PythonPluginInitializer().GetCommands(file).ToArray();
			report.Check("probe script commands", commands.Length > 0, commands.Length + " scenarios: " + string.Join(", ", commands.Select(c => ((PythonCommand)c).Method)));
			foreach (PythonCommand command in commands.Cast<PythonCommand>())
			{
				string scenario = command.Method;
				using (MemoryStream captured = new MemoryStream())
				{
					PythonCommand.Output = captured;
					try
					{
						if (!command.Initialize(env, Path.GetDirectoryName(file)))
						{
							report.Fail(scenario, "Initialize returned false");
							continue;
						}
						try
						{
							command.Invoke(new object[0]);
						}
						catch (Exception e)
						{
							report.Fail(scenario, "raised " + Describe(e));
						}
					}
					finally
					{
						PythonCommand.Output = null;
					}
					string text = Encoding.Default.GetString(captured.ToArray());
					foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
					{
						if (line.StartsWith("FAIL ", StringComparison.Ordinal))
						{
							report.Fail(scenario + ": " + line.Substring(5));
						}
						else if (line.StartsWith("PASS ", StringComparison.Ordinal))
						{
							report.Pass(scenario + ": " + line.Substring(5));
						}
						else
						{
							report.Info(scenario, line);
						}
					}
				}
			}
		}

		private static void SampleChecks(Report report, PluginEnvironment env, string sample)
		{
			if (Environment.GetEnvironmentVariable(RewriteRefsVariable) == "1")
			{
				string temp = Path.Combine(Path.GetTempPath(), "comicrack-s4-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(temp);
				foreach (string source in Directory.GetFiles(Path.GetDirectoryName(sample)))
				{
					string target = Path.Combine(temp, Path.GetFileName(source));
					if (source.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
					{
						File.WriteAllText(target, File.ReadAllText(source)
							.Replace("AddReferenceByPartialName(\"System.Windows.Forms\")", "AddReference(\"cYo.Compat.Forms\")")
							.Replace("AddReferenceByPartialName(\"System.Drawing\")", "AddReferenceByPartialName(\"System.Drawing\"); clr.AddReference(\"cYo.Compat.Drawing\")"));
					}
					else if (!source.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
					{
						File.Copy(source, target);
					}
				}
				report.Info("Sample.py", "run from a copy of the scripts folder with the two AddReferenceByPartialName lines rewritten to the stand-in names");
				sample = Path.Combine(temp, "Sample.py");
			}
			Command[] commands = new PythonPluginInitializer().GetCommands(sample).ToArray();
			report.Check("Sample.py commands found", commands.Length >= 10, commands.Length + " commands: " + string.Join(", ", commands.Select(c => ((PythonCommand)c).Method)));
			string xml = Path.ChangeExtension(sample, ".xml");
			if (File.Exists(xml))
			{
				// ParseComicPath and BookHasBeenOpened are declared in Sample.xml, not by comments in the script.
				Command[] fromXml = new XmlPluginInitializer().GetCommands(xml).ToArray();
				report.Check("Sample.xml commands found", fromXml.Length == 2, fromXml.Length + " commands");
				commands = commands.Concat(fromXml).ToArray();
			}
			Dictionary<string, Command> byMethod = new Dictionary<string, Command>();
			string dir = Path.GetDirectoryName(sample);
			foreach (PythonCommand c in commands.Cast<PythonCommand>())
			{
				// Command.Initialize also loads the command image; Sample.xml names none for these.
				bool ok = c.Initialize(env, dir);
				if (!ok)
				{
					report.Fail("initialize " + c.Method, "Initialize returned false");
					continue;
				}
				byMethod[c.Method] = c;
			}

			// Compiling Sample.py runs its top level: imports, `from System.Windows.Forms import *` and the class UIControl(Control).
			PythonCommand first = (PythonCommand)byMethod.Values.FirstOrDefault();
			if (first != null)
			{
				try
				{
					first.PreCompile(handleException: false);
					report.Pass("Sample.py compiles", "top level ran (imports, class UIControl(Control))");
				}
				catch (Exception e)
				{
					report.Fail("Sample.py compiles", Describe(e));
					return;
				}
			}

			Invoke(report, byMethod, "ParseComicPath", () =>
			{
				ComicNameInfo info = new ComicNameInfo { Series = "Batman", Volume = 3, Number = "7", Count = 12, Year = 2011 };
				PythonCommand c = (PythonCommand)byMethod["ParseComicPath"];
				c.Invoke(new object[] { "/comics/Batman 007.cbz", info });
				return info.Series == "-Batman" && info.Number == "1.5" && info.Count == 56 && info.Year == 2000 && info.Volume == 1
					? null : $"Series={info.Series} Number={info.Number} Count={info.Count} Year={info.Year} Volume={info.Volume}";
			});
			Invoke(report, byMethod, "GetBooksWith", () =>
			{
				ComicBook[] books = { NewBook("Alpha", 2000, "1"), NewBook("Beta", 2000, "2"), NewBook("Alpine", 2001, "3") };
				object result = byMethod["GetBooksWith"].Invoke(new object[] { books, "Al", "" });
				IEnumerable<ComicBook> list = result as IEnumerable<ComicBook>;
				int count = list == null ? -1 : list.Count();
				return count == 2 ? null : "returned " + (result == null ? "null" : result.GetType().Name) + " with " + count + " books";
			});
			Invoke(report, byMethod, "DummySearch", () =>
			{
				object general = byMethod["DummySearch"].Invoke(new object[] { "hint", "text", -1 });
				object three = byMethod["DummySearch"].Invoke(new object[] { "hint", "x", 4 });
				Dictionary<string, string> g = general as Dictionary<string, string>;
				Dictionary<string, string> t = three as Dictionary<string, string>;
				return g != null && g.Count == 1 && t != null && t.Count == 3 ? null : "dictionaries: " + (g?.Count.ToString() ?? "null") + "/" + (t?.Count.ToString() ?? "null");
			});
			Invoke(report, byMethod, "DummyHtmlInfoPanel", () =>
			{
				ComicBook[] books = { NewBook("Alpha", 2000, "1") };
				string html = byMethod["DummyHtmlInfoPanel"].Invoke(new object[] { books }) as string;
				return html != null && html.StartsWith("<table>", StringComparison.Ordinal) ? null : "html = " + html;
			});
			Invoke(report, byMethod, "WebLinkInfoPanel", () =>
			{
				ComicBook[] books = { new ComicBook { Series = "A", Web = "http://example.org/x" } };
				string r = byMethod["WebLinkInfoPanel"].Invoke(new object[] { books }) as string;
				return r == "!http://example.org/x" ? null : "result = " + r;
			});
			UiHooks(report, byMethod);
			AllScripts(report, env, dir, sample);
			Invoke(report, byMethod, "ComicRackUserForum", () =>
			{
				string r = byMethod["ComicRackUserForum"].Invoke(new object[] { new ComicBook[0] }) as string;
				return r != null && r.StartsWith("!http", StringComparison.Ordinal) ? null : "result = " + r;
			});
		}

		/// <summary>Information only: does the top level of every other bundled script run (imports, class definitions)?</summary>
		private static void AllScripts(Report report, PluginEnvironment env, string dir, string sample)
		{
			foreach (string file in Directory.GetFiles(dir, "*.py").OrderBy(f => f, StringComparer.Ordinal))
			{
				if (string.Equals(file, sample, StringComparison.Ordinal))
				{
					continue;
				}
				PythonCommand[] found = new PythonPluginInitializer().GetCommands(file).Cast<PythonCommand>().ToArray();
				if (found.Length == 0)
				{
					report.Info("script " + Path.GetFileName(file), "no command with a hook comment");
					continue;
				}
				try
				{
					if (!found[0].Initialize(env, dir))
					{
						report.Info("script " + Path.GetFileName(file), "Initialize returned false");
						continue;
					}
					found[0].PreCompile(handleException: false);
					report.Info("script " + Path.GetFileName(file), "top level runs; " + found.Length + " commands: " + string.Join(", ", found.Select(c => c.Method + "[" + c.Hook + "]")));
				}
				catch (Exception e)
				{
					report.Info("script " + Path.GetFileName(file), "top level raised " + Describe(e));
				}
			}
		}

		/// <summary>Hooks that touch the UI: the outcome is information for the Script UI layer (P4.10, P4.11), never a failure of this spike.</summary>
		private static void UiHooks(Report report, Dictionary<string, Command> byMethod)
		{
			Action<string, Func<object>> run = (method, call) =>
			{
				if (!byMethod.ContainsKey(method))
				{
					return;
				}
				try
				{
					object r = call();
					report.Info("ui hook " + method + " (" + byMethod[method].Hook + ")", "returned " + (r == null ? "null" : r.GetType().FullName));
				}
				catch (Exception e)
				{
					report.Info("ui hook " + method + " (" + byMethod[method].Hook + ")", "raised " + Describe(e));
				}
			};
			run("DummyUIInfoPanel", () => byMethod["DummyUIInfoPanel"].Invoke(new object[0]));
			run("BookHasBeenOpened", () => byMethod["BookHasBeenOpened"].Invoke(new object[] { NewBook("Alpha", 2000, "1") }));
			run("ShowStartupMessage", () => byMethod["ShowStartupMessage"].Invoke(new object[0]));
			run("FadeReadThumbnails", () =>
			{
				using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(32, 32))
				using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bitmap))
				{
					return byMethod["FadeReadThumbnails"].Invoke(new object[] { new ComicBook { Series = "A", LastPageRead = 20, PageCount = 21 }, g, new System.Drawing.Rectangle(0, 0, 32, 32), 0 });
				}
			});
		}

		private static void Invoke(Report report, Dictionary<string, Command> byMethod, string method, Func<string> body)
		{
			if (!byMethod.ContainsKey(method))
			{
				report.Fail("hook " + method, "no such command in Sample.py");
				return;
			}
			try
			{
				string problem = body();
				if (problem == null)
				{
					report.Pass("hook " + method + " (" + byMethod[method].Hook + ")");
				}
				else
				{
					report.Fail("hook " + method + " (" + byMethod[method].Hook + ")", problem);
				}
			}
			catch (Exception e)
			{
				report.Fail("hook " + method, Describe(e));
			}
		}

		private sealed class Config : IPluginConfig
		{
			private readonly string[] paths;

			public Config(params string[] paths)
			{
				this.paths = paths;
			}

			public IEnumerable<string> LibraryPaths => paths.Where(Directory.Exists);
		}

		private sealed class ThemeStub : cYo.Projects.ComicRack.Plugins.Theme.IThemePlugin
		{
			public cYo.Common.Windows.Forms.Theme.Themes CurrentTheme => cYo.Common.Windows.Forms.Theme.Themes.Default;

			public bool IsDarkModeEnabled => false;

			public System.Windows.Forms.ToolStripRenderer ToolStripRenderer => null;

			public cYo.Common.Windows.Forms.Theme.UIComponent UIComponent => cYo.Common.Windows.Forms.Theme.UIComponent.None;

			public void ApplyTheme(System.Windows.Forms.Control control)
			{
			}
		}
	}
}
