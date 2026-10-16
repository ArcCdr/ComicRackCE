using System;
using System.Drawing;
using System.Windows.Forms;

namespace cYo.Projects.ComicRack.Engine.Display
{
	// Replacement: a copy of ComicRack.Engine/Display/GestureEventArgs.cs (excluded together with ComicDisplay.cs)
	// because IComicDisplay exposes it in events that scripts subscribe to.
	public class GestureEventArgs : EventArgs
	{
		private readonly GestureType gesture;

		public GestureType Gesture => gesture;

		public ContentAlignment Area
		{
			get;
			set;
		}

		public Rectangle AreaBounds
		{
			get;
			set;
		}

		public Point Location
		{
			get;
			set;
		}

		public MouseButtons MouseButton
		{
			get;
			set;
		}

		public bool Double
		{
			get;
			set;
		}

		public bool Handled
		{
			get;
			set;
		}

		public bool IsTouch
		{
			get;
			set;
		}

		public GestureEventArgs(GestureType gesture)
		{
			this.gesture = gesture;
		}
	}
}
