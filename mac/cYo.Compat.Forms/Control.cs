using System;
using System.ComponentModel;

namespace System.Windows.Forms
{
	public interface IWin32Window
	{
		IntPtr Handle
		{
			get;
		}
	}

	// Control, Form and the controls built on them are in Model/ (P4.9a). Control.Handle stays unavailable (P4.17.6 decides what a script may do with a window handle).
	public partial class Control
	{
		/// <summary>There are no native windows: a script that asks for the Win32 handle gets this exception.</summary>
		public IntPtr Handle => throw new NotSupportedException("System.Windows.Forms.Control.Handle is not yet supported on macOS");
	}

	public partial class TreeView : Control
	{
	}

	/// <summary>Real type: sealed-in-practice node of a TreeView. Only the members that VariableHeightTreeNode uses.</summary>
	[Serializable]
	public partial class TreeNode
	{
		public IntPtr Handle => throw new NotSupportedException("System.Windows.Forms.TreeNode.Handle is not yet supported on macOS");

		public TreeView TreeView => throw new NotSupportedException("System.Windows.Forms.TreeNode.TreeView is not yet supported on macOS");
	}
}
