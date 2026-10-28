using Xunit;

// FileSystemServices is process-wide, like the shell it replaces: the tests change it one after the other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
