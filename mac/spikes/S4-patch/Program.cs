using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace S4
{
	/// <summary>
	/// Patches IronPython.dll 2.7.12 so that it runs on .NET 9 and later. NewTypeMaker.ImplementCustomTypeDescriptor overrides every
	/// method of ICustomTypeDescriptor with a helper of the same name in CustomTypeDescHelpers. .NET 9 added four members with a
	/// default implementation (GetConverterFromRegisteredType, GetEventsFromRegisteredType, GetPropertiesFromRegisteredType,
	/// RequireRegisteredTypes) that have no helper, so every Python class that derives from anything but an old-style class fails with
	/// "Type doesn't have a method with a given name and signature". The patch makes NewTypeMaker.ImplementCTDOverride(MethodInfo) return at
	/// once for a method that is not abstract: the generated type then keeps the default implementation.
	/// Usage: patch-ironpython IN.dll OUT.dll
	/// </summary>
	internal static class Program
	{
		private static int Main(string[] args)
		{
			if (args.Length != 2)
			{
				Console.Error.WriteLine("Usage: patch-ironpython IN.dll OUT.dll");
				return 2;
			}
			// The resolver needs the neighbours (Microsoft.Dynamic, Microsoft.Scripting) to write enum constants.
			DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
			resolver.AddSearchDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(args[0])));
			ModuleDefinition module = ModuleDefinition.ReadModule(args[0], new ReaderParameters { AssemblyResolver = resolver });
			TypeDefinition maker = module.GetTypes().Single(t => t.FullName == "IronPython.Runtime.Types.NewTypeMaker");
			MethodDefinition method = maker.Methods.Single(m => m.Name == "ImplementCTDOverride");
			ILProcessor il = method.Body.GetILProcessor();
			Instruction first = method.Body.Instructions[0];
			MethodReference isAbstract = module.ImportReference(typeof(System.Reflection.MethodBase).GetProperty("IsAbstract").GetGetMethod());
			// if (!m.IsAbstract) return;
			il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
			il.InsertBefore(first, il.Create(OpCodes.Callvirt, isAbstract));
			il.InsertBefore(first, il.Create(OpCodes.Brtrue_S, first));
			il.InsertBefore(first, il.Create(OpCodes.Ret));
			module.Write(args[1]);
			Console.WriteLine("patched " + method.FullName);
			return 0;
		}
	}
}
