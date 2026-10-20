using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Platform;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The alert as a window: the fallback of <see cref="MacAlert"/> where the system alert cannot run (tests, the headless platform). It is the
	/// original's <c>QuestionDialog</c> look on the <see cref="CrDialogWindow"/> frame: the message in bold, the description below (at most 350
	/// wide), the optional check box, and a row of 80 x 24 buttons at the bottom right; the first button is the default, Esc presses the cancel button.
	/// </summary>
	public sealed class AlertWindow : CrDialogWindow
	{
		private readonly MacAlertOptions options;
		private readonly CheckBox suppression;
		private readonly List<Button> buttons = new List<Button>();
		private int pressed = -1;

		public AlertWindow(MacAlertOptions options)
		{
			this.options = options ?? throw new ArgumentNullException(nameof(options));
			Buttons = DialogButtons.None;
			Title = "ComicRack";
			var panel = new StackPanel { Spacing = 8, Margin = new Thickness(15, 15, 15, 0), MinWidth = 300, MaxWidth = 350 };
			panel.Children.Add(new TextBlock { Text = options.Message, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
			if (!string.IsNullOrEmpty(options.Informative))
			{
				panel.Children.Add(new TextBlock { Text = options.Informative, TextWrapping = TextWrapping.Wrap });
			}
			if (!string.IsNullOrEmpty(options.SuppressionText))
			{
				suppression = new CheckBox { Content = options.SuppressionText, Margin = new Thickness(0, 4, 0, 0) };
				panel.Children.Add(suppression);
			}
			var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = ChromeMetrics.DialogButtonGap, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 12) };
			for (int i = 0; i < options.Buttons.Count; i++)
			{
				int index = i;
				var button = new Button
				{
					Content = CrDialogWindow.StripMnemonic(options.Buttons[i]),
					MinWidth = ChromeMetrics.DialogButtonWidth,
					Height = ChromeMetrics.DialogButtonHeight,
					Padding = new Thickness(8, 0),
					HorizontalContentAlignment = HorizontalAlignment.Center,
					VerticalContentAlignment = VerticalAlignment.Center,
					IsDefault = i == 0,
					IsCancel = i == options.CancelIndex,
				};
				button.Click += (sender, e) =>
				{
					pressed = index;
					Close(true);
				};
				buttons.Add(button);
				row.Children.Add(button);
			}
			panel.Children.Add(row);
			Body = panel;
		}

		/// <summary>The buttons, left to right.</summary>
		public IReadOnlyList<Button> AlertButtons => buttons;

		public CheckBox SuppressionBox => suppression;

		/// <summary>True if a button closed the window (not the close box).</summary>
		public bool ClosedWithButton => pressed >= 0;

		/// <summary>The result for a closed window: the pressed button, or the cancel button (else the first) when the window was closed another way.</summary>
		public MacAlertResult Result(bool accepted)
		{
			int index = pressed >= 0 ? pressed : (options.CancelIndex >= 0 ? options.CancelIndex : 0);
			return new MacAlertResult(index, suppression?.IsChecked == true);
		}
	}
}
