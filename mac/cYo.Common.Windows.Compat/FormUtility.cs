using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace cYo.Common.Windows.Forms
{
	/// <summary>
	/// The members of <c>cYo.Common.Windows.Forms.FormUtility</c> (cYo.Common.Windows/Forms/FormUtility.cs of the Windows program) that scripts and plug-ins reach without a WinForms handle:
	/// the service lookup of the active window and the DPI scale. Signatures and behaviour are those of the original. The other members (panels and list views filled from options, form positions,
	/// tool strip helpers) need controls that the facades implement with P4.9 and later; they are added there.
	/// </summary>
	public static class FormUtility
	{
		/// <summary>Maps a control (or any object) to the object that implements its services; <see cref="FindActiveService(Control, Type)"/> asks the mapped object.</summary>
		public static readonly Dictionary<object, object> ServiceTranslation = new Dictionary<object, object>();

		/// <summary>The scale from 96 dpi to the screen: always 1, because the facades measure in points of the logical (scaled) coordinate space of the window.</summary>
		public static PointF DpiScale => new PointF(1f, 1f);

		public static object FindActiveService(this Control root, Type service)
		{
			if (service != null && root != null)
			{
				ContainerControl containerControl = root as ContainerControl;
				if (containerControl != null)
				{
					Control activeControl = containerControl.ActiveControl;
					if (activeControl != null && activeControl.Visible)
					{
						object obj = activeControl.FindActiveService(service);
						if (obj != null)
						{
							return obj;
						}
					}
				}
				if (service.IsInstanceOfType(GetServiceObject(root)))
				{
					return GetServiceObject(root);
				}
				foreach (Control control in root.Controls)
				{
					if (control != null && control.Visible)
					{
						object obj2 = control.FindActiveService(service);
						if (obj2 != null)
						{
							return obj2;
						}
					}
				}
			}
			return null;
		}

		public static T FindActiveService<T>(this Control root) where T : class
		{
			return (T)root.FindActiveService(typeof(T));
		}

		public static K InvokeActiveService<T, K>(this Control root, Func<T, K> predicate, K defaultReturn = default(K)) where T : class
		{
			T val = root.FindActiveService<T>();
			if (val != null)
			{
				return predicate(val);
			}
			return defaultReturn;
		}

		public static void InvokeActiveService<T>(this Control root, Action<T> action) where T : class
		{
			T val = root.FindActiveService<T>();
			if (val != null)
			{
				action(val);
			}
		}

		public static IEnumerable<T> FindServices<T>(this Control root) where T : class
		{
			T val = GetServiceObject(root) as T;
			if (val != null)
			{
				yield return val;
			}
			foreach (Control control in root.Controls)
			{
				foreach (T item in control.FindServices<T>())
				{
					yield return item;
				}
			}
		}

		public static T FindFirstService<T>(this Control root) where T : class
		{
			return root.FindServices<T>().FirstOrDefault();
		}

		/// <summary>
		/// The service of type <typeparamref name="T"/> of the active window (the original starts at <c>Form.ActiveForm</c>). The program is asked first (<see cref="ActiveServices"/>);
		/// when it has no answer, the control tree of the active facade form is searched as on Windows.
		/// </summary>
		public static T FindActiveService<T>() where T : class
		{
			T fromProgram = ActiveServices.Resolve(typeof(T)) as T;
			if (fromProgram != null)
			{
				return fromProgram;
			}
			return Form.ActiveForm.FindActiveService<T>();
		}

		public static T FindParentService<T>(this Control c) where T : class
		{
			while ((c = c.Parent) != null)
			{
				T val = c as T;
				if (val != null)
				{
					return val;
				}
			}
			return null;
		}

		private static object GetServiceObject(object obj)
		{
			if (!ServiceTranslation.TryGetValue(obj, out var value))
			{
				return obj;
			}
			return value;
		}

		public static Size ScaleDpi(this Size size)
		{
			return new Size((int)((float)size.Width * DpiScale.X), (int)((float)size.Height * DpiScale.Y));
		}

		public static Point ScaleDpi(this Point pt)
		{
			return new Point((int)((float)pt.X * DpiScale.X), (int)((float)pt.Y * DpiScale.Y));
		}

		public static Padding ScaleDpi(this Padding pd)
		{
			pd.Left = ScaleDpiX(pd.Left);
			pd.Right = ScaleDpiX(pd.Right);
			pd.Top = ScaleDpiY(pd.Top);
			pd.Bottom = ScaleDpiY(pd.Bottom);
			return pd;
		}

		public static RectangleF ScaleDpi(this RectangleF rect)
		{
			rect.X = ScaleDpiX(rect.X);
			rect.Width = ScaleDpiX(rect.Width);
			rect.Y = ScaleDpiX(rect.Y);
			rect.Height = ScaleDpiY(rect.Height);
			return rect;
		}

		public static float ScaleDpiX(float v)
		{
			return v * DpiScale.X;
		}

		public static float ScaleDpiY(float v)
		{
			return v * DpiScale.Y;
		}

		public static int ScaleDpiX(int v)
		{
			return (int)((float)v * DpiScale.X);
		}

		public static int ScaleDpiY(int v)
		{
			return (int)((float)v * DpiScale.Y);
		}

		public static Rectangle ScaleDpi(this Rectangle rect)
		{
			return new Rectangle(ScaleDpiX(rect.X), ScaleDpiY(rect.Y), ScaleDpiX(rect.Width), ScaleDpiY(rect.Height));
		}
	}
}
