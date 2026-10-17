using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using ComicRack.Mac.Diagnostics;

namespace ComicRack.Mac.Views
{
	public partial class MainWindow : Window
	{
		private PerfOverlayController perf;

		public MainWindow()
		{
			InitializeComponent();
		}

		/// <summary>Adds the performance overlay (Option+Cmd+P) over the content. Call once.</summary>
		public PerfOverlay AttachPerfOverlay(PerfOverlayController controller)
		{
			perf = controller;
			var overlay = new PerfOverlay(controller, StopwatchClock.Now);
			this.FindControl<Grid>("ContentArea").Children.Add(overlay);
			return overlay;
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (!e.Handled && perf != null && e.Key == Key.P && e.KeyModifiers == (KeyModifiers.Meta | KeyModifiers.Alt))
			{
				perf.Toggle();
				e.Handled = true;
			}
		}
	}
}
