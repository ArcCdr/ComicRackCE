using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using cYo.Common.ComponentModel;
using cYo.Common.Drawing;
using cYo.Common.Net;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using cYo.Projects.ComicRack.Engine.IO;
using cYo.Projects.ComicRack.Engine.IO.Cache;
using cYo.Projects.ComicRack.Plugins.Automation;

namespace ComicRack.Mac.Host.Scripting
{
	/// <summary>
	/// <c>ComicRack.App</c> for scripts (<see cref="IApplication"/>), over the open <see cref="MacHost"/>: the library, the page and thumbnail cache and the icons
	/// come from the host, everything that needs a window goes to the current <see cref="IScriptUi"/>. Each method does what the WinForms <c>MainForm</c> does
	/// (the citations are in the comments); a failing page or thumbnail request answers null, like the original.
	/// </summary>
	public sealed class MacScriptApplication : IApplication
	{
		private readonly MacHost host;
		private readonly Func<IScriptUi> ui;

		public MacScriptApplication(MacHost host, Func<IScriptUi> ui)
		{
			this.host = host ?? throw new ArgumentNullException(nameof(host));
			this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
		}

		/// <summary>The version the WinForms program reports (<c>Control.ProductVersion</c> of its assembly), see <see cref="ScriptHost.ProductVersion"/>.</summary>
		public string ProductVersion => ScriptHost.ProductVersion;

		public void Restart() => ui().Restart(); // MainForm.cs:4195

		public void SynchronizeDevices() => host.QueueManager.SynchronizeDevices(); // MainForm.cs:4205 (it also stores the workspace first)

		public void ScanFolders() => ui().ScanFolders(); // MainForm.cs:4200

		/// <summary>The original's <c>MainForm</c> throws here; the books of <c>ComicRack.ReadDatabaseBooks</c> are the ones this returns.</summary>
		public IEnumerable<ComicBook> ReadDatabaseBooks(string file) => ComicDatabase.LoadXml(file).Books;

		public IEnumerable<ComicBook> GetLibraryBooks() => host.DatabaseManager.Database.Books.ToArray(); // MainForm.cs:4299

		public ComicBook AddNewBook(bool showDialog) // MainForm.cs:1879
		{
			ComicBook book = new ComicBook { AddedTime = DateTime.Now };
			if (showDialog && !ui().EditNewBook(book))
			{
				return null;
			}
			host.DatabaseManager.Database.Add(book);
			return book;
		}

		public bool RemoveBook(ComicBook cb) => host.DatabaseManager.Database.Remove(cb); // MainForm.cs:4304

		public bool SetCustomBookThumbnail(ComicBook cb, Bitmap bitmap) // MainForm.cs:4309
		{
			if (cb.IsLinked)
			{
				return false;
			}
			cb.CustomThumbnailKey = host.ImagePool.AddCustomThumbnail(bitmap);
			return true;
		}

		public ComicBook GetBook(string file) => host.DatabaseManager.BookFactory.Create(file, CreateBookOption.AddToTemporary); // MainForm.cs:4319

		public Bitmap GetComicPage(ComicBook cb, int page) // MainForm.cs:4229
		{
			try
			{
				using (IItemLock<PageImage> itemLock = host.ImagePool.GetPage(cb.GetPageKey(page, BitmapAdjustment.Empty), cb))
				{
					if (itemLock == null || itemLock.Item == null || itemLock.Item.Bitmap == null)
					{
						return null;
					}
					return itemLock.Item.Bitmap.Clone() as Bitmap;
				}
			}
			catch
			{
				return null;
			}
		}

		public Bitmap GetComicThumbnail(ComicBook cb, int page) // MainForm.cs:4248
		{
			try
			{
				using (IItemLock<ThumbnailImage> itemLock = host.ImagePool.GetThumbnail(cb.GetThumbnailKey(page), cb))
				{
					if (itemLock == null || itemLock.Item == null || itemLock.Item.Bitmap == null)
					{
						return null;
					}
					return itemLock.Item.Bitmap.Clone() as Bitmap;
				}
			}
			catch
			{
				return null;
			}
		}

		public IDictionary<string, string> GetComicFields() => ComicBook.GetTranslatedWritableStringProperties(); // MainForm.cs:4289

		public Bitmap GetComicPublisherIcon(ComicBook cb) // MainForm.cs:4267
		{
			return Copy(ComicBook.PublisherIcons.GetImage(cb.GetPublisherIconKey()) ?? ComicBook.PublisherIcons.GetImage(cb.Publisher));
		}

		public Bitmap GetComicImprintIcon(ComicBook cb) // MainForm.cs:4273
		{
			return Copy(ComicBook.PublisherIcons.GetImage(cb.GetImprintIconKey()) ?? ComicBook.PublisherIcons.GetImage(cb.Imprint));
		}

		public Bitmap GetComicAgeRatingIcon(ComicBook cb) => Copy(ComicBook.AgeRatingIcons.GetImage(cb.AgeRating)); // MainForm.cs:4279

		public Bitmap GetComicFormatIcon(ComicBook cb) => Copy(ComicBook.FormatIcons.GetImage(cb.Format)); // MainForm.cs:4284

		/// <summary>The original copies the icon without looking; a missing icon is null here, where the original would fail.</summary>
		private static Bitmap Copy(Image image) => image?.CreateCopy(alwaysTrueCopy: true);

		public string ReadInternet(string url) => HttpAccess.ReadText(url); // MainForm.cs:4294

		public int AskQuestion(string question, string buttonText, string optionText) => ui().AskQuestion(question, buttonText, optionText); // MainForm.cs:4216

		public void ShowComicInfo(IEnumerable<ComicBook> books) // MainForm.cs:4344
		{
			ComicBook[] editable = (books ?? Enumerable.Empty<ComicBook>()).Where(cb => cb.EditMode.CanEditProperties()).ToArray();
			if (editable.Length > 0)
			{
				ui().ShowComicInfo(editable);
			}
		}
	}

	/// <summary><c>ComicRack.Browser</c> for scripts (<see cref="IBrowser"/>): moving through the book list of the browser and selecting books, by the current <see cref="IScriptUi"/>.</summary>
	public sealed class MacScriptBrowser : IBrowser
	{
		private readonly Func<IScriptUi> ui;

		public MacScriptBrowser(Func<IScriptUi> ui)
		{
			this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
		}

		public bool OpenNextComic() => ui().OpenNextComic(1);

		public bool OpenPrevComic() => ui().OpenNextComic(-1);

		public bool OpenRandomComic() => ui().OpenNextComic(0);

		public void SelectComics(IEnumerable<ComicBook> books) => ui().SelectComics(books);
	}

	/// <summary><c>ComicRack.OpenBooks</c> for scripts (<see cref="IOpenBooksManager"/>), by the current <see cref="IScriptUi"/>.</summary>
	public sealed class MacScriptOpenBooks : IOpenBooksManager
	{
		private readonly Func<IScriptUi> ui;

		public MacScriptOpenBooks(Func<IScriptUi> ui)
		{
			this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
		}

		public bool Open(ComicBook cb, bool inNewSlot, int page) => ui().OpenBook(cb, inNewSlot, page);

		public bool OpenFile(string file, bool inNewSlot, int page) => ui().OpenFile(file, inNewSlot, page);

		public bool IsOpen(ComicBook cb) => ui().IsOpen(cb);
	}
}
