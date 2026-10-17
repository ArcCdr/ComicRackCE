using Xunit;

// The dispose tests count the live SKBitmaps of the whole process, so no other test may allocate bitmaps at the same time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
