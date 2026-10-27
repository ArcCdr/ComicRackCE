using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows.Forms.Layout;
using cYo.Compat.Forms;

namespace System.Windows.Forms.Layout
{
	/// <summary>The list behind <see cref="Control.ControlCollection"/>. Thread safe: every access to the list is under its own lock, enumerations run over a copy.</summary>
	public partial class ArrangedElementCollection : IList
	{
		internal readonly List<object> Items = new List<object>();

		public virtual int Count
		{
			get
			{
				lock (Items)
				{
					return Items.Count;
				}
			}
		}

		public virtual bool IsReadOnly => false;

		public void CopyTo(Array array, int index)
		{
			lock (Items)
			{
				((ICollection)Items).CopyTo(array, index);
			}
		}

		public virtual IEnumerator GetEnumerator()
		{
			lock (Items)
			{
				return Items.ToArray().GetEnumerator();
			}
		}

		bool ICollection.IsSynchronized => false;

		object ICollection.SyncRoot => Items;

		int IList.Add(object value) => throw new NotSupportedException();

		void IList.Clear() => throw new NotSupportedException();

		bool IList.Contains(object value)
		{
			lock (Items)
			{
				return Items.Contains(value);
			}
		}

		int IList.IndexOf(object value)
		{
			lock (Items)
			{
				return Items.IndexOf(value);
			}
		}

		void IList.Insert(int index, object value) => throw new NotSupportedException();

		bool IList.IsFixedSize => false;

		void IList.Remove(object value) => throw new NotSupportedException();

		void IList.RemoveAt(int index) => throw new NotSupportedException();

		object IList.this[int index]
		{
			get
			{
				lock (Items)
				{
					return Items[index];
				}
			}
			set => throw new NotSupportedException();
		}
	}
}

namespace System.Windows.Forms
{
	public partial class Control
	{
		/// <summary>
		/// The children of a control, front to back (index 0 is the control at the front of the z-order). Add, Remove and SetChildIndex raise the events of dotnet/winforms in its order:
		/// ParentChanged of the child, then ControlAdded or ControlRemoved of the owner.
		/// </summary>
		public partial class ControlCollection : ArrangedElementCollection, ICloneable
		{
			private readonly Control owner;

			public ControlCollection(Control owner)
			{
				this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
			}

			public Control Owner => owner;

			public virtual Control this[int index]
			{
				get
				{
					lock (Items)
					{
						if (index < 0 || index >= Items.Count)
						{
							throw new ArgumentOutOfRangeException(nameof(index), index, "Index was out of range. Must be non-negative and less than the size of the collection.");
						}
						return (Control)Items[index];
					}
				}
			}

			public virtual Control this[string key]
			{
				get
				{
					if (string.IsNullOrEmpty(key))
					{
						return null;
					}
					int index = IndexOfKey(key);
					return index >= 0 ? this[index] : null;
				}
			}

			internal Control[] Snapshot()
			{
				lock (Items)
				{
					var result = new Control[Items.Count];
					for (int i = 0; i < result.Length; i++)
					{
						result[i] = (Control)Items[i];
					}
					return result;
				}
			}

			public virtual void Add(Control value)
			{
				if (value == null)
				{
					return;
				}
				if (value.GetTopLevel())
				{
					throw new ArgumentException("Top-level control cannot be added to a control.");
				}
				if (value == owner || value.Contains(owner))
				{
					throw new ArgumentException("A circular control reference has been made. A control cannot be owned by or parented to itself.");
				}
				if (value.parent == owner)
				{
					value.SendToBack();
					return;
				}
				value.parent?.Controls.Remove(value);
				lock (Items)
				{
					Items.Add(value);
					if (value.tabIndex == -1)
					{
						int next = 0;
						for (int i = 0; i < Items.Count - 1; i++)
						{
							int t = ((Control)Items[i]).TabIndex;
							if (next <= t)
							{
								next = t + 1;
							}
						}
						value.tabIndex = next;
					}
				}
				owner.SuspendLayout();
				try
				{
					value.AssignParent(owner);
				}
				finally
				{
					owner.ResumeLayout(false);
				}
				value.InitLayout();
				if (owner.created && value.Visible)
				{
					value.CreateControl(false);
				}
				owner.Notify("Controls");
				owner.PerformLayout(value, "Parent");
				owner.OnControlAdded(new ControlEventArgs(value));
				ChildrenChanged();
			}

			public virtual void AddRange(params Control[] controls)
			{
				if (controls == null)
				{
					throw new ArgumentNullException(nameof(controls));
				}
				if (controls.Length == 0)
				{
					return;
				}
				owner.SuspendLayout();
				try
				{
					foreach (Control c in controls)
					{
						Add(c);
					}
				}
				finally
				{
					owner.ResumeLayout(true);
				}
			}

			public virtual void Clear()
			{
				owner.SuspendLayout();
				try
				{
					while (Count != 0)
					{
						RemoveAt(Count - 1);
					}
				}
				finally
				{
					owner.ResumeLayout(true);
				}
			}

			public bool Contains(Control control)
			{
				lock (Items)
				{
					return Items.Contains(control);
				}
			}

			public virtual bool ContainsKey(string key) => IndexOfKey(key) >= 0;

			public Control[] Find(string key, bool searchAllChildren)
			{
				if (string.IsNullOrEmpty(key))
				{
					throw new ArgumentNullException(nameof(key), "Parameter 'key' cannot be empty.");
				}
				var found = new List<Control>();
				FindInternal(key, searchAllChildren, this, found);
				return found.ToArray();
			}

			private static void FindInternal(string key, bool searchAllChildren, ControlCollection collection, List<Control> into)
			{
				foreach (Control c in collection.Snapshot())
				{
					if (string.Equals(c.Name, key, StringComparison.OrdinalIgnoreCase))
					{
						into.Add(c);
					}
				}
				if (searchAllChildren)
				{
					foreach (Control c in collection.Snapshot())
					{
						if (c.controls != null)
						{
							FindInternal(key, searchAllChildren, c.controls, into);
						}
					}
				}
			}

			public int GetChildIndex(Control child) => GetChildIndex(child, true);

			public virtual int GetChildIndex(Control child, bool throwException)
			{
				int index = IndexOf(child);
				if (index == -1 && throwException)
				{
					throw new ArgumentException("The control must be a child of this control.", nameof(child));
				}
				return index;
			}

			public override IEnumerator GetEnumerator() => Snapshot().GetEnumerator();

			public int IndexOf(Control control)
			{
				lock (Items)
				{
					return Items.IndexOf(control);
				}
			}

			public virtual int IndexOfKey(string key)
			{
				if (string.IsNullOrEmpty(key))
				{
					return -1;
				}
				lock (Items)
				{
					for (int i = 0; i < Items.Count; i++)
					{
						if (string.Equals(((Control)Items[i]).Name, key, StringComparison.OrdinalIgnoreCase))
						{
							return i;
						}
					}
				}
				return -1;
			}

			public virtual void Remove(Control value)
			{
				if (value == null || value.parent != owner)
				{
					return;
				}
				lock (Items)
				{
					Items.Remove(value);
				}
				value.AssignParent(null);
				owner.Notify("Controls");
				owner.PerformLayout(value, "Parent");
				owner.OnControlRemoved(new ControlEventArgs(value));
				(owner.GetContainerControl() as ContainerControl)?.AfterControlRemoved(value, owner);
				ChildrenChanged();
			}

			public void RemoveAt(int index)
			{
				Remove(this[index]);
			}

			public virtual void RemoveByKey(string key)
			{
				int index = IndexOfKey(key);
				if (index >= 0)
				{
					RemoveAt(index);
				}
			}

			public virtual void SetChildIndex(Control child, int newIndex)
			{
				if (child == null)
				{
					throw new ArgumentNullException(nameof(child));
				}
				lock (Items)
				{
					int current = Items.IndexOf(child);
					if (current < 0)
					{
						throw new ArgumentException("The control must be a child of this control.", nameof(child));
					}
					if (current == newIndex)
					{
						return;
					}
					if (newIndex >= Items.Count || newIndex == -1)
					{
						newIndex = Items.Count - 1;
					}
					Items.RemoveAt(current);
					Items.Insert(newIndex, child);
				}
				child.UpdateZOrder();
				owner.PerformLayout(child, "ChildIndex");
				ChildrenChanged();
			}

			private void ChildrenChanged()
			{
				IControlPeer p = owner.peer;
				if (p == null)
				{
					return;
				}
				IFormsBackend backend = FormsRuntime.Backend;
				if (backend.CheckAccess())
				{
					FormsRuntime.Guard(p.ChildrenChanged);
				}
				else
				{
					backend.Post(() => FormsRuntime.Guard(p.ChildrenChanged));
				}
			}

			object ICloneable.Clone()
			{
				var clone = new ControlCollection(owner);
				lock (Items)
				{
					clone.Items.AddRange(Items);
				}
				return clone;
			}
		}
	}
}
