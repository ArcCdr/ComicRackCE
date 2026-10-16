// Replacements for types of cYo.Common.Windows (the WinForms helper library) that Engine files still reference.
// They keep the full type names. M1.1 links the real ItemViewConfig, ItemViewColumnInfo and ItemViewMode and
// then deletes the ItemViewConfig stub here.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using cYo.Common.ComponentModel;
using cYo.Common.Localize;
using cYo.Common.Text;

namespace cYo.Common.Windows.Forms
{
	// Real type: cYo.Common.Windows/Forms/ItemViewConfig.cs. It is serialized inside DisplayListConfig and
	// StacksConfig (ComicDb.xml). The stub has only the members whose types need no WinForms (the ones Engine
	// reads); the other elements are ignored on load until M1.1 links the real file.
	[Serializable]
	public class ItemViewConfig
	{
		[XmlAttribute]
		[DefaultValue(false)]
		public bool Grouping
		{
			get;
			set;
		}

		[XmlAttribute]
		[DefaultValue(null)]
		public string SortKey
		{
			get;
			set;
		}

		[XmlAttribute]
		[DefaultValue(null)]
		public string GrouperId
		{
			get;
			set;
		}

		[XmlAttribute]
		[DefaultValue(null)]
		public string StackerId
		{
			get;
			set;
		}
	}

	// Real type: cYo.Common.Windows/Forms/IViewableItem.cs. Engine only uses it as a generic argument.
	public interface IViewableItem
	{
	}

	// Real type: cYo.Common.Windows/Forms/IColumn.cs, reduced to what ComicBookMetadataManager reads.
	public interface IColumn
	{
		int Id
		{
			get;
		}

		string Name
		{
			get;
		}

		IComparer<IViewableItem> ColumnSorter
		{
			get;
		}

		IGrouper<IViewableItem> ColumnGrouper
		{
			get;
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
