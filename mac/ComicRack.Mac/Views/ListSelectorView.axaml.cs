using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ComicRack.Mac.Editor;
using cYo.Common.Localize;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The popup that opens next to a text box with a list (<c>ListSelectorControl</c>): the Lists view (the values the box holds above, the other values of the library below,
	/// four buttons to move them), the Check view (every value with a check box) and the Text view. The buttons at the bottom choose the view; the last choice is kept for the next
	/// popup. <see cref="ListSelectorModel"/> holds the values and gives <see cref="ListSelectorModel.Result"/>, which the box takes when the popup closes.
	/// </summary>
	public partial class ListSelectorView : UserControl
	{
		private ListSelectorModel model;
		private bool filling;

		public ListSelectorView()
		{
			InitializeComponent();
			Translate();
			btAllToOwn.Click += (sender, e) => Do(model.AddAll);
			btAllToPool.Click += (sender, e) => Do(model.RemoveAll);
			btSelectedToOwn.Click += (sender, e) => Do(() => model.Add(PoolList.SelectedItems.OfType<string>().ToList()));
			btSelectedToPool.Click += (sender, e) => Do(() => model.Remove(OwnList.SelectedItems.OfType<string>().ToList()));
			OwnList.SelectionChanged += (sender, e) => UpdateButtons();
			PoolList.SelectionChanged += (sender, e) => UpdateButtons();
			OwnList.DoubleTapped += (sender, e) => Do(() => model.Remove(OwnList.SelectedItems.OfType<string>().ToList()));
			PoolList.DoubleTapped += (sender, e) => Do(() => model.Add(PoolList.SelectedItems.OfType<string>().ToList()));
			OwnList.KeyDown += (sender, e) => EnterMovesSelection(e, () => model.Remove(OwnList.SelectedItems.OfType<string>().ToList()));
			PoolList.KeyDown += (sender, e) => EnterMovesSelection(e, () => model.Add(PoolList.SelectedItems.OfType<string>().ToList()));
			CheckView.SelectionChanged += OnCheckChanged;
			btLists.Click += (sender, e) => ShowTab(0);
			btCheck.Click += (sender, e) => ShowTab(1);
			btText.Click += (sender, e) => ShowTab(2);
		}

		private void Translate()
		{
			foreach (Button button in new[] { btAllToOwn, btSelectedToOwn, btSelectedToPool, btAllToPool, btLists, btCheck, btText })
			{
				if (button.Tag is string key && button.Content is string english)
				{
					button.Content = UiText.Get("ListSelectorControl", key, english);
				}
			}
		}

		public ListSelectorModel Model => model;

		public TextBox TextTab => TextView;

		public ListBox Own => OwnList;

		public ListBox Pool => PoolList;

		/// <summary>The values of the Check view and the ones that are checked, for tests.</summary>
		public IReadOnlyList<string> CheckItems => CheckView.Items.OfType<string>().ToList();

		public IReadOnlyList<string> Checked => CheckView.SelectedItems.OfType<string>().ToList();

		/// <summary>Shows the values of <paramref name="selector"/> and the tab it was left on.</summary>
		public void Attach(ListSelectorModel selector)
		{
			model = selector;
			ShowTab(model.Tab, fromText: false);
		}

		private void Do(Action move)
		{
			move();
			Fill();
		}

		private void EnterMovesSelection(KeyEventArgs e, Action move)
		{
			if (e.Key == Key.Return)
			{
				Do(move);
				e.Handled = true;
			}
		}

		private void ShowTab(int tab, bool fromText = true)
		{
			if (fromText && model.Tab == 2)
			{
				model.LeaveTextTab();
			}
			if (tab == 2)
			{
				model.EnterTextTab();
				TextView.Text = model.TextTabText;
			}
			model.Tab = tab;
			ListSelectorModel.LastTab = tab;
			ListsView.IsVisible = tab == 0;
			CheckFrame.IsVisible = tab == 1;
			TextView.IsVisible = tab == 2;
			Fill();
		}

		// the lists show the model
		private void Fill()
		{
			filling = true;
			try
			{
				OwnList.ItemsSource = model.Own.ToList();
				PoolList.ItemsSource = model.Pool.ToList();
				CheckView.ItemsSource = model.Own.Concat(model.Pool).ToList();
				foreach (string value in model.Own)
				{
					CheckView.SelectedItems.Add(value);
				}
			}
			finally
			{
				filling = false;
			}
			UpdateButtons();
		}

		private void OnCheckChanged(object sender, SelectionChangedEventArgs e)
		{
			if (filling)
			{
				return;
			}
			foreach (string value in e.AddedItems.OfType<string>())
			{
				model.SetChecked(value, true);
			}
			foreach (string value in e.RemovedItems.OfType<string>())
			{
				model.SetChecked(value, false);
			}
			filling = true;
			try
			{
				OwnList.ItemsSource = model.Own.ToList();
				PoolList.ItemsSource = model.Pool.ToList();
			}
			finally
			{
				filling = false;
			}
			UpdateButtons();
		}

		private void UpdateButtons()
		{
			if (model == null)
			{
				return;
			}
			btAllToOwn.IsEnabled = model.CanAddAll;
			btAllToPool.IsEnabled = model.CanRemoveAll;
			btSelectedToOwn.IsEnabled = PoolList.SelectedItems?.Count > 0;
			btSelectedToPool.IsEnabled = OwnList.SelectedItems?.Count > 0;
		}

		/// <summary>The text for the box when the popup closes (<c>PopupClosed</c>); reads the Text tab when it is the one showing.</summary>
		public string Result()
		{
			if (model.Tab == 2)
			{
				model.TextTabText = TextView.Text ?? string.Empty;
			}
			return model.Result;
		}
	}
}
