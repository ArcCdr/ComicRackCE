using System;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>
	/// Writes a small .NET assembly with the metadata of a compiled WinForms plugin, without a compiler: a class that derives from Form, member references to the properties and methods the plugin
	/// calls (as compiled accessors: <c>set_Text</c>), a window procedure override, a P/Invoke declaration into a Windows library, a reference to a WPF type and a Windows path in a string literal.
	/// </summary>
	internal static class ScanAssemblyBuilder
	{
		public static byte[] Build(bool withNativeCalls = true)
		{
			var md = new MetadataBuilder();
			md.AddModule(0, md.GetOrAddString("Plugin.dll"), md.GetOrAddGuid(Guid.Parse("11111111-2222-3333-4444-555555555555")), default, default);
			md.AddAssembly(md.GetOrAddString("Plugin"), new Version(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.None);

			AssemblyReferenceHandle runtime = md.AddAssemblyReference(md.GetOrAddString("System.Runtime"), new Version(4, 0, 0, 0), default, default, 0, default);
			AssemblyReferenceHandle forms = md.AddAssemblyReference(md.GetOrAddString("System.Windows.Forms"), new Version(4, 0, 0, 0), default, default, 0, default);
			AssemblyReferenceHandle drawing = md.AddAssemblyReference(md.GetOrAddString("System.Drawing"), new Version(4, 0, 0, 0), default, default, 0, default);
			AssemblyReferenceHandle wpf = md.AddAssemblyReference(md.GetOrAddString("PresentationFramework"), new Version(4, 0, 0, 0), default, default, 0, default);

			TypeReferenceHandle obj = md.AddTypeReference(runtime, md.GetOrAddString("System"), md.GetOrAddString("Object"));
			TypeReferenceHandle str = md.AddTypeReference(runtime, md.GetOrAddString("System"), md.GetOrAddString("String"));
			TypeReferenceHandle form = md.AddTypeReference(forms, md.GetOrAddString("System.Windows.Forms"), md.GetOrAddString("Form"));
			TypeReferenceHandle control = md.AddTypeReference(forms, md.GetOrAddString("System.Windows.Forms"), md.GetOrAddString("Control"));
			TypeReferenceHandle button = md.AddTypeReference(forms, md.GetOrAddString("System.Windows.Forms"), md.GetOrAddString("Button"));
			TypeReferenceHandle messageBox = md.AddTypeReference(forms, md.GetOrAddString("System.Windows.Forms"), md.GetOrAddString("MessageBox"));
			TypeReferenceHandle dialogResult = md.AddTypeReference(forms, md.GetOrAddString("System.Windows.Forms"), md.GetOrAddString("DialogResult"));
			TypeReferenceHandle message = md.AddTypeReference(forms, md.GetOrAddString("System.Windows.Forms"), md.GetOrAddString("Message"));
			TypeReferenceHandle color = md.AddTypeReference(drawing, md.GetOrAddString("System.Drawing"), md.GetOrAddString("Color"));
			md.AddTypeReference(wpf, md.GetOrAddString("System.Windows.Controls"), md.GetOrAddString("Grid"));

			// Member references, as a compiler writes them: accessors for properties.
			md.AddMemberReference(button, md.GetOrAddString(".ctor"), Method(md, true, r => r.Void()));
			md.AddMemberReference(button, md.GetOrAddString("set_Text"), Method(md, true, r => r.Void(), Class(str)));
			md.AddMemberReference(control, md.GetOrAddString("get_Controls"), Method(md, true, r => Class(obj)(r.Type())));
			md.AddMemberReference(messageBox, md.GetOrAddString("Show"), Method(md, false, r => Value(dialogResult)(r.Type()), Class(str)));
			md.AddMemberReference(dialogResult, md.GetOrAddString("OK"), Field(md, dialogResult));
			md.AddMemberReference(color, md.GetOrAddString("get_Red"), Method(md, false, r => Value(color)(r.Type())));

			// The class PluginForm : Form with a window procedure override and a P/Invoke.
			var il = new BlobBuilder();
			var bodies = new MethodBodyStreamEncoder(il);
			var code = new BlobBuilder();
			var encoder = new InstructionEncoder(code);
			encoder.OpCode(ILOpCode.Ret);
			int body = bodies.AddMethodBody(encoder, maxStack: 1, localVariablesSignature: default, attributes: MethodBodyAttributes.None);

			md.AddTypeDefinition(default, default, md.GetOrAddString("<Module>"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
			MethodDefinitionHandle first = md.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static, MethodImplAttributes.IL, md.GetOrAddString("Run"), Method(md, false, r => r.Void()), body, default);
			md.AddMethodDefinition(MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig, MethodImplAttributes.IL, md.GetOrAddString("WndProc"), Method(md, true, r => r.Void(), Value(message)), body, default);
			if (withNativeCalls)
			{
				MethodDefinitionHandle native = md.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PinvokeImpl, MethodImplAttributes.PreserveSig,
					md.GetOrAddString("SendMessageNative"), Method(md, false, r => r.Void(), Class(str)), -1, default);
				md.AddMethodImport(native, MethodImportAttributes.CallingConventionWinApi, md.GetOrAddString("SendMessageW"), md.AddModuleReference(md.GetOrAddString("user32.dll")));
			}
			md.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit, md.GetOrAddString("Plugin"), md.GetOrAddString("PluginForm"), form, MetadataTokens.FieldDefinitionHandle(1), first);

			md.GetOrAddUserString("C:\\Program Files\\Plugin\\data.xml");

			var peBuilder = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(md), il, flags: CorFlags.ILOnly,
				deterministicIdProvider: _ => new BlobContentId(Guid.Parse("66666666-7777-8888-9999-000000000000"), 0x04030201));
			var output = new BlobBuilder();
			peBuilder.Serialize(output);
			return output.ToArray();
		}

		/// <summary>The same file without the CLR header entry: a native Windows image.</summary>
		public static byte[] WithoutClrHeader(byte[] managed)
		{
			byte[] copy = (byte[])managed.Clone();
			using var pe = new PEReader(new System.IO.MemoryStream(managed));
			PEHeaders headers = pe.PEHeaders;
			int dataDirectories = headers.PEHeaderStartOffset + (headers.PEHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
			Array.Clear(copy, dataDirectories + 14 * 8, 8);
			return copy;
		}

		private static Func<SignatureTypeEncoder, SignatureTypeEncoder> Class(EntityHandle type) => e =>
		{
			e.Type(type, false);
			return e;
		};

		private static Func<SignatureTypeEncoder, SignatureTypeEncoder> Value(EntityHandle type) => e =>
		{
			e.Type(type, true);
			return e;
		};

		private static BlobHandle Method(MetadataBuilder md, bool instance, Action<ReturnTypeEncoder> returnType, params Func<SignatureTypeEncoder, SignatureTypeEncoder>[] parameters)
		{
			var blob = new BlobBuilder();
			new BlobEncoder(blob).MethodSignature(isInstanceMethod: instance).Parameters(parameters.Length, out ReturnTypeEncoder rt, out ParametersEncoder ps);
			returnType(rt);
			foreach (var p in parameters)
			{
				p(ps.AddParameter().Type());
			}
			return md.GetOrAddBlob(blob);
		}

		private static BlobHandle Field(MetadataBuilder md, EntityHandle type)
		{
			var blob = new BlobBuilder();
			new BlobEncoder(blob).Field().Type().Type(type, true);
			return md.GetOrAddBlob(blob);
		}
	}
}
