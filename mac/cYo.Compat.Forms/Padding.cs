using System;

namespace System.Windows.Forms
{
	/// <summary>Same layout and semantics as System.Windows.Forms.Padding (used by RectangleExtensions).</summary>
	[Serializable]
	public struct Padding : IEquatable<Padding>
	{
		private bool all;

		private int top;

		private int left;

		private int right;

		private int bottom;

		public static readonly Padding Empty = default(Padding);

		public int All
		{
			get
			{
				if (!all)
				{
					return -1;
				}
				return top;
			}
			set
			{
				if (!all || top != value)
				{
					all = true;
					top = (left = (right = (bottom = value)));
				}
			}
		}

		public int Bottom
		{
			get
			{
				if (all)
				{
					return top;
				}
				return bottom;
			}
			set
			{
				if (all || bottom != value)
				{
					all = false;
					bottom = value;
				}
			}
		}

		public int Left
		{
			get
			{
				if (all)
				{
					return top;
				}
				return left;
			}
			set
			{
				if (all || left != value)
				{
					all = false;
					left = value;
				}
			}
		}

		public int Right
		{
			get
			{
				if (all)
				{
					return top;
				}
				return right;
			}
			set
			{
				if (all || right != value)
				{
					all = false;
					right = value;
				}
			}
		}

		public int Top
		{
			get
			{
				return top;
			}
			set
			{
				if (all || top != value)
				{
					all = false;
					top = value;
				}
			}
		}

		public int Horizontal => Left + Right;

		public int Vertical => Top + Bottom;

		public System.Drawing.Size Size => new System.Drawing.Size(Horizontal, Vertical);

		public Padding(int all)
		{
			this.all = true;
			top = (left = (right = (bottom = all)));
		}

		public Padding(int left, int top, int right, int bottom)
		{
			this.top = top;
			this.left = left;
			this.right = right;
			this.bottom = bottom;
			all = this.top == this.left && this.top == this.right && this.top == this.bottom;
		}

		public static Padding Add(Padding p1, Padding p2)
		{
			return new Padding(p1.Left + p2.Left, p1.Top + p2.Top, p1.Right + p2.Right, p1.Bottom + p2.Bottom);
		}

		public static Padding Subtract(Padding p1, Padding p2)
		{
			return new Padding(p1.Left - p2.Left, p1.Top - p2.Top, p1.Right - p2.Right, p1.Bottom - p2.Bottom);
		}

		public override bool Equals(object other)
		{
			return other is Padding padding && Equals(padding);
		}

		public bool Equals(Padding other)
		{
			return Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Left, Top, Right, Bottom);
		}

		public override string ToString()
		{
			return "{Left=" + Left + ",Top=" + Top + ",Right=" + Right + ",Bottom=" + Bottom + "}";
		}

		public static Padding operator +(Padding p1, Padding p2) => Add(p1, p2);

		public static Padding operator -(Padding p1, Padding p2) => Subtract(p1, p2);

		public static bool operator ==(Padding p1, Padding p2) => p1.Equals(p2);

		public static bool operator !=(Padding p1, Padding p2) => !p1.Equals(p2);
	}
}
