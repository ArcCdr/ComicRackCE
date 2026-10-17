// Pass B replacement for ComicRack.Engine/Properties/Resources.Designer.cs (excluded from the pass B compile).
// The original reads Bitmap resources from a .resx whose entries are ResXFileRef values of type
// "System.Drawing.Bitmap, System.Drawing"; they would deserialize to the framework's Bitmap, not to the
// cYo.Compat.Drawing one, and the shadow project embeds no .resx. This stand-in embeds the PNG/JPEG files themselves
// (Sources.props, logical names "ComicRack.Engine.Resources.<file>") and decodes them through Image.FromStream.
// Every access returns a new Bitmap that the caller owns (the original shared one instance, which callers such as
// CacheManager.RequestResourceThumbnail disposed). Certificate and Certificate2 are not declared: only the excluded
// IO/Network/ComicLibraryServer.cs uses them.
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;

namespace cYo.Projects.ComicRack.Engine.Properties
{
	internal class Resources
	{
		private sealed class EmbeddedBitmapResourceManager : ResourceManager
		{
			public EmbeddedBitmapResourceManager()
				: base("cYo.Projects.ComicRack.Engine.Properties.Resources", typeof(Resources).Assembly)
			{
			}

			// An unknown name gives null, as a ResourceManager over the original .resx does.
			public override object GetObject(string name)
			{
				return Load(name);
			}

			public override object GetObject(string name, CultureInfo culture)
			{
				return Load(name);
			}
		}

		private static readonly string[] Extensions = new string[2] { ".png", ".jpg" };

		private static ResourceManager resourceMan;

		internal static ResourceManager ResourceManager => resourceMan ?? (resourceMan = new EmbeddedBitmapResourceManager());

		internal static CultureInfo Culture
		{
			get;
			set;
		}

		internal static Bitmap ErrorPage => Load("ErrorPage");

		internal static Bitmap NewPages => Load("NewPages");

		internal static Bitmap NewPages1 => Load("NewPages1");

		internal static Bitmap NewPages2 => Load("NewPages2");

		internal static Bitmap NewPages3 => Load("NewPages3");

		internal static Bitmap NewPages4 => Load("NewPages4");

		internal static Bitmap NewPages5 => Load("NewPages5");

		internal static Bitmap NewPages5Plus => Load("NewPages5Plus");

		internal static Bitmap PageCurl => Load("PageCurl");

		internal static Bitmap PageCurlShadow => Load("PageCurlShadow");

		internal static Bitmap RedCross => Load("RedCross");

		internal static Bitmap ShadowMap => Load("ShadowMap");

		internal static Bitmap Unknown => Load("Unknown");

		/// <summary>Decodes the embedded bitmap with this resource name, or returns null if there is none.</summary>
		private static Bitmap Load(string name)
		{
			Assembly assembly = typeof(Resources).Assembly;
			foreach (string extension in Extensions)
			{
				using (Stream stream = assembly.GetManifestResourceStream("ComicRack.Engine.Resources." + name + extension))
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
