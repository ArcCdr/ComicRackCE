using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using ComicRack.Mac.Host;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using Xunit;
using Xunit.Sdk;

// Every test of this assembly runs followed by these checks (see TestGuardAttribute).
[assembly: ComicRack.Mac.Tests.TestGuard]

namespace ComicRack.Mac.Tests
{
	/// <summary>
	/// Keeps the process-wide state of this test assembly deterministic, whatever order the tests run in. After every test:
	/// <list type="bullet">
	/// <item>A <see cref="MacHost"/> that the test left open is closed and the test fails, instead of the next test failing with
	/// "A MacHost is already open" (the Core allows one host per process).</item>
	/// <item>The live <see cref="PixelBlock"/> counter is allowed to return to what it was when the test began: a decode that is
	/// still finishing when the test ends (the reader cancels it, the pool thread releases its pixels a moment later) would
	/// otherwise show up in the baseline that the next test takes, and vanish during that test.</item>
	/// </list>
	/// </summary>
	[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
	public sealed class TestGuardAttribute : BeforeAfterTestAttribute
	{
		private static long liveAtStart;

		public override void Before(MethodInfo methodUnderTest)
		{
			liveAtStart = PixelBlock.LiveCount;
		}

		public override void After(MethodInfo methodUnderTest)
		{
			// MacHost keeps its single instance in a private static field; the test assembly has no other way to see it.
			FieldInfo field = typeof(MacHost).GetField("current", BindingFlags.NonPublic | BindingFlags.Static);
			MacHost leaked = field?.GetValue(null) as MacHost;
			if (leaked != null)
			{
				try
				{
					leaked.Dispose();
				}
				catch (Exception)
				{
					// the next tests must find a closed host; the leak itself is reported below
				}
				throw new InvalidOperationException(methodUnderTest.DeclaringType?.Name + "." + methodUnderTest.Name + " left a MacHost open.");
			}
			WaitForPixelBlocksToBeReleased();
		}

		/// <summary>
		/// Waits (at most 300 ms) while more pixel blocks are alive than when the test began: pool work that finishes after the test's
		/// last assertion, and garbage that only a finalizer releases. Costs nothing when the test left no excess. Some tests keep blocks
		/// on purpose until their class is disposed (after this check), so the wait is short. A test that really leaks still fails on its
		/// own assertion; this only makes the counters of the next test start from the same value.
		/// </summary>
		private static void WaitForPixelBlocksToBeReleased()
		{
			var watch = Stopwatch.StartNew();
			long nextFlush = 50;
			while (PixelBlock.LiveCount > liveAtStart && watch.ElapsedMilliseconds < 300)
			{
				if (watch.ElapsedMilliseconds >= nextFlush)
				{
					Gc.Flush();
					nextFlush += 50;
				}
				Thread.Sleep(5);
			}
		}
	}
}
