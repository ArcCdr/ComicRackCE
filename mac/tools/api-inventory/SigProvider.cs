using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ApiInventory
{
	/// <summary>Decodes signatures to display text and reports every type reference it meets.</summary>
	internal sealed class SigProvider : ISignatureTypeProvider<string, object>
	{
		private readonly MetadataReader md;

		public readonly List<(string Scope, string Name, byte Raw)> Found = new();

		public SigProvider(MetadataReader md)
		{
			this.md = md;
		}

		public static string Short(string fullName)
		{
			int i = fullName.LastIndexOf('.');
			return (i >= 0 ? fullName[(i + 1)..] : fullName).Replace('+', '.');
		}

		public (string Scope, string Name) ResolveTypeRef(TypeReferenceHandle h)
		{
			var tr = md.GetTypeReference(h);
			if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
			{
				var (s, outer) = ResolveTypeRef((TypeReferenceHandle)tr.ResolutionScope);
				return (s, outer + "+" + md.GetString(tr.Name));
			}
			string ns = md.GetString(tr.Namespace);
			string n = md.GetString(tr.Name);
			string full = ns.Length > 0 ? ns + "." + n : n;
			string scope = tr.ResolutionScope.Kind switch
			{
				HandleKind.AssemblyReference => md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)tr.ResolutionScope).Name),
				HandleKind.ModuleReference => "module:" + md.GetString(md.GetModuleReference((ModuleReferenceHandle)tr.ResolutionScope).Name),
				_ => "(self)",
			};
			return (scope, full);
		}

		public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
		{
			PrimitiveTypeCode.Boolean => "bool",
			PrimitiveTypeCode.Byte => "byte",
			PrimitiveTypeCode.SByte => "sbyte",
			PrimitiveTypeCode.Char => "char",
			PrimitiveTypeCode.Int16 => "short",
			PrimitiveTypeCode.UInt16 => "ushort",
			PrimitiveTypeCode.Int32 => "int",
			PrimitiveTypeCode.UInt32 => "uint",
			PrimitiveTypeCode.Int64 => "long",
			PrimitiveTypeCode.UInt64 => "ulong",
			PrimitiveTypeCode.IntPtr => "nint",
			PrimitiveTypeCode.UIntPtr => "nuint",
			PrimitiveTypeCode.Single => "float",
			PrimitiveTypeCode.Double => "double",
			PrimitiveTypeCode.String => "string",
			PrimitiveTypeCode.Object => "object",
			PrimitiveTypeCode.Void => "void",
			PrimitiveTypeCode.TypedReference => "typedref",
			_ => typeCode.ToString(),
		};

		public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
		{
			var td = reader.GetTypeDefinition(handle);
			return reader.GetString(td.Name);
		}

		public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
		{
			var (scope, name) = ResolveTypeRef(handle);
			Found.Add((scope, name, rawTypeKind));
			return Short(name);
		}

		public string GetTypeFromSpecification(MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
			=> reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

		public string GetSZArrayType(string elementType) => elementType + "[]";

		public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";

		public string GetByReferenceType(string elementType) => elementType + "&";

		public string GetPointerType(string elementType) => elementType + "*";

		public string GetPinnedType(string elementType) => elementType;

		public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

		public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
			=> genericType + "<" + string.Join(", ", typeArguments) + ">";

		public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";

		public string GetGenericMethodParameter(object genericContext, int index) => "!!" + index;

		public string GetGenericTypeParameter(object genericContext, int index) => "!" + index;
	}
}
