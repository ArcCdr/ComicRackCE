using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ApiInventory
{
	/// <summary>Classifies the referenced types (enum, struct, interface, ...) by reading the reference assemblies they come from.</summary>
	internal static class RefKinds
	{
		public static int Apply(Inventory inv, IEnumerable<string> referenceAssemblies)
		{
			var kinds = new Dictionary<string, (string Kind, bool Flags)>(StringComparer.Ordinal);
			foreach (var path in referenceAssemblies)
			{
				if (Inventory.Area(Path.GetFileNameWithoutExtension(path)) == null)
					continue;
				using var pe = new PEReader(File.OpenRead(path));
				if (!pe.HasMetadata)
					continue;
				var md = pe.GetMetadataReader();
				foreach (var th in md.TypeDefinitions)
				{
					var td = md.GetTypeDefinition(th);
					kinds[FullName(md, th)] = (Classify(md, td), HasFlags(md, td));
				}
			}
			int found = 0;
			foreach (var t in inv.Types.Values)
			{
				if (kinds.TryGetValue(t.FullName, out var k))
				{
					t.Kind = k.Kind;
					t.IsFlags = k.Flags;
					found++;
				}
			}
			return found;
		}

		private static string FullName(MetadataReader md, TypeDefinitionHandle th)
		{
			var td = md.GetTypeDefinition(th);
			var outer = td.GetDeclaringType();
			if (!outer.IsNil)
				return FullName(md, outer) + "+" + md.GetString(td.Name);
			string ns = md.GetString(td.Namespace);
			return ns.Length > 0 ? ns + "." + md.GetString(td.Name) : md.GetString(td.Name);
		}

		private static string BaseName(MetadataReader md, TypeDefinition td)
		{
			if (td.BaseType.IsNil)
				return "";
			if (td.BaseType.Kind == HandleKind.TypeReference)
			{
				var tr = md.GetTypeReference((TypeReferenceHandle)td.BaseType);
				return md.GetString(tr.Namespace) + "." + md.GetString(tr.Name);
			}
			if (td.BaseType.Kind == HandleKind.TypeDefinition)
				return FullName(md, (TypeDefinitionHandle)td.BaseType);
			return "";
		}

		private static string Classify(MetadataReader md, TypeDefinition td)
		{
			if ((td.Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Interface)
				return "interface";
			switch (BaseName(md, td))
			{
				case "System.Enum":
					return "enum";
				case "System.ValueType":
					return "struct";
				case "System.MulticastDelegate":
					return "delegate";
			}
			if ((td.Attributes & TypeAttributes.Abstract) != 0 && (td.Attributes & TypeAttributes.Sealed) != 0)
				return "static class";
			return "class";
		}

		private static bool HasFlags(MetadataReader md, TypeDefinition td)
		{
			foreach (var ah in td.GetCustomAttributes())
			{
				var attr = md.GetCustomAttribute(ah);
				if (attr.Constructor.Kind != HandleKind.MemberReference)
					continue;
				var mr = md.GetMemberReference((MemberReferenceHandle)attr.Constructor);
				if (mr.Parent.Kind != HandleKind.TypeReference)
					continue;
				var tr = md.GetTypeReference((TypeReferenceHandle)mr.Parent);
				if (md.GetString(tr.Name) == "FlagsAttribute" && md.GetString(tr.Namespace) == "System")
					return true;
			}
			return false;
		}
	}
}
