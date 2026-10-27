using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace ComicRack.Mac.Host.Scripting.Scan
{
	public enum ApiTypeKind
	{
		Class,
		Struct,
		Enum,
		Interface,
		Delegate,
	}

	public enum ApiMemberKind
	{
		Method,
		Property,
		Field,
		Event,
		Constructor,
		NestedType,
	}

	/// <summary>One public or protected member of a catalog type.</summary>
	public sealed class ApiMember
	{
		public string Name { get; init; }

		public ApiMemberKind Kind { get; init; }

		/// <summary>Return type (method), type (property, field, event) or full name (nested type); null for a constructor.</summary>
		public string Type { get; init; }

		public bool IsStatic { get; init; }

		/// <summary>An overridable member (virtual and not final); a script class may override it.</summary>
		public bool IsVirtual { get; init; }

		public bool IsProtected { get; init; }
	}

	/// <summary>A public type of the WinForms or GDI+ surface.</summary>
	public sealed class ApiType
	{
		private readonly Dictionary<string, List<ApiMember>> members = new Dictionary<string, List<ApiMember>>(StringComparer.Ordinal);

		/// <summary>Metadata name, nested types with a plus: <c>System.Windows.Forms.Control+ControlCollection</c>.</summary>
		public string FullName { get; init; }

		public string Namespace { get; init; }

		/// <summary>Name without the namespace, nested types with a dot: <c>Control.ControlCollection</c>.</summary>
		public string Name { get; init; }

		public ApiTypeKind Kind { get; init; }

		public string BaseName { get; init; }

		public bool IsAbstract { get; init; }

		public IReadOnlyDictionary<string, List<ApiMember>> Members => members;

		internal void Add(ApiMember member)
		{
			if (!members.TryGetValue(member.Name, out List<ApiMember> list))
			{
				members[member.Name] = list = new List<ApiMember>();
			}
			list.Add(member);
		}

		public override string ToString() => FullName;
	}

	/// <summary>
	/// The real API surface the script compatibility layer has to cover (R11, P4.6a): the public types and members of <c>System.Windows.Forms</c> and <c>System.Drawing</c>,
	/// read from metadata (reference assemblies of the Windows Desktop pack, or the facade assemblies that ship with the program). The scanner resolves names and member chains against it.
	/// </summary>
	public sealed class ApiCatalog
	{
		/// <summary>Namespaces whose types the catalog keeps.</summary>
		public static readonly string[] SurfaceNamespaces = { "System.Windows.Forms", "System.Drawing" };

		private readonly Dictionary<string, ApiType> byFullName = new Dictionary<string, ApiType>(StringComparer.Ordinal);
		private readonly Dictionary<string, Dictionary<string, ApiType>> byNamespace = new Dictionary<string, Dictionary<string, ApiType>>(StringComparer.Ordinal);
		private readonly HashSet<string> namespaces = new HashSet<string>(StringComparer.Ordinal);

		/// <summary>Where the surface comes from (the folder of the reference pack, or a note that the facade is used).</summary>
		public string Source { get; private set; } = "empty";

		/// <summary>True when the catalog was read from reference assemblies (the whole WinForms surface), false for the partial facade or an empty catalog.</summary>
		public bool IsComplete { get; private set; }

		public int TypeCount => byFullName.Count;

		public static ApiCatalog Empty { get; } = new ApiCatalog();

		public ApiType Type(string fullName) => fullName != null && byFullName.TryGetValue(fullName, out ApiType t) ? t : null;

		public ApiType Type(string ns, string simpleName) => byNamespace.TryGetValue(ns, out var map) && map.TryGetValue(simpleName, out ApiType t) ? t : null;

		/// <summary>True for a namespace of the surface or one of its parents (<c>System.Windows</c>).</summary>
		public bool IsNamespace(string ns) => namespaces.Contains(ns);

		public ApiType BaseOf(ApiType type) => type.BaseName == null ? null : Type(type.BaseName);

		/// <summary>The first member with this name on the type or on a base type of the catalog; <paramref name="declaring"/> is the type that declares it.</summary>
		public List<ApiMember> FindMember(ApiType type, string name, out ApiType declaring)
		{
			var seen = new HashSet<ApiType>();
			for (ApiType t = type; t != null && seen.Add(t); t = BaseOf(t))
			{
				if (t.Members.TryGetValue(name, out List<ApiMember> list))
				{
					declaring = t;
					return list;
				}
			}
			declaring = null;
			return null;
		}

		/// <summary>Why a member can never run on macOS, or null: <see cref="WindowsOnlyApi"/> asked for the type that declares it and for its base types (a form overrides what a control declares).</summary>
		public string MemberReason(ApiType declaring, string name)
		{
			var seen = new HashSet<ApiType>();
			for (ApiType t = declaring; t != null && seen.Add(t); t = BaseOf(t))
			{
				string reason = WindowsOnlyApi.MemberReason(t.FullName, name);
				if (reason != null)
				{
					return reason;
				}
			}
			return null;
		}

		/// <summary>True when the type is the given type or derives from it (inside the catalog).</summary>
		public bool IsSubclassOf(ApiType type, string baseFullName)
		{
			var seen = new HashSet<ApiType>();
			for (ApiType t = type; t != null && seen.Add(t); t = BaseOf(t))
			{
				if (t.FullName == baseFullName || t.BaseName == baseFullName)
				{
					return true;
				}
			}
			return false;
		}

		public IEnumerable<ApiType> Types => byFullName.Values;

		/// <summary>
		/// The catalog from the reference assemblies of the Windows Desktop pack (<paramref name="referenceDirectory"/> holds <c>System.Windows.Forms.dll</c>, <c>System.Drawing.Common.dll</c>)
		/// plus <c>System.Drawing.Primitives</c> of the runtime.
		/// </summary>
		public static ApiCatalog FromReferenceDirectory(string referenceDirectory)
		{
			var paths = new List<string>();
			foreach (string name in new[] { "System.Windows.Forms.dll", "System.Windows.Forms.Primitives.dll", "System.Drawing.Common.dll" })
			{
				string p = Path.Combine(referenceDirectory, name);
				if (File.Exists(p))
				{
					paths.Add(p);
				}
			}
			AddRuntimeDrawing(paths, referenceDirectory);
			var catalog = FromAssemblies(paths);
			catalog.Source = "reference assemblies " + referenceDirectory;
			catalog.IsComplete = true;
			return catalog;
		}

		/// <summary>
		/// The catalog from the facade assemblies next to the program, plus <c>System.Drawing.Primitives</c> of the runtime (Point, Size, Color ... are not redeclared by the facade).
		/// Since P4.7 the facades declare the whole surface of the reference assemblies (generated by mac/tools/facade-gen), so this catalog is complete; it is empty when the facade
		/// assemblies are not found, and incomplete when they lack the generated types (an older build).
		/// </summary>
		public static ApiCatalog FromFacade(string directory)
		{
			var paths = new List<string>();
			// The assemblies are named cYo.Compat.* until P4.8 gives them the names of the framework assemblies. Until then the output folder of a program that hosts IronPython also
			// holds the real System.Drawing.Common 4.7.0 (a transitive package of IronPython), which is not the facade: the pair with the facade name wins.
			foreach (string[] pair in new[] { new[] { "cYo.Compat.Forms.dll", "System.Windows.Forms.dll" }, new[] { "cYo.Compat.Drawing.dll", "System.Drawing.Common.dll" } })
			{
				string p = pair.Select(n => Path.Combine(directory, n)).FirstOrDefault(File.Exists);
				if (p != null)
				{
					paths.Add(p);
				}
			}
			AddRuntimeDrawing(paths, directory);
			var catalog = FromAssemblies(paths);
			catalog.IsComplete = catalog.Type("System.Windows.Forms", "DataGridView") != null && catalog.Type("System.Drawing.Printing", "PrintDocument") != null;
			catalog.Source = "macOS facade assemblies in " + directory + (catalog.IsComplete ? "" : " (partial)");
			return catalog;
		}

		private static void AddRuntimeDrawing(List<string> paths, string referenceDirectory)
		{
			string primitives = "System.Drawing.Primitives.dll";
			if (!File.Exists(Path.Combine(referenceDirectory, primitives)))
			{
				string runtime = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), primitives);
				if (File.Exists(runtime))
				{
					paths.Add(runtime);
				}
			}
			else
			{
				paths.Add(Path.Combine(referenceDirectory, primitives));
			}
		}

		public static ApiCatalog FromAssemblies(IEnumerable<string> assemblyPaths)
		{
			var catalog = new ApiCatalog();
			foreach (string path in assemblyPaths)
			{
				catalog.Read(path);
			}
			catalog.Source = "assemblies";
			return catalog;
		}

		/// <summary>True for a namespace of the WinForms or GDI+ surface.</summary>
		public static bool InSurface(string ns)
		{
			foreach (string s in SurfaceNamespaces)
			{
				if (ns == s || ns.StartsWith(s + ".", StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		private void Read(string path)
		{
			using var stream = File.OpenRead(path);
			using var pe = new PEReader(stream);
			if (!pe.HasMetadata)
			{
				return;
			}
			MetadataReader md = pe.GetMetadataReader();
			foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
			{
				TypeDefinition td = md.GetTypeDefinition(handle);
				if (!IsPublic(td))
				{
					continue;
				}
				string ns = md.GetString(td.Namespace);
				TypeDefinitionHandle outer = td.GetDeclaringType();
				if (!outer.IsNil)
				{
					ns = md.GetString(md.GetTypeDefinition(outer).Namespace);
				}
				if (!InSurface(ns))
				{
					continue;
				}
				string fullName = MetadataNames.TypeDefinition(md, handle);
				if (byFullName.ContainsKey(fullName))
				{
					continue;
				}
				string baseName = td.BaseType.IsNil ? null : MetadataNames.Entity(md, td.BaseType, out _);
				ApiTypeKind kind = (td.Attributes & TypeAttributes.Interface) != 0 ? ApiTypeKind.Interface
					: baseName == "System.Enum" ? ApiTypeKind.Enum
					: baseName == "System.ValueType" ? ApiTypeKind.Struct
					: baseName == "System.MulticastDelegate" ? ApiTypeKind.Delegate
					: ApiTypeKind.Class;
				string name = fullName.Substring(ns.Length + 1).Replace('+', '.');
				var type = new ApiType
				{
					FullName = fullName,
					Namespace = ns,
					Name = name,
					Kind = kind,
					BaseName = baseName,
					IsAbstract = (td.Attributes & TypeAttributes.Abstract) != 0 && kind == ApiTypeKind.Class,
				};
				ReadMembers(md, td, type);
				byFullName[fullName] = type;
				if (outer.IsNil)
				{
					if (!byNamespace.TryGetValue(ns, out var map))
					{
						byNamespace[ns] = map = new Dictionary<string, ApiType>(StringComparer.Ordinal);
					}
					map[name] = type;
				}
				string prefix = ns;
				while (true)
				{
					namespaces.Add(prefix);
					int dot = prefix.LastIndexOf('.');
					if (dot < 0)
					{
						break;
					}
					prefix = prefix.Substring(0, dot);
				}
			}
			// Nested types become members of their declaring type (after all types are known).
			foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
			{
				TypeDefinition td = md.GetTypeDefinition(handle);
				TypeDefinitionHandle outer = td.GetDeclaringType();
				if (outer.IsNil)
				{
					continue;
				}
				string nested = MetadataNames.TypeDefinition(md, handle);
				if (byFullName.TryGetValue(nested, out ApiType nestedType) && byFullName.TryGetValue(MetadataNames.TypeDefinition(md, outer), out ApiType owner))
				{
					owner.Add(new ApiMember { Name = md.GetString(td.Name), Kind = ApiMemberKind.NestedType, Type = nested, IsStatic = true });
				}
			}
		}

		private static bool IsPublic(TypeDefinition td)
		{
			TypeAttributes vis = td.Attributes & TypeAttributes.VisibilityMask;
			return vis == TypeAttributes.Public || vis == TypeAttributes.NestedPublic;
		}

		private static bool Visible(MethodAttributes attributes, out bool isProtected)
		{
			MethodAttributes access = attributes & MethodAttributes.MemberAccessMask;
			isProtected = access == MethodAttributes.Family || access == MethodAttributes.FamORAssem;
			return access == MethodAttributes.Public || isProtected;
		}

		private static void ReadMembers(MetadataReader md, TypeDefinition td, ApiType type)
		{
			var accessors = new HashSet<MethodDefinitionHandle>();
			foreach (PropertyDefinitionHandle ph in td.GetProperties())
			{
				PropertyDefinition pd = md.GetPropertyDefinition(ph);
				PropertyAccessors pa = pd.GetAccessors();
				MethodDefinition? primary = null;
				foreach (MethodDefinitionHandle mh in new[] { pa.Getter, pa.Setter })
				{
					if (!mh.IsNil)
					{
						accessors.Add(mh);
						MethodDefinition m = md.GetMethodDefinition(mh);
						if (primary == null || (Visible(m.Attributes, out _) && !Visible(primary.Value.Attributes, out _)))
						{
							primary = m;
						}
					}
				}
				if (primary == null || !Visible(primary.Value.Attributes, out bool prot))
				{
					continue;
				}
				string propertyType = pd.DecodeSignature(MetadataNames.Instance, null).ReturnType;
				type.Add(new ApiMember
				{
					Name = md.GetString(pd.Name),
					Kind = ApiMemberKind.Property,
					Type = propertyType,
					IsStatic = (primary.Value.Attributes & MethodAttributes.Static) != 0,
					IsVirtual = IsOverridable(primary.Value.Attributes),
					IsProtected = prot,
				});
			}
			foreach (EventDefinitionHandle eh in td.GetEvents())
			{
				EventDefinition ed = md.GetEventDefinition(eh);
				EventAccessors ea = ed.GetAccessors();
				MethodDefinition? primary = null;
				foreach (MethodDefinitionHandle mh in new[] { ea.Adder, ea.Remover, ea.Raiser })
				{
					if (!mh.IsNil)
					{
						accessors.Add(mh);
						primary ??= md.GetMethodDefinition(mh);
					}
				}
				if (primary == null || !Visible(primary.Value.Attributes, out bool prot))
				{
					continue;
				}
				type.Add(new ApiMember
				{
					Name = md.GetString(ed.Name),
					Kind = ApiMemberKind.Event,
					Type = MetadataNames.Entity(md, ed.Type, out _),
					IsStatic = (primary.Value.Attributes & MethodAttributes.Static) != 0,
					IsVirtual = IsOverridable(primary.Value.Attributes),
					IsProtected = prot,
				});
			}
			foreach (MethodDefinitionHandle mh in td.GetMethods())
			{
				if (accessors.Contains(mh))
				{
					continue;
				}
				MethodDefinition m = md.GetMethodDefinition(mh);
				if (!Visible(m.Attributes, out bool prot))
				{
					continue;
				}
				string name = md.GetString(m.Name);
				if (name == ".cctor")
				{
					continue;
				}
				bool ctor = name == ".ctor";
				type.Add(new ApiMember
				{
					Name = name,
					Kind = ctor ? ApiMemberKind.Constructor : ApiMemberKind.Method,
					Type = ctor ? null : m.DecodeSignature(MetadataNames.Instance, null).ReturnType,
					IsStatic = (m.Attributes & MethodAttributes.Static) != 0,
					IsVirtual = IsOverridable(m.Attributes),
					IsProtected = prot,
				});
			}
			foreach (FieldDefinitionHandle fh in td.GetFields())
			{
				FieldDefinition fd = md.GetFieldDefinition(fh);
				FieldAttributes access = fd.Attributes & FieldAttributes.FieldAccessMask;
				bool prot = access == FieldAttributes.Family || access == FieldAttributes.FamORAssem;
				if (access != FieldAttributes.Public && !prot)
				{
					continue;
				}
				string name = md.GetString(fd.Name);
				if (name == "value__")
				{
					continue;
				}
				type.Add(new ApiMember
				{
					Name = name,
					Kind = ApiMemberKind.Field,
					Type = fd.DecodeSignature(MetadataNames.Instance, null),
					IsStatic = (fd.Attributes & FieldAttributes.Static) != 0,
					IsProtected = prot,
				});
			}
		}

		private static bool IsOverridable(MethodAttributes attributes) => (attributes & MethodAttributes.Virtual) != 0 && (attributes & MethodAttributes.Final) == 0;
	}

	/// <summary>Finds the reference assemblies of the WinForms and GDI+ surface on this machine.</summary>
	public static class ReferenceSurface
	{
		/// <summary>The name of the environment variable that may name a folder with the reference assemblies.</summary>
		public const string EnvironmentVariable = "COMICRACK_WINFORMS_REFERENCE";

		/// <summary>
		/// The folder with <c>System.Windows.Forms.dll</c> of the Windows Desktop reference pack: <paramref name="explicitPath"/> (folder or file), the environment variable,
		/// the NuGet cache (<c>microsoft.windowsdesktop.app.ref</c>, highest version, downloaded by a pass A build), or null.
		/// </summary>
		public static string FindDirectory(string explicitPath = null)
		{
			foreach (string candidate in new[] { explicitPath, Environment.GetEnvironmentVariable(EnvironmentVariable) })
			{
				if (string.IsNullOrEmpty(candidate))
				{
					continue;
				}
				string dir = File.Exists(candidate) ? Path.GetDirectoryName(Path.GetFullPath(candidate)) : candidate;
				if (File.Exists(Path.Combine(dir, "System.Windows.Forms.dll")))
				{
					return dir;
				}
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
				.Select(d => (dir: d, version: Version.TryParse(Path.GetFileName(d), out Version v) ? v : new Version(0, 0)))
				.OrderByDescending(x => x.version)
				.Select(x => Path.Combine(x.dir, "ref", "net10.0"))
				.FirstOrDefault(d => File.Exists(Path.Combine(d, "System.Windows.Forms.dll")));
		}

		/// <summary>The best catalog available: the reference assemblies when found, else the facade next to the program.</summary>
		public static ApiCatalog Load(string explicitPath = null)
		{
			string dir = FindDirectory(explicitPath);
			if (dir != null)
			{
				return ApiCatalog.FromReferenceDirectory(dir);
			}
			return ApiCatalog.FromFacade(AppContext.BaseDirectory);
		}
	}
}
