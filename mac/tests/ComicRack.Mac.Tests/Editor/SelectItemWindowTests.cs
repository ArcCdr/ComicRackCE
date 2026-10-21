using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Views;
using Xunit;

namespace ComicRack.Mac.Tests.Editor
{
	/// <summary>P5.7c: the name dialog (DL.31, <c>SelectItemDialog</c>) and the bookmark of the page menu that asks for the name with it.</summary>
	public class SelectItemWindowTests
	{
		[Fact]
		public async Task The_dialog_has_the_positions_texts_and_rules_of_the_original()
		{
			await Ui.Run(() =>
			{
				var window = new SelectItemWindow("Bookmark", "Page 3");
				window.Show();
				Dpi.Settle(window);
				Assert.Equal("Bookmark", window.Title);
				Assert.Equal("Name:", window.NameLabel.Content);
				Rect label = Dpi.Area(window.NameLabel, window.Content as Visual ?? window);
				Rect box = Dpi.Area(window.NameBox, window.Content as Visual ?? window);
				Assert.Equal(320, box.Width, 0.5);
				Assert.Equal(21, box.Height, 0.5);
				Assert.Equal(label.X, box.X, 0.5); // both at 12
				Assert.Equal(16, box.Y - label.Y - 0, 3.5); // the box is 16 below the label (24 and 40 in the Designer)
				Assert.Equal("Page 3", window.NameBox.Text);
				Assert.True(window.OkButton.IsEnabled);
				window.NameBox.Text = string.Empty;
				Assert.False(window.OkButton.IsEnabled); // OK is off while the text is empty
				window.NameBox.Text = "  Chapter 1  ";
				Assert.True(window.OkButton.IsEnabled);
				Assert.Equal("Chapter 1", window.Value); // trimmed
				Assert.Equal(80, window.OkButton.Width);
				Assert.Equal(24, window.OkButton.Height);
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_caption_and_a_list_of_items_change_the_label_and_make_the_box_a_combo_box()
		{
			await Ui.Run(() =>
			{
				var window = new SelectItemWindow("Add Item", "b", new[] { "a", "b", "c" }, "Series");
				window.Show();
				Dpi.Settle(window);
				Assert.Equal("Series:", window.NameLabel.Content);
				Assert.True(window.NameChoice.IsVisible);
				Assert.Equal(new[] { "a", "b", "c" }, window.NameChoice.Items.Cast<object>().Select(i => (string)i).ToArray());
				Assert.Equal("b", window.Value);
				window.Close();
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task OK_returns_the_text_and_Cancel_returns_nothing()
		{
			await Ui.Run(async () =>
			{
				var owner = new Window { Width = 400, Height = 300 };
				owner.Show();
				Task<string> asked = SelectItemWindow.GetNameAsync(owner, "Bookmark", "Start");
				Dpi.Settle(owner);
				var dialog = owner.OwnedWindows.OfType<SelectItemWindow>().Single();
				dialog.NameBox.Text = " Middle ";
				dialog.Accept();
				Assert.Equal("Middle", await asked);

				asked = SelectItemWindow.GetNameAsync(owner, "Bookmark", "Start");
				Dpi.Settle(owner);
				dialog = owner.OwnedWindows.OfType<SelectItemWindow>().Single();
				dialog.Cancel();
				Assert.Null(await asked);
				owner.Close();
			});
		}

		[Fact]
		public async Task Set_Bookmark_asks_for_the_name_with_the_proposal_and_a_cancelled_dialog_sets_nothing()
		{
			using (var rig = new PagesRig())
			{
				string file = rig.MakeCbz("a.cbz", PagesRig.ThreePages);
				using (PageListModel model = rig.Open(rig.Book(file), 3))
				{
					await Ui.Run(async () =>
					{
						string caption = null, proposal = null;
						string answer = "Chapter 1";
						var menu = new PagesContextMenu(() => model, (c, p) =>
						{
							caption = c;
							proposal = p;
							return Task.FromResult(answer);
						});
						model.SetSelection(new[] { 1 });
						await menu.SetBookmarkAsync();
						Assert.Equal("Bookmark", caption);
						Assert.Equal("Page 2", proposal);
						Assert.Equal("Chapter 1", model.Bookmark);

						// the second time the proposal is the bookmark itself
						answer = null; // Cancel
						await menu.SetBookmarkAsync();
						Assert.Equal("Chapter 1", proposal);
						Assert.Equal("Chapter 1", model.Bookmark); // unchanged

						model.SetSelection(new[] { 0, 1 }); // two pages: no bookmark
						caption = null;
						await menu.SetBookmarkAsync();
						Assert.Null(caption); // nothing was asked
					});
				}
			}
		}
	}
}
