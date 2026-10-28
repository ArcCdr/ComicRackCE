using System;
using System.ComponentModel;
using System.Drawing;

// The container controls (P4.9a): ScrollableControl, ContainerControl (active control, validation), UserControl, Panel and GroupBox. Layout of their children is P4.9b to d.
namespace System.Windows.Forms
{
	public partial class ScrollableControl : Control
	{
		private bool autoScroll;
		private Size autoScrollMargin;
		private Size autoScrollMinSize;
		private Point autoScrollPosition;

		public ScrollableControl()
		{
		}

		private DockPaddingEdges dockPadding;

		/// <summary>The client area minus the padding; with AutoScroll at least as large as AutoScrollMinSize (the area that docked and anchored children are arranged in).</summary>
		public override Rectangle DisplayRectangle
		{
			get
			{
				Rectangle rect = ClientRectangle;
				if (autoScroll)
				{
					Size minimum = AutoScrollMinSize;
					rect.Width = Math.Max(rect.Width, minimum.Width);
					rect.Height = Math.Max(rect.Height, minimum.Height);
				}
				Padding p = Padding;
				if (p != Padding.Empty)
				{
					rect.X += p.Left;
					rect.Y += p.Top;
					rect.Width -= p.Horizontal;
					rect.Height -= p.Vertical;
				}
				return rect;
			}
		}

		/// <summary>The padding of the container as the edges that docked children keep away from.</summary>
		public DockPaddingEdges DockPadding => dockPadding ?? (dockPadding = new DockPaddingEdges(this));

		public partial class DockPaddingEdges
		{
			private readonly ScrollableControl owner;

			internal DockPaddingEdges(ScrollableControl owner)
			{
				this.owner = owner;
			}

			public int All
			{
				get => owner.Padding.All;
				set => owner.Padding = new Padding(value);
			}

			public int Left
			{
				get => owner.Padding.Left;
				set
				{
					Padding p = owner.Padding;
					owner.Padding = new Padding(value, p.Top, p.Right, p.Bottom);
				}
			}

			public int Top
			{
				get => owner.Padding.Top;
				set
				{
					Padding p = owner.Padding;
					owner.Padding = new Padding(p.Left, value, p.Right, p.Bottom);
				}
			}

			public int Right
			{
				get => owner.Padding.Right;
				set
				{
					Padding p = owner.Padding;
					owner.Padding = new Padding(p.Left, p.Top, value, p.Bottom);
				}
			}

			public int Bottom
			{
				get => owner.Padding.Bottom;
				set
				{
					Padding p = owner.Padding;
					owner.Padding = new Padding(p.Left, p.Top, p.Right, value);
				}
			}
		}

		public virtual bool AutoScroll
		{
			get => autoScroll;
			set
			{
				if (autoScroll != value)
				{
					autoScroll = value;
					Notify("AutoScroll");
					PerformLayout(this, "AutoScroll");
				}
			}
		}

		public Size AutoScrollMargin
		{
			get
			{
				lock (SyncRoot)
				{
					return autoScrollMargin;
				}
			}
			set
			{
				if (value.Width < 0 || value.Height < 0)
				{
					throw new ArgumentOutOfRangeException(nameof(value), value, "The value of 'AutoScrollMargin' cannot be negative.");
				}
				lock (SyncRoot)
				{
					autoScrollMargin = value;
				}
				PerformLayout(this, "AutoScrollMargin");
			}
		}

		public Size AutoScrollMinSize
		{
			get
			{
				lock (SyncRoot)
				{
					return autoScrollMinSize;
				}
			}
			set
			{
				bool changed;
				lock (SyncRoot)
				{
					changed = autoScrollMinSize != value;
					if (changed)
					{
						autoScrollMinSize = value;
					}
				}
				if (changed)
				{
					AutoScroll = true;
					PerformLayout(this, "AutoScrollMinSize");
				}
			}
		}

		public Point AutoScrollPosition
		{
			get
			{
				lock (SyncRoot)
				{
					return new Point(-autoScrollPosition.X, -autoScrollPosition.Y);
				}
			}
			set
			{
				lock (SyncRoot)
				{
					autoScrollPosition = new Point(Math.Abs(value.X), Math.Abs(value.Y));
				}
				Notify("AutoScrollPosition");
			}
		}

		public void SetAutoScrollMargin(int x, int y)
		{
			AutoScrollMargin = new Size(Math.Max(0, x), Math.Max(0, y));
		}

		public void ScrollControlIntoView(Control activeControl)
		{
		}

		protected virtual void OnScroll(ScrollEventArgs se)
		{
			Scroll?.Invoke(this, se);
		}

		public event ScrollEventHandler Scroll;
	}

	public partial class ContainerControl : ScrollableControl, IContainerControl
	{
		private Control activeControl;
		private AutoScaleMode autoScaleMode = AutoScaleMode.Inherit;
		private SizeF autoScaleDimensions;
		private AutoValidate autoValidate = AutoValidate.Inherit;

		public ContainerControl()
		{
			SetStyle(ControlStyles.ContainerControl, true);
		}

		public Control ActiveControl
		{
			get => activeControl;
			set => SetActiveControl(value);
		}

		private void SetActiveControl(Control value)
		{
			if (value != null && (value == this || !Contains(value)))
			{
				throw new ArgumentException("Cannot activate invisible or disabled control.");
			}
			if (ReferenceEquals(activeControl, value) && (value == null || value.Focused))
			{
				return;
			}
			activeControl = value;
			if (value == null)
			{
				return;
			}
			// A user control or form inside this one keeps track of its own active control.
			for (Control c = value; c != this; c = c.ParentInternal)
			{
				if (c.ParentInternal is ContainerControl inner && inner != this)
				{
					inner.activeControl = c;
				}
			}
			Form top = FindForm();
			bool formIsActive = top == null || top.IsActivated;
			if (IsHandleCreated && value.CanFocus && formIsActive)
			{
				value.FocusCore(selectOrder: true);
			}
		}

		/// <summary>The control was removed from a descendant: the active control must not be one that is no longer inside.</summary>
		internal void AfterControlRemoved(Control control, Control oldParent)
		{
			if (activeControl != null && (activeControl == control || control.Contains(activeControl)))
			{
				activeControl = null;
			}
		}

		bool IContainerControl.ActivateControl(Control active)
		{
			try
			{
				ActiveControl = active;
			}
			catch (ArgumentException)
			{
				return false;
			}
			return true;
		}

		public SizeF AutoScaleDimensions
		{
			get
			{
				lock (SyncRoot)
				{
					return autoScaleDimensions;
				}
			}
			set
			{
				if (value.Width < 0 || value.Height < 0)
				{
					throw new ArgumentOutOfRangeException(nameof(value), value, "The value of 'AutoScaleDimensions' cannot be negative.");
				}
				lock (SyncRoot)
				{
					autoScaleDimensions = value;
				}
				if (!value.IsEmpty)
				{
					autoScaleMode = AutoScaleMode.Dpi;
				}
			}
		}

		public AutoScaleMode AutoScaleMode
		{
			get => autoScaleMode;
			set
			{
				if (!Enum.IsDefined(typeof(AutoScaleMode), value))
				{
					throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(AutoScaleMode));
				}
				if (autoScaleMode != value)
				{
					if (autoScaleMode != AutoScaleMode.Inherit)
					{
						lock (SyncRoot)
						{
							autoScaleDimensions = SizeF.Empty;
						}
					}
					autoScaleMode = value;
				}
			}
		}

		public virtual AutoValidate AutoValidate
		{
			get => autoValidate == AutoValidate.Inherit ? AutoValidate.EnablePreventFocusChange : autoValidate;
			set
			{
				if (autoValidate != value)
				{
					autoValidate = value;
					OnAutoValidateChanged(EventArgs.Empty);
				}
			}
		}

		public Form ParentForm => ParentInternal?.FindForm();

		public bool Validate() => Validate(false);

		public bool Validate(bool checkAutoValidate)
		{
			if (checkAutoValidate && AutoValidate == AutoValidate.Disable)
			{
				return true;
			}
			Control c = activeControl;
			if (c == null || !c.CausesValidation)
			{
				return true;
			}
			var cancel = new CancelEventArgs();
			c.RaiseValidating(cancel);
			if (cancel.Cancel)
			{
				return false;
			}
			c.RaiseValidated();
			return true;
		}

		public virtual bool ValidateChildren() => ValidateChildren(ValidationConstraints.Selectable);

		public virtual bool ValidateChildren(ValidationConstraints validationConstraints)
		{
			if (!Enum.IsDefined(typeof(ValidationConstraints), validationConstraints) && validationConstraints != (ValidationConstraints.Enabled | ValidationConstraints.Visible))
			{
				// the flags are combined: only values outside the defined bits are an error
				if (((int)validationConstraints & ~0x1F) != 0)
				{
					throw new InvalidEnumArgumentException(nameof(validationConstraints), (int)validationConstraints, typeof(ValidationConstraints));
				}
			}
			return ValidateChildrenOf(this, validationConstraints);
		}

		private static bool ValidateChildrenOf(Control container, ValidationConstraints constraints)
		{
			foreach (Control child in container.ChildrenInTabOrder())
			{
				bool allowed = true;
				if ((constraints & ValidationConstraints.Enabled) != 0 && !child.Enabled)
				{
					allowed = false;
				}
				if ((constraints & ValidationConstraints.Visible) != 0 && !child.Visible)
				{
					allowed = false;
				}
				if ((constraints & ValidationConstraints.Selectable) != 0 && !child.CanSelect)
				{
					allowed = false;
				}
				if ((constraints & ValidationConstraints.TabStop) != 0 && !child.TabStop)
				{
					allowed = false;
				}
				if (allowed && child.CausesValidation)
				{
					var cancel = new CancelEventArgs();
					child.RaiseValidating(cancel);
					if (cancel.Cancel)
					{
						return false;
					}
					child.RaiseValidated();
				}
				if ((constraints & ValidationConstraints.ImmediateChildren) == 0 && child.HasChildren && !ValidateChildrenOf(child, constraints))
				{
					return false;
				}
			}
			return true;
		}

		protected virtual void OnAutoValidateChanged(EventArgs e)
		{
			AutoValidateChanged?.Invoke(this, e);
		}

		public event EventHandler AutoValidateChanged;

		protected virtual void UpdateDefaultButton()
		{
		}
	}

	public partial class UserControl : ContainerControl
	{
		private BorderStyle borderStyle;
		private AutoSizeMode userAutoSizeMode = AutoSizeMode.GrowOnly;

		public UserControl()
		{
			SetStyle(ControlStyles.SupportsTransparentBackColor, true);
			TabStop = false;
		}

		protected override Size DefaultSize => new Size(150, 150);

		internal override Size NonClientSize => BorderSize(borderStyle);

		internal override Point ClientOffset => new Point(BorderSize(borderStyle).Width / 2, BorderSize(borderStyle).Height / 2);

		internal static Size BorderSize(BorderStyle style)
		{
			switch (style)
			{
			case BorderStyle.FixedSingle:
				return new Size(2, 2);
			case BorderStyle.Fixed3D:
				return new Size(4, 4);
			default:
				return Size.Empty;
			}
		}

		public BorderStyle BorderStyle
		{
			get => borderStyle;
			set
			{
				if (borderStyle != value)
				{
					Size client = ClientSize;
					borderStyle = value;
					Size = SizeFromClientSize(client);
					Notify("BorderStyle");
				}
			}
		}

		public AutoSizeMode AutoSizeMode
		{
			get => userAutoSizeMode;
			set
			{
				if (userAutoSizeMode != value)
				{
					userAutoSizeMode = value;
					SetAutoSizeMode(value);
					PerformLayout(this, "AutoSize");
				}
			}
		}

		protected override void OnCreateControl()
		{
			base.OnCreateControl();
			OnLoad(EventArgs.Empty);
		}

		protected virtual void OnLoad(EventArgs e)
		{
			Load?.Invoke(this, e);
		}

		public event EventHandler Load;
	}

	public partial class Panel : ScrollableControl
	{
		private BorderStyle borderStyle;
		private AutoSizeMode panelAutoSizeMode = AutoSizeMode.GrowOnly;

		public Panel()
		{
			TabStop = false;
			SetStyle(ControlStyles.Selectable, false);
			SetStyle(ControlStyles.SupportsTransparentBackColor, true);
		}

		protected override Size DefaultSize => new Size(200, 100);

		internal override Size NonClientSize => UserControl.BorderSize(borderStyle);

		internal override Point ClientOffset => new Point(UserControl.BorderSize(borderStyle).Width / 2, UserControl.BorderSize(borderStyle).Height / 2);

		public BorderStyle BorderStyle
		{
			get => borderStyle;
			set
			{
				if (borderStyle != value)
				{
					Size client = ClientSize;
					borderStyle = value;
					Size = SizeFromClientSize(client);
					Notify("BorderStyle");
				}
			}
		}

		public virtual AutoSizeMode AutoSizeMode
		{
			get => panelAutoSizeMode;
			set
			{
				if (panelAutoSizeMode != value)
				{
					panelAutoSizeMode = value;
					SetAutoSizeMode(value);
					PerformLayout(this, "AutoSize");
				}
			}
		}

		public override string ToString() => base.ToString() + ", BorderStyle: " + BorderStyle;
	}

	public partial class GroupBox : Control
	{
		private FlatStyle flatStyle = FlatStyle.Standard;
		private bool useCompatibleTextRendering;
		private AutoSizeMode groupAutoSizeMode = AutoSizeMode.GrowOnly;

		public GroupBox()
		{
			SetStyle(ControlStyles.ContainerControl, true);
			SetStyle(ControlStyles.SupportsTransparentBackColor, true);
			SetStyle(ControlStyles.Selectable, false);
			TabStop = false;
		}

		protected override Size DefaultSize => new Size(200, 100);

		protected override Padding DefaultPadding => new Padding(3);

		public override Rectangle DisplayRectangle
		{
			get
			{
				Size client = ClientSize;
				Padding p = Padding;
				int top = FontHeight + 3 + p.Top;
				return new Rectangle(p.Left + 3, top, Math.Max(0, client.Width - p.Horizontal - 6), Math.Max(0, client.Height - top - p.Bottom - 3));
			}
		}

		public FlatStyle FlatStyle
		{
			get => flatStyle;
			set
			{
				flatStyle = value;
				Notify("FlatStyle");
			}
		}

		public bool UseCompatibleTextRendering
		{
			get => useCompatibleTextRendering;
			set => useCompatibleTextRendering = value;
		}

		public AutoSizeMode AutoSizeMode
		{
			get => groupAutoSizeMode;
			set
			{
				if (groupAutoSizeMode != value)
				{
					groupAutoSizeMode = value;
					SetAutoSizeMode(value);
					PerformLayout(this, "AutoSize");
				}
			}
		}

		public override string ToString() => base.ToString() + ", Text: " + Text;
	}
}
