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

	/// <summary>Types-only stand-in. There are no native windows, so Handle is not available.</summary>
	public class Control : Component, IWin32Window
	{
		private bool isDisposed;

		public IntPtr Handle => throw new NotSupportedException("System.Windows.Forms.Control.Handle is not yet supported on macOS");

		public bool IsDisposed => isDisposed;

		public bool Visible
		{
			get;
			set;
		} = true;

		protected override void Dispose(bool disposing)
		{
			isDisposed = true;
			base.Dispose(disposing);
		}
	}

	public class Form : Control
	{
		public FormWindowState WindowState
		{
			get;
			set;
		}

		public Form()
		{
			Visible = false;
		}
	}

	public class TreeView : Control
	{
	}

	/// <summary>Real type: sealed-in-practice node of a TreeView. Only the members that VariableHeightTreeNode uses.</summary>
	[Serializable]
	public class TreeNode
	{
		public IntPtr Handle => throw new NotSupportedException("System.Windows.Forms.TreeNode.Handle is not yet supported on macOS");

		public TreeView TreeView => throw new NotSupportedException("System.Windows.Forms.TreeNode.TreeView is not yet supported on macOS");
	}
}
