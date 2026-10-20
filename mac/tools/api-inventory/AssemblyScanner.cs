using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ApiInventory
{
	/// <summary>Reads one assembly (and its portable PDB) and adds what it references to the inventory.</summary>
	internal sealed class AssemblyScanner : IDisposable
	{
		private static readonly Dictionary<ushort, OperandType> Operands = typeof(OpCodes)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(f => f.FieldType == typeof(OpCode))
			.Select(f => (OpCode)f.GetValue(null))
			.ToDictionary(o => (ushort)o.Value, o => o.OperandType);

		private readonly Inventory inv;
		private readonly string repoDir;
		private readonly PEReader pe;
		private readonly MetadataReader md;
		private readonly MetadataReaderProvider pdbProvider;
		private readonly MetadataReader pdb;
		private readonly SigProvider provider;
		private readonly Dictionary<MethodDefinitionHandle, PInvokeRec> pinvokeDefs = new();
		private readonly Dictionary<TypeDefinitionHandle, string> typeFiles = new();

		public string Name { get; }

		public int MethodBodies { get; private set; }

		public AssemblyScanner(Inventory inv, string dllPath, string repoDir)
		{
			this.inv = inv;
			this.repoDir = repoDir;
			pe = new PEReader(File.OpenRead(dllPath));
			md = pe.GetMetadataReader();
			Name = md.GetString(md.GetAssemblyDefinition().Name);
			string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
			if (!File.Exists(pdbPath))
				throw new FileNotFoundException("Portable PDB not found (needed for the file column)", pdbPath);
			pdbProvider = MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(pdbPath));
			pdb = pdbProvider.GetMetadataReader();
			provider = new SigProvider(md);
			inv.OwnAssemblies.Add(Name);
		}

		public void Dispose()
		{
			pdbProvider.Dispose();
			pe.Dispose();
		}

		public IEnumerable<string> DefinedTypeNames() => md.TypeDefinitions.Select(TypeName);

		public IEnumerable<string> AssemblyReferenceNames()
			=> md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name));

		/// <summary>Phase 1: P/Invoke declarations, so that calls from other assemblies can be attributed.</summary>
		public void CollectPInvokes()
		{
			foreach (var th in md.TypeDefinitions)
			{
				var td = md.GetTypeDefinition(th);
				string declaring = TypeName(th);
				foreach (var mh in td.GetMethods())
				{
					var m = md.GetMethodDefinition(mh);
					if ((m.Attributes & MethodAttributes.PinvokeImpl) == 0)
						continue;
					var import = m.GetImport();
					var parameters = m.DecodeSignature(provider, null).ParameterTypes;
					var rec = new PInvokeRec
					{
						Module = import.Module.IsNil ? "?" : md.GetString(md.GetModuleReference(import.Module).Name),
						EntryPoint = import.Name.IsNil ? md.GetString(m.Name) : md.GetString(import.Name),
						Declaring = declaring,
						Name = md.GetString(m.Name),
						Assembly = Name,
						ParamCount = parameters.Length,
						Sig = "(" + string.Join(", ", parameters) + ")",
					};
					pinvokeDefs[mh] = rec;
					inv.PInvokes.Add(rec);
					string key = Name + "|" + declaring + "|" + rec.Name;
					if (!inv.PInvokeByKey.TryGetValue(key, out var list))
						inv.PInvokeByKey[key] = list = new List<PInvokeRec>();
					list.Add(rec);
				}
			}
		}

		/// <summary>Phase 2: declarations and method bodies.</summary>
		public void Scan()
		{
			foreach (var th in md.TypeDefinitions)
				typeFiles[th] = MostCommonFile(md.GetTypeDefinition(th).GetMethods());

			foreach (var th in md.TypeDefinitions)
			{
				var td = md.GetTypeDefinition(th);
				string file = FileOfType(th);
				if (!td.BaseType.IsNil)
					Decl(td.BaseType, file);
				foreach (var ih in td.GetInterfaceImplementations())
					Decl(md.GetInterfaceImplementation(ih).Interface, file);
				foreach (var fh in td.GetFields())
				{
					provider.Found.Clear();
					md.GetFieldDefinition(fh).DecodeSignature(provider, null);
					Drain(file);
				}
				foreach (var ph in td.GetProperties())
				{
					provider.Found.Clear();
					md.GetPropertyDefinition(ph).DecodeSignature(provider, null);
					Drain(file);
				}
				foreach (var eh in td.GetEvents())
					Decl(md.GetEventDefinition(eh).Type, file);
				foreach (var mh in td.GetMethods())
					ScanMethod(mh);
			}
		}

		/// <summary>Phase 3 (after <see cref="Scan"/>): the file of each P/Invoke declaration's type.</summary>
		public void ResolveDeclFiles()
		{
			foreach (var (mh, rec) in pinvokeDefs)
			{
				string file = FileOfType(md.GetMethodDefinition(mh).GetDeclaringType());
				rec.DeclFile = file.StartsWith("(no code:", StringComparison.Ordinal) ? null : file;
			}
		}

		private void ScanMethod(MethodDefinitionHandle mh)
		{
			var m = md.GetMethodDefinition(mh);
			string file = FileOf(mh) ?? FileOfType(m.GetDeclaringType());
			provider.Found.Clear();
			m.DecodeSignature(provider, null);
			Drain(file);
			if (m.RelativeVirtualAddress == 0)
				return;

			var body = pe.GetMethodBody(m.RelativeVirtualAddress);
			MethodBodies++;
			if (!body.LocalSignature.IsNil)
			{
				provider.Found.Clear();
				md.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(provider, null);
				Drain(file);
			}
			var il = body.GetILBytes();
			int i = 0;
			while (i < il.Length)
			{
				ushort op = il[i++];
				if (op == 0xFE)
					op = (ushort)(0xFE00 | il[i++]);
				switch (Operands[op])
				{
					case OperandType.InlineNone:
						break;
					case OperandType.ShortInlineBrTarget:
					case OperandType.ShortInlineI:
					case OperandType.ShortInlineVar:
						i += 1;
						break;
					case OperandType.InlineVar:
						i += 2;
						break;
					case OperandType.InlineI:
					case OperandType.InlineBrTarget:
					case OperandType.ShortInlineR:
					case OperandType.InlineString:
					case OperandType.InlineSig:
						i += 4;
						break;
					case OperandType.InlineI8:
					case OperandType.InlineR:
						i += 8;
						break;
					case OperandType.InlineSwitch:
						i += 4 + 4 * BitConverter.ToInt32(il, i);
						break;
					case OperandType.InlineField:
					case OperandType.InlineMethod:
					case OperandType.InlineTok:
					case OperandType.InlineType:
						Token(MetadataTokens.EntityHandle(BitConverter.ToInt32(il, i)), file);
						i += 4;
						break;
					default:
						throw new InvalidOperationException("Unknown operand type for opcode 0x" + op.ToString("X"));
				}
			}
		}

		private void Token(EntityHandle h, string file)
		{
			switch (h.Kind)
			{
				case HandleKind.MemberReference:
					Member((MemberReferenceHandle)h, file);
					break;
				case HandleKind.MethodSpecification:
					{
						var spec = md.GetMethodSpecification((MethodSpecificationHandle)h);
						provider.Found.Clear();
						spec.DecodeSignature(provider, null);
						Drain(file);
						Token(spec.Method, file);
						break;
					}
				case HandleKind.MethodDefinition:
					if (pinvokeDefs.TryGetValue((MethodDefinitionHandle)h, out var rec))
					{
						rec.Calls++;
						Inventory.Add(rec.Files, file);
					}
					break;
				case HandleKind.TypeReference:
					{
						var (scope, name) = provider.ResolveTypeRef((TypeReferenceHandle)h);
						if (Inventory.Area(scope) != null)
						{
							var t = inv.GetType(scope, name, 0);
							t.IlUses++;
							Inventory.Add(t.Files, file);
						}
						break;
					}
				case HandleKind.TypeSpecification:
					{
						provider.Found.Clear();
						md.GetTypeSpecification((TypeSpecificationHandle)h).DecodeSignature(provider, null);
						foreach (var (scope, name, raw) in provider.Found)
						{
							if (Inventory.Area(scope) == null)
								continue;
							var t = inv.GetType(scope, name, raw);
							t.IlUses++;
							Inventory.Add(t.Files, file);
						}
						provider.Found.Clear();
						break;
					}
			}
		}

		private void Member(MemberReferenceHandle h, string file)
		{
			var mr = md.GetMemberReference(h);
			TypeReferenceHandle parent;
			switch (mr.Parent.Kind)
			{
				case HandleKind.TypeReference:
					parent = (TypeReferenceHandle)mr.Parent;
					break;
				case HandleKind.TypeSpecification:
					{
						// Generic instantiation: the arguments count as declaration-level uses; the member belongs to the generic type.
						var tsh = (TypeSpecificationHandle)mr.Parent;
						provider.Found.Clear();
						md.GetTypeSpecification(tsh).DecodeSignature(provider, null);
						Drain(file);
						var br = md.GetBlobReader(md.GetTypeSpecification(tsh).Signature);
						if (br.ReadByte() != 0x15)
							return;
						br.ReadByte();
						var th = br.ReadTypeHandle();
						if (th.Kind != HandleKind.TypeReference)
							return;
						parent = (TypeReferenceHandle)th;
						break;
					}
				default:
					return;
			}

			var (scope, name) = provider.ResolveTypeRef(parent);
			string area = Inventory.Area(scope);
			if (area == null)
			{
				if (inv.OwnAssemblies.Contains(scope) || scope.StartsWith("cYo.", StringComparison.Ordinal) || scope.StartsWith("ComicRack.", StringComparison.Ordinal))
					CrossAssemblyPInvoke(mr, scope, name, file);
				return;
			}

			var type = inv.GetType(scope, name, 0);
			type.MemberUses++;
			Inventory.Add(type.Files, file);

			string mname = md.GetString(mr.Name);
			provider.Found.Clear();
			string kind;
			string display;
			bool isStatic = false;
			string accessor = null;
			if (mr.GetKind() == MemberReferenceKind.Field)
			{
				kind = "field";
				display = mname + " : " + mr.DecodeFieldSignature(provider, null);
			}
			else
			{
				var sig = mr.DecodeMethodSignature(provider, null);
				isStatic = !sig.Header.IsInstance;
				string ps = string.Join(", ", sig.ParameterTypes);
				if (mname == ".ctor" || mname == ".cctor")
				{
					kind = "ctor";
					display = "(" + ps + ")";
					mname = ".ctor";
				}
				else if (mname.StartsWith("get_", StringComparison.Ordinal))
				{
					kind = "property";
					accessor = "get";
					mname = mname[4..];
					display = (sig.ParameterTypes.Length > 0 ? "[" + ps + "]" : "") + " : " + sig.ReturnType;
				}
				else if (mname.StartsWith("set_", StringComparison.Ordinal) && sig.ParameterTypes.Length > 0)
				{
					kind = "property";
					accessor = "set";
					mname = mname[4..];
					var idx = sig.ParameterTypes.Take(sig.ParameterTypes.Length - 1);
					display = (sig.ParameterTypes.Length > 1 ? "[" + string.Join(", ", idx) + "]" : "") + " : " + sig.ParameterTypes[^1];
				}
				else if (mname.StartsWith("add_", StringComparison.Ordinal) || mname.StartsWith("remove_", StringComparison.Ordinal))
				{
					kind = "event";
					accessor = mname.StartsWith("add_", StringComparison.Ordinal) ? "add" : "remove";
					mname = mname[(mname.IndexOf('_') + 1)..];
					display = " : " + string.Join(", ", sig.ParameterTypes);
				}
				else
				{
					kind = "method";
					display = (sig.GenericParameterCount > 0 ? "<" + sig.GenericParameterCount + ">" : "") + "(" + ps + ") : " + sig.ReturnType;
				}
			}

			string key = scope + "|" + name + "|" + kind + "|" + mname + "|" + display + "|" + isStatic;
			if (!inv.Members.TryGetValue(key, out var rec))
			{
				inv.Members[key] = rec = new MemberRec { Type = type, Kind = kind, Name = mname, Sig = display, IsStatic = isStatic };
				Drain(file);
			}
			provider.Found.Clear();
			if (accessor != null)
				rec.Accessors.Add(accessor);
			rec.Calls++;
			Inventory.Add(rec.Files, file);
		}

		private void CrossAssemblyPInvoke(MemberReference mr, string scope, string typeName, string file)
		{
			if (mr.GetKind() != MemberReferenceKind.Method)
				return;
			string key = scope + "|" + typeName + "|" + md.GetString(mr.Name);
			if (!inv.PInvokeByKey.TryGetValue(key, out var list))
				return;
			int count = mr.DecodeMethodSignature(provider, null).ParameterTypes.Length;
			provider.Found.Clear();
			var rec = list.FirstOrDefault(r => r.ParamCount == count) ?? list[0];
			rec.Calls++;
			Inventory.Add(rec.Files, file);
		}

		/// <summary>Records the type references of a base type, interface or event type.</summary>
		private void Decl(EntityHandle h, string file)
		{
			provider.Found.Clear();
			switch (h.Kind)
			{
				case HandleKind.TypeReference:
					var (scope, name) = provider.ResolveTypeRef((TypeReferenceHandle)h);
					provider.Found.Add((scope, name, 0x12));
					break;
				case HandleKind.TypeSpecification:
					md.GetTypeSpecification((TypeSpecificationHandle)h).DecodeSignature(provider, null);
					break;
			}
			Drain(file);
		}

		private void Drain(string file)
		{
			foreach (var (scope, name, raw) in provider.Found)
			{
				if (Inventory.Area(scope) == null)
					continue;
				var t = inv.GetType(scope, name, raw);
				t.DeclUses++;
				Inventory.Add(t.Files, file);
			}
			provider.Found.Clear();
		}

		private string TypeName(TypeDefinitionHandle th)
		{
			var td = md.GetTypeDefinition(th);
			var declaring = td.GetDeclaringType();
			if (!declaring.IsNil)
				return TypeName(declaring) + "+" + md.GetString(td.Name);
			string ns = md.GetString(td.Namespace);
			return ns.Length > 0 ? ns + "." + md.GetString(td.Name) : md.GetString(td.Name);
		}

		private string FileOf(MethodDefinitionHandle mh)
		{
			var mdi = pdb.GetMethodDebugInformation(mh.ToDebugInformationHandle());
			var dh = mdi.Document;
			if (dh.IsNil)
			{
				foreach (var sp in mdi.GetSequencePoints())
				{
					if (!sp.Document.IsNil)
					{
						dh = sp.Document;
						break;
					}
				}
			}
			if (dh.IsNil)
				return null;
			string path = pdb.GetString(pdb.GetDocument(dh).Name);
			return path.StartsWith(repoDir + "/", StringComparison.Ordinal) ? path[(repoDir.Length + 1)..] : path;
		}

		private string MostCommonFile(IEnumerable<MethodDefinitionHandle> methods)
		{
			return methods.Select(FileOf).Where(f => f != null).GroupBy(f => f)
				.OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
		}

		private string FileOfType(TypeDefinitionHandle th)
		{
			var start = th;
			while (!th.IsNil)
			{
				if (typeFiles.TryGetValue(th, out var f) && f != null)
					return f;
				th = md.GetTypeDefinition(th).GetDeclaringType();
			}
			// Interfaces, enums and abstract-only types have no method bodies, so the PDB has no document for them.
			return "(no code: " + TypeName(start) + ")";
		}
	}
}
