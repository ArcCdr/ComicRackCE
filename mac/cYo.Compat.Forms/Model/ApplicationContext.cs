using System;

// ApplicationContext and FormCollection (P4.9f): the context of Application.Run and the list Application.OpenForms. Both follow dotnet/winforms: the context ends the thread's loop
// when its main form's handle is destroyed (ExitThreadCore raises ThreadExit, which Application.Run listens to) and disposes the main form when the loop is over.
namespace System.Windows.Forms
{
	public partial class ApplicationContext : IDisposable
	{
		private Form mainForm;
		private object tag;

		public ApplicationContext()
			: this(null)
		{
		}

		public ApplicationContext(Form mainForm)
		{
			MainForm = mainForm;
		}

		/// <summary>The form whose closing ends the loop.</summary>
		public Form MainForm
		{
			get => mainForm;
			set
			{
				EventHandler onClose = OnMainFormDestroy;
				if (mainForm != null)
				{
					mainForm.HandleDestroyed -= onClose;
				}
				mainForm = value;
				if (mainForm != null)
				{
					mainForm.HandleDestroyed += onClose;
				}
			}
		}

		public object Tag
		{
			get => tag;
			set => tag = value;
		}

		/// <summary>Raised when the loop that this context runs is about to end.</summary>
		public event EventHandler ThreadExit;

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing && mainForm != null)
			{
				// the end of the loop is known already: disposing the form must not end it a second time
				mainForm.HandleDestroyed -= OnMainFormDestroy;
				if (!mainForm.IsDisposed)
				{
					mainForm.Dispose();
				}
				mainForm = null;
			}
		}

		public void ExitThread()
		{
			ExitThreadCore();
		}

		protected virtual void ExitThreadCore()
		{
			ThreadExit?.Invoke(this, EventArgs.Empty);
		}

		protected virtual void OnMainFormClosed(object sender, EventArgs e)
		{
			ExitThreadCore();
		}

		private void OnMainFormDestroy(object sender, EventArgs e)
		{
			var form = (Form)sender;
			if (!form.RecreatingHandle)
			{
				form.HandleDestroyed -= OnMainFormDestroy;
				OnMainFormClosed(sender, e);
			}
		}
	}

	/// <summary>The forms that are open (<see cref="Application.OpenForms"/>), in the order they were shown. It changes as forms open and close.</summary>
	public partial class FormCollection : System.Collections.ReadOnlyCollectionBase
	{
		public FormCollection()
		{
		}

		public virtual Form this[int index]
		{
			get
			{
				lock (InnerList.SyncRoot)
				{
					return (Form)InnerList[index];
				}
			}
		}

		public virtual Form this[string name]
		{
			get
			{
				if (name == null)
				{
					return null;
				}
				lock (InnerList.SyncRoot)
				{
					foreach (Form form in InnerList)
					{
						if (form != null && string.Equals(form.Name, name))
						{
							return form;
						}
					}
				}
				return null;
			}
		}

		internal void Add(Form form)
		{
			lock (InnerList.SyncRoot)
			{
				if (!InnerList.Contains(form))
				{
					InnerList.Add(form);
				}
			}
		}

		internal void Remove(Form form)
		{
			lock (InnerList.SyncRoot)
			{
				InnerList.Remove(form);
			}
		}

		internal void Clear()
		{
			lock (InnerList.SyncRoot)
			{
				InnerList.Clear();
			}
		}

		internal Form[] Snapshot()
		{
			lock (InnerList.SyncRoot)
			{
				var forms = new Form[InnerList.Count];
				InnerList.CopyTo(forms);
				return forms;
			}
		}
	}
}
