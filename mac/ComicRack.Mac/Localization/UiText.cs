using System;
using cYo.Common.Localize;
using ComicRack.Mac.Controls;

namespace ComicRack.Mac
{
	/// <summary>
	/// The labels of the UI, from the original's texts (<c>Languages/&lt;culture&gt;/&lt;Form&gt;.xml</c>, section = the form's type name, key = the
	/// control's name, full-port.md section 2.11 rule 2). The default is the English text of the Designer file, so a missing file or key (English has
	/// no folder) gives what the original shows. The Windows mnemonic ampersand is dropped (macOS has none, chrome-guidelines section 8).
	/// </summary>
	public static class UiText
	{
		/// <summary>The text for <paramref name="key"/> of <paramref name="section"/>; <paramref name="english"/> is the Designer text (with its mnemonic).</summary>
		public static string Get(string section, string key, string english)
		{
			return CrDialogWindow.StripMnemonic(Raw(section, key, english));
		}

		/// <summary>The same text with the ampersands kept (a format string that may contain a literal ampersand pair).</summary>
		public static string Raw(string section, string key, string english)
		{
			try
			{
				return TR.Load(section)[key, english];
			}
			catch (Exception)
			{
				return english;
			}
		}

		/// <summary>A text of the shared <c>Default</c> section (<c>TR.Default</c>), for example "None" or "Search".</summary>
		public static string Default(string key, string english)
		{
			try
			{
				return TR.Default[key, english];
			}
			catch (Exception)
			{
				return english;
			}
		}

		/// <summary>A text of the <c>Messages</c> section (<c>TR.Messages</c>).</summary>
		public static string Message(string key, string english)
		{
			try
			{
				return TR.Messages[key, english];
			}
			catch (Exception)
			{
				return english;
			}
		}

		/// <summary>The tool tip of a control: <c>&lt;key&gt;.Tooltip</c> if the language file has one, else null.</summary>
		public static string Tooltip(string section, string key)
		{
			string text = Raw(section, key + ".Tooltip", null);
			return string.IsNullOrEmpty(text) ? null : CrDialogWindow.StripMnemonic(text);
		}
	}
}
