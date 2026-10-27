using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.ComTypes;

namespace System.Windows.Forms
{
	/// <summary>
	/// In-memory format table; there is no OS clipboard behind it. Like the real class it implements the COM
	/// IDataObject, because cYo.Common.Win32.DataObjectEx declares that interface again; the COM members throw.
	/// </summary>
	public partial class DataObject : System.Runtime.InteropServices.ComTypes.IDataObject
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

		int System.Runtime.InteropServices.ComTypes.IDataObject.DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection)
		{
			throw Com("DAdvise");
		}

		void System.Runtime.InteropServices.ComTypes.IDataObject.DUnadvise(int connection)
		{
			throw Com("DUnadvise");
		}

		int System.Runtime.InteropServices.ComTypes.IDataObject.EnumDAdvise(out IEnumSTATDATA enumAdvise)
		{
			throw Com("EnumDAdvise");
		}

		IEnumFORMATETC System.Runtime.InteropServices.ComTypes.IDataObject.EnumFormatEtc(DATADIR direction)
		{
			throw Com("EnumFormatEtc");
		}

		int System.Runtime.InteropServices.ComTypes.IDataObject.GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
		{
			throw Com("GetCanonicalFormatEtc");
		}

		void System.Runtime.InteropServices.ComTypes.IDataObject.GetData(ref FORMATETC format, out STGMEDIUM medium)
		{
			throw Com("GetData");
		}

		void System.Runtime.InteropServices.ComTypes.IDataObject.GetDataHere(ref FORMATETC format, ref STGMEDIUM medium)
		{
			throw Com("GetDataHere");
		}

		int System.Runtime.InteropServices.ComTypes.IDataObject.QueryGetData(ref FORMATETC format)
		{
			throw Com("QueryGetData");
		}

		void System.Runtime.InteropServices.ComTypes.IDataObject.SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
		{
			throw Com("SetData");
		}
	}

	public static partial class DataFormats
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

			public Format(string name, int id)
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

	public static partial class Clipboard
	{
		public static void SetDataObject(object data)
		{
			throw new NotSupportedException("System.Windows.Forms.Clipboard.SetDataObject is not yet supported on macOS");
		}
	}
}
