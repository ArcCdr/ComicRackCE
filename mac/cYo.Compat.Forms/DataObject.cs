using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.ComTypes;

namespace System.Windows.Forms
{
	/// <summary>
	/// In-memory format table; there is no OS clipboard behind it. Like the real class it implements the COM
	/// IDataObject, because cYo.Common.Win32.DataObjectEx declares that interface again; the COM members throw.
	/// </summary>
	public class DataObject : IDataObject
	{
		private readonly Dictionary<string, object> data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

		public DataObject()
		{
		}

		public virtual void SetData(string format, object data)
		{
			this.data[format] = data;
		}

		public virtual object GetData(string format, bool autoConvert)
		{
			if (!data.TryGetValue(format, out var value))
			{
				return null;
			}
			return value;
		}

		private static NotSupportedException Com(string member)
		{
			return new NotSupportedException("System.Windows.Forms.DataObject." + member + " (COM IDataObject) is not yet supported on macOS");
		}

		int IDataObject.DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection)
		{
			throw Com("DAdvise");
		}

		void IDataObject.DUnadvise(int connection)
		{
			throw Com("DUnadvise");
		}

		int IDataObject.EnumDAdvise(out IEnumSTATDATA enumAdvise)
		{
			throw Com("EnumDAdvise");
		}

		IEnumFORMATETC IDataObject.EnumFormatEtc(DATADIR direction)
		{
			throw Com("EnumFormatEtc");
		}

		int IDataObject.GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
		{
			throw Com("GetCanonicalFormatEtc");
		}

		void IDataObject.GetData(ref FORMATETC format, out STGMEDIUM medium)
		{
			throw Com("GetData");
		}

		void IDataObject.GetDataHere(ref FORMATETC format, ref STGMEDIUM medium)
		{
			throw Com("GetDataHere");
		}

		int IDataObject.QueryGetData(ref FORMATETC format)
		{
			throw Com("QueryGetData");
		}

		void IDataObject.SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
		{
			throw Com("SetData");
		}
	}

	public static class DataFormats
	{
		public class Format
		{
			public string Name
			{
				get;
			}

			public int Id
			{
				get;
			}

			internal Format(string name, int id)
			{
				Name = name;
				Id = id;
			}
		}

		public static readonly string UnicodeText = "UnicodeText";

		private static readonly Dictionary<string, Format> formats = new Dictionary<string, Format>(StringComparer.OrdinalIgnoreCase);

		private static int nextId = 0xC000;

		public static Format GetFormat(string format)
		{
			lock (formats)
			{
				if (!formats.TryGetValue(format, out var value))
				{
					int id = ((string.Compare(format, UnicodeText, StringComparison.OrdinalIgnoreCase) == 0) ? 13 : nextId++);
					value = new Format(format, id);
					formats[format] = value;
				}
				return value;
			}
		}
	}

	public static class Clipboard
	{
		public static void SetDataObject(object data)
		{
			throw new NotSupportedException("System.Windows.Forms.Clipboard.SetDataObject is not yet supported on macOS");
		}
	}
}
