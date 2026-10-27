using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ApiInventory
{
	/// <summary>
	/// Answers "does a shim have it" for a type or member of the inventory, by reflecting over <c>cYo.Compat.Drawing</c> (plus the framework's
	/// <c>System.Drawing.Primitives</c>) and <c>cYo.Compat.Forms</c> through a MetadataLoadContext, so nothing of the shims (and no SkiaSharp
	/// native library) is loaded into this process. A member is covered when the type has a member of that kind and name with the same
	/// parameters (an int may stand for a float, and trailing optional parameters may be extra) and the same return type.
	/// The same rule as <c>mac/tools/drawing-coverage</c> ("exact" and "convertible"), without its stub detection.
	/// </summary>
	internal sealed class ShimCoverage : IDisposable
	{
		private readonly MetadataLoadContext mlc;
		private readonly Dictionary<string, Type> drawing = new(StringComparer.Ordinal);
		private readonly Dictionary<string, Type> forms = new(StringComparer.Ordinal);

		public ShimCoverage(string drawingDll, string formsDll)
		{
			var paths = new List<string>();
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			void AddDir(string dir)
			{
				if (dir == null || !Directory.Exists(dir))
					return;
				foreach (var f in Directory.GetFiles(dir, "*.dll"))
					if (names.Add(Path.GetFileName(f)))
						paths.Add(f);
			}
			// The shim files first, so that the copy of the Drawing shim next to the Forms shim is not used.
			foreach (var f in new[] { drawingDll, formsDll })
				if (names.Add(Path.GetFileName(f)))
					paths.Add(f);
			AddDir(RuntimeEnvironment.GetRuntimeDirectory());
			AddDir(Path.GetDirectoryName(drawingDll));
			AddDir(Path.GetDirectoryName(formsDll));
			AddDir(SkiaSharpDir());

			mlc = new MetadataLoadContext(new PathAssemblyResolver(paths), "System.Private.CoreLib");
			foreach (var t in mlc.LoadFromAssemblyPath(drawingDll).GetExportedTypes())
				drawing[Normalize(t.FullName)] = t;
			foreach (var t in mlc.LoadFromAssemblyName("System.Drawing.Primitives").GetExportedTypes())
				drawing.TryAdd(Normalize(t.FullName), t);
			foreach (var t in mlc.LoadFromAssemblyPath(formsDll).GetExportedTypes())
				forms[Normalize(t.FullName)] = t;
		}

		public void Dispose() => mlc.Dispose();

		public int TypeCount => drawing.Count + forms.Count;

		private static string Normalize(string name) => name.Replace('+', '.');

		/// <summary>The newest System.Drawing.Common.dll and System.Windows.Forms.dll (the shim assemblies, named like the framework assemblies since P4.8) below mac/ (a build of the shim projects must exist).</summary>
		public static (string Drawing, string Forms) Locate(string macDir)
		{
			return (Newest(macDir, "cYo.Compat.Drawing", "System.Drawing.Common"), Newest(macDir, "cYo.Compat.Forms", "System.Windows.Forms"));
		}

		private static string Newest(string macDir, string project, string assembly)
		{
			string bin = Path.Combine(macDir, project, "bin");
			string dll = Directory.Exists(bin)
				? Directory.GetFiles(bin, assembly + ".dll", SearchOption.AllDirectories)
					.Where(f => f.Contains(Path.DirectorySeparatorChar + "net10.0" + Path.DirectorySeparatorChar))
					.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
				: null;
			return dll ?? throw new FileNotFoundException($"{assembly}.dll not found below {bin}. Build it first: dotnet build mac/cYo.Compat.Forms/cYo.Compat.Forms.csproj");
		}

		// SkiaSharp types only occur in signatures of the Drawing shim; a missing assembly makes those parameters unresolvable, not the whole check.
		private static string SkiaSharpDir()
		{
			try
			{
				string root = Path.Combine(Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"), "skiasharp");
				if (!Directory.Exists(root))
					return null;
				return Directory.GetDirectories(root).OrderByDescending(d => Version.TryParse(Path.GetFileName(d), out var v) ? v : new Version(0, 0))
					.SelectMany(d => Directory.Exists(Path.Combine(d, "lib")) ? Directory.GetDirectories(Path.Combine(d, "lib")) : Array.Empty<string>())
					.Where(d => Path.GetFileName(d).StartsWith("net", StringComparison.Ordinal) && !Path.GetFileName(d).StartsWith("netstandard", StringComparison.Ordinal))
					.OrderByDescending(d => d, StringComparer.Ordinal).FirstOrDefault();
			}
			catch (IOException)
			{
				return null;
			}
		}

		private Dictionary<string, Type> Side(string scope) => scope switch
		{
			"System.Windows.Forms" or "System.Windows.Forms.Primitives" => forms,
			_ => drawing,
		};

		public bool CoversType(string scope, string fullName) => Side(scope).ContainsKey(Normalize(fullName));

		public bool CoversMember(ApiMember m)
		{
			if (!Side(m.Scope).TryGetValue(Normalize(m.Type), out var type))
				return false;
			try
			{
				return Covers(type, m);
			}
			catch (Exception e) when (e is FileNotFoundException or TypeLoadException or BadImageFormatException)
			{
				return false;
			}
		}

		private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

		private static bool Covers(Type type, ApiMember m)
		{
			string sig = m.Sig;
			string left = sig, ret = null;
			if (m.Kind != "field")
			{
				int ci = sig.LastIndexOf(" : ", StringComparison.Ordinal);
				if (ci >= 0)
				{
					left = sig[..ci];
					ret = sig[(ci + 3)..].Trim();
				}
			}
			left = left.Trim();
			var ps = left.Length > 2 && (left[0] == '(' || left[0] == '[') ? SplitTop(left[1..^1]) : new List<string>();

			switch (m.Kind)
			{
				case "ctor":
					return type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Any(c => ParamsMatch(c.GetParameters(), ps));
				case "method":
					return type.GetMethods(Flags).Where(x => x.Name == m.Name)
						.Any(x => ParamsMatch(x.GetParameters(), ps) && (ret == null || Alias(x.ReturnType) == ret));
				case "property":
				{
					bool wantGet = m.Accessors.Contains("get", StringComparison.Ordinal), wantSet = m.Accessors.Contains("set", StringComparison.Ordinal);
					return type.GetProperties(Flags).Where(p => p.Name == m.Name).Any(p =>
						(!wantGet || p.GetMethod?.IsPublic == true) && (!wantSet || p.SetMethod?.IsPublic == true)
						&& ParamsMatch(p.GetIndexParameters(), ps, exactCount: true) && (ret == null || Alias(p.PropertyType) == ret));
				}
				case "field":
				{
					// Sig is "Name : Type".
					int ci = sig.IndexOf(" : ", StringComparison.Ordinal);
					string fieldType = ci >= 0 ? sig[(ci + 3)..].Trim() : null;
					return type.GetFields(Flags).Any(f => f.Name == m.Name && (fieldType == null || Alias(f.FieldType) == fieldType))
						|| type.GetProperties(Flags).Any(p => p.Name == m.Name && p.GetMethod?.IsPublic == true && (fieldType == null || Alias(p.PropertyType) == fieldType));
				}
				case "event":
					return type.GetEvents(Flags).Any(e => e.Name == m.Name);
				default:
					return false;
			}
		}

		/// <summary>Same leading parameters (identical or implicitly widened); with <paramref name="exactCount"/> false, extra trailing parameters must be optional.</summary>
		private static bool ParamsMatch(ParameterInfo[] pp, List<string> ps, bool exactCount = false)
		{
			if (pp.Length < ps.Count || (exactCount && pp.Length != ps.Count))
				return false;
			for (int i = 0; i < pp.Length; i++)
			{
				if (i >= ps.Count)
				{
					if (!pp[i].IsOptional)
						return false;
				}
				else
				{
					string a = Alias(pp[i].ParameterType);
					if (a != ps[i] && !Widens(ps[i], a))
						return false;
				}
			}
			return true;
		}

		private static bool Widens(string from, string to) => (from, to) switch
		{
			("int", "float") or ("int", "double") or ("int", "long") or ("float", "double") or ("byte", "int") or ("byte", "float") or ("short", "int") => true,
			_ => false,
		};

		/// <summary>Splits a parameter list at the commas that are not inside angle brackets.</summary>
		private static List<string> SplitTop(string s)
		{
			var list = new List<string>();
			int depth = 0, start = 0;
			for (int i = 0; i < s.Length; i++)
			{
				if (s[i] == '<') depth++;
				else if (s[i] == '>') depth--;
				else if (s[i] == ',' && depth == 0 && i + 1 < s.Length && s[i + 1] == ' ')
				{
					list.Add(s[start..i].Trim());
					start = i + 1;
				}
			}
			var last = s[start..].Trim();
			if (last.Length > 0)
				list.Add(last);
			return list;
		}

		/// <summary>The text the inventory prints for a type in a signature (see SigProvider): C# aliases for primitives, short names otherwise.</summary>
		internal static string Alias(Type t)
		{
			try
			{
				if (t.IsByRef) return Alias(t.GetElementType()) + "&";
				if (t.IsPointer) return Alias(t.GetElementType()) + "*";
				if (t.IsArray) return Alias(t.GetElementType()) + (t.GetArrayRank() == 1 ? "[]" : "[" + new string(',', t.GetArrayRank() - 1) + "]");
				if (t.IsGenericParameter) return (t.DeclaringMethod != null ? "!!" : "!") + t.GenericParameterPosition;
				switch (t.FullName)
				{
					case "System.Boolean": return "bool";
					case "System.Byte": return "byte";
					case "System.SByte": return "sbyte";
					case "System.Char": return "char";
					case "System.Int16": return "short";
					case "System.UInt16": return "ushort";
					case "System.Int32": return "int";
					case "System.UInt32": return "uint";
					case "System.Int64": return "long";
					case "System.UInt64": return "ulong";
					case "System.IntPtr": return "nint";
					case "System.UIntPtr": return "nuint";
					case "System.Single": return "float";
					case "System.Double": return "double";
					case "System.String": return "string";
					case "System.Object": return "object";
					case "System.Void": return "void";
				}
				string n = t.Name;
				int tick = n.IndexOf('`');
				if (tick >= 0)
					n = n[..tick];
				if (t.IsGenericType)
					n += "<" + string.Join(", ", t.GetGenericArguments().Select(Alias)) + ">";
				for (var d = t.DeclaringType; d != null; d = d.DeclaringType)
					n = d.Name + "." + n;
				return n;
			}
			catch (Exception e) when (e is FileNotFoundException or TypeLoadException or BadImageFormatException)
			{
				// A type of an assembly that is not in the resolver (a SkiaSharp type in a signature): it can match nothing.
				return "?";
			}
		}
	}
}
