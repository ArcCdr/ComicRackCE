using System;
using System.Collections.Generic;
using System.Linq;
using ComicRack.Mac.Host.Scripting.Scan;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>
	/// The scanner reads the facade assemblies with the same reader as the reference assemblies (P4.7): the catalog of the facade has the types and members of the reference catalog,
	/// and the tier tables name only types of that surface.
	/// </summary>
	public class FacadeCatalogTests
	{
		private static readonly Lazy<ApiCatalog> Reference = new Lazy<ApiCatalog>(() =>
		{
			string dir = ReferenceSurface.FindDirectory();
			if (dir == null)
			{
				throw new System.IO.FileNotFoundException("Microsoft.WindowsDesktop.App.Ref pack not found in the NuGet cache. Run a pass A build first: dotnet build mac/ComicRack.Plugins.Core/ComicRack.Plugins.Core.csproj -p:CensusPass=A");
			}
			return ApiCatalog.FromReferenceDirectory(dir);
		});

		private static readonly Lazy<ApiCatalog> Facade = new Lazy<ApiCatalog>(() => ApiCatalog.FromFacade(AppContext.BaseDirectory));

		private static string Describe(ApiMember m) => m.Kind + (m.IsStatic ? " static" : "") + (m.IsVirtual ? " virtual" : "") + (m.IsProtected ? " protected" : "") + " " + m.Type;

		[Fact]
		public void Every_type_of_the_facade_catalog_has_the_members_of_the_reference_catalog()
		{
			var differences = new List<string>();
			foreach (ApiType type in Facade.Value.Types.OrderBy(t => t.FullName, StringComparer.Ordinal))
			{
				ApiType reference = Reference.Value.Type(type.FullName);
				if (reference == null)
				{
					differences.Add(type.FullName + ": not in the reference catalog");
					continue;
				}
				if (reference.Kind != type.Kind || reference.BaseName != type.BaseName || reference.IsAbstract != type.IsAbstract)
				{
					differences.Add(type.FullName + ": kind, base or abstract differs (" + reference.Kind + " " + reference.BaseName + " / " + type.Kind + " " + type.BaseName + ")");
				}
				foreach (string name in reference.Members.Keys.Union(type.Members.Keys).OrderBy(n => n, StringComparer.Ordinal))
				{
					if (type.FullName == "System.Drawing.Font" && (name == "FromLogFont" || name == "ToLogFont"))
					{
						continue; // omitted on purpose (Generated/omitted.txt): they use the Win32 interop struct LOGFONT
					}
					if (name == "Finalize")
					{
						continue; // a finalizer cannot be declared as a method in C#
					}
					string expected = reference.Members.TryGetValue(name, out List<ApiMember> r) ? string.Join("; ", r.Select(Describe).OrderBy(d => d, StringComparer.Ordinal).Distinct()) : null;
					string actual = type.Members.TryGetValue(name, out List<ApiMember> a) ? string.Join("; ", a.Select(Describe).OrderBy(d => d, StringComparer.Ordinal).Distinct()) : null;
					// An override that the shim adds to release its own state (Bitmap.Dispose) is not API surface.
					bool addedOverride = expected == null && actual != null && type.Members[name].All(m => m.IsVirtual && m.IsProtected);
					if (expected != actual && !addedOverride)
					{
						differences.Add(type.FullName + "." + name + ": reference [" + expected + "] facade [" + actual + "]");
					}
				}
			}
			Assert.True(differences.Count == 0, differences.Count + " difference(s):\n" + string.Join("\n", differences.Take(25)));
		}

		[Fact]
		public void The_tier_tables_name_only_types_of_the_surface()
		{
			var missing = new List<string>();
			foreach (var (tier, name) in ApiTiers.Listed())
			{
				if (Reference.Value.Type("System.Windows.Forms", name) == null)
				{
					missing.Add(tier + " " + name);
				}
			}
			Assert.True(missing.Count == 0, "the tier tables list types that System.Windows.Forms does not have: " + string.Join(", ", missing.OrderBy(n => n, StringComparer.Ordinal)));
		}
	}
}
