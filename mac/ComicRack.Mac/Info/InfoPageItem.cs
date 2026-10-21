using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Info
{
	/// <summary>
	/// A page in the container and what <c>ComicPageControl</c> adds to its content: the page that is not showing keeps the books it has to show, and gets them when it
	/// shows (<c>pendingUpdate</c>, <c>pendingBooks</c>, <c>OnVisibleChanged</c>).
	/// </summary>
	public sealed class InfoPageItem : ObservableObject
	{
		private bool visible;
		private bool pendingUpdate;
		private IReadOnlyList<ComicBook> pendingBooks;
		private string title;

		public InfoPageItem(IInfoPage page)
		{
			Page = page ?? throw new ArgumentNullException(nameof(page));
			title = page.Title ?? string.Empty;
			page.TitleChanged += OnTitleChanged;
		}

		public IInfoPage Page { get; }

		/// <summary>The text of the tab.</summary>
		public string Title
		{
			get => title;
			private set => SetProperty(ref title, value);
		}

		public string IconName => Page.IconName;

		/// <summary>The page is showing (its tab is selected and the panel is open).</summary>
		public bool IsShowing => visible;

		/// <summary>True while the page has books to show that it has not been given (it was not showing).</summary>
		public bool HasPendingUpdate => pendingUpdate;

		/// <summary><c>ComicPageControl.MarkAsDirty</c>: the page shows its books anew the next time it shows.</summary>
		public void MarkAsDirty() => pendingUpdate = true;

		/// <summary><c>ComicPageControl.ShowInfo</c>: shows the books at once if the page is showing, else remembers them.</summary>
		public void ShowInfo(IEnumerable<ComicBook> books)
		{
			pendingBooks = null;
			IReadOnlyList<ComicBook> list = books?.ToArray() ?? new ComicBook[0];
			if (visible)
			{
				Page.ShowInfo(list);
				pendingUpdate = false;
				pendingBooks = null;
			}
			else
			{
				pendingUpdate = true;
				pendingBooks = list;
			}
		}

		/// <summary><c>OnVisibleChanged</c>: a page that starts to show gets what it was waiting for.</summary>
		public void SetShowing(bool showing)
		{
			if (visible == showing)
			{
				return;
			}
			visible = showing;
			if (showing)
			{
				if (pendingUpdate)
				{
					Page.ShowInfo(pendingBooks ?? new ComicBook[0]);
				}
				pendingUpdate = false;
				pendingBooks = null;
			}
			OnPropertyChanged(nameof(IsShowing));
		}

		internal void Detach() => Page.TitleChanged -= OnTitleChanged;

		private void OnTitleChanged(object sender, EventArgs e) => Title = Page.Title ?? string.Empty;
	}
}
