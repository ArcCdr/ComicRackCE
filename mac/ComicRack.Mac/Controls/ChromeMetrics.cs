using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// The metrics of the original's custom-drawn chrome in device independent units (pixels at 96 DPI), chrome-guidelines section 4, and the
	/// fonts of section 3 (decision C-FONT-1: the system font with the original's proportions). Every value names its source in the original.
	/// </summary>
	public static class ChromeMetrics
	{
		/// <summary>Browser toolbar (<c>ComicBrowserControl.Designer.cs</c>) and the reader toolbar: 25 high.</summary>
		public const double ToolBarHeight = 25;

		/// <summary>A <c>ToolStripButton</c> with a 16 pixel image: 23 x 22, one pixel above and two below it in a 25 pixel strip.</summary>
		public const double ToolButtonMinWidth = 23;

		public const double ToolButtonHeight = 22;

		public const double ToolIconSize = 16;

		/// <summary><c>SplitButton.PushButtonWidth</c>.</summary>
		public const double SplitArrowWidth = 14;

		public const double ToolSeparatorWidth = 6;

		/// <summary><c>MainForm.Designer.cs:2577-2589</c> and <c>TabBar.cs</c>: bar 31, tab 24, left indent 8, tab width 60 to 250, padding 2 above and 4 below.</summary>
		public const double TabBarHeight = 31;

		public const double TabHeight = 24;

		public const double TabLeftIndent = 8;

		public const double TabMinWidth = 60;

		public const double TabMaxWidth = 250;

		public const double TabTopPadding = 2;

		public const double TabBottomPadding = 4;

		/// <summary><c>MainForm.Designer.cs:1799-1818</c>: status strip 24 high, activity icons 20 x 19 with sunken borders.</summary>
		public const double StatusBarHeight = 24;

		public const double StatusSlotWidth = 20;

		public const double StatusSlotHeight = 19;

		/// <summary><c>CollapsibleGroupBox.cs:41, 62</c>: header 24, the toggle square is as wide as the header is high.</summary>
		public const double GroupHeaderHeight = 24;

		/// <summary><c>SizableContainer.cs:126</c> grip 6; <c>SlideTime</c> 100 ms.</summary>
		public const double GripWidth = 6;

		public const int SlideMilliseconds = 100;

		/// <summary><c>ItemView</c> column header 20 (<c>ItemView.cs:398-400</c>); text padding 3 above and below.</summary>
		public const double ColumnHeaderHeight = 20;

		/// <summary>Tree rows are the font height plus 8 (<c>TreeViewSkinner.cs:71</c>).</summary>
		public const double TreeRowExtra = 8;

		/// <summary>Dialog buttons of the original (<c>PreferencesDialog.Designer.cs</c>): 80 x 24, 6 apart.</summary>
		public const double DialogButtonWidth = 80;

		public const double DialogButtonHeight = 24;

		public const double DialogButtonGap = 6;

		private static double treeRowHeight;

		/// <summary>The row height of the tree item template (item font + 8), measured once.</summary>
		public static double DefaultTreeRowHeight => treeRowHeight > 0 ? treeRowHeight : (treeRowHeight = TreeRowHeight());

		/// <summary>The row height of a tree whose labels use <paramref name="fontSize"/>: the height of one text line plus 8.</summary>
		public static double TreeRowHeight(double fontSize = ChromeFonts.ItemSize)
		{
			return Math.Ceiling(ChromeFonts.LineHeight(fontSize)) + TreeRowExtra;
		}
	}

	/// <summary>
	/// Fonts (C-FONT-1, chrome-guidelines section 3): the system font family, the original's sizes in pixels (8.25 pt x 96/72 = 11, 9 pt = 12,
	/// 6.75 pt bold = 9, 12 pt = 16). The same values are the resources <c>Cr.FontSize.*</c> in <c>Themes/Controls/CrMetrics.axaml</c>.
	/// </summary>
	public static class ChromeFonts
	{
		/// <summary>Microsoft Sans Serif 8.25 pt: dialogs, labels, buttons, toolstrips.</summary>
		public const double ControlSize = 11;

		/// <summary>Microsoft Sans Serif 6.75 pt bold: small field captions.</summary>
		public const double CaptionSize = 9;

		/// <summary><c>SystemFonts.IconTitleFont</c> (Segoe UI 9 pt): item views, trees, tiles.</summary>
		public const double ItemSize = 12;

		/// <summary>Group headers of the item view: 1.15 x the item font.</summary>
		public const double GroupHeaderSize = ItemSize * 1.15;

		/// <summary>Tree counters: 0.75 x the item font.</summary>
		public const double CounterSize = ItemSize * 0.75;

		/// <summary>Segoe UI 12 pt: reader messages and overlays.</summary>
		public const double OverlaySize = 16;

		/// <summary>The height of one line of text at a size in the default (system) font, as the original's <c>Font.Height</c>.</summary>
		public static double LineHeight(double fontSize, FontWeight weight = FontWeight.Normal)
		{
			var text = new FormattedText("Ag", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight), fontSize, Brushes.Transparent);
			return text.Height;
		}
	}
}
