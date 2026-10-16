// M1.2a surface skeleton. Signatures follow the System.Drawing.Common reference assembly; members the Core uses, bodies filled in by M1.2b/M1.2c.
using System;

namespace System.Drawing
{
	public sealed class Graphics : MarshalByRefObject, IDisposable, IDeviceContext
	{
		internal Graphics()
		{
		}

		public void Clear(Color color) => throw new NotSupportedException("System.Drawing.Graphics.Clear is not yet supported on macOS");

		public Region Clip
		{
			set => throw new NotSupportedException("System.Drawing.Graphics.Clip is not yet supported on macOS");
		}

		public System.Drawing.Drawing2D.CompositingMode CompositingMode
		{
			set => throw new NotSupportedException("System.Drawing.Graphics.CompositingMode is not yet supported on macOS");
		}

		public System.Drawing.Drawing2D.CompositingQuality CompositingQuality
		{
			set => throw new NotSupportedException("System.Drawing.Graphics.CompositingQuality is not yet supported on macOS");
		}

		public void DrawIcon(Icon icon, Rectangle targetRect) => throw new NotSupportedException("System.Drawing.Graphics.DrawIcon is not yet supported on macOS");

		public void DrawImage(Image image, Rectangle destRect, Rectangle srcRect, GraphicsUnit srcUnit) => throw new NotSupportedException("System.Drawing.Graphics.DrawImage is not yet supported on macOS");

		public void DrawImage(Image image, int x, int y) => throw new NotSupportedException("System.Drawing.Graphics.DrawImage is not yet supported on macOS");

		public void DrawImage(Image image, RectangleF rect) => throw new NotSupportedException("System.Drawing.Graphics.DrawImage is not yet supported on macOS");

		public void DrawImage(Image image, int x, int y, int width, int height) => throw new NotSupportedException("System.Drawing.Graphics.DrawImage is not yet supported on macOS");

		public void DrawImage(Image image, Rectangle destRect, int srcX, int srcY, int srcWidth, int srcHeight, GraphicsUnit srcUnit, System.Drawing.Imaging.ImageAttributes imageAttr) => throw new NotSupportedException("System.Drawing.Graphics.DrawImage is not yet supported on macOS");

		public void DrawImage(Image image, Rectangle destRect, int srcX, int srcY, int srcWidth, int srcHeight, GraphicsUnit srcUnit) => throw new NotSupportedException("System.Drawing.Graphics.DrawImage is not yet supported on macOS");

		public void DrawImage(Image image, Rectangle rect) => throw new NotSupportedException("System.Drawing.Graphics.DrawImage is not yet supported on macOS");

		public void DrawString(string s, Font font, Brush brush, RectangleF layoutRectangle, StringFormat format) => throw new NotSupportedException("System.Drawing.Graphics.DrawString is not yet supported on macOS");

		public void DrawString(string s, Font font, Brush brush, float x, float y) => throw new NotSupportedException("System.Drawing.Graphics.DrawString is not yet supported on macOS");

		public void FillEllipse(Brush brush, int x, int y, int width, int height) => throw new NotSupportedException("System.Drawing.Graphics.FillEllipse is not yet supported on macOS");

		public void FillRectangle(Brush brush, Rectangle rect) => throw new NotSupportedException("System.Drawing.Graphics.FillRectangle is not yet supported on macOS");

		public void FillRectangle(Brush brush, int x, int y, int width, int height) => throw new NotSupportedException("System.Drawing.Graphics.FillRectangle is not yet supported on macOS");

		public static Graphics FromImage(Image image) => throw new NotSupportedException("System.Drawing.Graphics.FromImage is not yet supported on macOS");

		public IntPtr GetHdc() => throw new NotSupportedException("System.Drawing.Graphics.GetHdc is not yet supported on macOS");

		public System.Drawing.Drawing2D.InterpolationMode InterpolationMode
		{
			get => throw new NotSupportedException("System.Drawing.Graphics.InterpolationMode is not yet supported on macOS");
			set => throw new NotSupportedException("System.Drawing.Graphics.InterpolationMode is not yet supported on macOS");
		}

		public SizeF MeasureString(string text, Font font) => throw new NotSupportedException("System.Drawing.Graphics.MeasureString is not yet supported on macOS");

		public SizeF MeasureString(string text, Font font, SizeF layoutArea, StringFormat stringFormat) => throw new NotSupportedException("System.Drawing.Graphics.MeasureString is not yet supported on macOS");

		public GraphicsUnit PageUnit
		{
			set => throw new NotSupportedException("System.Drawing.Graphics.PageUnit is not yet supported on macOS");
		}

		public System.Drawing.Drawing2D.PixelOffsetMode PixelOffsetMode
		{
			set => throw new NotSupportedException("System.Drawing.Graphics.PixelOffsetMode is not yet supported on macOS");
		}

		public void ReleaseHdc() => throw new NotSupportedException("System.Drawing.Graphics.ReleaseHdc is not yet supported on macOS");

		public void Restore(System.Drawing.Drawing2D.GraphicsState gstate) => throw new NotSupportedException("System.Drawing.Graphics.Restore is not yet supported on macOS");

		public System.Drawing.Drawing2D.GraphicsState Save() => throw new NotSupportedException("System.Drawing.Graphics.Save is not yet supported on macOS");

		public void ScaleTransform(float sx, float sy) => throw new NotSupportedException("System.Drawing.Graphics.ScaleTransform is not yet supported on macOS");

		public void SetClip(Rectangle rect, System.Drawing.Drawing2D.CombineMode combineMode) => throw new NotSupportedException("System.Drawing.Graphics.SetClip is not yet supported on macOS");

		public void SetClip(RectangleF rect, System.Drawing.Drawing2D.CombineMode combineMode) => throw new NotSupportedException("System.Drawing.Graphics.SetClip is not yet supported on macOS");

		public System.Drawing.Drawing2D.SmoothingMode SmoothingMode
		{
			get => throw new NotSupportedException("System.Drawing.Graphics.SmoothingMode is not yet supported on macOS");
			set => throw new NotSupportedException("System.Drawing.Graphics.SmoothingMode is not yet supported on macOS");
		}

		public System.Drawing.Text.TextRenderingHint TextRenderingHint
		{
			get => throw new NotSupportedException("System.Drawing.Graphics.TextRenderingHint is not yet supported on macOS");
			set => throw new NotSupportedException("System.Drawing.Graphics.TextRenderingHint is not yet supported on macOS");
		}

		public void TransformPoints(System.Drawing.Drawing2D.CoordinateSpace destSpace, System.Drawing.Drawing2D.CoordinateSpace srcSpace, params Point[] pts) => throw new NotSupportedException("System.Drawing.Graphics.TransformPoints is not yet supported on macOS");

		public void TransformPoints(System.Drawing.Drawing2D.CoordinateSpace destSpace, System.Drawing.Drawing2D.CoordinateSpace srcSpace, params PointF[] pts) => throw new NotSupportedException("System.Drawing.Graphics.TransformPoints is not yet supported on macOS");

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
		}
	}
}
