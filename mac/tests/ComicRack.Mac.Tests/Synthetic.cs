using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ComicRack.Mac.Tests
{
	/// <summary>Small stand-ins for a library, made in a test's own temp folder: CBZ files and Windows-style ComicDb.xml files.</summary>
	public static class Synthetic
	{
		// A valid 1 x 1 PNG.
		private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

		/// <summary>Writes <paramref name="count"/> CBZ files of 3 pages each under <paramref name="folder"/>/Series NN/. Returns the number of files.</summary>
		public static int CreateCbzFolder(string folder, int count)
		{
			for (int i = 0; i < count; i++)
			{
				string dir = Path.Combine(folder, "Series " + (i / 5 + 1).ToString("00"));
				Directory.CreateDirectory(dir);
				using (var zip = ZipFile.Open(Path.Combine(dir, $"Book {i + 1:000}.cbz"), ZipArchiveMode.Create))
				{
					for (int p = 1; p <= 3; p++)
					{
						ZipArchiveEntry entry = zip.CreateEntry(p.ToString("000") + ".png", CompressionLevel.NoCompression);
						using (Stream s = entry.Open())
						{
							s.Write(Png, 0, Png.Length);
						}
					}
				}
			}
			return count;
		}

		/// <summary>
		/// The text of a Windows ComicDb.xml with <paramref name="books"/> books under <c>S:\media\BD\Series NN\</c>, one book outside that
		/// prefix, a watch folder that is switched on, and a list item that contains an element called Book (which must not be touched; left out with <paramref name="decoy"/> false, for files the Core must load).
		/// Written the way the fixture is: single quoted XML declaration, an xsi namespace, xsi:type attributes.
		/// </summary>
		public static string WindowsDb(int books, bool decoy = true)
		{
			var sb = new StringBuilder();
			sb.AppendLine("<?xml version='1.0' encoding='utf-8'?>");
			sb.AppendLine("<ComicDatabase xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" Id=\"cf4f6499-c21a-4b02-8bf2-18eca0f8bdfe\">");
			sb.AppendLine("  <Books>");
			for (int i = 0; i < books; i++)
			{
				string id = new Guid(i + 1, 0, 0, new byte[8]).ToString();
				sb.AppendLine($"    <Book Id=\"{id}\" File=\"S:\\media\\BD\\Series {i / 5 + 1:00}\\Book {i + 1:000}.cbz\">");
				sb.AppendLine($"      <Title>Titre &amp; sous-titre \u00E9\u00E8 {i + 1}</Title>");
				sb.AppendLine("      <Series>Series</Series>");
				sb.AppendLine("      <Number>" + (i + 1) + "</Number>");
				sb.AppendLine("    </Book>");
			}
			sb.AppendLine("    <Book Id=\"" + new Guid(99999, 0, 0, new byte[8]) + "\" File=\"D:\\other\\Elsewhere.cbz\"><Title>Outside</Title></Book>");
			sb.AppendLine("  </Books>");
			sb.AppendLine("  <ComicLists>");
			sb.AppendLine("    <Item xsi:type=\"ComicLibraryListItem\" Id=\"7b3a5deb-4b93-4321-88ce-eb84bc8cef57\" Name=\"Library\">");
			if (decoy)
			{
				sb.AppendLine("      <Book File=\"S:\\media\\BD\\not-a-book-entry.cbz\" />");
			}
			sb.AppendLine("    </Item>");
			sb.AppendLine("  </ComicLists>");
			sb.AppendLine("  <WatchFolders>");
			sb.AppendLine("    <WatchFolder Folder=\"S:\\media\\BD\" Watch=\"true\" />");
			sb.AppendLine("    <WatchFolder Folder=\"S:\\incoming\" Watch=\"true\" />");
			sb.AppendLine("  </WatchFolders>");
			sb.AppendLine("  <BlackList />");
			sb.AppendLine("</ComicDatabase>");
			return sb.ToString();
		}
	}
}
