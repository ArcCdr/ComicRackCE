using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using ComicRack.Cli.Fixtures;
using ComicRack.Mac.Host;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Cli.Core
{
	/// <summary>
	/// `crcli info show` and `crcli info write` (P2.4b): the ComicInfo of a book file read and written through the Core
	/// (ComicBook.RefreshInfoFromFile and ComicBook.WriteInfoToFile, so the same code path as the app's "write info to file").
	/// </summary>
	public static class InfoCommands
	{
		private const string Usage = "info show <book>   |   info write <book> --set Field=Value [--set Field=Value ...]";

		public static int Run(string[] args, TextWriter output, TextWriter error)
		{
			if (args.Length == 0 || (args[0] != "show" && args[0] != "write"))
			{
				throw new UsageException("info: expected `" + Usage + "`.");
			}
			bool write = args[0] == "write";
			// --set can repeat, so it is taken out before the generic option parser (which keeps one value per name).
			var sets = new List<string>();
			var rest = new List<string>();
			for (int i = 1; i < args.Length; i++)
			{
				if (args[i] == "--set")
				{
					if (i + 1 >= args.Length)
					{
						throw new UsageException("Missing value for --set");
					}
					sets.Add(args[++i]);
				}
				else
				{
					rest.Add(args[i]);
				}
			}
			var o = Options.Parse(rest.ToArray());
			string book = Path.GetFullPath(o.Positional(0, Usage));
			if (!File.Exists(book))
			{
				throw new UsageException("Book not found: " + book);
			}
			if (write && sets.Count == 0)
			{
				throw new UsageException("info write: at least one --set Field=Value is required.");
			}
			if (!write && sets.Count > 0)
			{
				throw new UsageException("info show: --set belongs to `info write`.");
			}
			var assignments = sets.Select(ParseAssignment).ToList();

			using (CliSession session = CliSession.Create(o))
			using (MacHost host = MacHost.Open(new MacHostOptions { Paths = session.Paths, DatabaseReadOnly = true }))
			{
				ComicBook cb = host.DatabaseManager.BookFactory.Create(book, CreateBookOption.DoNotAdd, RefreshInfoOptions.GetPageCount);
				if (cb == null)
				{
					throw new UsageException("Not a book the Core can read: " + book);
				}
				cb.RefreshInfoFromFile(RefreshInfoOptions.ForceRefresh | RefreshInfoOptions.GetPageCount);
				if (!write)
				{
					Print("info show", book, cb, output);
					return 0;
				}
				foreach ((PropertyInfo property, string text) in assignments)
				{
					property.SetValue(cb, Convert(property, text));
				}
				cb.WriteError += (s, e) => error.WriteLine("  write error: " + e.Message);
				bool ok;
				try
				{
					ok = cb.WriteInfoToFile(host.Settings, withRefreshFileProperties: true);
				}
				catch (Exception ex)
				{
					error.WriteLine("info write: " + ex.GetType().Name + ": " + ex.Message);
					return 1;
				}
				output.WriteLine("info write: " + book);
				foreach ((PropertyInfo property, string text) in assignments)
				{
					output.WriteLine("  set " + property.Name + " = " + text);
				}
				output.WriteLine("  written: " + (ok ? "yes" : "no"));
				if (!ok)
				{
					error.WriteLine("The Core could not write the information (format " + cb.FileFormat + "; 7zz " + (ComicRack.Mac.Host.Platform.ToolLocator.SevenZip ?? "not found") + ").");
					return 1;
				}
				return 0;
			}
		}

		/// <summary>The properties of ComicInfo that the command knows by name: public, writable, simple types.</summary>
		private static IEnumerable<PropertyInfo> Fields()
		{
			return typeof(ComicInfo).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
				.Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && IsSimple(p.PropertyType))
				.OrderBy(p => p.Name, StringComparer.Ordinal);
		}

		private static bool IsSimple(Type t)
		{
			return t == typeof(string) || t == typeof(int) || t == typeof(float) || t == typeof(bool) || t.IsEnum;
		}

		private static (PropertyInfo, string) ParseAssignment(string set)
		{
			int eq = set.IndexOf('=');
			if (eq <= 0)
			{
				throw new UsageException("--set expects Field=Value, got: " + set);
			}
			string name = set.Substring(0, eq).Trim();
			PropertyInfo property = Fields().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
			if (property == null)
			{
				throw new UsageException("Unknown field '" + name + "'. Fields: " + string.Join(", ", Fields().Select(p => p.Name)));
			}
			return (property, set.Substring(eq + 1));
		}

		private static object Convert(PropertyInfo property, string text)
		{
			Type t = property.PropertyType;
			try
			{
				if (t == typeof(string))
				{
					return text;
				}
				if (t.IsEnum)
				{
					return Enum.Parse(t, text, ignoreCase: true);
				}
				return System.Convert.ChangeType(text, t, CultureInfo.InvariantCulture);
			}
			catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is OverflowException)
			{
				throw new UsageException("Field " + property.Name + " (" + t.Name + ") cannot take '" + text + "'.");
			}
		}

		private static void Print(string title, string book, ComicBook cb, TextWriter output)
		{
			output.WriteLine(title + ": " + book);
			output.WriteLine("  format: " + cb.FileFormat + ", pages in file: " + cb.PageCount);
			ComicInfo info = cb.GetInfo();
			foreach (PropertyInfo p in Fields())
			{
				object value = p.GetValue(info);
				object def = p.GetCustomAttribute<DefaultValueAttribute>()?.Value;
				string text = System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
				if (text.Length == 0 || (def != null && text == System.Convert.ToString(def, CultureInfo.InvariantCulture)) || (def == null && p.PropertyType.IsValueType && Equals(value, Activator.CreateInstance(p.PropertyType))))
				{
					continue;
				}
				output.WriteLine("  " + p.Name + ": " + text.Replace("\r", "").Replace("\n", "\\n"));
			}
		}
	}
}
