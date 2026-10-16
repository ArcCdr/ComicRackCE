// Replacements for types of cYo.Common.Windows (the WinForms helper library) that Engine files still reference.
// They keep the full type names. ItemViewConfig, ItemViewColumnInfo, ItemViewMode, ItemViewGroupsStatus and
// GroupHeaderInformation are the real files (linked in Sources.props since M1.1). IViewableItem and IColumn stay
// reduced stubs, because the real ones reference the ItemView control.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using cYo.Common.ComponentModel;
using cYo.Common.Localize;
using cYo.Common.Text;

namespace cYo.Common.Windows.Forms
{
	// Real type: cYo.Common.Windows/Forms/IViewableItem.cs. Engine only uses it as a generic argument.
	public interface IViewableItem
	{
	}

	// Real type: cYo.Common.Windows/Forms/IColumn.cs (with IBaseViewItem), reduced to what the Core reads: the members
	// that ComicBookMetadataManager and ItemViewColumnInfo use, with the real signatures. The real interface needs
	// ItemView, Graphics and HeaderState (a WinForms control and drawing); nothing in the Core implements it.
	public interface IColumn
	{
		int Id
		{
			get;
			set;
		}

		int FormatId
		{
			get;
			set;
		}

		string Text
		{
			get;
		}

		string Name
		{
			get;
		}

		object Tag
		{
			get;
			set;
		}

		bool Visible
		{
			get;
			set;
		}

		int Width
		{
			get;
			set;
		}

		DateTime LastTimeVisible
		{
			get;
			set;
		}

		IComparer<IViewableItem> ColumnSorter
		{
			get;
			set;
		}

		IGrouper<IViewableItem> ColumnGrouper
		{
			get;
			set;
		}
	}

	[Flags]
	public enum AutomaticProgressDialogOptions
	{
		None = 0x0,
		EnableCancel = 0x1
	}

	// Real type: cYo.Common.Windows/Forms/AutomaticProgressDialog.cs (a WinForms dialog). Headless stand-in:
	// the work runs on the calling thread and no dialog is shown.
	public static class AutomaticProgressDialog
	{
		public static bool ShouldAbort => false;

		public static int Value
		{
			get;
			set;
		}

		public static bool Process(object parent, string caption, string description, int timeToWait, Action exectuteMethod, AutomaticProgressDialogOptions options)
		{
			exectuteMethod();
			return true;
		}
	}
}

namespace cYo.Common.Windows
{
	// Real type: cYo.Common.Windows/LocalizeUtility.cs; only the enum helper that ComicPageInfo and the XML
	// info providers use, copied from there (without the WinForms parts).
	public static class LocalizeUtility
	{
		public static string LocalizeEnum(Type enumType, int value)
		{
			string name = Enum.GetName(enumType, value);
			return TR.Load(enumType.Name)[name, GetEnumDescription(enumType, name).PascalToSpaced()];
		}

		private static string GetEnumDescription(Type enumType, string name)
		{
			System.Reflection.FieldInfo field = enumType.GetField(name);
			System.ComponentModel.DescriptionAttribute descriptionAttribute = (System.ComponentModel.DescriptionAttribute)Attribute.GetCustomAttribute(field, typeof(System.ComponentModel.DescriptionAttribute), inherit: false);
			if (descriptionAttribute != null)
			{
				return descriptionAttribute.Description;
			}
			return name;
		}
	}
}
