using System;
using System.Collections.Generic;

namespace cYo.Common.Windows.Forms
{
	/// <summary>
	/// Where the program tells <see cref="FormUtility.FindActiveService{T}()"/> which services its active window offers. On Windows the lookup walks the controls of the active form
	/// (<c>IRefreshDisplay</c> is implemented by the browser views, <c>ILibraryBrowser</c> by the main form); the macOS program keeps no WinForms control tree for its own
	/// windows, so it answers the same question here. Scripts never see this type.
	/// </summary>
	public static class ActiveServices
	{
		private static readonly object gate = new object();
		private static Func<Type, object> resolver;

		/// <summary>The program's answer for "the service of this type in the active window", or null when it has none. Null removes the answer.</summary>
		public static Func<Type, object> Resolver
		{
			get { lock (gate) { return resolver; } }
			set { lock (gate) { resolver = value; } }
		}

		/// <summary>Asks <see cref="Resolver"/>; null when it is not set or knows no such service.</summary>
		public static object Resolve(Type service)
		{
			Func<Type, object> r = Resolver;
			object found = service == null ? null : r?.Invoke(service);
			return found != null && service.IsInstanceOfType(found) ? found : null;
		}
	}
}
