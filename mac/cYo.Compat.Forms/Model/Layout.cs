// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// The default layout engine of System.Windows.Forms (P4.9b): a port of System.Windows.Forms.Layout.DefaultLayout of dotnet/winforms (MIT), reduced to what the facade has:
// the controls are the facade's Control objects (there is no IArrangedElement), the bounds the engine computes are collected first and applied in the order of the children
// (the cached bounds of the original), a control is changed with BoundsSpecified.None so that its "specified bounds" and its anchor distances stay what the script set.
// Docking follows the z-order of the children (the last child docks first), anchoring keeps the distances to the display rectangle of the container, a control that is
// AutoSize and not docked grows (or shrinks) in the direction of its anchors. What is left for later: the preferred size of a container (AutoSize panels and forms,
// measure-only layout) and RightToLeft mirroring.
using System;
using System.Drawing;

namespace System.Windows.Forms.Layout
{
	/// <summary>The layout engine of every container that does not name its own (FlowLayoutPanel and TableLayoutPanel do, P4.9c).</summary>
	public abstract partial class LayoutEngine
	{
		protected LayoutEngine()
		{
		}

		public virtual void InitLayout(object child, BoundsSpecified specified)
		{
		}

		public virtual bool Layout(object container, LayoutEventArgs layoutEventArgs) => false;
	}

	/// <summary>Dock and Anchor (the engine behind <see cref="Control.LayoutEngine"/> of a plain container).</summary>
	internal sealed class DefaultLayoutEngine : LayoutEngine
	{
		public static readonly DefaultLayoutEngine Instance = new DefaultLayoutEngine();

		private DefaultLayoutEngine()
		{
		}

		public override void InitLayout(object child, BoundsSpecified specified)
		{
			DefaultLayout.InitLayout((Control)child, specified);
		}

		public override bool Layout(object container, LayoutEventArgs layoutEventArgs)
		{
			return DefaultLayout.Layout((Control)container, layoutEventArgs);
		}
	}

	/// <summary>The distances of an anchored control to the edges of the display rectangle of its container (AnchorInfo of the original).</summary>
	internal sealed class AnchorInfo
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	internal static class DefaultLayout
	{
		public const AnchorStyles DefaultAnchor = AnchorStyles.Top | AnchorStyles.Left;

		private static bool IsAnchored(AnchorStyles anchor, AnchorStyles desired) => (anchor & desired) == desired;

		/// <summary>An anchored control needs the anchor engine when it is not docked and its anchor is not the default.</summary>
		internal static bool NeedsAnchorLayout(Control c) => c.DockValue == DockStyle.None && c.AnchorValue != DefaultAnchor;

		/// <summary>The control is told that it has a new parent (all bounds specified) or new bounds: an anchored control remembers its distances.</summary>
		internal static void InitLayout(Control child, BoundsSpecified specified)
		{
			if (specified != BoundsSpecified.None && child.ParentInternal != null)
			{
				if (NeedsAnchorLayout(child))
				{
					UpdateAnchorInfo(child);
				}
				else
				{
					child.AnchorInfoValue = null;
				}
			}
		}

		/// <summary>Takes the distances of the control to its container from its present bounds (UpdateAnchorInfo of the original).</summary>
		internal static void UpdateAnchorInfo(Control element)
		{
			Control container = element.ParentInternal;
			if (container == null)
			{
				element.AnchorInfoValue = null;
				return;
			}
			Rectangle display = container.DisplayRectangle;
			Rectangle bounds = element.Bounds;
			AnchorStyles anchor = element.AnchorValue;
			var info = new AnchorInfo
			{
				Left = bounds.Left - display.X,
				Top = bounds.Top - display.Y,
				Right = bounds.Right - display.X,
				Bottom = bounds.Bottom - display.Y,
			};
			int parentWidth = display.Width;
			int parentHeight = display.Height;
			if (IsAnchored(anchor, AnchorStyles.Right))
			{
				info.Right -= parentWidth;
				if (!IsAnchored(anchor, AnchorStyles.Left))
				{
					info.Left -= parentWidth;
				}
			}
			else if (!IsAnchored(anchor, AnchorStyles.Left))
			{
				// Neither side: the control keeps its distance to the middle.
				int center = parentWidth / 2;
				info.Right -= center;
				info.Left -= center;
			}
			if (IsAnchored(anchor, AnchorStyles.Bottom))
			{
				info.Bottom -= parentHeight;
				if (!IsAnchored(anchor, AnchorStyles.Top))
				{
					info.Top -= parentHeight;
				}
			}
			else if (!IsAnchored(anchor, AnchorStyles.Top))
			{
				int center = parentHeight / 2;
				info.Bottom -= center;
				info.Top -= center;
			}
			element.AnchorInfoValue = info;
		}

		/// <summary>Arranges the children of <paramref name="container"/>: docked controls first, then the anchored ones, then the ones that size themselves. Returns true when the container sizes itself (AutoSize).</summary>
		internal static bool Layout(Control container, LayoutEventArgs args)
		{
			if (container.IsDisposed)
			{
				return false;
			}
			Control[] children = container.Children();
			if (children.Length == 0)
			{
				return container.AutoSizeValue;
			}
			Rectangle display = container.DisplayRectangle;
			var rects = new Rectangle[children.Length];
			for (int i = 0; i < children.Length; i++)
			{
				rects[i] = children[i].Bounds;
			}
			LayoutDockedControls(children, rects, display);
			LayoutAnchorControls(children, rects, display);
			LayoutAutoSizedControls(children, rects);
			for (int i = 0; i < children.Length; i++)
			{
				if (rects[i] != children[i].Bounds)
				{
					children[i].SetBoundsByLayout(rects[i]);
				}
			}
			return container.AutoSizeValue;
		}

		private static bool Participates(Control c) => c.VisibleFlag && !c.IsDisposed;

		/// <summary>Minimum and maximum size of the control apply; a negative size is none.</summary>
		private static Rectangle Constrain(Control c, Rectangle r)
		{
			var size = new Size(Math.Max(0, r.Width), Math.Max(0, r.Height));
			c.ApplySizeConstraints(ref size);
			return new Rectangle(r.X, r.Y, size.Width, size.Height);
		}

		/// <summary>The size an AutoSize control asks for within <paramref name="constraints"/>; the facade returns an empty preferred size for a control that cannot measure itself (before P4.9d), which counts as "no opinion".</summary>
		private static bool TryPreferredSize(Control c, Size constraints, out Size size)
		{
			size = c.GetPreferredSize(constraints);
			return size != Size.Empty;
		}

		private static Size GetDockedSize(Control element, Size constraints)
		{
			if (element.AutoSizeValue && TryPreferredSize(element, constraints, out Size preferred))
			{
				return preferred;
			}
			return element.Size;
		}

		private static void LayoutDockedControls(Control[] children, Rectangle[] rects, Rectangle display)
		{
			Rectangle remaining = display;
			// Docking is order dependent; the z-order is the docking order: the last child docks first.
			for (int i = children.Length - 1; i >= 0; i--)
			{
				Control element = children[i];
				if (!Participates(element) || element.DockValue == DockStyle.None)
				{
					continue;
				}
				Rectangle bounds;
				switch (element.DockValue)
				{
				case DockStyle.Top:
				{
					Size size = GetDockedSize(element, new Size(remaining.Width, 1));
					bounds = Constrain(element, new Rectangle(remaining.X, remaining.Y, remaining.Width, size.Height));
					remaining.Y += bounds.Height;
					remaining.Height -= bounds.Height;
					break;
				}
				case DockStyle.Bottom:
				{
					Size size = GetDockedSize(element, new Size(remaining.Width, 1));
					bounds = Constrain(element, new Rectangle(remaining.X, remaining.Bottom - size.Height, remaining.Width, size.Height));
					remaining.Height -= bounds.Height;
					break;
				}
				case DockStyle.Left:
				{
					Size size = GetDockedSize(element, new Size(1, remaining.Height));
					bounds = Constrain(element, new Rectangle(remaining.X, remaining.Y, size.Width, remaining.Height));
					remaining.X += bounds.Width;
					remaining.Width -= bounds.Width;
					break;
				}
				case DockStyle.Right:
				{
					Size size = GetDockedSize(element, new Size(1, remaining.Height));
					bounds = Constrain(element, new Rectangle(remaining.Right - size.Width, remaining.Y, size.Width, remaining.Height));
					remaining.Width -= bounds.Width;
					break;
				}
				default:
					bounds = Constrain(element, remaining);
					break;
				}
				rects[i] = bounds;
			}
		}

		private static void LayoutAnchorControls(Control[] children, Rectangle[] rects, Rectangle display)
		{
			for (int i = children.Length - 1; i >= 0; i--)
			{
				Control element = children[i];
				if (!Participates(element) || !NeedsAnchorLayout(element))
				{
					continue;
				}
				AnchorInfo info = element.AnchorInfoValue;
				if (info == null)
				{
					UpdateAnchorInfo(element);
					info = element.AnchorInfoValue;
					if (info == null)
					{
						continue;
					}
				}
				rects[i] = Constrain(element, GetAnchorDestination(element.AnchorValue, info, display));
			}
		}

		private static Rectangle GetAnchorDestination(AnchorStyles anchor, AnchorInfo info, Rectangle display)
		{
			int left = info.Left + display.X;
			int top = info.Top + display.Y;
			int right = info.Right + display.X;
			int bottom = info.Bottom + display.Y;
			if (IsAnchored(anchor, AnchorStyles.Right))
			{
				right += display.Width;
				if (!IsAnchored(anchor, AnchorStyles.Left))
				{
					left += display.Width;
				}
			}
			else if (!IsAnchored(anchor, AnchorStyles.Left))
			{
				int center = display.Width / 2;
				right += center;
				left += center;
			}
			if (IsAnchored(anchor, AnchorStyles.Bottom))
			{
				bottom += display.Height;
				if (!IsAnchored(anchor, AnchorStyles.Top))
				{
					top += display.Height;
				}
			}
			else if (!IsAnchored(anchor, AnchorStyles.Top))
			{
				int center = display.Height / 2;
				bottom += center;
				top += center;
			}
			return new Rectangle(left, top, right - left, bottom - top);
		}

		private static void LayoutAutoSizedControls(Control[] children, Rectangle[] rects)
		{
			for (int i = children.Length - 1; i >= 0; i--)
			{
				Control element = children[i];
				if (!Participates(element) || element.DockValue != DockStyle.None || !element.AutoSizeValue)
				{
					continue;
				}
				AnchorStyles anchor = element.AnchorValue;
				Rectangle bounds = rects[i];
				// A stretched axis is a constraint for the control (a label wraps its text at the width the anchors give it).
				var hint = new Size(
					IsAnchored(anchor, AnchorStyles.Left | AnchorStyles.Right) ? bounds.Width : 0,
					IsAnchored(anchor, AnchorStyles.Top | AnchorStyles.Bottom) ? bounds.Height : 0);
				if (!TryPreferredSize(element, hint, out Size preferred))
				{
					continue;
				}
				Rectangle result = bounds;
				if (element.AutoSizeModeValue == AutoSizeMode.GrowAndShrink)
				{
					result = GetGrowthBounds(anchor, bounds, preferred);
				}
				else if (bounds.Width < preferred.Width || bounds.Height < preferred.Height)
				{
					result = GetGrowthBounds(anchor, bounds, new Size(Math.Max(bounds.Width, preferred.Width), Math.Max(bounds.Height, preferred.Height)));
				}
				rects[i] = Constrain(element, result);
			}
		}

		/// <summary>The control grows towards the side that is not anchored: a control anchored on the right (and not on the left) grows to the left, one anchored at the bottom (and not at the top) grows upwards.</summary>
		private static Rectangle GetGrowthBounds(AnchorStyles anchor, Rectangle old, Size newSize)
		{
			int x = old.X;
			int y = old.Y;
			if (IsAnchored(anchor, AnchorStyles.Right) && !IsAnchored(anchor, AnchorStyles.Left))
			{
				x -= newSize.Width - old.Width;
			}
			if (IsAnchored(anchor, AnchorStyles.Bottom) && !IsAnchored(anchor, AnchorStyles.Top))
			{
				y -= newSize.Height - old.Height;
			}
			return new Rectangle(x, y, newSize.Width, newSize.Height);
		}
	}
}
