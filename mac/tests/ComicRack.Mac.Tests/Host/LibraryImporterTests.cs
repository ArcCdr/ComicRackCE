using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using Xunit;

namespace ComicRack.Mac.Tests.Host
{
	public class LibraryImporterTests
	{
		private static readonly PathRemapRule[] BdRule = { new PathRemapRule(@"S:\media\BD\", "/mac/BD/") };

		private static string Write(TempDir temp, string name, string content)
		{
			string path = temp.Combine(name);
			File.WriteAllText(path, content, new UTF8Encoding(false));
			return path;
		}

		[Fact]
		public void Remaps_book_files_and_watch_folders_and_changes_nothing_else()
		{
			using (var temp = new TempDir())
			{
				string source = Write(temp, "ComicDb.xml", Synthetic.WindowsDb(12));
				byte[] sourceBytes = File.ReadAllBytes(source);
				string dest = temp.Combine("out/ComicDb.xml");
				var progress = new System.Collections.Generic.List<int>();

				ImportResult r = LibraryImporter.Import(source, dest, new[] { new PathRemapRule(@"S:\media\BD\", "/mac/BD/"), new PathRemapRule(@"S:\media\BD", "/mac/BD") }, new Progress<int>(progress.Add));

				Assert.Equal(13, r.Books);
				Assert.Equal(12, r.BooksRemapped);
				Assert.Equal(0, r.BooksFound); // /mac/BD does not exist
				Assert.Equal(2, r.WatchFolders);
				Assert.Equal(1, r.WatchFoldersRemapped);
				Assert.Equal(2, r.WatchFoldersDisabled);
				Assert.Equal(sourceBytes, File.ReadAllBytes(source));
				Assert.False(File.Exists(dest + ".part"));

				XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
				XDocument inDoc = XDocument.Load(source);
				XDocument outDoc = XDocument.Load(dest);
				Assert.Equal("ComicDatabase", outDoc.Root.Name.LocalName);
				Assert.Equal(inDoc.Root.Attribute("Id").Value, outDoc.Root.Attribute("Id").Value);

				var inBooks = inDoc.Root.Element("Books").Elements("Book").ToList();
				var outBooks = outDoc.Root.Element("Books").Elements("Book").ToList();
				Assert.Equal(inBooks.Count, outBooks.Count);
				for (int i = 0; i < inBooks.Count; i++)
				{
					Assert.Equal(inBooks[i].Attribute("Id").Value, outBooks[i].Attribute("Id").Value);
					string f = inBooks[i].Attribute("File").Value;
					string expected = f.StartsWith(@"S:\media\BD\") ? "/mac/BD/" + f.Substring(@"S:\media\BD\".Length).Replace('\\', '/') : f;
					Assert.Equal(expected, outBooks[i].Attribute("File").Value);
					// Content, including & and accents, is unchanged.
					Assert.Equal(inBooks[i].Elements().Select(e => e.ToString()), outBooks[i].Elements().Select(e => e.ToString()));
				}
				Assert.Contains("Titre & sous-titre \u00E9\u00E8 1", outBooks[0].Element("Title").Value);

				// The element called Book inside a list item is not a book: untouched.
				Assert.Equal(@"S:\media\BD\not-a-book-entry.cbz", outDoc.Descendants("ComicLists").Single().Descendants("Book").Single().Attribute("File").Value);
				// xsi:type survives with its namespace.
				Assert.Equal("ComicLibraryListItem", outDoc.Descendants("Item").Single().Attribute(xsi + "type").Value);

				var watch = outDoc.Root.Element("WatchFolders").Elements("WatchFolder").ToList();
				Assert.Equal("/mac/BD", watch[0].Attribute("Folder").Value);
				Assert.Equal(@"S:\incoming", watch[1].Attribute("Folder").Value);
				Assert.All(watch, w => Assert.Equal("false", w.Attribute("Watch").Value));
				Assert.NotEmpty(progress);
				Assert.Equal(13, progress.Last());
			}
		}

		[Fact]
		public void Counts_the_remapped_books_that_exist_on_disk()
		{
			using (var temp = new TempDir())
			{
				Synthetic.CreateCbzFolder(temp.Combine("BD"), 5);
				string source = Write(temp, "ComicDb.xml", Synthetic.WindowsDb(5));
				ImportResult r = LibraryImporter.Import(source, temp.Combine("o.xml"), new[] { new PathRemapRule(@"S:\media\BD\", temp.Combine("BD") + "/") });
				Assert.Equal(5, r.BooksRemapped);
				Assert.Equal(5, r.BooksFound);
			}
		}

		[Fact]
		public void The_core_loads_the_copy_with_the_new_paths()
		{
			using (var temp = new TempDir())
			{
				string books = temp.Combine("BD");
				Synthetic.CreateCbzFolder(books, 7);
				string source = Write(temp, "ComicDb.xml", Synthetic.WindowsDb(7, decoy: false));
				string dest = temp.Combine("copy/ComicDb.xml");
				LibraryImporter.Import(source, dest, new[] { new PathRemapRule(@"S:\media\BD\", books + "/") });
				using (MacHost host = MacHost.Open(new MacHostOptions { Paths = MacPaths.Under(temp.Combine("host")), DatabaseFile = dest, DatabaseReadOnly = true }))
				{
					Assert.Null(host.OpenMessage);
					var list = host.DatabaseManager.Database.Books.ToArray();
					Assert.Equal(8, list.Length);
					Assert.Equal(7, list.Count(b => b.FilePath.StartsWith(books + "/") && File.Exists(b.FilePath)));
					Assert.Contains(list, b => b.FilePath == @"D:\other\Elsewhere.cbz");
					Assert.Equal(2, host.DatabaseManager.Database.WatchFolders.Count);
					Assert.All(host.DatabaseManager.Database.WatchFolders, w => Assert.False(w.Watch));
				}
			}
		}

		[Fact]
		public void Without_a_mapping_the_file_is_copied_as_it_is()
		{
			using (var temp = new TempDir())
			{
				string source = Write(temp, "ComicDb.xml", Synthetic.WindowsDb(3));
				ImportResult r = LibraryImporter.Import(source, temp.Combine("o.xml"), new PathRemapRule[0]);
				Assert.Equal(0, r.BooksRemapped);
				Assert.Equal(4, r.Books);
				Assert.Equal(@"S:\media\BD\Series 01\Book 001.cbz", XDocument.Load(temp.Combine("o.xml")).Descendants("Book").First().Attribute("File").Value);
			}
		}

		[Fact]
		public void A_cancel_leaves_the_destination_as_it_was_and_no_partial_file()
		{
			using (var temp = new TempDir())
			{
				string source = Write(temp, "ComicDb.xml", Synthetic.WindowsDb(2000));
				string dest = Write(temp, "dest.xml", "<ComicDatabase />");
				using (var cts = new CancellationTokenSource())
				{
					var progress = new SyncProgress<int>(n =>
					{
						if (n >= 512)
						{
							cts.Cancel();
						}
					});
					Assert.ThrowsAny<OperationCanceledException>(() => LibraryImporter.Import(source, dest, BdRule, progress, cts.Token));
				}
				Assert.Equal("<ComicDatabase />", File.ReadAllText(dest));
				Assert.False(File.Exists(dest + ".part"));
			}
		}

		[Theory]
		[InlineData("<Something><Books /></Something>", "not a ComicRack database")]
		[InlineData("<ComicDatabase><Books><Book File='x'></ComicDatabase>", "not a valid XML")]
		[InlineData("this is not xml", "not a valid XML")]
		[InlineData("", "")]
		public void Refuses_a_file_that_is_not_a_comicrack_database_and_changes_nothing(string content, string message)
		{
			using (var temp = new TempDir())
			{
				string source = Write(temp, "bad.xml", content);
				string dest = Write(temp, "dest.xml", "<keep />");
				var ex = Assert.ThrowsAny<Exception>(() => LibraryImporter.Import(source, dest, BdRule));
				Assert.True(ex is InvalidDataException || ex is System.Xml.XmlException, ex.GetType().Name);
				if (message.Length > 0)
				{
					Assert.Contains(message, ex.Message);
				}
				Assert.Equal("<keep />", File.ReadAllText(dest));
				Assert.False(File.Exists(dest + ".part"));
			}
		}

		[Fact]
		public void Refuses_a_missing_source_and_the_same_file()
		{
			using (var temp = new TempDir())
			{
				Assert.Throws<FileNotFoundException>(() => LibraryImporter.Import(temp.Combine("nope.xml"), temp.Combine("o.xml"), BdRule));
				string source = Write(temp, "ComicDb.xml", Synthetic.WindowsDb(1));
				Assert.Throws<ArgumentException>(() => LibraryImporter.Import(source, source, BdRule));
				Assert.Throws<ArgumentException>(() => LibraryImporter.Import(source, source.ToUpperInvariant().Replace(".XML", ".xml").Replace(temp.Path.ToUpperInvariant(), temp.Path), BdRule));
				Assert.Equal(Synthetic.WindowsDb(1), File.ReadAllText(source));
			}
		}

		[Fact]
		public void A_read_only_source_is_fine_and_stays_read_only()
		{
			using (var temp = new TempDir())
			{
				string source = Write(temp, "ComicDb.xml", Synthetic.WindowsDb(2));
				File.SetUnixFileMode(source, UnixFileMode.UserRead);
				try
				{
					LibraryImporter.Import(source, temp.Combine("o.xml"), BdRule);
					Assert.Equal(UnixFileMode.UserRead, File.GetUnixFileMode(source));
				}
				finally
				{
					File.SetUnixFileMode(source, UnixFileMode.UserRead | UnixFileMode.UserWrite);
				}
			}
		}

		/// <summary>Calls the handler on the reporting thread (Progress&lt;T&gt; would post it to a context).</summary>
		private sealed class SyncProgress<T> : IProgress<T>
		{
			private readonly Action<T> handler;

			public SyncProgress(Action<T> handler)
			{
				this.handler = handler;
			}

			public void Report(T value) => handler(value);
		}
	}
}
