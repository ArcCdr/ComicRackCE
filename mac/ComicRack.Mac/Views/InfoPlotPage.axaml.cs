using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Host.Library;
using cYo.Common.Localize;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The Plot &amp; Notes tab of the Info dialog (<c>tabPlot</c>, IN.03): the sub-tabs Summary, Notes and Review above, then Characters, Teams, Locations and Main Character or Team (the
	/// boxes with a list selector), Scan Information (a book with a file) and the web address. The three text boxes take the line endings of the Windows edit box, so what is saved
	/// is the same text on every system.
	/// </summary>
	public partial class InfoPlotPage : UserControl
	{
		private BookEditorModel model;
		private readonly Dictionary<Control, EditorList> lists = new Dictionary<Control, EditorList>();
		private readonly HashSet<Control> filled = new HashSet<Control>();

		public InfoPlotPage()
		{
			InitializeComponent();
			InfoPageText.Translate(this);
			// the sub-tabs are drawn by the Win32 tab control: the text as it is
			tabPageSummary.Header = UiText.Raw(InfoPageText.Section, "tabPageSummary", "Summary");
			tabPageNotes.Header = UiText.Raw(InfoPageText.Section, "tabPageNotes", "Notes");
			tabPageReview.Header = UiText.Raw(InfoPageText.Section, "tabPageReview", "Review");
		}

		/// <summary>The sub-tabs (<c>tabNotes</c>): Summary, Notes and Review.</summary>
		public TabControl NotesTabs => tabNotes;

		/// <summary>The boxes whose values come from the library.</summary>
		public IReadOnlyDictionary<Control, EditorList> ListBoxes => lists;

		/// <summary>The multi-line boxes: Summary, Notes, Review (any number of lines) and Characters (wraps, no line breaks).</summary>
		public IEnumerable<TextBox> MultilineBoxes => new[] { txSummary, txNotes, txReview, txCharacters };

		public void Initialize(BookEditorModel editor)
		{
			model = editor;
			DataContext = editor;
			Map(txTeams, EditorList.Teams);
			Map(txLocations, EditorList.Locations);
			Map(txMainCharacterOrTeam, EditorList.MainCharacterOrTeam);
			Map(txScanInformation, EditorList.ScanInformation);
			foreach (Control box in lists.Keys)
			{
				box.GotFocus += (sender, e) => EnsureList((Control)sender);
				box.AddHandler(PointerPressedEvent, (sender, e) => EnsureList((Control)sender), RoutingStrategies.Tunnel);
			}
			model.BookLoaded += (sender, e) => filled.Clear();
			txCharactersSelector.Click += (sender, e) => ListSelectorFlyout.Open(txCharactersSelector, txCharacters.Text, model.Values(EditorList.Characters), text => txCharacters.Text = text, PlacementMode.TopEdgeAlignedRight);
			Selector(txTeamsSelector, txTeams, EditorList.Teams);
			Selector(txLocationsSelector, txLocations, EditorList.Locations);
			Selector(txMainCharacterOrTeamSelector, txMainCharacterOrTeam, EditorList.MainCharacterOrTeam);
			// txCharacters_KeyPress: a line break typed (Ctrl+Return) is dropped; the box wraps instead
			txCharacters.AddHandler(TextInputEvent, (sender, e) =>
			{
				if (e.Text != null && e.Text.IndexOfAny(new[] { '\r', '\n' }) >= 0)
				{
					e.Handled = true;
				}
			}, RoutingStrategies.Tunnel);
		}

		private void Map(Control box, EditorList list)
		{
			lists[box] = list;
		}

		/// <summary>Builds the list of a box now (a box does it when it gets the focus).</summary>
		public void EnsureList(Control box)
		{
			if (box is AutoCompleteBox completing && lists.TryGetValue(box, out EditorList list) && filled.Add(box))
			{
				completing.ItemsSource = EditorValueLists.Completions(model.Values(list));
			}
		}

		private void Selector(Button button, AutoCompleteBox box, EditorList list)
		{
			button.Click += (sender, e) => OpenSelector(button, box, list);
		}

		/// <summary>Opens the list selector of a box (<c>ListSelectorControl.ShowPopup</c>); the box takes the result when the popup closes. These boxes sit in the lower half of the tab: the popup opens upwards.</summary>
		public Flyout OpenSelector(Control anchor, AutoCompleteBox box, EditorList list)
		{
			return ListSelectorFlyout.Open(anchor, box.Text, model.Values(list), text => box.Text = text, PlacementMode.TopEdgeAlignedRight);
		}
	}
}
