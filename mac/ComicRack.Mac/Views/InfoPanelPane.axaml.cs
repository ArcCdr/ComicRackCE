using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Info;
using ComicRack.Mac.ViewModels;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// <c>ComicPageContainerControl</c>: a tab for every page of the <see cref="InfoPanelViewModel"/> (text and icon of the page, <c>TabBarItem</c> with the page as its tag)
	/// and the content of the page whose tab is selected. The strip shows with two pages or more.
	/// </summary>
	public partial class InfoPanelPane : UserControl
	{
		private InfoPanelViewModel model;
		private bool syncing;
		private readonly Dictionary<InfoPageItem, CrTabItem> tabs = new Dictionary<InfoPageItem, CrTabItem>();

		public InfoPanelPane()
		{
			InitializeComponent();
			InfoTabBar.SelectedTabChanged += (sender, e) =>
			{
				if (!syncing && model != null)
				{
					model.SelectedIndex = e.Index;
				}
			};
		}

		/// <summary>The tab strip (<c>tabBar</c>).</summary>
		public CrTabBar TabBar => InfoTabBar;

		/// <summary>The place where the page that is showing sits.</summary>
		public Border PageHost => InfoPageHost;

		protected override void OnDataContextChanged(EventArgs e)
		{
			base.OnDataContextChanged(e);
			if (model != null)
			{
				model.PropertyChanged -= OnModelChanged;
				((INotifyCollectionChanged)model.Pages).CollectionChanged -= OnPagesChanged;
			}
			model = DataContext as InfoPanelViewModel;
			if (model != null)
			{
				model.PropertyChanged += OnModelChanged;
				((INotifyCollectionChanged)model.Pages).CollectionChanged += OnPagesChanged;
			}
			Rebuild();
		}

		private void OnModelChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(InfoPanelViewModel.SelectedPage) || e.PropertyName == nameof(InfoPanelViewModel.SelectedIndex))
			{
				SyncSelection();
			}
		}

		private void OnPagesChanged(object sender, NotifyCollectionChangedEventArgs e) => Rebuild();

		private void Rebuild()
		{
			syncing = true;
			try
			{
				foreach (KeyValuePair<InfoPageItem, CrTabItem> pair in tabs)
				{
					pair.Key.PropertyChanged -= OnPageChanged;
				}
				tabs.Clear();
				InfoTabBar.Tabs.Clear();
				if (model != null)
				{
					foreach (InfoPageItem page in model.Pages)
					{
						var tab = new CrTabItem { Text = page.Title, IconName = page.IconName, CanClose = false, MinimumWidth = 100, Tag = page };
						tabs[page] = tab;
						InfoTabBar.Tabs.Add(tab);
						page.PropertyChanged += OnPageChanged;
					}
				}
			}
			finally
			{
				syncing = false;
			}
			SyncSelection();
		}

		private void OnPageChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(InfoPageItem.Title) && sender is InfoPageItem page && tabs.TryGetValue(page, out CrTabItem tab))
			{
				tab.Text = page.Title;
			}
		}

		// the tab of the selected page is selected and its content shows (c.Visible = item == e.NewItem)
		private void SyncSelection()
		{
			int index = model == null ? -1 : model.SelectedIndex;
			syncing = true;
			try
			{
				if (InfoTabBar.SelectedIndex != index)
				{
					InfoTabBar.SelectedIndex = index;
				}
			}
			finally
			{
				syncing = false;
			}
			InfoPageHost.Child = model?.SelectedPage?.Page.View;
		}
	}
}
