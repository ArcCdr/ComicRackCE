using System;

namespace S4
{
	internal static class Program
	{
		private static int Main(string[] args)
		{
			if (args.Length != 1)
			{
				Console.Error.WriteLine("Usage: S4-ironpython PROBE.py   (Sample.py is looked up next to it, or in $COMICRACK_SCRIPTS_DIR)");
				return 2;
			}
			return ComicRack.Mac.ScriptSelfTest.Run(args[0], Console.Out);
		}
	}
}
