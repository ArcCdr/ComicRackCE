using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SWF = System.Windows.Forms;

namespace S6.Backend
{
	/// <summary>Base of the peers: holds the Avalonia visual of a facade control and keeps it in step with the facade state.</summary>
	public abstract class PeerBase : SWF.IControlPeer
	{
		protected readonly AvaloniaBackend Backend;
		protected readonly SWF.Control Facade;
		private readonly List<PeerBase> children = new List<PeerBase>();

		protected PeerBase(AvaloniaBackend backend, SWF.Control facade)
		{
			Backend = backend;
			Facade = facade;
		}

		public abstract Avalonia.Controls.Control Visual { get; }

		/// <summary>The canvas that holds the children (the visual itself for containers, null for leaves).</summary>
		protected virtual Canvas ChildHost => null;

		protected void Build()
		{
			Initialize();
			Canvas host = ChildHost;
			if (host != null)
			{
				foreach (SWF.Control c in Facade.Controls.OfType<SWF.Control>())
				{
					AddChildPeer(c);
				}
			}
		}

		protected abstract void Initialize();

		private void AddChildPeer(SWF.Control child)
		{
			PeerBase p = (PeerBase)child.EnsurePeer();
			children.Add(p);
			ChildHost.Children.Add(p.Visual);
		}

		public virtual void ChildAdded(SWF.Control child)
		{
			if (ChildHost != null)
			{
				AddChildPeer(child);
			}
		}

		public virtual void ChildRemoved(SWF.Control child)
		{
			PeerBase p = children.FirstOrDefault(x => ReferenceEquals(x.Facade, child));
			if (p != null)
			{
				children.Remove(p);
				ChildHost?.Children.Remove(p.Visual);
				p.Dispose();
			}
		}

		protected void ApplyBounds()
		{
			System.Drawing.Rectangle b = Facade.Bounds;
			Avalonia.Controls.Control v = Visual;
			Canvas.SetLeft(v, b.X);
			Canvas.SetTop(v, b.Y);
			v.Width = b.Width;
			v.Height = b.Height;
		}

		public virtual void PropertyChanged(string name)
		{
			switch (name)
			{
				case "Bounds":
					ApplyBounds();
					break;
				case "Enabled":
					Visual.IsEnabled = Facade.Enabled;
					break;
				case "Visible":
					Visual.IsVisible = Facade.Visible;
					break;
			}
		}

		public virtual void Invalidate()
		{
			Visual.InvalidateVisual();
		}

		public virtual void Dispose()
		{
			foreach (PeerBase p in children.ToArray())
			{
				p.Dispose();
			}
			children.Clear();
		}

		protected static string Mnemonic(string text) => (text ?? string.Empty).Replace("&&", "\u0001").Replace("&", string.Empty).Replace("\u0001", "&");
	}

	/// <summary>
	/// A Canvas that also paints its facade control: a surface control behind the children runs the OnPaint override (a script's subclass), which
	/// draws with the System.Drawing stand-in onto a bitmap that is drawn into the Avalonia frame.
	/// </summary>
	internal sealed class PaintCanvas : Canvas
	{
		private readonly PaintSurface surface;

		public PaintCanvas(SWF.Control facade)
		{
			surface = new PaintSurface(facade);
			Children.Add(surface);
		}

		public int Paints => surface.Paints;

		protected override Size ArrangeOverride(Size finalSize)
		{
			surface.Width = finalSize.Width;
			surface.Height = finalSize.Height;
			return base.ArrangeOverride(finalSize);
		}

		public void InvalidateSurface()
		{
			surface.InvalidateVisual();
		}
	}

	internal sealed class PaintSurface : Avalonia.Controls.Control
	{
		private readonly SWF.Control facade;
		public int Paints;

		public PaintSurface(SWF.Control facade)
		{
			this.facade = facade;
			IsHitTestVisible = false;
		}

		public override void Render(DrawingContext context)
		{
			base.Render(context);
			int w = (int)Math.Ceiling(Bounds.Width);
			int h = (int)Math.Ceiling(Bounds.Height);
			if (w <= 0 || h <= 0)
			{
				return;
			}
			double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
			int pw = (int)Math.Ceiling(w * scale);
			int ph = (int)Math.Ceiling(h * scale);
			System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(pw, ph, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
			using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bitmap))
			{
				g.ScaleTransform((float)scale, (float)scale);
				Paints++;
				facade.PeerPaint(g, new System.Drawing.Rectangle(0, 0, w, h));
			}
			SkiaSharp.SKBitmap copy = cYo.Compat.Drawing.SkiaInterop.GetSKBitmap(bitmap).Copy();
			bitmap.Dispose();
			context.Custom(new BitmapDrawOperation(copy, new Rect(0, 0, w, h)));
		}
	}

	internal sealed class BitmapDrawOperation : Avalonia.Rendering.SceneGraph.ICustomDrawOperation
	{
		private readonly SkiaSharp.SKBitmap bitmap;
		private readonly Rect destination;

		public BitmapDrawOperation(SkiaSharp.SKBitmap bitmap, Rect destination)
		{
			this.bitmap = bitmap;
			this.destination = destination;
		}

		public Rect Bounds => destination;

		public bool HitTest(Point p) => false;

		public bool Equals(Avalonia.Rendering.SceneGraph.ICustomDrawOperation other) => ReferenceEquals(this, other);

		public void Render(ImmediateDrawingContext context)
		{
			Avalonia.Skia.ISkiaSharpApiLeaseFeature feature = context.TryGetFeature<Avalonia.Skia.ISkiaSharpApiLeaseFeature>();
			if (feature == null)
			{
				return;
			}
			using (Avalonia.Skia.ISkiaSharpApiLease lease = feature.Lease())
			{
				lease.SkCanvas.DrawBitmap(bitmap, new SkiaSharp.SKRect((float)destination.Left, (float)destination.Top, (float)destination.Right, (float)destination.Bottom));
			}
		}

		public void Dispose()
		{
			bitmap.Dispose();
		}
	}

	/// <summary>The peer of every control that is not a form.</summary>
	public sealed class ControlPeer : PeerBase
	{
		private Avalonia.Controls.Control visual;
		private Canvas host;
		private bool updating;

		public ControlPeer(AvaloniaBackend backend, SWF.Control facade)
			: base(backend, facade)
		{
			Build();
		}

		public override Avalonia.Controls.Control Visual => visual;

		protected override Canvas ChildHost => host;

		public int PaintCount => (visual as PaintCanvas)?.Paints ?? 0;

		protected override void Initialize()
		{
			switch (Facade)
			{
				case SWF.Button b:
				{
					Button btn = new Button { Content = Mnemonic(b.Text), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(2) };
					btn.Click += (s, e) => SWF.FormsRuntime.Guard(b.PerformClick);
					visual = btn;
					break;
				}
				case SWF.CheckBox c:
				{
					CheckBox box = new CheckBox { Content = Mnemonic(c.Text), IsChecked = c.Checked };
					box.IsCheckedChanged += (s, e) => { if (!updating) { c.PeerStateChanged("Checked", box.IsChecked == true); } };
					visual = box;
					break;
				}
				case SWF.Label l:
					visual = new TextBlock { Text = Mnemonic(l.Text), TextWrapping = TextWrapping.Wrap, FontSize = AvaloniaBackend.DefaultFontSize };
					break;
				case SWF.TextBox t:
				{
					TextBox tb = new TextBox { Text = t.Text, AcceptsReturn = t.Multiline, IsReadOnly = t.ReadOnly, FontSize = AvaloniaBackend.DefaultFontSize };
					tb.PropertyChanged += (s, e) =>
					{
						if (e.Property == TextBox.TextProperty && !updating)
						{
							t.PeerStateChanged("Text", tb.Text);
						}
					};
					visual = tb;
					break;
				}
				case SWF.ComboBox cb:
				{
					ComboBox box = new ComboBox { FontSize = AvaloniaBackend.DefaultFontSize };
					SyncItems(box, cb);
					box.SelectionChanged += (s, e) => { if (!updating) { cb.PeerStateChanged("SelectedIndex", box.SelectedIndex); } };
					visual = box;
					break;
				}
				case SWF.ListBox lb:
				{
					ListBox box = new ListBox { FontSize = AvaloniaBackend.DefaultFontSize };
					SyncItems(box, lb);
					box.SelectionChanged += (s, e) => { if (!updating) { lb.PeerStateChanged("SelectedIndex", box.SelectedIndex); } };
					visual = box;
					break;
				}
				case SWF.NumericUpDown n:
				{
					NumericUpDown nud = new NumericUpDown { Minimum = n.Minimum, Maximum = n.Maximum, Increment = n.Increment, Value = n.Value, FormatString = "0", FontSize = AvaloniaBackend.DefaultFontSize };
					nud.ValueChanged += (s, e) => { if (!updating && nud.Value.HasValue) { n.PeerStateChanged("Value", nud.Value.Value); } };
					visual = nud;
					break;
				}
				default:
					host = new PaintCanvas(Facade);
					visual = host;
					break;
			}
			if (visual is Canvas)
			{
				// Plain containers (Panel, Control and Python subclasses of it) are canvases that paint their facade.
			}
			ApplyBounds();
			visual.IsEnabled = Facade.Enabled;
			visual.IsVisible = Facade.Visible;
		}

		public override void PropertyChanged(string name)
		{
			updating = true;
			try
			{
				switch (name)
				{
					case "Text":
						switch (visual)
						{
							case CheckBox c:
								c.Content = Mnemonic(Facade.Text);
								break;
							case Button b:
								b.Content = Mnemonic(Facade.Text);
								break;
							case TextBlock t:
								t.Text = Mnemonic(Facade.Text);
								break;
							case TextBox t:
								if (t.Text != Facade.Text)
								{
									t.Text = Facade.Text;
								}
								break;
						}
						break;
					case "Checked":
						if (visual is CheckBox cb)
						{
							cb.IsChecked = ((SWF.CheckBox)Facade).Checked;
						}
						break;
					case "Value":
						if (visual is NumericUpDown nud)
						{
							nud.Value = ((SWF.NumericUpDown)Facade).Value;
						}
						break;
					case "Range":
						if (visual is NumericUpDown n2)
						{
							SWF.NumericUpDown f = (SWF.NumericUpDown)Facade;
							n2.Minimum = f.Minimum;
							n2.Maximum = f.Maximum;
						}
						break;
					case "Items":
						if (visual is ComboBox combo)
						{
							SyncItems(combo, (SWF.ComboBox)Facade);
						}
						else if (visual is ListBox list)
						{
							SyncItems(list, (SWF.ListBox)Facade);
						}
						break;
					case "SelectedIndex":
						if (visual is SelectingItemsControl sic)
						{
							sic.SelectedIndex = ((SWF.ListControl)Facade).SelectedIndex;
						}
						break;
					default:
						base.PropertyChanged(name);
						break;
				}
			}
			finally
			{
				updating = false;
			}
		}

		private void SyncItems(SelectingItemsControl control, SWF.ListControl facade)
		{
			bool was = updating;
			updating = true;
			try
			{
				System.Collections.IEnumerable items = facade is SWF.ComboBox c ? c.Items : ((SWF.ListBox)facade).Items;
				control.ItemsSource = items.Cast<object>().Select(o => o?.ToString() ?? string.Empty).ToList();
				control.SelectedIndex = facade.SelectedIndex;
			}
			finally
			{
				updating = was;
			}
		}

		public override void Invalidate()
		{
			visual.InvalidateVisual();
			(visual as PaintCanvas)?.InvalidateSurface();
		}
	}

	/// <summary>The peer of a form: an Avalonia window with a canvas for the controls.</summary>
	public sealed class FormPeer : PeerBase, SWF.IFormPeer
	{
		private readonly SWF.Form form;
		private Window window;
		private Canvas canvas;
		private bool closedByPeer;

		public FormPeer(AvaloniaBackend backend, SWF.Form form)
			: base(backend, form)
		{
			this.form = form;
			Build();
		}

		public Window Window => window;

		public override Avalonia.Controls.Control Visual => canvas;

		protected override Canvas ChildHost => canvas;

		protected override void Initialize()
		{
			canvas = new Canvas();
			window = new Window
			{
				Title = form.Text,
				Content = canvas,
				SizeToContent = SizeToContent.Manual,
				CanResize = form.FormBorderStyle == SWF.FormBorderStyle.Sizable || form.FormBorderStyle == SWF.FormBorderStyle.SizableToolWindow,
				ShowInTaskbar = form.ShowInTaskbar,
				ShowActivated = true,
				WindowStartupLocation = form.StartPosition == SWF.FormStartPosition.CenterParent ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
				FontSize = AvaloniaBackend.DefaultFontSize,
			};
			SetWindowSize();
			window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
			window.Closing += OnClosing;
			window.Closed += OnClosed;
			window.SizeChanged += (s, e) =>
			{
				System.Drawing.Size now = new System.Drawing.Size((int)Math.Round(window.ClientSize.Width), (int)Math.Round(window.ClientSize.Height));
				if (now != form.ClientSize)
				{
					form.PeerStateChanged("Size", now);
				}
			};
		}

		private void SetWindowSize()
		{
			window.Width = Math.Max(1, form.ClientSize.Width);
			window.Height = Math.Max(1, form.ClientSize.Height);
		}

		private void OnKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter && !(e.Source is TextBox tb && tb.AcceptsReturn))
			{
				if (form.AcceptButton != null)
				{
					e.Handled = true;
					SWF.FormsRuntime.Guard(() => form.PeerKey(true));
				}
			}
			else if (e.Key == Key.Escape && form.CancelButton != null)
			{
				e.Handled = true;
				SWF.FormsRuntime.Guard(() => form.PeerKey(false));
			}
		}

		private void OnClosing(object sender, WindowClosingEventArgs e)
		{
			if (closedByPeer)
			{
				return;
			}
			SWF.CloseReason reason = e.CloseReason == WindowCloseReason.ApplicationShutdown ? SWF.CloseReason.ApplicationExitCall : e.CloseReason == WindowCloseReason.OSShutdown ? SWF.CloseReason.WindowsShutDown : e.CloseReason == WindowCloseReason.OwnerWindowClosing ? SWF.CloseReason.FormOwnerClosing : SWF.CloseReason.UserClosing;
			if (form.PeerClosing(reason))
			{
				e.Cancel = true;
			}
		}

		private void OnClosed(object sender, EventArgs e)
		{
			closedByPeer = true;
			form.PeerClosed();
		}

		public void Show(SWF.IFormPeer owner)
		{
			Window ownerWindow = (owner as FormPeer)?.Window;
			if (ownerWindow != null)
			{
				window.Show(ownerWindow);
			}
			else
			{
				window.Show();
			}
		}

		public void ShowDialog(SWF.IFormPeer owner)
		{
			Window ownerWindow = (owner as FormPeer)?.Window ?? Backend.DefaultOwner;
			if (ownerWindow != null && !ReferenceEquals(ownerWindow, window))
			{
				// The returned task completes when the window closes; the facade's own nested frame is the wait.
				_ = window.ShowDialog(ownerWindow);
			}
			else
			{
				window.Show();
			}
		}

		public void Close()
		{
			window.Close();
		}

		public void Hide()
		{
			closedByPeer = true;
			window.Close();
		}

		public override void PropertyChanged(string name)
		{
			switch (name)
			{
				case "Text":
					window.Title = form.Text;
					break;
				case "Bounds":
					SetWindowSize();
					break;
				case "Visible":
					break;
				default:
					base.PropertyChanged(name);
					break;
			}
		}

		public override void Dispose()
		{
			base.Dispose();
			if (window != null)
			{
				closedByPeer = true;
				window.Close();
			}
		}
	}
}
