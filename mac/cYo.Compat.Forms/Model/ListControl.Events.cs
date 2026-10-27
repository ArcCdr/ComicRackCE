using System;
using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms
{
	public partial class ListControl
	{
		public event EventHandler DataSourceChanged;

		public event EventHandler DisplayMemberChanged;

		public event ListControlConvertEventHandler Format;

		public event EventHandler FormatInfoChanged;

		public event EventHandler FormatStringChanged;

		public event EventHandler FormattingEnabledChanged;

		public event EventHandler SelectedValueChanged;

		public event EventHandler ValueMemberChanged;

		protected virtual void OnDataSourceChanged(EventArgs e)
		{
			DataSourceChanged?.Invoke(this, e);
		}

		protected virtual void OnDisplayMemberChanged(EventArgs e)
		{
			DisplayMemberChanged?.Invoke(this, e);
		}

		protected virtual void OnFormat(ListControlConvertEventArgs e)
		{
			Format?.Invoke(this, e);
		}

		protected virtual void OnFormatInfoChanged(EventArgs e)
		{
			FormatInfoChanged?.Invoke(this, e);
		}

		protected virtual void OnFormatStringChanged(EventArgs e)
		{
			FormatStringChanged?.Invoke(this, e);
		}

		protected virtual void OnFormattingEnabledChanged(EventArgs e)
		{
			FormattingEnabledChanged?.Invoke(this, e);
		}

		protected virtual void OnSelectedValueChanged(EventArgs e)
		{
			SelectedValueChanged?.Invoke(this, e);
		}

		protected virtual void OnValueMemberChanged(EventArgs e)
		{
			ValueMemberChanged?.Invoke(this, e);
		}
	}
}
