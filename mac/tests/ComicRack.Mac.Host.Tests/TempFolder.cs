using System;
using System.IO;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>A new folder under the system temp folder, deleted on dispose.</summary>
	internal sealed class TempFolder : IDisposable
	{
		public string Path { get; }

		public TempFolder()
		{
			Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "crmac-host-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path);
		}

		public MacPaths Paths => MacPaths.Under(Path);

		public void Dispose()
		{
			try
			{
				Directory.Delete(Path, recursive: true);
			}
			catch (Exception)
			{
			}
			RemoveEmptyTestHostFolder();
		}

		// SystemPaths always creates <ApplicationData>/<company>/<product> of the entry assembly, here "Microsoft Corporation/testhost"
		// (see "Facts from M1.5a" in mvp-track.md). It stays empty; do not leave it in the user's Application Support folder.
		private static void RemoveEmptyTestHostFolder()
		{
			try
			{
				string product = cYo.Projects.ComicRack.Engine.SystemPaths.ProductName;
				string company = cYo.Projects.ComicRack.Engine.SystemPaths.CompanyName;
				if (product != "testhost" || string.IsNullOrEmpty(company))
				{
					return;
				}
				string companyFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), company);
				Directory.Delete(System.IO.Path.Combine(companyFolder, product)); // fails if not empty
				Directory.Delete(companyFolder);
			}
			catch (Exception)
			{
			}
		}
	}
}
