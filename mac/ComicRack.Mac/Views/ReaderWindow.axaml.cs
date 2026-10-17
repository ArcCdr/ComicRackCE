using System;
using Avalonia.Controls;
using Avalonia.Input;
using ComicRack.Mac.ViewModels;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// A reader window: the page view, a status strip and the native menu of the reader. The keys go to
	/// <see cref="ReaderViewModel.HandleKey"/>; Esc and Cmd+W close the window. Closing disposes the view model, which records the
	/// page in the book and releases the book (<see cref="Services.ReaderManager"/> does that through <see cref="Closed"/>; a window
	/// made without the manager, as the tests do, passes <paramref name="ownsModel"/>).
	/// </summary>
	public partial class ReaderWindow : Window
	{
		private readonly ReaderViewModel model;
		private readonly bool ownsModel;

		public ReaderWindow()
		{
			InitializeComponent();
		}

		public ReaderWindow(ReaderViewModel model, bool ownsModel = false)
		{
			InitializeComponent();
			this.model = model;
			this.ownsModel = ownsModel;
			DataContext = model;
			model.CloseRequested += OnCloseRequested;
			NativeMenu.SetMenu(this, ReaderMenu.Build(model));
			Opened += (sender, e) => ReadingArea.Focus();
		}

		public ReaderViewModel Model => model;

		public ReaderView ReadingArea => this.FindControl<ReaderView>("PageView");

		private void OnCloseRequested(object sender, EventArgs e)
		{
			Close();
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (!e.Handled && model != null && model.HandleKey(e.Key, e.KeyModifiers))
			{
				e.Handled = true;
			}
		}

		protected override void OnClosed(EventArgs e)
		{
			if (model != null)
			{
				model.CloseRequested -= OnCloseRequested;
				if (ownsModel)
				{
					model.Dispose();
				}
			}
			base.OnClosed(e);
		}
	}
}
