using System.Runtime.CompilerServices;

// The peers of the script UI layer (P4.10a) and the tests drive the facade through its internal entry points (a click, typed text, a closed window).
[assembly: InternalsVisibleTo("ComicRack.Mac.FormsBackend")]
[assembly: InternalsVisibleTo("Compat.Forms.Tests")]
