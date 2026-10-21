using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using ComicRack.Mac.Services;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Info
{
	/// <summary>
	/// The page that stands in for a script's page until P4.11a: it shows how many books are selected. It only exists behind the hidden option
	/// <c>--test-info-page</c> (private data folder needed), for the tests and the e2e case <c>info-panel</c>; the original has no page of its own.
	/// </summary>
	public sealed class TestInfoPage : IInfoPage
	{
		private TextBlock label;
		private string title;
		private int count = -1;

		public TestInfoPage(string title = "Test Page")
		{
			this.title = title;
		}

		public string Title
		{
			get => title;
			set
			{
				title = value;
				TitleChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		public string IconName => null;

		/// <summary>The number of books of the last <see cref="ShowInfo"/>, or -1 before any.</summary>
		public int BookCount => count;

		/// <summary>How many times <see cref="ShowInfo"/> was called.</summary>
		public int Calls { get; private set; }

		public event EventHandler TitleChanged;

		public Control View
		{
			get
			{
				if (label == null)
				{
					label = new TextBlock { Text = Describe(), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
				}
				return label;
			}
		}

		public void ShowInfo(IReadOnlyList<ComicBook> books)
		{
			Calls++;
			count = books?.Count ?? 0;
			AppLog.Info("Info panel: " + title + " shows " + count + " book(s).");
			if (label != null)
			{
				label.Text = Describe();
			}
		}

		private string Describe() => count < 0 ? title : title + ": " + count + " book(s) selected";
	}
}
