using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace ComicRack.Mac.Diagnostics
{
	/// <summary>Where the overlay gets its numbers. Every member can be replaced (tests do); a null value means "not available".</summary>
	public sealed class PerfSources
	{
		public Func<double?> DecodeMilliseconds { get; set; } = () => null;

		public Func<double?> CachedTurnMilliseconds { get; set; } = () => null;

		/// <summary>(queued, running) of the thumbnail service, or null if there is none.</summary>
		public Func<(int Queued, int Running)?> Thumbnails { get; set; } = () => null;

		public Func<long> ManagedHeapBytes { get; set; } = () => GC.GetTotalMemory(false);

		public Func<ProcessMemoryInfo> Memory { get; set; } = ProcessMemory.Read;
	}

	/// <summary>The repeating timer behind the overlay. Only exists while the overlay is visible.</summary>
	public interface IPerfTicker
	{
		bool IsRunning { get; }

		void Start(TimeSpan interval, Action tick);

		void Stop();
	}

	/// <summary>A <see cref="DispatcherTimer"/> on the UI thread; stopping drops it, so nothing is left behind.</summary>
	public sealed class AvaloniaPerfTicker : IPerfTicker
	{
		private DispatcherTimer timer;

		public bool IsRunning => timer != null;

		public void Start(TimeSpan interval, Action tick)
		{
			Stop();
			var created = new DispatcherTimer { Interval = interval };
			created.Tick += (sender, e) => tick();
			timer = created;
			created.Start();
		}

		public void Stop()
		{
			DispatcherTimer old = timer;
			timer = null;
			old?.Stop();
		}
	}

	/// <summary>
	/// The performance overlay (Option+Cmd+P), shared by every window. While it is hidden nothing runs: no timer, no frame loop (the
	/// windows start theirs from <see cref="PropertyChanged"/> / <see cref="IsVisible"/> and stop them when it clears). While it is
	/// visible a timer samples <see cref="PerfSources"/> twice a second, tells the windows through <see cref="Updated"/>, and every
	/// 10 seconds writes one line to the log.
	/// </summary>
	public sealed class PerfOverlayController : INotifyPropertyChanged
	{
		public static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(500);
		public static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);

		private readonly PerfSources sources;
		private readonly IPerfTicker ticker;
		private readonly Func<TimeSpan> clock;
		private readonly Action<string> log;
		private readonly List<FpsCounter> counters = new List<FpsCounter>();
		private bool isVisible;
		private TimeSpan lastLog;
		private PerfSnapshot last;

		public PerfOverlayController(PerfSources sources, IPerfTicker ticker, Func<TimeSpan> clock, Action<string> log)
		{
			this.sources = sources ?? throw new ArgumentNullException(nameof(sources));
			this.ticker = ticker ?? throw new ArgumentNullException(nameof(ticker));
			this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
			this.log = log;
			ToggleCommand = new RelayCommand(Toggle);
		}

		public event PropertyChangedEventHandler PropertyChanged;

		/// <summary>Raised on every sample while visible, with the sample.</summary>
		public event Action<PerfSnapshot> Updated;

		public PerfSources Sources => sources;

		public void Hide() => IsVisible = false;

		public IRelayCommand ToggleCommand { get; }

		public PerfSnapshot Last => last;

		public bool IsVisible
		{
			get => isVisible;
			set
			{
				if (isVisible == value)
				{
					return;
				}
				isVisible = value;
				if (value)
				{
					lastLog = clock();
					foreach (FpsCounter counter in counters)
					{
						counter.Reset();
					}
					ticker.Start(UpdateInterval, Tick);
					Tick();
				}
				else
				{
					ticker.Stop();
				}
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
			}
		}

		public void Toggle() => IsVisible = !IsVisible;

		/// <summary>A window registers the counter its frame loop feeds.</summary>
		public void Register(FpsCounter counter)
		{
			if (counter != null && !counters.Contains(counter))
			{
				counters.Add(counter);
			}
		}

		public void Unregister(FpsCounter counter)
		{
			counters.Remove(counter);
		}

		/// <summary>Takes one sample (also called by the timer). The highest frame rate of the registered windows is the one reported.</summary>
		public PerfSnapshot Sample()
		{
			double fps = 0;
			foreach (FpsCounter counter in counters)
			{
				fps = Math.Max(fps, counter.Fps);
			}
			var thumbs = Safe(sources.Thumbnails);
			return new PerfSnapshot
			{
				Fps = fps,
				DecodeMilliseconds = Safe(sources.DecodeMilliseconds),
				CachedTurnMilliseconds = Safe(sources.CachedTurnMilliseconds),
				ThumbnailsQueued = thumbs?.Queued,
				ThumbnailsRunning = thumbs?.Running,
				ManagedHeapBytes = Safe(sources.ManagedHeapBytes),
				Memory = Safe(sources.Memory),
			};
		}

		private void Tick()
		{
			if (!isVisible)
			{
				return;
			}
			last = Sample();
			Updated?.Invoke(last);
			TimeSpan now = clock();
			if (now - lastLog >= LogInterval)
			{
				lastLog = now;
				try
				{
					log?.Invoke(PerfFormat.LogLine(last));
				}
				catch (Exception)
				{
				}
			}
		}

		private static T Safe<T>(Func<T> read)
		{
			try
			{
				return read();
			}
			catch (Exception)
			{
				return default;
			}
		}
	}
}
