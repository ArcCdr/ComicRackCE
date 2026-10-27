using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace PatchIronPython
{
	/// <summary>
	/// Patches IronPython.dll 2.7.12 so that it runs on .NET 9 and later (spike S4, mac/reports/s4-ironpython.md).
	/// <c>NewTypeMaker.ImplementCustomTypeDescriptor</c> overrides every method of <c>ICustomTypeDescriptor</c> with a helper of the same
	/// name in <c>CustomTypeDescHelpers</c>. .NET 9 added four members with a default implementation (GetConverterFromRegisteredType,
	/// GetEventsFromRegisteredType, GetPropertiesFromRegisteredType, RequireRegisteredTypes) that have no helper, so every Python class
	/// that derives from anything but an old-style class fails with "Type doesn't have a method with a given name and signature".
	/// The patch makes <c>NewTypeMaker.ImplementCTDOverride(MethodInfo)</c> return at once for a method that is not abstract: the generated
	/// type then keeps the default implementation.
	///
	/// Usage:
	///   patch-ironpython FILE            patch FILE in place (nothing happens when it is patched already)
	///   patch-ironpython IN OUT          write the patched copy of IN to OUT
	///   patch-ironpython --check FILE    exit 0 if FILE is patched, 1 if it is not, 2 on an error
	/// Exit codes: 0 done (or already patched), 2 bad arguments or an error.
	/// </summary>
	public static class Program
	{
		private const string MakerType = "IronPython.Runtime.Types.NewTypeMaker";
		private const string MethodName = "ImplementCTDOverride";

		public static int Main(string[] args)
		{
			try
			{
				if (args.Length == 2 && args[0] == "--check")
				{
					bool patched = IsPatched(args[1]);
					Console.WriteLine(patched ? "patched" : "not patched");
					return patched ? 0 : 1;
				}
				if (args.Length == 1 && !args[0].StartsWith("--", StringComparison.Ordinal))
				{
					return Patch(args[0], args[0]);
				}
				if (args.Length == 2 && !args[0].StartsWith("--", StringComparison.Ordinal))
				{
					return Patch(args[0], args[1]);
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine("patch-ironpython: " + ex.Message);
				return 2;
			}
			Console.Error.WriteLine("Usage: patch-ironpython FILE | IN OUT | --check FILE");
			return 2;
		}

		/// <summary>Reads the assembly into memory first, so that the output may be the input file.</summary>
		private static ModuleDefinition Read(string path)
		{
			byte[] bytes = File.ReadAllBytes(path);
			// The resolver needs the neighbours (Microsoft.Dynamic, Microsoft.Scripting) to write enum constants.
			DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
			resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
			return ModuleDefinition.ReadModule(new MemoryStream(bytes), new ReaderParameters { AssemblyResolver = resolver });
		}

		private static MethodDefinition Find(ModuleDefinition module)
		{
			TypeDefinition maker = module.GetTypes().SingleOrDefault(t => t.FullName == MakerType)
				?? throw new InvalidOperationException(MakerType + " was not found: this is not IronPython 2.7.");
			return maker.Methods.SingleOrDefault(m => m.Name == MethodName)
				?? throw new InvalidOperationException(MakerType + "." + MethodName + " was not found.");
		}

		private static bool IsPatched(MethodDefinition method)
		{
			// the patch starts the method with: ldarg.1, callvirt MethodBase.get_IsAbstract, brtrue.s, ret
			var body = method.Body.Instructions;
			return body.Count > 3 && body[0].OpCode == OpCodes.Ldarg_1 && body[1].OpCode == OpCodes.Callvirt
				&& body[1].Operand is MethodReference called && called.Name == "get_IsAbstract";
		}

		public static bool IsPatched(string path)
		{
			using (ModuleDefinition module = Read(path))
			{
				return IsPatched(Find(module));
			}
		}

		public static int Patch(string input, string output)
		{
			using (ModuleDefinition module = Read(input))
			{
				MethodDefinition method = Find(module);
				if (IsPatched(method))
				{
					if (!string.Equals(Path.GetFullPath(input), Path.GetFullPath(output), StringComparison.Ordinal))
					{
						File.Copy(input, output, overwrite: true);
					}
					Console.WriteLine("already patched " + method.FullName);
					return 0;
				}
				ILProcessor il = method.Body.GetILProcessor();
				Instruction first = method.Body.Instructions[0];
				MethodReference isAbstract = module.ImportReference(typeof(System.Reflection.MethodBase).GetProperty("IsAbstract").GetGetMethod());
				// if (!m.IsAbstract) return;
				il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
				il.InsertBefore(first, il.Create(OpCodes.Callvirt, isAbstract));
				il.InsertBefore(first, il.Create(OpCodes.Brtrue_S, first));
				il.InsertBefore(first, il.Create(OpCodes.Ret));
				string temp = output + ".patching";
				try
				{
					module.Write(temp);
					File.Move(temp, output, overwrite: true);
				}
				finally
				{
					if (File.Exists(temp))
					{
						File.Delete(temp);
					}
				}
				Console.WriteLine("patched " + method.FullName);
				return 0;
			}
		}
	}
}
