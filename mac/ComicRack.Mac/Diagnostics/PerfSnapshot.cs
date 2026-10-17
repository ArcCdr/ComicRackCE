using System;
using System.Globalization;

namespace ComicRack.Mac.Diagnostics
{
	/// <summary>One reading of the performance overlay. Nullable values are "not available" (no reader open, no library).</summary>
	public sealed class PerfSnapshot
	{
		/// <summary>Frames per second of the render loop of the window; 0 when no frame was seen lately.</summary>
		public double Fps { get; set; }

		/// <summary>The decode time of the page that was decoded last (Core read plus Skia decode), in the front reader.</summary>
		public double? DecodeMilliseconds { get; set; }

		/// <summary>The total time of the last page turn that needed no decoding (command to the end of the draw call), in the front reader.</summary>
		public double? CachedTurnMilliseconds { get; set; }

		/// <summary>Cover decodes waiting in the thumbnail queue, and the ones running now.</summary>
		public int? ThumbnailsQueued { get; set; }

		public int? ThumbnailsRunning { get; set; }

		public long ManagedHeapBytes { get; set; }

		public ProcessMemoryInfo Memory { get; set; }
	}

	/// <summary>Text of the overlay and of the log line. Invariant culture, so that logs are comparable.</summary>
	public static class PerfFormat
	{
		public static string Bytes(long bytes)
		{
			double mb = bytes / (1024.0 * 1024.0);
			return mb >= 1024 ? (mb / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB" : mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
		}

		public static string Milliseconds(double? value)
		{
			return value.HasValue ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) + " ms" : "-";
		}

		public static string Fps(double fps)
		{
			return fps.ToString("0.0", CultureInfo.InvariantCulture);
		}

		public static string Thumbnails(PerfSnapshot s)
		{
			if (!s.ThumbnailsQueued.HasValue)
			{
				return "-";
			}
			return s.ThumbnailsQueued.Value.ToString(CultureInfo.InvariantCulture) + " queued, " + (s.ThumbnailsRunning ?? 0).ToString(CultureInfo.InvariantCulture) + " running";
		}

		/// <summary>The lines shown in the overlay.</summary>
		public static string[] Lines(PerfSnapshot s)
		{
			return new[]
			{
				"FPS         " + Fps(s.Fps),
				"Decode      " + Milliseconds(s.DecodeMilliseconds),
				"Page turn   " + Milliseconds(s.CachedTurnMilliseconds),
				"Thumbnails  " + Thumbnails(s),
				"Heap        " + Bytes(s.ManagedHeapBytes),
				"Footprint   " + Bytes(s.Memory.FootprintBytes),
				"RSS         " + Bytes(s.Memory.ResidentBytes),
			};
		}

		/// <summary>The line logged every 10 seconds.</summary>
		public static string LogLine(PerfSnapshot s)
		{
			return "Perf: fps " + Fps(s.Fps)
				+ ", decode " + Milliseconds(s.DecodeMilliseconds)
				+ ", cached page turn " + Milliseconds(s.CachedTurnMilliseconds)
				+ ", thumbnails " + Thumbnails(s)
				+ ", heap " + Bytes(s.ManagedHeapBytes)
				+ ", footprint " + Bytes(s.Memory.FootprintBytes)
				+ ", rss " + Bytes(s.Memory.ResidentBytes) + ".";
		}
	}
}
