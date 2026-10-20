// Pass B replacement for cYo.Common/Properties/Resources.Designer.cs (excluded from the pass B compile).
// The original reads Bitmap resources from a .resx of ResXFileRef values of the framework's System.Drawing.Bitmap, and the
// shadow project embeds no .resx. This stand-in embeds the image files themselves (Sources.props, logical names
// "cYo.Common.Resources.<file>") and decodes them through Image.FromStream. Every access returns a new Bitmap that the
// caller owns (the original shared one instance per access through the ResourceManager cache, but callers such as
// BitmapCursor dispose the result).
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;

namespace cYo.Common.Properties
{
	internal class Resources
	{
		private sealed class EmbeddedBitmapResourceManager : ResourceManager
		{
			public EmbeddedBitmapResourceManager()
				: base("cYo.Common.Properties.Resources", typeof(Resources).Assembly)
			{
			}

			public override object GetObject(string name)
			{
				return Load(name);
			}

			public override object GetObject(string name, CultureInfo culture)
			{
				return Load(name);
			}
		}

		private static readonly string[] Extensions = new string[3] { ".png", ".bmp", ".jpg" };

		private static ResourceManager resourceMan;

		internal static ResourceManager ResourceManager => resourceMan ?? (resourceMan = new EmbeddedBitmapResourceManager());

		internal static CultureInfo Culture
		{
			get;
			set;
		}

		internal static Bitmap Folder => Load("Folder");

		internal static Bitmap FolderHome => Load("FolderHome");

		internal static Bitmap PlusOverlay => Load("PlusOverlay");

		internal static Bitmap Wikipedia => Load("Wikipedia");

		/// <summary>Decodes the embedded bitmap with this resource name, or returns null if there is none.</summary>
		private static Bitmap Load(string name)
		{
			Assembly assembly = typeof(Resources).Assembly;
			foreach (string extension in Extensions)
			{
				using (Stream stream = assembly.GetManifestResourceStream("cYo.Common.Resources." + name + extension))
				{
					if (stream != null)
					{
						return (Bitmap)Image.FromStream(stream);
					}
				}
			}
			return null;
		}
	}
}
