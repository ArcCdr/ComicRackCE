using System;
using System.Data.Common;

namespace cYo.Projects.ComicRack.Engine.Database.Storage
{
	// Replacement for ComicRack.Engine/Database/Storage/ComicStorageMsSql.cs (excluded: needs System.Data.SqlClient,
	// which is not part of .NET 10). SQL Server storage is not part of the macOS scope.
	public class ComicStorageMsSql : ComicStorageBaseSql
	{
		protected override DbConnection CreateConnection(string connection)
		{
			throw new NotSupportedException("cYo.Projects.ComicRack.Engine.Database.Storage.ComicStorageMsSql is not yet supported on macOS");
		}

		protected override bool CreateTables()
		{
			throw new NotSupportedException("cYo.Projects.ComicRack.Engine.Database.Storage.ComicStorageMsSql is not yet supported on macOS");
		}
	}
}
