using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Host.Scripting;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>P4.5: a script command runs on a worker thread and can be cancelled; the script output window's text (<see cref="ScriptConsoleLog"/>).</summary>
	public class ScriptCommandRunnerTests
	{
		private const string Commands = @"
import clr
import time
import System
from System.Threading import Thread

#@Name Edit books
#@Key EditBooks
#@Hook Books
def EditBooks(books):
	print 'worker', Thread.CurrentThread.IsThreadPoolThread
	for b in books:
		b.SetCustomValue('p45.edited', b.Series)

#@Name Fails
#@Key Fails
#@Hook Books
def Fails(books):
	raise ValueError('the script said no')

#@Name Endless
#@Key Endless
#@Hook Books
def Endless(books):
	try:
		while True:
			print 'tick'
			time.sleep(0.005)
	finally:
		books[0].SetCustomValue('p45.finished', 'yes')

#@Name Silent
#@Key Silent
#@Hook Books
def Silent(books):
	time.sleep(0.2)
	books[0].SetCustomValue('p45.silent', 'done')
";

		private static async Task<T> Within<T>(Task<T> task, string what)
		{
			if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30))) != task)
			{
				throw new TimeoutException("Not finished: " + what);
			}
			return await task;
		}

		private static async Task WaitFor(Func<bool> condition, string what)
		{
			var watch = Stopwatch.StartNew();
			while (!condition())
			{
				if (watch.Elapsed > TimeSpan.FromSeconds(30))
				{
					throw new TimeoutException("Not reached: " + what);
				}
				await Task.Delay(5);
			}
		}

		private static MacHost OpenWith(TempFolder temp, ScriptConsoleLog log, out Command[] commands)
		{
			string dir = ScriptTestSupport.WriteScripts(temp, "scripts", ("p45.py", Commands));
			MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir);
			host.Scripts.AttachConsole(log, log: true);
			commands = host.Scripts.GetCommands(PluginEngine.ScriptTypeBooks).ToArray();
			return host;
		}

		private static Command By(Command[] commands, string key) => commands.Single(c => c.Key == key);

		[Fact]
		public async Task A_command_runs_on_a_worker_thread_with_the_books_and_its_output_goes_to_the_console()
		{
			using (var temp = new TempFolder())
			{
				var log = new ScriptConsoleLog();
				using (MacHost host = OpenWith(temp, log, out Command[] commands))
				{
					ComicBook a = ScriptTestSupport.Book("Alpha", "1"), b = ScriptTestSupport.Book("Beta", "2");
					ScriptRunResult result = await Within(ScriptCommandRunner.RunAsync(By(commands, "EditBooks"), new object[] { new[] { a, b } }), "the command");
					Assert.Equal(ScriptRunOutcome.Completed, result.Outcome);
					Assert.Null(result.Error);
					Assert.False(result.Detached);
					Assert.Equal("Alpha", a.GetCustomValue("p45.edited"));
					Assert.Equal("Beta", b.GetCustomValue("p45.edited"));
					Assert.Contains("worker True", log.Text);
					Assert.Contains("Calling 'EditBooks'", log.Text); // the log of the commands (EnableLog) is in the output too
				}
			}
		}

		[Fact]
		public async Task A_failing_command_reports_the_error_of_the_script_not_the_reflection_call()
		{
			using (var temp = new TempFolder())
			{
				var log = new ScriptConsoleLog();
				using (MacHost host = OpenWith(temp, log, out Command[] commands))
				{
					ScriptRunResult result = await Within(ScriptCommandRunner.RunAsync(By(commands, "Fails"), new object[] { new[] { ScriptTestSupport.Book("A", "1") } }), "the command");
					Assert.Equal(ScriptRunOutcome.Failed, result.Outcome);
					Assert.Contains("the script said no", result.Message);
					Assert.IsNotType<System.Reflection.TargetInvocationException>(result.Error);
					Assert.Contains("the script said no", log.Text);
				}
			}
		}

		[Fact]
		public async Task Cancelling_gives_the_result_at_once_and_the_script_stops_at_its_next_print()
		{
			using (var temp = new TempFolder())
			{
				var log = new ScriptConsoleLog();
				using (MacHost host = OpenWith(temp, log, out Command[] commands))
				{
					ComicBook book = ScriptTestSupport.Book("Alpha", "1");
					var cts = new CancellationTokenSource();
					Task<ScriptRunResult> run = ScriptCommandRunner.RunAsync(By(commands, "Endless"), new object[] { new[] { book } }, cts.Token);
					await WaitFor(() => log.Text.Contains("tick"), "the script is running");
					Assert.False(run.IsCompleted);
					cts.Cancel();
					ScriptRunResult result = await Within(run, "the cancelled run");
					Assert.Equal(ScriptRunOutcome.Cancelled, result.Outcome);
					Assert.True(result.Detached, "the script was still going when the result was given");
					// the next print raises OperationCanceledException inside the script, whose finally block runs
					await WaitFor(() => book.GetCustomValue("p45.finished") == "yes", "the script stopped at its next print");
					int length = log.Length;
					await Task.Delay(100);
					Assert.Equal(length, log.Length); // and it prints no more
				}
			}
		}

		[Fact]
		public async Task A_script_that_never_prints_cannot_be_stopped_but_a_run_that_is_cancelled_before_it_starts_does_not_start()
		{
			using (var temp = new TempFolder())
			{
				var log = new ScriptConsoleLog();
				using (MacHost host = OpenWith(temp, log, out Command[] commands))
				{
					ComicBook book = ScriptTestSupport.Book("Alpha", "1");
					var cts = new CancellationTokenSource();
					Task<ScriptRunResult> run = ScriptCommandRunner.RunAsync(By(commands, "Silent"), new object[] { new[] { book } }, cts.Token);
					await Task.Delay(50);
					cts.Cancel();
					ScriptRunResult result = await Within(run, "the cancelled run");
					Assert.Equal(ScriptRunOutcome.Cancelled, result.Outcome);
					Assert.True(result.Detached);
					await WaitFor(() => book.GetCustomValue("p45.silent") == "done", "the silent script ends by itself");

					var already = new CancellationTokenSource();
					already.Cancel();
					ComicBook other = ScriptTestSupport.Book("Beta", "1");
					ScriptRunResult none = await Within(ScriptCommandRunner.RunAsync(By(commands, "EditBooks"), new object[] { new[] { other } }, already.Token), "the run that never started");
					Assert.Equal(ScriptRunOutcome.Cancelled, none.Outcome);
					await Task.Delay(100);
					Assert.Null(other.GetCustomValue("p45.edited"));
				}
			}
		}

		[Fact]
		public void The_console_log_collects_text_from_any_thread_and_keeps_the_last_million_characters()
		{
			var log = new ScriptConsoleLog();
			int changes = 0;
			log.Changed += (sender, e) => Interlocked.Increment(ref changes);
			long before = log.Version;
			log.WriteLine("one");
			log.Write("two");
			Assert.Equal("one" + Environment.NewLine + "two", log.Text);
			Assert.True(changes >= 2); // a line is written as its text and its line end
			Assert.True(log.Version > before);

			Parallel.For(0, 8, i =>
			{
				for (int n = 0; n < 50; n++)
				{
					log.WriteLine("worker " + i);
				}
			});
			Assert.Equal(8 * 50, log.Text.Split(new[] { "worker " }, StringSplitOptions.None).Length - 1);

			log.Clear();
			Assert.Equal(string.Empty, log.Text);
			log.Write(new string('a', ScriptConsoleLog.MaxLength - 2));
			log.Write("bcdef");
			Assert.Equal(ScriptConsoleLog.MaxLength, log.Length);
			Assert.EndsWith("bcdef", log.Text);
			Assert.StartsWith("aaa", log.Text);
		}
	}
}
