using System;
using cYo.Projects.ComicRack.Engine.IO.Provider;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Writers
{
	// Replacement for ComicRack.Engine/IO/Provider/Writers/PdfStorageProvider.cs (excluded). The original passes a
	// System.Drawing.Bitmap to sharpPDF, a .NET Framework assembly whose System.Drawing.Image parameter is bound to the
	// framework's own System.Drawing type; the cYo.Compat.Drawing stand-in is a different assembly and cannot match it.
	// Exporting to PDF is not part of the macOS MVP.
	[FileFormat("PDF Document (PDF)", KnownFileFormats.PDF, ".pdf")]
	public class PdfStorageProvider : StorageProvider
	{
		protected override ComicInfo OnStore(IImageProvider provider, ComicInfo info, string target, StorageSetting setting)
		{
			throw new NotSupportedException("cYo.Projects.ComicRack.Engine.IO.Provider.Writers.PdfStorageProvider is not yet supported on macOS");
		}
	}
}
