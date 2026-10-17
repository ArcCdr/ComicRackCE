using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;

namespace ComicRack.Mac.Host.Library
{
	public sealed class ImportResult
	{
		/// <summary>Books in the database.</summary>
		public int Books { get; internal set; }

		/// <summary>Books whose file path matched a mapping.</summary>
		public int BooksRemapped { get; internal set; }

		/// <summary>Of the remapped books, those whose file exists on this Mac.</summary>
		public int BooksFound { get; internal set; }

		public int WatchFolders { get; internal set; }

		/// <summary>Watch folders whose path matched a mapping.</summary>
		public int WatchFoldersRemapped { get; internal set; }

		/// <summary>Watch folders that were switched off in the copy (the MVP does not watch folders).</summary>
		public int WatchFoldersDisabled { get; internal set; }

		public long SourceBytes { get; internal set; }

		public long OutputBytes { get; internal set; }

		public TimeSpan Elapsed { get; internal set; }

		/// <summary>Where the app moved the library it replaced (set by the caller that does the replacing), or null.</summary>
		public string BackupFile { get; set; }

		public string Summary
		{
			get
			{
				return $"{Books} books, {BooksRemapped} remapped ({BooksFound} of them found on disk), {WatchFolders} watch folders ({WatchFoldersRemapped} remapped, {WatchFoldersDisabled} switched off), {Elapsed.TotalSeconds:0.0} s";
			}
		}
	}

	/// <summary>
	/// Copies a ComicRack database (a Windows <c>ComicDb.xml</c>) and rewrites the file paths while copying. The source is only
	/// read; the copy is written next to its final name and moved into place when complete, so a failure or a cancel leaves no
	/// half-written file. The paths are changed in the XML, so the Core never sees the Windows paths (loading them first and
	/// remapping afterwards would raise a FileRenamed event for every book and create a file watcher for every watch folder).
	///
	/// What is rewritten: the <c>File</c> attribute of <c>ComicDatabase/Books/Book</c> and the <c>Folder</c> attribute of
	/// <c>ComicDatabase/WatchFolders/WatchFolder</c>. Watch folders are switched off (<c>Watch="false"</c>): the MVP does not
	/// watch folders (a recursive FSEvents watch over a big library is unmeasured, and nothing in the Mac app reacts to it).
	/// Everything else is copied unchanged, as XML (attribute order, element order and text stay; formatting may differ).
	/// </summary>
	public static class LibraryImporter
	{
		private const int CheckEvery = 256;

		/// <summary>Imports <paramref name="sourceFile"/> into <paramref name="destinationFile"/>.</summary>
		/// <param name="progress">Receives the number of books copied so far, every 256 books and at the end.</param>
		/// <exception cref="FileNotFoundException">The source does not exist.</exception>
		/// <exception cref="InvalidDataException">The source is not XML, or not a ComicRack database.</exception>
		/// <exception cref="OperationCanceledException">Cancelled; the destination is unchanged.</exception>
		public static ImportResult Import(string sourceFile, string destinationFile, IReadOnlyList<PathRemapRule> rules, IProgress<int> progress = null, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(sourceFile))
			{
				throw new ArgumentException("No database file given.", nameof(sourceFile));
			}
			rules = rules ?? new PathRemapRule[0];
			string source = Path.GetFullPath(sourceFile);
			string destination = Path.GetFullPath(destinationFile);
			if (!File.Exists(source))
			{
				throw new FileNotFoundException("The database file does not exist: " + source, source);
			}
			if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
			{
				throw new ArgumentException("The source and the destination are the same file: " + source);
			}
			Directory.CreateDirectory(Path.GetDirectoryName(destination));
			string part = destination + ".part";
			var result = new ImportResult { SourceBytes = new FileInfo(source).Length };
			var sw = Stopwatch.StartNew();
			try
			{
				using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
				using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
				{
					Copy(input, output, rules, result, progress, cancellationToken);
				}
				result.OutputBytes = new FileInfo(part).Length;
				File.Move(part, destination, overwrite: true);
			}
			catch
			{
				try
				{
					File.Delete(part);
				}
				catch (Exception)
				{
				}
				throw;
			}
			result.Elapsed = sw.Elapsed;
			return result;
		}

		private static void Copy(Stream input, Stream output, IReadOnlyList<PathRemapRule> rules, ImportResult result, IProgress<int> progress, CancellationToken ct)
		{
			var readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = false, XmlResolver = null };
			var writerSettings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, CloseOutput = false };
			var names = new string[64]; // local name of the element open at each depth
			bool sawRoot = false;
			try
			{
				using (XmlReader reader = XmlReader.Create(input, readerSettings))
				using (XmlWriter writer = XmlWriter.Create(output, writerSettings))
				{
					writer.WriteStartDocument();
					while (reader.Read())
					{
						switch (reader.NodeType)
						{
						case XmlNodeType.Element:
						{
							int depth = reader.Depth;
							if (depth == 0)
							{
								if (reader.LocalName != "ComicDatabase")
								{
									throw new InvalidDataException("This is not a ComicRack database: the root element is <" + reader.LocalName + ">, expected <ComicDatabase>.");
								}
								sawRoot = true;
							}
							if (depth < names.Length)
							{
								names[depth] = reader.LocalName;
							}
							bool isBook = depth == 2 && reader.LocalName == "Book" && names[1] == "Books";
							bool isWatch = depth == 2 && reader.LocalName == "WatchFolder" && names[1] == "WatchFolders";
							bool empty = reader.IsEmptyElement;
							writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
							if (reader.HasAttributes)
							{
								while (reader.MoveToNextAttribute())
								{
									string value = reader.Value;
									if (reader.NamespaceURI.Length == 0 && reader.Prefix.Length == 0)
									{
										if (isBook && reader.LocalName == "File")
										{
											string mapped = PathRemapRule.MapFirst(rules, value);
											if (mapped != null)
											{
												value = mapped;
												result.BooksRemapped++;
												if (File.Exists(mapped))
												{
													result.BooksFound++;
												}
											}
										}
										else if (isWatch && reader.LocalName == "Folder")
										{
											string mapped = PathRemapRule.MapFirst(rules, value);
											if (mapped != null)
											{
												value = mapped;
												result.WatchFoldersRemapped++;
											}
										}
										else if (isWatch && reader.LocalName == "Watch")
										{
											if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
											{
												value = "false";
												result.WatchFoldersDisabled++;
											}
										}
									}
									writer.WriteStartAttribute(reader.Prefix, reader.LocalName, reader.NamespaceURI);
									writer.WriteString(value);
									writer.WriteEndAttribute();
								}
								reader.MoveToElement();
							}
							if (isBook)
							{
								result.Books++;
								if (result.Books % CheckEvery == 0)
								{
									ct.ThrowIfCancellationRequested();
									progress?.Report(result.Books);
								}
							}
							else if (isWatch)
							{
								result.WatchFolders++;
							}
							if (empty)
							{
								writer.WriteEndElement();
							}
							break;
						}
						case XmlNodeType.EndElement:
							writer.WriteEndElement();
							break;
						case XmlNodeType.Text:
							writer.WriteString(reader.Value);
							break;
						case XmlNodeType.Whitespace:
						case XmlNodeType.SignificantWhitespace:
							writer.WriteWhitespace(reader.Value);
							break;
						case XmlNodeType.CDATA:
							writer.WriteCData(reader.Value);
							break;
						case XmlNodeType.Comment:
							writer.WriteComment(reader.Value);
							break;
						case XmlNodeType.ProcessingInstruction:
							writer.WriteProcessingInstruction(reader.Name, reader.Value);
							break;
						}
					}
					writer.WriteEndDocument();
					writer.Flush();
				}
			}
			catch (XmlException ex)
			{
				throw new InvalidDataException("The file is not a valid XML database: " + ex.Message, ex);
			}
			if (!sawRoot)
			{
				throw new InvalidDataException("The file is empty.");
			}
			ct.ThrowIfCancellationRequested();
			progress?.Report(result.Books);
		}
	}
}
