using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using cYo.Common.IO;
using cYo.Common.Localize;
using cYo.Common.Threading;
using ComicRack.Mac.Services;

namespace ComicRack.Mac
{
	/// <summary>
	/// Language bootstrap of the app (P2.6). The texts are the original's: <c>Languages/&lt;culture&gt;/*.xml</c> of ComicRack/Output, copied next to
	/// the entry assembly (<c>Contents/MacOS/Languages</c> in the bundle), read by the Core's <see cref="TR"/>. English has no folder: the Core
	/// returns the English text it was given when a key is missing, so English is "no translation".
	/// Order of choice: the language setting, else the first of the macOS preferred languages that the app can show (a translation in
	/// <c>Languages/</c>, or English, which is always available), else English.
	/// </summary>
	public static class Localization
	{
		/// <summary>The culture name used for English: there is no folder of that name, so every text falls back to the English default.</summary>
		public const string English = "en";

		/// <summary>The folder where <see cref="TRDictionary"/> looks by default: <c>Languages</c> next to the entry assembly.</summary>
		public static string DefaultLanguagesFolder()
		{
			string location = null;
			try
			{
				location = Assembly.GetEntryAssembly()?.Location;
			}
			catch (Exception)
			{
			}
			string directory = string.IsNullOrEmpty(location) ? AppContext.BaseDirectory : Path.GetDirectoryName(location);
			return Path.Combine(directory, "Languages");
		}

		/// <summary>The cultures that have texts: the sub folders of <paramref name="languagesFolder"/> with at least one XML file.</summary>
		public static IReadOnlyList<string> AvailableCultures(string languagesFolder)
		{
			try
			{
				if (!Directory.Exists(languagesFolder))
				{
					return Array.Empty<string>();
				}
				return Directory.GetDirectories(languagesFolder)
					.Where(d => Directory.EnumerateFiles(d, "*.xml").Any())
					.Select(Path.GetFileName)
					.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
					.ToList();
			}
			catch (Exception)
			{
				return Array.Empty<string>();
			}
		}

		/// <summary>
		/// The culture to use for one language tag (<c>fr-BE</c>, <c>zh_Hans_CN</c>, <c>pt</c>): a folder name, <see cref="English"/>, or null if the app has
		/// nothing for it. Tried in this order: the tag as it is (any <c>zh-Hans*</c> tag is <c>zh-CN</c>), the tag with subtags removed from the right (<c>zh-Hant-TW</c>, <c>zh-Hant</c>, <c>zh</c>),
		/// English for any <c>en</c> tag, then the first folder of the same language (<c>pt-PT</c> gives <c>pt-BR</c>, <c>nl-NL</c> gives <c>nl-BE</c>).
		/// </summary>
		public static string Match(string tag, IReadOnlyCollection<string> available)
		{
			if (string.IsNullOrWhiteSpace(tag))
			{
				return null;
			}
			string normal = tag.Trim().Replace('_', '-');
			string[] parts = normal.Split('-', StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 0)
			{
				return null;
			}
			// Simplified Chinese (zh-Hans, zh-Hans-CN, zh-Hans-SG ...) is the zh-CN translation: it is the complete one (zh-Hans has fewer files)
			if (parts.Length >= 2 && string.Equals(parts[0], "zh", StringComparison.OrdinalIgnoreCase) && string.Equals(parts[1], "Hans", StringComparison.OrdinalIgnoreCase))
			{
				string simplified = available.FirstOrDefault(a => string.Equals(a, "zh-CN", StringComparison.OrdinalIgnoreCase));
				if (simplified != null)
				{
					return simplified;
				}
			}
			for (int length = parts.Length; length >= 1; length--)
			{
				string candidate = string.Join("-", parts.Take(length));
				string folder = available.FirstOrDefault(a => string.Equals(a, candidate, StringComparison.OrdinalIgnoreCase));
				if (folder != null)
				{
					return folder;
				}
			}
			string language = parts[0];
			if (string.Equals(language, English, StringComparison.OrdinalIgnoreCase))
			{
				return English;
			}
			return available
				.OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
				.FirstOrDefault(a => string.Equals(a.Split('-')[0], language, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// The culture for the run. A setting that the app cannot show is ignored (the preferred languages decide); a preferred list in which nothing
		/// can be shown gives English.
		/// </summary>
		public static string Choose(string setting, IEnumerable<string> preferred, IReadOnlyCollection<string> available)
		{
			string fromSetting = Match(setting, available);
			if (fromSetting != null)
			{
				return fromSetting;
			}
			foreach (string tag in preferred ?? Enumerable.Empty<string>())
			{
				string match = Match(tag, available);
				if (match != null)
				{
					return match;
				}
			}
			return English;
		}

		/// <summary>
		/// Chooses the culture and gives it to the Core: <see cref="TR.ResourceFolder"/>, <see cref="TR.DefaultCulture"/> (always set, so that the
		/// current UI culture of the system cannot select a language the user did not get) and, for a translation, the UI culture of the thread and
		/// of new threads. Texts that were loaded before are dropped. Returns the culture.
		/// </summary>
		/// <param name="setting">The language setting (a culture name), or null.</param>
		/// <param name="languagesFolder">The folder with the culture folders; null for <see cref="DefaultLanguagesFolder"/>.</param>
		/// <param name="preferred">The preferred languages in order; null for the macOS list.</param>
		public static string Apply(string setting, string languagesFolder = null, IEnumerable<string> preferred = null)
		{
			// Eight Croatian files declare encoding="windows-1250"; .NET (unlike .NET Framework) needs the code page provider to read them.
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			string folder = languagesFolder ?? DefaultLanguagesFolder();
			IReadOnlyList<string> available = AvailableCultures(folder);
			if (!string.IsNullOrWhiteSpace(setting) && Match(setting, available) == null)
			{
				AppLog.Info("Language setting '" + setting + "' has no translation; the preferred languages decide.");
			}
			string culture = Choose(setting, preferred ?? MacPreferredLanguages(), available);
			CultureInfo info;
			try
			{
				info = new CultureInfo(culture);
			}
			catch (CultureNotFoundException)
			{
				culture = English;
				info = new CultureInfo(English);
			}
			TRDictionary resources = TR.LanguageResources;
			using (ItemMonitor.Lock(resources))
			{
				resources.Clear();
				TR.ResourceFolder = new VirtualFileFolder(folder);
				TR.DefaultCulture = info;
			}
			if (culture != English)
			{
				CultureInfo.DefaultThreadCurrentUICulture = info;
				Thread.CurrentThread.CurrentUICulture = info;
			}
			AppLog.Info("Language: " + culture + (culture == English ? " (no translation)" : "") + "; " + available.Count + " translations in " + folder + ".");
			return culture;
		}

		/// <summary>
		/// The language tags the user prefers, best first (<c>CFLocaleCopyPreferredLanguages</c>, the list of System Settings > General > Language
		/// &amp; Region, or of the app's own entry). Falls back to the current UI culture of the process.
		/// </summary>
		public static IReadOnlyList<string> MacPreferredLanguages()
		{
			var list = new List<string>();
			try
			{
				IntPtr array = CFLocaleCopyPreferredLanguages();
				if (array != IntPtr.Zero)
				{
					try
					{
						long count = CFArrayGetCount(array);
						for (long i = 0; i < count; i++)
						{
							string text = ReadString(CFArrayGetValueAtIndex(array, (IntPtr)i));
							if (!string.IsNullOrEmpty(text))
							{
								list.Add(text);
							}
						}
					}
					finally
					{
						CFRelease(array);
					}
				}
			}
			catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
			{
			}
			if (list.Count == 0 && !string.IsNullOrEmpty(CultureInfo.CurrentUICulture.Name))
			{
				list.Add(CultureInfo.CurrentUICulture.Name);
			}
			return list;
		}

		private static string ReadString(IntPtr cfString)
		{
			if (cfString == IntPtr.Zero)
			{
				return null;
			}
			var buffer = new byte[256];
			return CFStringGetCString(cfString, buffer, buffer.Length, KCFStringEncodingUTF8) != 0
				? Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0) is int end and >= 0 ? end : buffer.Length)
				: null;
		}

		private const uint KCFStringEncodingUTF8 = 0x08000100;
		private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

		[DllImport(CoreFoundation)]
		private static extern IntPtr CFLocaleCopyPreferredLanguages();

		[DllImport(CoreFoundation)]
		private static extern long CFArrayGetCount(IntPtr array);

		[DllImport(CoreFoundation)]
		private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, IntPtr index);

		[DllImport(CoreFoundation)]
		private static extern byte CFStringGetCString(IntPtr str, byte[] buffer, long bufferSize, uint encoding);

		[DllImport(CoreFoundation)]
		private static extern void CFRelease(IntPtr obj);
	}
}
