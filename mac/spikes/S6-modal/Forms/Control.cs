using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace System.Windows.Forms
{
	public interface IWin32Window
	{
		IntPtr Handle
		{
			get;
		}
	}

	public interface IButtonControl
	{
		DialogResult DialogResult
		{
			get;
			set;
		}

		void PerformClick();
	}

	public delegate void MethodInvoker();

	public delegate void PaintEventHandler(object sender, PaintEventArgs e);

	public class PaintEventArgs : EventArgs, IDisposable
	{
		public Graphics Graphics
		{
			get;
		}

		public Rectangle ClipRectangle
		{
			get;
		}

		public PaintEventArgs(Graphics graphics, Rectangle clipRect)
		{
			Graphics = graphics;
			ClipRectangle = clipRect;
		}

		public void Dispose()
		{
		}
	}

	[Flags]
	public enum AnchorStyles
	{
		None = 0,
		Top = 1,
		Bottom = 2,
		Left = 4,
		Right = 8
	}

	public enum DockStyle
	{
		None = 0,
		Top = 1,
		Bottom = 2,
		Left = 3,
		Right = 4,
		Fill = 5
	}

	public enum HorizontalAlignment
	{
		Left = 0,
		Right = 1,
		Center = 2
	}

	/// <summary>
	/// The facade of System.Windows.Forms.Control. All state lives here (any thread may set and get it); the peer is created lazily on the UI thread
	/// when the form is shown, and every change after that is forwarded to it on the UI thread (the cross-thread auto-marshalling of section 5.9.4).
	/// </summary>
	public class Control : Component, IWin32Window
	{
		private static int handleCounter = 0x10000;

		private readonly IntPtr handle = new IntPtr(Interlocked.Increment(ref handleCounter));
		private readonly ControlCollection controls;
		private string text = string.Empty;
		private Rectangle bounds;
		private bool visible = true;
		private bool enabled = true;
		private DockStyle dock;
		private AnchorStyles anchor = AnchorStyles.Top | AnchorStyles.Left;
		private bool autoSize;
		private int tabIndex;
		private int layoutSuspended;
		private bool layoutPending;
		private volatile IControlPeer peer;
		private Control parent;

		public Control()
		{
			controls = new ControlCollection(this);
		}

		public IntPtr Handle => handle;

		public IControlPeer Peer => peer;

		public ControlCollection Controls => controls;

		public Control Parent => parent;

		public bool IsDisposed
		{
			get;
			private set;
		}

		public string Name
		{
			get;
			set;
		} = string.Empty;

		public object Tag
		{
			get;
			set;
		}

		public virtual string Text
		{
			get => text;
			set
			{
				value = value ?? string.Empty;
				if (text != value)
				{
					text = value;
					Changed("Text");
					RaiseOnUi(() => OnTextChanged(EventArgs.Empty));
				}
			}
		}

		public Point Location
		{
			get => bounds.Location;
			set => SetBounds(value.X, value.Y, bounds.Width, bounds.Height);
		}

		public Size Size
		{
			get => bounds.Size;
			set => SetBounds(bounds.X, bounds.Y, value.Width, value.Height);
		}

		public Rectangle Bounds
		{
			get => bounds;
			set => SetBounds(value.X, value.Y, value.Width, value.Height);
		}

		public int Left
		{
			get => bounds.X;
			set => SetBounds(value, bounds.Y, bounds.Width, bounds.Height);
		}

		public int Top
		{
			get => bounds.Y;
			set => SetBounds(bounds.X, value, bounds.Width, bounds.Height);
		}

		public int Width
		{
			get => bounds.Width;
			set => SetBounds(bounds.X, bounds.Y, value, bounds.Height);
		}

		public int Height
		{
			get => bounds.Height;
			set => SetBounds(bounds.X, bounds.Y, bounds.Width, value);
		}

		public virtual Size ClientSize
		{
			get => bounds.Size;
			set => Size = value;
		}

		public Rectangle ClientRectangle => new Rectangle(Point.Empty, ClientSize);

		public bool Visible
		{
			get => visible;
			set
			{
				if (visible != value)
				{
					visible = value;
					Changed("Visible");
					RaiseOnUi(() => OnVisibleChanged(EventArgs.Empty));
				}
			}
		}

		public bool Enabled
		{
			get => enabled;
			set
			{
				if (enabled != value)
				{
					enabled = value;
					Changed("Enabled");
				}
			}
		}

		public DockStyle Dock
		{
			get => dock;
			set
			{
				dock = value;
				PerformLayoutOfParent();
			}
		}

		public AnchorStyles Anchor
		{
			get => anchor;
			set => anchor = value;
		}

		public virtual bool AutoSize
		{
			get => autoSize;
			set => autoSize = value;
		}

		public int TabIndex
		{
			get => tabIndex;
			set => tabIndex = value;
		}

		public bool TabStop
		{
			get;
			set;
		} = true;

		public bool UseVisualStyleBackColor
		{
			get;
			set;
		}

		public Color BackColor
		{
			get;
			set;
		} = Color.Empty;

		public Color ForeColor
		{
			get;
			set;
		} = Color.Empty;

		public Font Font
		{
			get;
			set;
		}

		/// <summary>True when the caller is not on the UI thread (Control.InvokeRequired).</summary>
		public bool InvokeRequired => FormsRuntime.HasBackend && !FormsRuntime.Backend.CheckAccess();

		public event EventHandler Click;

		public event EventHandler TextChanged;

		public event EventHandler Resize;

		public event EventHandler VisibleChanged;

		public event PaintEventHandler Paint;

		// ---------------------------------------------------------------- virtuals scripts override

		protected virtual void OnClick(EventArgs e)
		{
			Click?.Invoke(this, e);
		}

		protected virtual void OnTextChanged(EventArgs e)
		{
			TextChanged?.Invoke(this, e);
		}

		protected virtual void OnResize(EventArgs e)
		{
			Resize?.Invoke(this, e);
		}

		protected virtual void OnVisibleChanged(EventArgs e)
		{
			VisibleChanged?.Invoke(this, e);
		}

		protected virtual void OnPaint(PaintEventArgs e)
		{
			Paint?.Invoke(this, e);
		}

		protected virtual void OnPaintBackground(PaintEventArgs e)
		{
		}

		// ---------------------------------------------------------------- layout

		public void SuspendLayout()
		{
			layoutSuspended++;
		}

		public void ResumeLayout()
		{
			ResumeLayout(true);
		}

		public void ResumeLayout(bool performLayout)
		{
			if (layoutSuspended > 0)
			{
				layoutSuspended--;
			}
			if (layoutSuspended == 0 && performLayout && layoutPending)
			{
				PerformLayout();
			}
		}

		public void PerformLayout()
		{
			if (layoutSuspended > 0)
			{
				layoutPending = true;
				return;
			}
			layoutPending = false;
			Rectangle area = ClientRectangle;
			foreach (Control c in controls.Snapshot())
			{
				if (c.autoSize && FormsRuntime.HasBackend && c.WantsMeasuredSize)
				{
					c.SetBoundsCore(c.Left, c.Top, 0, 0, measure: true);
				}
				switch (c.dock)
				{
					case DockStyle.Top:
						c.SetBoundsCore(area.X, area.Y, area.Width, c.Height, false);
						area = new Rectangle(area.X, area.Y + c.Height, area.Width, Math.Max(0, area.Height - c.Height));
						break;
					case DockStyle.Bottom:
						c.SetBoundsCore(area.X, area.Bottom - c.Height, area.Width, c.Height, false);
						area = new Rectangle(area.X, area.Y, area.Width, Math.Max(0, area.Height - c.Height));
						break;
					case DockStyle.Left:
						c.SetBoundsCore(area.X, area.Y, c.Width, area.Height, false);
						area = new Rectangle(area.X + c.Width, area.Y, Math.Max(0, area.Width - c.Width), area.Height);
						break;
					case DockStyle.Right:
						c.SetBoundsCore(area.Right - c.Width, area.Y, c.Width, area.Height, false);
						area = new Rectangle(area.X, area.Y, Math.Max(0, area.Width - c.Width), area.Height);
						break;
					case DockStyle.Fill:
						c.SetBoundsCore(area.X, area.Y, area.Width, area.Height, false);
						break;
				}
				c.PerformLayout();
			}
		}

		private void PerformLayoutOfParent()
		{
			parent?.PerformLayout();
		}

		/// <summary>True for controls whose size follows their text (labels and check boxes with AutoSize).</summary>
		protected virtual bool WantsMeasuredSize => false;

		private void SetBounds(int x, int y, int width, int height)
		{
			SetBoundsCore(x, y, width, height, false);
		}

		private void SetBoundsCore(int x, int y, int width, int height, bool measure)
		{
			if (measure)
			{
				Size s = FormsRuntime.Backend.MeasureText(text, Font, 0);
				width = s.Width + 2;
				height = s.Height + 2;
			}
			Rectangle old = bounds;
			Rectangle now = new Rectangle(x, y, Math.Max(0, width), Math.Max(0, height));
			if (old == now)
			{
				return;
			}
			bounds = now;
			Changed("Bounds");
			if (old.Size != now.Size)
			{
				RaiseOnUi(() =>
				{
					OnResize(EventArgs.Empty);
					PerformLayout();
				});
			}
		}

		// ---------------------------------------------------------------- hierarchy

		public Form FindForm()
		{
			for (Control c = this; c != null; c = c.parent)
			{
				if (c is Form f)
				{
					return f;
				}
			}
			return null;
		}

		internal void SetParent(Control newParent)
		{
			parent = newParent;
		}

		// ---------------------------------------------------------------- peer plumbing

		internal void SetPeer(IControlPeer p)
		{
			peer = p;
		}

		/// <summary>Forgets the peers of this control and its children (the window closed); the next show builds new ones from the facade state.</summary>
		internal void DiscardPeers()
		{
			peer = null;
			foreach (Control c in controls.Snapshot())
			{
				c.DiscardPeers();
			}
		}

		/// <summary>Creates this control's peer (UI thread). Children are created by the peer.</summary>
		public IControlPeer EnsurePeer()
		{
			if (peer == null)
			{
				peer = FormsRuntime.Backend.CreatePeer(this);
			}
			return peer;
		}

		/// <summary>A property changed: the peer, if there is one, is told on the UI thread (posted when the caller is another thread).</summary>
		protected void Changed(string property)
		{
			IControlPeer p = peer;
			if (p == null)
			{
				return;
			}
			IFormsBackend backend = FormsRuntime.Backend;
			if (backend.CheckAccess())
			{
				p.PropertyChanged(property);
			}
			else
			{
				backend.Post(() => p.PropertyChanged(property));
			}
		}

		/// <summary>Raises an event on the UI thread: directly when already there, queued otherwise (events of other threads' property writes).</summary>
		private void RaiseOnUi(Action raise)
		{
			if (!FormsRuntime.HasBackend || FormsRuntime.Backend.CheckAccess())
			{
				raise();
			}
			else
			{
				FormsRuntime.Backend.Post(() => FormsRuntime.Guard(raise));
			}
		}

		public void Invalidate()
		{
			IControlPeer p = peer;
			if (p == null)
			{
				return;
			}
			IFormsBackend backend = FormsRuntime.Backend;
			if (backend.CheckAccess())
			{
				p.Invalidate();
			}
			else
			{
				backend.Post(p.Invalidate);
			}
		}

		public void Update()
		{
		}

		public void Refresh()
		{
			Invalidate();
		}

		public bool Focus() => false;

		// ---------------------------------------------------------------- entry points for peers (UI thread; exceptions go to Application.ThreadException)

		public void PeerClick()
		{
			FormsRuntime.Guard(() => OnClick(EventArgs.Empty));
		}

		/// <summary>The peer paints this control onto <paramref name="graphics"/> (the clip is the whole control).</summary>
		public void PeerPaint(Graphics graphics, Rectangle clip)
		{
			FormsRuntime.Guard(() =>
			{
				using (PaintEventArgs e = new PaintEventArgs(graphics, clip))
				{
					OnPaintBackground(e);
					OnPaint(e);
				}
			});
		}

		/// <summary>The peer's own state changed through the user (typing, a click on a check box ...): the facade takes it over and raises the events.</summary>
		public virtual void PeerStateChanged(string property, object value)
		{
			if (property == "Text")
			{
				string v = (string)value ?? string.Empty;
				if (v != text)
				{
					text = v;
					FormsRuntime.Guard(() => OnTextChanged(EventArgs.Empty));
				}
			}
		}

		// ---------------------------------------------------------------- marshalling

		/// <summary>Runs <paramref name="method"/> on the UI thread, blocking the caller (Control.Invoke).</summary>
		public object Invoke(Delegate method)
		{
			return Invoke(method, null);
		}

		public object Invoke(Delegate method, params object[] args)
		{
			if (method == null)
			{
				throw new ArgumentNullException(nameof(method));
			}
			IFormsBackend backend = FormsRuntime.Backend;
			if (backend.CheckAccess())
			{
				return method.DynamicInvoke(args);
			}
			object result = null;
			ExceptionDispatchInfo error = null;
			backend.Invoke(() =>
			{
				try
				{
					result = method.DynamicInvoke(args);
				}
				catch (System.Reflection.TargetInvocationException e)
				{
					error = ExceptionDispatchInfo.Capture(e.InnerException ?? e);
				}
				catch (Exception e)
				{
					error = ExceptionDispatchInfo.Capture(e);
				}
			});
			error?.Throw();
			return result;
		}

		public IAsyncResult BeginInvoke(Delegate method)
		{
			return BeginInvoke(method, null);
		}

		public IAsyncResult BeginInvoke(Delegate method, params object[] args)
		{
			AsyncResult result = new AsyncResult();
			FormsRuntime.Backend.Post(() =>
			{
				try
				{
					result.Result = method.DynamicInvoke(args);
				}
				catch (Exception e)
				{
					result.Error = e is System.Reflection.TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
					Application.OnThreadException(result.Error);
				}
				finally
				{
					result.Done.Set();
				}
			});
			return result;
		}

		public object EndInvoke(IAsyncResult asyncResult)
		{
			AsyncResult r = (AsyncResult)asyncResult;
			r.Done.Wait();
			return r.Result;
		}

		private sealed class AsyncResult : IAsyncResult
		{
			public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
			public object Result;
			public Exception Error;

			public bool IsCompleted => Done.IsSet;

			public WaitHandle AsyncWaitHandle => Done.WaitHandle;

			public object AsyncState => null;

			public bool CompletedSynchronously => false;
		}

		protected override void Dispose(bool disposing)
		{
			if (!IsDisposed)
			{
				IsDisposed = true;
				IControlPeer p = peer;
				peer = null;
				if (p != null)
				{
					IFormsBackend backend = FormsRuntime.Backend;
					if (backend.CheckAccess())
					{
						p.Dispose();
					}
					else
					{
						backend.Post(p.Dispose);
					}
				}
				if (disposing)
				{
					foreach (Control c in controls.Snapshot())
					{
						c.Dispose();
					}
				}
			}
			base.Dispose(disposing);
		}

		// ---------------------------------------------------------------- children

		public class ControlCollection : IEnumerable
		{
			private readonly Control owner;
			private readonly List<Control> items = new List<Control>();

			public ControlCollection(Control owner)
			{
				this.owner = owner;
			}

			public int Count
			{
				get
				{
					lock (items)
					{
						return items.Count;
					}
				}
			}

			public Control this[int index]
			{
				get
				{
					lock (items)
					{
						return items[index];
					}
				}
			}

			public virtual void Add(Control value)
			{
				if (value == null)
				{
					throw new ArgumentNullException(nameof(value));
				}
				lock (items)
				{
					if (value.parent != null)
					{
						value.parent.controls.Remove(value);
					}
					items.Add(value);
				}
				value.parent = owner;
				owner.layoutPending = true;
				IControlPeer p = owner.peer;
				if (p != null)
				{
					IFormsBackend backend = FormsRuntime.Backend;
					if (backend.CheckAccess())
					{
						p.ChildAdded(value);
					}
					else
					{
						backend.Post(() => p.ChildAdded(value));
					}
				}
				if (owner.layoutSuspended == 0 && value.dock != DockStyle.None)
				{
					owner.PerformLayout();
				}
			}

			public virtual void AddRange(Control[] controls)
			{
				foreach (Control c in controls)
				{
					Add(c);
				}
			}

			public virtual void Remove(Control value)
			{
				bool removed;
				lock (items)
				{
					removed = items.Remove(value);
				}
				if (removed)
				{
					value.parent = null;
					IControlPeer p = owner.peer;
					if (p != null)
					{
						IFormsBackend backend = FormsRuntime.Backend;
						if (backend.CheckAccess())
						{
							p.ChildRemoved(value);
						}
						else
						{
							backend.Post(() => p.ChildRemoved(value));
						}
					}
				}
			}

			public virtual void Clear()
			{
				foreach (Control c in Snapshot())
				{
					Remove(c);
				}
			}

			public bool Contains(Control c)
			{
				lock (items)
				{
					return items.Contains(c);
				}
			}

			public IEnumerator GetEnumerator() => Snapshot().GetEnumerator();

			internal Control[] Snapshot()
			{
				lock (items)
				{
					return items.ToArray();
				}
			}
		}
	}
}
