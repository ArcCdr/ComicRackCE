using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Reflection;

// List controls and the numeric up-down (P4.9a): the items, the selection and the events that a change raises, in the order of dotnet/winforms (SelectedValueChanged is raised
// before SelectedIndexChanged; a combo box updates its Text, and so raises TextChanged, before it raises SelectedIndexChanged).
namespace System.Windows.Forms
{
	/// <summary>The items of a list box or a combo box, shared by the two nested ObjectCollection types. Thread safe, enumerations run over a copy.</summary>
	internal sealed class ListItems
	{
		private readonly List<object> items = new List<object>();
		private readonly Func<bool> sorted;
		private readonly Func<object, string> textOf;

		public ListItems(Func<bool> sorted, Func<object, string> textOf)
		{
			this.sorted = sorted;
			this.textOf = textOf;
		}

		public int Count
		{
			get
			{
				lock (items)
				{
					return items.Count;
				}
			}
		}

		public object this[int index]
		{
			get
			{
				lock (items)
				{
					if (index < 0 || index >= items.Count)
					{
						throw new ArgumentOutOfRangeException(nameof(index), index, "Index was out of range. Must be non-negative and less than the size of the collection.");
					}
					return items[index];
				}
			}
			set
			{
				lock (items)
				{
					if (index < 0 || index >= items.Count)
					{
						throw new ArgumentOutOfRangeException(nameof(index), index, "Index was out of range. Must be non-negative and less than the size of the collection.");
					}
					items[index] = value ?? throw new ArgumentNullException(nameof(value));
				}
			}
		}

		public object[] ToArray()
		{
			lock (items)
			{
				return items.ToArray();
			}
		}

		public int Add(object item)
		{
			if (item == null)
			{
				throw new ArgumentNullException(nameof(item));
			}
			lock (items)
			{
				if (sorted())
				{
					int at = items.Count;
					string text = textOf(item);
					for (int i = 0; i < items.Count; i++)
					{
						if (string.Compare(textOf(items[i]), text, StringComparison.CurrentCulture) > 0)
						{
							at = i;
							break;
						}
					}
					items.Insert(at, item);
					return at;
				}
				items.Add(item);
				return items.Count - 1;
			}
		}

		public void Insert(int index, object item)
		{
			if (item == null)
			{
				throw new ArgumentNullException(nameof(item));
			}
			lock (items)
			{
				if (index < 0 || index > items.Count)
				{
					throw new ArgumentOutOfRangeException(nameof(index), index, "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
				}
				items.Insert(sorted() ? Math.Min(index, items.Count) : index, item);
			}
		}

		public bool RemoveAt(int index)
		{
			lock (items)
			{
				if (index < 0 || index >= items.Count)
				{
					throw new ArgumentOutOfRangeException(nameof(index), index, "Index was out of range. Must be non-negative and less than the size of the collection.");
				}
				items.RemoveAt(index);
				return true;
			}
		}

		public int IndexOf(object value)
		{
			if (value == null)
			{
				throw new ArgumentNullException(nameof(value));
			}
			lock (items)
			{
				return items.IndexOf(value);
			}
		}

		public bool Contains(object value) => IndexOf(value) != -1;

		public void Clear()
		{
			lock (items)
			{
				items.Clear();
			}
		}

		public void ReplaceAll(IEnumerable source)
		{
			lock (items)
			{
				items.Clear();
				foreach (object o in source)
				{
					items.Add(o);
				}
			}
		}
	}

	public abstract partial class ListControl : Control
	{
		private object dataSource;
		private string displayMember = string.Empty;
		private string valueMember = string.Empty;
		private bool formattingEnabled;
		private string formatString = string.Empty;
		private IFormatProvider formatInfo;

		protected ListControl()
		{
		}

		public object DataSource
		{
			get => dataSource;
			set
			{
				if (!ReferenceEquals(dataSource, value))
				{
					if (value != null && !(value is IList) && !(value is IListSource))
					{
						throw new ArgumentException("Complex DataBinding accepts as a data source either an IList or an IListSource.", nameof(value));
					}
					dataSource = value;
					OnDataSourceChanged(EventArgs.Empty);
					DataSourceAssigned(value);
				}
			}
		}

		/// <summary>The list control puts the bound list in its items.</summary>
		internal virtual void DataSourceAssigned(object source)
		{
		}

		public string DisplayMember
		{
			get => displayMember;
			set
			{
				value = value ?? string.Empty;
				if (displayMember != value)
				{
					displayMember = value;
					OnDisplayMemberChanged(EventArgs.Empty);
				}
			}
		}

		public string ValueMember
		{
			get => valueMember;
			set
			{
				value = value ?? string.Empty;
				if (valueMember != value)
				{
					valueMember = value;
					OnValueMemberChanged(EventArgs.Empty);
				}
			}
		}

		public bool FormattingEnabled
		{
			get => formattingEnabled;
			set
			{
				if (formattingEnabled != value)
				{
					formattingEnabled = value;
					OnFormattingEnabledChanged(EventArgs.Empty);
				}
			}
		}

		public string FormatString
		{
			get => formatString;
			set
			{
				value = value ?? string.Empty;
				if (formatString != value)
				{
					formatString = value;
					OnFormatStringChanged(EventArgs.Empty);
				}
			}
		}

		public IFormatProvider FormatInfo
		{
			get => formatInfo;
			set
			{
				if (!ReferenceEquals(formatInfo, value))
				{
					formatInfo = value;
					OnFormatInfoChanged(EventArgs.Empty);
				}
			}
		}

		protected virtual bool AllowSelection => true;

		public abstract int SelectedIndex
		{
			get;
			set;
		}

		/// <summary>The item at an index (the bound list or the Items of the control).</summary>
		internal abstract object ItemAt(int index);

		public object SelectedValue
		{
			get
			{
				if (SelectedIndex == -1)
				{
					return null;
				}
				object item = ItemAt(SelectedIndex);
				return string.IsNullOrEmpty(valueMember) ? item : ReadMember(item, valueMember);
			}
			set
			{
				int count = ItemCount;
				for (int i = 0; i < count; i++)
				{
					object item = ItemAt(i);
					object candidate = string.IsNullOrEmpty(valueMember) ? item : ReadMember(item, valueMember);
					if (Equals(candidate, value))
					{
						SelectedIndex = i;
						return;
					}
				}
				SelectedIndex = -1;
			}
		}

		internal abstract int ItemCount
		{
			get;
		}

		private static object ReadMember(object item, string member)
		{
			if (item == null)
			{
				return null;
			}
			Type type = item.GetType();
			PropertyInfo property = type.GetProperty(member, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
			if (property != null)
			{
				return property.GetValue(item);
			}
			FieldInfo field = type.GetField(member, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
			return field?.GetValue(item);
		}

		/// <summary>The text of an item: the DisplayMember of the item when it has one, otherwise its ToString().</summary>
		public string GetItemText(object item)
		{
			if (item == null)
			{
				return string.Empty;
			}
			object value = string.IsNullOrEmpty(displayMember) ? item : ReadMember(item, displayMember) ?? item;
			if (!formattingEnabled)
			{
				return Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
			}
			var args = new ListControlConvertEventArgs(value, typeof(string), item);
			OnFormat(args);
			if (args.Value is string formatted && args.Value != value)
			{
				return formatted;
			}
			if (value is IFormattable formattable && !string.IsNullOrEmpty(formatString))
			{
				return formattable.ToString(formatString, formatInfo ?? CultureInfo.CurrentCulture);
			}
			return Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
		}

		protected object FilterItemOnProperty(object item) => FilterItemOnProperty(item, displayMember);

		protected object FilterItemOnProperty(object item, string field)
		{
			if (item == null || string.IsNullOrEmpty(field))
			{
				return item;
			}
			return ReadMember(item, field) ?? item;
		}

		protected abstract void RefreshItem(int index);

		protected virtual void RefreshItems()
		{
		}

		protected virtual void SetItemCore(int index, object value)
		{
		}

		protected abstract void SetItemsCore(IList items);

		protected virtual void OnSelectedIndexChanged(EventArgs e)
		{
			OnSelectedValueChanged(EventArgs.Empty);
		}
	}

	public partial class ListBox : ListControl
	{
		private readonly ListItems store;
		private readonly ObjectCollection items;
		private readonly SortedSet<int> selected = new SortedSet<int>();
		private SelectionMode selectionMode = SelectionMode.One;
		private BorderStyle borderStyle = BorderStyle.Fixed3D;
		private bool sorted;
		private bool integralHeight = true;
		private bool multiColumn;
		private bool horizontalScrollbar;
		private bool scrollAlwaysVisible;
		private bool useTabStops = true;
		private int columnWidth;
		private int itemHeight = 13;
		private int topIndex;
		private int updating;
		private DrawMode drawMode = DrawMode.Normal;

		public ListBox()
		{
			store = new ListItems(() => sorted, GetItemText);
			items = CreateItemCollection();
			SetStyle(ControlStyles.UserPaint | ControlStyles.StandardClick | ControlStyles.UseTextForAccessibility, false);
			SetStyle(ControlStyles.StandardClick, false);
		}

		protected override Size DefaultSize => new Size(120, 96);

		internal override Size NonClientSize => UserControl.BorderSize(borderStyle);

		internal override Point ClientOffset => new Point(UserControl.BorderSize(borderStyle).Width / 2, UserControl.BorderSize(borderStyle).Height / 2);

		public override Color BackColor
		{
			get => HasOwnBackColor ? base.BackColor : Color.FromArgb(255, 255, 255);
			set => base.BackColor = value;
		}

		public override Color ForeColor
		{
			get => HasOwnForeColor ? base.ForeColor : Color.FromArgb(0, 0, 0);
			set => base.ForeColor = value;
		}

		internal ListItems Store => store;

		internal override int ItemCount => DataSource is IList bound ? bound.Count : store.Count;

		internal override object ItemAt(int index) => DataSource is IList bound ? bound[index] : store[index];

		internal override void DataSourceAssigned(object source)
		{
			if (source is IListSource listSource)
			{
				source = listSource.GetList();
			}
			ClearSelectionSilently();
			if (source is IList list)
			{
				store.ReplaceAll(list);
			}
			else
			{
				store.Clear();
			}
			Notify("Items");
		}

		protected virtual ObjectCollection CreateItemCollection() => new ObjectCollection(this);

		public ObjectCollection Items => items;

		public BorderStyle BorderStyle
		{
			get => borderStyle;
			set
			{
				if (borderStyle != value)
				{
					Size client = ClientSize;
					borderStyle = value;
					Size = SizeFromClientSize(client);
					Notify("BorderStyle");
				}
			}
		}

		public bool Sorted
		{
			get => sorted;
			set
			{
				if (sorted != value)
				{
					sorted = value;
					if (value)
					{
						object[] all = store.ToArray();
						Array.Sort(all, (a, b) => string.Compare(GetItemText(a), GetItemText(b), StringComparison.CurrentCulture));
						store.ReplaceAll(all);
						ClearSelectionSilently();
						Notify("Items");
					}
				}
			}
		}

		public virtual SelectionMode SelectionMode
		{
			get => selectionMode;
			set
			{
				if (!Enum.IsDefined(typeof(SelectionMode), value))
				{
					throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(SelectionMode));
				}
				if (selectionMode != value)
				{
					selectionMode = value;
					ClearSelected();
					Notify("SelectionMode");
				}
			}
		}

		public bool IntegralHeight
		{
			get => integralHeight;
			set => integralHeight = value;
		}

		public bool MultiColumn
		{
			get => multiColumn;
			set => multiColumn = value;
		}

		public bool HorizontalScrollbar
		{
			get => horizontalScrollbar;
			set => horizontalScrollbar = value;
		}

		public bool ScrollAlwaysVisible
		{
			get => scrollAlwaysVisible;
			set => scrollAlwaysVisible = value;
		}

		public bool UseTabStops
		{
			get => useTabStops;
			set => useTabStops = value;
		}

		public int ColumnWidth
		{
			get => columnWidth;
			set => columnWidth = value;
		}

		public virtual int ItemHeight
		{
			get => itemHeight;
			set => itemHeight = value;
		}

		public virtual DrawMode DrawMode
		{
			get => drawMode;
			set => drawMode = value;
		}

		public int TopIndex
		{
			get => topIndex;
			set
			{
				topIndex = value;
				Notify("TopIndex");
			}
		}

		// ---- selection

		public override int SelectedIndex
		{
			get
			{
				lock (selected)
				{
					return selected.Count == 0 ? -1 : selected.Min;
				}
			}
			set
			{
				int count = ItemCount;
				if (value < -1 || value >= count)
				{
					throw new ArgumentOutOfRangeException(nameof(value), value, "Index was out of range. Must be non-negative and less than the size of the collection.");
				}
				if (selectionMode == SelectionMode.None)
				{
					throw new ArgumentException("Cannot set the selection when the SelectionMode is None.");
				}
				bool changed;
				lock (selected)
				{
					if (value == -1)
					{
						changed = selected.Count > 0;
						selected.Clear();
					}
					else
					{
						changed = selected.Count != 1 || selected.Min != value;
						selected.Clear();
						selected.Add(value);
					}
				}
				if (changed)
				{
					SelectionChanged();
				}
			}
		}

		public object SelectedItem
		{
			get
			{
				int index = SelectedIndex;
				return index == -1 ? null : ItemAt(index);
			}
			set
			{
				if (value == null)
				{
					SelectedIndex = -1;
					return;
				}
				int count = ItemCount;
				for (int i = 0; i < count; i++)
				{
					if (Equals(ItemAt(i), value))
					{
						SelectedIndex = i;
						return;
					}
				}
			}
		}

		public SelectedIndexCollection SelectedIndices => new SelectedIndexCollection(this);

		public SelectedObjectCollection SelectedItems => new SelectedObjectCollection(this);

		internal int[] SelectedSnapshot()
		{
			lock (selected)
			{
				var result = new int[selected.Count];
				selected.CopyTo(result);
				return result;
			}
		}

		public bool GetSelected(int index)
		{
			if (index < 0 || index >= ItemCount)
			{
				throw new ArgumentOutOfRangeException(nameof(index), index, "Index was out of range. Must be non-negative and less than the size of the collection.");
			}
			lock (selected)
			{
				return selected.Contains(index);
			}
		}

		public void SetSelected(int index, bool value)
		{
			if (index < 0 || index >= ItemCount)
			{
				throw new ArgumentOutOfRangeException(nameof(index), index, "Index was out of range. Must be non-negative and less than the size of the collection.");
			}
			if (selectionMode == SelectionMode.None)
			{
				throw new InvalidOperationException("Cannot set the selection when the SelectionMode is None.");
			}
			bool changed;
			lock (selected)
			{
				if (selectionMode == SelectionMode.One && value)
				{
					changed = selected.Count != 1 || !selected.Contains(index);
					selected.Clear();
					selected.Add(index);
				}
				else
				{
					changed = value ? selected.Add(index) : selected.Remove(index);
				}
			}
			if (changed)
			{
				SelectionChanged();
			}
		}

		public void ClearSelected()
		{
			bool changed;
			lock (selected)
			{
				changed = selected.Count > 0;
				selected.Clear();
			}
			if (changed)
			{
				SelectionChanged();
			}
		}

		private void ClearSelectionSilently()
		{
			lock (selected)
			{
				selected.Clear();
			}
		}

		private void SelectionChanged()
		{
			Notify("SelectedIndex");
			if (updating == 0)
			{
				OnSelectedIndexChanged(EventArgs.Empty);
			}
		}

		/// <summary>The items changed: the selection follows the removed or inserted items and SelectedIndexChanged is raised when the selection changed.</summary>
		internal void ItemsChanged(int removedIndex, int insertedIndex)
		{
			bool changed = false;
			lock (selected)
			{
				if (removedIndex == -2)
				{
					changed = selected.Count > 0;
					selected.Clear();
				}
				else if (removedIndex >= 0)
				{
					var next = new List<int>();
					foreach (int i in selected)
					{
						if (i == removedIndex)
						{
							changed = true;
						}
						else
						{
							next.Add(i > removedIndex ? i - 1 : i);
						}
					}
					selected.Clear();
					foreach (int i in next)
					{
						selected.Add(i);
					}
				}
				else if (insertedIndex >= 0)
				{
					var next = new List<int>();
					foreach (int i in selected)
					{
						next.Add(i >= insertedIndex ? i + 1 : i);
					}
					selected.Clear();
					foreach (int i in next)
					{
						selected.Add(i);
					}
				}
			}
			Notify("Items");
			if (changed)
			{
				SelectionChanged();
			}
		}

		public void BeginUpdate()
		{
			updating++;
		}

		public void EndUpdate()
		{
			if (updating > 0)
			{
				updating--;
			}
		}

		public int FindString(string s) => FindString(s, -1);

		public int FindString(string s, int startIndex)
		{
			return Find(s, startIndex, exact: false);
		}

		public int FindStringExact(string s) => FindStringExact(s, -1);

		public int FindStringExact(string s, int startIndex)
		{
			return Find(s, startIndex, exact: true);
		}

		private int Find(string s, int startIndex, bool exact)
		{
			if (s == null)
			{
				return -1;
			}
			int count = ItemCount;
			if (count == 0)
			{
				return -1;
			}
			if (startIndex < -1 || startIndex >= count)
			{
				throw new ArgumentOutOfRangeException(nameof(startIndex), startIndex, "Index was out of range. Must be non-negative and less than the size of the collection.");
			}
			for (int step = 1; step <= count; step++)
			{
				int i = (startIndex + step) % count;
				string text = GetItemText(ItemAt(i));
				if (exact ? string.Equals(text, s, StringComparison.OrdinalIgnoreCase) : text.StartsWith(s, StringComparison.OrdinalIgnoreCase))
				{
					return i;
				}
			}
			return -1;
		}

		public int GetItemHeight(int index) => itemHeight;

		protected virtual void AddItemsCore(object[] value)
		{
			foreach (object o in value)
			{
				store.Add(o);
			}
		}

		protected virtual void Sort()
		{
		}

		protected virtual void OnDrawItem(DrawItemEventArgs e)
		{
			DrawItem?.Invoke(this, e);
		}

		protected virtual void OnMeasureItem(MeasureItemEventArgs e)
		{
			MeasureItem?.Invoke(this, e);
		}

		protected override void OnSelectedIndexChanged(EventArgs e)
		{
			base.OnSelectedIndexChanged(e);
			SelectedIndexChanged?.Invoke(this, e);
		}

		protected override void RefreshItem(int index)
		{
			Notify("Items");
		}

		protected override void SetItemsCore(IList value)
		{
			ClearSelectionSilently();
			store.ReplaceAll(value);
			Notify("Items");
		}

		public event DrawItemEventHandler DrawItem;

		public event MeasureItemEventHandler MeasureItem;

		public event EventHandler SelectedIndexChanged;

		public partial class ObjectCollection : IList
		{
			private readonly ListBox owner;

			public ObjectCollection(ListBox owner)
			{
				this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
			}

			public ObjectCollection(ListBox owner, ObjectCollection value)
				: this(owner)
			{
				AddRange(value);
			}

			public ObjectCollection(ListBox owner, object[] value)
				: this(owner)
			{
				AddRange(value);
			}

			public int Count => owner.ItemCount;

			public bool IsReadOnly => false;

			public virtual object this[int index]
			{
				get
				{
					if (index < 0 || index >= Count)
					{
						throw new ArgumentOutOfRangeException(nameof(index), index, "Index was out of range. Must be non-negative and less than the size of the collection.");
					}
					return owner.ItemAt(index);
				}
				set
				{
					owner.store[index] = value;
					owner.Notify("Items");
				}
			}

			public int Add(object item)
			{
				CheckBound();
				int index = owner.store.Add(item);
				owner.ItemsChanged(-1, index);
				return index;
			}

			public void AddRange(ObjectCollection value)
			{
				if (value == null)
				{
					throw new ArgumentNullException(nameof(value));
				}
				AddRange(value.owner.store.ToArray());
			}

			public void AddRange(params object[] items)
			{
				if (items == null)
				{
					throw new ArgumentNullException(nameof(items));
				}
				CheckBound();
				owner.BeginUpdate();
				try
				{
					foreach (object o in items)
					{
						int index = owner.store.Add(o);
						owner.ItemsChanged(-1, index);
					}
				}
				finally
				{
					owner.EndUpdate();
				}
			}

			public virtual void Clear()
			{
				CheckBound();
				owner.store.Clear();
				owner.ItemsChanged(-2, -1);
			}

			public bool Contains(object value) => owner.store.Contains(value);

			public void CopyTo(object[] destination, int arrayIndex)
			{
				object[] all = owner.store.ToArray();
				Array.Copy(all, 0, destination, arrayIndex, all.Length);
			}

			public IEnumerator GetEnumerator() => owner.store.ToArray().GetEnumerator();

			public int IndexOf(object value) => owner.store.IndexOf(value);

			public void Insert(int index, object item)
			{
				CheckBound();
				owner.store.Insert(index, item);
				owner.ItemsChanged(-1, index);
			}

			public void Remove(object value)
			{
				int index = owner.store.IndexOf(value);
				if (index != -1)
				{
					RemoveAt(index);
				}
			}

			public void RemoveAt(int index)
			{
				CheckBound();
				owner.store.RemoveAt(index);
				owner.ItemsChanged(index, -1);
			}

			private void CheckBound()
			{
				if (owner.DataSource != null)
				{
					throw new ArgumentException("Cannot modify the Items collection when the DataSource property is set.");
				}
			}

			void ICollection.CopyTo(Array array, int index)
			{
				((ICollection)owner.store.ToArray()).CopyTo(array, index);
			}

			bool ICollection.IsSynchronized => false;

			object ICollection.SyncRoot => this;

			int IList.Add(object value) => Add(value);

			bool IList.IsFixedSize => false;
		}

		public partial class SelectedIndexCollection : IList
		{
			private readonly ListBox owner;

			public SelectedIndexCollection(ListBox owner)
			{
				this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
			}

			public int Count => owner.SelectedSnapshot().Length;

			public bool IsReadOnly => true;

			public int this[int index]
			{
				get
				{
					int[] all = owner.SelectedSnapshot();
					if (index < 0 || index >= all.Length)
					{
						throw new ArgumentOutOfRangeException(nameof(index), index, "Index was out of range. Must be non-negative and less than the size of the collection.");
					}
					return all[index];
				}
			}

			public void Add(int index) => owner.SetSelected(index, true);

			public void Clear() => owner.ClearSelected();

			public bool Contains(int selectedIndex) => Array.IndexOf(owner.SelectedSnapshot(), selectedIndex) >= 0;

			public void CopyTo(Array destination, int index) => owner.SelectedSnapshot().CopyTo(destination, index);

			public IEnumerator GetEnumerator() => owner.SelectedSnapshot().GetEnumerator();

			public int IndexOf(int selectedIndex) => Array.IndexOf(owner.SelectedSnapshot(), selectedIndex);

			public void Remove(int index) => owner.SetSelected(index, false);

			bool ICollection.IsSynchronized => false;

			object ICollection.SyncRoot => this;

			int IList.Add(object value)
			{
				Add((int)value);
				return Count - 1;
			}

			bool IList.Contains(object value) => value is int i && Contains(i);

			int IList.IndexOf(object value) => value is int i ? IndexOf(i) : -1;

			void IList.Insert(int index, object value) => throw new NotSupportedException();

			bool IList.IsFixedSize => false;

			void IList.Remove(object value)
			{
				Remove((int)value);
			}

			void IList.RemoveAt(int index) => throw new NotSupportedException();

			object IList.this[int index]
			{
				get => this[index];
				set => throw new NotSupportedException();
			}
		}

		public partial class SelectedObjectCollection : IList
		{
			private readonly ListBox owner;

			public SelectedObjectCollection(ListBox owner)
			{
				this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
			}

			private object[] Snapshot()
			{
				int[] indexes = owner.SelectedSnapshot();
				var result = new object[indexes.Length];
				for (int i = 0; i < result.Length; i++)
				{
					result[i] = owner.ItemAt(indexes[i]);
				}
				return result;
			}

			public int Count => owner.SelectedSnapshot().Length;

			public bool IsReadOnly => true;

			public object this[int index]
			{
				get
				{
					object[] all = Snapshot();
					if (index < 0 || index >= all.Length)
					{
						throw new ArgumentOutOfRangeException(nameof(index), index, "Index was out of range. Must be non-negative and less than the size of the collection.");
					}
					return all[index];
				}
				set => throw new NotSupportedException("The selected object collection is read-only.");
			}

			public void Add(object value)
			{
				int index = owner.Items.IndexOf(value);
				if (index != -1)
				{
					owner.SetSelected(index, true);
				}
			}

			public void Clear() => owner.ClearSelected();

			public bool Contains(object selectedObject) => Array.IndexOf(Snapshot(), selectedObject) >= 0;

			public void CopyTo(Array destination, int index) => Snapshot().CopyTo(destination, index);

			public IEnumerator GetEnumerator() => Snapshot().GetEnumerator();

			public int IndexOf(object selectedObject) => Array.IndexOf(Snapshot(), selectedObject);

			public void Remove(object value)
			{
				int index = owner.Items.IndexOf(value);
				if (index != -1)
				{
					owner.SetSelected(index, false);
				}
			}

			bool ICollection.IsSynchronized => false;

			object ICollection.SyncRoot => this;

			int IList.Add(object value)
			{
				Add(value);
				return Count - 1;
			}

			void IList.Insert(int index, object value) => throw new NotSupportedException();

			bool IList.IsFixedSize => false;

			void IList.RemoveAt(int index) => throw new NotSupportedException();
		}
	}

	public partial class ComboBox : ListControl
	{
		private readonly ListItems store;
		private readonly ObjectCollection items;
		private int selectedIndex = -1;
		private ComboBoxStyle dropDownStyle = ComboBoxStyle.DropDown;
		private bool sorted;
		private bool droppedDown;
		private bool integralHeight = true;
		private int dropDownHeight = 106;
		private int dropDownWidth;
		private int maxDropDownItems = 8;
		private int maxLength;
		private int itemHeight = 13;
		private int selectionStart;
		private int selectionLength;
		private int updating;
		private FlatStyle flatStyle = FlatStyle.Standard;
		private DrawMode drawMode = DrawMode.Normal;
		private AutoCompleteMode autoCompleteMode = AutoCompleteMode.None;
		private AutoCompleteSource autoCompleteSource = AutoCompleteSource.None;

		public ComboBox()
		{
			store = new ListItems(() => sorted, GetItemText);
			items = new ObjectCollection(this);
			SetStyle(ControlStyles.UserPaint | ControlStyles.UseTextForAccessibility | ControlStyles.StandardClick, false);
			SetStyle(ControlStyles.FixedHeight, true);
		}

		protected override Size DefaultSize => new Size(121, 21);

		public override Color BackColor
		{
			get => HasOwnBackColor ? base.BackColor : Color.FromArgb(255, 255, 255);
			set => base.BackColor = value;
		}

		public override Color ForeColor
		{
			get => HasOwnForeColor ? base.ForeColor : Color.FromArgb(0, 0, 0);
			set => base.ForeColor = value;
		}

		internal ListItems Store => store;

		internal override int ItemCount => DataSource is IList bound ? bound.Count : store.Count;

		internal override object ItemAt(int index) => DataSource is IList bound ? bound[index] : store[index];

		internal override void DataSourceAssigned(object source)
		{
			if (source is IListSource listSource)
			{
				source = listSource.GetList();
			}
			selectedIndex = -1;
			if (source is IList list)
			{
				store.ReplaceAll(list);
			}
			else
			{
				store.Clear();
			}
			Notify("Items");
		}

		public ObjectCollection Items => items;

		public ComboBoxStyle DropDownStyle
		{
			get => dropDownStyle;
			set
			{
				if (!Enum.IsDefined(typeof(ComboBoxStyle), value))
				{
					throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ComboBoxStyle));
				}
				if (dropDownStyle != value)
				{
					dropDownStyle = value;
					Notify("DropDownStyle");
					OnDropDownStyleChanged(EventArgs.Empty);
				}
			}
		}

		public bool Sorted
		{
			get => sorted;
			set
			{
				if (sorted != value)
				{
					sorted = value;
					if (value)
					{
						object[] all = store.ToArray();
						Array.Sort(all, (a, b) => string.Compare(GetItemText(a), GetItemText(b), StringComparison.CurrentCulture));
						store.ReplaceAll(all);
						selectedIndex = -1;
						Notify("Items");
					}
				}
			}
		}

		public bool DroppedDown
		{
			get => droppedDown;
			set
			{
				if (droppedDown != value)
				{
					droppedDown = value;
					Notify("DroppedDown");
					if (value)
					{
						OnDropDown(EventArgs.Empty);
					}
					else
					{
						OnDropDownClosed(EventArgs.Empty);
					}
				}
			}
		}

		public bool IntegralHeight
		{
			get => integralHeight;
			set => integralHeight = value;
		}

		public int DropDownHeight
		{
			get => dropDownHeight;
			set => dropDownHeight = value;
		}

		public int DropDownWidth
		{
			get => dropDownWidth == 0 ? Width : dropDownWidth;
			set => dropDownWidth = value;
		}

		public int MaxDropDownItems
		{
			get => maxDropDownItems;
			set => maxDropDownItems = value;
		}

		public int MaxLength
		{
			get => maxLength;
			set => maxLength = Math.Max(0, value);
		}

		public int ItemHeight
		{
			get => itemHeight;
			set => itemHeight = value;
		}

		public FlatStyle FlatStyle
		{
			get => flatStyle;
			set => flatStyle = value;
		}

		public DrawMode DrawMode
		{
			get => drawMode;
			set => drawMode = value;
		}

		public AutoCompleteMode AutoCompleteMode
		{
			get => autoCompleteMode;
			set => autoCompleteMode = value;
		}

		public AutoCompleteSource AutoCompleteSource
		{
			get => autoCompleteSource;
			set => autoCompleteSource = value;
		}

		/// <summary>A drop-down list shows the text of the selected item; the text of a combo box with an edit field is its own. Setting the text selects the item with the same text.</summary>
		public override string Text
		{
			get => base.Text;
			set
			{
				value = value ?? string.Empty;
				if (value == base.Text)
				{
					return;
				}
				int index = FindStringExact(value);
				if (index != -1 && index != selectedIndex)
				{
					SelectedIndex = index;
					return;
				}
				if (dropDownStyle != ComboBoxStyle.DropDownList)
				{
					base.Text = value;
					selectionStart = 0;
					selectionLength = 0;
					if (selectedIndex != -1)
					{
						selectedIndex = -1;
						OnSelectedItemChanged(EventArgs.Empty);
						OnSelectedIndexChanged(EventArgs.Empty);
					}
				}
			}
		}

		public override int SelectedIndex
		{
			get => selectedIndex;
			set
			{
				int count = ItemCount;
				if (value < -1 || value >= count)
				{
					throw new ArgumentOutOfRangeException(nameof(value), value, "Index was out of range. Must be non-negative and less than the size of the collection.");
				}
				if (selectedIndex == value)
				{
					return;
				}
				selectedIndex = value;
				Notify("SelectedIndex");
				UpdateText();
				OnSelectedItemChanged(EventArgs.Empty);
				OnSelectedIndexChanged(EventArgs.Empty);
			}
		}

		private void UpdateText()
		{
			string text = selectedIndex == -1 ? (dropDownStyle == ComboBoxStyle.DropDownList ? string.Empty : base.Text) : GetItemText(ItemAt(selectedIndex));
			if (selectedIndex == -1 && dropDownStyle != ComboBoxStyle.DropDownList)
			{
				return;
			}
			base.Text = text;
			selectionStart = 0;
			selectionLength = 0;
		}

		public object SelectedItem
		{
			get => selectedIndex == -1 ? null : ItemAt(selectedIndex);
			set
			{
				if (value == null)
				{
					SelectedIndex = -1;
					return;
				}
				int count = ItemCount;
				for (int i = 0; i < count; i++)
				{
					if (Equals(ItemAt(i), value))
					{
						SelectedIndex = i;
						return;
					}
				}
			}
		}

		public int SelectionStart
		{
			get => selectionStart;
			set => Select(value, SelectionLength);
		}

		public int SelectionLength
		{
			get => selectionLength;
			set => Select(SelectionStart, value);
		}

		public string SelectedText
		{
			get
			{
				string text = Text;
				int start = Math.Min(selectionStart, text.Length);
				return text.Substring(start, Math.Min(selectionLength, text.Length - start));
			}
			set
			{
				string text = Text;
				int start = Math.Min(selectionStart, text.Length);
				int length = Math.Min(selectionLength, text.Length - start);
				Text = text.Substring(0, start) + (value ?? string.Empty) + text.Substring(start + length);
			}
		}

		public void Select(int start, int length)
		{
			if (start < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(start), start, "'" + start + "' is not a valid value for 'start'. 'start' must be greater than or equal to 0.");
			}
			selectionStart = Math.Min(start, Text.Length);
			selectionLength = Math.Max(0, Math.Min(length, Text.Length - selectionStart));
			Notify("Selection");
		}

		public void SelectAll()
		{
			Select(0, Text.Length);
		}

		internal void ItemsChanged(int removedIndex, int insertedIndex)
		{
			bool selectionLost = false;
			if (removedIndex == -2)
			{
				selectionLost = selectedIndex != -1;
				selectedIndex = -1;
			}
			else if (removedIndex >= 0)
			{
				if (removedIndex == selectedIndex)
				{
					selectedIndex = -1;
					selectionLost = true;
				}
				else if (removedIndex < selectedIndex)
				{
					selectedIndex--;
				}
			}
			else if (insertedIndex >= 0 && insertedIndex <= selectedIndex)
			{
				selectedIndex++;
			}
			Notify("Items");
			if (selectionLost)
			{
				if (dropDownStyle == ComboBoxStyle.DropDownList)
				{
					base.Text = string.Empty;
				}
				OnSelectedItemChanged(EventArgs.Empty);
				OnSelectedIndexChanged(EventArgs.Empty);
			}
		}

		public void BeginUpdate()
		{
			updating++;
		}

		public void EndUpdate()
		{
			if (updating > 0)
			{
				updating--;
			}
		}

		public int FindString(string s) => FindString(s, -1);

		public int FindString(string s, int startIndex) => Find(s, startIndex, exact: false);

		public int FindStringExact(string s) => FindStringExact(s, -1);

		public int FindStringExact(string s, int startIndex) => Find(s, startIndex, exact: true);

		private int Find(string s, int startIndex, bool exact)
		{
			if (s == null)
			{
				return -1;
			}
			int count = ItemCount;
			if (count == 0)
			{
				return -1;
			}
			if (startIndex < -1 || startIndex >= count)
			{
				throw new ArgumentOutOfRangeException(nameof(startIndex), startIndex, "Index was out of range. Must be non-negative and less than the size of the collection.");
			}
			for (int step = 1; step <= count; step++)
			{
				int i = (startIndex + step) % count;
				string text = GetItemText(ItemAt(i));
				if (exact ? string.Equals(text, s, StringComparison.OrdinalIgnoreCase) : text.StartsWith(s, StringComparison.OrdinalIgnoreCase))
				{
					return i;
				}
			}
			return -1;
		}

		public int GetItemHeight(int index) => itemHeight;

		protected virtual void AddItemsCore(object[] value)
		{
			foreach (object o in value)
			{
				store.Add(o);
			}
		}

		protected virtual void OnDrawItem(DrawItemEventArgs e)
		{
			DrawItem?.Invoke(this, e);
		}

		protected virtual void OnMeasureItem(MeasureItemEventArgs e)
		{
			MeasureItem?.Invoke(this, e);
		}

		protected virtual void OnDropDown(EventArgs e)
		{
			DropDown?.Invoke(this, e);
		}

		protected virtual void OnDropDownClosed(EventArgs e)
		{
			DropDownClosed?.Invoke(this, e);
		}

		protected virtual void OnDropDownStyleChanged(EventArgs e)
		{
			DropDownStyleChanged?.Invoke(this, e);
		}

		protected virtual void OnSelectedItemChanged(EventArgs e)
		{
		}

		protected virtual void OnSelectionChangeCommitted(EventArgs e)
		{
			SelectionChangeCommitted?.Invoke(this, e);
		}

		protected virtual void OnTextUpdate(EventArgs e)
		{
			TextUpdate?.Invoke(this, e);
		}

		protected override void OnSelectedIndexChanged(EventArgs e)
		{
			base.OnSelectedIndexChanged(e);
			SelectedIndexChanged?.Invoke(this, e);
		}

		protected override void RefreshItem(int index)
		{
			Notify("Items");
		}

		protected override void SetItemsCore(IList value)
		{
			selectedIndex = -1;
			store.ReplaceAll(value);
			Notify("Items");
		}

		/// <summary>The user picked an item in the drop-down: the index changes and SelectionChangeCommitted follows the change events.</summary>
		internal void UserSelectedIndex(int index)
		{
			SelectedIndex = index;
			OnSelectionChangeCommitted(EventArgs.Empty);
		}

		public event DrawItemEventHandler DrawItem;

		public event EventHandler DropDown;

		public event EventHandler DropDownClosed;

		public event EventHandler DropDownStyleChanged;

		public event MeasureItemEventHandler MeasureItem;

		public event EventHandler SelectedIndexChanged;

		public event EventHandler SelectionChangeCommitted;

		public event EventHandler TextUpdate;

		public partial class ObjectCollection : IList
		{
			private readonly ComboBox owner;

			public ObjectCollection(ComboBox owner)
			{
				this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
			}

			public int Count => owner.ItemCount;

			public bool IsReadOnly => false;

			public virtual object this[int index]
			{
				get
				{
					if (index < 0 || index >= Count)
					{
						throw new ArgumentOutOfRangeException(nameof(index), index, "Index was out of range. Must be non-negative and less than the size of the collection.");
					}
					return owner.ItemAt(index);
				}
				set
				{
					owner.store[index] = value;
					owner.Notify("Items");
				}
			}

			public int Add(object item)
			{
				CheckBound();
				int index = owner.store.Add(item);
				owner.ItemsChanged(-1, index);
				return index;
			}

			public void AddRange(params object[] items)
			{
				if (items == null)
				{
					throw new ArgumentNullException(nameof(items));
				}
				CheckBound();
				owner.BeginUpdate();
				try
				{
					foreach (object o in items)
					{
						int index = owner.store.Add(o);
						owner.ItemsChanged(-1, index);
					}
				}
				finally
				{
					owner.EndUpdate();
				}
			}

			public void Clear()
			{
				CheckBound();
				owner.store.Clear();
				owner.ItemsChanged(-2, -1);
				if (owner.dropDownStyle == ComboBoxStyle.DropDownList)
				{
					owner.UpdateText();
				}
			}

			public bool Contains(object value) => owner.store.Contains(value);

			public void CopyTo(object[] destination, int arrayIndex)
			{
				object[] all = owner.store.ToArray();
				Array.Copy(all, 0, destination, arrayIndex, all.Length);
			}

			public IEnumerator GetEnumerator() => owner.store.ToArray().GetEnumerator();

			public int IndexOf(object value) => owner.store.IndexOf(value);

			public void Insert(int index, object item)
			{
				CheckBound();
				owner.store.Insert(index, item);
				owner.ItemsChanged(-1, index);
			}

			public void Remove(object value)
			{
				int index = owner.store.IndexOf(value);
				if (index != -1)
				{
					RemoveAt(index);
				}
			}

			public void RemoveAt(int index)
			{
				CheckBound();
				owner.store.RemoveAt(index);
				owner.ItemsChanged(index, -1);
			}

			private void CheckBound()
			{
				if (owner.DataSource != null)
				{
					throw new ArgumentException("Cannot modify the Items collection when the DataSource property is set.");
				}
			}

			void ICollection.CopyTo(Array array, int index)
			{
				((ICollection)owner.store.ToArray()).CopyTo(array, index);
			}

			bool ICollection.IsSynchronized => false;

			object ICollection.SyncRoot => this;

			int IList.Add(object value) => Add(value);

			bool IList.IsFixedSize => false;
		}
	}

	public abstract partial class UpDownBase : ContainerControl
	{
		private bool readOnly;
		private bool interceptArrowKeys = true;
		private HorizontalAlignment textAlign = HorizontalAlignment.Left;
		private LeftRightAlignment upDownAlign = LeftRightAlignment.Right;
		private BorderStyle borderStyle = BorderStyle.Fixed3D;
		private string editText = string.Empty;
		private bool userEdit;
		private bool changingText;

		public UpDownBase()
		{
			SetStyle(ControlStyles.FixedHeight, true);
			SetStyle(ControlStyles.Selectable, true);
			TabStop = false;
		}

		internal override Size NonClientSize => UserControl.BorderSize(borderStyle);

		internal override Point ClientOffset => new Point(UserControl.BorderSize(borderStyle).Width / 2, UserControl.BorderSize(borderStyle).Height / 2);

		public override Color BackColor
		{
			get => HasOwnBackColor ? base.BackColor : Color.FromArgb(255, 255, 255);
			set => base.BackColor = value;
		}

		public override Color ForeColor
		{
			get => HasOwnForeColor ? base.ForeColor : Color.FromArgb(0, 0, 0);
			set => base.ForeColor = value;
		}

		public BorderStyle BorderStyle
		{
			get => borderStyle;
			set
			{
				if (borderStyle != value)
				{
					borderStyle = value;
					Notify("BorderStyle");
				}
			}
		}

		public bool ReadOnly
		{
			get => readOnly;
			set
			{
				readOnly = value;
				Notify("ReadOnly");
			}
		}

		public bool InterceptArrowKeys
		{
			get => interceptArrowKeys;
			set => interceptArrowKeys = value;
		}

		public HorizontalAlignment TextAlign
		{
			get => textAlign;
			set
			{
				textAlign = value;
				Notify("TextAlign");
			}
		}

		public LeftRightAlignment UpDownAlign
		{
			get => upDownAlign;
			set
			{
				upDownAlign = value;
				Notify("UpDownAlign");
			}
		}

		protected bool ChangingText
		{
			get => changingText;
			set => changingText = value;
		}

		protected bool UserEdit
		{
			get => userEdit;
			set => userEdit = value;
		}

		/// <summary>The text of the edit field. A script that sets it makes it a user edit that the next ValidateEditText parses.</summary>
		public override string Text
		{
			get => editText;
			set
			{
				value = value ?? string.Empty;
				if (value == editText)
				{
					return;
				}
				editText = value;
				if (changingText)
				{
					changingText = false;
				}
				else
				{
					userEdit = true;
				}
				Notify("Text");
				OnTextChanged(EventArgs.Empty);
			}
		}

		public void Select(int start, int length)
		{
			Notify("Selection");
		}

		public abstract void DownButton();

		public abstract void UpButton();

		protected abstract void UpdateEditText();

		protected virtual void ValidateEditText()
		{
		}

		protected virtual void OnChanged(object source, EventArgs e)
		{
		}

		/// <summary>The peer reports an edit of the field by the user.</summary>
		internal void UserChangedText(string text)
		{
			editText = text ?? string.Empty;
			userEdit = true;
			OnTextChanged(EventArgs.Empty);
		}

		/// <summary>The peer reports that the edit field lost the focus or Enter was pressed: the text is parsed and shown again.</summary>
		internal void CommitEdit()
		{
			ValidateEditText();
		}
	}

	public partial class NumericUpDown : UpDownBase, ISupportInitialize
	{
		private decimal currentValue;
		private decimal minimum;
		private decimal maximum = 100m;
		private decimal increment = 1m;
		private int decimalPlaces;
		private bool hexadecimal;
		private bool thousandsSeparator;
		private bool initializing;

		public NumericUpDown()
		{
			Size = new Size(120, 20);
			Text = "0";
			TextAlign = HorizontalAlignment.Left;
		}

		/// <summary>A new value is range checked, ValueChanged is raised, then the text of the edit field is formatted from it.</summary>
		public decimal Value
		{
			get
			{
				if (UserEdit)
				{
					ValidateEditText();
				}
				return currentValue;
			}
			set
			{
				if (value != currentValue)
				{
					if (!initializing && (value < minimum || value > maximum))
					{
						throw new ArgumentOutOfRangeException(nameof(Value), value, "Value of '" + value + "' is not valid for 'Value'. 'Value' should be between 'Minimum' and 'Maximum'.");
					}
					currentValue = value;
					Notify("Value");
					OnValueChanged(EventArgs.Empty);
					UpdateEditText();
				}
			}
		}

		public decimal Minimum
		{
			get => minimum;
			set
			{
				minimum = value;
				if (minimum > maximum)
				{
					maximum = minimum;
				}
				Value = Constrain(currentValue);
				Notify("Range");
			}
		}

		public decimal Maximum
		{
			get => maximum;
			set
			{
				maximum = value;
				if (minimum > maximum)
				{
					minimum = maximum;
				}
				Value = Constrain(currentValue);
				Notify("Range");
			}
		}

		public decimal Increment
		{
			get => increment;
			set
			{
				if (value < 0m)
				{
					throw new ArgumentOutOfRangeException(nameof(value), value, "Value of '" + value + "' is not valid for 'Increment'. 'Increment' must be greater than or equal to 0.");
				}
				increment = value;
			}
		}

		public int DecimalPlaces
		{
			get => decimalPlaces;
			set
			{
				if (value < 0 || value > 99)
				{
					throw new ArgumentOutOfRangeException(nameof(value), value, "Value of '" + value + "' is not valid for 'DecimalPlaces'. 'DecimalPlaces' should be between 0 and 99.");
				}
				decimalPlaces = value;
				UpdateEditText();
			}
		}

		public bool Hexadecimal
		{
			get => hexadecimal;
			set
			{
				hexadecimal = value;
				UpdateEditText();
			}
		}

		public bool ThousandsSeparator
		{
			get => thousandsSeparator;
			set
			{
				thousandsSeparator = value;
				UpdateEditText();
			}
		}

		private decimal Constrain(decimal value)
		{
			if (value < minimum)
			{
				value = minimum;
			}
			if (value > maximum)
			{
				value = maximum;
			}
			return value;
		}

		public override void UpButton()
		{
			if (UserEdit)
			{
				ParseEditText();
			}
			try
			{
				Value = Math.Min(maximum, checked(currentValue + increment));
			}
			catch (OverflowException)
			{
				Value = maximum;
			}
		}

		public override void DownButton()
		{
			if (UserEdit)
			{
				ParseEditText();
			}
			try
			{
				Value = Math.Max(minimum, checked(currentValue - increment));
			}
			catch (OverflowException)
			{
				Value = minimum;
			}
		}

		protected void ParseEditText()
		{
			try
			{
				string text = Text;
				if (!string.IsNullOrEmpty(text) && !(text.Length == 1 && text == "-"))
				{
					if (hexadecimal)
					{
						Value = Constrain(Convert.ToDecimal(Convert.ToInt32(text, 16)));
					}
					else
					{
						Value = Constrain(decimal.Parse(text, CultureInfo.CurrentCulture));
					}
				}
			}
			catch (Exception)
			{
				// text that is not a number keeps the old value; UpdateEditText shows it again
			}
			finally
			{
				UserEdit = false;
			}
		}

		protected override void ValidateEditText()
		{
			ParseEditText();
			UpdateEditText();
		}

		protected override void UpdateEditText()
		{
			if (initializing)
			{
				return;
			}
			if (UserEdit)
			{
				ParseEditText();
			}
			ChangingText = true;
			string formatted = hexadecimal ? ((long)currentValue).ToString("X", CultureInfo.InvariantCulture) : currentValue.ToString((thousandsSeparator ? "N" : "F") + decimalPlaces, CultureInfo.CurrentCulture);
			if (formatted == Text)
			{
				ChangingText = false;
			}
			Text = formatted;
			UserEdit = false;
		}

		public void BeginInit()
		{
			initializing = true;
		}

		public void EndInit()
		{
			initializing = false;
			Value = Constrain(currentValue);
			UpdateEditText();
		}

		protected virtual void OnValueChanged(EventArgs e)
		{
			ValueChanged?.Invoke(this, e);
		}

		public event EventHandler ValueChanged;
	}
}
