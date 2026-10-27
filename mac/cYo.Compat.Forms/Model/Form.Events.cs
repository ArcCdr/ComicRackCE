using System;
using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms
{
	public partial class Form
	{
		public event EventHandler Activated;

		public event EventHandler Closed;

		public event CancelEventHandler Closing;

		public event EventHandler Deactivate;

		public event DpiChangedEventHandler DpiChanged;

		public event EventHandler FormBorderColorChanged;

		public event EventHandler FormCaptionBackColorChanged;

		public event EventHandler FormCaptionTextColorChanged;

		public event FormClosedEventHandler FormClosed;

		public event FormClosingEventHandler FormClosing;

		public event EventHandler FormCornerPreferenceChanged;

		public event CancelEventHandler HelpButtonClicked;

		public event InputLanguageChangedEventHandler InputLanguageChanged;

		public event InputLanguageChangingEventHandler InputLanguageChanging;

		public event EventHandler Load;

		public event EventHandler MaximizedBoundsChanged;

		public event EventHandler MaximumSizeChanged;

		public event EventHandler MdiChildActivate;

		public event EventHandler MenuComplete;

		public event EventHandler MenuStart;

		public event EventHandler MinimumSizeChanged;

		public event EventHandler ResizeBegin;

		public event EventHandler ResizeEnd;

		public event EventHandler RightToLeftLayoutChanged;

		public event EventHandler Shown;

		protected virtual void OnDpiChanged(DpiChangedEventArgs e)
		{
			DpiChanged?.Invoke(this, e);
		}

		protected virtual void OnFormBorderColorChanged(EventArgs e)
		{
			FormBorderColorChanged?.Invoke(this, e);
		}

		protected virtual void OnFormCaptionBackColorChanged(EventArgs e)
		{
			FormCaptionBackColorChanged?.Invoke(this, e);
		}

		protected virtual void OnFormCaptionTextColorChanged(EventArgs e)
		{
			FormCaptionTextColorChanged?.Invoke(this, e);
		}

		protected virtual void OnFormCornerPreferenceChanged(EventArgs e)
		{
			FormCornerPreferenceChanged?.Invoke(this, e);
		}

		protected virtual void OnHelpButtonClicked(CancelEventArgs e)
		{
			HelpButtonClicked?.Invoke(this, e);
		}

		protected virtual void OnInputLanguageChanged(InputLanguageChangedEventArgs e)
		{
			InputLanguageChanged?.Invoke(this, e);
		}

		protected virtual void OnInputLanguageChanging(InputLanguageChangingEventArgs e)
		{
			InputLanguageChanging?.Invoke(this, e);
		}

		protected virtual void OnMaximizedBoundsChanged(EventArgs e)
		{
			MaximizedBoundsChanged?.Invoke(this, e);
		}

		protected virtual void OnMaximumSizeChanged(EventArgs e)
		{
			MaximumSizeChanged?.Invoke(this, e);
		}

		protected virtual void OnMdiChildActivate(EventArgs e)
		{
			MdiChildActivate?.Invoke(this, e);
		}

		protected virtual void OnMenuComplete(EventArgs e)
		{
			MenuComplete?.Invoke(this, e);
		}

		protected virtual void OnMenuStart(EventArgs e)
		{
			MenuStart?.Invoke(this, e);
		}

		protected virtual void OnMinimumSizeChanged(EventArgs e)
		{
			MinimumSizeChanged?.Invoke(this, e);
		}

		protected virtual void OnResizeBegin(EventArgs e)
		{
			ResizeBegin?.Invoke(this, e);
		}

		protected virtual void OnResizeEnd(EventArgs e)
		{
			ResizeEnd?.Invoke(this, e);
		}

		protected virtual void OnRightToLeftLayoutChanged(EventArgs e)
		{
			RightToLeftLayoutChanged?.Invoke(this, e);
		}
	}
}
