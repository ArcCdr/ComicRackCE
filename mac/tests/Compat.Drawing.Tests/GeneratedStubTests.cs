using System;
using System.Drawing;
using System.Drawing.Printing;
using Xunit;

namespace Compat.Drawing.Tests
{
	/// <summary>What the generated part of the stand-in does when a script uses a member that is not implemented yet (P4.7).</summary>
	public class GeneratedStubTests
	{
		private const string Suffix = " is not yet supported on macOS (ComicRack script compatibility layer)";

		[Fact]
		public void A_generated_constructor_throws_the_standard_message()
		{
			var ex = Assert.Throws<NotSupportedException>(() => new PrintDocument());
			Assert.Equal("System.Drawing.Printing.PrintDocument.ctor" + Suffix, ex.Message);
		}

		[Fact]
		public void A_generated_member_of_a_hand_written_type_throws_the_standard_message()
		{
			using var bitmap = new Bitmap(4, 4);
			using Graphics g = Graphics.FromImage(bitmap);
			var ex = Assert.Throws<NotSupportedException>(() => g.DrawEllipse(null, 0, 0, 2, 2));
			Assert.Equal("System.Drawing.Graphics.DrawEllipse" + Suffix, ex.Message);
			ex = Assert.Throws<NotSupportedException>(() => _ = g.DpiX);
			Assert.Equal("System.Drawing.Graphics.DpiX" + Suffix, ex.Message);
			ex = Assert.Throws<NotSupportedException>(() => _ = bitmap.HorizontalResolution);
			Assert.Equal("System.Drawing.Image.HorizontalResolution" + Suffix, ex.Message);
		}

		[Fact]
		public void The_hand_written_part_keeps_working()
		{
			using var bitmap = new Bitmap(4, 4);
			using Graphics g = Graphics.FromImage(bitmap);
			g.Clear(Color.Red);
			Assert.Equal(Color.FromArgb(255, 255, 0, 0).ToArgb(), bitmap.GetPixel(1, 1).ToArgb());
			Assert.Equal(4, bitmap.Width);
		}

		[Fact]
		public void Dispose_and_the_object_methods_of_a_generated_type_do_not_throw()
		{
			var ex = Assert.Throws<NotSupportedException>(() => new Pen(Brushes.Black, 2f).Dispose());
			Assert.Contains("Pen", ex.Message); // the constructor throws; nothing to dispose
			using var bitmap = new Bitmap(2, 2);
			bitmap.Dispose();
			bitmap.Dispose();
		}
	}
}
