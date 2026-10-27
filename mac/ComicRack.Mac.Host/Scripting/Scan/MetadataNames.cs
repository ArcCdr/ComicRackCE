using System;
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ComicRack.Mac.Host.Scripting.Scan
{
	/// <summary>
	/// Names of types and signatures read from assembly metadata without loading the assembly (P4.6a): full CLR names (<c>System.Windows.Forms.Control+ControlCollection</c>),
	/// the same for reference assemblies, facades and plugin assemblies. A generic instantiation is named by its definition (<c>System.Collections.Generic.List`1</c>).
	/// </summary>
	internal sealed class MetadataNames : ISignatureTypeProvider<string, object>
	{
		public static readonly MetadataNames Instance = new MetadataNames();

		/// <summary>The full name of a type definition (nested types as <c>Outer+Inner</c>).</summary>
		public static string TypeDefinition(MetadataReader md, TypeDefinitionHandle handle)
		{
			TypeDefinition td = md.GetTypeDefinition(handle);
			string name = md.GetString(td.Name);
			TypeDefinitionHandle outer = td.GetDeclaringType();
			if (!outer.IsNil)
			{
				return TypeDefinition(md, outer) + "+" + name;
			}
			string ns = md.GetString(td.Namespace);
			return ns.Length > 0 ? ns + "." + name : name;
		}

		/// <summary>The full name of a type reference and the assembly (or module) it resolves in.</summary>
		public static string TypeReference(MetadataReader md, TypeReferenceHandle handle, out string scope)
		{
			TypeReference tr = md.GetTypeReference(handle);
			string name = md.GetString(tr.Name);
			if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
			{
				return TypeReference(md, (TypeReferenceHandle)tr.ResolutionScope, out scope) + "+" + name;
			}
			switch (tr.ResolutionScope.Kind)
			{
			case HandleKind.AssemblyReference:
				scope = md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)tr.ResolutionScope).Name);
				break;
			case HandleKind.ModuleReference:
				scope = md.GetString(md.GetModuleReference((ModuleReferenceHandle)tr.ResolutionScope).Name);
				break;
			default:
				scope = null;
				break;
			}
			string ns = md.GetString(tr.Namespace);
			return ns.Length > 0 ? ns + "." + name : name;
		}

		/// <summary>The full name of a type definition, reference or specification; <paramref name="scope"/> is the assembly of a reference, null for others.</summary>
		public static string Entity(MetadataReader md, EntityHandle handle, out string scope)
		{
			scope = null;
			switch (handle.Kind)
			{
			case HandleKind.TypeDefinition:
				return TypeDefinition(md, (TypeDefinitionHandle)handle);
			case HandleKind.TypeReference:
				return TypeReference(md, (TypeReferenceHandle)handle, out scope);
			case HandleKind.TypeSpecification:
			{
				string name = md.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(Instance, null);
				return name;
			}
			default:
				return null;
			}
		}

		public string GetPrimitiveType(PrimitiveTypeCode typeCode)
		{
			switch (typeCode)
			{
			case PrimitiveTypeCode.Void: return "System.Void";
			case PrimitiveTypeCode.Boolean: return "System.Boolean";
			case PrimitiveTypeCode.Char: return "System.Char";
			case PrimitiveTypeCode.SByte: return "System.SByte";
			case PrimitiveTypeCode.Byte: return "System.Byte";
			case PrimitiveTypeCode.Int16: return "System.Int16";
			case PrimitiveTypeCode.UInt16: return "System.UInt16";
			case PrimitiveTypeCode.Int32: return "System.Int32";
			case PrimitiveTypeCode.UInt32: return "System.UInt32";
			case PrimitiveTypeCode.Int64: return "System.Int64";
			case PrimitiveTypeCode.UInt64: return "System.UInt64";
			case PrimitiveTypeCode.Single: return "System.Single";
			case PrimitiveTypeCode.Double: return "System.Double";
			case PrimitiveTypeCode.String: return "System.String";
			case PrimitiveTypeCode.TypedReference: return "System.TypedReference";
			case PrimitiveTypeCode.IntPtr: return "System.IntPtr";
			case PrimitiveTypeCode.UIntPtr: return "System.UIntPtr";
			case PrimitiveTypeCode.Object: return "System.Object";
			default: return typeCode.ToString();
			}
		}

		public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => TypeDefinition(reader, handle);

		public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => TypeReference(reader, handle, out _);

		public string GetTypeFromSpecification(MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

		public string GetSZArrayType(string elementType) => elementType + "[]";

		public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', Math.Max(0, shape.Rank - 1)) + "]";

		public string GetByReferenceType(string elementType) => elementType;

		public string GetPointerType(string elementType) => elementType + "*";

		public string GetPinnedType(string elementType) => elementType;

		public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

		public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType;

		public string GetGenericMethodParameter(object genericContext, int index) => "!!" + index;

		public string GetGenericTypeParameter(object genericContext, int index) => "!" + index;

		public string GetFunctionPointerType(MethodSignature<string> signature) => "method*";
	}
}
