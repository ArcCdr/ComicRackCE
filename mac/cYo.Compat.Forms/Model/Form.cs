using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using cYo.Compat.Forms;

// The object model of System.Windows.Forms.Form for modeless forms (P4.9a): state, Show, Hide, Activate and Close with the events in the order of dotnet/winforms.
// ShowDialog, DialogResult semantics, AcceptButton/CancelButton keys and MessageBox are P4.9f; the properties exist and keep their values.
namespace System.Windows.Forms
{
	public partial class Form : ContainerControl
	{
		private static Form activeForm;

		private IButtonControl acceptButton;
		private IButtonControl cancelButton;
		private DialogResult dialogResult;
		private FormBorderStyle formBorderStyle = FormBorderStyle.Sizable;
		private FormStartPosition startPosition = FormStartPosition.WindowsDefaultLocation;
		private FormWindowState windowState;
		private SizeGripStyle sizeGripStyle;
		private AutoSizeMode formAutoSizeMode = AutoSizeMode.GrowOnly;
		private bool controlBox = true;
		private bool helpButton;
		private bool keyPreview;
		private bool maximizeBox = true;
		private bool minimizeBox = true;
		private bool showIcon = true;
		private bool showInTaskbar = true;
		private bool topMost;
		private bool allowTransparency;
		private bool rightToLeftLayout;
		private double opacity = 1.0;
		private Color transparencyKey;
		private Rectangle maximizedBounds;
		private Icon icon;
		private MenuStrip mainMenuStrip;
		private MainMenu menu;
		private Form owner;
		private readonly List<Form> ownedForms = new List<Form>();
		private bool calledMakeVisible;
		private bool calledOnLoad;
		private bool shownRaised;
		private bool closeInProgress;
		private bool activated;
		private Rectangle restoreBounds;

		public Form()
		{
			TopLevelInternal = true;
			VisibleFlag = false;
			SetStyle(ControlStyles.ContainerControl, true);
			SetStyle(ControlStyles.Selectable, false);
			TabStop = false;
		}

		protected override Size DefaultSize => new Size(300, 300);

		// ---------------------------------------------------------------- the window frame (Windows 10 at 96 dpi: the sizable frame is 16 x 39, as every form of the Framework has it)

		internal override Size NonClientSize
		{
			get
			{
				switch (formBorderStyle)
				{
				case FormBorderStyle.None:
					return Size.Empty;
				case FormBorderStyle.FixedSingle:
				case FormBorderStyle.FixedDialog:
					return new Size(8, 31);
				case FormBorderStyle.Fixed3D:
					return new Size(12, 35);
				case FormBorderStyle.FixedToolWindow:
					return new Size(8, 26);
				case FormBorderStyle.SizableToolWindow:
					return new Size(16, 34);
				default:
					return new Size(16, 39);
				}
			}
		}

		internal override Point ClientOffset
		{
			get
			{
				Size nc = NonClientSize;
				return new Point(nc.Width / 2, nc.Height - nc.Width / 2);
			}
		}

		public FormBorderStyle FormBorderStyle
		{
			get => formBorderStyle;
			set
			{
				if (formBorderStyle == value)
				{
					return;
				}
				Size client = ClientSize;
				formBorderStyle = value;
				Size = SizeFromClientSize(client);
				Notify("FormBorderStyle");
			}
		}

		// ---------------------------------------------------------------- plain state

		public IButtonControl AcceptButton
		{
			get => acceptButton;
			set => acceptButton = value;
		}

		public IButtonControl CancelButton
		{
			get => cancelButton;
			set => cancelButton = value;
		}

		public DialogResult DialogResult
		{
			get => dialogResult;
			set
			{
				if (!Enum.IsDefined(typeof(DialogResult), value))
				{
					throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(DialogResult));
				}
				dialogResult = value;
			}
		}

		public bool ControlBox
		{
			get => controlBox;
			set
			{
				controlBox = value;
				Notify("ControlBox");
			}
		}

		public bool HelpButton
		{
			get => helpButton;
			set
			{
				helpButton = value;
				Notify("HelpButton");
			}
		}

		public bool KeyPreview
		{
			get => keyPreview;
			set => keyPreview = value;
		}

		public bool MaximizeBox
		{
			get => maximizeBox;
			set
			{
				maximizeBox = value;
				Notify("MaximizeBox");
			}
		}

		public bool MinimizeBox
		{
			get => minimizeBox;
			set
			{
				minimizeBox = value;
				Notify("MinimizeBox");
			}
		}

		public bool ShowIcon
		{
			get => showIcon;
			set
			{
				showIcon = value;
				Notify("ShowIcon");
			}
		}

		public bool ShowInTaskbar
		{
			get => showInTaskbar;
			set
			{
				showInTaskbar = value;
				Notify("ShowInTaskbar");
			}
		}

		public bool TopMost
		{
			get => topMost;
			set
			{
				topMost = value;
				Notify("TopMost");
			}
		}

		public bool TopLevel
		{
			get => TopLevelInternal;
			set => TopLevelInternal = value;
		}

		public bool AllowTransparency
		{
			get => allowTransparency;
			set => allowTransparency = value;
		}

		public double Opacity
		{
			get => opacity;
			set
			{
				opacity = Math.Max(0.0, Math.Min(1.0, value));
				Notify("Opacity");
			}
		}

		public Color TransparencyKey
		{
			get => transparencyKey;
			set
			{
				transparencyKey = value;
				Notify("TransparencyKey");
			}
		}

		public Icon Icon
		{
			get => icon;
			set
			{
				icon = value;
				Notify("Icon");
			}
		}

		public MenuStrip MainMenuStrip
		{
			get => mainMenuStrip;
			set => mainMenuStrip = value;
		}

		public MainMenu Menu
		{
			get => menu;
			set => menu = value;
		}

		public SizeGripStyle SizeGripStyle
		{
			get => sizeGripStyle;
			set
			{
				sizeGripStyle = value;
				Notify("SizeGripStyle");
			}
		}

		public FormStartPosition StartPosition
		{
			get => startPosition;
			set => startPosition = value;
		}

		public FormWindowState WindowState
		{
			get => windowState;
			set
			{
				if (windowState != value)
				{
					if (windowState == FormWindowState.Normal)
					{
						restoreBounds = Bounds;
					}
					windowState = value;
					Notify("WindowState");
					OnResize(EventArgs.Empty);
				}
			}
		}

		public Rectangle RestoreBounds => windowState == FormWindowState.Normal ? Bounds : restoreBounds;

		public AutoSizeMode AutoSizeMode
		{
			get => formAutoSizeMode;
			set
			{
				if (formAutoSizeMode != value)
				{
					formAutoSizeMode = value;
					SetAutoSizeMode(value);
					PerformLayout(this, "AutoSize");
				}
			}
		}

		public virtual bool RightToLeftLayout
		{
			get => rightToLeftLayout;
			set
			{
				if (rightToLeftLayout != value)
				{
					rightToLeftLayout = value;
					OnRightToLeftLayoutChanged(EventArgs.Empty);
				}
			}
		}

		protected Rectangle MaximizedBounds
		{
			get => maximizedBounds;
			set
			{
				if (maximizedBounds != value)
				{
					maximizedBounds = value;
					OnMaximizedBoundsChanged(EventArgs.Empty);
				}
			}
		}

		protected virtual bool ShowWithoutActivation => false;

		public bool IsMdiChild => false;

		public bool IsMdiContainer
		{
			get => false;
			set
			{
				if (value)
				{
					throw new NotSupportedException("System.Windows.Forms.Form.IsMdiContainer is not yet supported on macOS");
				}
			}
		}

		public bool IsRestrictedWindow => false;

		public Form ActiveMdiChild => null;

		public Form[] MdiChildren => Array.Empty<Form>();

		public bool MdiChildrenMinimizedAnchorBottom
		{
			get => true;
			set
			{
			}
		}

		public Form MdiParent
		{
			get => null;
			set
			{
				if (value != null)
				{
					throw new NotSupportedException("System.Windows.Forms.Form.MdiParent is not yet supported on macOS");
				}
			}
		}

		public Rectangle DesktopBounds
		{
			get => Bounds;
			set => SetDesktopBounds(value.X, value.Y, value.Width, value.Height);
		}

		public Point DesktopLocation
		{
			get => Location;
			set => SetDesktopLocation(value.X, value.Y);
		}

		public void SetDesktopBounds(int x, int y, int width, int height)
		{
			SetBounds(x, y, width, height);
		}

		public void SetDesktopLocation(int x, int y)
		{
			Location = new Point(x, y);
		}

		public override Size MinimumSize
		{
			get => base.MinimumSize;
			set
			{
				if (base.MinimumSize != value)
				{
					base.MinimumSize = value;
					OnMinimumSizeChanged(EventArgs.Empty);
				}
			}
		}

		public override Size MaximumSize
		{
			get => base.MaximumSize;
			set
			{
				if (base.MaximumSize != value)
				{
					base.MaximumSize = value;
					OnMaximumSizeChanged(EventArgs.Empty);
				}
			}
		}

		// ---------------------------------------------------------------- owners

		public Form Owner
		{
			get => owner;
			set
			{
				if (owner == value)
				{
					return;
				}
				if (value == this)
				{
					throw new ArgumentException("A form cannot own itself.", nameof(value));
				}
				Form old = owner;
				owner = value;
				old?.DropOwned(this);
				value?.KeepOwned(this);
				Notify("Owner");
			}
		}

		public Form[] OwnedForms
		{
			get
			{
				lock (ownedForms)
				{
					return ownedForms.ToArray();
				}
			}
		}

		private void KeepOwned(Form form)
		{
			lock (ownedForms)
			{
				if (!ownedForms.Contains(form))
				{
					ownedForms.Add(form);
				}
			}
		}

		private void DropOwned(Form form)
		{
			lock (ownedForms)
			{
				ownedForms.Remove(form);
			}
		}

		public void AddOwnedForm(Form ownedForm)
		{
			if (ownedForm == null)
			{
				return;
			}
			if (ownedForm.owner != this)
			{
				ownedForm.Owner = this;
			}
			else
			{
				KeepOwned(ownedForm);
			}
		}

		public void RemoveOwnedForm(Form ownedForm)
		{
			if (ownedForm == null)
			{
				return;
			}
			if (ownedForm.owner == this)
			{
				ownedForm.Owner = null;
			}
			else
			{
				DropOwned(ownedForm);
			}
		}

		// ---------------------------------------------------------------- activation

		/// <summary>The form that is active now (the last one that was shown or activated and not closed or hidden); null when none.</summary>
		public static Form ActiveForm => Volatile.Read(ref activeForm);

		internal static void ResetActiveForm()
		{
			Volatile.Write(ref activeForm, null);
		}

		/// <summary>Makes this form the active window: Deactivate of the form that was active, then Activated, then the focus goes to the active control or the first control in tab order.</summary>
		public void Activate()
		{
			if (IsDisposed || !Visible || !IsHandleCreated)
			{
				return;
			}
			ActivateCore();
		}

		private void ActivateCore()
		{
			Form previous = Volatile.Read(ref activeForm);
			if (previous == this)
			{
				return;
			}
			Volatile.Write(ref activeForm, this);
			if (previous != null && !previous.IsDisposed)
			{
				previous.activated = false;
				previous.OnDeactivate(EventArgs.Empty);
			}
			activated = true;
			IFormPeer fp = Peer as IFormPeer;
			if (fp != null)
			{
				OnUi(fp.Activate);
			}
			OnActivated(EventArgs.Empty);
			FocusOnActivation();
		}

		private void FocusOnActivation()
		{
			Control target = ActiveControl;
			if (target == null)
			{
				Control first = GetNextControlForActivation();
				if (first != null)
				{
					ActiveControl = first;
					return;
				}
			}
			else if (target.CanFocus)
			{
				target.FocusCore(selectOrder: true);
			}
		}

		private Control GetNextControlForActivation()
		{
			foreach (Control c in ChildrenInTabOrder())
			{
				if (c.CanSelect && c.TabStop)
				{
					return c;
				}
			}
			return null;
		}

		private void DeactivateIfActive()
		{
			if (ReferenceEquals(Volatile.Read(ref activeForm), this))
			{
				Volatile.Write(ref activeForm, null);
				activated = false;
				OnDeactivate(EventArgs.Empty);
			}
		}

		internal bool IsActivated => activated;

		// ---------------------------------------------------------------- show, hide, close

		/// <summary>Runs an action on the UI thread (peers are touched there only).</summary>
		private static void OnUi(Action action)
		{
			IFormsBackend backend = FormsRuntime.Backend;
			if (backend.CheckAccess())
			{
				action();
			}
			else
			{
				backend.Invoke(action);
			}
		}

		public void Show(IWin32Window owner)
		{
			if (owner == this)
			{
				throw new InvalidOperationException("A form cannot be its own owner.");
			}
			if (owner != null)
			{
				Form ownerForm = owner as Form ?? (owner as Control)?.FindForm();
				if (ownerForm == null)
				{
					throw new NotSupportedException("An owner that is not a form is not supported on macOS.");
				}
				Owner = ownerForm;
			}
			Show();
		}

		/// <summary>
		/// Visible = true on a form: the handles of the form and its controls are created (HandleCreated, parent before children), Load is raised while Visible is still false, then
		/// VisibleChanged, Activated, and Shown after the caller returned to the message loop. Visible = false hides the window and deactivates it.
		/// </summary>
		protected override void SetVisibleCore(bool value)
		{
			if (value)
			{
				if (IsDisposed)
				{
					throw new ObjectDisposedException(GetType().Name);
				}
				if (!calledMakeVisible)
				{
					calledMakeVisible = true;
				}
				if (!Created)
				{
					CreateControl(ignoreVisible: true);
				}
			}
			bool before = GetVisibleCore();
			VisibleFlag = value;
			if (before == value)
			{
				return;
			}
			IFormPeer fp = Peer as IFormPeer;
			if (fp != null)
			{
				if (value)
				{
					IFormPeer ownerPeer = owner?.Peer as IFormPeer;
					OnUi(() => fp.Show(ownerPeer));
				}
				else
				{
					OnUi(fp.Hide);
				}
			}
			OnVisibleChanged(EventArgs.Empty);
			if (value)
			{
				if (!ShowWithoutActivation)
				{
					ActivateCore();
				}
				if (!shownRaised)
				{
					shownRaised = true;
					PostShown();
				}
			}
			else
			{
				ReleaseFocusOfHiddenForm();
				DeactivateIfActive();
			}
		}

		private void ReleaseFocusOfHiddenForm()
		{
			Control focus = FocusedControl;
			if (focus != null && Contains(focus))
			{
				focus.LoseFocus();
			}
		}

		private void PostShown()
		{
			FormsRuntime.Backend.Post(() => FormsRuntime.Guard(() =>
			{
				if (!IsDisposed && Visible)
				{
					OnShown(EventArgs.Empty);
				}
			}));
		}

		protected override void OnCreateControl()
		{
			base.OnCreateControl();
			if (calledMakeVisible && !calledOnLoad)
			{
				calledOnLoad = true;
				PositionForFirstShow();
				OnLoad(EventArgs.Empty);
			}
		}

		private void PositionForFirstShow()
		{
			switch (startPosition)
			{
			case FormStartPosition.CenterScreen:
				CenterToScreen();
				break;
			case FormStartPosition.CenterParent:
				CenterToParent();
				break;
			}
		}

		protected void CenterToScreen()
		{
			Rectangle screen = FormsRuntime.Backend.GetScreenBounds();
			Size size = Size;
			Location = new Point(screen.X + (screen.Width - size.Width) / 2, screen.Y + (screen.Height - size.Height) / 2);
		}

		protected void CenterToParent()
		{
			Form centerOn = owner;
			if (centerOn == null || !centerOn.Visible)
			{
				CenterToScreen();
				return;
			}
			Rectangle area = centerOn.Bounds;
			Size size = Size;
			Location = new Point(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2);
		}

		/// <summary>
		/// Close(): FormClosing (Closing first, then FormClosing; both can cancel), then Closed and FormClosed, then Deactivate; a form that was shown is disposed. A form that was
		/// never shown is only disposed, without events. Close reports CloseReason.UserClosing, as the Framework does.
		/// </summary>
		public void Close()
		{
			if (IsDisposed || Disposing)
			{
				return;
			}
			if (!IsHandleCreated)
			{
				Dispose();
				return;
			}
			CloseCore(CloseReason.UserClosing);
		}

		/// <summary>The entry point of a peer whose window is closed by the user (the close box).</summary>
		internal bool CloseByUser() => CloseCore(CloseReason.UserClosing);

		private bool CloseCore(CloseReason reason)
		{
			if (closeInProgress || IsDisposed)
			{
				return false;
			}
			closeInProgress = true;
			try
			{
				foreach (Form owned in OwnedForms)
				{
					if (!owned.IsDisposed && owned.IsHandleCreated)
					{
						owned.CloseCore(CloseReason.FormOwnerClosing);
					}
				}
				var closing = new FormClosingEventArgs(reason, false);
				OnClosing(closing);
				OnFormClosing(closing);
				if (closing.Cancel)
				{
					dialogResult = DialogResult.None;
					return false;
				}
				var closed = new FormClosedEventArgs(reason);
				OnClosed(closed);
				OnFormClosed(closed);
				DeactivateIfActive();
				Dispose();
				return true;
			}
			finally
			{
				closeInProgress = false;
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (IsDisposed || Disposing)
				{
					return;
				}
				IFormPeer fp = Peer as IFormPeer;
				if (fp != null && GetVisibleCore())
				{
					VisibleFlag = false;
					OnUi(fp.Hide);
				}
				if (ReferenceEquals(Volatile.Read(ref activeForm), this))
				{
					Volatile.Write(ref activeForm, null);
				}
				Owner = null;
				foreach (Form owned in OwnedForms)
				{
					owned.Owner = null;
				}
			}
			base.Dispose(disposing);
		}

		// ---------------------------------------------------------------- events

		protected virtual void OnLoad(EventArgs e)
		{
			Load?.Invoke(this, e);
		}

		protected virtual void OnShown(EventArgs e)
		{
			Shown?.Invoke(this, e);
		}

		protected virtual void OnActivated(EventArgs e)
		{
			Activated?.Invoke(this, e);
		}

		protected virtual void OnDeactivate(EventArgs e)
		{
			Deactivate?.Invoke(this, e);
		}

		protected virtual void OnClosing(CancelEventArgs e)
		{
			Closing?.Invoke(this, e);
		}

		protected virtual void OnClosed(EventArgs e)
		{
			Closed?.Invoke(this, e);
		}

		protected virtual void OnFormClosing(FormClosingEventArgs e)
		{
			FormClosing?.Invoke(this, e);
		}

		protected virtual void OnFormClosed(FormClosedEventArgs e)
		{
			FormClosed?.Invoke(this, e);
		}

		public override string ToString()
		{
			return base.ToString() + ", Text: " + Text;
		}
	}
}
