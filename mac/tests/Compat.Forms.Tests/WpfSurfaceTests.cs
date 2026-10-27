using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Compat.Shared;
using Xunit;

namespace Compat.Forms.Tests
{
	/// <summary>The load context of the WPF drift guard (reading the reference assemblies takes a second).</summary>
	public sealed class WpfSurfaceFixture : IDisposable
	{
		public static readonly string[] Facades = { "System.Xaml", "PresentationCore", "PresentationFramework" };

		public static readonly string[] References = { "System.Xaml", "WindowsBase", "PresentationCore", "PresentationFramework" };

		private readonly string omittedFile = Path.Combine(Path.GetTempPath(), "wpf-omitted-" + Guid.NewGuid().ToString("N") + ".txt");

		internal SurfaceFixture Surface { get; }

		public static string FacadePath(string name) => Path.Combine(AppContext.BaseDirectory, name + ".dll");

		public WpfSurfaceFixture()
		{
			string reference = Path.GetDirectoryName(ReferencePack.Find());
			string mac = MacRoot.Find();
			File.WriteAllLines(omittedFile, new[] { "System.Xaml", "PresentationCore", "PresentationFramework" }
				.SelectMany(n => File.ReadAllLines(Path.Combine(mac, "cYo.Compat." + n.Replace("System.", ""), "Generated", "omitted.txt"))));
			Surface = new SurfaceFixture(reference, References, Facades.Select(FacadePath).ToArray(), omittedFile, References);
		}

		public void Dispose()
		{
			Surface.Dispose();
			File.Delete(omittedFile);
		}
	}

	/// <summary>
	/// P4.17.3: the facades of the Windows Presentation Foundation assemblies (System.Xaml, PresentationCore with the types of WindowsBase, PresentationFramework) stay the surface of the reference assemblies
	/// of the Windows Desktop pack, as the Forms facade does (<see cref="ShimSurfaceTests"/>), and carry the identity scripts bind to.
	/// </summary>
	public class WpfSurfaceTests : IClassFixture<WpfSurfaceFixture>
	{
		private readonly SurfaceFixture surface;

		public WpfSurfaceTests(WpfSurfaceFixture fixture)
		{
			surface = fixture.Surface;
		}

		private static string Report(IEnumerable<string> lines, string title)
		{
			var list = lines.ToList();
			return list.Count == 0 ? "" : title + " (" + list.Count + "):\n  " + string.Join("\n  ", list.Take(40)) + (list.Count > 40 ? "\n  ..." : "");
		}

		[Fact]
		public void The_facades_carry_the_names_and_the_version_scripts_bind_to()
		{
			foreach (string name in WpfSurfaceFixture.Facades)
			{
				AssemblyName identity = AssemblyName.GetAssemblyName(WpfSurfaceFixture.FacadePath(name));
				Assert.Equal(name, identity.Name);
				Assert.Equal(new Version(10, 0, 0, 0), identity.Version);
				Assert.Empty(identity.GetPublicKeyToken());
			}
			// the runtime's own WindowsBase.dll (4.0.0.0, type forwards) wins over a file of that name in the program folder, so no facade of that name is built
			Assert.False(File.Exists(WpfSurfaceFixture.FacadePath("WindowsBase")) && AssemblyName.GetAssemblyName(WpfSurfaceFixture.FacadePath("WindowsBase")).Version == new Version(10, 0, 0, 0));
		}

		[Fact]
		public void The_reference_has_the_surface_the_generator_covers()
		{
			// about 1900 types in the pack of this machine; a smaller figure means that the pack changed or was not found.
			Assert.True(surface.ReferenceTypes.Count > 1500, "only " + surface.ReferenceTypes.Count + " reference types");
			Assert.NotNull(surface.Facade("System.Windows.Markup.XamlReader"));
			Assert.NotNull(surface.Facade("System.Windows.Media.Imaging.BitmapImage"));
			Assert.NotNull(surface.Facade("System.Windows.Visibility"));
			Assert.NotNull(surface.Facade("System.Windows.Interop.WindowInteropHelper"));
		}

		[Fact]
		public void Every_visible_type_of_the_reference_is_declared()
		{
			string missing = Report(surface.ReferenceTypes.Where(t => surface.Facade(t.FullName) == null).Select(t => t.FullName), "types of the reference that the facades lack");
			Assert.True(missing == "", missing);
		}

		[Fact]
		public void The_types_have_the_kind_base_type_interfaces_and_enum_values_of_the_reference()
		{
			var differences = new List<string>();
			foreach (Type reference in surface.ReferenceTypes)
			{
				Type facade = surface.Facade(reference.FullName);
				if (facade != null)
				{
					differences.AddRange(surface.ShapeDifferences(reference, facade).Select(d => reference.FullName + ": " + d));
				}
			}
			Assert.True(differences.Count == 0, Report(differences, "shape differences"));
		}

		[Fact]
		public void The_members_are_those_of_the_reference()
		{
			var differences = new List<string>();
			foreach (Type reference in surface.ReferenceTypes)
			{
				Type facade = surface.Facade(reference.FullName);
				if (facade == null)
				{
					continue;
				}
				var expected = new HashSet<string>(surface.Members(reference), StringComparer.Ordinal);
				var actual = new HashSet<string>(surface.Members(facade), StringComparer.Ordinal);
				differences.AddRange(expected.Except(actual).OrderBy(s => s, StringComparer.Ordinal).Select(s => reference.FullName + " lacks " + s));
				differences.AddRange(actual.Except(expected).OrderBy(s => s, StringComparer.Ordinal).Select(s => reference.FullName + " has extra " + s));
			}
			Assert.True(differences.Count == 0, Report(differences, "member differences"));
		}

		[Fact]
		public void Every_body_throws_the_standard_message()
		{
			Assembly core = Assembly.LoadFrom(WpfSurfaceFixture.FacadePath("PresentationCore"));
			Type uri = core.GetType("System.Windows.Media.Imaging.BitmapImage");
			var e = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(uri));
			Assert.IsType<NotSupportedException>(e.InnerException);
			Assert.Contains("is not yet supported on macOS", e.InnerException.Message);
		}
	}
}
