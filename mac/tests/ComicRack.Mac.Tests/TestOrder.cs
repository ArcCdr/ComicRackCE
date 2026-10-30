using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

// The memory test of the fixture grid compares points of its own run, but it also reads the whole process, and the tests that ran before it
// leave managed memory behind (windows, libraries, caches: 0.2 to 2.9 GB at its start, depending on the order xunit picks for the classes,
// which changes from run to run). It runs first, so that it starts from a small heap, finishes in about a minute instead of swapping, and
// its numbers are the same in every run. The order of all other classes is not changed.
[assembly: TestCollectionOrderer("ComicRack.Mac.Tests.FixtureGridFirstOrderer", "ComicRack.Mac.Tests")]

namespace ComicRack.Mac.Tests
{
	public sealed class FixtureGridFirstOrderer : ITestCollectionOrderer
	{
		public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
		{
			return testCollections.OrderBy(c => c.DisplayName.EndsWith("." + nameof(Browser.FixtureGridTests)) ? 0 : 1).ToList();
		}
	}
}
