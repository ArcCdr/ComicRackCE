using System.Runtime.CompilerServices;

// The unit tests read internal state (live SKBitmap count, storage layout) that the public GDI+ surface does not expose.
[assembly: InternalsVisibleTo("Compat.Drawing.Tests")]
