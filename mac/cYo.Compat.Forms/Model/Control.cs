using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms.Layout;
using cYo.Compat.Forms;

// The object model of System.Windows.Forms.Control (P4.9a): all state of a control lives here, any thread may set and get it, and the events are raised from this state in the
// order of dotnet/winforms (MACOS_NATIVE_PATH.md 5.9.3 and 5.9.4). A peer (cYo.Compat.Forms.IControlPeer) exists only while the control's handle is created, it is built from this
// state and told what changed. Layout (P4.9b to d), marshalling (P4.9e), modal semantics (P4.9f) and drawing (P4.10e) build on the members declared here.
namespace System.Windows.Forms
{
	public partial class Control : Component, IWin32Window
	{
		private static Control focused;
		private static bool checkForIllegalCrossThreadCalls;

		private const ControlStyles DefaultStyles = ControlStyles.UserPaint | ControlStyles.StandardClick | ControlStyles.Selectable | ControlStyles.StandardDoubleClick
			| ControlStyles.AllPaintingInWmPaint | ControlStyles.UseTextForAccessibility;

		private readonly object sync = new object();
		private ControlCollection controls;
		private Control parent;
		private volatile IControlPeer peer;

		private string name = string.Empty;
		private string text = string.Empty;
		private object tag;
		private Rectangle bounds;
		private Size minimumSize;
		private Size maximumSize;
		private Padding margin;
		private Padding padding;
		private AnchorStyles anchor = AnchorStyles.Top | AnchorStyles.Left;
		private DockStyle dock;
		private bool autoSize;
		private AutoSizeMode autoSizeMode = AutoSizeMode.GrowOnly;
		private int tabIndex = -1;
		private bool tabStop = true;
		private bool visibleFlag = true;
		private bool enabledFlag = true;
		private bool causesValidation = true;
		private bool allowDrop;
		private bool capture;
		private bool isAccessible;
		private bool useWaitCursor;
		private bool created;
		private bool handleCreated;
		private bool isDisposed;
		private bool disposing;
		private bool recreatingHandle;
		private bool topLevel;
		private ControlStyles styles = DefaultStyles;
		private Color backColor = Color.Empty;
		private Color foreColor = Color.Empty;
		private Font font;
		private Cursor cursor;
		private RightToLeft rightToLeft = RightToLeft.Inherit;
		private ImeMode imeMode = ImeMode.Inherit;
		private Image backgroundImage;
		private ImageLayout backgroundImageLayout = ImageLayout.Tile;
		private object dataContext;
		private Region region;
		private ContextMenu contextMenu;
		private ContextMenuStrip contextMenuStrip;
		private Point autoScrollOffset;
		private string accessibleName;
		private string accessibleDescription;
		private string accessibleDefaultActionDescription;
		private AccessibleRole accessibleRole = AccessibleRole.Default;
		private int layoutSuspended;
		private bool layoutPending;
		private LayoutEventArgs layoutPendingArgs;
		private Rectangle specifiedBounds;
		private AnchorInfo anchorInfo;

		public Control()
			: this(null, string.Empty)
		{
		}

		public Control(string text)
			: this(null, text)
		{
		}

		public Control(string text, int left, int top, int width, int height)
			: this(null, text, left, top, width, height)
		{
		}

		public Control(Control parent, string text)
		{
			Size defaultSize = DefaultSize;
			bounds = new Rectangle(0, 0, defaultSize.Width, defaultSize.Height);
			specifiedBounds = bounds;
			margin = DefaultMargin;
			padding = DefaultPadding;
			minimumSize = DefaultMinimumSize;
			maximumSize = DefaultMaximumSize;
			this.text = text ?? string.Empty;
			if (parent != null)
			{
				Parent = parent;
			}
		}

		public Control(Control parent, string text, int left, int top, int width, int height)
			: this(parent, text)
		{
			SetBounds(left, top, width, height);
		}

		// ---------------------------------------------------------------- peers and handles

		/// <summary>The toolkit object of this control while its handle is created; null before the form is shown and after it closed.</summary>
		internal IControlPeer Peer => peer;

		internal object SyncRoot => sync;

		/// <summary>Resets what is static (the focused control, the active form, CheckForIllegalCrossThreadCalls); for FormsRuntime.Reset.</summary>
		internal static void ResetStaticState()
		{
			focused = null;
			checkForIllegalCrossThreadCalls = false;
			Form.ResetActiveForm();
		}

		/// <summary>Tells the peer, on the UI thread, that a facade property changed.</summary>
		internal void Notify(string property)
		{
			IControlPeer p = peer;
			if (p == null)
			{
				return;
			}
			IFormsBackend backend = FormsRuntime.Backend;
			if (backend.CheckAccess())
			{
				FormsRuntime.Guard(() => p.PropertyChanged(property));
			}
			else
			{
				backend.Post(() => FormsRuntime.Guard(() => p.PropertyChanged(property)));
			}
		}

		public bool IsHandleCreated => handleCreated;

		public bool Created => created;

		public bool RecreatingHandle => recreatingHandle;

		public bool IsDisposed => isDisposed;

		public bool Disposing => disposing;

		protected override bool CanRaiseEvents => !GetAnyDisposingInHierarchy();

		private bool GetAnyDisposingInHierarchy()
		{
			for (Control c = this; c != null; c = c.parent)
			{
				if (c.disposing)
				{
					return true;
				}
			}
			return false;
		}

		public void CreateControl() => CreateControl(false);

		internal void CreateControl(bool ignoreVisible)
		{
			if (isDisposed)
			{
				throw new ObjectDisposedException(GetType().Name);
			}
			if (created || (!ignoreVisible && !Visible))
			{
				return;
			}
			created = true;
			bool ok = false;
			try
			{
				if (!handleCreated)
				{
					CreateHandle();
				}
				foreach (Control child in Children())
				{
					child.CreateControl(ignoreVisible);
				}
				ok = true;
			}
			finally
			{
				if (!ok)
				{
					created = false;
				}
			}
			OnCreateControl();
		}

		/// <summary>Creates the peer on the UI thread and raises HandleCreated.</summary>
		protected virtual void CreateHandle()
		{
			if (isDisposed)
			{
				throw new ObjectDisposedException(GetType().Name);
			}
			if (handleCreated)
			{
				return;
			}
			IFormsBackend backend = FormsRuntime.Backend;
			Action create = () =>
			{
				peer = backend.CreatePeer(this);
				handleCreated = true;
			};
			if (backend.CheckAccess())
			{
				create();
			}
			else
			{
				backend.Invoke(create);
			}
			OnHandleCreated(EventArgs.Empty);
		}

		/// <summary>Disposes the peer and raises HandleDestroyed.</summary>
		protected virtual void DestroyHandle()
		{
			if (!handleCreated)
			{
				return;
			}
			IControlPeer p = peer;
			peer = null;
			handleCreated = false;
			created = false;
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
			OnHandleDestroyed(EventArgs.Empty);
		}

		protected void RecreateHandle()
		{
			if (!handleCreated)
			{
				return;
			}
			recreatingHandle = true;
			try
			{
				DestroyHandle();
				CreateHandle();
			}
			finally
			{
				recreatingHandle = false;
			}
		}

		protected bool GetTopLevel() => topLevel;

		protected void SetTopLevel(bool value)
		{
			topLevel = value;
		}

		internal bool TopLevelInternal
		{
			get => topLevel;
			set => topLevel = value;
		}

		protected bool GetStyle(ControlStyles flag) => (styles & flag) == flag;

		protected void SetStyle(ControlStyles flag, bool value)
		{
			styles = value ? styles | flag : styles & ~flag;
		}

		protected void UpdateStyles()
		{
			OnStyleChanged(EventArgs.Empty);
		}

		protected bool ResizeRedraw
		{
			get => GetStyle(ControlStyles.ResizeRedraw);
			set => SetStyle(ControlStyles.ResizeRedraw, value);
		}

		protected virtual bool DoubleBuffered
		{
			get => GetStyle(ControlStyles.OptimizedDoubleBuffer);
			set
			{
				if (DoubleBuffered != value)
				{
					SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, value);
				}
			}
		}

		public static bool CheckForIllegalCrossThreadCalls
		{
			get => checkForIllegalCrossThreadCalls;
			set => checkForIllegalCrossThreadCalls = value;
		}

		// ---------------------------------------------------------------- identity

		public string Name
		{
			get
			{
				lock (sync)
				{
					return name;
				}
			}
			set
			{
				lock (sync)
				{
					name = value ?? string.Empty;
				}
			}
		}

		public object Tag
		{
			get => Volatile.Read(ref tag);
			set => Volatile.Write(ref tag, value);
		}

		public virtual string Text
		{
			get
			{
				lock (sync)
				{
					return text;
				}
			}
			set
			{
				value = value ?? string.Empty;
				lock (sync)
				{
					if (text == value)
					{
						return;
					}
					text = value;
				}
				Notify("Text");
				OnTextChanged(EventArgs.Empty);
			}
		}

		public virtual void ResetText()
		{
			Text = string.Empty;
		}

		public string AccessibleName
		{
			get => accessibleName;
			set => accessibleName = value;
		}

		public string AccessibleDescription
		{
			get => accessibleDescription;
			set => accessibleDescription = value;
		}

		public string AccessibleDefaultActionDescription
		{
			get => accessibleDefaultActionDescription;
			set => accessibleDefaultActionDescription = value;
		}

		public AccessibleRole AccessibleRole
		{
			get => accessibleRole;
			set => accessibleRole = value;
		}

		public bool IsAccessible
		{
			get => isAccessible;
			set => isAccessible = value;
		}

		public string CompanyName => Application.CompanyName;

		public string ProductName => Application.ProductName;

		public string ProductVersion => Application.ProductVersion;

		public override ISite Site
		{
			get => base.Site;
			set => base.Site = value;
		}

		// ---------------------------------------------------------------- geometry

		/// <summary>The size of the frame around the client area (the title bar and borders of a form, the border of a panel or text box). Client size = size - this.</summary>
		internal virtual Size NonClientSize => Size.Empty;

		/// <summary>The position of the client area inside the control's bounds.</summary>
		internal virtual Point ClientOffset => Point.Empty;

		protected virtual Size DefaultSize => Size.Empty;

		protected virtual Size DefaultMinimumSize => Size.Empty;

		protected virtual Size DefaultMaximumSize => Size.Empty;

		protected virtual Padding DefaultMargin => new Padding(3);

		protected virtual Padding DefaultPadding => Padding.Empty;

		protected virtual ImeMode DefaultImeMode => ImeMode.Inherit;

		public Rectangle Bounds
		{
			get
			{
				lock (sync)
				{
					return bounds;
				}
			}
			set => SetBounds(value.X, value.Y, value.Width, value.Height, BoundsSpecified.All);
		}

		public Point Location
		{
			get
			{
				lock (sync)
				{
					return bounds.Location;
				}
			}
			set => SetBounds(value.X, value.Y, 0, 0, BoundsSpecified.Location);
		}

		public Size Size
		{
			get
			{
				lock (sync)
				{
					return bounds.Size;
				}
			}
			set => SetBounds(0, 0, value.Width, value.Height, BoundsSpecified.Size);
		}

		public int Left
		{
			get => Bounds.X;
			set => SetBounds(value, 0, 0, 0, BoundsSpecified.X);
		}

		public int Top
		{
			get => Bounds.Y;
			set => SetBounds(0, value, 0, 0, BoundsSpecified.Y);
		}

		public int Width
		{
			get => Bounds.Width;
			set => SetBounds(0, 0, value, 0, BoundsSpecified.Width);
		}

		public int Height
		{
			get => Bounds.Height;
			set => SetBounds(0, 0, 0, value, BoundsSpecified.Height);
		}

		public int Right => Bounds.Right;

		public int Bottom => Bounds.Bottom;

		public Size ClientSize
		{
			get
			{
				lock (sync)
				{
					return ClientSizeOf(bounds.Size);
				}
			}
			set => SetClientSizeCore(value.Width, value.Height);
		}

		private Size ClientSizeOf(Size size)
		{
			Size nc = NonClientSize;
			return new Size(Math.Max(0, size.Width - nc.Width), Math.Max(0, size.Height - nc.Height));
		}

		public Rectangle ClientRectangle => new Rectangle(Point.Empty, ClientSize);

		public virtual Rectangle DisplayRectangle => ClientRectangle;

		public virtual Size MinimumSize
		{
			get
			{
				lock (sync)
				{
					return minimumSize;
				}
			}
			set
			{
				lock (sync)
				{
					if (minimumSize == value)
					{
						return;
					}
					minimumSize = value;
				}
				Size current = Size;
				ApplySizeConstraints(ref current);
				if (current != Size)
				{
					Size = current;
				}
				parent?.PerformLayout(this, "MinimumSize");
			}
		}

		public virtual Size MaximumSize
		{
			get
			{
				lock (sync)
				{
					return maximumSize;
				}
			}
			set
			{
				lock (sync)
				{
					if (maximumSize == value)
					{
						return;
					}
					maximumSize = value;
				}
				Size current = Size;
				ApplySizeConstraints(ref current);
				if (current != Size)
				{
					Size = current;
				}
				parent?.PerformLayout(this, "MaximumSize");
			}
		}

		/// <summary>The minimum size wins over the maximum, a zero maximum side is "no limit" (the rules of Control.ApplySizeConstraints).</summary>
		internal void ApplySizeConstraints(ref Size size)
		{
			Size min = MinimumSize;
			Size max = MaximumSize;
			int width = size.Width;
			int height = size.Height;
			if (max.Width > 0 && width > max.Width)
			{
				width = max.Width;
			}
			if (max.Height > 0 && height > max.Height)
			{
				height = max.Height;
			}
			width = Math.Max(width, min.Width);
			height = Math.Max(height, min.Height);
			size = new Size(width, height);
		}

		public void SetBounds(int x, int y, int width, int height)
		{
			Rectangle now = Bounds;
			if (now.X != x || now.Y != y || now.Width != width || now.Height != height)
			{
				SetBoundsCore(x, y, width, height, BoundsSpecified.All);
			}
		}

		public void SetBounds(int x, int y, int width, int height, BoundsSpecified specified)
		{
			Rectangle now = Bounds;
			if ((specified & BoundsSpecified.X) == BoundsSpecified.None)
			{
				x = now.X;
			}
			if ((specified & BoundsSpecified.Y) == BoundsSpecified.None)
			{
				y = now.Y;
			}
			if ((specified & BoundsSpecified.Width) == BoundsSpecified.None)
			{
				width = now.Width;
			}
			if ((specified & BoundsSpecified.Height) == BoundsSpecified.None)
			{
				height = now.Height;
			}
			if (now.X != x || now.Y != y || now.Width != width || now.Height != height)
			{
				SetBoundsCore(x, y, width, height, specified);
			}
		}

		protected virtual void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
		{
			// The parent must not lay out before the layout engine knows the new bounds (InitLayout), so its layout waits until the end (as the original does).
			Control container = parent;
			container?.SuspendLayout();
			try
			{
				Rectangle now = Bounds;
				if (now.X != x || now.Y != y || now.Width != width || now.Height != height)
				{
					UpdateSpecifiedBounds(x, y, width, height, specified);
					var size = new Size(Math.Max(0, width), Math.Max(0, height));
					ApplySizeConstraints(ref size);
					UpdateBoundsCore(x, y, size.Width, size.Height);
					container?.LayoutEngine.InitLayout(this, specified);
				}
			}
			finally
			{
				container?.ResumeLayout(true);
			}
		}

		/// <summary>The bounds the script asked for: a part is taken over only when it was specified (the layout engine restores them when Dock goes back to None).</summary>
		private void UpdateSpecifiedBounds(int x, int y, int width, int height, BoundsSpecified specified)
		{
			lock (sync)
			{
				Rectangle s = specifiedBounds;
				specifiedBounds = new Rectangle(
					(specified & BoundsSpecified.X) != BoundsSpecified.None ? x : s.X,
					(specified & BoundsSpecified.Y) != BoundsSpecified.None ? y : s.Y,
					(specified & BoundsSpecified.Width) != BoundsSpecified.None ? width : s.Width,
					(specified & BoundsSpecified.Height) != BoundsSpecified.None ? height : s.Height);
			}
		}

		/// <summary>How the layout engine places a control: nothing counts as specified by the script.</summary>
		internal void SetBoundsByLayout(Rectangle r) => SetBoundsCore(r.X, r.Y, r.Width, r.Height, BoundsSpecified.None);

		protected virtual void SetClientSizeCore(int x, int y)
		{
			Size nc = NonClientSize;
			Size = new Size(x + nc.Width, y + nc.Height);
		}

		protected virtual Size SizeFromClientSize(Size clientSize)
		{
			Size nc = NonClientSize;
			return new Size(clientSize.Width + nc.Width, clientSize.Height + nc.Height);
		}

		protected internal void UpdateBounds()
		{
		}

		protected void UpdateBounds(int x, int y, int width, int height)
		{
			UpdateBoundsCore(x, y, width, height);
		}

		protected void UpdateBounds(int x, int y, int width, int height, int clientWidth, int clientHeight)
		{
			UpdateBoundsCore(x, y, width, height);
		}

		/// <summary>Stores the new bounds and raises Move, LocationChanged, Resize, SizeChanged and ClientSizeChanged (UpdateBounds of Control).</summary>
		private void UpdateBoundsCore(int x, int y, int width, int height)
		{
			bool newLocation;
			bool newSize;
			lock (sync)
			{
				newLocation = bounds.X != x || bounds.Y != y;
				newSize = bounds.Width != width || bounds.Height != height;
				if (!newLocation && !newSize)
				{
					return;
				}
				bounds = new Rectangle(x, y, width, height);
			}
			Notify("Bounds");
			if (newLocation)
			{
				OnLocationChanged(EventArgs.Empty);
			}
			if (newSize)
			{
				OnSizeChanged(EventArgs.Empty);
				OnClientSizeChanged(EventArgs.Empty);
			}
			parent?.PerformLayout(this, "Bounds");
		}

		protected virtual void OnLocationChanged(EventArgs e)
		{
			OnMove(EventArgs.Empty);
			LocationChanged?.Invoke(this, e);
		}

		protected virtual void OnSizeChanged(EventArgs e)
		{
			OnResize(EventArgs.Empty);
			SizeChanged?.Invoke(this, e);
		}

		protected virtual void OnResize(EventArgs e)
		{
			if (ResizeRedraw)
			{
				Invalidate();
			}
			PerformLayout(this, "Bounds");
			Resize?.Invoke(this, e);
		}

		public Size PreferredSize => GetPreferredSize(Size.Empty);

		public virtual Size GetPreferredSize(Size proposedSize)
		{
			// A container asks its layout engine (FlowLayoutPanel, TableLayoutPanel); the frame and the padding come on top of what the children need.
			Size result = Size.Empty;
			Size around = Padding.Size + NonClientSize;
			var inner = new Size(proposedSize.Width > 0 ? Math.Max(1, proposedSize.Width - around.Width) : 0, proposedSize.Height > 0 ? Math.Max(1, proposedSize.Height - around.Height) : 0);
			if (LayoutEngine.TryGetPreferredSize(this, inner, out Size content))
			{
				result = content + around;
			}
			ApplySizeConstraints(ref result);
			return result;
		}

		public int DeviceDpi => 96;

		public int LogicalToDeviceUnits(int value) => value;

		public Size LogicalToDeviceUnits(Size value) => value;

		public Point PointToClient(Point p)
		{
			Point origin = ClientOriginOnScreen();
			return new Point(p.X - origin.X, p.Y - origin.Y);
		}

		public Point PointToScreen(Point p)
		{
			Point origin = ClientOriginOnScreen();
			return new Point(p.X + origin.X, p.Y + origin.Y);
		}

		public Rectangle RectangleToClient(Rectangle r)
		{
			Point origin = ClientOriginOnScreen();
			return new Rectangle(r.X - origin.X, r.Y - origin.Y, r.Width, r.Height);
		}

		public Rectangle RectangleToScreen(Rectangle r)
		{
			Point origin = ClientOriginOnScreen();
			return new Rectangle(r.X + origin.X, r.Y + origin.Y, r.Width, r.Height);
		}

		/// <summary>Where the upper left corner of the client area lies on the screen: the chain of locations up to the form, plus the frame of the form.</summary>
		private Point ClientOriginOnScreen()
		{
			int x = 0;
			int y = 0;
			for (Control c = this; c != null; c = c.parent)
			{
				Rectangle b = c.Bounds;
				x += b.X + c.ClientOffset.X;
				y += b.Y + c.ClientOffset.Y;
			}
			return new Point(x, y);
		}

		// ---------------------------------------------------------------- layout state (the engine is P4.9b)

		public virtual AnchorStyles Anchor
		{
			get => anchor;
			set
			{
				if (anchor != value)
				{
					SuspendLayout();
					try
					{
						if (dock != DockStyle.None)
						{
							ApplyDock(DockStyle.None);
						}
						anchor = value;
						if (parent != null)
						{
							DefaultLayout.InitLayout(this, BoundsSpecified.All);
						}
						Notify("Anchor");
						parent?.PerformLayout(this, "Anchor");
					}
					finally
					{
						ResumeLayout();
					}
				}
			}
		}

		public virtual DockStyle Dock
		{
			get => dock;
			set
			{
				if (value < DockStyle.None || value > DockStyle.Fill)
				{
					throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(DockStyle));
				}
				if (dock != value)
				{
					SuspendLayout();
					try
					{
						ApplyDock(value);
						Notify("Dock");
						parent?.PerformLayout(this, "Dock");
						OnDockChanged(EventArgs.Empty);
					}
					finally
					{
						ResumeLayout();
					}
				}
			}
		}

		/// <summary>Docking replaces the anchors (they read as the default); undocking brings back the bounds the script specified.</summary>
		private void ApplyDock(DockStyle value)
		{
			dock = value;
			anchorInfo = null;
			if (value != DockStyle.None)
			{
				anchor = DefaultLayout.DefaultAnchor;
				return;
			}
			Rectangle s;
			lock (sync)
			{
				s = specifiedBounds;
			}
			SetBoundsCore(s.X, s.Y, s.Width, s.Height, BoundsSpecified.None);
		}

		internal AnchorStyles AnchorValue => anchor;

		internal DockStyle DockValue => dock;

		internal bool AutoSizeValue => autoSize;

		internal AutoSizeMode AutoSizeModeValue => autoSizeMode;

		internal AnchorInfo AnchorInfoValue
		{
			get => anchorInfo;
			set => anchorInfo = value;
		}

		public virtual bool AutoSize
		{
			get => autoSize;
			set
			{
				if (autoSize != value)
				{
					autoSize = value;
					Notify("AutoSize");
					parent?.PerformLayout(this, "AutoSize");
					OnAutoSizeChanged(EventArgs.Empty);
				}
			}
		}

		protected AutoSizeMode GetAutoSizeMode() => autoSizeMode;

		protected void SetAutoSizeMode(AutoSizeMode mode)
		{
			autoSizeMode = mode;
		}

		public Padding Margin
		{
			get
			{
				lock (sync)
				{
					return margin;
				}
			}
			set
			{
				lock (sync)
				{
					if (margin == value)
					{
						return;
					}
					margin = value;
				}
				parent?.PerformLayout(this, "Margin");
				OnMarginChanged(EventArgs.Empty);
			}
		}

		public Padding Padding
		{
			get
			{
				lock (sync)
				{
					return padding;
				}
			}
			set
			{
				lock (sync)
				{
					if (padding == value)
					{
						return;
					}
					padding = value;
				}
				PerformLayout(this, "Padding");
				OnPaddingChanged(EventArgs.Empty);
			}
		}

		public void SuspendLayout()
		{
			lock (sync)
			{
				layoutSuspended++;
			}
		}

		public void ResumeLayout() => ResumeLayout(true);

		public void ResumeLayout(bool performLayout)
		{
			bool run = false;
			LayoutEventArgs args = null;
			lock (sync)
			{
				if (layoutSuspended > 0)
				{
					layoutSuspended--;
				}
				if (layoutSuspended == 0 && layoutPending)
				{
					run = performLayout;
					args = layoutPendingArgs;
					if (performLayout)
					{
						layoutPending = false;
						layoutPendingArgs = null;
					}
				}
			}
			if (run)
			{
				PerformLayout(args?.AffectedControl, args?.AffectedProperty);
			}
		}

		public void PerformLayout() => PerformLayout(null, null);

		public void PerformLayout(Control affectedControl, string affectedProperty)
		{
			if (isDisposed)
			{
				return;
			}
			lock (sync)
			{
				if (layoutSuspended > 0)
				{
					layoutPending = true;
					layoutPendingArgs = new LayoutEventArgs(affectedControl, affectedProperty);
					return;
				}
				layoutPending = false;
				layoutPendingArgs = null;
				// While the layout runs it counts as suspended: a child that changes its size in the engine (or in a Layout handler) does not start a second layout.
				layoutSuspended = 1;
			}
			try
			{
				OnLayout(new LayoutEventArgs(affectedControl, affectedProperty));
			}
			finally
			{
				lock (sync)
				{
					layoutSuspended = 0;
					layoutPending = false;
					layoutPendingArgs = null;
				}
			}
		}

		protected virtual void OnLayout(LayoutEventArgs levent)
		{
			Layout?.Invoke(this, levent);
			LayoutEngine.Layout(this, levent);
		}

		/// <summary>The engine that arranges the children: Dock and Anchor for a plain container (FlowLayoutPanel and TableLayoutPanel override it, P4.9c).</summary>
		public virtual LayoutEngine LayoutEngine => DefaultLayoutEngine.Instance;

		/// <summary>True while SuspendLayout was called more often than ResumeLayout.</summary>
		internal bool IsLayoutSuspended
		{
			get
			{
				lock (sync)
				{
					return layoutSuspended > 0;
				}
			}
		}

		protected virtual void InitLayout()
		{
			(parent ?? this).LayoutEngine.InitLayout(this, BoundsSpecified.All);
		}

		// ---------------------------------------------------------------- hierarchy

		public ControlCollection Controls
		{
			get
			{
				lock (sync)
				{
					return controls ?? (controls = CreateControlsInstance());
				}
			}
		}

		protected virtual ControlCollection CreateControlsInstance() => new ControlCollection(this);

		internal Control[] Children()
		{
			ControlCollection c = controls;
			return c == null ? Array.Empty<Control>() : c.Snapshot();
		}

		public bool HasChildren
		{
			get
			{
				ControlCollection c = controls;
				return c != null && c.Count > 0;
			}
		}

		public Control Parent
		{
			get => Volatile.Read(ref parent);
			set
			{
				if (value == Volatile.Read(ref parent))
				{
					return;
				}
				if (value != null)
				{
					value.Controls.Add(this);
				}
				else
				{
					parent?.Controls.Remove(this);
				}
			}
		}

		internal Control ParentInternal
		{
			get => parent;
			set => Parent = value;
		}

		public Control TopLevelControl
		{
			get
			{
				Control c = this;
				while (c != null && !c.GetTopLevel())
				{
					c = c.parent;
				}
				return c;
			}
		}

		public bool Contains(Control ctl)
		{
			while (ctl != null)
			{
				ctl = ctl.parent;
				if (ctl == null)
				{
					return false;
				}
				if (ctl == this)
				{
					return true;
				}
			}
			return false;
		}

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

		public IContainerControl GetContainerControl()
		{
			Control c = this;
			if (c is IContainerControl && c.parent != null)
			{
				c = c.parent;
			}
			while (c != null && !(c is IContainerControl))
			{
				c = c.parent;
			}
			return (IContainerControl)c;
		}

		public Control GetChildAtPoint(Point pt) => GetChildAtPoint(pt, GetChildAtPointSkip.None);

		public Control GetChildAtPoint(Point pt, GetChildAtPointSkip skipValue)
		{
			Control[] children = Children();
			foreach (Control child in children)
			{
				if (child.Bounds.Contains(pt))
				{
					if ((skipValue & GetChildAtPointSkip.Invisible) != GetChildAtPointSkip.None && !child.Visible)
					{
						continue;
					}
					if ((skipValue & GetChildAtPointSkip.Disabled) != GetChildAtPointSkip.None && !child.Enabled)
					{
						continue;
					}
					return child;
				}
			}
			return null;
		}

		public void BringToFront()
		{
			parent?.Controls.SetChildIndex(this, 0);
		}

		public void SendToBack()
		{
			Control p = parent;
			if (p != null)
			{
				p.Controls.SetChildIndex(this, -1);
			}
		}

		protected void UpdateZOrder()
		{
			Notify("ZOrder");
		}

		/// <summary>Called by the collection after the control was added: TabIndex, ambient values and the events of the new parent, in the order of AssignParent.</summary>
		internal void AssignParent(Control newParent)
		{
			Control oldParent;
			Font oldFont = Font;
			Color oldFore = ForeColor;
			Color oldBack = BackColor;
			Cursor oldCursor = Cursor;
			RightToLeft oldRtl = RightToLeft;
			bool oldEnabled = Enabled;
			bool oldVisible = Visible;
			lock (sync)
			{
				oldParent = parent;
				parent = newParent;
			}
			if (oldParent == newParent)
			{
				return;
			}
			Notify("Parent");
			OnParentChanged(EventArgs.Empty);
			if (GetAnyDisposingInHierarchy())
			{
				return;
			}
			if (!Equals(oldFont, Font))
			{
				OnFontChanged(EventArgs.Empty);
			}
			if (oldFore != ForeColor)
			{
				OnForeColorChanged(EventArgs.Empty);
			}
			if (oldBack != BackColor)
			{
				OnBackColorChanged(EventArgs.Empty);
			}
			if (!Equals(oldCursor, Cursor))
			{
				OnCursorChanged(EventArgs.Empty);
			}
			if (oldRtl != RightToLeft)
			{
				OnRightToLeftChanged(EventArgs.Empty);
			}
			if (oldEnabled != Enabled)
			{
				OnEnabledChanged(EventArgs.Empty);
			}
			if (oldVisible != Visible)
			{
				OnVisibleChanged(EventArgs.Empty);
			}
		}

		internal int TabIndexRaw
		{
			get => tabIndex;
			set => tabIndex = value;
		}

		// ---------------------------------------------------------------- tab order and focus

		public int TabIndex
		{
			get => tabIndex < 0 ? 0 : tabIndex;
			set
			{
				if (value < 0)
				{
					throw new ArgumentOutOfRangeException(nameof(value), value, "'" + value + "' is not a valid value for 'TabIndex'. 'TabIndex' must be greater than or equal to 0.");
				}
				if (tabIndex != value)
				{
					tabIndex = value;
					OnTabIndexChanged(EventArgs.Empty);
				}
			}
		}

		public bool TabStop
		{
			get => tabStop;
			set
			{
				if (tabStop != value)
				{
					tabStop = value;
					OnTabStopChanged(EventArgs.Empty);
				}
			}
		}

		public bool CausesValidation
		{
			get => causesValidation;
			set
			{
				if (causesValidation != value)
				{
					causesValidation = value;
					OnCausesValidationChanged(EventArgs.Empty);
				}
			}
		}

		public bool Capture
		{
			get => capture;
			set
			{
				if (capture != value)
				{
					capture = value;
					Notify("Capture");
				}
			}
		}

		/// <summary>True when the control can take the keyboard focus: its handle exists and it is visible and enabled.</summary>
		public bool CanFocus => handleCreated && Visible && Enabled;

		public bool CanSelect
		{
			get
			{
				if (!GetStyle(ControlStyles.Selectable))
				{
					return false;
				}
				for (Control c = this; c != null; c = c.parent)
				{
					if (!c.visibleFlag || !c.enabledFlag)
					{
						return false;
					}
				}
				return true;
			}
		}

		public virtual bool Focused => ReferenceEquals(Volatile.Read(ref focused), this) && handleCreated;

		public bool ContainsFocus
		{
			get
			{
				Control f = Volatile.Read(ref focused);
				return f != null && (f == this || Contains(f));
			}
		}

		/// <summary>Focus(): the order of events is Enter, GotFocus on this control, then LostFocus, Leave, Validating, Validated on the one that had the focus.</summary>
		public bool Focus()
		{
			return FocusCore(selectOrder: false);
		}

		internal bool FocusCore(bool selectOrder)
		{
			if (!CanFocus)
			{
				return false;
			}
			Control previous = Volatile.Read(ref focused);
			if (previous == this)
			{
				return true;
			}
			IControlPeer p = peer;
			if (p != null)
			{
				FormsRuntime.Backend.Invoke(() => p.Focus());
			}
			Volatile.Write(ref focused, this);
			// Enter runs from the outermost container that did not have the focus down to this control.
			var enters = new List<Control>();
			for (Control c = this; c != null; c = c.parent)
			{
				if (previous != null && (c == previous || c.Contains(previous)))
				{
					break;
				}
				enters.Insert(0, c);
			}
			foreach (Control c in enters)
			{
				c.OnEnter(EventArgs.Empty);
			}
			OnGotFocus(EventArgs.Empty);
			if (previous == null || previous.isDisposed)
			{
				return true;
			}
			// Select, SelectNextControl, Tab: Leave, Validating, Validated, LostFocus. Focus() and the mouse: LostFocus, Leave, Validating, Validated.
			if (!selectOrder)
			{
				previous.OnLostFocus(EventArgs.Empty);
			}
			LeaveChain(previous);
			if (causesValidation)
			{
				var cancel = new CancelEventArgs();
				previous.OnValidating(cancel);
				if (cancel.Cancel)
				{
					// the old control keeps the focus
					Volatile.Write(ref focused, previous);
					OnLostFocus(EventArgs.Empty);
					for (Control c = this; c != null && !(c == previous || c.Contains(previous)); c = c.parent)
					{
						c.OnLeave(EventArgs.Empty);
					}
					return false;
				}
				previous.OnValidated(EventArgs.Empty);
			}
			if (selectOrder)
			{
				previous.OnLostFocus(EventArgs.Empty);
			}
			return true;
		}

		/// <summary>Leave runs from the old control up to the container that does not contain the new focus.</summary>
		private void LeaveChain(Control previous)
		{
			for (Control c = previous; c != null; c = c.parent)
			{
				if (c == this || c.Contains(this))
				{
					break;
				}
				c.OnLeave(EventArgs.Empty);
			}
		}

		/// <summary>Releases the focus of this control (it was disabled or hidden): LostFocus and Leave without a new owner.</summary>
		internal void LoseFocus()
		{
			if (!ReferenceEquals(Volatile.Read(ref focused), this))
			{
				return;
			}
			Volatile.Write(ref focused, null);
			OnLostFocus(EventArgs.Empty);
			for (Control c = this; c != null; c = c.parent)
			{
				c.OnLeave(EventArgs.Empty);
			}
		}

		internal static Control FocusedControl => Volatile.Read(ref focused);

		/// <summary>The user clicked the control (called by the peer on the UI thread): Click is raised through OnClick, as for a click with the mouse.</summary>
		internal void PeerClick() => FormsRuntime.Guard(() => OnClick(EventArgs.Empty));

		internal void RaiseValidating(CancelEventArgs e) => OnValidating(e);

		internal void RaiseValidated() => OnValidated(EventArgs.Empty);

		public void Select() => Select(false, false);

		protected virtual void Select(bool directed, bool forward)
		{
			IContainerControl container = GetContainerControl();
			if (container != null)
			{
				container.ActiveControl = this;
			}
			else if (CanFocus)
			{
				FocusCore(selectOrder: true);
			}
		}

		public bool SelectNextControl(Control ctl, bool forward, bool tabStopOnly, bool nested, bool wrap)
		{
			Control next = GetNextControlCore(ctl, forward, tabStopOnly, nested, wrap);
			if (next == null)
			{
				return false;
			}
			next.Select(true, forward);
			return true;
		}

		public Control GetNextControl(Control ctl, bool forward)
		{
			if (!Contains(ctl))
			{
				ctl = this;
			}
			var order = new List<Control>();
			Flatten(this, nested: true, respectContainers: true, order);
			int index = ctl == this ? -1 : order.IndexOf(ctl);
			if (forward)
			{
				return index + 1 < order.Count ? order[index + 1] : null;
			}
			return index > 0 ? order[index - 1] : null;
		}

		/// <summary>The children sorted by TabIndex (a stable sort: the order of the collection settles equal indexes).</summary>
		internal Control[] ChildrenInTabOrder()
		{
			Control[] children = Children();
			var keyed = new List<KeyValuePair<int, Control>>(children.Length);
			for (int i = 0; i < children.Length; i++)
			{
				keyed.Add(new KeyValuePair<int, Control>(i, children[i]));
			}
			keyed.Sort((a, b) =>
			{
				int byIndex = a.Value.TabIndex.CompareTo(b.Value.TabIndex);
				return byIndex != 0 ? byIndex : a.Key.CompareTo(b.Key);
			});
			var result = new Control[keyed.Count];
			for (int i = 0; i < result.Length; i++)
			{
				result[i] = keyed[i].Value;
			}
			return result;
		}

		/// <summary>The next control in tab order below this one that can be selected (the walk SelectNextControl does).</summary>
		private Control GetNextControlCore(Control start, bool forward, bool tabStopOnly, bool nested, bool wrap)
		{
			if (start != null && !Contains(start))
			{
				start = this;
			}
			var order = new List<Control>();
			Flatten(this, nested, respectContainers: false, order);
			if (order.Count == 0)
			{
				return null;
			}
			if (!forward)
			{
				order.Reverse();
			}
			int index = start == null || start == this ? -1 : order.IndexOf(start);
			int total = order.Count;
			for (int step = 1; step <= total; step++)
			{
				int i = index + step;
				if (i >= total)
				{
					if (!wrap)
					{
						return null;
					}
					i -= total;
				}
				Control candidate = order[i];
				if (candidate == start)
				{
					return null;
				}
				if (candidate.CanSelect && (!tabStopOnly || candidate.TabStop))
				{
					return candidate;
				}
			}
			return null;
		}

		/// <summary>A container control that manages its own focus (a form, a user control) is not entered by GetNextControl.</summary>
		private static bool ManagesFocus(Control c) => c.GetStyle(ControlStyles.ContainerControl) && c is IContainerControl;

		private static void Flatten(Control c, bool nested, bool respectContainers, List<Control> into)
		{
			foreach (Control child in c.ChildrenInTabOrder())
			{
				into.Add(child);
				if (nested && child.HasChildren && !(respectContainers && ManagesFocus(child)))
				{
					Flatten(child, nested, respectContainers, into);
				}
			}
		}

		// ---------------------------------------------------------------- visibility and enabled state

		public bool Visible
		{
			get => GetVisibleCore();
			set => SetVisibleCore(value);
		}

		/// <summary>The flag of this control and all its parents.</summary>
		internal bool GetVisibleCore()
		{
			for (Control c = this; c != null; c = c.parent)
			{
				if (!c.visibleFlag)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>The size the script asked for (the layout engines of the flow and the table panel size a control that does not measure itself by it, so that their own stretching does not feed back).</summary>
		internal Size SpecifiedSize
		{
			get
			{
				lock (sync)
				{
					return specifiedBounds.Size;
				}
			}
		}

		internal bool VisibleFlag
		{
			get => visibleFlag;
			set => visibleFlag = value;
		}

		protected virtual void SetVisibleCore(bool value)
		{
			bool before = GetVisibleCore();
			bool flagChanged = visibleFlag != value;
			visibleFlag = value;
			if (before != value)
			{
				Notify("Visible");
				if (!value && !GetVisibleCore())
				{
					ReleaseFocusWhenHidden();
				}
				OnVisibleChanged(EventArgs.Empty);
			}
			if (flagChanged)
			{
				parent?.PerformLayout(this, "Visible");
			}
		}

		private void ReleaseFocusWhenHidden()
		{
			Control f = Volatile.Read(ref focused);
			if (f != null && (f == this || Contains(f)))
			{
				f.LoseFocus();
			}
		}

		public void Show() => Visible = true;

		public void Hide() => Visible = false;

		public bool Enabled
		{
			get
			{
				for (Control c = this; c != null; c = c.parent)
				{
					if (!c.enabledFlag)
					{
						return false;
					}
				}
				return true;
			}
			set
			{
				bool before = Enabled;
				enabledFlag = value;
				if (before != Enabled)
				{
					Notify("Enabled");
					if (!Enabled)
					{
						Control f = Volatile.Read(ref focused);
						if (f != null && (f == this || Contains(f)))
						{
							f.LoseFocus();
						}
					}
					OnEnabledChanged(EventArgs.Empty);
				}
			}
		}

		protected virtual void OnVisibleChanged(EventArgs e)
		{
			if (Visible && parent != null && parent.created)
			{
				CreateControl(false);
			}
			VisibleChanged?.Invoke(this, e);
			foreach (Control child in Children())
			{
				child.OnParentVisibleChanged(e);
			}
		}

		protected virtual void OnParentVisibleChanged(EventArgs e)
		{
			if (visibleFlag)
			{
				OnVisibleChanged(e);
			}
		}

		protected virtual void OnEnabledChanged(EventArgs e)
		{
			EnabledChanged?.Invoke(this, e);
			foreach (Control child in Children())
			{
				child.OnParentEnabledChanged(e);
			}
		}

		protected virtual void OnParentEnabledChanged(EventArgs e)
		{
			if (enabledFlag)
			{
				OnEnabledChanged(e);
			}
		}

		// ---------------------------------------------------------------- ambient properties: BackColor, ForeColor, Font, Cursor, RightToLeft

		public static Color DefaultBackColor => Color.FromArgb(240, 240, 240);

		public static Color DefaultForeColor => Color.FromArgb(0, 0, 0);

		/// <summary>The font of the platform's dialogs ("Microsoft Sans Serif" 8.25 pt on the Framework); null until the Drawing layer can make fonts (P3.4).</summary>
		public static Font DefaultFont
		{
			get
			{
				try
				{
					return SystemFonts.DefaultFont;
				}
				catch (NotSupportedException)
				{
					return null;
				}
			}
		}

		protected virtual Cursor DefaultCursor => null;

		public virtual Color BackColor
		{
			get
			{
				Color c;
				lock (sync)
				{
					c = backColor;
				}
				if (!c.IsEmpty)
				{
					return c;
				}
				return parent != null ? parent.BackColor : DefaultBackColor;
			}
			set
			{
				Color before = BackColor;
				lock (sync)
				{
					backColor = value;
				}
				if (before != BackColor)
				{
					Notify("BackColor");
					OnBackColorChanged(EventArgs.Empty);
				}
			}
		}

		internal bool HasOwnBackColor => !backColor.IsEmpty;

		public virtual void ResetBackColor()
		{
			BackColor = Color.Empty;
		}

		protected virtual void OnBackColorChanged(EventArgs e)
		{
			Invalidate();
			BackColorChanged?.Invoke(this, e);
			foreach (Control child in Children())
			{
				child.OnParentBackColorChanged(e);
			}
		}

		protected virtual void OnParentBackColorChanged(EventArgs e)
		{
			if (backColor.IsEmpty)
			{
				OnBackColorChanged(e);
			}
		}

		public virtual Color ForeColor
		{
			get
			{
				Color c;
				lock (sync)
				{
					c = foreColor;
				}
				if (!c.IsEmpty)
				{
					return c;
				}
				return parent != null ? parent.ForeColor : DefaultForeColor;
			}
			set
			{
				Color before = ForeColor;
				lock (sync)
				{
					foreColor = value;
				}
				if (before != ForeColor)
				{
					Notify("ForeColor");
					OnForeColorChanged(EventArgs.Empty);
				}
			}
		}

		internal bool HasOwnForeColor => !foreColor.IsEmpty;

		public virtual void ResetForeColor()
		{
			ForeColor = Color.Empty;
		}

		protected virtual void OnForeColorChanged(EventArgs e)
		{
			Invalidate();
			ForeColorChanged?.Invoke(this, e);
			foreach (Control child in Children())
			{
				child.OnParentForeColorChanged(e);
			}
		}

		protected virtual void OnParentForeColorChanged(EventArgs e)
		{
			if (foreColor.IsEmpty)
			{
				OnForeColorChanged(e);
			}
		}

		public virtual Font Font
		{
			get
			{
				Font f = Volatile.Read(ref font);
				if (f != null)
				{
					return f;
				}
				return parent != null ? parent.Font : DefaultFont;
			}
			set
			{
				Font before = Font;
				Volatile.Write(ref font, value);
				if (!Equals(before, Font))
				{
					Notify("Font");
					OnFontChanged(EventArgs.Empty);
				}
			}
		}

		internal bool HasOwnFont => font != null;

		protected int FontHeight
		{
			get
			{
				if (fontHeight > 0)
				{
					return fontHeight;
				}
				Font f = Font;
				return f == null ? 13 : FormsRuntime.Backend.MeasureText("0", f, 0).Height;
			}
			set => fontHeight = value;
		}

		private int fontHeight = -1;

		public virtual void ResetFont()
		{
			Font = null;
		}

		protected virtual void OnFontChanged(EventArgs e)
		{
			Invalidate();
			FontChanged?.Invoke(this, e);
			foreach (Control child in Children())
			{
				child.OnParentFontChanged(e);
			}
		}

		protected virtual void OnParentFontChanged(EventArgs e)
		{
			if (font == null)
			{
				OnFontChanged(e);
			}
		}

		public virtual Cursor Cursor
		{
			get
			{
				Cursor c = Volatile.Read(ref cursor);
				if (c != null)
				{
					return c;
				}
				return parent != null ? parent.Cursor : DefaultCursor;
			}
			set
			{
				Cursor before = Cursor;
				Volatile.Write(ref cursor, value);
				if (!Equals(before, Cursor))
				{
					Notify("Cursor");
					OnCursorChanged(EventArgs.Empty);
				}
			}
		}

		public virtual void ResetCursor()
		{
			Cursor = null;
		}

		public bool UseWaitCursor
		{
			get => useWaitCursor;
			set
			{
				if (useWaitCursor != value)
				{
					useWaitCursor = value;
					Notify("UseWaitCursor");
					foreach (Control child in Children())
					{
						child.UseWaitCursor = value;
					}
				}
			}
		}

		protected virtual void OnCursorChanged(EventArgs e)
		{
			CursorChanged?.Invoke(this, e);
			foreach (Control child in Children())
			{
				child.OnParentCursorChanged(e);
			}
		}

		protected virtual void OnParentCursorChanged(EventArgs e)
		{
			if (cursor == null)
			{
				OnCursorChanged(e);
			}
		}

		public virtual RightToLeft RightToLeft
		{
			get
			{
				if (rightToLeft == RightToLeft.Inherit)
				{
					return parent != null ? parent.RightToLeft : RightToLeft.No;
				}
				return rightToLeft;
			}
			set
			{
				RightToLeft before = RightToLeft;
				rightToLeft = value;
				if (before != RightToLeft)
				{
					Notify("RightToLeft");
					OnRightToLeftChanged(EventArgs.Empty);
				}
			}
		}

		public virtual void ResetRightToLeft()
		{
			RightToLeft = RightToLeft.Inherit;
		}

		protected virtual void OnRightToLeftChanged(EventArgs e)
		{
			RightToLeftChanged?.Invoke(this, e);
			foreach (Control child in Children())
			{
				child.OnParentRightToLeftChanged(e);
			}
		}

		protected virtual void OnParentRightToLeftChanged(EventArgs e)
		{
			if (rightToLeft == RightToLeft.Inherit)
			{
				OnRightToLeftChanged(e);
			}
		}

		// ---------------------------------------------------------------- plain state

		public virtual bool AllowDrop
		{
			get => allowDrop;
			set => allowDrop = value;
		}

		public virtual Image BackgroundImage
		{
			get => Volatile.Read(ref backgroundImage);
			set
			{
				if (!ReferenceEquals(Volatile.Read(ref backgroundImage), value))
				{
					Volatile.Write(ref backgroundImage, value);
					Notify("BackgroundImage");
					OnBackgroundImageChanged(EventArgs.Empty);
				}
			}
		}

		public virtual ImageLayout BackgroundImageLayout
		{
			get => backgroundImageLayout;
			set
			{
				if (backgroundImageLayout != value)
				{
					backgroundImageLayout = value;
					Notify("BackgroundImageLayout");
					OnBackgroundImageLayoutChanged(EventArgs.Empty);
				}
			}
		}

		public virtual object DataContext
		{
			get => Volatile.Read(ref dataContext);
			set
			{
				if (!Equals(Volatile.Read(ref dataContext), value))
				{
					Volatile.Write(ref dataContext, value);
					OnDataContextChanged(EventArgs.Empty);
				}
			}
		}

		public Region Region
		{
			get => Volatile.Read(ref region);
			set
			{
				if (!ReferenceEquals(Volatile.Read(ref region), value))
				{
					Volatile.Write(ref region, value);
					Notify("Region");
					OnRegionChanged(EventArgs.Empty);
				}
			}
		}

		public virtual ContextMenu ContextMenu
		{
			get => Volatile.Read(ref contextMenu);
			set
			{
				if (!ReferenceEquals(Volatile.Read(ref contextMenu), value))
				{
					Volatile.Write(ref contextMenu, value);
					OnContextMenuChanged(EventArgs.Empty);
				}
			}
		}

		public virtual ContextMenuStrip ContextMenuStrip
		{
			get => Volatile.Read(ref contextMenuStrip);
			set
			{
				if (!ReferenceEquals(Volatile.Read(ref contextMenuStrip), value))
				{
					Volatile.Write(ref contextMenuStrip, value);
					OnContextMenuStripChanged(EventArgs.Empty);
				}
			}
		}

		public virtual Point AutoScrollOffset
		{
			get => autoScrollOffset;
			set => autoScrollOffset = value;
		}

		public ImeMode ImeMode
		{
			get => imeMode == ImeMode.Inherit ? ImeModeBase : imeMode;
			set
			{
				if (imeMode != value)
				{
					imeMode = value;
					OnImeModeChanged(EventArgs.Empty);
				}
			}
		}

		protected virtual ImeMode ImeModeBase
		{
			get => imeMode == ImeMode.Inherit ? DefaultImeMode : imeMode;
			set => ImeMode = value;
		}

		public void ResetImeMode()
		{
			ImeMode = DefaultImeMode;
		}

		protected virtual bool ScaleChildren => true;

		protected internal virtual bool ShowFocusCues => true;

		protected internal virtual bool ShowKeyboardCues => true;

		protected internal bool RenderRightToLeft => RightToLeft == RightToLeft.Yes;

		public bool IsMirrored => false;

		public bool IsAncestorSiteInDesignMode => false;

		// ---------------------------------------------------------------- input state of the device (static)

		public static Keys ModifierKeys => FormsRuntime.Backend.GetModifierKeys();

		public static MouseButtons MouseButtons => FormsRuntime.Backend.GetMouseButtons();

		public static Point MousePosition => FormsRuntime.Backend.GetMousePosition();

		// ---------------------------------------------------------------- drawing requests (the surface itself is P4.10e)

		public void Invalidate() => Invalidate(false);

		public void Invalidate(bool invalidateChildren)
		{
			InvalidateCore(ClientRectangle, invalidateChildren);
		}

		public void Invalidate(Rectangle rc) => Invalidate(rc, false);

		public void Invalidate(Rectangle rc, bool invalidateChildren)
		{
			InvalidateCore(rc, invalidateChildren);
		}

		public void Invalidate(Region region) => Invalidate(region, false);

		public void Invalidate(Region region, bool invalidateChildren)
		{
			InvalidateCore(ClientRectangle, invalidateChildren);
		}

		private void InvalidateCore(Rectangle area, bool invalidateChildren)
		{
			if (!handleCreated || !GetVisibleCore())
			{
				return;
			}
			OnInvalidated(new InvalidateEventArgs(area));
			IControlPeer p = peer;
			if (p != null)
			{
				IFormsBackend backend = FormsRuntime.Backend;
				if (backend.CheckAccess())
				{
					FormsRuntime.Guard(() => p.Invalidate(area));
				}
				else
				{
					backend.Post(() => FormsRuntime.Guard(() => p.Invalidate(area)));
				}
			}
			if (invalidateChildren)
			{
				foreach (Control child in Children())
				{
					child.Invalidate(true);
				}
			}
		}

		public void Update()
		{
		}

		public virtual void Refresh()
		{
			Invalidate(true);
			Update();
		}

		protected virtual void NotifyInvalidate(Rectangle invalidatedArea)
		{
		}

		// ---------------------------------------------------------------- dispose

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (this.disposing || isDisposed)
				{
					return;
				}
				this.disposing = true;
				SuspendLayout();
				try
				{
					Control f = Volatile.Read(ref focused);
					if (f != null && (f == this || Contains(f)))
					{
						Volatile.Write(ref focused, null);
					}
					if (handleCreated)
					{
						DestroyHandle();
					}
					Control p = parent;
					if (p != null)
					{
						p.Controls.Remove(this);
					}
					ControlCollection own = controls;
					if (own != null)
					{
						foreach (Control child in own.Snapshot())
						{
							child.parent = null;
							child.Dispose();
						}
						controls = null;
					}
					// Component.Dispose raises Disposed only while CanRaiseEvents is true: the control stops "disposing" first.
					isDisposed = true;
					this.disposing = false;
					base.Dispose(disposing);
				}
				finally
				{
					ResumeLayout(false);
				}
			}
			else
			{
				isDisposed = true;
				base.Dispose(disposing);
			}
		}
	}
}
