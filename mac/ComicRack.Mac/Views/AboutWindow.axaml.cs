using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ComicRack.Mac.Views
{
	public partial class AboutWindow : Window
	{
		public AboutWindow()
		{
			InitializeComponent();
			string version = typeof(AboutWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
			int plus = version.IndexOf('+');
			VersionText.Text = "Version " + (plus > 0 ? version.Substring(0, plus) : version);
		}

		private void OnOk(object sender, RoutedEventArgs e)
		{
			Close();
		}
	}
}
