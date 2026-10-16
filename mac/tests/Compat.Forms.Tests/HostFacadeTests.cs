using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Xunit;

namespace Compat.Forms.Tests
{
	public class HostFacadeTests
	{
		private static string FindWindowsAssemblyInfo()
		{
			for (string dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
			{
				string candidate = Path.Combine(dir, "ComicRack", "Properties", "AssemblyInfo.cs");
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}
			throw new FileNotFoundException("ComicRack/Properties/AssemblyInfo.cs not found above " + AppContext.BaseDirectory);
		}

		private static string Attribute(string text, string name)
		{
			Match m = Regex.Match(text, @"\[assembly: " + name + @"\(""([^""]*)""\)\]");
			Assert.True(m.Success, name + " not found");
			return m.Groups[1].Value;
		}

		[Fact]
		public void Application_strings_are_the_ones_of_the_windows_app()
		{
			string text = File.ReadAllText(FindWindowsAssemblyInfo());
			Assert.Equal(Attribute(text, "AssemblyCompany"), Application.CompanyName);
			Assert.Equal(Attribute(text, "AssemblyProduct"), Application.ProductName);
			Assert.Equal(Attribute(text, "AssemblyInformationalVersion"), Application.ProductVersion);
		}

		[Fact]
		public void Application_paths_come_from_the_process()
		{
			Assert.True(Directory.Exists(Application.StartupPath));
			Assert.False(Application.StartupPath.EndsWith(Path.DirectorySeparatorChar.ToString()));
			Assert.True(File.Exists(Application.ExecutablePath));
		}

		[Fact]
		public void Application_RaiseIdle_reaches_Idle_handlers()
		{
			int count = 0;
			EventHandler handler = (s, e) => count++;
			Application.Idle += handler;
			try
			{
				Application.RaiseIdle(EventArgs.Empty);
			}
			finally
			{
				Application.Idle -= handler;
			}
			Assert.Equal(1, count);
		}

		[Fact]
		public void Members_without_a_macOS_implementation_throw_a_descriptive_exception()
		{
			var ex = Assert.Throws<NotSupportedException>(() => Clipboard.SetDataObject(new DataObject()));
			Assert.Equal("System.Windows.Forms.Clipboard.SetDataObject is not yet supported on macOS", ex.Message);
			ex = Assert.Throws<NotSupportedException>(() => _ = Screen.PrimaryScreen);
			Assert.Equal("System.Windows.Forms.Screen.PrimaryScreen is not yet supported on macOS", ex.Message);
			ex = Assert.Throws<NotSupportedException>(() => _ = new Control().Handle);
			Assert.Equal("System.Windows.Forms.Control.Handle is not yet supported on macOS", ex.Message);
		}

		[Fact]
		public void Padding_behaves_like_the_winforms_struct()
		{
			var all = new Padding(3);
			Assert.Equal(3, all.All);
			Assert.Equal(6, all.Horizontal);
			Assert.Equal(6, all.Vertical);
			var p = new Padding(1, 2, 3, 4);
			Assert.Equal(-1, p.All);
			Assert.Equal((1, 2, 3, 4), (p.Left, p.Top, p.Right, p.Bottom));
			Assert.Equal(4, p.Horizontal);
			Assert.Equal(6, p.Vertical);
			Assert.Equal(new System.Drawing.Size(4, 6), p.Size);
			Assert.Equal(new Padding(2, 4, 6, 8), p + p);
			Assert.Equal(Padding.Empty, p - p);
			Assert.True(new Padding(5) == new Padding(5, 5, 5, 5));
			Assert.Equal("{Left=1,Top=2,Right=3,Bottom=4}", p.ToString());
		}

		[Fact]
		public void DataObject_keeps_formats_in_memory()
		{
			var data = new DataObject();
			data.SetData(DataFormats.UnicodeText, "x");
			Assert.Equal("x", data.GetData(DataFormats.UnicodeText, autoConvert: true));
			Assert.Null(data.GetData("Other", autoConvert: false));
			Assert.Equal(13, DataFormats.GetFormat(DataFormats.UnicodeText).Id);
			Assert.Equal(DataFormats.GetFormat("ComicBook").Id, DataFormats.GetFormat("ComicBook").Id);
		}

		[Fact]
		public void Cursor_compares_by_handle()
		{
			var a = new Cursor(new IntPtr(5));
			Assert.True(a == new Cursor(new IntPtr(5)));
			Assert.True(a != new Cursor(new IntPtr(6)));
			Assert.True(a != null);
			Assert.True((Cursor)null == null);
		}
	}
}
