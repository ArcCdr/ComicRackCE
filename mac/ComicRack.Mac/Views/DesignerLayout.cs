using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ComicRack.Mac.Views
{
	/// <summary>The edges of its parent a control keeps its distance to when the parent is resized (WinForms <c>AnchorStyles</c>).</summary>
	[Flags]
	public enum Anchor
	{
		None = 0,
		Top = 1,
		Bottom = 2,
		Left = 4,
		Right = 8,
		TopLeft = Top | Left,
		TopRight = Top | Right,
		BottomLeft = Bottom | Left,
		BottomRight = Bottom | Right,
		TopLeftRight = Top | Left | Right,
		BottomLeftRight = Bottom | Left | Right,
		All = Top | Bottom | Left | Right,
	}

	/// <summary>
	/// Puts a control where the Designer file of the original puts it: its position and size in the parent's designed size, and the anchors, which become the alignment and the
	/// margins that keep the distances to the anchored edges. The parent is a plain <see cref="Panel"/>, whose children all fill it, so a margin is a position.
	/// </summary>
	public static class DesignerLayout
	{
		public static T Place<T>(Panel parent, T control, double x, double y, double width, double height, Anchor anchor, double parentWidth, double parentHeight) where T : Control
		{
			double right = parentWidth - (x + width);
			double bottom = parentHeight - (y + height);
			bool left = (anchor & Anchor.Left) != 0, rightAnchored = (anchor & Anchor.Right) != 0;
			bool top = (anchor & Anchor.Top) != 0, bottomAnchored = (anchor & Anchor.Bottom) != 0;
			double marginLeft = 0, marginRight = 0, marginTop = 0, marginBottom = 0;
			if (left && rightAnchored)
			{
				control.HorizontalAlignment = HorizontalAlignment.Stretch;
				marginLeft = x;
				marginRight = right;
			}
			else if (rightAnchored)
			{
				control.HorizontalAlignment = HorizontalAlignment.Right;
				marginRight = right;
				control.Width = width;
			}
			else
			{
				control.HorizontalAlignment = HorizontalAlignment.Left;
				marginLeft = x;
				control.Width = width;
			}
			if (top && bottomAnchored)
			{
				control.VerticalAlignment = VerticalAlignment.Stretch;
				marginTop = y;
				marginBottom = bottom;
			}
			else if (bottomAnchored)
			{
				control.VerticalAlignment = VerticalAlignment.Bottom;
				marginBottom = bottom;
				control.Height = height;
			}
			else
			{
				control.VerticalAlignment = VerticalAlignment.Top;
				marginTop = y;
				control.Height = height;
			}
			control.Margin = new Thickness(marginLeft, marginTop, marginRight, marginBottom);
			parent.Children.Add(control);
			return control;
		}
	}
}
