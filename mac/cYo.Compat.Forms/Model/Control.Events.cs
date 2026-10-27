#pragma warning disable CS0067
using System;
using System.ComponentModel;
using System.Drawing;

// The events of System.Windows.Forms.Control and the OnXxx methods that raise them (P4.9a). A script that overrides OnXxx and calls the base method (Control.OnPaint(self, e))
// reaches these. The state changes that raise them are in Control.cs; the members with propagation to the children are there too.
namespace System.Windows.Forms
{
	public partial class Control
	{
		public event EventHandler AutoSizeChanged;

		public event EventHandler BackColorChanged;

		public event EventHandler BackgroundImageChanged;

		public event EventHandler BackgroundImageLayoutChanged;

		public event EventHandler BindingContextChanged;

		public event EventHandler CausesValidationChanged;

		public event UICuesEventHandler ChangeUICues;

		public event EventHandler Click;

		public event EventHandler ClientSizeChanged;

		public event EventHandler ContextMenuChanged;

		public event EventHandler ContextMenuStripChanged;

		public event ControlEventHandler ControlAdded;

		public event ControlEventHandler ControlRemoved;

		public event EventHandler CursorChanged;

		public event EventHandler DataContextChanged;

		public event EventHandler DockChanged;

		public event EventHandler DoubleClick;

		public event EventHandler DpiChangedAfterParent;

		public event EventHandler DpiChangedBeforeParent;

		public event DragEventHandler DragDrop;

		public event DragEventHandler DragEnter;

		public event EventHandler DragLeave;

		public event DragEventHandler DragOver;

		public event EventHandler EnabledChanged;

		public event EventHandler Enter;

		public event EventHandler FontChanged;

		public event EventHandler ForeColorChanged;

		public event GiveFeedbackEventHandler GiveFeedback;

		public event EventHandler GotFocus;

		public event EventHandler HandleCreated;

		public event EventHandler HandleDestroyed;

		public event HelpEventHandler HelpRequested;

		public event EventHandler ImeModeChanged;

		public event InvalidateEventHandler Invalidated;

		public event KeyEventHandler KeyDown;

		public event KeyPressEventHandler KeyPress;

		public event KeyEventHandler KeyUp;

		public event LayoutEventHandler Layout;

		public event EventHandler Leave;

		public event EventHandler LocationChanged;

		public event EventHandler LostFocus;

		public event EventHandler MarginChanged;

		public event EventHandler MouseCaptureChanged;

		public event MouseEventHandler MouseClick;

		public event MouseEventHandler MouseDoubleClick;

		public event MouseEventHandler MouseDown;

		public event EventHandler MouseEnter;

		public event EventHandler MouseHover;

		public event EventHandler MouseLeave;

		public event MouseEventHandler MouseMove;

		public event MouseEventHandler MouseUp;

		public event MouseEventHandler MouseWheel;

		public event EventHandler Move;

		public event EventHandler PaddingChanged;

		public event PaintEventHandler Paint;

		public event EventHandler ParentChanged;

		public event PreviewKeyDownEventHandler PreviewKeyDown;

		public event QueryAccessibilityHelpEventHandler QueryAccessibilityHelp;

		public event QueryContinueDragEventHandler QueryContinueDrag;

		public event EventHandler RegionChanged;

		public event EventHandler Resize;

		public event EventHandler RightToLeftChanged;

		public event EventHandler SizeChanged;

		public event EventHandler StyleChanged;

		public event EventHandler SystemColorsChanged;

		public event EventHandler TabIndexChanged;

		public event EventHandler TabStopChanged;

		public event EventHandler TextChanged;

		public event EventHandler Validated;

		public event CancelEventHandler Validating;

		public event EventHandler VisibleChanged;


		protected virtual void OnAutoSizeChanged(EventArgs e)
		{
			AutoSizeChanged?.Invoke(this, e);
		}

		protected virtual void OnBackgroundImageChanged(EventArgs e)
		{
			BackgroundImageChanged?.Invoke(this, e);
		}

		protected virtual void OnBackgroundImageLayoutChanged(EventArgs e)
		{
			BackgroundImageLayoutChanged?.Invoke(this, e);
		}

		protected virtual void OnBindingContextChanged(EventArgs e)
		{
			BindingContextChanged?.Invoke(this, e);
		}

		protected virtual void OnCausesValidationChanged(EventArgs e)
		{
			CausesValidationChanged?.Invoke(this, e);
		}

		protected virtual void OnChangeUICues(UICuesEventArgs e)
		{
			ChangeUICues?.Invoke(this, e);
		}

		protected virtual void OnClick(EventArgs e)
		{
			Click?.Invoke(this, e);
		}

		protected virtual void OnClientSizeChanged(EventArgs e)
		{
			ClientSizeChanged?.Invoke(this, e);
		}

		protected virtual void OnContextMenuChanged(EventArgs e)
		{
			ContextMenuChanged?.Invoke(this, e);
		}

		protected virtual void OnContextMenuStripChanged(EventArgs e)
		{
			ContextMenuStripChanged?.Invoke(this, e);
		}

		protected virtual void OnControlAdded(ControlEventArgs e)
		{
			ControlAdded?.Invoke(this, e);
		}

		protected virtual void OnControlRemoved(ControlEventArgs e)
		{
			ControlRemoved?.Invoke(this, e);
		}

		protected virtual void OnCreateControl()
		{
		}

		protected virtual void OnDataContextChanged(EventArgs e)
		{
			DataContextChanged?.Invoke(this, e);
		}

		protected virtual void OnDockChanged(EventArgs e)
		{
			DockChanged?.Invoke(this, e);
		}

		protected virtual void OnDoubleClick(EventArgs e)
		{
			DoubleClick?.Invoke(this, e);
		}

		protected virtual void OnDpiChangedAfterParent(EventArgs e)
		{
			DpiChangedAfterParent?.Invoke(this, e);
		}

		protected virtual void OnDpiChangedBeforeParent(EventArgs e)
		{
			DpiChangedBeforeParent?.Invoke(this, e);
		}

		protected virtual void OnDragDrop(DragEventArgs drgevent)
		{
			DragDrop?.Invoke(this, drgevent);
		}

		protected virtual void OnDragEnter(DragEventArgs drgevent)
		{
			DragEnter?.Invoke(this, drgevent);
		}

		protected virtual void OnDragLeave(EventArgs e)
		{
			DragLeave?.Invoke(this, e);
		}

		protected virtual void OnDragOver(DragEventArgs drgevent)
		{
			DragOver?.Invoke(this, drgevent);
		}

		protected internal virtual void OnEnter(EventArgs e)
		{
			Enter?.Invoke(this, e);
		}

		protected virtual void OnGiveFeedback(GiveFeedbackEventArgs gfbevent)
		{
			GiveFeedback?.Invoke(this, gfbevent);
		}

		protected virtual void OnGotFocus(EventArgs e)
		{
			GotFocus?.Invoke(this, e);
		}

		protected virtual void OnHandleCreated(EventArgs e)
		{
			HandleCreated?.Invoke(this, e);
		}

		protected virtual void OnHandleDestroyed(EventArgs e)
		{
			HandleDestroyed?.Invoke(this, e);
		}

		protected virtual void OnHelpRequested(HelpEventArgs hevent)
		{
			HelpRequested?.Invoke(this, hevent);
		}

		protected virtual void OnImeModeChanged(EventArgs e)
		{
			ImeModeChanged?.Invoke(this, e);
		}

		protected virtual void OnInvalidated(InvalidateEventArgs e)
		{
			Invalidated?.Invoke(this, e);
		}

		protected virtual void OnKeyDown(KeyEventArgs e)
		{
			KeyDown?.Invoke(this, e);
		}

		protected virtual void OnKeyPress(KeyPressEventArgs e)
		{
			KeyPress?.Invoke(this, e);
		}

		protected virtual void OnKeyUp(KeyEventArgs e)
		{
			KeyUp?.Invoke(this, e);
		}

		protected internal virtual void OnLeave(EventArgs e)
		{
			Leave?.Invoke(this, e);
		}

		protected virtual void OnLostFocus(EventArgs e)
		{
			LostFocus?.Invoke(this, e);
		}

		protected virtual void OnMarginChanged(EventArgs e)
		{
			MarginChanged?.Invoke(this, e);
		}

		protected virtual void OnMouseCaptureChanged(EventArgs e)
		{
			MouseCaptureChanged?.Invoke(this, e);
		}

		protected virtual void OnMouseClick(MouseEventArgs e)
		{
			MouseClick?.Invoke(this, e);
		}

		protected virtual void OnMouseDoubleClick(MouseEventArgs e)
		{
			MouseDoubleClick?.Invoke(this, e);
		}

		protected virtual void OnMouseDown(MouseEventArgs e)
		{
			MouseDown?.Invoke(this, e);
		}

		protected virtual void OnMouseEnter(EventArgs e)
		{
			MouseEnter?.Invoke(this, e);
		}

		protected virtual void OnMouseHover(EventArgs e)
		{
			MouseHover?.Invoke(this, e);
		}

		protected virtual void OnMouseLeave(EventArgs e)
		{
			MouseLeave?.Invoke(this, e);
		}

		protected virtual void OnMouseMove(MouseEventArgs e)
		{
			MouseMove?.Invoke(this, e);
		}

		protected virtual void OnMouseUp(MouseEventArgs e)
		{
			MouseUp?.Invoke(this, e);
		}

		protected virtual void OnMouseWheel(MouseEventArgs e)
		{
			MouseWheel?.Invoke(this, e);
		}

		protected virtual void OnMove(EventArgs e)
		{
			Move?.Invoke(this, e);
		}

		protected virtual void OnNotifyMessage(Message m)
		{
		}

		protected virtual void OnPaddingChanged(EventArgs e)
		{
			PaddingChanged?.Invoke(this, e);
		}

		protected virtual void OnPaint(PaintEventArgs e)
		{
			Paint?.Invoke(this, e);
		}

		protected virtual void OnPaintBackground(PaintEventArgs pevent)
		{
		}

		protected virtual void OnParentBackgroundImageChanged(EventArgs e)
		{
		}

		protected virtual void OnParentBindingContextChanged(EventArgs e)
		{
		}

		protected virtual void OnParentChanged(EventArgs e)
		{
			ParentChanged?.Invoke(this, e);
		}

		protected virtual void OnParentDataContextChanged(EventArgs e)
		{
		}

		protected virtual void OnPreviewKeyDown(PreviewKeyDownEventArgs e)
		{
			PreviewKeyDown?.Invoke(this, e);
		}

		protected virtual void OnPrint(PaintEventArgs e)
		{
		}

		protected virtual void OnQueryContinueDrag(QueryContinueDragEventArgs qcdevent)
		{
			QueryContinueDrag?.Invoke(this, qcdevent);
		}

		protected virtual void OnRegionChanged(EventArgs e)
		{
			RegionChanged?.Invoke(this, e);
		}

		protected virtual void OnStyleChanged(EventArgs e)
		{
			StyleChanged?.Invoke(this, e);
		}

		protected virtual void OnSystemColorsChanged(EventArgs e)
		{
			SystemColorsChanged?.Invoke(this, e);
		}

		protected virtual void OnTabIndexChanged(EventArgs e)
		{
			TabIndexChanged?.Invoke(this, e);
		}

		protected virtual void OnTabStopChanged(EventArgs e)
		{
			TabStopChanged?.Invoke(this, e);
		}

		protected virtual void OnTextChanged(EventArgs e)
		{
			TextChanged?.Invoke(this, e);
		}

		protected virtual void OnValidated(EventArgs e)
		{
			Validated?.Invoke(this, e);
		}

		protected virtual void OnValidating(CancelEventArgs e)
		{
			Validating?.Invoke(this, e);
		}

	}
}
