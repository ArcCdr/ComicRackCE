using System;
using System.Windows.Forms;
using Xunit;

namespace Compat.Forms.Tests
{
	/// <summary>What the generated part of the facade does when a script uses a member that is not implemented yet (P4.7).</summary>
	public class GeneratedStubTests
	{
		private const string Suffix = " is not yet supported on macOS (ComicRack script compatibility layer)";

		[Fact]
		public void A_generated_constructor_throws_the_standard_message()
		{
			var ex = Assert.Throws<NotSupportedException>(() => new ListView());
			Assert.Equal("System.Windows.Forms.ListView.ctor" + Suffix, ex.Message);
			ex = Assert.Throws<NotSupportedException>(() => new OpenFileDialog());
			Assert.Equal("System.Windows.Forms.OpenFileDialog.ctor" + Suffix, ex.Message);
		}

		[Fact]
		public void A_generated_member_of_a_hand_written_type_throws_the_standard_message()
		{
			var control = new Control();
			Assert.True(control.Visible, "the hand-written part keeps working");
			Assert.Equal(string.Empty, control.Text);
			var ex = Assert.Throws<NotSupportedException>(() => _ = control.AccessibilityObject);
			Assert.Equal("System.Windows.Forms.Control.AccessibilityObject" + Suffix, ex.Message);
			ex = Assert.Throws<NotSupportedException>(() => control.Scale(1.5f));
			Assert.Equal("System.Windows.Forms.Control.Scale" + Suffix, ex.Message);
			ex = Assert.Throws<NotSupportedException>(() => control.DoDragDrop(null, DragDropEffects.None));
			Assert.Equal("System.Windows.Forms.Control.DoDragDrop" + Suffix, ex.Message);
			ex = Assert.Throws<NotSupportedException>(() => control.CreateGraphics());
			Assert.Equal("System.Windows.Forms.Control.CreateGraphics" + Suffix, ex.Message);
		}

		[Fact]
		public void A_static_member_and_a_nested_type_name_their_declaring_type()
		{
			var ex = Assert.Throws<NotSupportedException>(() => MessageBox.Show("x"));
			Assert.Equal("System.Windows.Forms.MessageBox.Show" + Suffix, ex.Message);
			ex = Assert.Throws<NotSupportedException>(() => _ = Control.IsKeyLocked(Keys.CapsLock));
			Assert.Equal("System.Windows.Forms.Control.IsKeyLocked" + Suffix, ex.Message);
		}

		[Fact]
		public void Dispose_and_the_object_methods_never_throw()
		{
			var control = new Control();
			control.Dispose();
			Assert.True(control.IsDisposed);
			Assert.NotNull(control.ToString());
			_ = control.GetHashCode();
			Assert.False(control.Equals(new Control()));
			Assert.True(control.Equals(control));
		}

		[Fact]
		public void The_equality_operators_of_generated_types_compare_instead_of_throwing()
		{
			Assert.True(default(LinkArea) == default(LinkArea));
			Assert.False(default(LinkArea) != default(LinkArea));
			Assert.False(default(LinkArea).Equals(null));
			HtmlWindow a = null, b = null;
			Assert.True(a == b);
			Assert.False(a != b);
		}

		[Fact]
		public void Objects_whose_constructor_threw_are_finalized_without_crashing_the_process()
		{
			// A Component has a finalizer that calls the virtual Dispose(false). A generated Dispose that threw would end the process on the finalizer thread.
			for (int i = 0; i < 200; i++)
			{
				Assert.Throws<NotSupportedException>(() => new OpenFileDialog());
				Assert.Throws<NotSupportedException>(() => new ToolStripMenuItem());
				Assert.Throws<NotSupportedException>(() => new NotifyIcon());
			}
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}

		[Fact]
		public void Enums_and_the_flags_of_the_surface_are_real()
		{
			Assert.Equal(1, (int)DockStyle.Top);
			Assert.Equal(AnchorStyles.Top | AnchorStyles.Left, (AnchorStyles)5);
			Assert.True(typeof(AnchorStyles).IsDefined(typeof(FlagsAttribute), false));
			Assert.Equal(FormBorderStyle.Sizable, (FormBorderStyle)4);
			Assert.Equal(DialogResult.OK, (DialogResult)1);
		}
	}
}
