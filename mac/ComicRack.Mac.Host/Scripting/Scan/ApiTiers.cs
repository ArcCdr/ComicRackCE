using System;
using System.Collections.Generic;

namespace ComicRack.Mac.Host.Scripting.Scan
{
	/// <summary>
	/// The tiers of the Script UI Compatibility Layer (MACOS_NATIVE_PATH.md section 5.9.5) by type of the WinForms and GDI+ surface:
	/// A = must (v1), B = should (v1 if the scan shows usage), C = unsupported (a clear error), S = supporting type (enum, struct, delegate, interface, event arguments)
	/// that raises no tier of its own, U = a class that no tier lists (counts as B). System.Drawing classes are A. The table is about the surface as a whole, not about any script.
	/// </summary>
	public static class ApiTiers
	{
		private static readonly HashSet<string> TierA = new HashSet<string>(StringComparer.Ordinal)
		{
			"Form", "Control", "UserControl", "Panel", "GroupBox", "Label", "LinkLabel", "Button", "CheckBox", "RadioButton", "TextBox", "ComboBox", "ListBox", "CheckedListBox",
			"NumericUpDown", "PictureBox", "ProgressBar", "TabControl", "TabPage", "SplitContainer", "FlowLayoutPanel", "TableLayoutPanel", "ToolTip", "Timer", "ContextMenuStrip",
			"ToolStripMenuItem", "MessageBox", "OpenFileDialog", "SaveFileDialog", "FolderBrowserDialog", "DateTimePicker", "TrackBar", "Clipboard", "Cursors",
			// Classes the tier A types need or are used with every dialog.
			"Application", "Cursor", "Screen", "ScrollableControl", "ContainerControl", "ButtonBase", "TextBoxBase", "ListControl", "UpDownBase", "FileDialog", "CommonDialog",
			"IWin32Window", "ToolStripItem", "ToolStripDropDownItem",
			"ToolStripDropDown", "ToolStripDropDownMenu", "ToolStripSeparator", "ToolStripItemCollection", "SendKeys", "SplitterPanel",
		};

		private static readonly HashSet<string> TierB = new HashSet<string>(StringComparer.Ordinal)
		{
			"ListView", "ListViewItem", "ListViewSubItem", "ListViewGroup", "ColumnHeader", "TreeView", "TreeNode", "MenuStrip", "StatusStrip", "ToolStrip", "ToolStripButton", "ToolStripLabel",
			"ToolStripComboBox", "ToolStripTextBox", "ToolStripStatusLabel", "ToolStripProgressBar", "ToolStripSplitButton", "ToolStripDropDownButton", "DataGridView", "DataGridViewRow",
			"DataGridViewColumn", "DataGridViewCell", "DataGridViewTextBoxColumn", "DataGridViewCheckBoxColumn", "DataGridViewButtonColumn", "DataGridViewComboBoxColumn", "RichTextBox",
			"ColorDialog", "FontDialog", "WebBrowser", "HScrollBar", "VScrollBar", "ScrollBar", "MaskedTextBox", "PropertyGrid", "ImageList", "BindingSource", "NotifyIcon", "Splitter",
			"MainMenu", "ContextMenu", "MenuItem", "Menu", "StatusBar", "StatusBarPanel", "ToolBar", "ToolBarButton",
		};

		private static readonly HashSet<string> TierC = new HashSet<string>(StringComparer.Ordinal)
		{
			"NativeWindow", "Message", "IMessageFilter", "HtmlDocument", "HtmlElement", "HtmlElementCollection", "HtmlWindow", "HtmlWindowCollection", "HtmlHistory", "HtmlElementEventArgs",
			"HtmlElementErrorEventArgs", "HtmlDocument.HtmlElementEventHandler", "WebBrowserSiteBase",
		};

		/// <summary>The tier of a type: A, B, C, S or U.</summary>
		public static string Of(ApiCatalog catalog, ApiType type)
		{
			if (type == null)
			{
				return "U";
			}
			int plus = type.FullName.LastIndexOf('+');
			if (plus > 0 && type.Kind == ApiTypeKind.Class && !IsEventArguments(catalog, type))
			{
				// A nested class (a collection, an item) belongs to the tier of the type that declares it.
				ApiType outer = catalog.Type(type.FullName.Substring(0, plus));
				if (outer != null)
				{
					return Of(catalog, outer);
				}
			}
			if (type.Namespace == "System.Windows.Forms")
			{
				if (TierC.Contains(type.Name))
				{
					return "C";
				}
				if (TierA.Contains(type.Name))
				{
					return "A";
				}
				if (TierB.Contains(type.Name))
				{
					return "B";
				}
			}
			if (type.Kind != ApiTypeKind.Class || IsEventArguments(catalog, type))
			{
				return "S";
			}
			if (type.Namespace == "System.Windows.Forms")
			{
				return catalog.IsSubclassOf(type, "System.Windows.Forms.CommonDialog") ? "B" : "U";
			}
			return "A";
		}

		private static bool IsEventArguments(ApiCatalog catalog, ApiType type) => catalog.IsSubclassOf(type, "System.EventArgs") || catalog.IsSubclassOf(type, "System.Exception");

		/// <summary>The tier of a scanned unit: none (no use of the surface), A, B or C (C when it has an error finding).</summary>
		public static string ForUnit(ApiCatalog catalog, ScanUnit unit)
		{
			string tier = "none";
			var types = new HashSet<string>(unit.Types.Keys);
			foreach (string key in unit.Members.Keys)
			{
				types.Add(key.Substring(0, key.IndexOf("::", StringComparison.Ordinal)));
			}
			foreach (string full in types)
			{
				string t = ScriptTier(Of(catalog, catalog.Type(full)));
				tier = tier == "none" || Rank(t) > Rank(tier) ? t : tier;
			}
			foreach (ScanFinding f in unit.Findings)
			{
				if (f.Severity == "error")
				{
					return "C";
				}
			}
			return tier;
		}

		/// <summary>The larger of two tiers (A &lt; B &lt; C), with S counted as A and U as B; null counts as none.</summary>
		public static string Max(string a, string b) => Rank(a) >= Rank(b) ? a : b;

		public static int Rank(string tier)
		{
			switch (tier)
			{
			case "C": return 3;
			case "B":
			case "U": return 2;
			case "A":
			case "S": return 1;
			default: return 0;
			}
		}

		/// <summary>The tier a script gets from a type tier: A, B or C.</summary>
		public static string ScriptTier(string typeTier)
		{
			switch (typeTier)
			{
			case "C": return "C";
			case "B":
			case "U": return "B";
			default: return "A";
			}
		}
	}
}
