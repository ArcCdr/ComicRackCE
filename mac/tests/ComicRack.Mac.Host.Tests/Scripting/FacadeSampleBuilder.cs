using System;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>
	/// Writes a tiny assembly shaped like a generated facade (mac/tools/facade-gen), without a compiler: the internal class <c>FacadeStub</c> with <c>Member(string)</c>, and the public class
	/// <c>Thing</c> whose generated members end in <c>call FacadeStub.Member; throw</c>: the constructor, <c>Stubbed()</c>, the getter of the property <c>Name</c>, one of two overloads of <c>Mixed</c>, and a
	/// static read-only field <c>Default</c> (no static constructor); <c>Real()</c>, <c>Mixed(int)</c> and the instance field <c>Plain</c> are ordinary.
	/// </summary>
	internal static class FacadeSampleBuilder
	{
		public static byte[] Build(bool withFacadeStub = true)
		{
			var md = new MetadataBuilder();
			md.AddModule(0, md.GetOrAddString("Sample.dll"), md.GetOrAddGuid(Guid.Parse("aaaaaaaa-2222-3333-4444-555555555555")), default, default);
			md.AddAssembly(md.GetOrAddString("Sample.Facade"), new Version(10, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.None);
			AssemblyReferenceHandle runtime = md.AddAssemblyReference(md.GetOrAddString("System.Runtime"), new Version(10, 0, 0, 0), default, default, 0, default);
			TypeReferenceHandle obj = md.AddTypeReference(runtime, md.GetOrAddString("System"), md.GetOrAddString("Object"));
			TypeReferenceHandle exception = md.AddTypeReference(runtime, md.GetOrAddString("System"), md.GetOrAddString("Exception"));
			TypeReferenceHandle str = md.AddTypeReference(runtime, md.GetOrAddString("System"), md.GetOrAddString("String"));

			var il = new BlobBuilder();
			var bodies = new MethodBodyStreamEncoder(il);

			int Body(Action<InstructionEncoder> write)
			{
				var code = new BlobBuilder();
				var e = new InstructionEncoder(code);
				write(e);
				return bodies.AddMethodBody(e, maxStack: 2, localVariablesSignature: default, attributes: MethodBodyAttributes.None);
			}

			BlobHandle Sig(bool instance, Action<ReturnTypeEncoder> ret, Action<ParametersEncoder> parameters, int count)
			{
				var b = new BlobBuilder();
				new BlobEncoder(b).MethodSignature(isInstanceMethod: instance).Parameters(count, ret, parameters);
				return md.GetOrAddBlob(b);
			}

			int plainBody = Body(e => e.OpCode(ILOpCode.Ret));
			int memberBody = Body(e =>
			{
				e.OpCode(ILOpCode.Ldnull);
				e.OpCode(ILOpCode.Ret);
			});

			md.AddTypeDefinition(default, default, md.GetOrAddString("<Module>"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

			// FacadeStub.Member(string): the first method definition
			MethodDefinitionHandle member = default;
			if (withFacadeStub)
			{
				member = md.AddMethodDefinition(MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig, MethodImplAttributes.IL, md.GetOrAddString("Member"),
					Sig(false, r => r.Type().Type(exception, false), p => p.AddParameter().Type().Type(str, false), 1), memberBody, default);
				md.AddTypeDefinition(TypeAttributes.NotPublic | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed, default, md.GetOrAddString("FacadeStub"), obj,
					MetadataTokens.FieldDefinitionHandle(1), member);
			}

			int stubBody = withFacadeStub ? Body(e =>
			{
				e.OpCode(ILOpCode.Ldnull);
				e.Call(member);
				e.OpCode(ILOpCode.Throw);
			}) : plainBody;

			MethodDefinitionHandle first = md.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.HideBySig, MethodImplAttributes.IL,
				md.GetOrAddString(".ctor"), Sig(true, r => r.Void(), p => { }, 0), stubBody, default);
			md.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.HideBySig, MethodImplAttributes.IL, md.GetOrAddString("Stubbed"), Sig(true, r => r.Void(), p => { }, 0), stubBody, default);
			md.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.HideBySig, MethodImplAttributes.IL, md.GetOrAddString("Real"), Sig(true, r => r.Void(), p => { }, 0), plainBody, default);
			MethodDefinitionHandle getter = md.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, MethodImplAttributes.IL, md.GetOrAddString("get_Name"),
				Sig(true, r => r.Type().Type(str, false), p => { }, 0), stubBody, default);
			md.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.HideBySig, MethodImplAttributes.IL, md.GetOrAddString("Mixed"), Sig(true, r => r.Void(), p => { }, 0), stubBody, default);
			md.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.HideBySig, MethodImplAttributes.IL, md.GetOrAddString("Mixed"), Sig(true, r => r.Void(), p => p.AddParameter().Type().Int32(), 1), plainBody, default);

			var fieldSig = new BlobBuilder();
			new BlobEncoder(fieldSig).Field().Type().Int32();
			FieldDefinitionHandle defaultField = md.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly, md.GetOrAddString("Default"), md.GetOrAddBlob(fieldSig));
			md.AddFieldDefinition(FieldAttributes.Public, md.GetOrAddString("Plain"), md.GetOrAddBlob(fieldSig));

			TypeDefinitionHandle thing = md.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class, md.GetOrAddString("Sample.Facade"), md.GetOrAddString("Thing"), obj, defaultField, first);

			var propertySig = new BlobBuilder();
			new BlobEncoder(propertySig).PropertySignature(isInstanceProperty: true).Parameters(0, r => r.Type().Type(str, false), p => { });
			PropertyDefinitionHandle name = md.AddProperty(PropertyAttributes.None, md.GetOrAddString("Name"), md.GetOrAddBlob(propertySig));
			md.AddPropertyMap(thing, name);
			md.AddMethodSemantics(name, MethodSemanticsAttributes.Getter, getter);

			var peBuilder = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(md), il, flags: CorFlags.ILOnly,
				deterministicIdProvider: _ => new BlobContentId(Guid.Parse("bbbbbbbb-7777-8888-9999-000000000000"), 0x04030202));
			var output = new BlobBuilder();
			peBuilder.Serialize(output);
			return output.ToArray();
		}
	}
}
