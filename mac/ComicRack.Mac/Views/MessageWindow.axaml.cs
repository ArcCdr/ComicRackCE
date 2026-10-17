using Avalonia.Controls;

namespace ComicRack.Mac.Views
{
	/// <summary>A message with an OK button, for problems the user must know about.</summary>
	public partial class MessageWindow : Window
	{
		public MessageWindow()
		{
			InitializeComponent();
			this.FindControl<Button>("OkButton").Click += (sender, e) => Close();
		}

		public MessageWindow(string title, string message)
			: this()
		{
			this.FindControl<TextBlock>("TitleLabel").Text = title;
			this.FindControl<TextBlock>("MessageLabel").Text = message;
		}
	}
}
