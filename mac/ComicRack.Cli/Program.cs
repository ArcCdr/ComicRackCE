using System;
using System.Collections.Generic;
using System.IO;
using ComicRack.Cli.Core;
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
				case "db":
					return CoreCommands.Db(rest.ToArray(), output, error);
				case "scan":
					return CoreCommands.Scan(rest.ToArray(), output, error);
				case "page":
					return CoreCommands.Page(rest.ToArray(), output, error);
				case "thumb":
					return CoreCommands.Thumb(rest.ToArray(), output, error);
				case "image":
					return ImageCommands.Run(rest.ToArray(), output, error);
				case "info":
					return InfoCommands.Run(rest.ToArray(), output, error);
				case "smartlist":
					return SmartListCommand.Run(rest.ToArray(), output, error);
				case "script":
					return ScriptCommands.Run(rest.ToArray(), output, error);
				case "script-scan":
					return ComicRack.Cli.Scripting.ScriptScanCommand.Run(rest.ToArray(), output, error);
				case "plugin-gap":
					return ComicRack.Cli.Scripting.PluginGapCommand.Run(rest.ToArray(), output, error);
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
  fixtures pdf <file.pdf> [--pages N=3]
      Write a small vector PDF (A5 portrait pages with panels and the page number) to test the PDF reader
  fixtures cb7 <in.cbz> <out.cb7>
      Repack a CBZ as a 7z archive (SharpCompress writer; needs no 7zz)
  fixtures from-db <ComicDb.xml> --root <dir> [--pages N] [--max-books N] [--strip <prefix>]
      Copy the DB to mac/fixtures/ComicDb.xml and write a stand-in CBZ per book below <dir>
      (pages scaled to fit 800 px, aspect from the DB, JPEG quality 70). Default --pages 1.
  db stats <ComicDb.xml> [--remap ""S:\media\BD\=/abs/path/""]
      Load time, book count, memory. The database is opened read only. The remap is applied in memory to books and watch folders.
  scan <folder> --db <out.xml>
      Scan a folder into a new database; timing per book.
  page <book> --page N --out p.png
      Decode page N (1-based) through the book provider and the ImagePool; write a PNG.
  thumb <book> --out t.jpg [--page N]
      Thumbnail (front cover, or page N) through the Core cache; run twice to show the disk cache.
  smartlist <ComicDb.xml> (--list ""<name>"" | --all) [--remap ""from=to""] [--cached]
      Evaluate a list of the database with the Core (the same code the app's sidebar uses) and print its book count.
      --all prints every list as a tab separated table. By default each list is computed from its matchers; --cached trusts the query cache stored in the file. Lists with a script
      matcher are reported as ""requires scripts"" (scripting off) or ""script not found: key"" with count 0; a list whose scripts are on (bundled Scripts/ next to crcli, --scripts-dir) is computed.
      --no-scripts switches scripting off, --scripts-dir <dir[;dir]> adds script folders. The database is opened read only.
  script run <file.py> --hook <Hook> [--method M] [--db x.xml [--remap ""from=to""]] [--list ""name""] [--path P] [--hint H --text T --limit N] [--a A --b B] [--user-closing] [--width W --height H] [--scripts-dir D]
      Call the commands of one script file for a hook (Books, Library, NewBooks, Editor, ParseComicPath, CreateBookList, NetSearch, Startup, Shutdown, BookOpened, ReaderResized,
      ComicInfoHtml, QuickOpenHtml) with the arguments the Windows program passes, on the books of the database or of --list. Prints the script output and a line for the result. Exit 1 if a script failed.
  script install <file.crplugin> [--stage] | script list | script remove <name> [--stage]
      Plugin packages in <data-dir>/Scripts (the user script folder). install and remove take effect at once; --stage leaves them pending until the next start, as the Windows program does.
  script expr ""<python expression>"" [--db x.xml [--remap ""from=to""]] [--list ""name""] [--show N]
      The books a Python smart list expression such as ""{Series} == 'Batman' and {Year} > 2000"" matches (the Expression matcher of the smart list editor).
  script-scan <file-or-folder>... [--json out.json] [--top N] [--reference-pack <dir>] [--fail-on-error]
      The script compatibility scanner: reads scripts (.py, parsed with the IronPython parser), .NET assemblies (.dll/.exe, member references of the metadata), .html files and
      .crplugin/.zip packages (in memory) and prints the ranked table type.member -> number of scripts for System.Windows.Forms and System.Drawing, the assemblies they reference,
      and what can never run on macOS (Tier C: native Windows libraries through ctypes or P/Invoke, window messages, WPF, COM). --json writes the whole report to a file.
      Nothing is run or loaded. --reference-pack is the folder with the reference System.Windows.Forms.dll (default: the NuGet cache of the Windows Desktop reference pack).
  plugin-gap <package.crplugin>... [--rules F] [--notes-dir D] [--markdown out.md] [--json out.json] [--reference-pack <dir>] [--require-tasks]
      What each plugin package reaches and what the macOS build has: installs the package into a temporary data folder through the plugin install path, loads its scripts in the plugin
      host, scans it (nothing else of it runs) and classifies every WinForms/GDI+ member, .NET type and member, Core type, Python module, referenced assembly and by-name access as
      real, stub, partial, missing, differs, plugin, python or dynamic. --rules gives the task that owns each gap (mac/scripting/gap-rules.txt). SOURCE.txt next to a package gives its URL, pin and licence.
  info show <book>
      The ComicInfo of a book file as the Core reads it (fields that are not empty or default).
  info write <book> --set Field=Value [--set Field=Value ...]
      Set fields and write them into the book file through the Core (ComicBook.WriteInfoToFile: CBZ, CB7 and CBT use the bundled 7zz;
      CBR and PDF cannot be written). Field names are those of ComicInfo (Series, Number, Summary, Year, ...). Exit 1 if the Core could not write.
  image probe <png> <x> <y> [--box N]
      Colour at a pixel (top left origin), or the mean of the N x N pixels around it. Prints rgba and hex.
  image stats <png> [--require-nonblank]
      Size, mean/min/max of R, G, B and A, number of distinct colours, and whether the image is blank (one colour). With
      --require-nonblank a blank image exits with 1. Used by mac/tools/ui-drive to check screenshots.
  image sheet --out <png> --cols N [--cell-width W] [--gap G] [--background #RRGGBB] <image-or-card.json>[::caption]...
      Contact sheet: the inputs in rows of N cells (scaled to the cell width, default 480), each with a caption under it
      (the text after `::`, else the file name). A .json input is a reference card (title, lines, swatches, icons) that is drawn first.
Options of db/scan/page/thumb/smartlist/info: --data-dir <dir> (or CRCLI_DATA_DIR) keeps data, cache and logs there; --app-folders uses
~/Library/.../ComicRack CE (MVP). Default: a temporary folder that is deleted at the end.
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
