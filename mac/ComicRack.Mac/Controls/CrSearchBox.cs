using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// The quick search box of the browser toolbar (<c>ToolStripSearchTextBox</c> and <c>SearchTextBox</c> in
	/// <c>cYo.Common.Windows/Forms/SearchTextBox.cs</c>): 130 x 24 with a flat frame, the <c>Search</c> image on the left (as wide as 1.5 times the
	/// height), the text, and the <c>SmallCloseGray</c> clear button on the right while there is text. A cue text shows while it is empty. The box
	/// doubles its width while it has the keyboard focus (<c>ComicBrowserControl.tsQuickSearch_Enter</c>). The scope menu on the search image belongs to
	/// the task that implements the scopes (P5.15a).
	/// </summary>
	public class CrSearchBox : Border
	{
		public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<CrSearchBox, string>(nameof(Text), string.Empty, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

		public static readonly StyledProperty<string> CueTextProperty = AvaloniaProperty.Register<CrSearchBox, string>(nameof(CueText));

		/// <summary>The width of the box at rest (<c>tsQuickSearch.Size</c>).</summary>
		public const double RestWidth = 130;

		/// <summary>The height of the box (<c>tsQuickSearch.Size</c>).</summary>
		public const double BoxHeight = 24;

		private readonly TextBox box;
		private readonly Border clear;
		private bool updating;

		public CrSearchBox()
		{
			Width = RestWidth;
			Height = BoxHeight;
			Margin = new Thickness(1, 0, 1, 1);
			VerticalAlignment = VerticalAlignment.Center;
			BorderThickness = new Thickness(1);
			TextElement.SetFontSize(this, ChromeFonts.ControlSize);

			box = new TextBox
			{
				BorderThickness = new Thickness(0),
				MinHeight = 0,
				Height = BoxHeight - 2,
				Padding = new Thickness(2, 0),
				Background = Brushes.Transparent,
				VerticalContentAlignment = VerticalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
			};
			box.PropertyChanged += (sender, e) =>
			{
				if (e.Property == TextBox.TextProperty)
				{
					OnBoxTextChanged(box.Text);
				}
			};
			box.GotFocus += (sender, e) => Width = RestWidth * 2;
			box.LostFocus += (sender, e) => Width = RestWidth;
			box.KeyDown += OnBoxKeyDown;

			var search = new CrIcon { IconName = "Search", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
			var searchSlot = new Border { Width = Math.Round((BoxHeight - 2) * 1.5), Child = search, Background = Brushes.Transparent };
			searchSlot.PointerPressed += (sender, e) =>
			{
				box.Focus();
				e.Handled = true;
			};
			clear = new Border
			{
				Width = BoxHeight - 2,
				Background = Brushes.Transparent,
				IsVisible = false,
				Child = new CrIcon { IconName = "SmallCloseGray", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
			};
			clear.PointerPressed += (sender, e) =>
			{
				Text = string.Empty;
				box.Focus();
				e.Handled = true;
			};

			var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
			Grid.SetColumn(searchSlot, 0);
			Grid.SetColumn(box, 1);
			Grid.SetColumn(clear, 2);
			grid.Children.Add(searchSlot);
			grid.Children.Add(box);
			grid.Children.Add(clear);
			Child = grid;
			ClipToBounds = true;

			ActualThemeVariantChanged += (sender, e) => ApplyTokens();
			AttachedToVisualTree += (sender, e) => ApplyTokens();
			ApplyCue();
		}

		static CrSearchBox()
		{
			TextProperty.Changed.AddClassHandler<CrSearchBox>((box, e) => box.OnTextChanged());
			CueTextProperty.Changed.AddClassHandler<CrSearchBox>((box, e) => box.ApplyCue());
		}

		/// <summary>The text in the box.</summary>
		public string Text
		{
			get => GetValue(TextProperty);
			set => SetValue(TextProperty, value);
		}

		/// <summary>The grey text shown while the box is empty ("Search All").</summary>
		public string CueText
		{
			get => GetValue(CueTextProperty);
			set => SetValue(CueTextProperty, value);
		}

		/// <summary>The text box inside (focus, tests).</summary>
		public TextBox Box => box;

		/// <summary>Gives the keyboard focus to the text (<c>ComicBrowserControl.FocusQuickSearch</c>).</summary>
		public void FocusBox()
		{
			box.Focus();
		}

		private void ApplyTokens()
		{
			// The renderer clears the box with SystemColors.Window and draws a flat 3D border.
			Background = ChromePaint.Brush(this, ThemeTokens.Names.Window);
			BorderBrush = ChromePaint.Brush(this, ThemeTokens.Names.ControlDark);
			box.Foreground = ChromePaint.Brush(this, ThemeTokens.Names.WindowText);
			Resources["TextControlPlaceholderForeground"] = ChromePaint.Brush(this, ThemeTokens.Names.GrayText);
			Resources["TextControlPlaceholderForegroundPointerOver"] = ChromePaint.Brush(this, ThemeTokens.Names.GrayText);
			Resources["TextControlPlaceholderForegroundFocused"] = ChromePaint.Brush(this, ThemeTokens.Names.GrayText);
		}

		private void ApplyCue()
		{
			box.PlaceholderText = CueText;
		}

		private void OnTextChanged()
		{
			if (updating)
			{
				return;
			}
			updating = true;
			try
			{
				string value = Text ?? string.Empty;
				if (box.Text != value)
				{
					box.Text = value;
				}
				clear.IsVisible = value.Length > 0;
			}
			finally
			{
				updating = false;
			}
		}

		private void OnBoxTextChanged(string value)
		{
			if (updating)
			{
				return;
			}
			updating = true;
			try
			{
				Text = value ?? string.Empty;
				clear.IsVisible = !string.IsNullOrEmpty(value);
			}
			finally
			{
				updating = false;
			}
		}

		private void OnBoxKeyDown(object sender, KeyEventArgs e)
		{
			// Escape empties the box, the usual way out of a search field.
			if (e.Key == Key.Escape && !string.IsNullOrEmpty(Text) && e.KeyModifiers == KeyModifiers.None)
			{
				Text = string.Empty;
				e.Handled = true;
			}
		}
	}
}
