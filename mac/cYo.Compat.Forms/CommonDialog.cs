using System;
using System.ComponentModel;

namespace System.Windows.Forms
{
	public abstract partial class CommonDialog : Component
	{
		protected abstract bool RunDialog(IntPtr hwndOwner);

		public abstract void Reset();

		public DialogResult ShowDialog()
		{
			throw new NotSupportedException("System.Windows.Forms.CommonDialog.ShowDialog is not yet supported on macOS");
		}
	}
}
