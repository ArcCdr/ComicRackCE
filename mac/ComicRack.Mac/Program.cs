using System;
using Avalonia;
using Avalonia.Headless;

namespace ComicRack.Mac
{
	internal static class Program
	{
		[STAThread]
		public static int Main(string[] args)
		{
			AppOptions options = AppOptions.Parse(args);
			if (options.Error != null)
			{
				Console.Error.WriteLine(options.Error);
				Console.Error.WriteLine("Usage: ComicRack.Mac [--smoke-test[=SECONDS]] [--headless] [--data-dir DIR] [--read-book FILE] [--language CULTURE] [--theme light|dark]");
				return 2;
			}
			if (options.ScriptSelfTest != null)
			{
				if (!ScriptSelfTest.IsEnabled)
				{
					Console.Error.WriteLine("--script-selftest is a developer option and is off in this build. Set " + ScriptSelfTest.EnableVariable + "=1 to use it.");
					return 2;
				}
				return ScriptSelfTest.Run(options.ScriptSelfTest, Console.Out);
			}
			App.StartupOptions = options;
			try
			{
				return BuildAvaloniaApp(options).StartWithClassicDesktopLifetime(args);
			}
			catch (InvalidOperationException ex) when (ex.Message.Contains("RenderTimer"))
			{
				// Avalonia.Native cannot start its display link when no display is usable (screen locked or asleep, no window server session).
				Console.Error.WriteLine("No usable display: " + ex.Message);
				Console.Error.WriteLine("Unlock the screen, or run with --headless for a check without a window server.");
				return 4;
			}
		}

		public static AppBuilder BuildAvaloniaApp(AppOptions options)
		{
			AppBuilder builder = AppBuilder.Configure<App>();
			if (options.Headless)
			{
				return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
			}
			return builder
				.UseAvaloniaNative()
				.UseSkia()
				.UseHarfBuzz()
				.LogToTrace();
		}
	}
}
