using System;
using System.Collections.Generic;
using System.IO;
using ComicRack.Cli.Fixtures;

namespace ComicRack.Cli
{
	/// <summary>Entry point and command dispatcher of crcli. A command is "group verb"; the groups register themselves here.</summary>
	public static class Program
	{
		public static int Main(string[] args)
		{
			return Run(args, Console.Out, Console.Error);
		}

		public static int Run(string[] args, TextWriter output, TextWriter error)
		{
			if (args.Length == 0 || args[0] == "help" || args[0] == "--help" || args[0] == "-h")
			{
				output.WriteLine(Usage);
				return args.Length == 0 ? 2 : 0;
			}
			var rest = new List<string>(args);
			rest.RemoveAt(0);
			try
			{
				switch (args[0])
				{
				case "fixtures":
					return FixtureCommands.Run(rest.ToArray(), output, error, FixtureEnvironment.Default());
				default:
					error.WriteLine("Unknown command: " + args[0]);
					error.WriteLine(Usage);
					return 2;
				}
			}
			catch (UsageException ex)
			{
				error.WriteLine(ex.Message);
				return 2;
			}
			catch (RefusedException ex)
			{
				error.WriteLine("REFUSED: " + ex.Message);
				return 3;
			}
		}

		private const string Usage = @"crcli commands:
  fixtures make <dir> [--books N] [--pages M] [--formats cbz[,cb7,cbr]] [--seed S]
      Generate N books (default 10) of M pages (default 12), 2-6 MP, JPEG/PNG/WebP mix.
  fixtures from-db <ComicDb.xml> --root <dir> [--pages N] [--max-books N] [--strip <prefix>]
      Copy the DB to mac/fixtures/ComicDb.xml and write a stand-in CBZ per book below <dir>
      (pages scaled to fit 800 px, aspect from the DB, JPEG quality 70). Default --pages 1.
Everything under mac/fixtures is limited to 1.5 GB; the commands estimate first and refuse to exceed it.";
	}

	/// <summary>Bad command line.</summary>
	public sealed class UsageException : Exception
	{
		public UsageException(string message) : base(message) { }
	}

	/// <summary>A guard (disk budget, free space) said no. Exit code 3.</summary>
	public sealed class RefusedException : Exception
	{
		public RefusedException(string message) : base(message) { }
	}
}
