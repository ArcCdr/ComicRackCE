using Xunit;

// The host is a process-wide singleton (the Core keeps static state), so the tests must not run side by side.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
