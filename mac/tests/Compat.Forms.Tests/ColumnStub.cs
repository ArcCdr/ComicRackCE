using System;
using System.Collections.Generic;

namespace cYo.Common.Collections
{
	// Stand-in for cYo.Common.Collections.ListExtensions.AddRange (the real file needs more of cYo.Common).
	public static class TestCollectionExtensions
	{
		public static void AddRange<T>(this ICollection<T> list, IEnumerable<T> items)
		{
			foreach (T item in items)
			{
				list.Add(item);
			}
		}
	}
}

namespace cYo.Common.Windows.Forms
{
	public interface IViewableItem
	{
	}

	// Same shape as the reduced IColumn stub in mac/ComicRack.Engine.Core/Replacements/CommonWindowsTypes.cs,
	// without the grouping members (they need cYo.Common).
	public interface IColumn
	{
		int Id { get; set; }

		int FormatId { get; set; }

		string Text { get; }

		string Name { get; }

		object Tag { get; set; }

		bool Visible { get; set; }

		int Width { get; set; }

		DateTime LastTimeVisible { get; set; }
	}
}
