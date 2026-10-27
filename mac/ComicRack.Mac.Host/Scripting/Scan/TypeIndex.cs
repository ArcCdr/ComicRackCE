using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ComicRack.Mac.Host.Scripting.Scan
{
	/// <summary>One member name of an indexed type: all overloads of a method, a property with its accessors, an event, a field.</summary>
	public sealed class IndexedMember
	{
		public string Name { get; init; }

		/// <summary>method, property, event, field, constructor.</summary>
		public string Kind { get; init; }

		/// <summary>How many methods (or accessors) the member has.</summary>
		public int Parts { get; set; }

		/// <summary>How many of them only throw <see cref="NotSupportedException"/> through the facade stub (see <see cref="TypeIndex"/>), or are fields that keep their default value.</summary>
		public int Stubs { get; set; }

		/// <summary>real (no stub), stub (all parts are stubs) or partial.</summary>
		public string Status => Stubs == 0 ? "real" : Stubs >= Parts ? "stub" : "partial";
	}

	/// <summary>A public type of an indexed assembly.</summary>
	public sealed class IndexedType
	{
		internal TypeIndex Owner;
		internal MetadataReader Reader;
		internal TypeDefinitionHandle Handle;
		internal PEReader Pe;
		internal int FacadeStubToken;
		private Dictionary<string, IndexedMember> members;

		/// <summary>The CLR name; nested types as <c>Outer+Inner</c>.</summary>
		public string FullName { get; init; }

		public string Assembly { get; init; }

		/// <summary><c>program</c> (the folder of the application: facades, Core, IronPython) or <c>runtime</c> (the .NET 10 runtime).</summary>
		public string Origin { get; init; }

		public string BaseName { get; init; }

		public bool IsInterface { get; init; }

		public IReadOnlyList<string> Interfaces { get; init; }

		/// <summary>The public and protected members of this type itself (not inherited), read on first use.</summary>
		public IReadOnlyDictionary<string, IndexedMember> Members => members ??= Owner.ReadMembers(this);

		public override string ToString() => FullName;
	}

	/// <summary>
	/// An index of the public types of a set of managed assemblies, read from metadata without loading anything (P4.17): the assemblies of the .NET runtime, the facades of the layer (System.Windows.Forms,
	/// System.Drawing.Common, System.Web.Extensions, System.Xaml, PresentationCore, PresentationFramework, cYo.Common.Windows) and the Core assemblies (ComicRack.Engine, ComicRack.Plugins, cYo.Common). It answers the question of the gap report: is this type or member
	/// there, and is it real or a stub. A member of a facade is a <b>stub</b> when its body is the generated one: it ends in <c>call FacadeStub.Member(string); throw</c> (mac/tools/facade-gen); a static read-only field
	/// of a type without static constructor in a facade (a generated type) keeps its default value and counts as a stub too.
	/// </summary>
	public sealed class TypeIndex : IDisposable
	{
		private readonly List<PEReader> readers = new List<PEReader>();
		private readonly Dictionary<string, IndexedType> types = new Dictionary<string, IndexedType>(StringComparer.Ordinal);
		private readonly HashSet<string> namespaces = new HashSet<string>(StringComparer.Ordinal);

		public IReadOnlyCollection<IndexedType> Types => types.Values;

		public int AssemblyCount { get; private set; }

		/// <summary>Indexes the managed assemblies among the files; an earlier file wins when two define one type name (so the facades come before the runtime).</summary>
		public static TypeIndex Load(IEnumerable<string> assemblyFiles, string origin = "runtime")
		{
			var index = new TypeIndex();
			foreach (string file in assemblyFiles)
			{
				index.Add(file, origin);
			}
			return index;
		}

		/// <summary>The assemblies of the program folder (facades and Core) followed by those of the running .NET runtime.</summary>
		public static TypeIndex ForProgram(string programFolder = null)
		{
			programFolder ??= AppContext.BaseDirectory;
			string runtime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
			var index = new TypeIndex();
			foreach (string file in Directory.GetFiles(programFolder, "*.dll").OrderBy(f => f, StringComparer.Ordinal).Where(f => IsProgramAssembly(Path.GetFileName(f))))
			{
				index.Add(file, "program");
			}
			foreach (string file in Directory.GetFiles(runtime, "*.dll").OrderBy(f => f, StringComparer.Ordinal))
			{
				index.Add(file, "runtime");
			}
			return index;
		}

		/// <summary>The assemblies of the program that play a role in the gap report: facades, Core, and the files of IronPython (whose names scripts use).</summary>
		private static bool IsProgramAssembly(string name) => name.StartsWith("System.", StringComparison.Ordinal) || name.StartsWith("Presentation", StringComparison.Ordinal) || name.StartsWith("ComicRack.Engine", StringComparison.Ordinal)
			|| name.StartsWith("ComicRack.Plugins", StringComparison.Ordinal) || name.StartsWith("cYo.Common", StringComparison.Ordinal) || name.StartsWith("IronPython", StringComparison.Ordinal)
			|| name.StartsWith("Microsoft.Scripting", StringComparison.Ordinal) || name.StartsWith("Microsoft.Dynamic", StringComparison.Ordinal);

		/// <summary>Assembly simple names to their origin (<c>program</c> or <c>runtime</c>); the first file of a name wins.</summary>
		public IReadOnlyDictionary<string, string> Assemblies => assemblies;

		private readonly Dictionary<string, string> assemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		private void Add(string file, string origin)
		{
			PEReader pe = null;
			try
			{
				pe = new PEReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read));
				if (!pe.HasMetadata)
				{
					pe.Dispose();
					return;
				}
				MetadataReader md = pe.GetMetadataReader();
				if (!md.IsAssembly)
				{
					pe.Dispose();
					return;
				}
				string assembly = md.GetString(md.GetAssemblyDefinition().Name);
				if (assemblies.ContainsKey(assembly))
				{
					pe.Dispose();
					return;
				}
				assemblies[assembly] = origin;
				int stubToken = FacadeStubToken(md);
				readers.Add(pe);
				AssemblyCount++;
				foreach (TypeDefinitionHandle h in md.TypeDefinitions)
				{
					TypeDefinition td = md.GetTypeDefinition(h);
					if (!IsVisible(md, td))
					{
						continue;
					}
					string full = MetadataNames.TypeDefinition(md, h);
					if (types.ContainsKey(full))
					{
						continue;
					}
					string baseName = td.BaseType.IsNil ? null : MetadataNames.Entity(md, td.BaseType, out _);
					var interfaces = new List<string>();
					foreach (InterfaceImplementationHandle ih in td.GetInterfaceImplementations())
					{
						string iname = MetadataNames.Entity(md, md.GetInterfaceImplementation(ih).Interface, out _);
						if (iname != null)
						{
							interfaces.Add(iname);
						}
					}
					types[full] = new IndexedType
					{
						Owner = this,
						Reader = md,
						Handle = h,
						Pe = pe,
						FacadeStubToken = stubToken,
						FullName = full,
						Assembly = assembly,
						Origin = origin,
						BaseName = baseName,
						IsInterface = (td.Attributes & TypeAttributes.Interface) != 0,
						Interfaces = interfaces,
					};
					string ns = md.GetString(td.Namespace);
					if (td.GetDeclaringType().IsNil && ns.Length > 0)
					{
						namespaces.Add(ns);
					}
				}
			}
			catch (BadImageFormatException)
			{
				pe?.Dispose();
			}
			catch (IOException)
			{
				pe?.Dispose();
			}
		}

		private static bool IsVisible(MetadataReader md, TypeDefinition td)
		{
			TypeAttributes v = td.Attributes & TypeAttributes.VisibilityMask;
			if (v == TypeAttributes.Public)
			{
				return true;
			}
			if (v == TypeAttributes.NestedPublic || v == TypeAttributes.NestedFamily || v == TypeAttributes.NestedFamORAssem)
			{
				TypeDefinitionHandle outer = td.GetDeclaringType();
				return !outer.IsNil && IsVisible(md, md.GetTypeDefinition(outer));
			}
			return false;
		}

		/// <summary>The metadata token of <c>FacadeStub.Member</c> in a facade (0 for any other assembly).</summary>
		private static int FacadeStubToken(MetadataReader md)
		{
			foreach (TypeDefinitionHandle h in md.TypeDefinitions)
			{
				TypeDefinition td = md.GetTypeDefinition(h);
				if (md.GetString(td.Name) != "FacadeStub" || md.GetString(td.Namespace).Length != 0)
				{
					continue;
				}
				foreach (MethodDefinitionHandle m in td.GetMethods())
				{
					if (md.GetString(md.GetMethodDefinition(m).Name) == "Member")
					{
						return MetadataTokens.GetToken(m);
					}
				}
			}
			return 0;
		}

		public void Dispose()
		{
			foreach (PEReader r in readers)
			{
				r.Dispose();
			}
			readers.Clear();
		}

		/// <summary>The public type with this CLR name; <c>Outer.Inner</c> is accepted for <c>Outer+Inner</c>.</summary>
		public IndexedType Find(string fullName)
		{
			if (fullName == null)
			{
				return null;
			}
			if (types.TryGetValue(fullName, out IndexedType t))
			{
				return t;
			}
			// a generic type is named with its arity in metadata (List`1): Python and the scan say List
			if (fullName.IndexOf('`') < 0)
			{
				for (int arity = 1; arity <= 4; arity++)
				{
					if (types.TryGetValue(fullName + "`" + arity, out t))
					{
						return t;
					}
				}
			}
			// the Python spelling of a nested type: replace dots from the right by plus signs
			string candidate = fullName;
			int dot = candidate.LastIndexOf('.');
			while (dot > 0)
			{
				candidate = candidate.Substring(0, dot) + "+" + candidate.Substring(dot + 1);
				if (types.TryGetValue(candidate, out t))
				{
					return t;
				}
				dot = candidate.LastIndexOf('.', dot - 1);
			}
			return null;
		}

		public IndexedType Find(string ns, string simpleName) => Find(ns + "." + simpleName);

		/// <summary>True when the name is a namespace of an indexed type or a prefix of one (<c>System.Web</c> for <c>System.Web.Script.Serialization</c>).</summary>
		public bool IsNamespaceOrPrefix(string name) => namespaces.Contains(name) || namespaces.Any(n => n.StartsWith(name + ".", StringComparison.Ordinal));

		/// <summary>The member of the type or of a base class or interface, with the type that declares it.</summary>
		public IndexedMember FindMember(IndexedType type, string name, out IndexedType declaring)
		{
			var seen = new HashSet<string>(StringComparer.Ordinal);
			return FindMember(type, name, out declaring, seen);
		}

		private IndexedMember FindMember(IndexedType type, string name, out IndexedType declaring, HashSet<string> seen)
		{
			declaring = null;
			if (type == null || !seen.Add(type.FullName))
			{
				return null;
			}
			if (type.Members.TryGetValue(name, out IndexedMember m))
			{
				declaring = type;
				return m;
			}
			IndexedMember found = type.BaseName == null ? null : FindMember(Find(type.BaseName), name, out declaring, seen);
			if (found != null)
			{
				return found;
			}
			foreach (string i in type.Interfaces)
			{
				found = FindMember(Find(i), name, out declaring, seen);
				if (found != null)
				{
					return found;
				}
			}
			return null;
		}

		private readonly Dictionary<string, Dictionary<string, List<IndexedType>>> memberMaps = new Dictionary<string, Dictionary<string, List<IndexedType>>>(StringComparer.Ordinal);

		/// <summary>
		/// Every type of a group that declares a member of this name. The group is named by the caller (<paramref name="group"/>) and defined by <paramref name="filter"/>; the lookup of a group is built on its first use
		/// (it reads the members of all its types).
		/// </summary>
		public IReadOnlyList<IndexedType> TypesDeclaring(string group, Func<IndexedType, bool> filter, string memberName)
		{
			if (!memberMaps.TryGetValue(group, out Dictionary<string, List<IndexedType>> map))
			{
				map = new Dictionary<string, List<IndexedType>>(StringComparer.Ordinal);
				foreach (IndexedType t in types.Values.Where(filter).OrderBy(t => t.FullName, StringComparer.Ordinal))
				{
					foreach (string n in t.Members.Keys)
					{
						if (!map.TryGetValue(n, out List<IndexedType> list))
						{
							map[n] = list = new List<IndexedType>();
						}
						list.Add(t);
					}
				}
				memberMaps[group] = map;
			}
			return map.TryGetValue(memberName, out List<IndexedType> found) ? found : (IReadOnlyList<IndexedType>)Array.Empty<IndexedType>();
		}

		internal Dictionary<string, IndexedMember> ReadMembers(IndexedType type)
		{
			MetadataReader md = type.Reader;
			TypeDefinition td = md.GetTypeDefinition(type.Handle);
			var result = new Dictionary<string, IndexedMember>(StringComparer.Ordinal);
			var accessors = new HashSet<MethodDefinitionHandle>();

			IndexedMember Get(string name, string kind)
			{
				if (!result.TryGetValue(name, out IndexedMember m))
				{
					result[name] = m = new IndexedMember { Name = name, Kind = kind };
				}
				return m;
			}

			foreach (PropertyDefinitionHandle ph in td.GetProperties())
			{
				PropertyDefinition p = md.GetPropertyDefinition(ph);
				IndexedMember m = Get(md.GetString(p.Name), "property");
				foreach (MethodDefinitionHandle a in new[] { p.GetAccessors().Getter, p.GetAccessors().Setter })
				{
					if (!a.IsNil && IsVisibleMethod(md.GetMethodDefinition(a)))
					{
						accessors.Add(a);
						m.Parts++;
						m.Stubs += IsStub(type, md.GetMethodDefinition(a)) ? 1 : 0;
					}
				}
				if (m.Parts == 0)
				{
					result.Remove(m.Name);
				}
			}
			foreach (EventDefinitionHandle eh in td.GetEvents())
			{
				EventDefinition e = md.GetEventDefinition(eh);
				IndexedMember m = Get(md.GetString(e.Name), "event");
				foreach (MethodDefinitionHandle a in new[] { e.GetAccessors().Adder, e.GetAccessors().Remover })
				{
					if (!a.IsNil && IsVisibleMethod(md.GetMethodDefinition(a)))
					{
						accessors.Add(a);
						m.Parts++;
						m.Stubs += IsStub(type, md.GetMethodDefinition(a)) ? 1 : 0;
					}
				}
				if (m.Parts == 0)
				{
					result.Remove(m.Name);
				}
			}
			foreach (MethodDefinitionHandle mh in td.GetMethods())
			{
				if (accessors.Contains(mh))
				{
					continue;
				}
				MethodDefinition md2 = md.GetMethodDefinition(mh);
				if (!IsVisibleMethod(md2))
				{
					continue;
				}
				string name = md.GetString(md2.Name);
				if (name == ".cctor")
				{
					continue;
				}
				IndexedMember m = Get(name, name == ".ctor" ? "constructor" : "method");
				m.Parts++;
				m.Stubs += IsStub(type, md2) ? 1 : 0;
			}
			bool hasStaticConstructor = td.GetMethods().Any(h => md.GetString(md.GetMethodDefinition(h).Name) == ".cctor");
			foreach (FieldDefinitionHandle fh in td.GetFields())
			{
				FieldDefinition f = md.GetFieldDefinition(fh);
				FieldAttributes access = f.Attributes & FieldAttributes.FieldAccessMask;
				if (access != FieldAttributes.Public && access != FieldAttributes.Family && access != FieldAttributes.FamORAssem)
				{
					continue;
				}
				string name = md.GetString(f.Name);
				if ((f.Attributes & FieldAttributes.RTSpecialName) != 0)
				{
					continue; // value__ of an enum
				}
				IndexedMember m = Get(name, "field");
				m.Parts++;
				bool defaultValue = type.FacadeStubToken != 0 && (f.Attributes & FieldAttributes.Static) != 0 && (f.Attributes & FieldAttributes.InitOnly) != 0 && (f.Attributes & FieldAttributes.Literal) == 0 && !hasStaticConstructor;
				m.Stubs += defaultValue ? 1 : 0;
			}
			foreach (TypeDefinitionHandle nh in td.GetNestedTypes())
			{
				TypeDefinition nested = md.GetTypeDefinition(nh);
				if (IsVisible(md, nested))
				{
					string name = md.GetString(nested.Name);
					if (!result.ContainsKey(name))
					{
						result[name] = new IndexedMember { Name = name, Kind = "type", Parts = 1 };
					}
				}
			}
			return result;
		}

		private static bool IsVisibleMethod(MethodDefinition m)
		{
			MethodAttributes a = m.Attributes & MethodAttributes.MemberAccessMask;
			return a == MethodAttributes.Public || a == MethodAttributes.Family || a == MethodAttributes.FamORAssem;
		}

		/// <summary>The body ends in <c>call FacadeStub.Member; throw</c>.</summary>
		private static bool IsStub(IndexedType type, MethodDefinition m)
		{
			if (type.FacadeStubToken == 0 || m.RelativeVirtualAddress == 0)
			{
				return false;
			}
			byte[] il;
			try
			{
				il = type.Pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes();
			}
			catch (BadImageFormatException)
			{
				return false;
			}
			if (il == null || il.Length < 6)
			{
				return false;
			}
			int token = type.FacadeStubToken;
			int n = il.Length;
			return il[n - 1] == 0x7A && il[n - 6] == 0x28 && il[n - 5] == (byte)token && il[n - 4] == (byte)(token >> 8) && il[n - 3] == (byte)(token >> 16) && il[n - 2] == (byte)(token >> 24);
		}
	}
}
