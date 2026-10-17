using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using ComicRack.Mac.Services;
using ComicRack.Mac.ViewModels;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The Import Windows Library dialog. Closes with an <see cref="ImportRequest"/> or null. The file picker of the "Choose..."
	/// button is the only part that needs the real StorageProvider; the validation is in <see cref="ImportViewModel"/>.
	/// </summary>
	public partial class ImportWindow : Window
	{
		public ImportWindow()
		{
			InitializeComponent();
			DataContext = new ImportViewModel();
			this.FindControl<Button>("ImportButton").Click += OnImport;
			this.FindControl<Button>("CancelButton").Click += (sender, e) => Close(null);
			this.FindControl<Button>("BrowseButton").Click += OnBrowse;
		}

		public ImportViewModel ViewModel => (ImportViewModel)DataContext;

		private void OnImport(object sender, RoutedEventArgs e)
		{
			ImportRequest request = ViewModel.TryCreateRequest();
			if (request != null)
			{
				Close(request);
			}
		}

		private async void OnBrowse(object sender, RoutedEventArgs e)
		{
			try
			{
				IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
				{
					Title = "Choose the ComicDb.xml of the Windows installation",
					AllowMultiple = false,
					FileTypeFilter = new[] { new FilePickerFileType("ComicRack database") { Patterns = new[] { "*.xml" } } },
				});
				if (files.Count > 0)
				{
					string path = files[0].TryGetLocalPath();
					if (path != null)
					{
						ViewModel.DatabasePath = path;
					}
				}
			}
			catch (Exception ex)
			{
				ViewModel.ErrorText = "The file picker failed: " + ex.Message;
			}
		}
	}
}
