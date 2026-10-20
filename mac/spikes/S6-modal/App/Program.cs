using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using S6.Backend;
using S6.Cases;
using S6.Host;
using SWF = System.Windows.Forms;

namespace S6
{
	internal sealed class Options
	{
		public bool Real;
		public bool Identity;
		public string Part = "all";
		public bool ShutdownCase;
		public bool Interactive;
		public bool Auto;
		public bool UnhandledCase;
		public bool UnhandledHandler;
		public bool List;
		public string Cases = "all";
		public string Out;
		public string ScriptsDir;
		public string ResolverDir;
		public List<string> Plugins = new List<string>();
		public string Error;

		public static Options Parse(string[] args)
		{
			Options o = new Options();
			string root = FindRoot();
			o.ScriptsDir = root == null ? null : Path.Combine(root, "ComicRack", "Output", "Scripts");
			o.Out = Path.Combine(Path.GetTempPath(), "s6-out");
			for (int i = 0; i < args.Length; i++)
			{
				switch (args[i])
				{
					case "--real": o.Real = true; break;
					case "--headless": o.Real = false; break;
					case "--identity": o.Identity = true; break;
					case "--part": o.Part = args[++i]; break;
					case "--shutdown-case": o.ShutdownCase = true; break;
					case "--interactive": o.Interactive = true; o.Real = true; break;
					case "--auto": o.Auto = true; break;
					case "--unhandled-case": o.UnhandledCase = true; break;
					case "--with-handler": o.UnhandledHandler = true; break;
					case "--list": o.List = true; break;
					case "--cases": o.Cases = args[++i]; break;
					case "--out": o.Out = args[++i]; break;
					case "--scripts": o.ScriptsDir = args[++i]; break;
					case "--plugin": o.Plugins.Add(args[++i]); break;
					case "--resolver": o.ResolverDir = args[++i]; break;
					case "--data-dir": o.Out = Path.Combine(args[++i], "s6-out"); break; // ui-drive passes it
					default: o.Error = "unknown option " + args[i]; break;
				}
			}
			return o;
		}

		private static string FindRoot()
		{
			for (string d = AppContext.BaseDirectory; d != null; d = Path.GetDirectoryName(d))
			{
				if (Directory.Exists(Path.Combine(d, "ComicRack", "Output", "Scripts")))
				{
					return d;
				}
			}
			return Environment.GetEnvironmentVariable("COMICRACK_REPO");
		}
	}

	internal static class Program
	{
		public static Options Opts;
		public static int ExitCode;

		[STAThread]
		private static int Main(string[] args)
		{
			Opts = Options.Parse(args);
			if (Opts.Error != null)
			{
				Console.Error.WriteLine(Opts.Error);
				Console.Error.WriteLine("Usage: S6-modal [--headless|--real] [--cases LIST|all] [--out DIR] [--scripts DIR] [--identity [--plugin FILE]...] [--shutdown-case] [--interactive] [--list]");
				return 2;
			}
			Directory.CreateDirectory(Opts.Out);
			if (Opts.List)
			{
				foreach ((CaseAttribute a, MethodInfo m) in Discover())
				{
					Console.WriteLine(a.Id + "\t" + a.Title);
				}
				return 0;
			}
			if (Opts.Identity)
			{
				Report report = new Report(Console.Out);
				if (Opts.ResolverDir != null)
				{
					// The facade is not next to the host: an AssemblyLoadContext.Default.Resolving handler finds it by its simple name (identity option "Resolving hook").
					string dir = Opts.ResolverDir;
					System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) =>
					{
						string file = Path.Combine(dir, name.Name + ".dll");
						Console.WriteLine("INFO resolving hook asked for " + name.FullName + (File.Exists(file) ? " -> " + file : " -> not in " + dir));
						return File.Exists(file) ? context.LoadFromAssemblyPath(file) : null;
					};
				}
				IdentityCases.Run(report, Opts.Plugins, Opts.Part);
				report.Save(Path.Combine(Opts.Out, "identity.txt"));
				Console.WriteLine("RESULT " + (report.Failures == 0 ? "no failures" : report.Failures + " failed"));
				return report.Failures == 0 ? 0 : 1;
			}
			try
			{
				AppBuilder builder = AppBuilder.Configure<S6App>();
				builder = Opts.Real
					? builder.UseAvaloniaNative().UseSkia().UseHarfBuzz()
					: builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
				builder.StartWithClassicDesktopLifetime(args);
				return ExitCode;
			}
			catch (InvalidOperationException ex) when (ex.Message.Contains("RenderTimer"))
			{
				Console.Error.WriteLine("No usable display: " + ex.Message);
				return 4;
			}
		}

		public static IEnumerable<(CaseAttribute, MethodInfo)> Discover()
		{
			List<(CaseAttribute, MethodInfo)> all = new List<(CaseAttribute, MethodInfo)>();
			foreach (Type type in new[] { typeof(ModalCases), typeof(ScriptCases) })
			{
				foreach (MethodInfo m in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
				{
					CaseAttribute a = m.GetCustomAttribute<CaseAttribute>();
					if (a != null)
					{
						all.Add((a, m));
					}
				}
			}
			return all.OrderBy(x => x.Item1.Id, StringComparer.Ordinal);
		}
	}

	public sealed class S6App : Application
	{
		public override void Initialize()
		{
			Styles.Add(new FluentTheme());
		}

		public override void OnFrameworkInitializationCompleted()
		{
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
				Dispatcher.UIThread.Post(() => Task.Run(() => Runner.Run(desktop)));
			}
			base.OnFrameworkInitializationCompleted();
		}
	}

	internal static class Runner
	{
		public static async Task Run(IClassicDesktopStyleApplicationLifetime desktop)
		{
			Options o = Program.Opts;
			Report report = new Report(Console.Out);
			int code = 1;
			Thread watchdog = new Thread(() =>
			{
				Thread.Sleep(TimeSpan.FromMinutes(o.Interactive ? 10 : 5));
				Console.WriteLine("WATCHDOG the run did not finish in time");
				Environment.Exit(9);
			})
			{ IsBackground = true };
			watchdog.Start();
			try
			{
				AvaloniaBackend backend = new AvaloniaBackend();
				SWF.FormsRuntime.Backend = backend;
				string spikeScripts = Path.Combine(AppContext.BaseDirectory, "scripts");
				Ctx ctx = new Ctx(report, backend, o.Real, o.Out, o.ScriptsDir, spikeScripts);
				Console.WriteLine("S6 " + (o.Real ? "real window server" : "headless") + ", runtime " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + ", facade " + typeof(SWF.Form).Assembly.GetName().FullName);
				await ctx.Initialize();
				if (o.ShutdownCase)
				{
					await ShutdownCase.Run(ctx, desktop);
					return;
				}
				if (o.UnhandledCase)
				{
					await UnhandledCase.Run(ctx, desktop, o.UnhandledHandler);
					return;
				}
				if (o.Interactive)
				{
					await Interactive.Run(ctx, o.Auto);
					code = 0;
					return;
				}
				foreach ((CaseAttribute a, MethodInfo m) in Program.Discover())
				{
					if (o.Cases != "all" && !o.Cases.Split(',').Any(sel => a.Id.StartsWith(sel, StringComparison.OrdinalIgnoreCase)))
					{
						continue;
					}
					report.Case = a.Id;
					report.Info("case", a.Title);
					try
					{
						Task task = (Task)m.Invoke(null, new object[] { ctx });
						Task done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)));
						if (done != task)
						{
							throw new TimeoutException("the case did not finish in 60 s");
						}
						await task;
					}
					catch (Exception e)
					{
						Exception inner = e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
						report.Line("FAIL", "case aborted", Ctx.Describe(inner));
					}
					try
					{
						await ctx.Cleanup();
					}
					catch (Exception e)
					{
						report.Info("cleanup", Ctx.Describe(e));
					}
					report.Case = string.Empty;
				}
				string openLeft = string.Join(", ", SWF.FormsRuntime.OpenForms.Where(f => !ReferenceEquals(f, ctx.MainForm)).Select(f => f.Name));
				report.Check("no dialog is left open at the end", openLeft.Length == 0, openLeft);
				report.Save(Path.Combine(o.Out, "report.txt"));
				Console.WriteLine("RESULT " + (report.Failures == 0 ? "no failures" : report.Failures + " failed") + ", " + report.Passes + " checks passed");
				code = report.Failures == 0 ? 0 : 1;
			}
			catch (Exception e)
			{
				Console.WriteLine("FAIL runner " + Ctx.Describe(e));
				code = 1;
			}
			finally
			{
				Program.ExitCode = code;
				if (!o.ShutdownCase && !o.UnhandledCase)
				{
					Dispatcher.UIThread.Post(() => desktop.Shutdown(code));
				}
			}
		}
	}
}
