using Xunit;

// The host is a process-wide singleton (the Core keeps static state) and there is one Avalonia application per process.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
