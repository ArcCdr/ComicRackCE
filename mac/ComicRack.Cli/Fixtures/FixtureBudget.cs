using System;
using System.IO;
using System.Threading;

namespace ComicRack.Cli.Fixtures
{
	/// <summary>
	/// The 1.5 GB budget of mac/fixtures. <see cref="Check"/> runs before anything is written (estimate vs. budget and free space);
	/// <see cref="Add"/> counts the real bytes while writing and refuses as soon as the budget is exceeded anyway.
	/// </summary>
	public sealed class FixtureBudget
	{
		private readonly FixtureEnvironment env;
		private long written;

		public long UsedBefore { get; private set; }

		public long Written => Interlocked.Read(ref written);

		public FixtureBudget(FixtureEnvironment env)
		{
			this.env = env;
		}

		/// <param name="estimate">Bytes the command is about to write.</param>
		/// <param name="replacing">Bytes of existing files that the command will overwrite (not counted twice).</param>
		public void Check(long estimate, long replacing, TextWriter output)
		{
			UsedBefore = FixtureEnvironment.DirectorySize(env.FixturesRoot);
			if (env.PrintDiskFree)
			{
				output.WriteLine("Disk before:");
				output.WriteLine(FixtureEnvironment.DiskFreeReport());
			}
			output.WriteLine("Fixtures budget: " + Mb(env.BudgetBytes) + " MB, in use " + Mb(UsedBefore) + " MB, this command is estimated at " + Mb(estimate) + " MB.");
			long after = UsedBefore - replacing + estimate;
			if (after > env.BudgetBytes)
			{
				throw new RefusedException("estimated " + Mb(after) + " MB in " + env.FixturesRoot + " exceeds the budget of " + Mb(env.BudgetBytes) + " MB. Nothing was written.");
			}
			try
			{
				string probe = Directory.Exists(env.FixturesRoot) ? env.FixturesRoot : Path.GetDirectoryName(Path.GetFullPath(env.FixturesRoot));
				long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(probe))).AvailableFreeSpace;
				if (free - estimate < env.MinFreeBytes)
				{
					throw new RefusedException("only " + Mb(free) + " MB free; writing " + Mb(estimate) + " MB would leave less than " + Mb(env.MinFreeBytes) + " MB. Nothing was written.");
				}
			}
			catch (ArgumentException)
			{
			}
		}

		/// <summary>Counts bytes that were really written.</summary>
		public void Add(long bytes)
		{
			long now = Interlocked.Add(ref written, bytes);
			if (UsedBefore + now > env.BudgetBytes)
			{
				throw new RefusedException("the budget of " + Mb(env.BudgetBytes) + " MB was exceeded while writing (the estimate was too low). Stopped.");
			}
		}

		public static long Mb(long bytes) => bytes / (1024 * 1024);
	}
}
