using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ComicRack.Mac.Diagnostics
{
	/// <summary>The memory the process uses, in bytes.</summary>
	public readonly struct ProcessMemoryInfo
	{
		public ProcessMemoryInfo(long footprintBytes, long residentBytes, bool footprintIsPhysical)
		{
			FootprintBytes = footprintBytes;
			ResidentBytes = residentBytes;
			FootprintIsPhysical = footprintIsPhysical;
		}

		/// <summary>The physical footprint (what Activity Monitor shows as "Memory"), or the working set where the footprint is not available.</summary>
		public long FootprintBytes { get; }

		/// <summary>The resident set size.</summary>
		public long ResidentBytes { get; }

		/// <summary>True if <see cref="FootprintBytes"/> is the macOS <c>phys_footprint</c>.</summary>
		public bool FootprintIsPhysical { get; }
	}

	/// <summary>
	/// Process memory. On macOS it reads <c>proc_pid_rusage</c> (flavor RUSAGE_INFO_V2: <c>ri_resident_size</c> and
	/// <c>ri_phys_footprint</c>, which counts compressed and graphics memory the way Activity Monitor does); elsewhere, or if the call
	/// fails, it falls back to <see cref="Process.WorkingSet64"/>.
	/// </summary>
	public static class ProcessMemory
	{
		private const int RusageInfoV2 = 2;
		private const int BufferSize = 512;

		// struct rusage_info_v2: uuid[16], then u64 fields: user_time, system_time, pkg_idle_wkups, interrupt_wkups, pageins,
		// wired_size, resident_size, phys_footprint.
		private const int ResidentOffset = 16 + 6 * 8;
		private const int FootprintOffset = ResidentOffset + 8;

		[DllImport("libproc.dylib", EntryPoint = "proc_pid_rusage")]
		private static extern int proc_pid_rusage(int pid, int flavor, IntPtr buffer);

		public static ProcessMemoryInfo Read()
		{
			if (OperatingSystem.IsMacOS() && TryReadRusage(out ProcessMemoryInfo info))
			{
				return info;
			}
			return ReadWorkingSet();
		}

		/// <summary>The portable fallback.</summary>
		public static ProcessMemoryInfo ReadWorkingSet()
		{
			try
			{
				using (Process process = Process.GetCurrentProcess())
				{
					return new ProcessMemoryInfo(process.WorkingSet64, process.WorkingSet64, false);
				}
			}
			catch (Exception)
			{
				return new ProcessMemoryInfo(0, 0, false);
			}
		}

		private static bool TryReadRusage(out ProcessMemoryInfo info)
		{
			info = default;
			IntPtr buffer = IntPtr.Zero;
			try
			{
				buffer = Marshal.AllocHGlobal(BufferSize);
				for (int i = 0; i < BufferSize; i += 8)
				{
					Marshal.WriteInt64(buffer, i, 0);
				}
				if (proc_pid_rusage(Environment.ProcessId, RusageInfoV2, buffer) != 0)
				{
					return false;
				}
				long resident = Marshal.ReadInt64(buffer, ResidentOffset);
				long footprint = Marshal.ReadInt64(buffer, FootprintOffset);
				if (footprint <= 0 || resident <= 0)
				{
					return false;
				}
				info = new ProcessMemoryInfo(footprint, resident, true);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
			finally
			{
				if (buffer != IntPtr.Zero)
				{
					Marshal.FreeHGlobal(buffer);
				}
			}
		}
	}
}
