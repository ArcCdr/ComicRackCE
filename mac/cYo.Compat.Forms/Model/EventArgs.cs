using System;
using System.ComponentModel;
using System.Drawing;

// The data classes of the events that the object model raises (P4.9a). Signatures and values follow the reference assembly of the Windows Desktop pack.
namespace System.Windows.Forms
{
	public partial class ControlEventArgs : EventArgs
	{
		public ControlEventArgs(Control control)
		{
			Control = control;
		}

		public Control Control
		{
			get;
		}
	}

	public sealed partial class LayoutEventArgs : EventArgs
	{
		public LayoutEventArgs(Control affectedControl, string affectedProperty)
		{
			AffectedControl = affectedControl;
			AffectedProperty = affectedProperty;
		}

		public LayoutEventArgs(IComponent affectedComponent, string affectedProperty)
		{
			AffectedComponent = affectedComponent;
			AffectedControl = affectedComponent as Control;
			AffectedProperty = affectedProperty;
		}

		public IComponent AffectedComponent
		{
			get;
		}

		public Control AffectedControl
		{
			get;
		}

		public string AffectedProperty
		{
			get;
		}
	}

	public partial class InvalidateEventArgs : EventArgs
	{
		public InvalidateEventArgs(Rectangle invalidRect)
		{
			InvalidRect = invalidRect;
		}

		public Rectangle InvalidRect
		{
			get;
		}
	}

	public partial class PaintEventArgs : EventArgs
	{
		public PaintEventArgs(Graphics graphics, Rectangle clipRect)
		{
			if (graphics == null)
			{
				throw new ArgumentNullException(nameof(graphics));
			}
			Graphics = graphics;
			ClipRectangle = clipRect;
		}

		public Rectangle ClipRectangle
		{
			get;
		}

		public Graphics Graphics
		{
			get;
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
		}
	}

	public partial class MouseEventArgs : EventArgs
	{
		public MouseEventArgs(MouseButtons button, int clicks, int x, int y, int delta)
		{
			Button = button;
			Clicks = clicks;
			X = x;
			Y = y;
			Delta = delta;
		}

		public MouseButtons Button
		{
			get;
		}

		public int Clicks
		{
			get;
		}

		public int Delta
		{
			get;
		}

		public Point Location => new Point(X, Y);

		public int X
		{
			get;
		}

		public int Y
		{
			get;
		}
	}

	public partial class KeyEventArgs : EventArgs
	{
		private readonly Keys keyData;
		private bool suppressKeyPress;

		public KeyEventArgs(Keys keyData)
		{
			this.keyData = keyData;
		}

		public virtual bool Alt => (keyData & Keys.Alt) == Keys.Alt;

		public bool Control => (keyData & Keys.Control) == Keys.Control;

		public bool Handled
		{
			get;
			set;
		}

		public Keys KeyCode
		{
			get
			{
				Keys code = keyData & Keys.KeyCode;
				return Enum.IsDefined(typeof(Keys), (int)code) ? code : Keys.None;
			}
		}

		public Keys KeyData => keyData;

		public int KeyValue => (int)(keyData & Keys.KeyCode);

		public Keys Modifiers => keyData & Keys.Modifiers;

		public virtual bool Shift => (keyData & Keys.Shift) == Keys.Shift;

		public bool SuppressKeyPress
		{
			get => suppressKeyPress;
			set
			{
				suppressKeyPress = value;
				Handled = value;
			}
		}
	}

	public partial class KeyPressEventArgs : EventArgs
	{
		public KeyPressEventArgs(char keyChar)
		{
			KeyChar = keyChar;
		}

		public bool Handled
		{
			get;
			set;
		}

		public char KeyChar
		{
			get;
			set;
		}
	}

	public partial class PreviewKeyDownEventArgs : EventArgs
	{
		public PreviewKeyDownEventArgs(Keys keyData)
		{
			KeyData = keyData;
		}

		public bool Alt => (KeyData & Keys.Alt) == Keys.Alt;

		public bool Control => (KeyData & Keys.Control) == Keys.Control;

		public bool IsInputKey
		{
			get;
			set;
		}

		public Keys KeyCode
		{
			get
			{
				Keys code = KeyData & Keys.KeyCode;
				return Enum.IsDefined(typeof(Keys), (int)code) ? code : Keys.None;
			}
		}

		public Keys KeyData
		{
			get;
		}

		public int KeyValue => (int)(KeyData & Keys.KeyCode);

		public Keys Modifiers => KeyData & Keys.Modifiers;

		public bool Shift => (KeyData & Keys.Shift) == Keys.Shift;
	}

	public partial class HelpEventArgs : EventArgs
	{
		public HelpEventArgs(Point mousePos)
		{
			MousePos = mousePos;
		}

		public bool Handled
		{
			get;
			set;
		}

		public Point MousePos
		{
			get;
		}
	}

	public partial class UICuesEventArgs : EventArgs
	{
		public UICuesEventArgs(UICues uicues)
		{
			Changed = uicues;
		}

		public bool ChangeFocus => (Changed & UICues.ChangeFocus) != UICues.None;

		public bool ChangeKeyboard => (Changed & UICues.ChangeKeyboard) != UICues.None;

		public UICues Changed
		{
			get;
		}

		public bool ShowFocus => (Changed & UICues.ShowFocus) != UICues.None;

		public bool ShowKeyboard => (Changed & UICues.ShowKeyboard) != UICues.None;
	}

	public partial class FormClosingEventArgs : CancelEventArgs
	{
		public FormClosingEventArgs(CloseReason closeReason, bool cancel)
			: base(cancel)
		{
			CloseReason = closeReason;
		}

		public CloseReason CloseReason
		{
			get;
		}
	}

	public partial class FormClosedEventArgs : EventArgs
	{
		public FormClosedEventArgs(CloseReason closeReason)
		{
			CloseReason = closeReason;
		}

		public CloseReason CloseReason
		{
			get;
		}
	}
}
