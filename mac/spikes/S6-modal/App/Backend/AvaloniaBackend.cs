using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using SWF = System.Windows.Forms;

namespace S6.Backend
{
	/// <summary>
	/// The Avalonia side of the facade (section 5.9.3): peers are real Avalonia controls on a Canvas, placed at the rectangles the facade computed.
	/// A nested <see cref="DispatcherFrame"/> is the modal loop.
	/// </summary>
	public sealed class AvaloniaBackend : SWF.IFormsBackend
	{
		public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

		public void Invoke(Action action)
		{
			if (Dispatcher.UIThread.CheckAccess())
			{
				action();
			}
			else
			{
				Dispatcher.UIThread.Invoke(action);
			}
		}

		public void Post(Action action)
		{
			Dispatcher.UIThread.Post(action);
		}

		public SWF.IModalLoop CreateModalLoop() => new FrameLoop();

		public void DoEvents()
		{
			if (Dispatcher.UIThread.CheckAccess())
			{
				Dispatcher.UIThread.RunJobs();
			}
		}

		public SWF.IControlPeer CreatePeer(SWF.Control control)
		{
			SWF.IControlPeer peer = null;
			Invoke(() => peer = control is SWF.Form form ? new FormPeer(this, form) : (SWF.IControlPeer)new ControlPeer(this, control));
			return peer;
		}

		public SWF.ITimerPeer CreateTimer(Action tick) => new TimerPeer(tick);

		public System.Drawing.Size MeasureText(string text, System.Drawing.Font font, int maxWidth)
		{
			System.Drawing.Size result = System.Drawing.Size.Empty;
			Invoke(() =>
			{
				FormattedText ft = new FormattedText(string.IsNullOrEmpty(text) ? " " : text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), DefaultFontSize, Brushes.Black);
				if (maxWidth > 0)
				{
					ft.MaxTextWidth = maxWidth;
				}
				result = new System.Drawing.Size((int)Math.Ceiling(ft.Width), (int)Math.Ceiling(ft.Height));
			});
			return result;
		}

		/// <summary>8.25 pt (the WinForms default, Microsoft Sans Serif) at 96 dpi.</summary>
		public const double DefaultFontSize = 11;

		/// <summary>Windows used as the owner of dialogs that have none (the host's main window).</summary>
		public Window DefaultOwner
		{
			get;
			set;
		}

		/// <summary>The Avalonia control behind a facade control (null until its form has been shown).</summary>
		public static Avalonia.Controls.Control VisualOf(SWF.Control control)
		{
			return (control.Peer as PeerBase)?.Visual;
		}

		public static Window WindowOf(SWF.Form form)
		{
			return (form.Peer as FormPeer)?.Window;
		}

		/// <summary>A nested dispatcher frame.</summary>
		private sealed class FrameLoop : SWF.IModalLoop
		{
			private readonly DispatcherFrame frame = new DispatcherFrame();
			private volatile bool exited;

			public void Run()
			{
				if (exited)
				{
					return;
				}
				Dispatcher.UIThread.PushFrame(frame);
			}

			public void Exit()
			{
				exited = true;
				frame.Continue = false;
			}
		}

		private sealed class TimerPeer : SWF.ITimerPeer
		{
			private readonly DispatcherTimer timer;

			public TimerPeer(Action tick)
			{
				timer = new DispatcherTimer(DispatcherPriority.Normal);
				timer.Tick += (s, e) => tick();
			}

			public int Interval
			{
				get => (int)timer.Interval.TotalMilliseconds;
				set => timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, value));
			}

			public void Start() => timer.Start();

			public void Stop() => timer.Stop();

			public void Dispose() => timer.Stop();
		}
	}
}
