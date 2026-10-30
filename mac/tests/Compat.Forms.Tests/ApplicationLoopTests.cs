using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using cYo.Compat.Forms;
using Xunit;

namespace Compat.Forms.Tests
{
	/// <summary>
	/// Application.Run, Exit, ExitThread, MessageLoop, OpenForms and the events around them, and ApplicationContext and FormCollection (P4.9f). The loop is the one of the null backend:
	/// Run shows the main form and pumps until the context ends; the test ends it from a handler of the form or from a posted action, never by waiting.
	/// </summary>
	[Collection("Forms runtime")]
	public sealed class ApplicationLoopTests : IDisposable
	{
		private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

		private readonly NullFormsBackend backend;
		private readonly Trace trace = new Trace();

		public ApplicationLoopTests()
		{
			FormsRuntime.Reset();
			backend = new NullFormsBackend { ThreadAffinity = true };
			backend.AdoptCurrentThread();
			FormsRuntime.Backend = backend;
		}

		public void Dispose()
		{
			Application.ApplicationExit -= appExit;
			Application.ThreadExit -= threadExit;
			FormsRuntime.Reset();
		}

		private EventHandler appExit;
		private EventHandler threadExit;

		private void WatchApplication()
		{
			appExit = (s, e) => trace.Add("Application.ApplicationExit");
			threadExit = (s, e) => trace.Add("Application.ThreadExit");
			Application.ApplicationExit += appExit;
			Application.ThreadExit += threadExit;
		}

		// ---------------------------------------------------------------- Run

		[Fact]
		public void Run_shows_the_main_form_runs_until_it_closes_and_ends_with_ThreadExit_then_ApplicationExit()
		{
			WatchApplication();
			var main = new Form { Name = "main" };
			trace.Watch(main, "main");
			bool loopInside = false;
			bool listed = false;
			main.Shown += (s, e) =>
			{
				loopInside = Application.MessageLoop;
				listed = Application.OpenForms.Cast<Form>().Contains(main);
				main.Close();
			};
			Assert.False(Application.MessageLoop);
			Application.Run(main);
			Assert.True(loopInside);
			Assert.True(listed);
			Assert.False(Application.MessageLoop);
			Assert.True(main.IsDisposed);
			Assert.Empty(Application.OpenForms);
			Assert.Equal(new[]
			{
				"main.Load", "main.Activated", "main.Shown", "main.Closing", Reason("main", "FormClosing"), "main.Closed", Reason("main", "FormClosed"), "main.Deactivate",
				"Application.ThreadExit", "Application.ApplicationExit"
			}, trace.Entries);
		}

		private static string Reason(string name, string ev) => name + "." + ev + "(UserClosing)";

		[Fact]
		public void A_main_form_that_cancels_its_closing_keeps_the_loop_running()
		{
			var main = new Form { Name = "main" };
			int tries = 0;
			main.FormClosing += (s, e) => e.Cancel = ++tries == 1;
			main.Shown += (s, e) =>
			{
				main.Close();
				trace.Add("still running " + Application.MessageLoop + ", disposed " + main.IsDisposed);
				backend.Post(main.Close);
			};
			Application.Run(main);
			Assert.Equal(2, tries);
			Assert.Equal(new[] { "still running True, disposed False" }, trace.Entries);
			Assert.True(main.IsDisposed);
		}

		[Fact]
		public void Run_with_a_context_ends_when_the_context_ends_and_disposes_the_main_form()
		{
			var main = new Form { Name = "main" };
			var context = new ApplicationContext(main);
			context.ThreadExit += (s, e) => trace.Add("context.ThreadExit");
			Assert.Same(main, context.MainForm);
			main.Shown += (s, e) =>
			{
				trace.Add("shown");
				context.ExitThread();
			};
			Application.Run(context);
			Assert.Equal(new[] { "shown", "context.ThreadExit" }, trace.Entries);
			Assert.True(main.IsDisposed);
			Assert.Null(context.MainForm);
		}

		[Fact]
		public void A_context_can_decide_that_the_closing_of_its_main_form_does_not_end_the_loop()
		{
			var main = new Form { Name = "main" };
			var context = new KeepRunning(main);
			main.Shown += (s, e) =>
			{
				main.Close();
				trace.Add("closed, loop " + Application.MessageLoop);
				backend.Post(context.ExitThread);
			};
			Application.Run(context);
			// the form closed, the context was told, and the loop went on until the context was asked to end
			Assert.Equal(new[] { "closed, loop True" }, trace.Entries);
			Assert.Equal(1, context.Closed);
			Assert.True(main.IsDisposed);
		}

		private sealed class KeepRunning : ApplicationContext
		{
			public KeepRunning(Form main)
				: base(main)
			{
			}

			public int Closed;

			protected override void OnMainFormClosed(object sender, EventArgs e)
			{
				Closed++;
			}
		}

		[Fact]
		public void Run_without_a_form_runs_until_Exit()
		{
			WatchApplication();
			bool inside = false;
			backend.Post(() =>
			{
				inside = Application.MessageLoop;
				Application.Exit();
			});
			Application.Run();
			Assert.True(inside);
			Assert.Equal(new[] { "Application.ThreadExit", "Application.ApplicationExit" }, trace.Entries);
		}

		[Fact]
		public void A_second_Run_inside_a_loop_is_refused()
		{
			Exception seen = null;
			backend.Post(() =>
			{
				try
				{
					Application.Run(new Form());
				}
				catch (Exception e)
				{
					seen = e;
				}
				Application.ExitThread();
			});
			Application.Run();
			Assert.IsType<InvalidOperationException>(seen);
			Assert.Contains("second message loop", seen.Message);
			// and the loop of the first Run is gone, so Run works again
			backend.Post(Application.ExitThread);
			Application.Run();
		}

		[Fact]
		public void On_a_backend_without_a_loop_Run_ends_at_once_and_disposes_the_form()
		{
			FormsRuntime.Backend = new NullFormsBackend();
			var main = new Form();
			Application.Run(main);
			Assert.True(main.IsDisposed);
			Application.Run();
		}

		[Fact]
		public void Run_from_a_script_thread_blocks_it_runs_the_loop_on_the_ui_thread_and_returns_when_the_form_closes()
		{
			backend.ModalExitsWhenIdle = false;
			int ui = Environment.CurrentManagedThreadId;
			var main = new Form { Name = "main" };
			int shownThread = -1;
			int workerThread = -1;
			var returned = new ManualResetEventSlim(false);
			bool returnedInShown = true;
			main.Shown += (s, e) =>
			{
				shownThread = Environment.CurrentManagedThreadId;
				returnedInShown = returned.IsSet;
				main.Close();
			};
			var pump = new ModalLoop();
			Exception error = null;
			var script = new Thread(() =>
			{
				try
				{
					workerThread = Environment.CurrentManagedThreadId;
					Application.Run(main);
					returned.Set();
				}
				catch (Exception e)
				{
					error = e;
				}
				finally
				{
					pump.Exit();
				}
			})
			{ IsBackground = true };
			using (new System.Threading.Timer(_ => pump.Exit(), null, Deadline, Timeout.InfiniteTimeSpan))
			{
				script.Start();
				backend.RunModal(pump);
			}
			Assert.True(script.Join(Deadline));
			Assert.Null(error);
			Assert.True(returned.IsSet);
			Assert.False(returnedInShown);
			Assert.Equal(ui, shownThread);
			Assert.NotEqual(ui, workerThread);
			Assert.True(main.IsDisposed);
		}

		// ---------------------------------------------------------------- Exit and ExitThread

		[Fact]
		public void Exit_asks_every_open_form_in_the_order_they_were_shown_and_closes_them_without_disposing_them()
		{
			WatchApplication();
			var one = new Form { Name = "one" };
			var two = new Form { Name = "two" };
			one.Show();
			two.Show();
			backend.Pump();
			trace.Watch(one, "one");
			trace.Watch(two, "two");
			trace.Clear();
			Application.Exit();
			Assert.Equal(new[]
			{
				"one.Closing", "one.FormClosing(ApplicationExitCall)", "two.Closing", "two.FormClosing(ApplicationExitCall)",
				"one.Closed", "one.FormClosed(ApplicationExitCall)", "two.Closed", "two.FormClosed(ApplicationExitCall)",
				"Application.ThreadExit", "Application.ApplicationExit"
			}, trace.Entries);
			Assert.False(one.IsDisposed || two.IsDisposed);
			Assert.Empty(Application.OpenForms);
		}

		[Fact]
		public void A_form_can_refuse_Exit_and_Exit_with_arguments_tells_the_caller()
		{
			WatchApplication();
			var one = new Form { Name = "one" };
			var two = new Form { Name = "two" };
			one.Show();
			two.Show();
			backend.Pump();
			trace.Watch(one, "one");
			trace.Watch(two, "two");
			two.FormClosing += (s, e) => e.Cancel = true;
			trace.Clear();
			var args = new CancelEventArgs();
			Application.Exit(args);
			Assert.True(args.Cancel);
			Assert.Equal(new[]
			{
				"one.Closing", "one.FormClosing(ApplicationExitCall)", "two.Closing", "two.FormClosing(ApplicationExitCall)"
			}, trace.Entries);
			Assert.Equal(new[] { one, two }, Application.OpenForms.Cast<Form>());
			var again = new CancelEventArgs(true);
			trace.Clear();
			// the first form was told already; the one that refused is asked again
			two.FormClosing += (s, e) => e.Cancel = false;
			Application.Exit(again);
			Assert.False(again.Cancel);
			Assert.Contains("Application.ApplicationExit", trace.Entries);
		}

		[Fact]
		public void Exit_ends_the_open_dialogs_and_the_loop_of_Run()
		{
			WatchApplication();
			var main = new Form { Name = "main" };
			var dialog = new Form { Name = "dialog" };
			DialogResult dialogResult = DialogResult.None;
			dialog.Shown += (s, e) =>
			{
				trace.Add("dialog up");
				Application.Exit();
				trace.Add("Exit returned, dialog visible " + dialog.Visible);
			};
			main.Shown += (s, e) => dialogResult = dialog.ShowDialog(main);
			Application.Run(main);
			Assert.Equal(DialogResult.Cancel, dialogResult);
			Assert.Equal(new[] { "dialog up", "Exit returned, dialog visible True", "Application.ThreadExit", "Application.ApplicationExit" }, trace.Entries);
			Assert.True(main.IsDisposed);
			Assert.False(dialog.Visible);
			Assert.Equal(0, backend.ModalDepth);
		}

		[Fact]
		public void ExitThread_ends_the_loops_without_asking_the_forms()
		{
			WatchApplication();
			var main = new Form { Name = "main" };
			trace.Watch(main, "main");
			main.Shown += (s, e) => Application.ExitThread();
			Application.Run(main);
			Assert.DoesNotContain("main.FormClosing(ApplicationExitCall)", trace.Entries);
			Assert.Equal("Application.ApplicationExit", trace.Entries.Last());
			Assert.True(main.IsDisposed);
		}

		[Fact]
		public void Exit_from_a_script_thread_waits_for_the_ui_thread()
		{
			backend.ModalExitsWhenIdle = false;
			int ui = Environment.CurrentManagedThreadId;
			var one = new Form { Name = "one" };
			one.Show();
			backend.Pump();
			int closingThread = -1;
			one.FormClosing += (s, e) => closingThread = Environment.CurrentManagedThreadId;
			var pump = new ModalLoop();
			var args = new CancelEventArgs(true);
			var script = new Thread(() =>
			{
				Application.Exit(args);
				pump.Exit();
			})
			{ IsBackground = true };
			using (new System.Threading.Timer(_ => pump.Exit(), null, Deadline, Timeout.InfiniteTimeSpan))
			{
				script.Start();
				backend.RunModal(pump);
			}
			Assert.True(script.Join(Deadline));
			Assert.Equal(ui, closingThread);
			Assert.False(args.Cancel);
		}

		// ---------------------------------------------------------------- OpenForms and ApplicationContext

		[Fact]
		public void OpenForms_lists_the_forms_that_were_shown_and_not_closed_in_the_order_of_showing_and_finds_them_by_name()
		{
			var one = new Form { Name = "one" };
			var two = new Form { Name = "two" };
			var never = new Form { Name = "never" };
			two.Show();
			one.Show();
			Assert.Equal(2, Application.OpenForms.Count);
			Assert.Same(two, Application.OpenForms[0]);
			Assert.Same(one, Application.OpenForms[1]);
			Assert.Same(one, Application.OpenForms["one"]);
			Assert.Null(Application.OpenForms["never"]);
			Assert.Null(Application.OpenForms["nothing"]);
			Assert.Null(Application.OpenForms[(string)null]);
			Assert.Same(Application.OpenForms, Application.OpenForms);
			one.Hide();
			Assert.Equal(2, Application.OpenForms.Count);
			one.Close();
			Assert.Equal(new[] { two }, Application.OpenForms.Cast<Form>());
			two.Dispose();
			Assert.Empty(Application.OpenForms);
			never.Dispose();
		}

		[Fact]
		public void An_application_context_follows_its_main_form_and_lets_go_of_the_old_one()
		{
			var a = new Form { Name = "a" };
			var b = new Form { Name = "b" };
			var context = new ApplicationContext { Tag = "tag" };
			int exits = 0;
			context.ThreadExit += (s, e) => exits++;
			Assert.Null(context.MainForm);
			Assert.Equal("tag", context.Tag);
			context.MainForm = a;
			context.MainForm = b;
			a.Show();
			a.Close();
			Assert.Equal(0, exits);
			b.Show();
			b.Close();
			Assert.Equal(1, exits);
			context.Dispose();
			Assert.Null(context.MainForm);
		}

		[Fact]
		public void Disposing_a_context_disposes_the_main_form_and_a_recreated_handle_does_not_end_the_thread()
		{
			var main = new Form();
			var context = new ApplicationContext(main);
			int exits = 0;
			context.ThreadExit += (s, e) => exits++;
			context.Dispose();
			Assert.True(main.IsDisposed);
			Assert.Equal(0, exits);
		}
	}
}
