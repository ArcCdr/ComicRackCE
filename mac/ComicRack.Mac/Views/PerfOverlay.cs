using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ComicRack.Mac.Diagnostics;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The overlay panel of one window: a small translucent block in the top right corner that shows the lines of the latest
	/// sample. It takes no input. Hidden, it is not even in the layout and has no frame loop; visible, it runs a frame loop
	/// (<see cref="TopLevel.RequestAnimationFrame"/>, one request per frame) that feeds the window's <see cref="FpsCounter"/>.
	/// Because the loop asks for a frame each time, the number is the rate of the render loop with the overlay on, which is the display
	/// rate when nothing else is drawing and lower when the window cannot keep up.
	/// </summary>
	public sealed class PerfOverlay : Border
	{
		private readonly PerfOverlayController controller;
		private readonly FpsCounter counter;
		private readonly TextBlock text;
		private int generation;
		private bool pumping;

		public PerfOverlay(PerfOverlayController controller, Func<TimeSpan> clock)
		{
			this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
			counter = new FpsCounter(clock);
			Name = "PerfOverlay";
			IsHitTestVisible = false;
			HorizontalAlignment = HorizontalAlignment.Right;
			VerticalAlignment = VerticalAlignment.Top;
			Margin = new Thickness(10);
			Classes.Add("cr-overlay");
			text = new TextBlock { Name = "PerfText", Classes = { "cr-overlay-text" } };
			Child = text;
			IsVisible = false;
		}

		/// <summary>The text of the overlay.</summary>
		public string Text => text.Text;

		/// <summary>True while this window's frame loop is requesting frames.</summary>
		public bool IsPumping => pumping;

		public FpsCounter Counter => counter;

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);
			controller.Register(counter);
			controller.PropertyChanged += OnControllerChanged;
			controller.Updated += OnUpdated;
			Apply();
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			controller.PropertyChanged -= OnControllerChanged;
			controller.Updated -= OnUpdated;
			controller.Unregister(counter);
			StopPump();
			base.OnDetachedFromVisualTree(e);
		}

		private void OnControllerChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(PerfOverlayController.IsVisible))
			{
				Apply();
			}
		}

		private void Apply()
		{
			if (controller.IsVisible)
			{
				IsVisible = true;
				if (controller.Last != null)
				{
					text.Text = string.Join(Environment.NewLine, PerfFormat.Lines(controller.Last));
				}
				StartPump();
			}
			else
			{
				StopPump();
				IsVisible = false;
			}
		}

		private void OnUpdated(PerfSnapshot snapshot)
		{
			// Each window shows its own frame rate and the shared rest.
			snapshot = new PerfSnapshot
			{
				Fps = counter.Fps,
				DecodeMilliseconds = snapshot.DecodeMilliseconds,
				CachedTurnMilliseconds = snapshot.CachedTurnMilliseconds,
				ThumbnailsQueued = snapshot.ThumbnailsQueued,
				ThumbnailsRunning = snapshot.ThumbnailsRunning,
				ManagedHeapBytes = snapshot.ManagedHeapBytes,
				Memory = snapshot.Memory,
			};
			text.Text = string.Join(Environment.NewLine, PerfFormat.Lines(snapshot));
		}

		private void StartPump()
		{
			if (pumping)
			{
				return;
			}
			TopLevel top = TopLevel.GetTopLevel(this);
			if (top == null)
			{
				return;
			}
			pumping = true;
			int mine = ++generation;
			top.RequestAnimationFrame(t => OnFrame(top, mine));
		}

		private void StopPump()
		{
			pumping = false;
			generation++;
		}

		private void OnFrame(TopLevel top, int mine)
		{
			if (!pumping || mine != generation)
			{
				return;
			}
			counter.Frame();
			top.RequestAnimationFrame(t => OnFrame(top, mine));
		}
	}
}
