using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Threading;

namespace System.Windows.Forms
{
	public enum ComboBoxStyle
	{
		Simple = 0,
		DropDown = 1,
		DropDownList = 2
	}

	/// <summary>Application members that scripts use. Exceptions of event handlers go to ThreadException (WinForms shows its own dialog when nobody listens).</summary>
	public sealed class Application
	{
		private Application()
		{
		}

		public static event ThreadExceptionEventHandler ThreadException;

		/// <summary>Exceptions that no ThreadException handler took (the host logs them; tests read them).</summary>
		public static readonly List<Exception> Unhandled = new List<Exception>();

		public static void OnThreadException(Exception e)
		{
			ThreadExceptionEventHandler handler = ThreadException;
			if (handler != null)
			{
				try
				{
					handler(null, new ThreadExceptionEventArgs(e));
					return;
				}
				catch (Exception inner)
				{
					e = inner;
				}
			}
			lock (Unhandled)
			{
				Unhandled.Add(e);
			}
			Console.Error.WriteLine("[facade] unhandled exception in a UI event handler: " + e.GetType().Name + ": " + e.Message);
		}

		/// <summary>Pumps the queue once (UI thread) and returns; off the UI thread it does nothing.</summary>
		public static void DoEvents()
		{
			if (FormsRuntime.HasBackend)
			{
				FormsRuntime.Backend.DoEvents();
			}
		}

		public static void EnableVisualStyles()
		{
		}

		public static void SetCompatibleTextRenderingDefault(bool defaultValue)
		{
		}

		public static string ProductVersion => "0.9.184";
	}

	/// <summary>System.Windows.Forms.Timer: ticks on the UI thread.</summary>
	public class Timer : Component
	{
		private ITimerPeer peer;
		private int interval = 100;
		private bool enabled;

		public int Interval
		{
			get => interval;
			set
			{
				interval = value;
				if (peer != null)
				{
					peer.Interval = value;
				}
			}
		}

		public bool Enabled
		{
			get => enabled;
			set
			{
				if (value)
				{
					Start();
				}
				else
				{
					Stop();
				}
			}
		}

		public event EventHandler Tick;

		public void Start()
		{
			enabled = true;
			IFormsBackend backend = FormsRuntime.Backend;
			backend.Invoke(() =>
			{
				if (peer == null)
				{
					peer = backend.CreateTimer(() => FormsRuntime.Guard(() => OnTick(EventArgs.Empty)));
				}
				peer.Interval = interval;
				peer.Start();
			});
		}

		public void Stop()
		{
			enabled = false;
			ITimerPeer p = peer;
			if (p != null)
			{
				FormsRuntime.Backend.Invoke(p.Stop);
			}
		}

		protected virtual void OnTick(EventArgs e)
		{
			Tick?.Invoke(this, e);
		}

		protected override void Dispose(bool disposing)
		{
			ITimerPeer p = peer;
			peer = null;
			if (p != null && FormsRuntime.HasBackend)
			{
				FormsRuntime.Backend.Invoke(p.Dispose);
			}
			base.Dispose(disposing);
		}
	}

	public class Panel : Control
	{
	}

	public class Label : Control
	{
		protected override bool WantsMeasuredSize => true;
	}

	public class ButtonBase : Control
	{
	}

	public class Button : ButtonBase, IButtonControl
	{
		public DialogResult DialogResult
		{
			get;
			set;
		}

		/// <summary>Click, as the user does it: the Click event, then the owning form closes with this button's DialogResult.</summary>
		public void PerformClick()
		{
			if (!Enabled)
			{
				return;
			}
			PeerClick();
			if (DialogResult != DialogResult.None)
			{
				Form form = FindForm();
				if (form != null)
				{
					form.DialogResult = DialogResult;
				}
			}
		}
	}

	public class CheckBox : ButtonBase
	{
		private bool isChecked;

		protected override bool WantsMeasuredSize => true;

		public bool Checked
		{
			get => isChecked;
			set
			{
				if (isChecked != value)
				{
					isChecked = value;
					Changed("Checked");
					FormsRuntime.Guard(() => OnCheckedChanged(EventArgs.Empty));
				}
			}
		}

		public event EventHandler CheckedChanged;

		protected virtual void OnCheckedChanged(EventArgs e)
		{
			CheckedChanged?.Invoke(this, e);
		}

		public override void PeerStateChanged(string property, object value)
		{
			if (property == "Checked")
			{
				Checked = (bool)value;
				return;
			}
			base.PeerStateChanged(property, value);
		}
	}

	public class TextBox : Control
	{
		public bool Multiline
		{
			get;
			set;
		}

		public bool ReadOnly
		{
			get;
			set;
		}

		public char PasswordChar
		{
			get;
			set;
		}

		public HorizontalAlignment TextAlign
		{
			get;
			set;
		}
	}

	/// <summary>The items of a list control. The nested subclasses carry the WinForms names (ListBox.ObjectCollection, ComboBox.ObjectCollection).</summary>
	public class ItemCollection : IEnumerable
	{
		private readonly List<object> items = new List<object>();
		private readonly Action changed;

		internal ItemCollection(Action changed)
		{
			this.changed = changed;
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
					return items[index];
				}
			}
		}

		public int Add(object item)
		{
			int index;
			lock (items)
			{
				items.Add(item);
				index = items.Count - 1;
			}
			changed();
			return index;
		}

		public void AddRange(object[] values)
		{
			lock (items)
			{
				items.AddRange(values);
			}
			changed();
		}

		public void Clear()
		{
			lock (items)
			{
				items.Clear();
			}
			changed();
		}

		public IEnumerator GetEnumerator()
		{
			lock (items)
			{
				return items.ToArray().GetEnumerator();
			}
		}
	}

	public class ListControl : Control
	{
		private int selectedIndex = -1;

		public event EventHandler SelectedIndexChanged;

		public virtual int SelectedIndex
		{
			get => selectedIndex;
			set
			{
				if (selectedIndex != value)
				{
					selectedIndex = value;
					Changed("SelectedIndex");
					FormsRuntime.Guard(() => OnSelectedIndexChanged(EventArgs.Empty));
				}
			}
		}

		public object SelectedItem
		{
			get
			{
				ItemCollection items = GetItems();
				return selectedIndex >= 0 && selectedIndex < items.Count ? items[selectedIndex] : null;
			}
		}

		internal virtual ItemCollection GetItems() => throw new NotSupportedException();

		protected virtual void OnSelectedIndexChanged(EventArgs e)
		{
			SelectedIndexChanged?.Invoke(this, e);
		}

		public override void PeerStateChanged(string property, object value)
		{
			if (property == "SelectedIndex")
			{
				SelectedIndex = (int)value;
				return;
			}
			base.PeerStateChanged(property, value);
		}
	}

	public class ComboBox : ListControl
	{
		private readonly ObjectCollection items;

		public ComboBox()
		{
			items = new ObjectCollection(() => Changed("Items"));
		}

		public ComboBoxStyle DropDownStyle
		{
			get;
			set;
		} = ComboBoxStyle.DropDown;

		public bool FormattingEnabled
		{
			get;
			set;
		}

		public ObjectCollection Items => items;

		internal override ItemCollection GetItems() => items;

		public class ObjectCollection : ItemCollection
		{
			internal ObjectCollection(Action changed)
				: base(changed)
			{
			}
		}
	}

	public class ListBox : ListControl
	{
		private readonly ObjectCollection items;

		public ListBox()
		{
			items = new ObjectCollection(() => Changed("Items"));
		}

		public bool IntegralHeight
		{
			get;
			set;
		} = true;

		public ObjectCollection Items => items;

		internal override ItemCollection GetItems() => items;

		public class ObjectCollection : ItemCollection
		{
			internal ObjectCollection(Action changed)
				: base(changed)
			{
			}
		}
	}

	public class NumericUpDown : Control
	{
		private decimal value;
		private decimal minimum;
		private decimal maximum = 100m;

		public decimal Value
		{
			get => value;
			set
			{
				if (value < minimum || value > maximum)
				{
					throw new ArgumentOutOfRangeException(nameof(value), "Value of " + value + " is not valid for 'Value'. 'Value' should be between 'Minimum' and 'Maximum'.");
				}
				if (this.value != value)
				{
					this.value = value;
					Changed("Value");
					FormsRuntime.Guard(() => OnValueChanged(EventArgs.Empty));
				}
			}
		}

		public decimal Minimum
		{
			get => minimum;
			set
			{
				minimum = value;
				if (this.value < value)
				{
					Value = value;
				}
				Changed("Range");
			}
		}

		public decimal Maximum
		{
			get => maximum;
			set
			{
				maximum = value;
				if (this.value > value)
				{
					Value = value;
				}
				Changed("Range");
			}
		}

		public decimal Increment
		{
			get;
			set;
		} = 1m;

		public int DecimalPlaces
		{
			get;
			set;
		}

		public HorizontalAlignment TextAlign
		{
			get;
			set;
		}

		public event EventHandler ValueChanged;

		protected virtual void OnValueChanged(EventArgs e)
		{
			ValueChanged?.Invoke(this, e);
		}

		public override void PeerStateChanged(string property, object newValue)
		{
			if (property == "Value")
			{
				decimal d = (decimal)newValue;
				if (d >= minimum && d <= maximum)
				{
					Value = d;
				}
				return;
			}
			base.PeerStateChanged(property, newValue);
		}
	}
}
