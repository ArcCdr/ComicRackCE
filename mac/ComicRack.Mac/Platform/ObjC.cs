using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ComicRack.Mac.Platform
{
	/// <summary>
	/// The little of the Objective-C runtime that the native pieces need (<see cref="MacAlert"/>, <see cref="WindowTabbing"/>): classes and
	/// selectors by name, <c>objc_msgSend</c> for the argument shapes in use, and NSString conversion. On arm64 every shape is a plain call, so
	/// one extern per shape is enough. Everything here is for the macOS UI thread (the main thread); nothing allocates a window until asked.
	/// </summary>
	public static class ObjC
	{
		private const string Runtime = "/usr/lib/libobjc.A.dylib";
		private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
		private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";

		[DllImport(Runtime)]
		private static extern IntPtr objc_getClass(string name);

		[DllImport(Runtime)]
		private static extern IntPtr sel_registerName(string name);

		[DllImport(Runtime)]
		private static extern IntPtr class_getInstanceMethod(IntPtr cls, IntPtr selector);

		[DllImport(Runtime)]
		private static extern IntPtr class_getClassMethod(IntPtr cls, IntPtr selector);

		[DllImport(Runtime)]
		private static extern IntPtr objc_autoreleasePoolPush();

		[DllImport(Runtime)]
		private static extern void objc_autoreleasePoolPop(IntPtr pool);

		[DllImport("libSystem.dylib")]
		private static extern IntPtr dlopen(string path, int mode);

		[DllImport(Runtime, EntryPoint = "objc_msgSend")]
		private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

		[DllImport(Runtime, EntryPoint = "objc_msgSend")]
		private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);

		[DllImport(Runtime, EntryPoint = "objc_msgSend")]
		private static extern IntPtr Send(IntPtr receiver, IntPtr selector, long arg);

		[DllImport(Runtime, EntryPoint = "objc_msgSend")]
		private static extern IntPtr Send(IntPtr receiver, IntPtr selector, byte arg);

		[DllImport(Runtime, EntryPoint = "objc_msgSend")]
		private static extern byte SendByte(IntPtr receiver, IntPtr selector);

		[DllImport(Runtime, EntryPoint = "objc_msgSend")]
		private static extern long SendLong(IntPtr receiver, IntPtr selector);

		private static bool loaded;

		/// <summary>True on macOS, where the runtime exists.</summary>
		public static bool IsAvailable => OperatingSystem.IsMacOS();

		/// <summary>Loads AppKit and Foundation once (the classes are not known to the runtime before that).</summary>
		public static void EnsureFrameworks()
		{
			if (loaded)
			{
				return;
			}
			dlopen(Foundation, 2);
			dlopen(AppKit, 2);
			loaded = true;
		}

		public static IntPtr Class(string name)
		{
			EnsureFrameworks();
			return objc_getClass(name);
		}

		public static IntPtr Selector(string name) => sel_registerName(name);

		/// <summary>
		/// An autorelease pool for the objects that the calls in a <c>using</c> block make and autorelease (strings from <see cref="NSString"/>, the buttons
		/// of an alert). Dispose it on the thread that made it. Where there is no Objective-C runtime it does nothing.
		/// </summary>
		public static IDisposable AutoreleasePool() => new Pool(IsAvailable ? objc_autoreleasePoolPush() : IntPtr.Zero);

		private sealed class Pool : IDisposable
		{
			private IntPtr handle;

			public Pool(IntPtr handle) => this.handle = handle;

			public void Dispose()
			{
				IntPtr h = handle;
				handle = IntPtr.Zero;
				if (h != IntPtr.Zero)
				{
					objc_autoreleasePoolPop(h);
				}
			}
		}

		/// <summary>True if the class has an instance method with the selector (the check that needs no object, hence no window).</summary>
		public static bool HasInstanceMethod(string className, string selector)
		{
			IntPtr cls = Class(className);
			return cls != IntPtr.Zero && class_getInstanceMethod(cls, Selector(selector)) != IntPtr.Zero;
		}

		/// <summary>True if the class has a class method with the selector.</summary>
		public static bool HasClassMethod(string className, string selector)
		{
			IntPtr cls = Class(className);
			return cls != IntPtr.Zero && class_getClassMethod(cls, Selector(selector)) != IntPtr.Zero;
		}

		public static IntPtr Call(IntPtr receiver, string selector) => Send(receiver, Selector(selector));

		public static IntPtr Call(IntPtr receiver, string selector, IntPtr arg) => Send(receiver, Selector(selector), arg);

		public static IntPtr Call(IntPtr receiver, string selector, long arg) => Send(receiver, Selector(selector), arg);

		/// <summary>Calls a method taking one <c>BOOL</c>.</summary>
		public static void CallBool(IntPtr receiver, string selector, bool arg) => Send(receiver, Selector(selector), (byte)(arg ? 1 : 0));

		/// <summary>Calls a method returning <c>BOOL</c>.</summary>
		public static bool CallReturnsBool(IntPtr receiver, string selector) => SendByte(receiver, Selector(selector)) != 0;

		public static long CallReturnsLong(IntPtr receiver, string selector) => SendLong(receiver, Selector(selector));

		/// <summary>An autoreleased NSString.</summary>
		public static IntPtr NSString(string text)
		{
			byte[] bytes = Encoding.UTF8.GetBytes((text ?? string.Empty) + "\0");
			GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
			try
			{
				return Send(Class("NSString"), Selector("stringWithUTF8String:"), pin.AddrOfPinnedObject());
			}
			finally
			{
				pin.Free();
			}
		}

		/// <summary>The text of an NSString (empty for nil).</summary>
		public static string ToManaged(IntPtr nsString)
		{
			if (nsString == IntPtr.Zero)
			{
				return string.Empty;
			}
			IntPtr utf8 = Send(nsString, Selector("UTF8String"));
			return utf8 == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(utf8);
		}
	}
}
