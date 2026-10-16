using System.Drawing;

namespace cYo.Common.Presentation
{
	// Replacement for cYo.Common.Presentation/RendererImage.cs. M1.3 links the real file.
	public abstract class RendererImage
	{
		private sealed class StandInBitmapImage : RendererImage
		{
			private readonly Bitmap bitmap;

			public override Bitmap Bitmap => bitmap;

			public StandInBitmapImage(Bitmap bitmap)
			{
				this.bitmap = bitmap;
			}
		}

		public abstract Bitmap Bitmap
		{
			get;
		}

		public virtual bool IsValid => Bitmap != null;

		public virtual Size Size => Bitmap.Size;

		public int Width => Size.Width;

		public int Height => Size.Height;

		public override bool Equals(object obj)
		{
			if (obj == null)
			{
				return false;
			}
			if (obj.GetType() != GetType())
			{
				return false;
			}
			return ((RendererImage)obj).Bitmap == Bitmap;
		}

		public override int GetHashCode()
		{
			return Bitmap.GetHashCode();
		}

		public static implicit operator Bitmap(RendererImage image)
		{
			return image.Bitmap;
		}

		public static implicit operator RendererImage(Bitmap image)
		{
			return new StandInBitmapImage(image);
		}
	}
}
