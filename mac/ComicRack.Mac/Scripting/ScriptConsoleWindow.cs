using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ComicRack.Mac.Host.Scripting;
using System.Linq;

namespace ComicRack.Mac.Scripting
{
	/// <summary>
	/// The script output window (DL.27, <c>ScriptOutputForm</c>): a window of 606 x 471 with one multi line text box that fills it, scroll bars in both directions and no word wrap, in the
	/// fixed width font of the original (Lucida Console 8.25 pt). It shows <see cref="ScriptConsoleLog"/>, the text of everything the scripts print and of the log of their commands, and
	/// follows its end as the original's <c>TextBoxStream</c> (<c>AppendText</c>) does. Spec: <c>mac/specs/views/ScriptOutputForm.md</c>.
	/// </summary>
	public sealed class ScriptConsoleWindow : Window
	{
		/// <summary>The client size of the form in the Designer (<c>ClientSize</c> 606 x 471).</summary>
		public const double DesignWidth = 606;

		public const double DesignHeight = 471;

		private readonly ScriptConsoleLog source;
		private readonly TextBox log = new TextBox();
		private readonly DispatcherTimer timer;
		private long shown = -1;

		public ScriptConsoleWindow(ScriptConsoleLog source)
		{
			this.source = source ?? throw new ArgumentNullException(nameof(source));
			Title = UiText.Get("ScriptOutputForm", "ScriptOutputForm", "Script Output");
			Classes.Add("cr-window");
			Width = DesignWidth;
			Height = DesignHeight;
			MinWidth = 200;
			MinHeight = 100;
			log.Classes.Add("cr-console");
			log.IsReadOnly = true;
			log.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top;
			log.AcceptsReturn = true;
			log.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;
			log.MaxLength = ScriptConsoleLog.MaxLength;
			ScrollViewer.SetHorizontalScrollBarVisibility(log, ScrollBarVisibility.Auto);
			ScrollViewer.SetVerticalScrollBarVisibility(log, ScrollBarVisibility.Auto);
			Content = log;
			// the text arrives on any thread; the window looks for news ten times a second while it is open
			timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (sender, e) => Update());
			Opened += (sender, e) =>
			{
				Update();
				timer.Start();
			};
			Closed += (sender, e) => timer.Stop();
			Update();
		}

		/// <summary>The text box (<c>ScriptOutputForm.Log</c>).</summary>
		public TextBox Log => log;

		/// <summary>Shows the text that has arrived, scrolled to its end. Called by the timer; tests call it to see the result at once.</summary>
		public void Update()
		{
			long version = source.Version;
			if (version == shown)
			{
				return;
			}
			shown = version;
			string text = source.Text;
			log.Text = text;
			log.CaretIndex = text.Length;
			Dispatcher.UIThread.Post(() => log.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToEnd(), DispatcherPriority.Background);
		}
	}
}
