using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using cYo.Compat.Forms;
using Xunit;

namespace Compat.Forms.Tests
{
	/// <summary>
	/// Threads in the facade (P4.9e): a script changes controls from worker threads (ComicRack sets CheckForIllegalCrossThreadCalls to false), calls Invoke and BeginInvoke, pumps with
	/// Application.DoEvents and uses System.Windows.Forms.Timer. The UI thread of these tests is the test thread; no assertion depends on timing: workers are released by events, the
	/// timer clock is the manual clock of the null backend, and every wait has a deadline that only bounds a hang.
	/// </summary>
	[Collection("Forms runtime")]
	public sealed class ThreadingTests : IDisposable
	{
		private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

		private readonly NullFormsBackend backend;

		public ThreadingTests()
		{
			FormsRuntime.Reset();
			backend = new NullFormsBackend { ThreadAffinity = true };
			backend.AdoptCurrentThread();
			FormsRuntime.Backend = backend;
		}

		public void Dispose()
		{
			FormsRuntime.Reset();
		}

		// ---------------------------------------------------------------- helpers

		/// <summary>Runs <paramref name="body"/> on <paramref name="count"/> threads that start together; returns when all ended (the first exception is rethrown).</summary>
		private static void OnThreads(int count, Action<int> body, Action whileRunning = null)
		{
			var errors = new List<Exception>();
			using (var barrier = new Barrier(count + (whileRunning == null ? 0 : 1)))
			{
				var workers = new List<Thread>();
				for (int t = 0; t < count; t++)
				{
					int id = t;
					workers.Add(new Thread(() =>
					{
						try
						{
							barrier.SignalAndWait();
							body(id);
						}
						catch (Exception e)
						{
							lock (errors)
							{
								errors.Add(e);
							}
						}
					})
					{ IsBackground = true });
				}
				foreach (Thread w in workers)
				{
					w.Start();
				}
				if (whileRunning != null)
				{
					barrier.SignalAndWait();
					var clock = Stopwatch.StartNew();
					while (workers.Any(w => w.IsAlive))
					{
						whileRunning();
						Assert.True(clock.Elapsed < Deadline, "the workers did not finish");
						Thread.Yield();
					}
				}
				foreach (Thread w in workers)
				{
					Assert.True(w.Join(Deadline), "a worker did not finish (hang)");
				}
			}
			if (errors.Count > 0)
			{
				throw new AggregateException(errors);
			}
		}

		private readonly List<Exception> workerErrors = new List<Exception>();

		/// <summary>A background thread (not started) whose exception is kept for <see cref="Finish"/> instead of ending the test process.</summary>
		private Thread Worker(Action body)
		{
			return new Thread(() =>
			{
				try
				{
					body();
				}
				catch (Exception e)
				{
					lock (workerErrors)
					{
						workerErrors.Add(e);
					}
				}
			})
			{ IsBackground = true };
		}

		/// <summary>Waits for the threads (a hang fails) and rethrows what they threw.</summary>
		private void Finish(params Thread[] threads)
		{
			foreach (Thread t in threads)
			{
				Assert.True(t.Join(Deadline), "a worker did not finish (hang)");
			}
			lock (workerErrors)
			{
				if (workerErrors.Count > 0)
				{
					throw new AggregateException(workerErrors.ToArray());
				}
			}
		}

		private static T Within<T>(Func<T> read, Func<T, bool> done)
		{
			var clock = Stopwatch.StartNew();
			T value = read();
			while (!done(value))
			{
				Assert.True(clock.Elapsed < Deadline, "the condition was not reached");
				Thread.Yield();
				value = read();
			}
			return value;
		}

		/// <summary>Sets a value from 4 threads (1,000 each) while another thread reads it; no value read may be a mixture of two writes and the last value must be the last write of a thread.</summary>
		private static void Hammer<T>(Func<int, int, T> make, Action<T> set, Func<T> get, Func<T, bool> consistent, T initial)
		{
			string torn = null;
			int done = 0;
			var reader = new Thread(() =>
			{
				try
				{
					while (Volatile.Read(ref done) == 0)
					{
						T seen = get();
						if (!consistent(seen) && !Equals(seen, initial))
						{
							torn = "torn value " + seen;
							return;
						}
					}
				}
				catch (Exception e)
				{
					torn = "the reader threw " + e;
				}
			})
			{ IsBackground = true };
			reader.Start();
			try
			{
				OnThreads(4, t =>
				{
					for (int i = 0; i < 1000; i++)
					{
						set(make(t, i));
					}
				});
			}
			finally
			{
				Volatile.Write(ref done, 1);
				Assert.True(reader.Join(Deadline));
			}
			Assert.Null(torn);
			T last = get();
			Assert.True(Enumerable.Range(0, 4).Any(t => Equals(make(t, 999), last)), "the last value is not the last write of a thread: " + last);
		}

		// ---------------------------------------------------------------- state written from 4 threads

		[Fact]
		public void Text_written_by_four_threads_loses_no_change_and_ends_with_a_last_write()
		{
			var c = new Control();
			int changed = 0;
			c.TextChanged += (s, e) => Interlocked.Increment(ref changed);
			Hammer((t, i) => "t" + t + "-" + i, v => c.Text = v, () => c.Text, v => v.StartsWith("t", StringComparison.Ordinal), string.Empty);
			Assert.Equal(4000, Volatile.Read(ref changed));
		}

		[Fact]
		public void Multi_word_values_are_never_torn()
		{
			var control = new Control();
			Hammer((t, i) => new Rectangle(1000 * t + i, 1000 * t + i + 1, 1000 * t + i + 2, 1000 * t + i + 3), v => control.Bounds = v, () => control.Bounds,
				r => r.Y == r.X + 1 && r.Width == r.X + 2 && r.Height == r.X + 3, control.Bounds);

			var colors = new Control();
			Hammer((t, i) => Color.FromArgb(t + 1, i % 250 + 1, i % 250 + 1, i % 250 + 1), v => colors.BackColor = v, () => colors.BackColor,
				c => c.R == c.G && c.G == c.B && c.A >= 1 && c.A <= 4, colors.BackColor);
			Hammer((t, i) => Color.FromArgb(t + 1, i % 250 + 1, i % 250 + 1, i % 250 + 1), v => colors.ForeColor = v, () => colors.ForeColor,
				c => c.R == c.G && c.G == c.B && c.A >= 1 && c.A <= 4, colors.ForeColor);

			var padded = new Control();
			Hammer((t, i) => new Padding(1000 * t + i, 1000 * t + i + 1, 1000 * t + i + 2, 1000 * t + i + 3), v => padded.Padding = v, () => padded.Padding,
				p => p.Top == p.Left + 1 && p.Right == p.Left + 2 && p.Bottom == p.Left + 3, padded.Padding);
			Hammer((t, i) => new Padding(1000 * t + i, 1000 * t + i + 1, 1000 * t + i + 2, 1000 * t + i + 3), v => padded.Margin = v, () => padded.Margin,
				p => p.Top == p.Left + 1 && p.Right == p.Left + 2 && p.Bottom == p.Left + 3, padded.Margin);

			var sized = new Control();
			Hammer((t, i) => new Size(1000 * t + i, 1000 * t + i + 1), v => sized.MinimumSize = v, () => sized.MinimumSize, s => s.Height == s.Width + 1, sized.MinimumSize);
			var located = new Control();
			Hammer((t, i) => new Point(1000 * t + i, 1000 * t + i + 1), v => located.Location = v, () => located.Location, p => p.Y == p.X + 1, located.Location);
		}

		[Fact]
		public void The_decimals_of_a_NumericUpDown_are_never_torn_and_the_form_values_are_whole()
		{
			var n = new NumericUpDown { Maximum = decimal.MaxValue, Minimum = decimal.MinValue };
			var written = new HashSet<decimal>();
			for (int t = 0; t < 4; t++)
			{
				for (int i = 0; i < 1000; i++)
				{
					written.Add(Make(t, i));
				}
			}
			Hammer(Make, v => n.Value = v, () => n.Value, v => written.Contains(v), 0m);

			var form = new Form();
			Hammer((t, i) => Color.FromArgb(t + 1, i % 250 + 1, i % 250 + 1, i % 250 + 1), v => form.TransparencyKey = v, () => form.TransparencyKey,
				c => c.R == c.G && c.G == c.B, form.TransparencyKey);

			var panel = new Panel();
			Hammer((t, i) => new Size(1000 * t + i, 1000 * t + i + 1), v => panel.AutoScrollMargin = v, () => panel.AutoScrollMargin, s => s.Height == s.Width + 1, panel.AutoScrollMargin);
			Hammer((t, i) => new Size(1000 * t + i, 1000 * t + i + 1), v => panel.AutoScrollMinSize = v, () => panel.AutoScrollMinSize, s => s.Height == s.Width + 1, panel.AutoScrollMinSize);
			Hammer((t, i) => new Point(1000 * t + i, 1000 * t + i + 1), v => panel.AutoScrollOffset = v, () => panel.AutoScrollOffset, p => p.Y == p.X + 1, panel.AutoScrollOffset);
		}

		private static decimal Make(int t, int i) => new decimal(i + 1, t + 1, 3 * t + (i % 7) + 1, false, 0);

		[Fact]
		public void Items_and_children_added_by_four_threads_are_all_there()
		{
			var list = new ListBox();
			var combo = new ComboBox();
			var panel = new Panel();
			var added = new Control[4, 250];
			OnThreads(4, t =>
			{
				for (int i = 0; i < 1000; i++)
				{
					list.Items.Add(t * 1000 + i);
					combo.Items.Add(t * 1000 + i);
				}
				for (int i = 0; i < 250; i++)
				{
					var child = new Label { Name = "c" + t + "-" + i };
					added[t, i] = child;
					panel.Controls.Add(child);
				}
			});
			Assert.Equal(4000, list.Items.Count);
			Assert.Equal(4000, combo.Items.Count);
			Assert.Equal(new HashSet<int>(Enumerable.Range(0, 4).SelectMany(t => Enumerable.Range(0, 1000).Select(i => t * 1000 + i))), new HashSet<int>(list.Items.Cast<int>()));
			Assert.Equal(1000, panel.Controls.Count);
			foreach (Control child in added)
			{
				Assert.Same(panel, child.Parent);
			}
			Assert.Equal(1000, panel.Controls.Cast<Control>().Select(c => c.TabIndex).Distinct().Count());
			for (int t = 0; t < 4; t++)
			{
				// each thread added its own children in order
				int[] positions = Enumerable.Range(0, 250).Select(i => panel.Controls.IndexOf(added[t, i])).ToArray();
				Assert.Equal(positions.OrderBy(p => p), positions);
			}
		}

		[Fact]
		public void Children_added_from_four_threads_to_a_docked_container_lay_out_afterwards_as_if_added_in_one_thread()
		{
			var form = new Form { ClientSize = new Size(400, 3000) };
			var host = new Panel { Dock = DockStyle.Fill };
			form.Controls.Add(host);
			OnThreads(4, t =>
			{
				for (int i = 0; i < 100; i++)
				{
					host.Controls.Add(new Label { Dock = DockStyle.Top, Height = 5 });
				}
			});
			host.PerformLayout();
			Assert.Equal(400, host.Controls.Count);
			var tops = host.Controls.Cast<Control>().Select(c => c.Top).OrderBy(x => x).ToArray();
			Assert.Equal(Enumerable.Range(0, 400).Select(i => i * 5), tops);
			Assert.All(host.Controls.Cast<Control>(), c => Assert.Equal(host.Width, c.Width));
		}

		// ---------------------------------------------------------------- what the peer is told

		[Fact]
		public void Changes_of_one_property_from_four_threads_reach_the_peer_as_one_post()
		{
			var form = new Form();
			var button = new Button();
			form.Controls.Add(button);
			form.Show();
			NullPeer peer = backend.Peers[1];
			backend.Pump();
			int before = peer.Log.Count(l => l == "PropertyChanged Text");
			OnThreads(4, t =>
			{
				for (int i = 0; i < 1000; i++)
				{
					button.Text = "t" + t + "-" + i;
				}
			});
			Assert.Equal(before, peer.Log.Count(l => l == "PropertyChanged Text"));
			Assert.Equal(1, backend.PendingCount);
			Assert.Equal(1, backend.Pump());
			Assert.Equal(before + 1, peer.Log.Count(l => l == "PropertyChanged Text"));
			Assert.Equal(0, backend.PendingCount);
			// a later change posts again
			RunOnWorker(() => button.Text = "later");
			Assert.Equal(1, backend.Pump());
			Assert.Equal(before + 2, peer.Log.Count(l => l == "PropertyChanged Text"));
			Assert.Equal("later", button.Text);
		}

		[Fact]
		public void Different_properties_are_posted_once_each_and_the_peer_is_told_while_the_ui_thread_pumps()
		{
			var form = new Form();
			var button = new Button();
			form.Controls.Add(button);
			form.Show();
			NullPeer peer = backend.Peers[1];
			backend.Pump();
			OnThreads(4, t =>
			{
				for (int i = 0; i < 1000; i++)
				{
					button.Text = "t" + t + "-" + i;
					button.Enabled = (i + t) % 2 == 0;
					button.Bounds = new Rectangle(i, t, 10 + i, 20);
				}
			}, () => backend.Pump());
			backend.Pump();
			Assert.InRange(peer.Log.Count(l => l == "PropertyChanged Text"), 1, 4000);
			Assert.InRange(peer.Log.Count(l => l == "PropertyChanged Bounds"), 1, 4000);
			Assert.Equal(0, backend.PendingCount);
			Assert.Matches(@"^t\d-999$", button.Text);
			Assert.Equal(999, button.Left);
			Assert.Equal(1009, button.Width);
		}

		[Fact]
		public void Invalidate_and_child_changes_from_threads_are_joined_into_one_post_each()
		{
			var form = new Form();
			var panel = new Panel();
			form.Controls.Add(panel);
			form.Show();
			NullPeer peer = backend.Peers[1];
			backend.Pump();
			int invalidates = peer.Log.Count(l => l == "Invalidate");
			int children = peer.Log.Count(l => l == "ChildrenChanged");
			OnThreads(4, t =>
			{
				for (int i = 0; i < 100; i++)
				{
					panel.Invalidate(new Rectangle(t * 20, 0, 10, 10));
					// a hidden child gets no peer, so adding it needs nothing from the UI thread
					panel.Controls.Add(new Label { Visible = false });
				}
			});
			Assert.Equal(invalidates, peer.Log.Count(l => l == "Invalidate"));
			Assert.Equal(children, peer.Log.Count(l => l == "ChildrenChanged"));
			backend.Pump();
			Assert.Equal(invalidates + 1, peer.Log.Count(l => l == "Invalidate"));
			Assert.Equal(children + 1, peer.Log.Count(l => l == "ChildrenChanged"));
			Assert.Equal(new Rectangle(0, 0, 70, 10), peer.LastInvalidated);
			Assert.Equal(400, panel.Controls.Count);
		}

		[Fact]
		public void A_post_for_a_peer_that_was_destroyed_meanwhile_is_dropped()
		{
			var form = new Form();
			var button = new Button();
			form.Controls.Add(button);
			form.Show();
			NullPeer peer = backend.Peers[1];
			backend.Pump();
			RunOnWorker(() => button.Text = "late");
			form.Close();
			backend.Pump();
			Assert.True(peer.Disposed);
			Assert.DoesNotContain("PropertyChanged Text", peer.Log);
		}

		[Fact]
		public void Controls_added_to_a_shown_form_by_workers_get_their_peers_on_the_ui_thread()
		{
			var form = new Form();
			form.Show();
			backend.Pump();
			int ui = Environment.CurrentManagedThreadId;
			var createdOn = new List<int>();
			form.Controls.Add(new Label());
			OnThreads(4, t =>
			{
				for (int i = 0; i < 25; i++)
				{
					var button = new Button();
					button.HandleCreated += (s, e) =>
					{
						lock (createdOn)
						{
							createdOn.Add(Environment.CurrentManagedThreadId);
						}
					};
					form.Controls.Add(button);
				}
			}, () => backend.Pump());
			backend.Pump();
			Assert.Equal(100, createdOn.Count);
			Assert.All(createdOn, id => Assert.NotEqual(ui, id) /* HandleCreated is raised on the thread that added the control, the peer is created on the UI thread */);
			Assert.Equal(102, backend.Peers.Count);
			Assert.All(backend.Peers, p => Assert.False(p.Disposed));
		}

		// ---------------------------------------------------------------- Invoke, BeginInvoke, EndInvoke, InvokeRequired

		private (Form form, Button button) ShownForm()
		{
			var form = new Form();
			var button = new Button();
			form.Controls.Add(button);
			form.Show();
			backend.Pump();
			return (form, button);
		}

		[Fact]
		public void InvokeRequired_follows_the_handle_and_the_thread()
		{
			var lone = new Button();
			bool fromWorker = true;
			RunOnWorker(() => fromWorker = lone.InvokeRequired);
			Assert.False(lone.InvokeRequired);
			Assert.False(fromWorker, "a control without a window handle has no thread to marshal to");

			var (form, button) = ShownForm();
			Assert.False(form.InvokeRequired);
			Assert.False(button.InvokeRequired);
			bool formFromWorker = false;
			bool buttonFromWorker = false;
			RunOnWorker(() =>
			{
				formFromWorker = form.InvokeRequired;
				buttonFromWorker = button.InvokeRequired;
			});
			Assert.True(formFromWorker);
			Assert.True(buttonFromWorker);

			// a child that has no handle uses the nearest parent that has one
			var late = new Label { Visible = false };
			form.Controls.Add(late);
			Assert.False(late.IsHandleCreated);
			Assert.False(late.InvokeRequired);
			bool lateFromWorker = false;
			RunOnWorker(() => lateFromWorker = late.InvokeRequired);
			Assert.True(lateFromWorker);
			int lateRanOn = -1;
			late.Invoke(new MethodInvoker(() => lateRanOn = Environment.CurrentManagedThreadId));
			Assert.Equal(Environment.CurrentManagedThreadId, lateRanOn);

			// after the form closed there is no window any more
			form.Close();
			bool closedFromWorker = true;
			RunOnWorker(() => closedFromWorker = form.InvokeRequired);
			Assert.False(closedFromWorker);
		}

		private static void RunOnWorker(Action action)
		{
			Exception error = null;
			var thread = new Thread(() =>
			{
				try
				{
					action();
				}
				catch (Exception e)
				{
					error = e;
				}
			})
			{ IsBackground = true };
			thread.Start();
			Assert.True(thread.Join(Deadline), "the worker did not finish");
			if (error != null)
			{
				throw new AggregateException(error);
			}
		}

		[Fact]
		public void Invoke_and_BeginInvoke_need_a_window_handle()
		{
			var button = new Button();
			var ex = Assert.Throws<InvalidOperationException>(() => button.Invoke(new MethodInvoker(() => { })));
			Assert.Equal("Invoke or BeginInvoke cannot be called on a control until the window handle has been created.", ex.Message);
			Assert.Throws<InvalidOperationException>(() => button.BeginInvoke(new MethodInvoker(() => { })));
			Assert.Throws<ArgumentNullException>(() => button.Invoke(null));
			Assert.Throws<ArgumentNullException>(() => button.BeginInvoke(null));
		}

		[Fact]
		public void Invoke_on_the_ui_thread_runs_at_once_after_the_calls_that_were_queued_before_it()
		{
			var (form, button) = ShownForm();
			var order = new List<string>();
			IAsyncResult a = button.BeginInvoke(new MethodInvoker(() => order.Add("a")));
			IAsyncResult b = button.BeginInvoke(new MethodInvoker(() => order.Add("b")));
			Assert.Empty(order);
			Assert.False(a.IsCompleted);
			object result = button.Invoke(new Func<int>(() =>
			{
				order.Add("c");
				return 42;
			}));
			Assert.Equal(42, result);
			Assert.Equal(new[] { "a", "b", "c" }, order);
			Assert.True(a.IsCompleted && b.IsCompleted);
			Assert.False(a.CompletedSynchronously);
			Assert.Null(a.AsyncState);
			Assert.True(a.AsyncWaitHandle.WaitOne(0));
			Assert.Null(button.EndInvoke(a));
			// the posts of the two queued calls have nothing left to run
			Assert.Equal(2, backend.Pump());
			Assert.Equal(new[] { "a", "b", "c" }, order);
		}

		[Fact]
		public void BeginInvoke_waits_for_the_ui_thread_and_EndInvoke_runs_it_there_when_it_has_not_run()
		{
			var (form, button) = ShownForm();
			int ran = 0;
			IAsyncResult call = button.BeginInvoke(new Func<int, int, int>((x, y) =>
			{
				ran++;
				return x + y;
			}), 2, 3);
			Assert.Equal(0, ran);
			Assert.Equal(5, button.EndInvoke(call));
			Assert.Equal(1, ran);
			Assert.Equal(5, button.EndInvoke(call));
			backend.Pump();
			Assert.Equal(1, ran);
			Assert.Throws<ArgumentNullException>(() => button.EndInvoke(null));
			Assert.Throws<ArgumentException>(() => form.EndInvoke(call));
			Assert.Throws<ArgumentException>(() => button.EndInvoke(new FakeResult()));
		}

		private sealed class FakeResult : IAsyncResult
		{
			public bool IsCompleted => true;
			public WaitHandle AsyncWaitHandle => null;
			public object AsyncState => null;
			public bool CompletedSynchronously => true;
		}

		[Fact]
		public void The_typed_overloads_and_the_framework_delegates_are_called_as_the_framework_calls_them()
		{
			var (form, button) = ShownForm();
			Assert.Equal(7, button.Invoke(new Func<int>(() => 7)));
			Assert.Equal(7, button.Invoke<int>(() => 7));
			int ran = 0;
			button.Invoke((Action)(() => ran++));
			Assert.Equal(1, ran);
			IAsyncResult call = button.BeginInvoke((Action)(() => ran++));
			button.EndInvoke(call);
			Assert.Equal(2, ran);

			object sender = null;
			EventArgs args = null;
			button.Invoke(new EventHandler((s, e) =>
			{
				sender = s;
				args = e;
			}));
			Assert.Same(button, sender);
			Assert.Same(EventArgs.Empty, args);
			var data = new EventArgs();
			button.Invoke(new EventHandler((s, e) =>
			{
				sender = s;
				args = e;
			}), form, data);
			Assert.Same(form, sender);
			Assert.Same(data, args);
			object state = null;
			button.Invoke(new WaitCallback(o => state = o), "state");
			Assert.Equal("state", state);
		}

		[Fact]
		public void An_exception_of_the_method_comes_back_from_Invoke_as_its_base_exception_and_from_BeginInvoke_to_the_application_handler()
		{
			var (form, button) = ShownForm();
			var thrown = Assert.Throws<ArgumentException>(() => button.Invoke(new MethodInvoker(() => throw new InvalidOperationException("outer", new ArgumentException("inner")))));
			Assert.Equal("inner", thrown.Message);
			Assert.Throws<FormatException>(() => button.Invoke(new Func<int>(() => throw new FormatException())));

			var seen = new List<Exception>();
			ThreadExceptionEventHandler handler = (s, e) => seen.Add(e.Exception);
			Application.ThreadException += handler;
			try
			{
				IAsyncResult call = button.BeginInvoke(new MethodInvoker(() => throw new InvalidOperationException("untrapped")));
				backend.Pump();
				Assert.Equal("untrapped", Assert.Single(seen).Message);
				Assert.Equal("untrapped", Assert.Throws<InvalidOperationException>(() => button.EndInvoke(call)).Message);
			}
			finally
			{
				Application.ThreadException -= handler;
			}
			// an exception of Invoke is not reported to the application, it belongs to the caller
			Assert.Single(seen);
		}

		[Fact]
		public void A_worker_runs_a_method_on_the_ui_thread_with_Invoke_and_gets_the_result_while_the_ui_thread_waits_in_a_nested_frame()
		{
			var (form, button) = ShownForm();
			backend.ModalExitsWhenIdle = false;
			int ui = Environment.CurrentManagedThreadId;
			var loop = new ModalLoop();
			int ranOn = -1;
			object result = null;
			int workerId = -1;
			bool required = false;
			Thread worker = Worker(() =>
			{
				workerId = Environment.CurrentManagedThreadId;
				required = button.InvokeRequired;
				result = button.Invoke(new Func<int>(() =>
				{
					ranOn = Environment.CurrentManagedThreadId;
					return 99;
				}));
				button.Invoke(new MethodInvoker(loop.Exit));
			});
			using (new Timer2(Deadline, loop))
			{
				worker.Start();
				backend.RunModal(loop);
			}
			Finish(worker);
			Assert.True(loop.ExitRequested);
			Assert.Equal(99, result);
			Assert.Equal(ui, ranOn);
			Assert.NotEqual(ui, workerId);
			Assert.True(required);
		}

		/// <summary>Ends the loops after the deadline so that a hang fails the test instead of blocking the run.</summary>
		private sealed class Timer2 : IDisposable
		{
			private readonly System.Threading.Timer timer;

			public Timer2(TimeSpan after, params ModalLoop[] loops)
			{
				timer = new System.Threading.Timer(_ =>
				{
					foreach (ModalLoop l in loops)
					{
						l.Exit();
					}
				}, null, after, Timeout.InfiniteTimeSpan);
			}

			public void Dispose() => timer.Dispose();
		}

		[Fact]
		public void Invoke_calls_of_four_workers_run_in_their_order_on_the_ui_thread_in_a_frame_that_a_call_opened_inside_another()
		{
			var (form, button) = ShownForm();
			backend.ModalExitsWhenIdle = false;
			int ui = Environment.CurrentManagedThreadId;
			var outer = new ModalLoop();
			var inner = new ModalLoop();
			var order = new List<string>();
			var nestedRunning = new ManualResetEventSlim(false);
			int wrongThread = 0;
			var finished = new CountdownEvent(3);

			Thread openNested = Worker(() =>
			{
				button.Invoke(new MethodInvoker(() =>
				{
					// a call on the UI thread opens a nested frame (a dialog) and other workers' calls are served in it
					order.Add("nested opened");
					nestedRunning.Set();
					backend.RunModal(inner);
					order.Add("nested closed");
				}));
				finished.Signal();
			});
			Thread inNested = Worker(() =>
			{
				Assert.True(nestedRunning.Wait(Deadline));
				Within(() => backend.ModalDepth, depth => depth == 2);
				for (int i = 0; i < 5; i++)
				{
					int n = i;
					button.Invoke(new MethodInvoker(() =>
					{
						if (Environment.CurrentManagedThreadId != ui)
						{
							Interlocked.Increment(ref wrongThread);
						}
						order.Add("call " + n);
					}));
				}
				button.Invoke(new MethodInvoker(inner.Exit));
				finished.Signal();
			});
			Thread closer = Worker(() =>
			{
				// the outer frame ends only after the nested one returned
				Within(() => inner.ExitRequested, requested => requested);
				Within(() => backend.ModalDepth, depth => depth == 1);
				button.Invoke(new MethodInvoker(outer.Exit));
				finished.Signal();
			});
			using (new Timer2(Deadline, outer, inner))
			{
				openNested.Start();
				inNested.Start();
				closer.Start();
				backend.RunModal(outer);
			}
			Assert.True(finished.Wait(Deadline), "an Invoke call did not return");
			Finish(openNested, inNested, closer);
			Assert.Equal(0, wrongThread);
			Assert.Equal(new[] { "nested opened", "call 0", "call 1", "call 2", "call 3", "call 4", "nested closed" }, order);
			Assert.Equal(0, backend.ModalDepth);
		}

		[Fact]
		public void Four_workers_calling_Invoke_a_thousand_times_all_run_on_the_ui_thread_in_the_order_of_each_worker()
		{
			var (form, button) = ShownForm();
			backend.ModalExitsWhenIdle = false;
			int ui = Environment.CurrentManagedThreadId;
			var loop = new ModalLoop();
			var next = new int[4];
			int wrongThread = 0;
			int outOfOrder = 0;
			int total = 0;
			var workers = new List<Thread>();
			var left = new CountdownEvent(4);
			for (int t = 0; t < 4; t++)
			{
				int id = t;
				workers.Add(Worker(() =>
				{
					for (int i = 0; i < 250; i++)
					{
						int n = i;
						button.Invoke(new MethodInvoker(() =>
						{
							if (Environment.CurrentManagedThreadId != ui)
							{
								wrongThread++;
							}
							if (next[id] != n)
							{
								outOfOrder++;
							}
							next[id] = n + 1;
							total++;
						}));
						// every few calls go through BeginInvoke and are collected with EndInvoke
						if (i % 25 == 0)
						{
							IAsyncResult call = button.BeginInvoke(new MethodInvoker(() => total++));
							button.EndInvoke(call);
						}
					}
					if (left.Signal())
					{
						button.Invoke(new MethodInvoker(loop.Exit));
					}
				}));
			}
			using (new Timer2(Deadline, loop))
			{
				foreach (Thread w in workers)
				{
					w.Start();
				}
				backend.RunModal(loop);
			}
			Finish(workers.ToArray());
			Assert.Equal(0, wrongThread);
			Assert.Equal(0, outOfOrder);
			Assert.Equal(1000 + 40, total);
		}

		[Fact]
		public void A_worker_waiting_in_Invoke_is_released_with_ObjectDisposedException_when_the_form_goes_away()
		{
			var (form, button) = ShownForm();
			Exception error = null;
			bool ran = false;
			Thread worker = Worker(() =>
			{
				try
				{
					button.Invoke(new MethodInvoker(() => ran = true));
				}
				catch (Exception e)
				{
					error = e;
				}
			});
			worker.Start();
			// the call is queued on the UI thread: the UI thread does not run it and closes the form
			Within(() => backend.PendingCount, pending => pending > 0);
			form.Close();
			Finish(worker);
			Assert.IsType<ObjectDisposedException>(error);
			Assert.False(ran);

			var (second, secondButton) = ShownForm();
			IAsyncResult call = secondButton.BeginInvoke(new MethodInvoker(() => ran = true));
			second.Close();
			Assert.True(call.IsCompleted);
			Assert.Throws<ObjectDisposedException>(() => secondButton.EndInvoke(call));
			Assert.False(ran);
			backend.Pump();
			Assert.False(ran);
			Assert.Throws<InvalidOperationException>(() => secondButton.BeginInvoke(new MethodInvoker(() => { })));
		}

		[Fact]
		public void On_a_backend_without_a_ui_thread_Invoke_and_BeginInvoke_run_at_once_on_the_calling_thread()
		{
			var headless = new NullFormsBackend();
			FormsRuntime.Backend = headless;
			var form = new Form();
			var button = new Button();
			form.Controls.Add(button);
			form.Show();
			bool required = true;
			int ranOn = -1;
			int workerId = -1;
			RunOnWorker(() =>
			{
				workerId = Environment.CurrentManagedThreadId;
				required = button.InvokeRequired;
				button.Invoke(new MethodInvoker(() => ranOn = Environment.CurrentManagedThreadId));
				IAsyncResult call = button.BeginInvoke(new MethodInvoker(() => ranOn = -ranOn));
				Assert.True(call.IsCompleted);
				button.EndInvoke(call);
			});
			Assert.False(required);
			Assert.Equal(-workerId, ranOn);
		}

		// ---------------------------------------------------------------- DoEvents

		[Fact]
		public void DoEvents_runs_what_is_queued_on_the_ui_thread_and_does_nothing_on_another_thread()
		{
			var (form, button) = ShownForm();
			NullPeer peer = backend.Peers[1];
			var order = new List<string>();
			RunOnWorker(() => button.Text = "from a worker");
			button.BeginInvoke(new MethodInvoker(() => order.Add("invoked")));
			Assert.Equal(2, backend.PendingCount);
			Assert.DoesNotContain("PropertyChanged Text", peer.Log);

			RunOnWorker(Application.DoEvents);
			Assert.Equal(2, backend.PendingCount);
			Assert.Empty(order);

			Application.DoEvents();
			Assert.Equal(0, backend.PendingCount);
			Assert.Equal(new[] { "invoked" }, order);
			Assert.Contains("PropertyChanged Text", peer.Log);
		}

		[Fact]
		public void DoEvents_in_a_loop_lets_a_worker_report_progress_with_Invoke()
		{
			var (form, button) = ShownForm();
			int finished = 0;
			var steps = new List<int>();
			Thread worker = Worker(() =>
			{
				for (int i = 1; i <= 20; i++)
				{
					int step = i;
					button.Invoke(new MethodInvoker(() => steps.Add(step)));
				}
				Volatile.Write(ref finished, 1);
			});
			worker.Start();
			var clock = Stopwatch.StartNew();
			while (Volatile.Read(ref finished) == 0)
			{
				Application.DoEvents();
				Assert.True(clock.Elapsed < Deadline, "the worker did not finish");
				Thread.Yield();
			}
			Finish(worker);
			Assert.Equal(Enumerable.Range(1, 20), steps);
		}

		[Fact]
		public void DoEvents_without_a_backend_that_was_installed_does_not_throw()
		{
			FormsRuntime.Reset();
			Application.DoEvents();
			Assert.True(FormsRuntime.HasBackend);
		}

		// ---------------------------------------------------------------- Timer

		[Fact]
		public void A_timer_has_the_defaults_of_the_framework()
		{
			var timer = new System.Windows.Forms.Timer();
			Assert.False(timer.Enabled);
			Assert.Equal(100, timer.Interval);
			Assert.Null(timer.Tag);
			timer.Tag = "tag";
			Assert.Equal("tag", timer.Tag);
			Assert.EndsWith(", Interval: 100", timer.ToString());
			var ex = Assert.Throws<ArgumentOutOfRangeException>(() => timer.Interval = 0);
			Assert.StartsWith("Value of '0' is not valid for 'Interval'. 'Interval' must be greater than 0.", ex.Message);
			Assert.Throws<ArgumentOutOfRangeException>(() => timer.Interval = -5);
			Assert.Equal(100, timer.Interval);
			var container = new Container();
			var contained = new System.Windows.Forms.Timer(container);
			Assert.Contains(contained, container.Components.Cast<IComponent>());
			Assert.Throws<ArgumentNullException>(() => new System.Windows.Forms.Timer(null));
		}

		[Fact]
		public void A_timer_ticks_on_the_ui_thread_when_the_clock_reaches_its_interval()
		{
			var timer = new System.Windows.Forms.Timer { Interval = 100 };
			int ui = Environment.CurrentManagedThreadId;
			var ticks = new List<string>();
			timer.Tick += (s, e) => ticks.Add(ReferenceEquals(s, timer) + " " + (Environment.CurrentManagedThreadId == ui));
			timer.Start();
			Assert.True(timer.Enabled);
			Assert.Equal(1, backend.RunningTimers);
			Assert.Equal(0, backend.AdvanceTime(99));
			Assert.Empty(ticks);
			Assert.Equal(1, backend.AdvanceTime(1));
			Assert.Empty(ticks);
			backend.Pump();
			Assert.Equal(new[] { "True True" }, ticks);
			// three intervals one after the other
			for (int i = 0; i < 3; i++)
			{
				backend.AdvanceTime(100);
				backend.Pump();
			}
			Assert.Equal(4, ticks.Count);
			timer.Stop();
			Assert.False(timer.Enabled);
			Assert.Equal(0, backend.RunningTimers);
			backend.AdvanceTime(1000);
			backend.Pump();
			Assert.Equal(4, ticks.Count);
		}

		[Fact]
		public void A_late_timer_ticks_once_and_not_once_per_missed_interval()
		{
			var timer = new System.Windows.Forms.Timer { Interval = 100 };
			int ticks = 0;
			timer.Tick += (s, e) => ticks++;
			timer.Start();
			Assert.Equal(1, backend.AdvanceTime(1000));
			backend.Pump();
			Assert.Equal(1, ticks);
			// the clock is at 1000 and the next due time is one interval after the one that was missed last: the timer is not behind any more
			Assert.Equal(0, backend.AdvanceTime(0));
			Assert.Equal(1, backend.AdvanceTime(100));
			backend.Pump();
			Assert.Equal(2, ticks);
		}

		[Fact]
		public void Stop_inside_the_handler_and_Stop_before_a_queued_tick_runs_end_the_ticks()
		{
			var timer = new System.Windows.Forms.Timer { Interval = 10 };
			int ticks = 0;
			timer.Tick += (s, e) =>
			{
				ticks++;
				if (ticks == 2)
				{
					timer.Stop();
				}
			};
			timer.Start();
			for (int i = 0; i < 5; i++)
			{
				backend.AdvanceTime(10);
				backend.Pump();
			}
			Assert.Equal(2, ticks);

			// a tick that is queued is dropped by Stop, also when the timer is started again
			var other = new System.Windows.Forms.Timer { Interval = 10 };
			int otherTicks = 0;
			other.Tick += (s, e) => otherTicks++;
			other.Start();
			backend.AdvanceTime(10);
			other.Stop();
			other.Start();
			backend.Pump();
			Assert.Equal(0, otherTicks);
			backend.AdvanceTime(10);
			backend.Pump();
			Assert.Equal(1, otherTicks);
		}

		[Fact]
		public void Starting_a_running_timer_does_not_restart_it_and_a_new_Interval_does()
		{
			var timer = new System.Windows.Forms.Timer { Interval = 100 };
			int ticks = 0;
			timer.Tick += (s, e) => ticks++;
			timer.Start();
			backend.AdvanceTime(60);
			timer.Start();
			timer.Enabled = true;
			backend.AdvanceTime(40);
			backend.Pump();
			Assert.Equal(1, ticks);

			backend.AdvanceTime(60);
			timer.Interval = 200;
			Assert.True(timer.Enabled);
			backend.AdvanceTime(199);
			backend.Pump();
			Assert.Equal(1, ticks);
			backend.AdvanceTime(1);
			backend.Pump();
			Assert.Equal(2, ticks);

			timer.Interval = 200;
			backend.AdvanceTime(199);
			backend.Pump();
			Assert.Equal(2, ticks);
		}

		[Fact]
		public void A_timer_started_on_a_worker_thread_ticks_on_the_ui_thread_and_Enabled_is_true_at_once()
		{
			var timer = new System.Windows.Forms.Timer { Interval = 50 };
			int ui = Environment.CurrentManagedThreadId;
			int tickedOn = -1;
			timer.Tick += (s, e) => tickedOn = Environment.CurrentManagedThreadId;
			bool enabledOnWorker = false;
			RunOnWorker(() =>
			{
				timer.Start();
				enabledOnWorker = timer.Enabled;
			});
			Assert.True(enabledOnWorker);
			Assert.True(timer.Enabled);
			Assert.Equal(0, backend.RunningTimers);
			backend.Pump();
			Assert.Equal(1, backend.RunningTimers);
			backend.AdvanceTime(50);
			backend.Pump();
			Assert.Equal(ui, tickedOn);

			RunOnWorker(timer.Stop);
			Assert.False(timer.Enabled);
			tickedOn = -1;
			backend.AdvanceTime(50);
			backend.Pump();
			Assert.Equal(-1, tickedOn);
			Assert.Equal(0, backend.RunningTimers);
		}

		[Fact]
		public void Start_and_Stop_from_a_worker_before_the_ui_thread_runs_them_leave_the_timer_stopped()
		{
			var timer = new System.Windows.Forms.Timer { Interval = 10 };
			int ticks = 0;
			timer.Tick += (s, e) => ticks++;
			RunOnWorker(() =>
			{
				timer.Start();
				timer.Stop();
				timer.Start();
				timer.Stop();
			});
			Assert.False(timer.Enabled);
			backend.Pump();
			Assert.Equal(0, backend.RunningTimers);
			backend.AdvanceTime(100);
			backend.Pump();
			Assert.Equal(0, ticks);
		}

		[Fact]
		public void A_disposed_timer_does_not_tick_and_an_exception_of_a_handler_goes_to_the_application()
		{
			var timer = new System.Windows.Forms.Timer { Interval = 10 };
			var seen = new List<Exception>();
			ThreadExceptionEventHandler handler = (s, e) => seen.Add(e.Exception);
			Application.ThreadException += handler;
			int ticks = 0;
			try
			{
				timer.Tick += (s, e) =>
				{
					ticks++;
					throw new InvalidOperationException("tick " + ticks);
				};
				timer.Start();
				backend.AdvanceTime(10);
				backend.Pump();
				backend.AdvanceTime(10);
				backend.Pump();
				Assert.Equal(new[] { "tick 1", "tick 2" }, seen.Select(e => e.Message));
				Assert.True(timer.Enabled);
			}
			finally
			{
				Application.ThreadException -= handler;
			}
			timer.Dispose();
			Assert.False(timer.Enabled);
			Assert.Equal(0, backend.RunningTimers);
			backend.AdvanceTime(100);
			backend.Pump();
			Assert.Equal(2, ticks);
		}

		[Fact]
		public void A_subclass_overrides_OnTick_and_several_timers_tick_in_the_order_of_their_due_times()
		{
			var order = new List<string>();
			var slow = new Ticking("slow", order) { Interval = 30 };
			var fast = new Ticking("fast", order) { Interval = 20 };
			slow.Start();
			fast.Start();
			for (int i = 0; i < 6; i++)
			{
				backend.AdvanceTime(10);
				backend.Pump();
			}
			// fast is due at 20, 40 and 60, slow at 30 and 60; both are due at 60 and the timer that was started first ticks first
			Assert.Equal(new[] { "fast", "slow", "fast", "slow", "fast" }, order);
		}

		private sealed class Ticking : System.Windows.Forms.Timer
		{
			private readonly string name;
			private readonly List<string> order;

			public Ticking(string name, List<string> order)
			{
				this.name = name;
				this.order = order;
			}

			protected override void OnTick(EventArgs e)
			{
				order.Add(name);
				base.OnTick(e);
			}
		}

		[Fact]
		public void Without_a_ui_thread_a_timer_ticks_only_when_the_clock_is_advanced_and_ticks_run_at_once()
		{
			var headless = new NullFormsBackend();
			FormsRuntime.Backend = headless;
			var timer = new System.Windows.Forms.Timer { Interval = 25 };
			int ticks = 0;
			timer.Tick += (s, e) => ticks++;
			timer.Start();
			Assert.Equal(0, ticks);
			Assert.Equal(1, headless.AdvanceTime(25));
			Assert.Equal(1, ticks);
			Assert.Equal(25, headless.Clock);
		}
	}
}
