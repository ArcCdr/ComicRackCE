using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Compat.Shared
{
	/// <summary>
	/// The drift guard of a generated facade (P4.7): the facade must declare every visible type of the reference assemblies of the Windows Desktop pack with the same kind, base
	/// type, interfaces and the same public and protected members (name, accessibility, static/abstract/virtual/override, parameter names, types, defaults and constants).
	/// What the generator leaves out on purpose is listed in <c>Generated/omitted.txt</c> of the facade (types, interfaces and members); nothing else may be missing or extra.
	/// Linked into Compat.Forms.Tests and Compat.Drawing.Tests.
	/// </summary>
	internal sealed class SurfaceFixture : IDisposable
	{
		private static readonly string[] DefaultPackAssemblies = { "System.Windows.Forms", "System.Windows.Forms.Primitives", "System.Drawing.Common" };

		private readonly string[] PackAssemblies;

		/// <summary>The WPF facades leave out every member that names a type outside the facades and the runtime (System.Printing, UIAutomationProvider ...): such a type counts as omitted.</summary>
		private readonly bool omitUnexposed;

		private readonly HashSet<string> runtimeNames;

		private readonly Dictionary<string, Type> facadeTypes = new Dictionary<string, Type>(StringComparer.Ordinal);

		private readonly HashSet<string> omittedTypes = new HashSet<string>(StringComparer.Ordinal);

		/// <summary>The load context of the reference assemblies.</summary>
		public MetadataLoadContext Context { get; }

		/// <summary>The load context of the facade assemblies.</summary>
		public MetadataLoadContext FacadeContext { get; }

		/// <summary>The visible types (nested ones included) of the reference assemblies of this facade, without the omitted ones.</summary>
		public List<Type> ReferenceTypes { get; } = new List<Type>();

		/// <param name="packAssemblies">The reference assemblies of the pack that the facades stand for (default: the WinForms and GDI+ ones). The runtime's own file of such a name is not used (WindowsBase).</param>
		public SurfaceFixture(string referenceDirectory, string[] referenceAssemblies, string[] facadePaths, string omittedFile, string[] packAssemblies = null)
		{
			PackAssemblies = packAssemblies ?? DefaultPackAssemblies;
			omitUnexposed = packAssemblies != null;
			string runtime = RuntimeEnvironment.GetRuntimeDirectory();
			runtimeNames = new HashSet<string>(Directory.GetFiles(runtime, "*.dll").Select(Path.GetFileNameWithoutExtension).Where(n => !PackAssemblies.Contains(n)), StringComparer.OrdinalIgnoreCase);
			// Since P4.8 the facades carry the identity of the assemblies they stand for (System.Windows.Forms and System.Drawing.Common, version 10.0.0.0, as the pack's
			// reference assemblies): one load context cannot hold both, so the reference and the facade each get their own. The comparison works on names, not on type identity.
			var referencePaths = Directory.GetFiles(runtime, "*.dll").Where(f => !PackAssemblies.Contains(Path.GetFileNameWithoutExtension(f)))
				.Concat(Directory.GetFiles(referenceDirectory, "*.dll").Where(f => PackAssemblies.Contains(Path.GetFileNameWithoutExtension(f)) || !File.Exists(Path.Combine(runtime, Path.GetFileName(f)))));
			var facadeResolvable = Directory.GetFiles(runtime, "*.dll").Where(f => !PackAssemblies.Contains(Path.GetFileNameWithoutExtension(f))).Concat(facadePaths);
			foreach (string facade in facadePaths)
			{
				string directory = Path.GetDirectoryName(facade);
				facadeResolvable = facadeResolvable.Concat(new[] { "SkiaSharp.dll" }.Select(n => Path.Combine(directory, n)).Where(File.Exists));
			}
			Context = new MetadataLoadContext(new PathAssemblyResolver(referencePaths.Distinct()), "System.Private.CoreLib");
			FacadeContext = new MetadataLoadContext(new PathAssemblyResolver(facadeResolvable.Distinct()), "System.Private.CoreLib");
			foreach (string line in File.ReadAllLines(omittedFile))
			{
				if (line.StartsWith("type ", StringComparison.Ordinal))
				{
					omittedTypes.Add(line.Substring(5, line.IndexOf(':') - 5));
				}
			}
			foreach (string facade in facadePaths)
			{
				foreach (Type t in VisibleTypes(FacadeContext.LoadFromAssemblyPath(facade)))
				{
					facadeTypes[t.FullName] = t;
				}
			}
			foreach (string name in referenceAssemblies)
			{
				foreach (Type t in VisibleTypes(Context.LoadFromAssemblyPath(Path.Combine(referenceDirectory, name + ".dll"))))
				{
					if (!omittedTypes.Contains(t.FullName))
					{
						ReferenceTypes.Add(t);
					}
				}
			}
			ReferenceTypes.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
		}

		public void Dispose()
		{
			Context.Dispose();
			FacadeContext.Dispose();
		}

		public Type Facade(string fullName) => facadeTypes.TryGetValue(fullName, out Type t) ? t : null;

		public int FacadeTypeCount => facadeTypes.Count;

		public static bool IsVisible(Type t)
		{
			if (t.IsNested)
			{
				return (t.IsNestedPublic || t.IsNestedFamily || t.IsNestedFamORAssem) && IsVisible(t.DeclaringType);
			}
			return t.IsPublic;
		}

		private static IEnumerable<Type> VisibleTypes(Assembly assembly)
		{
			var result = new List<Type>();
			void Add(Type t)
			{
				result.Add(t);
				foreach (Type n in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
				{
					if (IsVisible(n) && n.Name.IndexOf('<') < 0)
					{
						Add(n);
					}
				}
			}
			foreach (Type t in assembly.GetExportedTypes())
			{
				if (!t.IsNested && IsVisible(t))
				{
					Add(t);
				}
			}
			return result;
		}

		/// <summary>True for a type that a facade declares (or the runtime has): a visible type of the pack that the facade declares by name, or a runtime type.</summary>
		public bool Exposed(Type t)
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
				return Exposed(t.GetGenericTypeDefinition()) && t.GenericTypeArguments.All(Exposed);
			}
			string assembly = t.Assembly.GetName().Name;
			if (PackAssemblies.Contains(assembly))
			{
				return IsVisible(t) && facadeTypes.ContainsKey(t.FullName);
			}
			return runtimeNames.Contains(assembly) && IsVisible(t);
		}

		/// <summary>True when a type is not declared on purpose (the omitted list), also as element, argument or nested type.</summary>
		public bool Omitted(Type t)
		{
			while (t.HasElementType)
			{
				t = t.GetElementType();
			}
			if (t.IsConstructedGenericType)
			{
				return Omitted(t.GetGenericTypeDefinition()) || t.GenericTypeArguments.Any(Omitted);
			}
			return !t.IsGenericParameter && (omittedTypes.Contains(t.FullName) || t.IsNested && Omitted(t.DeclaringType) || omitUnexposed && !Exposed(t));
		}

		// ---- the comparison ----

		public IEnumerable<string> ShapeDifferences(Type reference, Type facade)
		{
			if (reference.IsEnum != facade.IsEnum || reference.IsInterface != facade.IsInterface || reference.IsValueType != facade.IsValueType)
			{
				yield return "kind: the reference is " + Kind(reference) + ", the facade " + Kind(facade);
				yield break;
			}
			if (reference.IsAbstract != facade.IsAbstract || reference.IsSealed != facade.IsSealed)
			{
				yield return "abstract/sealed: the reference " + reference.IsAbstract + "/" + reference.IsSealed + ", the facade " + facade.IsAbstract + "/" + facade.IsSealed;
			}
			if (reference.IsNested && Access(reference) != Access(facade))
			{
				yield return "accessibility: the reference " + Access(reference) + ", the facade " + Access(facade);
			}
			if (reference.IsGenericTypeDefinition != facade.IsGenericTypeDefinition || reference.IsGenericTypeDefinition && reference.GetGenericArguments().Length != facade.GetGenericArguments().Length)
			{
				yield return "generic arity differs";
			}
			string expectedBase = reference.BaseType == null ? null : reference.BaseType.ToString();
			string actualBase = facade.BaseType == null ? null : facade.BaseType.ToString();
			if (!reference.IsInterface && Name(reference.BaseType) != Name(facade.BaseType))
			{
				yield return "base type: the reference " + expectedBase + ", the facade " + actualBase;
			}
			var expectedInterfaces = new SortedSet<string>(reference.GetInterfaces().Where(Exposed).Select(Name), StringComparer.Ordinal);
			var actualInterfaces = new SortedSet<string>(facade.GetInterfaces().Select(Name), StringComparer.Ordinal);
			foreach (string i in expectedInterfaces.Except(actualInterfaces))
			{
				yield return "interface missing: " + i;
			}
			foreach (string i in actualInterfaces.Except(expectedInterfaces))
			{
				yield return "interface extra: " + i;
			}
			if (reference.IsEnum && facade.IsEnum)
			{
				foreach (string d in EnumDifferences(reference, facade))
				{
					yield return d;
				}
			}
		}

		private static string Kind(Type t) => t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsValueType ? "struct" : "class";

		private static string Name(Type t) => t == null ? "" : t.ToString();

		private static string Access(Type t) => t.IsNestedPublic ? "public" : t.IsNestedFamily ? "protected" : t.IsNestedFamORAssem ? "protected internal" : "?";

		private static IEnumerable<string> EnumDifferences(Type reference, Type facade)
		{
			if (reference.GetEnumUnderlyingType().FullName != facade.GetEnumUnderlyingType().FullName)
			{
				yield return "enum underlying type differs";
			}
			if (HasFlags(reference) != HasFlags(facade))
			{
				yield return "enum [Flags] differs";
			}
			var expected = EnumMembers(reference);
			var actual = EnumMembers(facade);
			foreach (var pair in expected)
			{
				if (!actual.TryGetValue(pair.Key, out string value))
				{
					yield return "enum member missing: " + pair.Key;
				}
				else if (value != pair.Value)
				{
					yield return "enum member " + pair.Key + " is " + value + ", the reference has " + pair.Value;
				}
			}
			foreach (string extra in actual.Keys.Except(expected.Keys))
			{
				yield return "enum member extra: " + extra;
			}
		}

		private static bool HasFlags(Type t) => t.CustomAttributes.Any(a => a.AttributeType.FullName == "System.FlagsAttribute");

		private static Dictionary<string, string> EnumMembers(Type enumType)
		{
			var result = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (FieldInfo f in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
			{
				result[f.Name] = Convert.ToString(f.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture);
			}
			return result;
		}

		/// <summary>The public and protected members of a type (declared on it) as comparable lines; a member that names an omitted type is not listed.</summary>
		public IEnumerable<string> Members(Type t)
		{
			if (t.IsEnum)
			{
				yield break;
			}
			const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
			foreach (ConstructorInfo c in t.GetConstructors(all))
			{
				string access = Access(c);
				if (access != null && !c.GetParameters().Any(p => Omitted(p.ParameterType)))
				{
					yield return "ctor " + access + " (" + Parameters(c.GetParameters()) + ")";
				}
			}
			foreach (MethodInfo m in t.GetMethods(all))
			{
				string access = Access(m);
				if (access == null || m.IsSpecialName && !m.Name.StartsWith("op_", StringComparison.Ordinal) || m.Name == "Finalize" || m.Name.IndexOf('.') >= 0)
				{
					continue;
				}
				if (Omitted(m.ReturnType) || m.GetParameters().Any(p => Omitted(p.ParameterType)))
				{
					continue;
				}
				string generic = m.IsGenericMethodDefinition ? "<" + string.Join(",", m.GetGenericArguments().Select(g => g.Name)) + ">" : "";
				yield return "method " + access + " " + Modifiers(m) + m.ReturnType + " " + m.Name + generic + "(" + Parameters(m.GetParameters()) + ")";
			}
			foreach (PropertyInfo p in t.GetProperties(all))
			{
				string get = p.GetMethod == null ? null : Access(p.GetMethod);
				string set = p.SetMethod == null ? null : Access(p.SetMethod);
				if ((get == null && set == null) || p.Name.IndexOf('.') >= 0 || Omitted(p.PropertyType) || p.GetIndexParameters().Any(i => Omitted(i.ParameterType)))
				{
					continue;
				}
				MethodInfo primary = get != null && (set == null || Rank(get) >= Rank(set)) ? p.GetMethod : p.SetMethod;
				yield return "property " + Modifiers(primary) + p.PropertyType + " " + p.Name + "[" + string.Join(",", p.GetIndexParameters().Select(i => i.ParameterType.ToString())) + "]"
					+ " get:" + (get ?? "-") + " set:" + (set ?? "-");
			}
			foreach (EventInfo e in t.GetEvents(all))
			{
				string access = e.AddMethod == null ? null : Access(e.AddMethod);
				if (access == null || e.Name.IndexOf('.') >= 0 || Omitted(e.EventHandlerType))
				{
					continue;
				}
				yield return "event " + access + " " + Modifiers(e.AddMethod) + e.EventHandlerType + " " + e.Name;
			}
			foreach (FieldInfo f in t.GetFields(all))
			{
				string access = f.IsPublic ? "public" : f.IsFamily ? "protected" : f.IsFamilyOrAssembly ? "protected internal" : null;
				if (access == null || Omitted(f.FieldType))
				{
					continue;
				}
				string kind = f.IsLiteral ? "const" : (f.IsStatic ? "static " : "") + (f.IsInitOnly ? "readonly" : "");
				yield return "field " + access + " " + kind + " " + f.FieldType + " " + f.Name + (f.IsLiteral ? " = " + f.GetRawConstantValue() : "");
			}
		}

		private static string Access(MethodBase m) => m.IsPublic ? "public" : m.IsFamily ? "protected" : m.IsFamilyOrAssembly ? "protected internal" : null;

		private static int Rank(string access) => access == "public" ? 3 : access == "protected internal" ? 2 : 1;

		private static string Modifiers(MethodBase m)
		{
			bool newSlot = (m.Attributes & MethodAttributes.NewSlot) != 0;
			if (m.IsStatic)
			{
				return "static ";
			}
			if (m.IsAbstract)
			{
				return newSlot ? "abstract " : "abstract override ";
			}
			if (m.IsVirtual)
			{
				// "virtual final newslot" is how the compiler marks a public member that implements an interface implicitly; it depends on which interfaces
				// a class lists (the internal ones of the pack are not in the facade), so it counts as a plain member.
				return m.IsFinal ? (newSlot ? "" : "sealed override ") : (newSlot ? "virtual " : "override ");
			}
			return "";
		}

		private static string Parameters(ParameterInfo[] parameters)
		{
			return string.Join(", ", parameters.Select(p =>
			{
				string direction = p.ParameterType.IsByRef ? (p.IsOut && !p.IsIn ? "out " : p.IsIn && !p.IsOut ? "in " : "ref ") : "";
				bool isParams = p.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "System.ParamArrayAttribute");
				string defaultValue = p.HasDefaultValue ? " = " + (p.RawDefaultValue ?? "null") : "";
				return (isParams ? "params " : "") + direction + p.ParameterType + " " + p.Name + defaultValue;
			}));
		}
	}
}

namespace Compat.Shared
{
	/// <summary>Finds the mac/ folder from the test assembly.</summary>
	internal static class MacRoot
	{
		public static string Find()
		{
			for (string dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
			{
				if (File.Exists(Path.Combine(dir, "ComicRack.Mac.slnx")))
				{
					return dir;
				}
			}
			throw new FileNotFoundException("mac/ComicRack.Mac.slnx not found above " + AppContext.BaseDirectory);
		}
	}
}
