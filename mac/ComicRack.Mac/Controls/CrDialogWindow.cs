using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using cYo.Common.Localize;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>The buttons of a <see cref="CrDialogWindow"/>, in the order of the original's dialogs: OK, Cancel, Apply, left to right.</summary>
	[Flags]
	public enum DialogButtons
	{
		None = 0,
		Ok = 1,
		Cancel = 2,
		Apply = 4,
		OkCancel = Ok | Cancel,
		OkCancelApply = Ok | Cancel | Apply,
	}

	/// <summary>
	/// The window of ComicRack's own dialogs (C-DLG-2: Info, Preferences, Export, Smart List, ...): a separate modal window centred on its owner, a
	/// body that the dialog fills with the original's layout, and a row of 80 x 24 buttons at the bottom right, OK then Cancel then Apply, 6 apart,
	/// with the texts of the original's translation (<see cref="TrSection"/>: <c>btOK</c>, <c>btCancel</c>, <c>btApply</c>). Return presses the default
	/// button (OK), Esc the cancel button; the dialog cannot be resized unless the owner allows it. <see cref="ShowDialogAsync"/> returns true after OK.
	/// </summary>
	public class CrDialogWindow : Window, IChromeElement
	{
		private readonly ContentControl bodyHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
		private DockPanel bottomBar;
		private readonly ContentControl footerHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
		private readonly StackPanel buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = ChromeMetrics.DialogButtonGap, HorizontalAlignment = HorizontalAlignment.Right };
		private readonly Button okButton = CreateButton();
		private readonly Button cancelButton = CreateButton();
		private readonly Button applyButton = CreateButton();
		private DialogButtons buttons = DialogButtons.OkCancel;
		private string trSection = "PreferencesDialog";
		private string okText;
		private string cancelText;
		private string applyText;

		public CrDialogWindow()
		{
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			CanResize = false;
			ShowInTaskbar = false;
			SizeToContent = SizeToContent.WidthAndHeight;
			FontSize = ChromeFonts.ControlSize;
			okButton.IsDefault = true;
			cancelButton.IsCancel = true;
			okButton.Click += (sender, e) => Accept();
			cancelButton.Click += (sender, e) => Cancel();
			applyButton.Click += (sender, e) => ApplyClicked?.Invoke(this, EventArgs.Empty);
			// the bottom bar: the buttons at the right, the footer (a check box such as "Show when Book read") at the left
			var bottom = bottomBar = new DockPanel { LastChildFill = true, Margin = new Thickness(15, 12, 15, 12) };
			DockPanel.SetDock(buttonRow, Dock.Right);
			bottom.Children.Add(buttonRow);
			bottom.Children.Add(footerHost);
			var root = new DockPanel { LastChildFill = true };
			DockPanel.SetDock(bottom, Dock.Bottom);
			root.Children.Add(bottom);
			root.Children.Add(bodyHost);
			Content = root;
			this.Bind(BackgroundProperty, this.GetResourceObservable(ThemeTokens.BrushKey(ThemeTokens.Names.Control)));
			RefreshButtons();
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.Dialogs;

		private static Button CreateButton()
		{
			return new Button
			{
				Width = ChromeMetrics.DialogButtonWidth,
				Height = ChromeMetrics.DialogButtonHeight,
				HorizontalContentAlignment = HorizontalAlignment.Center,
				VerticalContentAlignment = VerticalAlignment.Center,
				Padding = new Thickness(0),
			};
		}

		/// <summary>The dialog's content: the original's controls.</summary>
		public Control Body
		{
			get => (Control)bodyHost.Content;
			set => bodyHost.Content = value;
		}

		/// <summary>A control at the left of the button row, level with the buttons (the original puts a check box there: Quick Rating's "Show when Book read").</summary>
		public Control Footer
		{
			get => (Control)footerHost.Content;
			set
			{
				footerHost.Content = value;
				UpdateBottomBar();
			}
		}

		// the bar takes room only when it holds something (an alert has its own button row in the body)
		private void UpdateBottomBar()
		{
			if (bottomBar != null)
			{
				bottomBar.IsVisible = buttonRow.IsVisible || footerHost.Content != null;
			}
		}

		public DialogButtons Buttons
		{
			get => buttons;
			set
			{
				buttons = value;
				RefreshButtons();
			}
		}

		/// <summary>The section of <c>Languages/&lt;culture&gt;/*.xml</c> that holds <c>btOK</c>, <c>btCancel</c> and <c>btApply</c> (the form type name of the original).</summary>
		public string TrSection
		{
			get => trSection;
			set
			{
				trSection = value;
				RefreshButtons();
			}
		}

		public Button OkButton => okButton;

		public Button CancelButton => cancelButton;

		public Button ApplyButton => applyButton;

		/// <summary>The text of a button; null takes the original's translated text.</summary>
		public string OkText
		{
			get => okButton.Content as string;
			set
			{
				okText = value;
				RefreshButtons();
			}
		}

		public string CancelText
		{
			get => cancelButton.Content as string;
			set
			{
				cancelText = value;
				RefreshButtons();
			}
		}

		public string ApplyText
		{
			get => applyButton.Content as string;
			set
			{
				applyText = value;
				RefreshButtons();
			}
		}

		/// <summary>Raised when OK is pressed; cancel the event to keep the dialog open (validation).</summary>
		public event EventHandler<CancelEventArgs> OkClicked;

		public event EventHandler CancelClicked;

		public event EventHandler ApplyClicked;

		/// <summary>Shows the dialog modally over the owner; true after OK, false after Cancel, Esc or closing the window.</summary>
		public Task<bool> ShowDialogAsync(Window owner) => ShowDialog<bool>(owner);

		/// <summary>OK as the button does: raises <see cref="OkClicked"/> and closes with true unless it was cancelled.</summary>
		public void Accept()
		{
			var args = new CancelEventArgs();
			OkClicked?.Invoke(this, args);
			if (!args.Cancel)
			{
				Close(true);
			}
		}

		/// <summary>Cancel as the button does.</summary>
		public void Cancel()
		{
			CancelClicked?.Invoke(this, EventArgs.Empty);
			Close(false);
		}

		/// <summary>The text for a button key of the original: the translation without the Windows mnemonic ampersand.</summary>
		public string DialogText(string key, string english)
		{
			string text = english;
			try
			{
				text = TR.Load(TrSection ?? "PreferencesDialog")[key, "&" + english];
			}
			catch (Exception)
			{
				// no translation file: the English default of the original stays
			}
			return StripMnemonic(text);
		}

		/// <summary>"&amp;OK" becomes "OK", "A&amp;&amp;B" becomes "A&amp;B" (macOS has no mnemonics, decision of chrome-guidelines section 8).</summary>
		public static string StripMnemonic(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return text ?? string.Empty;
			}
			var result = new System.Text.StringBuilder(text.Length);
			for (int i = 0; i < text.Length; i++)
			{
				if (text[i] == '&')
				{
					if (i + 1 < text.Length && text[i + 1] == '&')
					{
						result.Append('&');
						i++;
					}
					continue;
				}
				result.Append(text[i]);
			}
			return result.ToString();
		}

		private void RefreshButtons()
		{
			okButton.Content = okText ?? DialogText("btOK", "OK");
			cancelButton.Content = cancelText ?? DialogText("btCancel", "Cancel");
			applyButton.Content = applyText ?? DialogText("btApply", "Apply");
			buttonRow.Children.Clear();
			if ((buttons & DialogButtons.Ok) != 0)
			{
				buttonRow.Children.Add(okButton);
			}
			if ((buttons & DialogButtons.Cancel) != 0)
			{
				buttonRow.Children.Add(cancelButton);
			}
			if ((buttons & DialogButtons.Apply) != 0)
			{
				buttonRow.Children.Add(applyButton);
			}
			buttonRow.IsVisible = buttonRow.Children.Count > 0;
			UpdateBottomBar();
		}
	}
}
