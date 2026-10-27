using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FacadeGen
{
	/// <summary>
	/// The reference assemblies of the Windows Desktop pack (<c>Microsoft.WindowsDesktop.App.Ref</c>, downloaded by a pass A build), read with a
	/// <see cref="MetadataLoadContext"/> together with the runtime assemblies. The facade covers every exported type of System.Windows.Forms,
	/// System.Windows.Forms.Primitives (the Forms facade) and System.Drawing.Common (the Drawing facade).
	/// </summary>
	internal sealed class ReferenceSurface : IDisposable
	{
		public static readonly string[] FormsAssemblies = { "System.Windows.Forms", "System.Windows.Forms.Primitives" };

		public static readonly string[] DrawingAssemblies = { "System.Drawing.Common" };

		/// <summary>
		/// The Windows Presentation Foundation facades (P4.17.3): the name of the facade assembly and the reference assemblies it declares, in dependency order (a later one references the earlier ones).
		/// The types of WindowsBase are declared in the PresentationCore facade: the runtime ships its own WindowsBase.dll (4.0.0.0, type forwards only), and on a framework-dependent start
		/// the host prefers that file to an app-local file of the same name (measured with COREHOST_TRACE: the deps entry of a project reference carries no version).
		/// </summary>
		public static readonly (string Facade, string[] References)[] WpfFacades =
		{
			("System.Xaml", new[] { "System.Xaml" }),
			("PresentationCore", new[] { "WindowsBase", "PresentationCore" }),
			("PresentationFramework", new[] { "PresentationFramework" }),
		};

		public static IEnumerable<string> WpfAssemblies => WpfFacades.SelectMany(f => f.References);

		private readonly HashSet<string> runtimeNames;

		private readonly HashSet<string> generatedNames;

		public MetadataLoadContext Context { get; }

		public string ReferenceDirectory { get; }

		public ReferenceSurface(string referenceDirectory)
		{
			ReferenceDirectory = referenceDirectory;
			string runtime = RuntimeEnvironment.GetRuntimeDirectory();
			generatedNames = new HashSet<string>(FormsAssemblies.Concat(DrawingAssemblies).Concat(WpfAssemblies), StringComparer.OrdinalIgnoreCase);
			// The runtime has its own WindowsBase.dll (type forwards only); the pack's reference assembly of that name is the one to read.
			var runtimeFiles = Directory.GetFiles(runtime, "*.dll").Where(f => !generatedNames.Contains(Path.GetFileNameWithoutExtension(f))).ToList();
			runtimeNames = new HashSet<string>(runtimeFiles.Select(Path.GetFileNameWithoutExtension), StringComparer.OrdinalIgnoreCase);
			var paths = runtimeFiles
				.Concat(Directory.GetFiles(referenceDirectory, "*.dll").Where(f => !runtimeNames.Contains(Path.GetFileNameWithoutExtension(f))));
			Context = new MetadataLoadContext(new PathAssemblyResolver(paths), "System.Private.CoreLib");
		}

		/// <summary>The <c>ref/net10.0</c> folder of the newest Microsoft.WindowsDesktop.App.Ref pack: the explicit directory, <c>COMICRACK_WINFORMS_REFERENCE</c> or the NuGet cache.</summary>
		public static string FindReferenceDirectory(string explicitDirectory)
		{
			if (!string.IsNullOrEmpty(explicitDirectory))
			{
				return explicitDirectory;
			}
			string env = Environment.GetEnvironmentVariable("COMICRACK_WINFORMS_REFERENCE");
			if (!string.IsNullOrEmpty(env))
			{
				return env;
			}
			string packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
			if (string.IsNullOrEmpty(packages))
			{
				packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
			}
			string root = Path.Combine(packages, "microsoft.windowsdesktop.app.ref");
			if (!Directory.Exists(root))
			{
				return null;
			}
			return Directory.GetDirectories(root)
				.Select(d => (dir: d, version: Version.TryParse(Path.GetFileName(d), out var v) ? v : new Version(0, 0)))
				.OrderByDescending(x => x.version)
				.Select(x => Path.Combine(x.dir, "ref", "net10.0"))
				.FirstOrDefault(d => File.Exists(Path.Combine(d, "System.Windows.Forms.dll")));
		}

		/// <summary>
		/// The type forwards of a reference assembly whose target a script can reach: a type of the runtime or of another generated facade (full name, generic arity, target assembly).
		/// Forwards to assemblies that are not part of the runtime or the facades (System.Windows.Extensions, System.Security.Permissions) are returned in <paramref name="skipped"/>.
		/// </summary>
		public List<(string Namespace, string Name, string Assembly)> ForwardsOf(string assemblyName, List<string> skipped)
		{
			var result = new List<(string, string, string)>();
			using var stream = File.OpenRead(Path.Combine(ReferenceDirectory, assemblyName + ".dll"));
			using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
			var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
			foreach (var handle in reader.ExportedTypes)
			{
				var exported = reader.GetExportedType(handle);
				if (!exported.IsForwarder || exported.Implementation.Kind != System.Reflection.Metadata.HandleKind.AssemblyReference)
				{
					continue;
				}
				string target = reader.GetString(reader.GetAssemblyReference((System.Reflection.Metadata.AssemblyReferenceHandle)exported.Implementation).Name);
				string ns = reader.GetString(exported.Namespace);
				string name = reader.GetString(exported.Name);
				if (runtimeNames.Contains(target) || generatedNames.Contains(target))
				{
					result.Add((ns, name, target));
				}
				else
				{
					skipped.Add(ns + "." + name + " (" + target + ")");
				}
			}
			result.Sort((a, b) => string.CompareOrdinal(a.Item1 + "." + a.Item2, b.Item1 + "." + b.Item2));
			return result;
		}

		/// <summary>True for a type a script can see: public, or protected nested in a visible type.</summary>
		public static bool IsVisible(Type t)
		{
			if (t.IsNested)
			{
				return (t.IsNestedPublic || t.IsNestedFamily || t.IsNestedFamORAssem) && IsVisible(t.DeclaringType);
			}
			return t.IsPublic;
		}

		/// <summary>The visible types of the named assemblies, nested types after their declaring type, ordered by full name.</summary>
		public List<Type> TypesOf(IEnumerable<string> assemblyNames)
		{
			var result = new List<Type>();
			foreach (string name in assemblyNames)
			{
				Assembly assembly = Context.LoadFromAssemblyPath(Path.Combine(ReferenceDirectory, name + ".dll"));
				foreach (Type t in assembly.GetExportedTypes())
				{
					if (!t.IsNested && IsVisible(t))
					{
						result.Add(t);
					}
				}
			}
			result.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
			return result;
		}

		/// <summary>True when a script can name the type in a facade: a visible type of the three reference assemblies, or a type of the runtime (never the internal types of the pack).</summary>
		public bool IsExposed(Type t)
		{
			while (t.HasElementType)
			{
				t = t.GetElementType();
			}
			if (t.IsGenericParameter)
			{
				return true;
			}
			if (t.IsConstructedGenericType)
			{
				return IsExposed(t.GetGenericTypeDefinition()) && t.GenericTypeArguments.All(IsExposed);
			}
			string assembly = t.Assembly.GetName().Name;
			if (generatedNames.Contains(assembly))
			{
				return IsVisible(t);
			}
			return runtimeNames.Contains(assembly) && IsVisible(t);
		}

		public void Dispose() => Context.Dispose();
	}
}
