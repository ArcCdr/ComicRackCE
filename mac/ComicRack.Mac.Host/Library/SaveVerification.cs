using System;
using System.IO;
using System.Xml;

namespace ComicRack.Mac.Host.Library
{
	/// <summary>
	/// What happened to the database file when the library was saved. The Core's DatabaseManager.Save swallows every exception,
	/// so "no exception" proves nothing: the check looks at the file itself.
	/// </summary>
	public sealed class SaveVerification
	{
		public string File { get; internal set; }

		/// <summary>True if the file was written during the save and holds the expected books. Also true for a skipped save (read only).</summary>
		public bool Ok { get; set; }

		/// <summary>True if no save was attempted (the host is read only).</summary>
		public bool Skipped { get; internal set; }

		/// <summary>What is wrong, or null.</summary>
		public string Problem { get; set; }

		public long Length { get; internal set; }

		public DateTime LastWriteUtc { get; internal set; }

		/// <summary>Books found in the file, or -1 if they were not counted.</summary>
		public int BooksInFile { get; internal set; } = -1;

		public int ExpectedBooks { get; internal set; }

		public TimeSpan Elapsed { get; internal set; }

		/// <summary>An exception from closing the host (see MacHost.DisposeAndVerify), or null. The file check is independent of it.</summary>
		public Exception CloseError { get; internal set; }

		public override string ToString()
		{
			if (Skipped)
			{
				return "Save skipped (read only).";
			}
			return Ok
				? $"Saved {File}: {Length} bytes, {BooksInFile} books, {Elapsed.TotalSeconds:0.0} s."
				: $"SAVE FAILED for {File}: {Problem}";
		}

		/// <summary>
		/// Checks <paramref name="file"/> after a save that started at <paramref name="savedAfterUtc"/>: it exists, is not empty, was
		/// modified at or after that moment, parses as XML up to the closing root tag and, if <paramref name="expectedBooks"/> is
		/// not negative, holds exactly that many <c>Books/Book</c> elements.
		/// </summary>
		public static SaveVerification Check(string file, DateTime savedAfterUtc, int expectedBooks)
		{
			var started = DateTime.UtcNow;
			var v = new SaveVerification { File = file, ExpectedBooks = expectedBooks };
			try
			{
				var info = new FileInfo(file);
				if (!info.Exists)
				{
					v.Problem = "The database file does not exist after the save.";
					return v;
				}
				v.Length = info.Length;
				v.LastWriteUtc = info.LastWriteTimeUtc;
				if (info.Length == 0)
				{
					v.Problem = "The database file is empty.";
					return v;
				}
				if (info.LastWriteTimeUtc < savedAfterUtc)
				{
					v.Problem = $"The database file was not written: its modification time ({info.LastWriteTimeUtc:O}) is before the save started ({savedAfterUtc:O}).";
					return v;
				}
				if (expectedBooks >= 0)
				{
					v.BooksInFile = CountBooks(file);
					if (v.BooksInFile != expectedBooks)
					{
						v.Problem = $"The database file holds {v.BooksInFile} books, expected {expectedBooks}.";
						return v;
					}
				}
				v.Ok = true;
			}
			catch (Exception ex)
			{
				v.Problem = "The database file could not be read back: " + ex.Message;
			}
			finally
			{
				v.Elapsed = DateTime.UtcNow - started;
			}
			return v;
		}

		/// <summary>Counts the <c>ComicDatabase/Books/Book</c> elements; throws if the XML is cut off or is not a ComicRack database.</summary>
		public static int CountBooks(string file)
		{
			var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = true, XmlResolver = null };
			int count = 0;
			string section = null;
			using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan))
			using (XmlReader reader = XmlReader.Create(stream, settings))
			{
				while (reader.Read())
				{
					if (reader.NodeType != XmlNodeType.Element)
					{
						continue;
					}
					if (reader.Depth == 0 && reader.LocalName != "ComicDatabase")
					{
						throw new InvalidDataException("The root element is <" + reader.LocalName + ">, not <ComicDatabase>.");
					}
					if (reader.Depth == 1)
					{
						section = reader.LocalName;
					}
					else if (reader.Depth == 2 && section == "Books" && reader.LocalName == "Book")
					{
						count++;
					}
				}
			}
			return count;
		}
	}
}
