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
	/// <summary>
	/// The scan of a .NET assembly that ships with a plugin (P4.6a, MACOS_NATIVE_PATH.md section 5.9.9 step 3): the member references of the metadata tell which types and members of
	/// <c>System.Windows.Forms</c> and <c>System.Drawing</c> the assembly binds to, which is what the generated surface has to provide with the same signatures. Also read: the assemblies it references,
	/// P/Invoke declarations (the import table), window procedure overrides, COM import types and Windows paths in string literals. Nothing is loaded or run. The use counts are member
	/// references of the metadata (each distinct member counts once per assembly), not call sites.
	/// </summary>
	public sealed class AssemblyScanner
	{
		private readonly ApiCatalog catalog;

		public AssemblyScanner(ApiCatalog catalog)
		{
			this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
		}

		/// <summary>Scans the bytes of an assembly into <paramref name="unit"/> (path and package are the caller's).</summary>
		public void Scan(byte[] data, ScanUnit unit)
		{
			unit.Kind = "assembly";
			PEReader pe = null;
			bool hasMetadata;
			try
			{
				pe = new PEReader(new MemoryStream(data, writable: false));
				hasMetadata = pe.HasMetadata;
			}
			catch (BadImageFormatException)
			{
				pe?.Dispose();
				unit.Kind = "native";
				unit.Parsed = false;
				unit.Tier = "?";
				unit.Error = "Not a Windows executable file.";
				return;
			}
			using (pe)
			{
				if (!hasMetadata)
				{
					unit.Kind = "native";
					string file = Path.GetFileName(unit.Path ?? "library");
					unit.AddFinding(new ScanFinding
					{
						Severity = "warning",
						Kind = "native-binary",
						Symbol = file,
						Message = "Contains the native Windows program or library `" + file + "` (not a .NET assembly); it cannot run on macOS, so what depends on it is unavailable.",
					});
					unit.Tier = "none";
					return;
				}
				try
				{
					Read(pe.GetMetadataReader(), unit);
				}
				catch (BadImageFormatException ex)
				{
					unit.Parsed = false;
					unit.Tier = "?";
					unit.Error = "The metadata is damaged: " + ex.Message;
					return;
				}
			}
			unit.Tier = ApiTiers.ForUnit(catalog, unit);
		}

		private void Read(MetadataReader md, ScanUnit unit)
		{
			foreach (AssemblyReferenceHandle h in md.AssemblyReferences)
			{
				string name = md.GetString(md.GetAssemblyReference(h).Name);
				if (!unit.References.Any(r => r.Name == name))
				{
					unit.References.Add(new ScanReference { Name = name, Kind = WindowsOnlyApi.ClassifyAssembly(name) });
				}
			}

			// Types of the surface the assembly names at all (base types, fields, signatures).
			foreach (TypeReferenceHandle h in md.TypeReferences)
			{
				string full = MetadataNames.TypeReference(md, h, out _);
				UseType(unit, full);
			}

			foreach (MemberReferenceHandle h in md.MemberReferences)
			{
				MemberReference mr = md.GetMemberReference(h);
				if (mr.Parent.Kind != HandleKind.TypeReference && mr.Parent.Kind != HandleKind.TypeSpecification)
				{
					continue;
				}
				string typeName = MetadataNames.Entity(md, mr.Parent, out _);
				string member = md.GetString(mr.Name);
				string signature = Signature(mr);
				string declaring = typeName;
				if (ApiCatalog.InSurface(Namespace(typeName)))
				{
					ApiType type = catalog.Type(typeName);
					member = Accessor(member, type);
					if (type != null && catalog.FindMember(type, member, out ApiType found) != null)
					{
						declaring = found.FullName;
					}
					unit.UseMember(declaring, member, signature);
				}
				ApiType declaringType = catalog.Type(declaring);
				string reason = declaringType != null ? catalog.MemberReason(declaringType, member) : WindowsOnlyApi.MemberReason(declaring, member);
				if (reason != null)
				{
					unit.AddFinding(ScanFinding.WindowsOnly("windows-member", ShortName(declaring) + "." + member, 0, reason, "plugin"));
				}
			}

			foreach (TypeDefinitionHandle h in md.TypeDefinitions)
			{
				TypeDefinition td = md.GetTypeDefinition(h);
				if ((td.Attributes & TypeAttributes.Import) != 0)
				{
					unit.AddFinding(ScanFinding.WindowsOnly("com", MetadataNames.TypeDefinition(md, h), 0, "a COM import type", "plugin"));
				}
			}

			foreach (MethodDefinitionHandle h in md.MethodDefinitions)
			{
				MethodDefinition m = md.GetMethodDefinition(h);
				MethodImport import = m.GetImport();
				if (!import.Module.IsNil)
				{
					string module = md.GetString(md.GetModuleReference(import.Module).Name);
					string entry = import.Name.IsNil ? md.GetString(m.Name) : md.GetString(import.Name);
					string symbol = Path.GetFileNameWithoutExtension(module.Replace('\\', '/')) + "." + entry;
					if (WindowsOnlyApi.IsWindowsLibrary(module))
					{
						unit.AddFinding(ScanFinding.WindowsOnly("p-invoke", symbol, 0, "a native Windows library", "plugin"));
					}
					else
					{
						unit.AddFinding(new ScanFinding
						{
							Severity = "warning",
							Kind = "p-invoke",
							Symbol = symbol,
							Message = "Calls the native library function `" + symbol + "`; it must exist on macOS for this plugin to run.",
						});
					}
					continue;
				}
				string name = md.GetString(m.Name);
				if ((name == "WndProc" || name == "DefWndProc") && (m.Attributes & MethodAttributes.Virtual) != 0)
				{
					string owner = MetadataNames.TypeDefinition(md, m.GetDeclaringType());
					unit.AddFinding(ScanFinding.WindowsOnly("wndproc", ShortName(owner) + "." + name, 0, "window message handling", "plugin"));
				}
			}

			Strings(md, unit);
		}

		/// <summary>The property or event a compiled accessor call stands for (<c>set_Text</c> is the property <c>Text</c>); the name itself when the catalog has a method of that name.</summary>
		private string Accessor(string name, ApiType type)
		{
			foreach (string prefix in new[] { "get_", "set_", "add_", "remove_" })
			{
				if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal) && char.IsUpper(name[prefix.Length]))
				{
					string stripped = name.Substring(prefix.Length);
					if (type == null)
					{
						return stripped;
					}
					if (catalog.FindMember(type, name, out _) != null)
					{
						return name;
					}
					return catalog.FindMember(type, stripped, out _) != null ? stripped : name;
				}
			}
			return name;
		}

		private void UseType(ScanUnit unit, string full)
		{
			if (ApiCatalog.InSurface(Namespace(full)))
			{
				unit.UseType(full);
			}
			string reason = WindowsOnlyApi.TypeReason(full);
			ApiType type = catalog.Type(full);
			if (reason == null && type != null && ApiTiers.Of(catalog, type) == "C")
			{
				reason = "unsupported on macOS";
			}
			if (reason != null)
			{
				unit.AddFinding(ScanFinding.WindowsOnly("windows-type", ShortName(full), 0, reason, "plugin"));
			}
		}

		private static void Strings(MetadataReader md, ScanUnit unit)
		{
			UserStringHandle h = MetadataTokens.UserStringHandle(0);
			int seen = 0;
			while (true)
			{
				h = md.GetNextHandle(h);
				if (h.IsNil || ++seen > 200000)
				{
					break;
				}
				string s = md.GetUserString(h);
				string warning = WindowsOnlyApi.StringWarning(s);
				if (warning != null)
				{
					unit.AddFinding(new ScanFinding { Severity = "warning", Kind = "windows-path", Symbol = warning, Message = "Contains a " + warning + "; it does not exist on macOS." });
				}
				if (s.IndexOf("window.external", StringComparison.Ordinal) >= 0)
				{
					HtmlChecks.Check(s, 0, unit);
				}
			}
		}

		private static string Namespace(string full)
		{
			int plus = full.IndexOf('+');
			string outer = plus < 0 ? full : full.Substring(0, plus);
			int dot = outer.LastIndexOf('.');
			return dot < 0 ? string.Empty : outer.Substring(0, dot);
		}

		private static string ShortName(string full)
		{
			string ns = Namespace(full);
			return ns.Length == 0 ? full : full.Substring(ns.Length + 1).Replace('+', '.');
		}

		private static string Signature(MemberReference mr)
		{
			try
			{
				if (mr.GetKind() == MemberReferenceKind.Field)
				{
					return Short(mr.DecodeFieldSignature(MetadataNames.Instance, null));
				}
				MethodSignature<string> sig = mr.DecodeMethodSignature(MetadataNames.Instance, null);
				return "(" + string.Join(", ", sig.ParameterTypes.Select(Short)) + ") -> " + Short(sig.ReturnType);
			}
			catch (BadImageFormatException)
			{
				return null;
			}
		}

		private static string Short(string type)
		{
			if (type.StartsWith("System.", StringComparison.Ordinal) && type.IndexOf('.', 7) < 0)
			{
				return type.Substring(7);
			}
			int dot = type.LastIndexOf('.');
			return dot < 0 ? type : type.Substring(dot + 1);
		}
	}
}
